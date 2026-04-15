using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public class TechManager : GameFrameworkComponent
{
    private readonly InputKey[] m_OptionalOptionKeys =
    {
        InputKey.InteractionPrimary,
        InputKey.InteractionSecondary,
        InputKey.InteractionTertiary,
    };

    public bool HasTechInteraction(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null || owner.buildingData.Lv == 0)
            return false;

        if (owner.buildingData.Type == BuilType.Tech)
            return HasResearchTechCandidates(owner);

        return HasUpgradeTechCandidates(owner);
    }

    public void ConfigureTechInteractionOptions(BuildingEntity owner, InteractionHost host)
    {
        if (owner == null || owner.buildingData == null || owner.buildingData.Lv == 0 || host == null)
            return;

        if (owner.buildingData.Type == BuilType.Tech)
        {
            ConfigureTechResearchOptions(owner, host);
            return;
        }

        ConfigureUpgradeOptions(owner, host);
    }

    public bool IsUpgradeOptionVisible(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (!CanPlayerOperateInBuildPhase(owner))
            return false;

        if (string.IsNullOrWhiteSpace(upgradeBuildingId) || BuildingDataModel.GetBuildingData(upgradeBuildingId) == null)
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null || string.IsNullOrWhiteSpace(techId))
            return false;

        if (techData.IsStackable)
            return !InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId);

        return !InGameDataModel.HasUnlockedTech(techId);
    }

    public bool IsResearchOptionVisible(BuildingEntity owner, string techId)
    {
        if (!CanPlayerOperateInBuildPhase(owner))
            return false;

        if (owner.buildingData.Type != BuilType.Tech)
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null || string.IsNullOrWhiteSpace(techId))
            return false;

        if (techData.IsStackable)
            return !InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId);

        return !InGameDataModel.HasUnlockedTech(techId);
    }

    public bool IsUpgradeOptionExecutable(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        var buildManager = RequireBuildManager();
        return IsUpgradeOptionVisible(owner, upgradeBuildingId, techId)
            && SatisfyUpgradeCondition(owner, upgradeBuildingId, techId)
            && buildManager.HasBuildCost(upgradeBuildingId);
    }

    public bool IsResearchOptionExecutable(BuildingEntity owner, string techId)
    {
        return IsResearchOptionVisible(owner, techId)
            && SatisfyTechCondition(techId)
            && HasTechCost(techId);
    }

    public KeyValuePair<IngameValueType, int>[] GetTechResourceCosts(string techId)
    {
        var techData = TechDataModel.GetTechData(techId);
        if (techData == null || techData.Cost <= 0)
            return null;

        return new[]
        {
            new KeyValuePair<IngameValueType, int>(IngameValueType.Coin, techData.Cost)
        };
    }

    public bool SatisfyUpgradeCondition(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (owner == null)
            return false;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null)
            return false;

        var buildManager = RequireBuildManager();
        return SatisfyTechCondition(techId) && buildManager.SatisfyBuildCondition(upgradeBuildingData, owner.OwnerFactionID);
    }

    public bool UpgradeBuilding(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (!IsUpgradeOptionExecutable(owner, upgradeBuildingId, techId))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null)
            return false;

        if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -upgradeBuildingData.Cost, true))
            return false;

        var buildManager = RequireBuildManager();
        bool built = buildManager.BuildBuildingForTechUpgrade(upgradeBuildingId, owner.CachedTransform.position, owner.BuildingInstanceId);
        if (!built)
            return false;

        InGameDataModel.UnlockTech(techId, techData.IsStackable, owner.BuildingInstanceId, owner.OwnerFactionID);
        GF.Entity.HideEntity(owner.Entity);
        return true;
    }

    public bool ResearchTech(BuildingEntity owner, string techId)
    {
        if (!IsResearchOptionExecutable(owner, techId))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -techData.Cost, true))
            return false;

        return InGameDataModel.UnlockTech(techId, techData.IsStackable, owner.BuildingInstanceId, owner.OwnerFactionID);
    }

    private bool HasUpgradeTechCandidates(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null || owner.buildingData.UpgradeTechIDs == null)
            return false;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return false;

        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            if (IsUpgradeOptionVisible(owner, upgradeBuildingId, owner.buildingData.UpgradeTechIDs[i]))
                return true;
        }

        return false;
    }

    private bool HasResearchTechCandidates(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null || owner.buildingData.UpgradeTechIDs == null)
            return false;

        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            if (IsResearchOptionVisible(owner, owner.buildingData.UpgradeTechIDs[i]))
                return true;
        }

        return false;
    }

    private void ConfigureUpgradeOptions(BuildingEntity owner, InteractionHost host)
    {
        if (owner.buildingData.UpgradeTechIDs == null)
            return;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId) || BuildingDataModel.GetBuildingData(upgradeBuildingId) == null)
            return;

        int optionIndex = 0;
        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (!IsUpgradeOptionVisible(owner, upgradeBuildingId, techId))
                continue;

            var techData = TechDataModel.GetTechData(techId);
            if (techData == null)
                continue;

            string displayName = GF.Localization.GetString(techData.NameKey);
            InteractionParams @params = InteractionParams.Create();
            @params.Set<VarString>("UpgradeBuildingId", upgradeBuildingId);
            @params.Set<VarString>("TechId", techId);

            if (TryGetOptionalOptionKey(optionIndex, out var optionKey))
                host.AddOption<BuildingUpgradeInteractionOption>(optionKey, displayName, @params);
            else
                host.AddOption<BuildingUpgradeInteractionOption>(displayName, @params);

            optionIndex++;
        }
    }

    private void ConfigureTechResearchOptions(BuildingEntity owner, InteractionHost host)
    {
        if (owner.buildingData.UpgradeTechIDs == null)
            return;

        int optionIndex = 0;
        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (!IsResearchOptionVisible(owner, techId))
                continue;

            var techData = TechDataModel.GetTechData(techId);
            if (techData == null)
                continue;

            string displayName = GF.Localization.GetString(techData.NameKey);
            InteractionParams @params = InteractionParams.Create();
            @params.Set<VarString>("TechId", techId);

            if (TryGetOptionalOptionKey(optionIndex, out var optionKey))
                host.AddOption<TechResearchInteractionOption>(optionKey, displayName, @params);
            else
                host.AddOption<TechResearchInteractionOption>(displayName, @params);

            optionIndex++;
        }
    }

    private bool SatisfyTechCondition(string techId)
    {
        if (string.IsNullOrWhiteSpace(techId))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        return techData.IsStackable || !InGameDataModel.HasUnlockedTech(techId);
    }

    private bool HasTechCost(string techId)
    {
        var techData = TechDataModel.GetTechData(techId);
        return techData != null && InGameDataModel.GetValue(IngameValueType.Coin) >= techData.Cost;
    }

    private bool TryGetOptionalOptionKey(int optionIndex, out InputKey key)
    {
        key = default;
        if (optionIndex < 0 || optionIndex >= m_OptionalOptionKeys.Length)
            return false;

        key = m_OptionalOptionKeys[optionIndex];
        return true;
    }

    private static bool CanPlayerOperateInBuildPhase(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null || owner.OwnerFactionID != 0)
            return false;

        var inGameData = GF.DataModel.GetDataModel<InGameDataModel>();
        return inGameData != null && (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) == GamePhase.Build;
    }

    private static BuildManager RequireBuildManager()
    {
        var buildManager = GameEntry.GetComponent<BuildManager>();
        if (buildManager == null)
            throw new InvalidOperationException("BuildManager is required for TechManager.");

        return buildManager;
    }
}
