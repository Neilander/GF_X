using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;

public class MAEntity :CompCreature
{
    public IMoveComp moveComp { get; protected set; }
    public IAtkComp atkComp{ get; protected set; }
    
    public ITargetingComp targetComp{ get; protected set; }
    
    private CharacterController cController;
    public MoveExecutor moveExecutor { get; private set; }
    public IDurationMoveEffectComp durationMoveEffectComp { get; protected set; }
    
    public IControlBrain Brain { get; private set; } 
    public void SetBrain(IControlBrain brain) => Brain = brain;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        //初始化移动和攻击组件

        SetUpMAComp();
        
        durationMoveEffectComp = new DurationMoveEffectComp();
        durationMoveEffectComp.Init(this);
        
        cController = GetComponent<CharacterController>();
        moveExecutor = gameObject.AddComponent<MoveExecutor>();
        moveExecutor.Init(cController);
    }

    protected virtual void Update()
    {
        if (Brain is ITickBrain tickBrain)
        {
            tickBrain.Tick(this, Time.deltaTime);
        }
        
        if (CanRun(targetComp))
            targetComp.UpdateTargeting();
        
        if (CanRun(moveComp))
            moveComp.Move();

        if (CanRun(atkComp))
            atkComp.Attack();
        
        if(CanRun(durationMoveEffectComp))
            durationMoveEffectComp.ApplyEffect(Time.deltaTime);
        
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
        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),this);
        FactoryHelper.CreateAtkComp(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),this);
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),MoveCompFactory.MoveFactoryCallBack,this );
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),AtkCompFactory.AtkFactoryCallBack,this );
    }

    public void SetMoveComp(IMoveComp newMoveComp)=>moveComp = newMoveComp; 
    public void SetAtkComp(IAtkComp newAtkComp)=> atkComp = newAtkComp;
    
    public void SetTargetingComp(ITargetingComp newTargetingComp)=> targetComp = newTargetingComp;

    #endregion
    
    
}
