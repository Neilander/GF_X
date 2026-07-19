using System.Collections;
using UnityEngine;

public class PlayerEntity : SkillEntity
{
    protected override void SetUpMAComp(object userData)
    {
        //不采用基础的setup，而是手动setup
        string moveFacPath = "PlayerMoveFactory";
        string atkFacPath = "PlayerAtkFactory";
        //设置组件
        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath), this);
        FactoryHelper.CreateAtkComp(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath), this);
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),MoveCompFactory.MoveFactoryCallBack,this );
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),AtkCompFactory.AtkFactoryCallBack,this );
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        CameraController.Instance.SetFollowTarget(gameObject.transform);

        Side = SideType.PlayerSide;
        RegisterToGroupMove(); // Side 已赋值，安全注册
    }

    protected override void SetUpSkillComp()
    {
        string skillFacPath = "PlayerSkillFactory";
        FactoryHelper.CreateSkillComp(UtilityBuiltin.AssetsPath.GetSkillFactoryPath(skillFacPath), this);
    }

}
