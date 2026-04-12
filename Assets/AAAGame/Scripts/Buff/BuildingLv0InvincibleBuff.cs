/// <summary>
/// Lv0 建筑常驻无敌 Buff。
/// 将旧的 buildingData.Lv == 0 硬判断迁移为 Buff 驱动状态。
/// </summary>
public class BuildingLv0InvincibleBuff : BuffCallback
{
    public override void OnAdd()
    {
        RefreshInvincibleState();
    }

    public override void OnUpdate(float deltaTime)
    {
        RefreshInvincibleState();
    }

    public override void OnRemove()
    {
        if (hostEntity is BuildingEntity building)
            building.SetLv0InvincibleByBuff(false);
    }

    private void RefreshInvincibleState()
    {
        if (!(hostEntity is BuildingEntity building))
            return;

        bool shouldInvincible = building.buildingData != null && building.buildingData.Lv == 0;
        building.SetLv0InvincibleByBuff(shouldInvincible);
    }
}
