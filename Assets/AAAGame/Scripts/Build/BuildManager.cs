using GameFramework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class BuildManager
{
    private static readonly string BaseMilestoneTechIdPatterns = "Tech_BaseBuilt_{0}_Lv{1}";
    private static readonly KeyValuePair<IngameValueType, int>[] EmptyResourceCosts = Array.Empty<KeyValuePair<IngameValueType, int>>();
    // 默认给前 3 个选项分配交互按键；更多选项仍走鼠标长按触发。
    private static readonly InputKey[] OptionalOptionKeys =
    {
        InputKey.InteractionPrimary,
        InputKey.InteractionSecondary,
        InputKey.InteractionTertiary,
    };

    public static bool HasUpgrade(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return false;

        if (owner.buildingData.Lv == 0)
            return GetLv0ConstructCandidates(owner).Count > 0;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return false;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        return upgradeBuildingData != null && upgradeBuildingData.TechIDs != null && upgradeBuildingData.TechIDs.Length > 0;
    }

    public static void ConfigureUpgradeInteractionOptions(BuildingEntity owner, InteractionHost host)
    {
        if (owner == null || owner.buildingData == null || host == null)
            return;

        if (owner.buildingData.Lv == 0)
        {
            ConfigureLv0ConstructOptions(owner, host);
            return;
        }

        ConfigureUpgradeOptions(owner, host);
    }

    public static bool IsConstructOptionVisible(BuildingEntity owner, string buildBuildingId)
    {
        if (owner == null || owner.buildingData == null)
            return false;

        if (owner.buildingData.Lv != 0)
            return false;

        if (owner.OwnerFactionID != 0)
            return false;

        var inGameData = GF.DataModel.GetDataModel<InGameDataModel>();
        if (inGameData == null || (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) != GamePhase.Build)
            return false;

        if (string.IsNullOrWhiteSpace(buildBuildingId))
            return false;

        var candidates = GetLv0ConstructCandidates(owner);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i].Identifier, buildBuildingId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool IsConstructOptionExecutable(BuildingEntity owner, string buildBuildingId)
    {
        if (!IsConstructOptionVisible(owner, buildBuildingId))
            return false;

        BuildingData target = BuildingDataModel.GetBuildingData(buildBuildingId);
        return target != null && SatisfyBuildCondition(target, owner.OwnerFactionID) && HasBuildCost(buildBuildingId);
    }

    public static bool ConstructBuilding(BuildingEntity owner, string buildBuildingId)
    {
        if (owner == null || !IsConstructOptionExecutable(owner, buildBuildingId))
            return false;

        bool built = BuildBuilding(buildBuildingId, owner.CachedTransform.position, owner.OwnerFactionID, owner.BuildingInstanceId);
        if (built)
            GF.Entity.HideEntity(owner.Entity);

        return built;
    }

    public static bool IsUpgradeOptionVisible(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (owner == null || owner.buildingData == null)
            return false;

        if (owner.OwnerFactionID != 0)
            return false;

        var inGameData = GF.DataModel.GetDataModel<InGameDataModel>();
        if (inGameData == null || (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) != GamePhase.Build)
            return false;

        if (string.IsNullOrWhiteSpace(upgradeBuildingId) || BuildingDataModel.GetBuildingData(upgradeBuildingId) == null)
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData != null && !techData.IsStackable && InGameDataModel.HasUnlockedTech(techId))
            return false;

        return true;
    }

    public static bool IsUpgradeOptionExecutable(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (!IsUpgradeOptionVisible(owner, upgradeBuildingId, techId))
            return false;

        if (!SatisfyUpgradeCondition(owner, upgradeBuildingId, techId))
            return false;

        return HasBuildCost(upgradeBuildingId);
    }

    public static bool HasBuildCost(string buildingId)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return false;

        return InGameDataModel.GetValue(IngameValueType.Coin) >= buildingData.Cost;
    }

    public static KeyValuePair<IngameValueType, int>[] GetBuildingResourceCosts(string buildingId)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null || buildingData.Cost <= 0)
            return EmptyResourceCosts;

        return new[]
        {
            new KeyValuePair<IngameValueType, int>(IngameValueType.Coin, buildingData.Cost)
        };
    }

    public static bool SatisfyUpgradeCondition(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (owner == null)
            return false;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null)
            return false;

        if (!SatisfyTechCondition(techId))
            return false;

        return SatisfyBuildCondition(upgradeBuildingData, owner.OwnerFactionID);
    }

    public static bool UpgradeBuilding(BuildingEntity owner, string upgradeBuildingId, string techId)
    {
        if (!IsUpgradeOptionExecutable(owner, upgradeBuildingId, techId))
            return false;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null)
            return false;

        if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -upgradeBuildingData.Cost, true))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        string techContextKey = owner.BuildingInstanceId;
        InGameDataModel.UnlockTech(techId, techData != null && techData.IsStackable, techContextKey);

        bool built = BuildBuilding(upgradeBuildingId, owner.CachedTransform.position, owner.OwnerFactionID, owner.BuildingInstanceId);
        if (built)
            GF.Entity.HideEntity(owner.Entity);

        return built;
    }

    public static void OnBuildingDemolished(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return;

        if (owner.buildingData.Type == BuilType.Base)
            ReduceBaseMilestoneTechs(owner.buildingData, owner.BuildingInstanceId);
    }

    public static bool BuildBuilding(string buildingId, Vector3 position, int ownerFactionId, string buildingInstanceId = null)
    {
        return BuildBuildingInternal(buildingId, position, ownerFactionId, buildingInstanceId, checkCondition: true, consumeCoins: true);
    }

    // 关卡初始化专用：忽略建造条件与金币消耗。
    public static bool BuildBuildingForLevelInit(string buildingId, Vector3 position, int ownerFactionId, string buildingInstanceId = null)
    {
        return BuildBuildingInternal(buildingId, position, ownerFactionId, buildingInstanceId, checkCondition: false, consumeCoins: false);
    }

    private static bool BuildBuildingInternal(string buildingId, Vector3 position, int ownerFactionId, string buildingInstanceId, bool checkCondition, bool consumeCoins)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return false;

        if (checkCondition && !SatisfyBuildCondition(buildingData, ownerFactionId))
            return false;

        if (consumeCoins)
        {
            if (!HasBuildCost(buildingId))
                return false;

            if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -buildingData.Cost, true))
                return false;
        }

        string resolvedBuildingInstanceId = string.IsNullOrWhiteSpace(buildingInstanceId)
            ? Guid.NewGuid().ToString("N")
            : buildingInstanceId;

        EntityParams buildingParams = EntityParams.Create(position);
        buildingParams.Set(BuildingEntity.P_BuildingData, buildingData);
        buildingParams.Set<VarInt32>(BuildingEntity.P_InitOwnerFactionID, ownerFactionId);
        buildingParams.Set<VarString>(BuildingEntity.P_BuildingInstanceId, resolvedBuildingInstanceId);

        GF.Entity.ShowEntity<BuildingEntity>(buildingData.PrefabPath, Const.EntityGroup.Building, buildingParams);

        GrantBaseMilestoneTechs(buildingData, resolvedBuildingInstanceId);
        return true;
    }

    public static bool SatisfyBuildCondition(BuildingData buildingData, int ownerFactionId)
    {
        if (buildingData == null)
            return false;

        if (ownerFactionId != 0)
            return true;

        int requiredBaseLevel = GetRequiredBaseLevel(buildingData);
        if (requiredBaseLevel <= 0)
            return true;

        return HasArchetypeBaseLevelTech(buildingData.Arche, requiredBaseLevel);
    }

    private static int GetRequiredBaseLevel(BuildingData buildingData)
    {
        if (buildingData == null || buildingData.Type == BuilType.Base)
            return 0;

        return Mathf.Clamp(buildingData.Lv, 1, 3);
    }

    private static bool HasArchetypeBaseLevelTech(Archetype archetype, int requiredBaseLevel)
    {
        for (int lv = requiredBaseLevel; lv <= 3; lv++)
        {
            if (TryResolveBaseMilestoneTechId(archetype, lv, out string techId) && InGameDataModel.HasUnlockedTech(techId))
                return true;
        }

        return false;
    }

    private static void GrantBaseMilestoneTechs(BuildingData buildingData, string buildingInstanceId)
    {
        if (buildingData == null || buildingData.Type != BuilType.Base || string.IsNullOrWhiteSpace(buildingInstanceId))
            return;

        // Lv0 基地不产生 Base 里程碑科技。
        if (buildingData.Lv <= 0)
            return;

        int maxLv = Mathf.Clamp(buildingData.Lv, 1, 3);
        for (int lv = 1; lv <= maxLv; lv++)
        {
            if (TryResolveBaseMilestoneTechId(buildingData.Arche, lv, out string techId))
            {
                InGameDataModel.UnlockTech(techId, true, buildingInstanceId);
            }
        }
    }

    private static void ReduceBaseMilestoneTechs(BuildingData buildingData, string buildingInstanceId)
    {
        if (buildingData == null || buildingData.Type != BuilType.Base || string.IsNullOrWhiteSpace(buildingInstanceId))
            return;

        if (buildingData.Lv <= 0)
            return;

        int maxLv = Mathf.Clamp(buildingData.Lv, 1, 3);
        for (int lv = 1; lv <= maxLv; lv++)
        {
            if (TryResolveBaseMilestoneTechId(buildingData.Arche, lv, out string techId))
            {
                InGameDataModel.ReduceTechStack(techId, buildingInstanceId, 1);
            }
        }
    }

    private static bool TryResolveBaseMilestoneTechId(Archetype archetype, int level, out string resolvedTechId)
    {
        resolvedTechId = null;
        string archeName = archetype.ToString();

        string candidate = string.Format(BaseMilestoneTechIdPatterns, archeName, level);
        if (TechDataModel.GetTechData(candidate) != null)
        {
            resolvedTechId = candidate;
            return true;
        }

        return false;
    }

    private static bool SatisfyTechCondition(string techId)
    {
        if (string.IsNullOrWhiteSpace(techId))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        if (!techData.IsStackable && InGameDataModel.HasUnlockedTech(techId))
            return false;

        return true;
    }

    private static void ConfigureUpgradeOptions(BuildingEntity owner, InteractionHost host)
    {
        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null || upgradeBuildingData.TechIDs == null)
            return;

        int optionIndex = 0;
        for (int i = 0; i < upgradeBuildingData.TechIDs.Length; i++)
        {
            string techId = upgradeBuildingData.TechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            var techData = TechDataModel.GetTechData(techId);
            string displayName = techData != null ? GF.Localization.GetString(techData.NameKey) : LocalizationTextDataModel.GetText("InteractOption_Upgrade");

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

    private static void ConfigureLv0ConstructOptions(BuildingEntity owner, InteractionHost host)
    {
        var candidates = GetLv0ConstructCandidates(owner);
        int optionIndex = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            string displayName = !string.IsNullOrWhiteSpace(candidate.NameKey)
                ? GF.Localization.GetString(candidate.NameKey)
                : candidate.Identifier;

            InteractionParams @params = InteractionParams.Create();
            @params.Set<VarString>("BuildBuildingId", candidate.Identifier);

            if (TryGetOptionalOptionKey(optionIndex, out var optionKey))
                host.AddOption<BuildingConstructInteractionOption>(optionKey, displayName, @params);
            else
                host.AddOption<BuildingConstructInteractionOption>(displayName, @params);

            optionIndex++;
        }
    }

    private static List<BuildingData> GetLv0ConstructCandidates(BuildingEntity owner)
    {
        var results = new List<BuildingData>();
        if (owner == null || owner.buildingData == null || owner.buildingData.Lv != 0)
            return results;

        HashSet<Archetype> unlockedArches = GetPlayerUnlockedBaseArches();
        if (unlockedArches.Count == 0)
            return results;

        foreach (var data in BuildingDataModel.GetAllBuildingData())
        {
            if (data == null)
                continue;
            if (data.Lv != 1)
                continue;
            if (data.Type != owner.buildingData.Type)
                continue;
            if (!unlockedArches.Contains(data.Arche))
                continue;

            results.Add(data);
        }

        results.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
        return results;
    }

    private static HashSet<Archetype> GetPlayerUnlockedBaseArches()
    {
        var arches = new HashSet<Archetype>();
        foreach (Archetype arche in Enum.GetValues(typeof(Archetype)))
        {
            if (arche == Archetype.None)
                continue;

            if (HasArchetypeBaseLevelTech(arche, 1))
                arches.Add(arche);
        }

        return arches;
    }

    private static bool TryGetOptionalOptionKey(int optionIndex, out InputKey key)
    {
        key = default;
        if (optionIndex < 0)
            return false;

        if (OptionalOptionKeys == null || optionIndex >= OptionalOptionKeys.Length)
            return false;

        key = OptionalOptionKeys[optionIndex];
        return true;
    }
}
