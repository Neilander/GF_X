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
    private const string PlayerInteractionNodeName = "InteractCollider";
    private const float PlayerInteractionRange = 2.7f;
    private const float PlayerInteractionPadding = 0.7f;

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

        _moveExecutor.Init(cController, navAgentTypeID);
        if (moveComp is CharacterMoveComp characterMoveComp)
            characterMoveComp.Init(this, navAgentTypeID);

        InitializeCollisionScaleBase();

        // BuffComp 在 OnShow（而非 OnInit）中创建：每次 Show 重置所有 Buff 状态
        var newBuffComp = new CharacterBuffComp();
        newBuffComp.Init(this);
        _buffComp = newBuffComp;

        // 应用出生自带的 Buff
        if (userData is EntityParams ep)
        {
            if (ep.StartBuffs != null)
            {
                for (int i = 0; i < ep.StartBuffs.Count; i++)
                {
                    BuffData buff = ep.StartBuffs[i];
                    _buffComp.AddBuff(buff, this);
                }
            }
        }

        SyncScaleFromCollisionRadius(true);

        if (Brain is AAAGame.Scripts.Entity.PlayerBrain)
        {
            EnsurePlayerInteractionRuntime();
        }

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
    private void EnsurePlayerInteractionRuntime()
    {
        Transform interactionNode = transform.Find(PlayerInteractionNodeName);
        GameObject interactionObject;

        if (interactionNode == null)
        {
            interactionObject = new GameObject(PlayerInteractionNodeName);
            interactionObject.transform.SetParent(transform);
            interactionObject.transform.localPosition = Vector3.zero;
            interactionObject.transform.localRotation = Quaternion.identity;
            interactionObject.transform.localScale = Vector3.one;
        }
        else
        {
            interactionObject = interactionNode.gameObject;
        }

        SphereCollider triggerSphere = interactionObject.GetComponent<SphereCollider>();
        if (triggerSphere == null)
            triggerSphere = interactionObject.AddComponent<SphereCollider>();
        triggerSphere.isTrigger = true;

        Rigidbody triggerBody = interactionObject.GetComponent<Rigidbody>();
        if (triggerBody == null)
            triggerBody = interactionObject.AddComponent<Rigidbody>();
        triggerBody.isKinematic = true;
        triggerBody.useGravity = false;
        triggerBody.constraints = RigidbodyConstraints.FreezeAll;

        InteractionDetector detector = interactionObject.GetComponent<InteractionDetector>();
        if (detector == null)
            detector = interactionObject.AddComponent<InteractionDetector>();

        InteractionManager manager = interactionObject.GetComponent<InteractionManager>();
        if (manager == null)
            manager = interactionObject.AddComponent<InteractionManager>();

        if (interactionObject.GetComponent<InteractOptionTipsPresenter>() == null)
            interactionObject.AddComponent<InteractOptionTipsPresenter>();

        manager.ConfigureRuntime(
            detector,
            PlayerInteractionRange,
            PlayerInteractionPadding,
            0.65f,
            0.35f,
            0.08f,
            0.1f);
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

    public void OnDead()
    {
        // 触发死亡回调，供Buff系统使用
        if (_buffComp != null)
        {
            _buffComp.OnHostDead();
        }
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
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

    protected virtual void Update()
    {
        float dt = Time.deltaTime;

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
                bool isMoving = false;
                Vector2 brainMove = Vector2.zero;

                if (Brain != null)
                {
                    if (Brain is AAAGame.Scripts.Entity.PlayerBrain playerBrain)
                    {
                        brainMove = playerBrain.Move;
                        isMoving = brainMove.sqrMagnitude > 0.001f;
                    }
                    else if (moveComp != null)
                    {
                        isMoving = moveComp.IsMoving;
                    }
                }

                animator.SetBool("Moving", isMoving);

                if (moveComp != null && Brain != null)
                {
                    Vector3 moveDirection = moveComp.GetNavDirection();
                    if (moveDirection.sqrMagnitude <= 0.001f && Brain is AAAGame.Scripts.Entity.PlayerBrain playerBrain)
                    {
                        moveDirection = new Vector3(brainMove.x, 0f, brainMove.y);
                    }

                    if (moveDirection.sqrMagnitude > 0.001f)
                    {
                        _targetRotation = Quaternion.LookRotation(new Vector3(moveDirection.x, 0f, moveDirection.z));
                    }
                }
            }
        }
    }

    protected virtual void LateUpdate()
    {
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
                RotationSpeed * Time.deltaTime);

            if (Quaternion.Angle(rotateTarget.rotation, _targetRotation.Value) < 0.5f)
            {
                _targetRotation = null;
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
