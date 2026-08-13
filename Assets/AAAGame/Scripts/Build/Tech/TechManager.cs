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

    public bool IsUpgradeOptionExecutable(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
    {
        if (HasPendingInteraction(owner))
            return false;
        var buildManager = RequireBuildManager();
        return IsUpgradeOptionVisible(owner, upgradeBuildingId, techId)
            && SatisfyUpgradeCondition(owner, upgradeBuildingId, techId)
            && buildManager.HasBuildCost(upgradeBuildingId, owner);
    }

    public bool IsInfoOptionVisible(IBuildingLogicContext owner)
    {
        return CanPlayerOperateInBuildPhase(owner) && HasInfoInteraction(owner);
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

        LogicInteractionCommandService.Submit(
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

        InGameDataModel.RecordBuildingPhaseModification(
            owner.BuildingInstanceId,
            owner.BuildingData.Identifier,
            upgradeCost);
        InGameDataModel.RecordBuildingPhaseTech(owner.BuildingInstanceId, techId);
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

    private bool IsUpgradeOptionExecutableForApply(IBuildingLogicContext owner, string upgradeBuildingId, string techId)
    {
        var buildManager = RequireBuildManager();
        return IsUpgradeOptionVisible(owner, upgradeBuildingId, techId)
               && SatisfyUpgradeCondition(owner, upgradeBuildingId, techId)
               && buildManager.HasBuildCost(upgradeBuildingId, owner);
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

    public int RollbackPhaseTechsForBuilding(IBuildingLogicContext owner)
    {
        if (owner == null || string.IsNullOrWhiteSpace(owner.BuildingInstanceId))
            throw new ArgumentException("Building is required.", nameof(owner));

        IReadOnlyList<string> techIds = InGameDataModel.GetBuildingPhaseTechIds(owner.BuildingInstanceId);
        GlobalBuffManager globalBuffManager = RequireGlobalBuffManager();
        int rolledBack = 0;
        for (int i = techIds.Count - 1; i >= 0; i--)
        {
            string techId = techIds[i];
            if (!InGameDataModel.ReduceTechStack(techId, owner.BuildingInstanceId, 1))
                throw new InvalidOperationException($"Phase tech rollback failed. building={owner.BuildingInstanceId}, tech={techId}.");
            globalBuffManager.UnregisterTechEffects(techId, owner.OwnerFactionId, owner.BuildingInstanceId);
            rolledBack++;
        }
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

    private bool HasInfoInteraction(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null)
            return false;

        return owner.BuildingData.Lv >= 3;
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
