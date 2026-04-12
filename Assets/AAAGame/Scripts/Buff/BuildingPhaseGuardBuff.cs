using UnityEngine;

/// <summary>
/// 非 Invade 阶段给敌方建筑提供“无敌/不可被索敌/不攻击”保护。
/// 通过每帧刷新状态与阶段和归属同步，而不是硬编码在外部流程里。
/// </summary>
public class BuildingPhaseGuardBuff : BuffCallback
{
    public override void OnAdd()
    {
        RefreshProtectionState();
    }

    public override void OnUpdate(float deltaTime)
    {
        RefreshProtectionState();
    }

    public override void OnRemove()
    {
        if (hostEntity is BuildingEntity building)
            building.SetPhaseProtectionByBuff(false);
    }

    private void RefreshProtectionState()
    {
        if (!(hostEntity is BuildingEntity building))
            return;

        bool isEnemyBuilding = building.OwnerFactionID == EntitySideHelper.EnemyFactionId;
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        bool shouldProtect = isEnemyBuilding && phase != GamePhase.Invade;

        building.SetPhaseProtectionByBuff(shouldProtect);
    }
}
