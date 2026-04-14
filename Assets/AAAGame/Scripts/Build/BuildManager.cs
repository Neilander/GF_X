using GameFramework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class BuildManager : GameFrameworkComponent
{
    private readonly BaseMilestoneTechService m_BaseMilestoneTechService = new("Tech_BaseBuilt_{0}_Lv{1}");

    // 默认给前 3 个选项分配交互按键；更多选项仍走鼠标长按触发。
    private readonly InputKey[] OptionalOptionKeys =
    {
        InputKey.InteractionPrimary,
        InputKey.InteractionSecondary,
        InputKey.InteractionTertiary,
    };

    public bool HasConstructOption(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return false;

        return owner.buildingData.Lv == 0
            && GetLv0ConstructCandidates(owner, requireUnlockedArche: false).Count > 0;
    }

    public void ConfigureConstructInteractionOptions(BuildingEntity owner, InteractionHost host)
    {
        if (owner == null || owner.buildingData == null || host == null)
            return;

        if (owner.buildingData.Lv != 0)
            return;

        ConfigureLv0ConstructOptions(owner, host);
    }

    public bool IsConstructOptionVisible(BuildingEntity owner, string buildBuildingId)
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

    public bool IsConstructOptionExecutable(BuildingEntity owner, string buildBuildingId)
    {
        if (!IsConstructOptionVisible(owner, buildBuildingId))
            return false;

        BuildingData target = BuildingDataModel.GetBuildingData(buildBuildingId);
        return target != null && SatisfyBuildCondition(target, owner.OwnerFactionID) && HasBuildCost(buildBuildingId);
    }

    public bool ConstructBuilding(BuildingEntity owner, string buildBuildingId)
    {
        if (owner == null || !IsConstructOptionExecutable(owner, buildBuildingId))
            return false;

        bool built = BuildBuilding(buildBuildingId, owner.CachedTransform.position, owner.BuildingInstanceId);
        if (built)
            GF.Entity.HideEntity(owner.Entity);

        return built;
    }

    public bool HasBuildCost(string buildingId)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return false;

        return InGameDataModel.GetValue(IngameValueType.Coin) >= buildingData.Cost;
    }

    public KeyValuePair<IngameValueType, int>[] GetBuildingResourceCosts(string buildingId)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null || buildingData.Cost <= 0)
            return null;

        return new[]
        {
            new KeyValuePair<IngameValueType, int>(IngameValueType.Coin, buildingData.Cost)
        };
    }

    public void OnBuildingDemolished(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return;

        if (owner.buildingData.Type == BuilType.Base)
            m_BaseMilestoneTechService.ReduceForDemolishedBase(owner.buildingData, owner.BuildingInstanceId);
    }

    public bool BuildBuilding(string buildingId, Vector3 position, string buildingInstanceId = null)
    {
        return BuildBuildingInternal(buildingId, position, buildingInstanceId, checkCondition: true, consumeCoins: true);
    }

    public bool BuildBuildingForTechUpgrade(string buildingId, Vector3 position, string buildingInstanceId)
    {
        return BuildBuildingInternal(buildingId, position, buildingInstanceId, checkCondition: true, consumeCoins: false);
    }

    // 关卡初始化专用：忽略建造条件与金币消耗。
    public bool BuildBuildingForLevelInit(string buildingId, Vector3 position, string buildingInstanceId = null)
    {
        return BuildBuildingInternal(buildingId, position, buildingInstanceId, checkCondition: false, consumeCoins: false);
    }

    private bool BuildBuildingInternal(string buildingId, Vector3 position, string buildingInstanceId, bool checkCondition, bool consumeCoins)
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

        m_BaseMilestoneTechService.GrantForBuiltBase(buildingData, resolvedBuildingInstanceId);
        return true;
    }

    public bool SatisfyBuildCondition(BuildingData buildingData, int ownerFactionId)
    {
        if (buildingData == null)
            return false;

        if (ownerFactionId != 0)
            return true;

        int requiredBaseLevel = GetRequiredBaseLevel(buildingData);
        if (requiredBaseLevel <= 0)
            return true;

        return m_BaseMilestoneTechService.HasArchetypeBaseLevelTech(buildingData.Arche, requiredBaseLevel);
    }

    private int GetRequiredBaseLevel(BuildingData buildingData)
    {
        if (buildingData == null || buildingData.Type == BuilType.Base)
            return 0;

        return Mathf.Clamp(buildingData.Lv, 1, 3);
    }

    private void ConfigureLv0ConstructOptions(BuildingEntity owner, InteractionHost host)
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

    private List<BuildingData> GetLv0ConstructCandidates(BuildingEntity owner, bool requireUnlockedArche = true)
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

    private HashSet<Archetype> GetPlayerUnlockedBaseArches()
    {
        var arches = new HashSet<Archetype>();
        foreach (Archetype arche in Enum.GetValues(typeof(Archetype)))
        {
            if (arche == Archetype.None)
                continue;

            if (m_BaseMilestoneTechService.HasArchetypeBaseLevelTech(arche, 1))
                arches.Add(arche);
        }

        return arches;
    }

    private bool TryGetOptionalOptionKey(int optionIndex, out InputKey key)
    {
        key = default;
        if (optionIndex < 0)
            return false;

        if (OptionalOptionKeys == null || optionIndex >= OptionalOptionKeys.Length)
            return false;

        key = OptionalOptionKeys[optionIndex];
        return true;
    }

    private int ResolveOwnerFactionId(Vector3 position)
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
