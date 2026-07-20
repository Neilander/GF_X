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
        if (hostEntity is IBuildingLogicContext building)
            building.UnregisterInvincibleSource(_invincibleSourceId);
    }

    private void RefreshInvincibleState()
    {
        if (hostEntity is not IBuildingLogicContext building)
            throw new System.InvalidOperationException("BuildingLv0InvincibleBuff requires a building logic context.");

        bool shouldInvincible = building.BuildingData != null && building.BuildingData.Lv == 0;
        if (shouldInvincible)
            building.RegisterInvincibleSource(_invincibleSourceId);
        else
            building.UnregisterInvincibleSource(_invincibleSourceId);
    }
}
