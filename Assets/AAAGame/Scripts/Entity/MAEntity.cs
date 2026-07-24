using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;

public class MAEntity : CompCreature, IEntityContext
{
    public LogicEntityId LogicEntityId { get; private set; }
    private LogicEntityState _logicState;
    private bool _isViewBound;
    public LogicEntityState LogicState => _logicState;
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
    /// View 在 OnShow 时从 LogicEntityState 读取该值。
    /// </summary>
    public const int UnknownNavAgentTypeId = int.MinValue;
    public int navAgentTypeID = UnknownNavAgentTypeId;
    private const float RotationSpeed = 720f; // 度/秒

    private IBuffComp _buffComp;
    public IBuffComp BuffComp => _buffComp;
    private readonly HashSet<string> _invincibleSourceRegistry = new HashSet<string>();

    private Quaternion? _targetRotation = null;
    public FixVector2 LogicForward => RequireLogicState().Forward;
    private Transform _modelTransform = null;
    private AnimationRatePresenter _animationRatePresenter;
    private bool _isLogicActive;

    private Vector3 _collisionScaleBase = Vector3.one;
    private float _collisionRadiusBaseWorld;
    private bool _collisionScaleBaseReady;
    private bool _hasAppliedCollisionScale;
    private Fix64 _lastAppliedCollisionRadius;
    public IControlBrain Brain { get; private set; }
    public bool IsLogicActive => _isLogicActive;
    public void SetBrain(IControlBrain brain) => Brain = brain;
    protected override bool InitializeDefaultTauntLevelOnShow => false;
    protected override bool UsesCoordinatedLogicFrameUpdate => true;
    protected override bool ShouldRunLogicFrameUpdate => false;

