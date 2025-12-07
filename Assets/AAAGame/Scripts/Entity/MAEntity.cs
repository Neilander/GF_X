using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;

public class MAEntity :GeneralCreature
{
    protected IMoveComp moveComp;
    protected IAtkComp atkComp;
    
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        //初始化移动和攻击组件

        SetUpMAComp();

    }

    protected virtual void Update()
    {
        moveComp?.Move();
        atkComp?.Attack();
    }

    #region Move and Attack

    protected virtual void SetUpMAComp()
    {
        //获取路径
        var row = GF.DataTable.GetDataTable<CharacterMAFactoryTable>().GetDataRows(r => r.CharacterKey == ReferenceId)[0];
        string moveFacPath = row.MoveFactoryPath;
        string atkFacPath = row.AttackFactoryPath;
        //设置组件
        GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),MoveCompFactory.MoveFactoryCallBack,this );
        GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),AtkCompFactory.AtkFactoryCallBack,this );
    }

    public void SetMoveComp(IMoveComp newMoveComp)=>moveComp = newMoveComp; 
    public void SetAtkComp(IAtkComp newAtkComp)=> atkComp = newAtkComp;

    #endregion
    
    
}
