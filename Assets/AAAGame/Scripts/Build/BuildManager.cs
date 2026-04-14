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
            return GetLv0ConstructCandidates(owner, requireUnlockedArche: false).Count > 0;

        if (owner.buildingData.Type == BuilType.Tech)
            return GetResearchTechCandidates(owner).Count > 0;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return false;

        return GetUpgradeTechCandidates(owner).Count > 0;
    }

    public static void ConfigureBuildInteractionOptions(BuildingEntity owner, InteractionHost host)
    {
        if (owner == null || owner.buildingData == null || host == null)
            return;

        if (owner.buildingData.Lv == 0)
        {
            ConfigureLv0ConstructOptions(owner, host);
            return;
        }

        if (owner.buildingData.Type == BuilType.Tech)
        {
            ConfigureTechResearchOptions(owner, host);
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

        bool built = BuildBuilding(buildBuildingId, owner.CachedTransform.position, owner.BuildingInstanceId);
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
        if (techData == null || string.IsNullOrWhiteSpace(techId))
            return false;

        if (techData.IsStackable)
        {
            if (InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId))
                return false;
        }
        else if (InGameDataModel.HasUnlockedTech(techId))
        {
            return false;
        }

        return true;
    }

    public static bool IsResearchOptionVisible(BuildingEntity owner, string techId)
    {
        if (owner == null || owner.buildingData == null)
            return false;

        if (owner.buildingData.Type != BuilType.Tech)
            return false;

        if (owner.OwnerFactionID != 0)
            return false;

        var inGameData = GF.DataModel.GetDataModel<InGameDataModel>();
        if (inGameData == null || (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) != GamePhase.Build)
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null || string.IsNullOrWhiteSpace(techId))
            return false;

        if (techData.IsStackable)
        {
            if (InGameDataModel.HasUnlockedTech(techId, owner.BuildingInstanceId))
                return false;
        }
        else if (InGameDataModel.HasUnlockedTech(techId))
        {
            return false;
        }

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

    public static bool IsResearchOptionExecutable(BuildingEntity owner, string techId)
    {
        if (!IsResearchOptionVisible(owner, techId))
            return false;

        if (!SatisfyTechCondition(techId))
            return false;

        return HasTechCost(techId);
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

    public static KeyValuePair<IngameValueType, int>[] GetTechResourceCosts(string techId)
    {
        var techData = TechDataModel.GetTechData(techId);
        if (techData == null || techData.Cost <= 0)
            return EmptyResourceCosts;

        return new[]
        {
            new KeyValuePair<IngameValueType, int>(IngameValueType.Coin, techData.Cost)
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

        bool built = BuildBuildingInternal(
            upgradeBuildingId,
            owner.CachedTransform.position,
            owner.BuildingInstanceId,
            true,
            false);

        if (!built)
            return false;

        var techData = TechDataModel.GetTechData(techId);
        string techContextKey = owner.BuildingInstanceId;
        InGameDataModel.UnlockTech(techId, techData != null && techData.IsStackable, techContextKey);

        if (built)
            GF.Entity.HideEntity(owner.Entity);

        return built;
    }

    public static bool ResearchTech(BuildingEntity owner, string techId)
    {
        if (!IsResearchOptionExecutable(owner, techId))
            return false;

        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -techData.Cost, true))
            return false;

        return InGameDataModel.UnlockTech(techId, techData.IsStackable, owner.BuildingInstanceId);
    }

    public static void OnBuildingDemolished(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return;

        if (owner.buildingData.Type == BuilType.Base)
            ReduceBaseMilestoneTechs(owner.buildingData, owner.BuildingInstanceId);
    }

    public static bool BuildBuilding(string buildingId, Vector3 position, string buildingInstanceId = null)
    {
        return BuildBuildingInternal(buildingId, position, buildingInstanceId, checkCondition: true, consumeCoins: true);
    }

    // 关卡初始化专用：忽略建造条件与金币消耗。
    public static bool BuildBuildingForLevelInit(string buildingId, Vector3 position, string buildingInstanceId = null)
    {
        return BuildBuildingInternal(buildingId, position, buildingInstanceId, checkCondition: false, consumeCoins: false);
    }

    private static bool BuildBuildingInternal(string buildingId, Vector3 position, string buildingInstanceId, bool checkCondition, bool consumeCoins)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return false;

        int ownerFactionId = ResolveOwnerFactionId(position);

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

        MAEntityFactory.ShowBuilding(buildingData, position, resolvedBuildingInstanceId);

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
        if (archetype == Archetype.None || level <= 0)
            return false;

        string archeName = archetype.ToString();

        string candidate = string.Format(BaseMilestoneTechIdPatterns, archeName, level);
        // Base 里程碑科技按命名约定生效，不强依赖 TechData 配表。
        // 这样关卡预设直接放置高等级基地时，也能稳定补齐对应等级的建造/升级权限。
        resolvedTechId = candidate;
        return true;
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

    private static bool HasTechCost(string techId)
    {
        var techData = TechDataModel.GetTechData(techId);
        if (techData == null)
            return false;

        return InGameDataModel.GetValue(IngameValueType.Coin) >= techData.Cost;
    }

    private static List<string> GetUpgradeTechCandidates(BuildingEntity owner)
    {
        var results = new List<string>();
        if (owner == null || owner.buildingData == null || owner.buildingData.UpgradeTechIDs == null)
            return results;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return results;

        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (!IsUpgradeOptionVisible(owner, upgradeBuildingId, techId))
                continue;

            results.Add(techId);
        }

        return results;
    }

    private static List<string> GetResearchTechCandidates(BuildingEntity owner)
    {
        var results = new List<string>();
        if (owner == null || owner.buildingData == null || owner.buildingData.UpgradeTechIDs == null)
            return results;

        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (!IsResearchOptionVisible(owner, techId))
                continue;

            results.Add(techId);
        }

        return results;
    }

    private static void ConfigureUpgradeOptions(BuildingEntity owner, InteractionHost host)
    {
        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(owner.buildingData.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId))
            return;

        BuildingData upgradeBuildingData = BuildingDataModel.GetBuildingData(upgradeBuildingId);
        if (upgradeBuildingData == null || owner.buildingData.UpgradeTechIDs == null)
            return;

        int optionIndex = 0;
        for (int i = 0; i < owner.buildingData.UpgradeTechIDs.Length; i++)
        {
            string techId = owner.buildingData.UpgradeTechIDs[i];
            if (!IsUpgradeOptionVisible(owner, upgradeBuildingId, techId))
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

    private static void ConfigureTechResearchOptions(BuildingEntity owner, InteractionHost host)
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
            string displayName = techData != null ? GF.Localization.GetString(techData.NameKey) : techId;

            InteractionParams @params = InteractionParams.Create();
            @params.Set<VarString>("TechId", techId);

            if (TryGetOptionalOptionKey(optionIndex, out var optionKey))
                host.AddOption<TechResearchInteractionOption>(optionKey, displayName, @params);
            else
                host.AddOption<TechResearchInteractionOption>(displayName, @params);

            optionIndex++;
        }
    }

    private static void ConfigureLv0ConstructOptions(BuildingEntity owner, InteractionHost host)
    {
        // Lv0 在 host 初始化时先挂载同类型的所有 Lv1 备选，
        // 可见性仍由 IsConstructOptionVisible 动态判断（含科技解锁条件）。
        // 这样即使关卡预设生成顺序是“先 Lv0 再 Base”，后续解锁后也能立刻出现可交互面板。
        var candidates = GetLv0ConstructCandidates(owner, requireUnlockedArche: false);
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

    private static List<BuildingData> GetLv0ConstructCandidates(BuildingEntity owner, bool requireUnlockedArche = true)
    {
        var results = new List<BuildingData>();
        if (owner == null || owner.buildingData == null || owner.buildingData.Lv != 0)
            return results;

        HashSet<Archetype> unlockedArches = null;
        if (requireUnlockedArche)
        {
            unlockedArches = GetPlayerUnlockedBaseArches();
            if (unlockedArches.Count == 0)
                return results;
        }

        foreach (var data in BuildingDataModel.GetAllBuildingData())
        {
            if (data == null)
                continue;
            if (data.Lv != 1)
                continue;
            if (data.Type != owner.buildingData.Type)
                continue;
            if (requireUnlockedArche && !unlockedArches.Contains(data.Arche))
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

    private static int ResolveOwnerFactionId(Vector3 position)
    {
        var levelEntity = LevelEntity.ActiveLevelEntity;
        if (levelEntity == null)
        {
            return 0;
        }

        var stronghold = levelEntity.GetStrongholdAtWorldPosition(position);
        return stronghold != null ? stronghold.OwnerFactionId : 0;
    }
}
