using UnityEngine;

/// <summary>
/// 小兵实体：使用 DirectAtkComp（直接选定目标造成伤害，不走攻击盒）。
/// 适合大量小兵的战斗场景。
/// </summary>
public class SoldierEntity : MAEntity
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        if (userData is EntityParams ep)
        {
            Side = ep.Side;
            SetBrain(BrainFactory.Create(ep.BrainType, this, ep));
        }

        RegisterToGroupMove(); // Side 已赋值，安全注册
    }

    protected override void Update()
    {
        base.Update();

        // Debug: 绿线=NavMesh方向, 红线=到目标直线
        if (targetComp?.CurrentTarget != null && targetComp.CurrentTarget.Alive)
        {
            var target = targetComp.CurrentTarget;
            Vector3 pos = Position + Vector3.up * 0.5f;
            // 红线：到目标的直线
            Debug.DrawLine(pos, target.Position + Vector3.up * 0.5f, Color.red);
            // 绿线：NavMesh 计算的方向
            if (moveComp != null)
            {
                Vector3 navDir = moveComp.GetNavDirection();
                Debug.DrawRay(pos, navDir * 3f, Color.green);
            }
        }
    }

    protected override void SetUpMAComp()
    {
        string moveFacPath = "CharacterMoveFactory";
        string targetFacPath = "CharacterTargetingFactory";

        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath), this);
        FactoryHelper.CreateTargetingComp(UtilityBuiltin.AssetsPath.GetTargetingFactoryPath(targetFacPath), this);

        // 直接创建 DirectAtkComp，不再走 Factory
        var atkComp = new DirectAtkComp("ranged_test"); // TODO: index 应从配表或 EntityParams 获取，暂时修改为ranged_test
        this.SetAtkComp(atkComp);    // 先让 Entity 持有引用
        atkComp.Init(this);          // Init 内部会创建 WeaponComp 并通过 SetWeaponComp 挂载
    }
}
