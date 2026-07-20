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
    private const string InfoOptionTextId = "InteractOption_Check";

    public bool HasTechInteraction(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null || owner.buildingData.Lv == 0)
            return false;

        if (HasInfoInteraction(owner))
            return true;

        if (owner.buildingData.Type == BuilType.Tech)
            return HasResearchTechCandidates(owner);

        return HasUpgradeTechCandidates(owner);
    }

    public void ConfigureTechInteractionOptions(BuildingEntity owner, InteractionHost host)
    {
        if (owner == null || owner.buildingData == null || owner.buildingData.Lv == 0 || host == null)
            return;

        if (HasInfoInteraction(owner))
        {
            ConfigureInfoOption(owner, host);
            return;
        }

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
            return !InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId)
                   && !LogicTechEffectCommandService.HasPending(techId, owner.BuildingInstanceId);

        return !InGameDataModel.HasUnlockedTech(techId)
               && !LogicTechEffectCommandService.HasPending(techId);
    }

    public bool IsResearchOptionVisible(BuildingEntity owner, string techId)
    {
        if (!CanPlayerOperateInBuildPhase(owner))
            return false;

        if (owner.buildingData.Type != BuilType.Tech)
            return false;

        if (HasReachedResearchLimit(owner))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null || string.IsNullOrWhiteSpace(techId))
            return false;

        if (techData.IsStackable)
            return !InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId)
                   && !LogicTechEffectCommandService.HasPending(techId, owner.BuildingInstanceId);

        return !InGameDataModel.HasUnlockedTech(techId)
               && !LogicTechEffectCommandService.HasPending(techId);
    }

    public bool IsUpgradeOptionExecutable(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (HasPendingInteraction(owner))
            return false;
        var buildManager = RequireBuildManager();
        return IsUpgradeOptionVisible(owner, upgradeBuildingId, techId)
            && SatisfyUpgradeCondition(owner, upgradeBuildingId, techId)
            && buildManager.HasBuildCost(upgradeBuildingId, owner);
    }

    public bool IsResearchOptionExecutable(BuildingEntity owner, string techId)
    {
        if (HasPendingInteraction(owner))
            return false;
        return IsResearchOptionVisible(owner, techId)
            && SatisfyTechCondition(techId)
            && HasTechCost(techId);
    }

    public bool IsInfoOptionVisible(BuildingEntity owner)
    {
        return CanPlayerOperateInBuildPhase(owner) && HasInfoInteraction(owner);
    }

    public KeyValuePair<IngameValueType, int>[] GetTechResourceCosts(string techId)
    {
        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return null;

        int cost = LevelTagRuntime.ModifyTechCost(techData.Cost);
        if (cost <= 0)
            return null;

        return new[]
        {
            new KeyValuePair<IngameValueType, int>(IngameValueType.Coin, cost)
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

        LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.UpgradeBuilding,
            owner.LogicEntityId,
            owner.BuildingInstanceId,
            upgradeBuildingId,
            techId);
        return true;
    }

    internal bool ApplyScheduledUpgradeBuilding(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        EnsureInteractionApplyWindow();
        if (!IsUpgradeOptionExecutableForApply(owner, upgradeBuildingId, techId))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null)
            return false;

        var buildManager = RequireBuildManager();
        int upgradeCost = buildManager.GetBuildingCost(upgradeBuildingData, owner.CurrentStronghold);
        bool built = buildManager.BuildBuildingForTechUpgrade(upgradeBuildingId, owner.CachedTransform.position, owner.BuildingInstanceId);
        if (!built)
            return false;

        if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -upgradeCost, true))
            throw new InvalidOperationException("Upgrade transaction lost its validated coin balance before commit.");

        InGameDataModel.RecordBuildingCostSpent(owner.BuildingInstanceId, upgradeCost);
        if (!InGameDataModel.UnlockTechInCurrentInteractionFrame(
                techId,
                techData.IsStackable,
                owner.BuildingInstanceId,
                owner.OwnerFactionID))
        {
            throw new InvalidOperationException($"Upgrade transaction failed to schedule tech '{techId}'.");
        }
        owner.RequestDespawn();
        return true;
    }

    public bool ResearchTech(BuildingEntity owner, string techId)
    {
        if (!IsResearchOptionExecutable(owner, techId))
            return false;

        LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.ResearchTech,
            owner.LogicEntityId,
            owner.BuildingInstanceId,
            techId);
        return true;
    }

    internal bool ApplyScheduledResearchTech(BuildingEntity owner, string techId)
    {
        EnsureInteractionApplyWindow();
        if (!IsResearchOptionExecutableForApply(owner, techId))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        int techCost = LevelTagRuntime.ModifyTechCost(techData.Cost);
        if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -techCost, true))
            return false;

        if (!InGameDataModel.UnlockTechInCurrentInteractionFrame(
                techId,
                techData.IsStackable,
                owner.BuildingInstanceId,
                owner.OwnerFactionID))
        {
            throw new InvalidOperationException($"Research transaction failed to schedule tech '{techId}'.");
        }

        return true;
    }

    private bool IsUpgradeOptionExecutableForApply(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        var buildManager = RequireBuildManager();
        return IsUpgradeOptionVisible(owner, upgradeBuildingId, techId)
               && SatisfyUpgradeCondition(owner, upgradeBuildingId, techId)
               && buildManager.HasBuildCost(upgradeBuildingId, owner);
    }

    private bool IsResearchOptionExecutableForApply(BuildingEntity owner, string techId)
    {
        return IsResearchOptionVisible(owner, techId)
               && SatisfyTechCondition(techId)
               && HasTechCost(techId);
    }

    private static bool HasPendingInteraction(BuildingEntity owner)
    {
        return owner != null
               && LogicInteractionCommandService.IsActive
               && LogicInteractionCommandService.HasPendingForTarget(owner.LogicEntityId);
    }

    private static void EnsureInteractionApplyWindow()
    {
        if (!LogicInteractionCommandService.IsApplyingFrame)
            throw new InvalidOperationException("Tech interaction mutation requires the logic interaction command apply window.");
    }

    public int RollbackTechsForBuilding(BuildingEntity owner)
    {
        if (owner == null || string.IsNullOrWhiteSpace(owner.BuildingInstanceId))
            return 0;

        List<string> techIds = InGameDataModel.GetUnlockedTechIdsForBuilding(owner.BuildingInstanceId);
        if (techIds == null || techIds.Count <= 0)
            return 0;

        int rolledBack = 0;
        GlobalBuffManager globalBuffManager = GameEntry.GetComponent<GlobalBuffManager>();
        for (int i = 0; i < techIds.Count; i++)
        {
            string techId = techIds[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (!InGameDataModel.ReduceTechStack(techId, owner.BuildingInstanceId, 1))
                continue;

            globalBuffManager?.UnregisterTechEffects(techId, owner.OwnerFactionID, owner.BuildingInstanceId);
            rolledBack++;
        }

        if (rolledBack > 0)
            globalBuffManager?.ClearBuildingRuntimeTechState(owner.BuildingInstanceId, owner.OwnerFactionID);

        return rolledBack;
    }

    private bool HasUpgradeTechCandidates(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null || owner.buildingData.UpgradeTechIDs == null)
            return false;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return false;

        if (BuildingDataModel.GetBuildingData(upgradeBuildingId) == null)
            return false;

        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (TechDataModel.GetTechData(techId) != null)
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
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (TechDataModel.GetTechData(techId) != null)
                return true;
        }

        return false;
    }

    private bool HasInfoInteraction(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return false;

        BuildingData data = owner.buildingData;
        if (data.Lv >= 3)
            return true;

        if (data.Type != BuilType.Tech || data.Lv <= 0 || string.IsNullOrWhiteSpace(owner.BuildingInstanceId))
            return false;

        if (data.UpgradeTechIDs == null)
            return false;

        for (int i = 0; i < data.UpgradeTechIDs.Length; i++)
        {
            string techId = data.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId))
                return HasReachedResearchLimit(owner);
        }

        return false;
    }

    private static bool HasReachedResearchLimit(BuildingEntity owner)
    {
        return CountResearchedTech(owner) >= GetResearchLimit(owner);
    }

    private static int GetResearchLimit(BuildingEntity owner)
    {
        if (owner?.buildingData == null || owner.buildingData.Type != BuilType.Tech)
            return 1;

        return Math.Max(1, 1 + LevelTagRuntime.GetExtraTechResearchCountPerBuilding());
    }

    private static int CountResearchedTech(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null || string.IsNullOrWhiteSpace(owner.BuildingInstanceId))
            return 0;

        if (owner.buildingData.UpgradeTechIDs == null)
            return 0;

        int count = 0;
        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId))
                count++;
        }

        return count;
    }

    private void ConfigureInfoOption(BuildingEntity owner, InteractionHost host)
    {
        string displayName = LocalizationTextDataModel.GetText(InfoOptionTextId);
        host.AddOption<BuildingInfoInteractionOption>(InputKey.InteractionPrimary, displayName, InteractionParams.Create());
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
            // 先挂载所有合法升级选项，是否显示/可执行交给 option 的动态判定。
            string techId = owner.buildingData.UpgradeTechIDs[i];
            var techData = TechDataModel.GetTechData(techId);
            if (techData == null)
                continue;

            string displayName = LocalizationTextManager.GetLocalizedText(techData.NameKey, false);
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
            // 先挂载所有合法研究选项，避免生成时机导致后续阶段无可见项。
            string techId = owner.buildingData.UpgradeTechIDs[i];
            var techData = TechDataModel.GetTechData(techId);
            if (techData == null)
                continue;

            string displayName = LocalizationTextManager.GetLocalizedText(techData.NameKey, false);
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

        if (techData.ScopeType == TechScopeType.Skill)
            return !string.IsNullOrWhiteSpace(techData.SkillID);

        return techData.IsStackable || !InGameDataModel.HasUnlockedTech(techId);
    }

    private bool HasTechCost(string techId)
    {
        var techData = TechDataModel.GetTechData(techId);
        return techData != null && InGameDataModel.GetValue(IngameValueType.Coin) >= LevelTagRuntime.ModifyTechCost(techData.Cost);
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
        return inGameData != null && InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase));
    }

    private static BuildManager RequireBuildManager()
    {
        var buildManager = GameEntry.GetComponent<BuildManager>();
        if (buildManager == null)
            throw new InvalidOperationException("BuildManager is required for TechManager.");

        return buildManager;
    }
}
