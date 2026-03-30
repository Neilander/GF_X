using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;

public class MAEntity : CompCreature, IEntityContext
{
    public IMoveComp moveComp { get; protected set; }
    public IAtkComp atkComp { get; protected set; }

    public ITargetingComp targetComp { get; protected set; }
    public IBuffComp buffComp { get; protected set; }
    public WeaponComp weaponComp { get; protected set; }

    private CharacterController cController;
    private MoveExecutor _moveExecutor;
    public IMoveExecutor moveExecutor => _moveExecutor;
    public IDurationMoveEffectComp durationMoveEffectComp { get; protected set; }

    private BuffManager _buffManager;
    public BuffManager BuffManager => _buffManager;

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
    IBuffComp IEntityContext.BuffComp => buffComp;
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
        //初始化移动和攻击组件

        SetUpMAComp();

        durationMoveEffectComp = new DurationMoveEffectComp();
        durationMoveEffectComp.Init(this);

        cController = GetComponent<CharacterController>();
        _moveExecutor = gameObject.AddComponent<MoveExecutor>();
        _moveExecutor.Init(cController);
    }

    protected override void OnShow(object userData)
    {
        // 初始化Buff管理器（必须在 base.OnShow 之前，因为 OnShowCallback 里会用到）
        InitializeBuffManager();

        base.OnShow(userData);

        // BuffComp 在 OnShow（而非 OnInit）中创建：每次 Show 重置所有 Buff 状态，
        // 与 moveComp/atkComp 不同——后者通过工厂在 OnInit 中创建并跨 Show/Hide 复用。
        // 直接 new（通用且无外部引用，不需要工厂）。
        var newBuffComp = new CharacterBuffComp();
        newBuffComp.Init(this);
        buffComp = newBuffComp;

        // 应用出生自带的 Buff
        if (userData is EntityParams ep && ep.StartBuffs != null)
        {
            for (int i = 0; i < ep.StartBuffs.Count; i++)
            {
                buffComp.AddBuff(ep.StartBuffs[i], this);
            }
        }

        // 注意：RegisterAgent 移到子类 OnShow 末尾，确保 Side 等字段已赋值
        EntityRegistry.Register(this);
    }

    private void InitializeBuffManager()
    {
        _buffManager = gameObject.AddComponent<BuffManager>();
        _buffManager.Initialize(this);
    }

    public void OnKill(MAEntity target)
    {
        _buffManager?.OnKill(target);
    }

    public void OnDead()
    {
        _buffManager?.OnHostDead();
    }

    /// <summary>
    /// 子类在 OnShow 末尾（Side 等字段赋值完毕后）调用，注册到 GroupMoveManager。
    /// </summary>
    protected void RegisterToGroupMove()
    {
        if (GroupMoveManager.HasInstance)
            GroupMoveManager.Instance.RegisterAgent(this);
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        // 显式清理 BuffComp，防止将来持有外部订阅时泄漏
        if (buffComp != null)
        {
            buffComp.ShutDown();
            buffComp = null;
        }

        _buffManager?.ClearAllBuffs();
        if (GroupMoveManager.HasInstance)
            GroupMoveManager.Instance.UnregisterAgent(this);
        EntityRegistry.Unregister(this);
        base.OnHide(isShutdown, userData);
    }

    protected virtual void Update()
    {
        float dt = Time.deltaTime;

        _buffManager?.UpdateBuffs(dt);

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

        if (CanRun(durationMoveEffectComp))
            durationMoveEffectComp.ApplyEffect(dt);

        if (CanRun(buffComp))
            buffComp.UpdateBuff(dt);

        moveExecutor.Execute();
    }

    #region Move and Attack

    protected virtual void SetUpMAComp()
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
    public void SetBuffComp(IBuffComp newBuffComp) => buffComp = newBuffComp;
    public void SetWeaponComp(WeaponComp newWeaponComp) => weaponComp = newWeaponComp;

    #endregion
}
