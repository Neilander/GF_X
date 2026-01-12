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
        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath), this);
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),MoveCompFactory.MoveFactoryCallBack,this );
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),AtkCompFactory.AtkFactoryCallBack,this );
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        CameraController.Instance.SetFollowTargetLegacyIsometric(gameObject.transform, false);
        if (userData is EntityParams)
        {
            transform.position = (userData as EntityParams).position ?? Vector3.zero;
        }
    }
}
