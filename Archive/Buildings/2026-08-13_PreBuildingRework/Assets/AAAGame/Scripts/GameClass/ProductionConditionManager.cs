using UnityGameFramework.Runtime;

/// <summary>
/// Legacy component facade for UI and serialized scene references.
/// Authoritative production statistics live in LogicProductionConditionState.
/// </summary>
public sealed class ProductionConditionManager : GameFrameworkComponent
{
    private void OnEnable()
    {
        LevelSelectionService.LevelLoadStarted += ClearAllStatistics;
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= ClearAllStatistics;
    }

    public int GetTroopCountInStronghold(Stronghold stronghold)
    {
        return LogicProductionConditionState.GetTroopCount(GetStrongholdId(stronghold));
    }

    public int GetBuildingCountInStronghold(Stronghold stronghold, string buildingType)
    {
        return LogicProductionConditionState.GetBuildingCount(GetStrongholdId(stronghold), buildingType);
    }

    public int GetKillCountForStronghold(Stronghold stronghold, int day)
    {
        return LogicProductionConditionState.GetKillCount(GetStrongholdId(stronghold), day);
    }

    public int GetHeavyKillCountForStronghold(Stronghold stronghold, int day)
    {
        return LogicProductionConditionState.GetHeavyKillCount(GetStrongholdId(stronghold), day);
    }

    public int GetSurvivorCountForStronghold(Stronghold stronghold, int day)
    {
        return LogicProductionConditionState.GetSurvivorCount(GetStrongholdId(stronghold), day);
    }

    public bool WasBuildingDamagedInStronghold(Stronghold stronghold, int day)
    {
        return LogicProductionConditionState.WasBuildingDamaged(GetStrongholdId(stronghold), day);
    }

    public int GetPlayerOccupiedStrongholdCount()
    {
        return LogicProductionConditionState.GetPlayerOccupiedStrongholdCount();
    }

    public void OnPhaseChanged(GamePhase newPhase)
    {
        if (InGameDataModel.IsBuildPhase(newPhase))
            LogicBuildingProductionService.RefreshAll();
    }

    public void SetKillCountForStronghold(Stronghold stronghold, int day, int killCount)
    {
        LogicProductionConditionState.SetKillCount(GetStrongholdId(stronghold), day, killCount);
    }

    public void ClearAllStatistics()
    {
        LogicProductionConditionState.ClearAll();
    }

    private static string GetStrongholdId(Stronghold stronghold)
    {
        return stronghold?.strongholdData?.StrongholdId;
    }
}
