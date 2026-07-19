using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;

public class MAEntity : CompCreature, IEntityContext, ILogicFrameStableOrder
{
    public LogicEntityId LogicEntityId { get; private set; }
    public long LogicFrameStableKey
    {
        get
        {
            if (!LogicEntityId.IsValid)
                throw new InvalidOperationException("MAEntity.LogicFrameStableKey failed: logic entity id is invalid.");
            return LogicEntityId.Value;
        }
    }
    protected override bool InterpolateRenderRotation => false;

    public CharacterDataDetail CharacterData { get; protected set; }
    public int UnitLevel { get; protected set; } = 1;
    public IMoveComp moveComp { get; protected set; }
    public IAtkComp atkComp { get; protected set; }

    public ITargetingComp targetComp { get; protected set; }
    public WeaponComp weaponComp { get; protected set; }

    private CharacterController cController;
    private MoveExecutor _moveExecutor;
    public IMoveExecutor moveExecutor => _moveExecutor;
    internal MoveExecutor LogicMoveExecutor => _moveExecutor;
    public IDurationMoveEffectComp durationMoveEffectComp { get; protected set; }

    /// <summary>
    /// 导航 movement type ID，用于 FlowField world 和移动约束。
    /// 子类可在 OnShow/SetUpMAComp 之前设置。
    /// </summary>
    public const int UnknownNavAgentTypeId = int.MinValue;
    public int navAgentTypeID = UnknownNavAgentTypeId;
    private const float RotationSpeed = 720f; // 度/秒

    private IBuffComp _buffComp;
    public IBuffComp BuffComp => _buffComp;
    private readonly HashSet<string> _invincibleSourceRegistry = new HashSet<string>();

    private Quaternion? _targetRotation = null;
    private FixVector2 _logicForward = new FixVector2(Fix64.Zero, Fix64.One);
    public FixVector2 LogicForward => _logicForward;
    private Transform _modelTransform = null;
    private AnimationRatePresenter _animationRatePresenter;
    private bool _maCompInitialized;
    private bool _isLogicActive;
    private bool _groupMoveRegistrationRequested;
    private bool _isGroupMoveRegistered;
    private bool _playerRegistrationRequested;
    private Fix64 _combatStateClock;
    private Fix64 _outOfCombatStartTime;
    private bool _coordinatedLogicFrameActive;
    private MAEntityLogicFramePhase _nextCoordinatedLogicFramePhase;
    private Fix64 _coordinatedLogicFrameDeltaTime;
    private long _logicFrameUpdateStartTicks;
    private long _logicFrameUpdateStartAllocatedBytes;

    private Vector3 _collisionScaleBase = Vector3.one;
    private float _collisionRadiusBaseWorld;
    private bool _collisionScaleBaseReady;
    private bool _hasAppliedCollisionScale;
    private Fix64 _lastAppliedCollisionRadius;
    public IControlBrain Brain { get; private set; }
    public bool IsLogicActive => _isLogicActive;
    public void SetBrain(IControlBrain brain) => Brain = brain;
    protected virtual bool UsesFlowNavigationAgent => true;
    protected override bool UsesCoordinatedLogicFrameUpdate => true;
    protected override bool ShouldRunLogicFrameUpdate => _isLogicActive;

    public virtual void ChangeSide(SideType newSide)
    {
        SideType oldSide = Side;
        int oldFactionId = EntitySideHelper.ToFactionId(Side);
        Side = newSide;
        int newFactionId = EntitySideHelper.ToFactionId(newSide);

        if (oldSide != newSide)
        {
            if (_isGroupMoveRegistered)
                GroupMoveManager.Instance.UpdateAgentSide(this);

            if (Brain is IBrainSideChangeHandler sideChangeHandler)
                sideChangeHandler.OnSideChanged(this, oldSide, newSide);
        }

        if (oldFactionId != newFactionId)
        {
            var e = EntityFactionChangedEventArgs.Create(Id, oldFactionId, newFactionId, null);
            GF.Event.Fire(this, e);
        }

        HealthBarComp.ForceUpdateSide(Id, newSide == SideType.PlayerSide);

        var outlines = gameObject.GetComponentsInChildren<AAAGame.Effect.UnitOutline>(true);
        foreach (var outline in outlines)
        {
            if (outline != null)
            {
                outline.ForceRefreshOutline(newSide == SideType.PlayerSide
                    ? AAAGame.Effect.UnitOutline.OutlineType.Friendly
                    : AAAGame.Effect.UnitOutline.OutlineType.Enemy);
            }
        }
    }

