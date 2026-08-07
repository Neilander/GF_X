using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class TechManager : GameFrameworkComponent
{
    private BuildManager m_BuildManager;
    private GlobalBuffManager m_GlobalBuffManager;

    public void PrepareRuntimeDependencies()
    {
        m_BuildManager = GameEntry.GetComponent<BuildManager>()
                         ?? throw new InvalidOperationException("TechManager requires BuildManager during preload.");
        m_GlobalBuffManager = GameEntry.GetComponent<GlobalBuffManager>()
                              ?? throw new InvalidOperationException("TechManager requires GlobalBuffManager during preload.");
    }

    public bool HasTechInteraction(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null || owner.BuildingData.Lv == 0)
            return false;

        if (HasInfoInteraction(owner))
            return true;

        if (owner.BuildingData.Type == BuilType.Tech)
            return HasResearchTechCandidates(owner);

        return HasUpgradeTechCandidates(owner);
    }

    public bool IsUpgradeOptionVisible(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
    {
        if (!CanPlayerOperateInBuildPhase(owner))
            return false;

        if (!TutorialManager.IsUpgradeOptionAllowed(owner))
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

    public bool IsResearchOptionVisible(IBuildingLogicContext owner, string techId)
    {
        if (!CanPlayerOperateInBuildPhase(owner))
            return false;

        if (owner.BuildingData.Type != BuilType.Tech)
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

    public bool IsUpgradeOptionExecutable(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
    {
        if (HasPendingInteraction(owner))
            return false;
        var buildManager = RequireBuildManager();
        return IsUpgradeOptionVisible(owner, upgradeBuildingId, techId)
            && SatisfyUpgradeCondition(owner, upgradeBuildingId, techId)
            && buildManager.HasBuildCost(upgradeBuildingId, owner);
    }

    public bool IsResearchOptionExecutable(IBuildingLogicContext owner, string techId)
    {
        if (HasPendingInteraction(owner))
            return false;
        return IsResearchOptionVisible(owner, techId)
            && SatisfyTechCondition(techId)
            && HasTechCost(techId);
    }

    public bool IsInfoOptionVisible(IBuildingLogicContext owner)
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

    public bool SatisfyUpgradeCondition(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
    {
        if (owner == null)
            return false;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null)
            return false;

        var buildManager = RequireBuildManager();
        return SatisfyTechCondition(techId) && buildManager.SatisfyBuildCondition(upgradeBuildingData, owner.OwnerFactionId);
    }

    public bool UpgradeBuilding(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
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

    internal bool ApplyScheduledUpgradeBuilding(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
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
        int upgradeCost = buildManager.GetBuildingCost(upgradeBuildingId, owner);
        bool built = buildManager.BuildBuildingForTechUpgrade(
            upgradeBuildingId,
            owner.LogicFramePositionFixed(),
            owner);
        if (!built)
            return false;

        if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -upgradeCost, true))
            throw new InvalidOperationException("Upgrade transaction lost its validated coin balance before commit.");

        InGameDataModel.RecordBuildingCostSpent(owner.BuildingInstanceId, upgradeCost);
        if (!InGameDataModel.UnlockTechInCurrentInteractionFrame(
                techId,
                techData.IsStackable,
                owner.BuildingInstanceId,
                owner.OwnerFactionId))
        {
            throw new InvalidOperationException($"Upgrade transaction failed to schedule tech '{techId}'.");
        }
        LogicEntityLifecycleService.RequestDespawnForCurrentInteractionFrame(owner.LogicEntityId);
        return true;
    }

    public bool ResearchTech(IBuildingLogicContext owner, string techId)
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

    internal bool ApplyScheduledResearchTech(IBuildingLogicContext owner, string techId)
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
                owner.OwnerFactionId))
        {
            throw new InvalidOperationException($"Research transaction failed to schedule tech '{techId}'.");
        }

        return true;
    }

    private bool IsUpgradeOptionExecutableForApply(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
    {
        var buildManager = RequireBuildManager();
        return IsUpgradeOptionVisible(owner, upgradeBuildingId, techId)
               && SatisfyUpgradeCondition(owner, upgradeBuildingId, techId)
               && buildManager.HasBuildCost(upgradeBuildingId, owner);
    }

    private bool IsResearchOptionExecutableForApply(IBuildingLogicContext owner, string techId)
    {
        return IsResearchOptionVisible(owner, techId)
               && SatisfyTechCondition(techId)
               && HasTechCost(techId);
    }

    private static bool HasPendingInteraction(IBuildingLogicContext owner)
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

    public int RollbackTechsForBuilding(IBuildingLogicContext owner)
    {
        if (owner == null || string.IsNullOrWhiteSpace(owner.BuildingInstanceId))
            return 0;

        List<string> techIds = InGameDataModel.GetUnlockedTechIdsForBuilding(owner.BuildingInstanceId);
        if (techIds == null || techIds.Count <= 0)
            return 0;

        int rolledBack = 0;
        GlobalBuffManager globalBuffManager = RequireGlobalBuffManager();
        for (int i = 0; i < techIds.Count; i++)
        {
            string techId = techIds[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (!InGameDataModel.ReduceTechStack(techId, owner.BuildingInstanceId, 1))
                continue;

            globalBuffManager.UnregisterTechEffects(techId, owner.OwnerFactionId, owner.BuildingInstanceId);
            rolledBack++;
        }

        if (rolledBack > 0)
            globalBuffManager.ClearBuildingRuntimeTechState(owner.BuildingInstanceId, owner.OwnerFactionId);

        return rolledBack;
    }

    private bool HasUpgradeTechCandidates(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null || owner.BuildingData.UpgradeTechIDs == null)
            return false;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.BuildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return false;

        if (BuildingDataModel.GetBuildingData(upgradeBuildingId) == null)
            return false;

        for (int i = 0; i < owner.BuildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.BuildingData.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (TechDataModel.GetTechData(techId) != null)
                return true;
        }

        return false;
    }

    private bool HasResearchTechCandidates(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null || owner.BuildingData.UpgradeTechIDs == null)
            return false;

        for (int i = 0; i < owner.BuildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.BuildingData.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (TechDataModel.GetTechData(techId) != null)
                return true;
        }

        return false;
    }

    private bool HasInfoInteraction(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null)
            return false;

        BuildingData data = owner.BuildingData;
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

    private static bool HasReachedResearchLimit(IBuildingLogicContext owner)
    {
        return CountResearchedTech(owner) >= GetResearchLimit(owner);
    }

    private static int GetResearchLimit(IBuildingLogicContext owner)
    {
        if (owner?.BuildingData == null || owner.BuildingData.Type != BuilType.Tech)
            return 1;

        return Math.Max(1, 1 + LevelTagRuntime.GetExtraTechResearchCountPerBuilding());
    }

    private static int CountResearchedTech(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null || string.IsNullOrWhiteSpace(owner.BuildingInstanceId))
            return 0;

        if (owner.BuildingData.UpgradeTechIDs == null)
            return 0;

        int count = 0;
        for (int i = 0; i < owner.BuildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.BuildingData.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId))
                count++;
        }

        return count;
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

    private static bool CanPlayerOperateInBuildPhase(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null || owner.OwnerFactionId != 0)
            return false;

        if (!InGameDataModel.HasActiveModel)
            throw new InvalidOperationException("TechManager requires an active InGameDataModel.");
        return InGameDataModel.IsBuildPhase(LogicPhaseCommandService.GetRequiredCurrentPhase());
    }

    private BuildManager RequireBuildManager()
    {
        return m_BuildManager
               ?? throw new InvalidOperationException("TechManager runtime dependencies were not prepared.");
    }

    private GlobalBuffManager RequireGlobalBuffManager()
    {
        return m_GlobalBuffManager
               ?? throw new InvalidOperationException("TechManager runtime dependencies were not prepared.");
    }

}
