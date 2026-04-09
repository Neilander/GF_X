using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;

public class MAEntity : CompCreature, IEntityContext
{
    public IMoveComp moveComp { get; protected set; }
    public IAtkComp atkComp { get; protected set; }

    public ITargetingComp targetComp { get; protected set; }
    public WeaponComp weaponComp { get; protected set; }

    private CharacterController cController;
    private MoveExecutor _moveExecutor;
    public IMoveExecutor moveExecutor => _moveExecutor;
    public IDurationMoveEffectComp durationMoveEffectComp { get; protected set; }
    private Animator _animator;
    private bool _wasMoving = false;

    private IBuffComp _buffComp;
    public IBuffComp BuffComp => _buffComp;
    
    private Quaternion? _targetRotation = null;
    private Transform _modelTransform = null;

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

    // Side, Alive, ReferenceId 已在 GeneralCreature 中定义

    IMoveExecutor IEntityContext.MoveExecutor => moveExecutor;
    IMoveComp IEntityContext.MoveComp => moveComp;
    IAtkComp IEntityContext.AtkComp => atkComp;
    ITargetingComp IEntityContext.TargetComp => targetComp;
    IBuffComp IEntityContext.BuffComp => _buffComp;
    WeaponComp IEntityContext.WeaponComp => weaponComp;

    public float GetProperty(CreatureMainProperty prop)
    {
        if (CreaturePropertyManager == null) return 5f;
        return (float)CreaturePropertyManager.GetProperty(prop);
    }

    #endregion

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        SetUpMAComp(userData);

        durationMoveEffectComp = new DurationMoveEffectComp();
        durationMoveEffectComp.Init(this);

        cController = GetComponent<CharacterController>();
        _moveExecutor = gameObject.AddComponent<MoveExecutor>();
        _moveExecutor.Init(cController);
        
        // 像DirectAtkComp一样，直接获取Animator组件
        _animator = GetComponent<Animator>();
        if (_animator == null)
        {
            // 如果没有，再尝试从display获取
            _animator = display.GetComponent<Animator>();
        }
        
        // 找到模型Transform（有Animator的子对象）
        foreach (Transform child in display)
        {
            Animator anim = child.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                _modelTransform = child;
                break;
            }
        }
        
        if (_modelTransform == null)
        {
            _modelTransform = display;
        }
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

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

        // 注意：RegisterAgent 移到子类 OnShow 末尾，确保 Side 等字段已赋值
        EntityRegistry.Register(this);
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
        base.OnHide(isShutdown, userData);
    }

    protected virtual void Update()
    {
        float dt = Time.deltaTime;

        if (CanRun(_buffComp))
            _buffComp.UpdateBuff(dt);

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
                
                // 处理动画和模型朝向
                if (_animator != null)
                {
                    // 获取移动状态
                    bool isMoving = false;
                    Vector2 brainMove = Vector2.zero;
                    
                    if (Brain != null)
                    {
                        // 如果是玩家控制的单位，检查PlayerBrain的移动输入
                        if (Brain is AAAGame.Scripts.Entity.PlayerBrain playerBrain)
                        {
                            brainMove = playerBrain.Move;
                            isMoving = brainMove.sqrMagnitude > 0.001f;
                        }
                        else if (moveComp != null) // AI单位使用moveComp的IsMoving
                        {
                            isMoving = moveComp.IsMoving;
                        }
                    }
                    
                    // 设置 Moving 参数，让 Animator Controller 处理过渡
                    _animator.SetBool("Moving", isMoving);
                    
                    // 设置模型朝向（不管是否在移动，只要有移动方向就转向）
                    if (moveComp != null && Brain != null) // 对所有单位执行旋转
                    {
                        Vector3 moveDirection = moveComp.GetNavDirection();
                        
                        // 如果PlayerMoveComp返回的方向为零，尝试从PlayerBrain获取
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
        
        private const float RotationSpeed = 720f; // 度/秒

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
                    rotateTarget.rotation, _targetRotation.Value, RotationSpeed * Time.deltaTime);

                // 到达目标后清除
                if (Quaternion.Angle(rotateTarget.rotation, _targetRotation.Value) < 0.5f)
                {
                    _targetRotation = null;
                }
            }
        }

    #region Move and Attack

    protected virtual void SetUpMAComp(object userData)
    {
        //获取路径
        var row = GF.DataTable.GetDataTable<CharacterMAFactoryTable>().GetDataRows(r => r.CharacterKey == ReferenceId)[0];
        string moveFacPath = row.MoveFactoryPath;
        string atkFacPath = row.AttackFactoryPath;
        //设置组件
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
