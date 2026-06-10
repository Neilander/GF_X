using GameFramework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class BuildManager : GameFrameworkComponent
{
    private const string BuildingRecycleRefundRateConfigKey = "BuildingRecycleRefundRate";

    private readonly BaseMilestoneTechService m_BaseMilestoneTechService = new("Tech_BaseBuilt_{0}_Lv{1}");
    private readonly Dictionary<Archetype, List<BuildingData>> m_Lv0ConstructCandidatesByArchetype = new();
    private HashSet<Archetype> m_PlayerUnlockedBaseArchesCache;
    private bool m_IsSubscribedTechUnlocked;
    private bool m_IsSubscribedEntityFactionChanged;

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

        if (owner.buildingData.Lv != 0)
            return false;

        return HasAnyLv0ConstructCandidate(owner, requireUnlockedArche: false);
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
        if (inGameData == null || !InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase)))
            return false;

        if (string.IsNullOrWhiteSpace(buildBuildingId))
            return false;

        var target = BuildingDataModel.GetBuildingData(buildBuildingId);
        if (target == null)
            return false;

        if (target.Lv != 1 || target.Type != owner.buildingData.Type)
            return false;

        if (target.Arche == Archetype.None)
            return false;

        if (!GetPlayerUnlockedBaseArches().Contains(target.Arche))
            return false;

        return true;
    }

    public List<BuildingData> GetLv0ConstructCandidates(BuildingEntity owner, bool requireUnlockedArche = true)
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

        foreach (var data in GetCachedLv0ConstructCandidates(owner.buildingData.Type))
        {
            if (data == null)
                continue;
            if (requireUnlockedArche && !unlockedArches.Contains(data.Arche))
                continue;

            results.Add(data);
        }

        results.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
        return results;
    }

    private bool HasAnyLv0ConstructCandidate(BuildingEntity owner, bool requireUnlockedArche = true)
    {
        return GetLv0ConstructCandidates(owner, requireUnlockedArche).Count > 0;
    }

    public bool IsConstructOptionExecutable(BuildingEntity owner, string buildBuildingId)
    {
        if (!IsConstructOptionVisible(owner, buildBuildingId))
            return false;

        BuildingData target = BuildingDataModel.GetBuildingData(buildBuildingId);
        return target != null && SatisfyBuildCondition(target, owner.OwnerFactionID) && HasBuildCost(buildBuildingId, owner);
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

        return HasBuildCost(buildingData, null);
    }

    public bool HasBuildCost(string buildingId, BuildingEntity owner)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return false;

        return HasBuildCost(buildingData, owner != null ? owner.CurrentStronghold : null);
    }

    public int GetBuildingCost(string buildingId, BuildingEntity owner)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        return GetBuildingCost(buildingData, owner != null ? owner.CurrentStronghold : null);
    }

    public int GetBuildingCost(BuildingData buildingData, Stronghold stronghold)
    {
        return BuildingCostModifierService.CalculateBuildingCost(buildingData, stronghold);
    }

    public KeyValuePair<IngameValueType, int>[] GetBuildingResourceCosts(string buildingId)
    {
        return GetBuildingResourceCosts(buildingId, null);
    }

    public KeyValuePair<IngameValueType, int>[] GetBuildingResourceCosts(string buildingId, BuildingEntity owner)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        int cost = GetBuildingCost(buildingData, owner != null ? owner.CurrentStronghold : null);
        if (buildingData == null || cost <= 0)
            return null;

        return new[]
        {
            new KeyValuePair<IngameValueType, int>(IngameValueType.Coin, cost)
        };
    }

    public void OnBuildingDemolished(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return;

        if (owner.buildingData.Type == BuilType.Base)
        {
            m_BaseMilestoneTechService.ReduceForDemolishedBase(owner.buildingData, owner.BuildingInstanceId);
            int supplyCapacity = CalculateBaseSupplyCapacity(owner.buildingData, owner.OwnerFactionID);
            if (supplyCapacity > 0)
                InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, -supplyCapacity, true);

            InvalidateUnlockedArchetypeCache();
        }
    }

    public bool CanRecycleBuilding(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return false;

        if (owner.OwnerFactionID != EntitySideHelper.PlayerFactionId)
            return false;

        if (owner.buildingData.Lv <= 0)
            return false;

        if (owner.buildingData.Type == BuilType.Base)
            return false;

        return InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase));
    }

    public int CalculateRecycleRefund(BuildingEntity owner)
    {
        if (owner == null || owner.buildingData == null)
            return 0;

        int spent = InGameDataModel.GetBuildingCostSpent(owner.BuildingInstanceId);
        if (spent <= 0)
            spent = InGameDataModel.CalculateOriginalBuildingCostSum(owner.buildingData);

        if (spent <= 0)
            return 0;

        int refundRate = GF.Config != null ? GF.Config.GetInt(BuildingRecycleRefundRateConfigKey, 0) : 0;
        if (refundRate <= 0)
            return 0;

        long numerator = (long)spent * refundRate;
        long refund = (numerator + 50L) / 100L;
        return refund > int.MaxValue ? int.MaxValue : (int)refund;
    }

    public bool RecycleBuilding(BuildingEntity owner)
    {
        if (!CanRecycleBuilding(owner))
            return false;

        string lv0BuildingId = ResolveLv0BuildingId(owner.buildingData.Type);
        if (string.IsNullOrWhiteSpace(lv0BuildingId))
            return false;

        Vector3 position = owner.CachedTransform.position;
        string buildingInstanceId = owner.BuildingInstanceId;
        int refund = CalculateRecycleRefund(owner);

        GameEntry.GetComponent<TechManager>()?.RollbackTechsForBuilding(owner);
        GameEntry.GetComponent<GlobalBuffManager>()?.ClearBuildingRuntimeTechState(buildingInstanceId, owner.OwnerFactionID);
        OnBuildingDemolished(owner);

        int entityId = BuildBuildingInternal(
            lv0BuildingId,
            position,
            buildingInstanceId,
            checkCondition: false,
            consumeCoins: false);
        if (entityId <= 0)
            return false;

        InGameDataModel.ResetBuildingCostSpent(buildingInstanceId);
        GF.Entity.HideEntity(owner.Entity);
        RewardManager.HandleBuildingRecycleReward(position, refund);

        if (AudioManager.Instance != null)
            AudioManager.Instance.Play("buildNormal");

        return true;
    }

    public bool BuildBuilding(string buildingId, Vector3 position, string buildingInstanceId = null)
    {
        bool ok = BuildBuildingInternal(buildingId, position, buildingInstanceId, checkCondition: true, consumeCoins: true) > 0;
        if (ok && AudioManager.Instance != null)
            AudioManager.Instance.Play("buildNormal");
        return ok;
    }

    public bool BuildBuildingForTechUpgrade(string buildingId, Vector3 position, string buildingInstanceId)
    {
        bool ok = BuildBuildingInternal(buildingId, position, buildingInstanceId, checkCondition: true, consumeCoins: false) > 0;
        if (ok && AudioManager.Instance != null)
            AudioManager.Instance.Play("buildImportant");
        return ok;
    }

    // 关卡初始化专用：忽略建造条件与金币消耗。
    public bool BuildBuildingForLevelInit(string buildingId, Vector3 position, string buildingInstanceId = null, bool isGameEndConditionBuilding = false, int? initialCoinReserves = null)
    {
        return TryBuildBuildingForLevelInit(buildingId, position, out _, buildingInstanceId, isGameEndConditionBuilding, initialCoinReserves);
    }

    // 关卡初始化专用：忽略建造条件与金币消耗，并返回稳定 BuildingInstanceId。
    public bool TryBuildBuildingForLevelInit(string buildingId, Vector3 position, out string resolvedBuildingInstanceId, string buildingInstanceId = null, bool isGameEndConditionBuilding = false, int? initialCoinReserves = null)
    {
        resolvedBuildingInstanceId = string.IsNullOrWhiteSpace(buildingInstanceId)
            ? Guid.NewGuid().ToString("N")
            : buildingInstanceId;

        int entityId = BuildBuildingInternal(
            buildingId,
            position,
            resolvedBuildingInstanceId,
            checkCondition: false,
            consumeCoins: false,
            isGameEndConditionBuilding: isGameEndConditionBuilding,
            initialCoinReserves: initialCoinReserves);
        if (entityId <= 0)
        {
            resolvedBuildingInstanceId = null;
            return false;
        }

        return true;
    }

    private int BuildBuildingInternal(string buildingId, Vector3 position, string buildingInstanceId, bool checkCondition, bool consumeCoins, bool isGameEndConditionBuilding = false, int? initialCoinReserves = null)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return 0;

        Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(position);
        int ownerFactionId = stronghold != null ? stronghold.OwnerFactionId : 0;

        if (checkCondition && !SatisfyBuildCondition(buildingData, ownerFactionId))
            return 0;

        int consumedCost = 0;
        if (consumeCoins)
        {
            int actualCost = GetBuildingCost(buildingData, stronghold);
            if (!HasBuildCost(buildingData, stronghold))
                return 0;

            if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, -actualCost, true))
                return 0;

            consumedCost = actualCost;
        }

        string resolvedBuildingInstanceId = string.IsNullOrWhiteSpace(buildingInstanceId)
            ? Guid.NewGuid().ToString("N")
            : buildingInstanceId;

        if (buildingData.Type == BuilType.Prod)
        {
            InGameDataModel.EnsureProductionBuildingCoinReserves(resolvedBuildingInstanceId, initialCoinReserves);
        }

        int previousBaseLevel = ResolveExistingBaseLevel(buildingData, ownerFactionId, resolvedBuildingInstanceId);

        int entityId = MAEntityFactory.ShowBuilding(buildingData, position, resolvedBuildingInstanceId, isGameEndConditionBuilding);

        if (entityId > 0)
        {
            if (consumeCoins)
                InGameDataModel.RecordBuildingCostSpent(resolvedBuildingInstanceId, consumedCost);

            TryGrantBaseSupplyCapacity(buildingData, ownerFactionId, previousBaseLevel);
        }

        m_BaseMilestoneTechService.GrantForBuiltBase(buildingData, resolvedBuildingInstanceId, ownerFactionId);
        return entityId;
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

        return m_BaseMilestoneTechService.HasArchetypeBaseLevelTech(buildingData.Arche, requiredBaseLevel, ownerFactionId);
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
                ? LocalizationTextManager.GetLocalizedText(candidate.NameKey, false)
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

    private HashSet<Archetype> GetPlayerUnlockedBaseArches()
    {
        if (m_PlayerUnlockedBaseArchesCache != null)
            return m_PlayerUnlockedBaseArchesCache;

        var arches = new HashSet<Archetype>();
        foreach (Archetype arche in Enum.GetValues(typeof(Archetype)))
        {
            if (arche == Archetype.None)
                continue;

            if (m_BaseMilestoneTechService.HasArchetypeBaseLevelTech(arche, 1, EntitySideHelper.PlayerFactionId))
                arches.Add(arche);
        }

        m_PlayerUnlockedBaseArchesCache = arches;
        return m_PlayerUnlockedBaseArchesCache;
    }

    private IEnumerable<BuildingData> GetCachedLv0ConstructCandidates(BuilType buildType)
    {
        if (m_Lv0ConstructCandidatesByArchetype.Count == 0)
            BuildLv0ConstructCandidateCache();

        foreach (var pair in m_Lv0ConstructCandidatesByArchetype)
        {
            if (pair.Value == null)
                continue;

            for (int i = 0; i < pair.Value.Count; i++)
            {
                BuildingData data = pair.Value[i];
                if (data != null && data.Type == buildType)
                    yield return data;
            }
        }
    }

    private void BuildLv0ConstructCandidateCache()
    {
        if (m_Lv0ConstructCandidatesByArchetype.Count > 0)
            return;

        var charDataDetailTb = GF.DataTable.GetDataTable<CharacterDataDetail>();

        foreach (var data in BuildingDataModel.GetAllBuildingData())
        {
            if (data == null || data.Lv != 1 || data.Arche == Archetype.None)
                continue;

            // 移除尚未配备 unit prefab 的兵营在建造列表中的展示。
            if (data.Type == BuilType.Army && !string.IsNullOrWhiteSpace(data.UnitID) && charDataDetailTb != null)
            {
                var charRow = charDataDetailTb.GetDataRow(r => r.CharacterKey == data.UnitID);
                if (charRow == null || string.IsNullOrWhiteSpace(charRow.PrefabPath))
                {
                    continue;
                }

                string assetPath = UtilityBuiltin.AssetsPath.GetEntityPath(charRow.PrefabPath);
                if (GF.Resource.HasAsset(assetPath) == GameFramework.Resource.HasAssetResult.NotExist)
                {
                    Log.Warning($"建筑 '{data.Identifier}' 对应的军营单位 '{data.UnitID}' 的 Prefab ({assetPath}) 不存在，已从建造列表中隐藏。");
                    continue;
                }
            }

            if (!m_Lv0ConstructCandidatesByArchetype.TryGetValue(data.Arche, out var list) || list == null)
            {
                list = new List<BuildingData>();
                m_Lv0ConstructCandidatesByArchetype[data.Arche] = list;
            }

            list.Add(data);
        }

        foreach (var list in m_Lv0ConstructCandidatesByArchetype.Values)
        {
            list.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
        }
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
        var stronghold = LevelEntity.GetStrongholdAtWorldPosition(position);
        return stronghold != null ? stronghold.OwnerFactionId : 0;
    }

    private static bool HasBuildCost(BuildingData buildingData, Stronghold stronghold)
    {
        return buildingData != null
               && InGameDataModel.GetValue(IngameValueType.Coin) >= BuildingCostModifierService.CalculateBuildingCost(buildingData, stronghold);
    }

    private static int ResolveExistingBaseLevel(BuildingData targetBuildingData, int ownerFactionId, string buildingInstanceId)
    {
        if (targetBuildingData == null
            || targetBuildingData.Type != BuilType.Base
            || ownerFactionId != EntitySideHelper.PlayerFactionId
            || string.IsNullOrWhiteSpace(buildingInstanceId))
        {
            return 0;
        }

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        if (dataModel == null)
            return 0;

        foreach (var building in dataModel.Buildings)
        {
            if (building == null || building.buildingData == null)
                continue;

            if (building.buildingData.Type != BuilType.Base)
                continue;

            if (!string.Equals(building.BuildingInstanceId, buildingInstanceId, StringComparison.Ordinal))
                continue;

            return Mathf.Max(0, building.buildingData.Lv);
        }

        return 0;
    }

    private static void TryGrantBaseSupplyCapacity(BuildingData targetBuildingData, int ownerFactionId, int previousBaseLevel)
    {
        if (targetBuildingData == null
            || targetBuildingData.Type != BuilType.Base
            || ownerFactionId != EntitySideHelper.PlayerFactionId)
        {
            return;
        }

        int newBaseLevel = Mathf.Max(0, targetBuildingData.Lv);
        int deltaLevel = Mathf.Max(0, newBaseLevel - Mathf.Max(0, previousBaseLevel));
        if (deltaLevel <= 0)
            return;

        int providePerLevel = InGameDataModel.GetBaseProvideSupplyPerLevel();
        if (providePerLevel <= 0)
            return;

        long deltaSupply = (long)deltaLevel * providePerLevel;
        if (deltaSupply > int.MaxValue)
            deltaSupply = int.MaxValue;

        InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, (int)deltaSupply, true);
    }
    protected override void Awake()
    {
        base.Awake();
        TrySubscribeTechUnlockedEvent();
        TrySubscribeEntityFactionChangedEvent();
    }

    private void Start()
    {
        TrySubscribeTechUnlockedEvent();
        TrySubscribeEntityFactionChangedEvent();
    }

    private void Update()
    {
        if (!m_IsSubscribedTechUnlocked)
            TrySubscribeTechUnlockedEvent();
        if (!m_IsSubscribedEntityFactionChanged)
            TrySubscribeEntityFactionChangedEvent();
    }

    private void OnDestroy()
    {
        if (m_IsSubscribedTechUnlocked && GF.Event != null)
            GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        if (m_IsSubscribedEntityFactionChanged && GF.Event != null)
            GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);

        m_IsSubscribedTechUnlocked = false;
        m_IsSubscribedEntityFactionChanged = false;
    }

    private void OnTechUnlocked(object sender, GameFramework.Event.GameEventArgs e)
    {
        InvalidateUnlockedArchetypeCache();
    }

    private void OnEntityFactionChanged(object sender, GameFramework.Event.GameEventArgs e)
    {
        EntityFactionChangedEventArgs args = e as EntityFactionChangedEventArgs;
        if (args == null)
            return;

        EnsureCapturedBuildingCostRecord(args);
        ApplyBaseOwnershipEffects(args);

        if (args.OldFactionId == EntitySideHelper.PlayerFactionId || args.NewFactionId == EntitySideHelper.PlayerFactionId)
            InvalidateUnlockedArchetypeCache();
    }

    private static void EnsureCapturedBuildingCostRecord(EntityFactionChangedEventArgs args)
    {
        if (args.NewFactionId != EntitySideHelper.PlayerFactionId || args.OldFactionId == EntitySideHelper.PlayerFactionId)
            return;

        BuildingEntity building = FindRegisteredBuilding(args);
        if (building == null || building.buildingData == null)
            return;

        InGameDataModel.EnsureBuildingCostSpentFromOriginalCosts(building.BuildingInstanceId, building.buildingData);
    }

    private void ApplyBaseOwnershipEffects(EntityFactionChangedEventArgs args)
    {
        BuildingEntity building = FindRegisteredBuilding(args);
        if (building == null || building.CurrentStronghold == null || building.buildingData == null)
            return;

        if (building.buildingData.Type != BuilType.Base)
            return;

        if (args.OldFactionId == args.NewFactionId)
            return;

        m_BaseMilestoneTechService.ReduceForDemolishedBase(building.buildingData, building.BuildingInstanceId);
        m_BaseMilestoneTechService.GrantForBuiltBase(building.buildingData, building.BuildingInstanceId, args.NewFactionId);

        int supplyDelta = CalculateBaseSupplyCapacity(building.buildingData, args.NewFactionId)
                        - CalculateBaseSupplyCapacity(building.buildingData, args.OldFactionId);
        if (supplyDelta != 0)
            InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, supplyDelta, true);
    }

    private static BuildingEntity FindRegisteredBuilding(EntityFactionChangedEventArgs args)
    {
        var dataModel = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        if (dataModel == null || dataModel.Buildings == null)
            return null;

        foreach (var building in dataModel.Buildings)
        {
            if (building == null)
                continue;

            if (building.Id == args.EntityId)
                return building;

            if (!string.IsNullOrWhiteSpace(args.BuildingInstanceId)
                && string.Equals(building.BuildingInstanceId, args.BuildingInstanceId, StringComparison.Ordinal))
            {
                return building;
            }
        }

        return null;
    }

    private static int CalculateBaseSupplyCapacity(BuildingData buildingData, int ownerFactionId)
    {
        if (buildingData == null
            || buildingData.Type != BuilType.Base
            || ownerFactionId != EntitySideHelper.PlayerFactionId)
        {
            return 0;
        }

        int providePerLevel = InGameDataModel.GetBaseProvideSupplyPerLevel();
        if (providePerLevel <= 0)
            return 0;

        long capacity = (long)Mathf.Max(0, buildingData.Lv) * providePerLevel;
        return capacity > int.MaxValue ? int.MaxValue : (int)capacity;
    }

    private static string ResolveLv0BuildingId(BuilType buildType)
    {
        foreach (BuildingData data in BuildingDataModel.GetAllBuildingData())
        {
            if (data == null || data.Type != buildType || data.Lv != 0)
                continue;

            return data.Identifier;
        }

        return null;
    }

    private bool TrySubscribeTechUnlockedEvent()
    {
        if (m_IsSubscribedTechUnlocked)
            return true;

        if (GF.Event == null)
            return false;

        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        m_IsSubscribedTechUnlocked = true;
        return true;
    }

    private bool TrySubscribeEntityFactionChangedEvent()
    {
        if (m_IsSubscribedEntityFactionChanged)
            return true;

        if (GF.Event == null)
            return false;

        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        m_IsSubscribedEntityFactionChanged = true;
        return true;
    }

    private void InvalidateUnlockedArchetypeCache()
    {
        m_PlayerUnlockedBaseArchesCache = null;
    }
}
