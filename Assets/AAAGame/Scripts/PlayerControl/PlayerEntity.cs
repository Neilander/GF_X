using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerEntity : MAEntity
{
    protected override void SetUpMAComp()
    {
        //不采用基础的setup，而是手动setup
        string moveFacPath = "PlayerMoveFactory";
        //string atkFacPath = "PlayerAtkFactory";
        //设置组件
        GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),MoveCompFactory.MoveFactoryCallBack,this );
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),AtkCompFactory.AtkFactoryCallBack,this );
    }
}
