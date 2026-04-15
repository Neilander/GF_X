/// <summary>
/// Lv0 建筑常驻无敌 Buff。
/// 将旧的 buildingData.Lv == 0 硬判断迁移为 Buff 驱动状态。
/// </summary>
public class BuildingLv0InvincibleBuff : BuffCallback
{
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "building_lv0_invincible::source"
            : $"{buffData.id}::source";

        RefreshInvincibleState();
    }

    public override void OnRemove()
    {
        if (hostEntity is BuildingEntity building)
        {
            building.SetLv0InvincibleByBuff(false);
            building.UnregisterInvincibleSource(_invincibleSourceId);
        }
    }

    private void RefreshInvincibleState()
    {
        if (!(hostEntity is BuildingEntity building))
            return;

        bool shouldInvincible = building.buildingData != null && building.buildingData.Lv == 0;
        building.SetLv0InvincibleByBuff(shouldInvincible);
        if (shouldInvincible)
            building.RegisterInvincibleSource(_invincibleSourceId);
        else
            building.UnregisterInvincibleSource(_invincibleSourceId);
    }
}