    #region IEntityContext 实现

    public Vector3 Position
    {
        get => transform.position;
        set => transform.position = value;
    }

    public Quaternion Rotation
    {
        get => transform.rotation;
        set => transform.rotation = value;
    }

    // Side, Alive, CharacterKey 已在 GeneralCreature 中定义

    IMoveExecutor IEntityContext.MoveExecutor => moveExecutor;
    IMoveComp IEntityContext.MoveComp => moveComp;
    IAtkComp IEntityContext.AtkComp => atkComp;
    ITargetingComp IEntityContext.TargetComp => targetComp;
    IBuffComp IEntityContext.BuffComp => _buffComp;
    WeaponComp IEntityContext.WeaponComp => weaponComp;
    public bool IsOutOfCombat { get; private set; }
    public Fix64 OutOfCombatElapsedLogicTime => IsOutOfCombat
        ? Fix64.Max(Fix64.Zero, _combatStateClock - _outOfCombatStartTime)
        : Fix64.Zero;
    public float OutOfCombatElapsedSeconds => (float)OutOfCombatElapsedLogicTime;

    public Fix64 GetProperty(CreatureMainProperty prop)
    {
        return CreaturePropertyManager.GetProperty(prop);
    }

    #endregion

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        durationMoveEffectComp = new DurationMoveEffectComp();
        durationMoveEffectComp.Init(this);

        cController = GetComponent<CharacterController>();
        _moveExecutor = gameObject.AddComponent<MoveExecutor>();

