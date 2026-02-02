using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;

public class PlayerEntity : SkillEntity
{
    protected override void SetUpMAComp()
    {
        //不采用基础的setup，而是手动setup
        string moveFacPath = "PlayerMoveFactory";
        string atkFacPath = "PlayerAtkFactory";
        //设置组件
        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),this);
        //PlayerAttackComp.CreateAtkComp(this);
        FactoryHelper.CreateAtkComp(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),this);
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath),MoveCompFactory.MoveFactoryCallBack,this );
        //GF.Resource.LoadAsset(UtilityBuiltin.AssetsPath.GetAttackFactoryPath(atkFacPath),AtkCompFactory.AtkFactoryCallBack,this );
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        CameraController.Instance.SetFollowTarget(gameObject.transform);
        if (userData is EntityParams)
        {
            transform.position = (userData as EntityParams).position?? Vector3.zero;
        }

        Side = SideType.PlayerSide;
    }

    protected override void SetUpSkillComp()
    {
        string skillFacPath = "PlayerSkillFactory";
        FactoryHelper.CreateSkillComp(UtilityBuiltin.AssetsPath.GetSkillFactoryPath(skillFacPath),this);
    }

    protected override void Update()
    {
        base.Update();
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.T))
        {
            SpawnTestProjectile();
            //SpawnTestSelector();
            //SpawnTestHitBoxAt003();
        }
       
#endif
    }

#if UNITY_EDITOR
    private void SpawnTestHitBoxAt003()
    {
        // 1. 创建空物体
        GameObject go = new GameObject("TestHitBox_003");

        // 2. 设置世界坐标 (0, 0, 3)
        go.transform.position = new Vector3(0f, 0f, 3f);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // 3. 加 BoxCollider（Trigger）
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = Vector3.one; // 测试用，默认 1x1x1

        // 4. 加 HitBox
        HitBox hitBox = go.AddComponent<HitBox>();

        // 5. 激活 HitBox
        hitBox.Activate(this,new Damage(this,1) );

        go.AddComponent(typeof(Rigidbody));
        go.GetComponent<Rigidbody>().isKinematic = true;
        go.layer = LayerMask.NameToLayer("Hit");
    }

    private void SpawnTestSelector()
    {
        var hitboxParams = EntityParams.Create();
        hitboxParams.OnShowCallback = logic =>
        {
            CylinderTargetSelector selector = (CylinderTargetSelector)logic;
            selector.Activate(new List<ISelectable>(), SideType.PlayerSide);
            selector.ChangeRange(new Vector3(3,4,0));
        };
        
        GF.Entity.ShowEntity<CylinderTargetSelector>("CylinderSelector", Const.EntityGroup.Default, hitboxParams);
    }

    private void SpawnTestProjectile()
    {
        var projectileParams = EntityParams.Create();
        
        projectileParams.OnShowCallback = logic =>
        {
            DirectionProjectile dirPro = (DirectionProjectile)logic;
            logic.transform.position = transform.position+Vector3.up*0.5f;
            dirPro.StartMoveWithDirection(new Vector3(1,0,0), this, new Damage(this, 1));
        };
        GF.Entity.ShowEntity<DirectionProjectile>("TestProjectile", Const.EntityGroup.Default, projectileParams);
    }
#endif
}
