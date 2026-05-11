using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;
using UnityEngine.AI;

public class MAEntity : CompCreature, IEntityContext
{
    public CharacterDataDetail CharacterData { get; protected set; }
    public IMoveComp moveComp { get; protected set; }
    public IAtkComp atkComp { get; protected set; }

    public ITargetingComp targetComp { get; protected set; }
    public WeaponComp weaponComp { get; protected set; }

    private CharacterController cController;
    private MoveExecutor _moveExecutor;
    public IMoveExecutor moveExecutor => _moveExecutor;
    public IDurationMoveEffectComp durationMoveEffectComp { get; protected set; }

    /// <summary>
    /// NavMesh Agent Type ID，用于导航和移动约束。
    /// 子类可在 OnShow/SetUpMAComp 之前设置。
    /// </summary>
    public int navAgentTypeID = -1372625422;
    private const float RotationSpeed = 720f; // 度/秒

    private IBuffComp _buffComp;
    public IBuffComp BuffComp => _buffComp;
    private readonly HashSet<string> _invincibleSourceRegistry = new HashSet<string>();

    private Quaternion? _targetRotation = null;
    private Transform _modelTransform = null;
    private bool _maCompInitialized;

    private Vector3 _collisionScaleBase = Vector3.one;
    private float _collisionRadiusBaseWorld;
    private bool _collisionScaleBaseReady;
    private bool _hasAppliedCollisionScale;
    private Fix64 _lastAppliedCollisionRadius;

    public IControlBrain Brain { get; private set; }
    public void SetBrain(IControlBrain brain) => Brain = brain;

    public virtual void ChangeSide(SideType newSide)
    {
        SideType oldSide = Side;
        int oldFactionId = EntitySideHelper.ToFactionId(Side);
        Side = newSide;
        int newFactionId = EntitySideHelper.ToFactionId(newSide);

        if (oldSide != newSide)
        {
            if (GroupMoveManager.HasInstance)
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
        RefreshCharacterData(userData);

        base.OnShow(userData);

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
        durationMoveEffectComp?.StopAllMove();
        _moveExecutor.SetInput(Vector3.zero);
        _moveExecutor.SetExternal(Vector3.zero);
        _moveExecutor.ClearOverride();

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

        // 注意：RegisterAgent 移到子类 OnShow 末尾，确保 Side 等字段已赋值
        EntityRegistry.Register(this);
    }

    protected virtual void RefreshCharacterData(object userData)
    {
        CharacterKey = (userData as EntityParams).GetString(EntityParams.P_CharacterKey);

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
        if (GroupMoveManager.HasInstance)
            GroupMoveManager.Instance.RegisterAgent(this);
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

        // 显式清理 BuffComp，防止将来持有外部订阅时泄漏
        if (_buffComp != null)
        {
            _buffComp.ShutDown();
            _buffComp = null;
        }

        if (GroupMoveManager.HasInstance)
            GroupMoveManager.Instance.UnregisterAgent(this);
        EntityRegistry.Unregister(this);

        _collisionScaleBaseReady = false;
        _hasAppliedCollisionScale = false;
        _collisionRadiusBaseWorld = 0f;
        _collisionScaleBase = Vector3.one;

        base.OnHide(isShutdown, userData);
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        float dt = realElapseSeconds;

        if (CanRun(_buffComp))
            _buffComp.UpdateBuff(dt);

        if (Alive)
            SyncScaleFromCollisionRadius();

        // 更新协调器中的位置（在 Brain.Tick 之前）
        if (GroupMoveManager.HasInstance)
            GroupMoveManager.Instance.UpdateAgentPosition(this);

        if (Brain is ITickBrain tickBrain)
        {
            tickBrain.Tick(this, dt);
        }

        if (CanRun(targetComp))
            targetComp.UpdateTargeting(dt);

        if (CanRun(moveComp))
            moveComp.Move(dt);

        if (CanRun(atkComp))
            atkComp.Attack(dt);

        if (Alive)
        {
            if (CanRun(durationMoveEffectComp))
                durationMoveEffectComp.ApplyEffect(dt);

            moveExecutor.Execute();

            if (animator != null)
            {
                Vector2 brainMove = Vector2.zero;
                if (Brain is AAAGame.Scripts.Entity.PlayerBrain playerBrain)
                    brainMove = playerBrain.Move;

                // 动画由“主动移动意图”驱动，不受击退等被动位移影响。
                bool isMoving = moveComp != null
                    ? moveComp.IsMoving
                    : brainMove.sqrMagnitude > 0.001f;

                animator.SetBool("Moving", isMoving);

                if (moveComp != null && Brain != null)
                {
                    Vector3 moveDirection = moveComp.GetNavDirection();
                    if (moveDirection.sqrMagnitude <= 0.001f && Brain is AAAGame.Scripts.Entity.PlayerBrain playerBrainForDirection)
                    {
                        moveDirection = new Vector3(brainMove.x, 0f, brainMove.y);
                    }

                    if (moveDirection.sqrMagnitude > 0.001f)
                    {
                        _targetRotation = Quaternion.LookRotation(new Vector3(moveDirection.x, 0f, moveDirection.z));
                    }
                    else
                    {
                        // 没在主动移动时，如果有攻击目标 → 朝目标转向
                        // （战斗状态进入攻击范围会停下，原逻辑保留最后移动方向，导致单位不看向敌人）
                        var combatTarget = targetComp?.CurrentTarget;
                        if (combatTarget != null && combatTarget.Alive)
                        {
                            Vector3 toTarget = combatTarget.Position - Position;
                            toTarget.y = 0f;
                            if (toTarget.sqrMagnitude > 0.001f)
                            {
                                _targetRotation = Quaternion.LookRotation(toTarget);
                            }
                        }
                    }
                }
            }
        }
        if (_targetRotation.HasValue && Brain != null && _modelTransform != null)
        {
            Transform rotateTarget = _modelTransform;
            if (_modelTransform.childCount > 0)
            {
                rotateTarget = _modelTransform.GetChild(0);
            }

            rotateTarget.rotation = Quaternion.RotateTowards(
                rotateTarget.rotation,
                _targetRotation.Value,
                RotationSpeed * dt);

            if (Quaternion.Angle(rotateTarget.rotation, _targetRotation.Value) < 0.5f)
            {
                _targetRotation = null;
            }
        }
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