        _modelTransform = animator != null ? animator.transform : display;
    }

    protected override void OnShow(object userData)
    {
        if (LogicEntityId.IsValid)
            throw new InvalidOperationException($"MAEntity.OnShow failed: pooled entity still has logic id {LogicEntityId.Value}.");
        if (userData is not EntityParams entityParams)
            throw new InvalidOperationException("MAEntity.OnShow failed: userData is not EntityParams.");
        if (!entityParams.LogicEntityId.IsValid)
            throw new InvalidOperationException("MAEntity.OnShow failed: EntityParams.LogicEntityId is invalid.");

        LogicEntityId = entityParams.LogicEntityId;
        LogicEntityLifecycleService.BindView(LogicEntityId, Entity.Id, this);
        RefreshCharacterData(userData);

        base.OnShow(userData);

        InitializeLogicForward();

        if (animator != null)
        {
            if (_animationRatePresenter != null)
                throw new InvalidOperationException("MAEntity.OnShow failed: animation rate presenter is already bound.");
            _animationRatePresenter = new AnimationRatePresenter(animator);
        }

        if (!_maCompInitialized)
        {
            SetUpMAComp(userData);
            _maCompInitialized = true;
        }

        // 自动挂载或更新描边效果
        SideType targetSide = Side;
        if (userData is EntityParams ep)
        {
            targetSide = ep.Side;
        }

        if (targetSide == SideType.PlayerSide || targetSide == SideType.EnemySide)
        {
            var outline = gameObject.GetComponent<AAAGame.Effect.UnitOutline>();
            if (outline == null)
            {
                outline = gameObject.AddComponent<AAAGame.Effect.UnitOutline>();
            }

            outline.outlineType = targetSide == SideType.PlayerSide
                ? AAAGame.Effect.UnitOutline.OutlineType.Friendly
                : AAAGame.Effect.UnitOutline.OutlineType.Enemy;

            outline.enabled = true;
        }
        else
        {
            var outline = gameObject.GetComponent<AAAGame.Effect.UnitOutline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
        }

        _moveExecutor.Init(cController, navAgentTypeID);
        if (moveComp is CharacterMoveComp characterMoveComp)
            characterMoveComp.Init(this, navAgentTypeID);

        // 对象池复用时，清理上一生命周期残留的移动/目标状态，避免出生后被旧状态拉走。
        moveComp?.StopMove();
        if (targetComp != null)
            targetComp.CurrentTarget = null;
        ResetOutOfCombatState();
        durationMoveEffectComp?.StopAllMove();
        _moveExecutor.SetInput(Vector3.zero);
        _moveExecutor.SetExternal(Vector3.zero);
        _moveExecutor.ClearOverride();
        _moveExecutor.SetMovementMode(MovementMode.Normal);

        InitializeCollisionScaleBase();

        // BuffComp 在 OnShow（而非 OnInit）中创建：每次 Show 重置所有 Buff 状态
        var newBuffComp = new CharacterBuffComp();
        newBuffComp.Init(this);
        _buffComp = newBuffComp;

        if (_invincibleSourceRegistry.Count > 0)
            EnsureSharedInvincibleBuff();

        // 应用出生自带的 Buff
        if (userData is EntityParams ep1)
        {
            if (ep1.StartBuffs != null)
            {
                for (int i = 0; i < ep1.StartBuffs.Count; i++)
                {
                    BuffData buff = ep1.StartBuffs[i];
                    _buffComp.AddBuff(buff, this);
                }
            }
        }

        SyncScaleFromCollisionRadius(true);

    }

    protected virtual void RefreshCharacterData(object userData)
    {
        EntityParams entityParams = userData as EntityParams;
        if (entityParams == null)
            throw new InvalidOperationException("MAEntity 初始化失败: userData 不是 EntityParams。");

        CharacterKey = entityParams.GetString(EntityParams.P_CharacterKey);
        UnitLevel = Mathf.Clamp(entityParams.UnitLevel, 1, 3);

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        CharacterData = table.GetDataRow(r => r.CharacterKey == CharacterKey);
        if (CharacterData == null)
            throw new InvalidOperationException($"MAEntity 初始化失败: 未找到 CharacterDataDetail，CharacterKey={CharacterKey}。");

        navAgentTypeID = GameEntry.GetComponent<AgentTypeHelper>().GetNavAgentTypeID(CharacterData.Size);
    }
    /// <summary>
    /// 子类在 OnShow 末尾（Side 等字段赋值完毕后）调用，注册到 GroupMoveManager。
    /// </summary>
    protected void RegisterToGroupMove()
    {
        if (_groupMoveRegistrationRequested)
            throw new InvalidOperationException($"MAEntity.RegisterToGroupMove failed: registration was already requested. entity={LogicEntityId.Value}.");

        _groupMoveRegistrationRequested = UsesFlowNavigationAgent;
    }

    public void RequestPlayerRegistration()
    {
        if (_playerRegistrationRequested)
            throw new InvalidOperationException($"MAEntity.RequestPlayerRegistration failed: player registration was already requested. entity={LogicEntityId.Value}.");

        _playerRegistrationRequested = true;
    }

    public void RequestDespawn()
    {
        LogicEntityLifecycleService.RequestDespawn(this);
    }

    internal void ActivateLogicParticipation(ulong frameId)
    {
        if (_isLogicActive)
            throw new InvalidOperationException($"MAEntity.ActivateLogicParticipation failed: entity {LogicEntityId.Value} is already active.");
        if (frameId != LogicTimeControlService.CurrentFrame)
            throw new InvalidOperationException($"MAEntity.ActivateLogicParticipation failed: frame mismatch. entity={LogicEntityId.Value}, current={LogicTimeControlService.CurrentFrame}, requested={frameId}.");

        if (_playerRegistrationRequested)
            EntityRegistry.RegisterAsPlayer(this);
        else
            EntityRegistry.Register(this);

        if (_groupMoveRegistrationRequested)
        {
            if (!GroupMoveManager.HasInstance)
                throw new InvalidOperationException($"MAEntity.ActivateLogicParticipation failed: GroupMoveManager is unavailable. entity={LogicEntityId.Value}.");

            GroupMoveManager.Instance.RegisterAgent(this);
            _isGroupMoveRegistered = true;
        }

        _isLogicActive = true;
        OnLogicActivated();
    }

    internal void DeactivateLogicParticipation(bool isShutdown = false)
    {
        if (!_isLogicActive)
            return;

        OnLogicDeactivating();
        if (_isGroupMoveRegistered)
        {
            if (!GroupMoveManager.HasInstance)
            {
                if (!isShutdown)
                    throw new InvalidOperationException($"MAEntity.DeactivateLogicParticipation failed: GroupMoveManager is unavailable. entity={LogicEntityId.Value}.");
            }
            else
            {
                GroupMoveManager.Instance.UnregisterAgent(this);
            }
            _isGroupMoveRegistered = false;
        }

        EntityRegistry.Unregister(this);
        _isLogicActive = false;
    }

    protected virtual void OnLogicActivated()
    {
    }

    protected virtual void OnLogicDeactivating()
    {
    }



    public void OnKill(MAEntity target)
    {
        // 触发击杀回调，供Buff系统使用
        if (_buffComp != null)
        {
            _buffComp.OnKill(target);
        }
    }

    public virtual void OnDead()
    {
        _invincibleSourceRegistry.Clear();

        // 触发死亡回调，供Buff系统使用
        if (_buffComp != null)
        {
            _buffComp.OnHostDead();
        }

        PlayDeathSound();
    }

    protected override void RemoveAfterDeath()
    {
        RequestDespawn();
    }

    /// <summary>
    /// 死亡音效。默认：敌方死亡播 "enemyDeath"。子类可覆盖（建筑覆盖为 "buildDeath"，无视阵营）。
    /// </summary>
    protected virtual void PlayDeathSound()
    {
        if (Side == SideType.EnemySide && AudioManager.Instance != null)
        {
            AudioManager.Instance.Play("enemyDeath");
        }
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        _invincibleSourceRegistry.Clear();

        if (_animationRatePresenter != null)
        {
            _animationRatePresenter.Dispose();
            _animationRatePresenter = null;
        }

        // 显式清理 BuffComp，防止将来持有外部订阅时泄漏
        if (_buffComp != null)
        {
            _buffComp.ShutDown();
            _buffComp = null;
        }

        DeactivateLogicParticipation();
        LogicEntityLifecycleService.UnbindView(LogicEntityId, Id);
        LogicEntityId = default;

        _collisionScaleBaseReady = false;
        _hasAppliedCollisionScale = false;
        _collisionRadiusBaseWorld = 0f;
        _collisionScaleBase = Vector3.one;
        _maCompInitialized = false;
        _groupMoveRegistrationRequested = false;
        _isGroupMoveRegistered = false;
        _playerRegistrationRequested = false;

        base.OnHide(isShutdown, userData);
    }

    protected override void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        BeginLogicFramePhases(deltaTime);
        for (int phaseValue = 0; phaseValue < (int)MAEntityLogicFramePhase.Count; phaseValue++)
            ExecuteLogicFramePhase((MAEntityLogicFramePhase)phaseValue, deltaTime);
        CompleteLogicFramePhases(deltaTime);
    }

    internal void BeginCoordinatedLogicFrame(Fix64 deltaTime)
    {
        BeginCoordinatedLogicFrameUpdate();
        BeginLogicFramePhases(deltaTime);
    }

    internal void ExecuteCoordinatedLogicFramePhase(MAEntityLogicFramePhase phase, Fix64 deltaTime)
    {
        ExecuteLogicFramePhase(phase, deltaTime);
    }

    internal void CompleteCoordinatedLogicFrame(Fix64 deltaTime)
    {
        CompleteLogicFramePhases(deltaTime);
        CompleteCoordinatedLogicFrameUpdate();
    }

    private void BeginLogicFramePhases(Fix64 deltaTime)
    {
        if (_coordinatedLogicFrameActive)
            throw new InvalidOperationException($"MAEntity.BeginLogicFramePhases failed: entity {LogicEntityId.Value} already has an active frame.");
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException($"MAEntity.BeginLogicFramePhases failed: entity {LogicEntityId.Value} received a non-fixed delta.");

        _coordinatedLogicFrameActive = true;
        _nextCoordinatedLogicFramePhase = MAEntityLogicFramePhase.BaseAndBuffs;
        _coordinatedLogicFrameDeltaTime = deltaTime;
        _logicFrameUpdateStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _logicFrameUpdateStartAllocatedBytes = System.GC.GetAllocatedBytesForCurrentThread();
    }

    private void ExecuteLogicFramePhase(MAEntityLogicFramePhase phase, Fix64 deltaTime)
    {
        if (!_coordinatedLogicFrameActive)
            throw new InvalidOperationException($"MAEntity.ExecuteLogicFramePhase failed: entity {LogicEntityId.Value} has no active frame.");
        if (deltaTime != _coordinatedLogicFrameDeltaTime)
            throw new InvalidOperationException($"MAEntity.ExecuteLogicFramePhase failed: entity {LogicEntityId.Value} delta changed within the frame.");
        if (phase != _nextCoordinatedLogicFramePhase)
        {
            throw new InvalidOperationException(
                $"MAEntity.ExecuteLogicFramePhase failed: entity {LogicEntityId.Value} phase mismatch. expected={_nextCoordinatedLogicFramePhase}, actual={phase}.");
        }

        switch (phase)
        {
            case MAEntityLogicFramePhase.BaseAndBuffs:
                ExecuteBaseAndBuffPhase(deltaTime);
                break;
            case MAEntityLogicFramePhase.NavigationSync:
                ExecuteNavigationSyncPhase();
                break;
            case MAEntityLogicFramePhase.Brain:
                ExecuteBrainPhase(deltaTime);
                break;
            case MAEntityLogicFramePhase.Targeting:
                ExecuteTargetingPhase(deltaTime);
                break;
            case MAEntityLogicFramePhase.Projectile:
                break;
            case MAEntityLogicFramePhase.Attack:
                ExecuteAttackPhase(deltaTime);
                break;
            case MAEntityLogicFramePhase.DamageResolve:
                break;
            case MAEntityLogicFramePhase.MoveIntent:
                ExecuteMoveIntentPhase(deltaTime);
                break;
            case MAEntityLogicFramePhase.MoveResolve:
                ExecuteMoveResolvePhase(deltaTime);
                break;
            case MAEntityLogicFramePhase.MoveCommit:
                ExecuteMoveCommitPhase(deltaTime);
                break;
            case MAEntityLogicFramePhase.PostUpdate:
                OnPostLogicFrameUpdate(deltaTime);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown MA logic frame phase.");
        }

        _nextCoordinatedLogicFramePhase = (MAEntityLogicFramePhase)((int)phase + 1);
    }

    private void CompleteLogicFramePhases(Fix64 deltaTime)
    {
        if (!_coordinatedLogicFrameActive)
            throw new InvalidOperationException($"MAEntity.CompleteLogicFramePhases failed: entity {LogicEntityId.Value} has no active frame.");
        if (deltaTime != _coordinatedLogicFrameDeltaTime)
            throw new InvalidOperationException($"MAEntity.CompleteLogicFramePhases failed: entity {LogicEntityId.Value} delta changed within the frame.");
        if (_nextCoordinatedLogicFramePhase != MAEntityLogicFramePhase.Count)
        {
            throw new InvalidOperationException(
                $"MAEntity.CompleteLogicFramePhases failed: entity {LogicEntityId.Value} stopped at phase {_nextCoordinatedLogicFramePhase}.");
        }

        UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
            UnityGameFramework.Runtime.MainThreadPerfScope.EntityUpdate,
            System.Diagnostics.Stopwatch.GetTimestamp() - _logicFrameUpdateStartTicks,
            System.Math.Max(0L, System.GC.GetAllocatedBytesForCurrentThread() - _logicFrameUpdateStartAllocatedBytes));
        _coordinatedLogicFrameActive = false;
    }

    private void ExecuteBaseAndBuffPhase(Fix64 deltaTime)
    {
        long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        base.OnLogicFrameUpdate(deltaTime);
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityBase, stageStartTicks);
        _combatStateClock += deltaTime;
        RefreshOutOfCombatState();
        OnOutOfCombatStateRefreshed();

        if (CanRun(_buffComp))
        {
            stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _buffComp.UpdateBuff(deltaTime);
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityBuff, stageStartTicks);
        }

        if (Alive)
        {
            stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            SyncScaleFromCollisionRadius();
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityScale, stageStartTicks);
        }
    }

    private void ExecuteNavigationSyncPhase()
    {
        if (!UsesFlowNavigationAgent || !GroupMoveManager.HasInstance)
            return;

        long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        GroupMoveManager.Instance.UpdateAgentPosition(this);
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityAgentPosition, stageStartTicks);
    }

    private void ExecuteBrainPhase(Fix64 deltaTime)
    {
        if (!(Brain is ITickBrain tickBrain))
            return;

        long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        tickBrain.Tick(this, deltaTime);
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityBrain, stageStartTicks);
    }

    private void ExecuteTargetingPhase(Fix64 deltaTime)
    {
        if (CanRun(targetComp))
        {
            long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            targetComp.UpdateTargeting(deltaTime);
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityTargeting, stageStartTicks);
        }

        RefreshOutOfCombatState();
        OnOutOfCombatStateRefreshed();
    }

    private void ExecuteAttackPhase(Fix64 deltaTime)
    {
        if (CanRun(atkComp))
        {
            long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            atkComp.Attack(deltaTime);
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityAttack, stageStartTicks);
        }

        RefreshOutOfCombatState();
        OnOutOfCombatStateRefreshed();
    }

    private void ExecuteMoveIntentPhase(Fix64 deltaTime)
    {
        if (!Alive)
            return;

        if (CanRun(durationMoveEffectComp))
        {
            long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            durationMoveEffectComp.ApplyEffect(deltaTime);
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityDurationMove, stageStartTicks);
        }

        if (CanRun(moveComp))
        {
            long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            moveComp.Move(deltaTime);
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityMoveComp, stageStartTicks);
        }
    }

    private void ExecuteMoveCommitPhase(Fix64 deltaTime)
    {
        FixVector2 resolvedPosition = LogicAgentCollisionShadowService.GetRequiredResolvedPosition(
            LogicEntityId,
            LogicFrameRuntime.CurrentFrame);
        long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _moveExecutor.CommitPreparedLogicFrame(resolvedPosition);
        UpdateLogicForward(resolvedPosition);
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityMoveExecutor, stageStartTicks);

        if (Alive && UsesFlowNavigationAgent && GroupMoveManager.HasInstance)
        {
            stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            GroupMoveManager.Instance.UpdateAgentPosition(this);
            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityAgentPosition, stageStartTicks);
        }
    }

    private void ExecuteMoveResolvePhase(Fix64 deltaTime)
    {
        LogicEntityFrameState frameState = LogicEntityFrameSnapshotService.GetRequiredCurrent(this);
        long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _moveExecutor.PrepareLogicFrame(deltaTime, frameState, Alive);
        RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityMoveExecutor, stageStartTicks);
    }

    protected virtual void OnPostLogicFrameUpdate(Fix64 deltaTime)
    {
    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);

        long stageStartTicks;
        if (animator != null)
        {
            if (_animationRatePresenter == null)
                throw new InvalidOperationException("MAEntity.OnRenderFrameUpdate failed: animator is not bound to AnimationRatePresenter.");
            _animationRatePresenter.ApplyCurrentScale();
        }

        if (Alive && animator != null)
        {
            stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            Vector2 brainMove = Vector2.zero;
            if (Brain is AAAGame.Scripts.Entity.PlayerBrain playerBrain)
                brainMove = playerBrain.Move;

            bool isMoving = moveComp != null
                ? moveComp.IsMoving
                : brainMove.sqrMagnitude > 0.001f;

            animator.SetBool("Moving", isMoving);

            if (Brain != null)
                _targetRotation = Quaternion.LookRotation(new Vector3((float)_logicForward.x, 0f, (float)_logicForward.y));

            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityAnimator, stageStartTicks);
        }

        if (_targetRotation.HasValue && Brain != null && _modelTransform != null)
        {
            stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            Transform rotateTarget = _modelTransform.childCount > 0 ? _modelTransform.GetChild(0) : _modelTransform;
            rotateTarget.rotation = Quaternion.RotateTowards(
                rotateTarget.rotation,
                _targetRotation.Value,
                RotationSpeed * realElapseSeconds);

            if (Quaternion.Angle(rotateTarget.rotation, _targetRotation.Value) < 0.5f)
                _targetRotation = null;

            RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope.EntityRotation, stageStartTicks);
        }
    }

    private static void RecordPerf(UnityGameFramework.Runtime.MainThreadPerfScope scope, long startTicks)
    {
        UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
            scope,
            System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
    }

    private void InitializeLogicForward()
    {
        Vector3 worldForward = Rotation * Vector3.forward;
        SetLogicForward(new FixVector2((Fix64)worldForward.x, (Fix64)worldForward.z), "OnShow");
    }

    private void UpdateLogicForward(FixVector2 resolvedPosition)
    {
        LogicEntityFrameState selfState = LogicEntityFrameSnapshotService.GetRequiredCurrent(this);
        IEntityContext target = targetComp?.CurrentTarget;
        if (atkComp != null && atkComp.IsAttacking && target != null && target.Alive)
        {
            FixVector2 targetPosition = LogicEntityFrameSnapshotService.GetRequiredPosition(target);
            FixVector2 toTarget = targetPosition - selfState.Position;
            if (FixVector2.SqrMagnitude(toTarget) > Fix64.Zero)
            {
                SetLogicForward(toTarget, "attack target");
                return;
            }
        }

        FixVector2 displacement = resolvedPosition - selfState.Position;
        if (FixVector2.SqrMagnitude(displacement) > Fix64.Zero)
        {
            SetLogicForward(displacement, "resolved movement");
            return;
        }

        if (target != null && target.Alive)
        {
            FixVector2 targetPosition = LogicEntityFrameSnapshotService.GetRequiredPosition(target);
            FixVector2 toTarget = targetPosition - selfState.Position;
            if (FixVector2.SqrMagnitude(toTarget) > Fix64.Zero)
                SetLogicForward(toTarget, "idle target");
        }
    }

    private void SetLogicForward(FixVector2 direction, string source)
    {
        FixVector2 normalized = direction.GetNormalized();
        if (FixVector2.SqrMagnitude(normalized) == Fix64.Zero)
            throw new InvalidOperationException($"MAEntity.SetLogicForward failed: zero direction. entity={LogicEntityId.Value}, source={source}.");
        _logicForward = normalized;
    }

    public override void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (damage <= Fix64.Zero || !Alive || CreaturePropertyManager == null)
            return;

        Fix64 before = HealthValue;
        base.TakeDamage(damage, modType, attacker);

        if (HealthValue < before)
            NotifyDamageTakenForOutOfCombat();
    }

    protected void NotifyDamageTakenForOutOfCombat()
    {
        _outOfCombatStartTime = _combatStateClock;
    }

    private void ResetOutOfCombatState()
    {
        _combatStateClock = Fix64.Zero;
        _outOfCombatStartTime = Fix64.Zero;
        IsOutOfCombat = false;
        RefreshOutOfCombatState();
    }

    private void RefreshOutOfCombatState()
    {
        if (!Alive)
        {
            ExitOutOfCombat();
            return;
        }

        bool hasAttackTarget = HasAttackTarget();
        if (hasAttackTarget)
            ExitOutOfCombat();
        else
            EnterOutOfCombat();
    }

    protected virtual void OnOutOfCombatStateRefreshed()
    {
    }

    private bool HasAttackTarget()
    {
        IEntityContext currentTarget = targetComp?.CurrentTarget;
        if (currentTarget != null && currentTarget.IsAttackTargetable())
            return true;

        return atkComp != null && atkComp.IsAttacking;
    }

    private void EnterOutOfCombat()
    {
        if (IsOutOfCombat)
            return;

        IsOutOfCombat = true;
        _outOfCombatStartTime = _combatStateClock;
    }

    private void ExitOutOfCombat()
    {
        if (!IsOutOfCombat)
            return;

        IsOutOfCombat = false;
        _outOfCombatStartTime = _combatStateClock;
    }

    public bool RegisterInvincibleSource(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
            return false;

        if (!_invincibleSourceRegistry.Add(sourceId))
            return false;

        EnsureSharedInvincibleBuff();
        return true;
    }

    public bool UnregisterInvincibleSource(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
            return false;

        if (!_invincibleSourceRegistry.Remove(sourceId))
            return false;

        if (_invincibleSourceRegistry.Count == 0)
            _buffComp?.RemoveBuff(InvincibleStateBuff.BuffId);

        return true;
    }

    private void EnsureSharedInvincibleBuff()
    {
        if (_buffComp == null || _buffComp.HasBuff(InvincibleStateBuff.BuffId))
            return;

        var buffData = BuffData.Create(
            id: InvincibleStateBuff.BuffId,
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { new InvincibleStateBuff() });

        _buffComp.AddBuff(buffData, this);
    }

    private void InitializeCollisionScaleBase()
    {
        _collisionScaleBase = transform.localScale;
        _collisionRadiusBaseWorld = ResolveCurrentCollisionRadiusWorld();
        _collisionScaleBaseReady = _collisionRadiusBaseWorld > 0.0001f;
    }

    private float ResolveCurrentCollisionRadiusWorld()
    {
        if (cController == null)
            return 0f;

        float scaleXZ = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        return cController.radius * scaleXZ;
    }

    private void SyncScaleFromCollisionRadius(bool force = false)
    {
        if (CreaturePropertyManager == null)
            return;

        Fix64 collisionRadius = CreaturePropertyManager.GetProperty(CreatureMainProperty.CollisionRadius);
        if (collisionRadius <= Fix64.Zero)
            return;

        if (!force && _hasAppliedCollisionScale && collisionRadius == _lastAppliedCollisionRadius)
            return;

        if (!_collisionScaleBaseReady)
            InitializeCollisionScaleBase();

        if (!_collisionScaleBaseReady)
            return;

        float targetWorldRadius = DistanceUnitConverter.ConvertToWorldFloat(collisionRadius);
        if (targetWorldRadius <= 0.0001f)
            return;

        float scaleRatio = targetWorldRadius / _collisionRadiusBaseWorld;
        if (scaleRatio <= 0.0001f)
            return;

        transform.localScale = _collisionScaleBase * scaleRatio;
        _hasAppliedCollisionScale = true;
        _lastAppliedCollisionRadius = collisionRadius;
    }

    #region Move and Attack

    protected virtual void SetUpMAComp(object userData)
    {
        string moveFacPath = "CharacterMoveFactory";
        string atkFacPath = "CharacterAtkFactory";

        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath), this);
        FactoryHelper.CreateAtkComp(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath), this);
    }

    public void SetMoveComp(IMoveComp newMoveComp) => moveComp = newMoveComp;
    public void SetAtkComp(IAtkComp newAtkComp) => atkComp = newAtkComp;

    public void SetTargetingComp(ITargetingComp newTargetingComp) => targetComp = newTargetingComp;
    public void SetBuffComp(IBuffComp newBuffComp) => _buffComp = newBuffComp;
    public void SetWeaponComp(WeaponComp newWeaponComp) => weaponComp = newWeaponComp;

    #endregion
}