    public virtual void ChangeSide(SideType newSide)
    {
        int oldFactionId = EntitySideHelper.ToFactionId(Side);
        Side = newSide;
        int newFactionId = EntitySideHelper.ToFactionId(newSide);

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

    public FixVector2 PositionFixed
    {
        get => RequireLogicState().Position;
        set => throw new InvalidOperationException(
            "MAEntity.PositionFixed setter rejected: View cannot mutate LogicEntityState position.");
    }

    public FixVector2 ForwardFixed => RequireLogicState().Forward;

    public virtual LogicCombatShape CombatShape => LogicCombatShape.Circle(
        PositionFixed,
        DistanceUnitConverter.ConvertToWorld(GetProperty(CreatureMainProperty.CollisionRadius)));

    public Vector3 Position
    {
        get
        {
            FixVector2 fixedPosition = PositionFixed;
            return new Vector3((float)fixedPosition.x, transform.position.y, (float)fixedPosition.y);
        }
        set => throw new InvalidOperationException(
            "MAEntity.Position setter rejected: View cannot mutate LogicEntityState position.");
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
    IDurationMoveEffectComp IEntityContext.DurationMoveEffectComp => durationMoveEffectComp;
    public CreaturePropertyManager CreatureProperties => CreaturePropertyManager;
    public bool IsOutOfCombat => RequireLogicState().IsOutOfCombat;
    public Fix64 OutOfCombatElapsedLogicTime => RequireLogicState().OutOfCombatElapsedLogicTime;
    public float OutOfCombatElapsedSeconds => RequireLogicState().OutOfCombatElapsedSeconds;
    public override int TauntLevel
    {
        get => _logicState != null ? _logicState.TauntLevel : base.TauntLevel;
        set
        {
            if (_logicState != null)
                throw new InvalidOperationException("MAEntity view cannot change TauntLevel; update LogicEntityState instead.");
            base.TauntLevel = value;
        }
    }

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
        if (_isViewBound)
            throw new InvalidOperationException("MAEntity.OnShow failed: pooled entity still reports a bound view.");
        if (userData is not EntityParams entityParams)
            throw new InvalidOperationException("MAEntity.OnShow failed: userData is not EntityParams.");
        if (!entityParams.LogicEntityId.IsValid)
            throw new InvalidOperationException("MAEntity.OnShow failed: EntityParams.LogicEntityId is invalid.");
        if (entityParams.LogicEntityState == null)
            throw new InvalidOperationException("MAEntity.OnShow failed: EntityParams.LogicEntityState is null.");

        LogicEntityId = entityParams.LogicEntityId;
        _logicState = LogicEntityStateStore.GetRequired(LogicEntityId);
        if (!ReferenceEquals(_logicState, entityParams.LogicEntityState))
            throw new InvalidOperationException($"MAEntity.OnShow failed: logic state identity mismatch. entity={LogicEntityId.Value}.");
        RefreshCharacterData(userData);
        BindLogicStateComponents();

        base.OnShow(userData);
        SubscribeLogicStatePresentation();
        Alive = _logicState.Alive;
        InitializePresentationPoseFromLogicState();

        if (animator != null)
        {
            if (_animationRatePresenter != null)
                throw new InvalidOperationException("MAEntity.OnShow failed: animation rate presenter is already bound.");
            _animationRatePresenter = new AnimationRatePresenter(animator);
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

        ResetPresentationMovementForShow();

        InitializeCollisionScaleBase();

        SyncScaleFromCollisionRadius(true);

        LogicEntityLifecycleService.BindView(LogicEntityId, Entity.Id, this);
        _isViewBound = true;

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

    protected override CreaturePropertyManager CreateCreaturePropertyManager()
    {
        return RequireLogicState().CreatureProperties
               ?? throw new InvalidOperationException($"MAEntity.CreateCreaturePropertyManager failed: logic properties are missing. entity={LogicEntityId.Value}.");
    }

    private void BindLogicStateComponents()
    {
        LogicEntityState state = RequireLogicState();
        if (!state.IsConfigured)
            throw new InvalidOperationException($"MAEntity.BindLogicStateComponents failed: logic state is not configured. entity={LogicEntityId.Value}.");

        Brain = state.Brain;
        moveComp = state.MoveComp ?? throw new InvalidOperationException($"MAEntity.BindLogicStateComponents failed: MoveComp is missing. entity={LogicEntityId.Value}.");
        atkComp = state.AtkComp ?? throw new InvalidOperationException($"MAEntity.BindLogicStateComponents failed: AtkComp is missing. entity={LogicEntityId.Value}.");
        targetComp = state.TargetComp ?? throw new InvalidOperationException($"MAEntity.BindLogicStateComponents failed: TargetComp is missing. entity={LogicEntityId.Value}.");
        weaponComp = state.WeaponComp ?? throw new InvalidOperationException($"MAEntity.BindLogicStateComponents failed: WeaponComp is missing. entity={LogicEntityId.Value}.");
        durationMoveEffectComp = state.DurationMoveEffectComp ?? throw new InvalidOperationException($"MAEntity.BindLogicStateComponents failed: DurationMoveEffectComp is missing. entity={LogicEntityId.Value}.");
        _buffComp = state.BuffComp ?? throw new InvalidOperationException($"MAEntity.BindLogicStateComponents failed: BuffComp is missing. entity={LogicEntityId.Value}.");
    }

    private void ResetPresentationMovementForShow()
    {
        if (_moveExecutor == null)
            throw new InvalidOperationException("MAEntity.ResetPresentationMovementForShow failed: MoveExecutor is missing.");

        _moveExecutor.SetInput(Vector3.zero);
        _moveExecutor.SetExternal(Vector3.zero);
        _moveExecutor.ClearOverride();
        _moveExecutor.SetMovementMode(MovementMode.Normal);
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

        if (!string.Equals(_logicState.CharacterKey, CharacterKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MAEntity.ActivateLogicParticipation failed: character key mismatch. entity={LogicEntityId.Value}, state={_logicState.CharacterKey}, view={CharacterKey}.");
        }

        _isLogicActive = true;
        OnLogicActivated();
    }

    internal void DeactivateLogicParticipation(bool isShutdown = false)
    {
        if (!_isLogicActive)
            return;

        OnLogicDeactivating();
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
        UnsubscribeLogicStatePresentation();
        _invincibleSourceRegistry.Clear();

        if (_animationRatePresenter != null)
        {
            _animationRatePresenter.Dispose();
            _animationRatePresenter = null;
        }

        _buffComp = null;

        DeactivateLogicParticipation();
        if (_isViewBound)
        {
            LogicEntityLifecycleService.UnbindView(LogicEntityId, Id);
            _isViewBound = false;
        }
        LogicEntityId = default;
        _logicState = null;

        _collisionScaleBaseReady = false;
        _hasAppliedCollisionScale = false;
        _collisionRadiusBaseWorld = 0f;
        _collisionScaleBase = Vector3.one;
        base.OnHide(isShutdown, userData);
    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
        SyncPresenterPoseFromLogicState();

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
            {
                FixVector2 logicForward = LogicForward;
                _targetRotation = Quaternion.LookRotation(new Vector3((float)logicForward.x, 0f, (float)logicForward.y));
            }

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

    private void InitializePresentationPoseFromLogicState()
    {
        LogicEntityState state = RequireLogicState();
        Vector3 currentPosition = transform.position;
        transform.position = new Vector3((float)state.Position.x, currentPosition.y, (float)state.Position.y);

        FixVector2 forward = state.Forward;
        if (FixVector2.SqrMagnitude(forward) == Fix64.Zero)
            throw new InvalidOperationException($"MAEntity.InitializePresentationPoseFromLogicState failed: entity {LogicEntityId.Value} has zero forward.");
        transform.rotation = Quaternion.LookRotation(new Vector3((float)forward.x, 0f, (float)forward.y));
    }

    private void SyncPresenterPoseFromLogicState()
    {
        if (_logicState == null || !_logicState.IsSpawnCommitted)
            return;

        FixVector2 position = _logicState.Position;
        Vector3 currentPosition = transform.position;
        transform.position = new Vector3((float)position.x, currentPosition.y, (float)position.y);

        FixVector2 forward = _logicState.Forward;
        if (FixVector2.SqrMagnitude(forward) > Fix64.Zero)
            transform.rotation = Quaternion.LookRotation(new Vector3((float)forward.x, 0f, (float)forward.y));
    }

    private LogicEntityState RequireLogicState()
    {
        if (_logicState == null)
            throw new InvalidOperationException("MAEntity logic state is not bound.");
        return _logicState;
    }

    public override void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        RequireLogicState().TakeDamage(damage, modType, attacker);
    }

    public override void Heal(Fix64 amount)
    {
        RequireLogicState().Heal(amount);
    }

    public bool RegisterInvincibleSource(string sourceId)
    {
        return RequireLogicState().RegisterInvincibleSource(sourceId);
    }

    public bool UnregisterInvincibleSource(string sourceId)
    {
        return RequireLogicState().UnregisterInvincibleSource(sourceId);
    }

    private void SubscribeLogicStatePresentation()
    {
        LogicEntityState state = RequireLogicState();
        state.HealthChanged += OnLogicHealthChanged;
        state.UnitDied += OnLogicUnitDied;
        state.BuildingDisabledChanged += OnLogicBuildingDisabledChanged;
        state.GhostStateChanged += OnLogicGhostStateChanged;
        state.CollisionBlockingChanged += OnLogicCollisionBlockingChanged;
        state.PermanentStealthChanged += OnLogicPermanentStealthChanged;
        state.PhaseProtectionChanged += OnLogicPhaseProtectionChanged;
        if (state.AtkComp is DirectAtkComp directAttack)
        {
            directAttack.AttackPresentationStarted += OnAttackPresentationStarted;
            directAttack.AttackPresentationInterrupted += OnAttackPresentationInterrupted;
        }
    }

    private void UnsubscribeLogicStatePresentation()
    {
        if (_logicState == null)
            return;
        _logicState.HealthChanged -= OnLogicHealthChanged;
        _logicState.UnitDied -= OnLogicUnitDied;
        _logicState.BuildingDisabledChanged -= OnLogicBuildingDisabledChanged;
        _logicState.GhostStateChanged -= OnLogicGhostStateChanged;
        _logicState.CollisionBlockingChanged -= OnLogicCollisionBlockingChanged;
        _logicState.PermanentStealthChanged -= OnLogicPermanentStealthChanged;
        _logicState.PhaseProtectionChanged -= OnLogicPhaseProtectionChanged;
        if (_logicState.AtkComp is DirectAtkComp directAttack)
        {
            directAttack.AttackPresentationStarted -= OnAttackPresentationStarted;
            directAttack.AttackPresentationInterrupted -= OnAttackPresentationInterrupted;
        }
    }

    private void OnLogicHealthChanged(LogicEntityHealthChange change)
    {
        Alive = RequireLogicState().Alive;
        if (change.Delta < Fix64.Zero)
            TriggerPresenterHitAnimation();

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(
            Id,
            (float)change.Current,
            (float)change.Max,
            (float)change.Delta));
        if (change.Delta > Fix64.Zero)
            GF.Event.Fire(this, CreatureHealedEventArgs.Create(Id, (float)change.Delta));

        OnLogicHealthChangedPresentation(change);
    }

    private void OnLogicUnitDied(IEntityContext attacker)
    {
        Alive = false;
        if (this is SoldierEntity victim)
            GF.Event.Fire(victim, SoldierDeadEventArgs.Create(victim));
        PlayDeathSound();
        OnLogicUnitDiedPresentation(attacker);
    }

    private void OnLogicBuildingDisabledChanged(bool disabled, IEntityContext attacker)
    {
        Alive = RequireLogicState().Alive;
        OnLogicBuildingDisabledPresentation(disabled, attacker);
    }

    private void OnLogicGhostStateChanged(bool enabled)
    {
        Alive = RequireLogicState().Alive;
        OnLogicGhostStatePresentation(enabled);
    }

    private void OnLogicCollisionBlockingChanged(bool enabled) => OnLogicCollisionBlockingPresentation(enabled);
    private void OnLogicPermanentStealthChanged(bool enabled) => OnLogicPermanentStealthPresentation(enabled);
    private void OnLogicPhaseProtectionChanged(bool enabled) => OnLogicPhaseProtectionPresentation(enabled);

    private void OnAttackPresentationStarted(Fix64 windUp, bool playTrail)
    {
        if (animator != null)
            animator.SetTrigger("Attack");
        if (playTrail)
            WeaponAttackTrailEffect.Play(this, Mathf.Max(0.08f, (float)windUp + 0.08f));
    }

    private void OnAttackPresentationInterrupted()
    {
        WeaponAttackTrailEffect.Stop(this, true);
        if (animator == null)
            return;
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Trigger && parameters[i].name == "Attack")
            {
                animator.ResetTrigger("Attack");
                return;
            }
        }
    }

    protected virtual void OnLogicHealthChangedPresentation(LogicEntityHealthChange change)
    {
    }

    protected virtual void OnLogicUnitDiedPresentation(IEntityContext attacker)
    {
    }

    protected virtual void OnLogicBuildingDisabledPresentation(bool disabled, IEntityContext attacker)
    {
    }

    protected virtual void OnLogicGhostStatePresentation(bool enabled)
    {
    }

    protected virtual void OnLogicCollisionBlockingPresentation(bool enabled)
    {
    }

    protected virtual void OnLogicPermanentStealthPresentation(bool enabled)
    {
    }

    protected virtual void OnLogicPhaseProtectionPresentation(bool enabled)
    {
    }

    private void TriggerPresenterHitAnimation()
    {
        if (animator == null)
            return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == "GetHit" && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger("GetHit");
                return;
            }
        }
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

    public void SetMoveComp(IMoveComp newMoveComp)
    {
        throw new InvalidOperationException("MAEntity view cannot own or replace MoveComp; configure LogicEntityState instead.");
    }

    public void SetAtkComp(IAtkComp newAtkComp)
    {
        throw new InvalidOperationException("MAEntity view cannot own or replace AtkComp; configure LogicEntityState instead.");
    }

    public void SetTargetingComp(ITargetingComp newTargetingComp)
    {
        throw new InvalidOperationException("MAEntity view cannot own or replace TargetingComp; configure LogicEntityState instead.");
    }

    public void SetWeaponComp(WeaponComp newWeaponComp)
    {
        throw new InvalidOperationException("MAEntity view cannot own or replace WeaponComp; configure LogicEntityState instead.");
    }

    #endregion
}
