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
    private bool m_IsSubscribedLogicTechApplied;
    private bool m_IsSubscribedBuildingOwnership;
    private bool m_IsSubscribedInteractionCommands;
    private TechManager m_TechManager;
    private GlobalBuffManager m_GlobalBuffManager;
    private int m_BuildingRecycleRefundRate;
    private readonly Queue<string> m_PendingPresentationAudio = new Queue<string>();

    public bool HasConstructOption(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null)
            return false;

        if (owner.BuildingData.Lv != 0)
            return false;

        return HasAnyLv0ConstructCandidate(owner, requireUnlockedArche: false);
    }

    public bool IsConstructOptionVisible(IBuildingLogicContext owner, string buildBuildingId)
    {
        if (owner == null || owner.BuildingData == null)
            return false;

        if (owner.BuildingData.Lv != 0)
            return false;

        if (owner.OwnerFactionId != 0)
            return false;

        var inGameData = GF.DataModel.GetDataModel<InGameDataModel>();
        if (inGameData == null || !InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase)))
            return false;

        if (string.IsNullOrWhiteSpace(buildBuildingId))
            return false;

        var target = BuildingDataModel.GetBuildingData(buildBuildingId);
        if (target == null)
            return false;

        if (!TutorialManager.IsConstructOptionAllowed(target))
            return false;

        if (target.Lv != 1 || target.Type != owner.BuildingData.Type)
            return false;

        if (target.Arche == Archetype.None)
            return false;

        if (!GetPlayerUnlockedBaseArches().Contains(target.Arche))
            return false;

        return true;
    }

    public List<BuildingData> GetLv0ConstructCandidates(IBuildingLogicContext owner, bool requireUnlockedArche = true)
    {
        var results = new List<BuildingData>();
        if (owner == null || owner.BuildingData == null || owner.BuildingData.Lv != 0)
            return results;

        HashSet<Archetype> unlockedArches = null;
        if (requireUnlockedArche)
        {
            unlockedArches = GetPlayerUnlockedBaseArches();
            if (unlockedArches.Count == 0)
                return results;
        }

        foreach (var data in GetCachedLv0ConstructCandidates(owner.BuildingData.Type))
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

    private bool HasAnyLv0ConstructCandidate(IBuildingLogicContext owner, bool requireUnlockedArche = true)
    {
        return GetLv0ConstructCandidates(owner, requireUnlockedArche).Count > 0;
    }

    public bool IsConstructOptionExecutable(IBuildingLogicContext owner, string buildBuildingId)
    {
        if (owner != null
            && LogicInteractionCommandService.IsActive
            && LogicInteractionCommandService.HasPendingForTarget(owner.LogicEntityId))
        {
            return false;
        }
        if (!IsConstructOptionVisible(owner, buildBuildingId))
            return false;

        BuildingData target = BuildingDataModel.GetBuildingData(buildBuildingId);
        return target != null && SatisfyBuildCondition(target, owner.OwnerFactionId) && HasBuildCost(buildBuildingId, owner);
    }

    public bool ConstructBuilding(IBuildingLogicContext owner, string buildBuildingId)
    {
        if (owner == null || !IsConstructOptionExecutable(owner, buildBuildingId))
            return false;

        LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.ConstructBuilding,
            owner.LogicEntityId,
            owner.BuildingInstanceId,
            buildBuildingId);
        return true;
    }

    private bool ApplyScheduledConstructBuilding(IBuildingLogicContext owner, string buildBuildingId)
    {
        EnsureInteractionApplyWindow();
        bool built = BuildBuildingInternalFixed(
            buildBuildingId,
            owner.PositionFixed,
            0f,
            owner.BuildingInstanceId,
            checkCondition: true,
            consumeCoins: true,
            currentInteractionFrameLifecycle: true).IsValid;
        if (built)
            m_PendingPresentationAudio.Enqueue("buildNormal");
        if (built)
            LogicEntityLifecycleService.RequestDespawnForCurrentInteractionFrame(owner.LogicEntityId);

        return built;
    }

    public bool HasBuildCost(string buildingId)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return false;

        return HasBuildCost(buildingData, null, EntitySideHelper.PlayerFactionId);
    }

    public bool HasBuildCost(string buildingId, IBuildingLogicContext owner)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return false;

        return HasBuildCost(buildingData, owner?.StrongholdId, owner?.OwnerFactionId ?? EntitySideHelper.PlayerFactionId);
    }

    public int GetBuildingCost(string buildingId, IBuildingLogicContext owner)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        return BuildingCostModifierService.CalculateBuildingCost(
            buildingData,
            owner?.StrongholdId,
            owner?.OwnerFactionId ?? EntitySideHelper.PlayerFactionId);
    }

    public int GetBuildingCost(BuildingData buildingData, IBuildingLogicContext owner)
    {
        return BuildingCostModifierService.CalculateBuildingCost(
            buildingData,
            owner?.StrongholdId,
            owner?.OwnerFactionId ?? EntitySideHelper.PlayerFactionId);
    }

    public KeyValuePair<IngameValueType, int>[] GetBuildingResourceCosts(string buildingId)
    {
        return GetBuildingResourceCosts(buildingId, null);
    }

    public KeyValuePair<IngameValueType, int>[] GetBuildingResourceCosts(string buildingId, IBuildingLogicContext owner)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        int cost = BuildingCostModifierService.CalculateBuildingCost(
            buildingData,
            owner?.StrongholdId,
            owner?.OwnerFactionId ?? EntitySideHelper.PlayerFactionId);
        if (buildingData == null || cost <= 0)
            return null;

        return new[]
        {
            new KeyValuePair<IngameValueType, int>(IngameValueType.Coin, cost)
        };
    }

    public void OnBuildingDemolished(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null)
            return;

        if (owner.BuildingData.Type == BuilType.Base)
        {
            m_BaseMilestoneTechService.ReduceForDemolishedBase(owner.BuildingData, owner.BuildingInstanceId);
            int supplyCapacity = CalculateBaseSupplyCapacity(owner.BuildingData, owner.OwnerFactionId);
            if (supplyCapacity > 0)
                InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, -supplyCapacity, true);

            InvalidateUnlockedArchetypeCache();
        }
    }

    public bool CanRecycleBuilding(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null)
            return false;

        if (owner.OwnerFactionId != EntitySideHelper.PlayerFactionId)
            return false;

        if (owner.BuildingData.Lv <= 0)
            return false;

        if (owner.BuildingData.Type == BuilType.Base)
            return false;

        if (LogicInteractionCommandService.IsActive
            && LogicInteractionCommandService.HasPendingForTarget(owner.LogicEntityId))
        {
            return false;
        }

        return InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase));
    }

    public int CalculateRecycleRefund(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null)
            return 0;

        int spent = InGameDataModel.GetBuildingCostSpent(owner.BuildingInstanceId);
        if (spent <= 0)
            spent = InGameDataModel.CalculateOriginalBuildingCostSum(owner.BuildingData);

        if (spent <= 0)
            return 0;

        int refundRate = m_BuildingRecycleRefundRate;
        refundRate = LevelTagRuntime.ModifyRecycleRefundRate(refundRate);
        if (refundRate <= 0)
            return 0;

        long numerator = (long)spent * refundRate;
        long refund = (numerator + 50L) / 100L;
        return refund > int.MaxValue ? int.MaxValue : (int)refund;
    }

    public bool RecycleBuilding(IBuildingLogicContext owner)
    {
        if (!CanRecycleBuilding(owner))
            return false;

        LogicInteractionCommandService.ScheduleForNextFrame(
            LogicInteractionActionKind.RecycleBuilding,
            owner.LogicEntityId,
            owner.BuildingInstanceId);
        return true;
    }

    private bool ApplyScheduledRecycleBuilding(IBuildingLogicContext owner)
    {
        EnsureInteractionApplyWindow();
        if (!CanRecycleBuildingForApply(owner))
            return false;

        string lv0BuildingId = ResolveLv0BuildingId(owner.BuildingData.Type);
        if (string.IsNullOrWhiteSpace(lv0BuildingId))
            return false;

        FixVector2 position = owner.PositionFixed;
        string buildingInstanceId = owner.BuildingInstanceId;
        int refund = CalculateRecycleRefund(owner);
        LogicEntityId entityId = BuildBuildingInternalFixed(
            lv0BuildingId,
            position,
            0f,
            buildingInstanceId,
            checkCondition: false,
            consumeCoins: false,
            isGameEndConditionBuilding: owner.IsGameEndConditionBuilding,
            isNavigationStaticBaked: owner.IsNavigationStaticBaked,
            currentInteractionFrameLifecycle: true);
        if (!entityId.IsValid)
            return false;

        m_TechManager.RollbackTechsForBuilding(owner);
        m_GlobalBuffManager.ClearBuildingRuntimeTechState(buildingInstanceId, owner.OwnerFactionId);
        OnBuildingDemolished(owner);
        InGameDataModel.ResetBuildingCostSpent(buildingInstanceId);
        LogicEntityLifecycleService.RequestDespawnForCurrentInteractionFrame(owner.LogicEntityId);
        RewardManager.HandleBuildingRecycleReward(
            position,
            refund);
        m_PendingPresentationAudio.Enqueue("buildNormal");

        return true;
    }

    public bool BuildBuildingForTechUpgrade(
        string buildingId,
        FixVector2 position,
        IBuildingLogicContext owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        bool ok = BuildBuildingInternalFixed(
            buildingId,
            position,
            0f,
            owner.BuildingInstanceId,
            checkCondition: true,
            consumeCoins: false,
            isGameEndConditionBuilding: owner.IsGameEndConditionBuilding,
            isNavigationStaticBaked: owner.IsNavigationStaticBaked,
            currentInteractionFrameLifecycle: true).IsValid;
        if (ok)
            m_PendingPresentationAudio.Enqueue("buildImportant");
        return ok;
    }

    // 关卡初始化专用：忽略建造条件与金币消耗。
    public bool BuildBuildingForLevelInit(
        string buildingId,
        Vector3 position,
        string buildingInstanceId = null,
        bool isGameEndConditionBuilding = false,
        int? initialCoinReserves = null,
        bool isNavigationStaticBaked = false,
        int defaultOwnerFactionId = EntitySideHelper.EnemyFactionId)
    {
        return TryBuildBuildingForLevelInit(buildingId, position, out _, buildingInstanceId, isGameEndConditionBuilding, initialCoinReserves, isNavigationStaticBaked, defaultOwnerFactionId);
    }

    // 关卡初始化专用：忽略建造条件与金币消耗，并返回稳定 BuildingInstanceId。
    public bool TryBuildBuildingForLevelInit(
        string buildingId,
        Vector3 position,
        out string resolvedBuildingInstanceId,
        string buildingInstanceId = null,
        bool isGameEndConditionBuilding = false,
        int? initialCoinReserves = null,
        bool isNavigationStaticBaked = false,
        int defaultOwnerFactionId = EntitySideHelper.EnemyFactionId)
    {
        resolvedBuildingInstanceId = string.IsNullOrWhiteSpace(buildingInstanceId)
            ? LogicPersistentIdAllocator.AllocateBuildingInstanceId()
            : buildingInstanceId;

        LogicEntityId entityId = BuildBuildingInternal(
            buildingId,
            position,
            resolvedBuildingInstanceId,
            checkCondition: false,
            consumeCoins: false,
            isGameEndConditionBuilding: isGameEndConditionBuilding,
            initialCoinReserves: initialCoinReserves,
            isNavigationStaticBaked: isNavigationStaticBaked,
            defaultOwnerFactionId: defaultOwnerFactionId);
        if (!entityId.IsValid)
        {
            resolvedBuildingInstanceId = null;
            return false;
        }

        return true;
    }

    public LogicEntityId RestoreBuildingForStageCheckpoint(StageBuildingCheckpoint checkpoint)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));
        BuildingData buildingData = BuildingDataModel.GetBuildingData(checkpoint.BuildingIdentifier)
                                    ?? throw new InvalidOperationException(
                                        $"Stage checkpoint references unknown building '{checkpoint.BuildingIdentifier}'.");

        string resolvedStrongholdId = null;
        LogicStrongholdMap.TryResolveStrongholdId(checkpoint.Position, out resolvedStrongholdId);
        if (!string.Equals(resolvedStrongholdId, checkpoint.StrongholdId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stage checkpoint building stronghold mismatch. instance='{checkpoint.BuildingInstanceId}', expected='{checkpoint.StrongholdId}', actual='{resolvedStrongholdId}'.");
        }

        int quarterTurns = ResolveBuildingQuarterTurns(checkpoint.Forward);
        LogicEntityId entityId = MAEntityFactory.ShowBuildingFixed(
            buildingData,
            checkpoint.Position,
            0f,
            checkpoint.BuildingInstanceId,
            checkpoint.StrongholdId,
            checkpoint.OwnerFactionId,
            quarterTurns,
            checkpoint.IsGameEndConditionBuilding,
            checkpoint.IsNavigationStaticBaked,
            false);
        if (!entityId.IsValid)
            throw new InvalidOperationException($"Failed to restore stage checkpoint building '{checkpoint.BuildingInstanceId}'.");

        ApplyStageCheckpointBuildingProperties(checkpoint);
        return entityId;
    }

    public static void ApplyStageCheckpointBuildingProperties(StageBuildingCheckpoint checkpoint)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));
        BuildingExtraProps props = LogicBuildingExtraPropsStore.TryGet(checkpoint.BuildingInstanceId)
                                   ?? throw new InvalidOperationException(
                                       $"Restored building properties are missing. instance='{checkpoint.BuildingInstanceId}'.");
        props.ArmyForce = checkpoint.ArmyForce;
        props.Production = checkpoint.Production;
        props.DynamicProduction = checkpoint.DynamicProduction;
        props.ProductionCap = checkpoint.ProductionCap;
        props.StoredProduction = checkpoint.StoredProduction;
        props.ProductionTraitFirstDay = checkpoint.ProductionTraitFirstDay;
        props.ConditionCount = checkpoint.ConditionCount;
        props.ProductionType = checkpoint.ProductionType;
    }

    private static int ResolveBuildingQuarterTurns(FixVector2 forward)
    {
        for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
        {
            if (MAEntityFactory.ResolveBuildingForwardFixed(quarterTurns) == forward)
                return quarterTurns;
        }
        throw new InvalidOperationException(
            $"Stage checkpoint building forward is not cardinal. raw=({forward.x.RawValue},{forward.y.RawValue}).");
    }

    private LogicEntityId BuildBuildingInternal(
        string buildingId,
        Vector3 position,
        string buildingInstanceId,
        bool checkCondition,
        bool consumeCoins,
        bool isGameEndConditionBuilding = false,
        int? initialCoinReserves = null,
        bool isNavigationStaticBaked = false,
        bool currentInteractionFrameLifecycle = false,
        int defaultOwnerFactionId = EntitySideHelper.PlayerFactionId)
    {
        return BuildBuildingInternalFixed(
            buildingId,
            new FixVector2((Fix64)position.x, (Fix64)position.z),
            position.y,
            buildingInstanceId,
            checkCondition,
            consumeCoins,
            isGameEndConditionBuilding,
            initialCoinReserves,
            isNavigationStaticBaked,
            currentInteractionFrameLifecycle,
            defaultOwnerFactionId);
    }

    private LogicEntityId BuildBuildingInternalFixed(
        string buildingId,
        FixVector2 position,
        float viewY,
        string buildingInstanceId,
        bool checkCondition,
        bool consumeCoins,
        bool isGameEndConditionBuilding = false,
        int? initialCoinReserves = null,
        bool isNavigationStaticBaked = false,
        bool currentInteractionFrameLifecycle = false,
        int defaultOwnerFactionId = EntitySideHelper.PlayerFactionId)
    {
        BuildingData buildingData = BuildingDataModel.GetBuildingData(buildingId);
        if (buildingData == null)
            return default;

        string strongholdId = null;
        int ownerFactionId = defaultOwnerFactionId;
        if (LogicStrongholdMap.TryResolveStrongholdId(position, out string resolvedStrongholdId))
        {
            strongholdId = resolvedStrongholdId;
            ownerFactionId = LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId);
        }
        else if (defaultOwnerFactionId == EntitySideHelper.EnemyFactionId)
        {
            Log.Info(
                "BuildManager level-init building is outside every stronghold; defaulting to enemy. building={0} raw=({1},{2}).",
                buildingId,
                position.x.RawValue,
                position.y.RawValue);
        }

        if (checkCondition && !SatisfyBuildCondition(buildingData, ownerFactionId))
            return default;

        int consumedCost = 0;
        if (consumeCoins)
        {
            int actualCost = BuildingCostModifierService.CalculateBuildingCost(
                buildingData,
                strongholdId,
                ownerFactionId);
            if (!HasBuildCost(buildingData, strongholdId, ownerFactionId))
                return default;
            consumedCost = actualCost;
        }

        string resolvedBuildingInstanceId = string.IsNullOrWhiteSpace(buildingInstanceId)
            ? LogicPersistentIdAllocator.AllocateBuildingInstanceId()
            : buildingInstanceId;

        int previousBaseLevel = ResolveExistingBaseLevel(buildingData, ownerFactionId, resolvedBuildingInstanceId);

        LogicEntityId entityId = MAEntityFactory.ShowBuildingFixed(
            buildingData,
            position,
            viewY,
            resolvedBuildingInstanceId,
            strongholdId,
            ownerFactionId,
            0,
            isGameEndConditionBuilding,
            isNavigationStaticBaked,
            currentInteractionFrameLifecycle);

        if (entityId.IsValid)
        {
            if (consumeCoins && !InGameDataModel.TryModifyValue(IngameValueType.Coin, -consumedCost, true))
                throw new InvalidOperationException("Build transaction lost its validated coin balance before commit.");

            if (buildingData.Type == BuilType.Prod)
                InGameDataModel.EnsureProductionBuildingCoinReserves(resolvedBuildingInstanceId, initialCoinReserves);

            if (consumeCoins)
                InGameDataModel.RecordBuildingCostSpent(resolvedBuildingInstanceId, consumedCost);

            TryGrantBaseSupplyCapacity(buildingData, ownerFactionId, previousBaseLevel);
        }

        if (entityId.IsValid)
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

        if (buildingData.Arche == Archetype.None)
            return 0;

        return LevelTagRuntime.ModifyRequiredBaseLevel(Mathf.Clamp(buildingData.Lv, 1, 3));
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

    private static bool HasBuildCost(BuildingData buildingData, string strongholdId, int ownerFactionId)
    {
        return buildingData != null
               && InGameDataModel.GetValue(IngameValueType.Coin) >= BuildingCostModifierService.CalculateBuildingCost(
                   buildingData,
                   strongholdId,
                   ownerFactionId);
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

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is IBuildingLogicContext building) || building.BuildingData == null)
                continue;

            if (building.BuildingData.Type != BuilType.Base)
                continue;

            if (!string.Equals(building.BuildingInstanceId, buildingInstanceId, StringComparison.Ordinal))
                continue;

            return Mathf.Max(0, building.BuildingData.Lv);
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
        TrySubscribeInteractionCommands();
        SubscribeLogicTechAppliedEvent();
        TrySubscribeBuildingOwnershipEvent();
    }

    private void Start()
    {
        TrySubscribeInteractionCommands();
        SubscribeLogicTechAppliedEvent();
        TrySubscribeBuildingOwnershipEvent();
    }

    public void UpdatePresentation()
    {
        if (m_PendingPresentationAudio.Count == 0 || AudioManager.Instance == null)
            return;

        while (m_PendingPresentationAudio.Count > 0)
            AudioManager.Instance.Play(m_PendingPresentationAudio.Dequeue());
    }

    public void PrepareRuntimeDependencies()
    {
        m_TechManager = GameEntry.GetComponent<TechManager>()
                        ?? throw new InvalidOperationException("BuildManager requires TechManager during preload.");
        m_GlobalBuffManager = GameEntry.GetComponent<GlobalBuffManager>()
                              ?? throw new InvalidOperationException("BuildManager requires GlobalBuffManager during preload.");
        if (GF.Config == null)
            throw new InvalidOperationException("BuildManager requires initialized game config during preload.");
        m_BuildingRecycleRefundRate = GF.Config.GetInt(BuildingRecycleRefundRateConfigKey, 0);
        TrySubscribeInteractionCommands();
        SubscribeLogicTechAppliedEvent();
        TrySubscribeBuildingOwnershipEvent();
    }

    private void OnDestroy()
    {
        if (m_IsSubscribedInteractionCommands)
            LogicInteractionCommandService.CommandApplying -= OnInteractionCommandApplying;
        if (m_IsSubscribedLogicTechApplied)
            LogicTechEffectCommandService.EffectApplied -= OnLogicTechEffectApplied;
        if (m_IsSubscribedBuildingOwnership)
            LogicBuildingOwnershipEventService.OwnerFactionChanged -= OnLogicBuildingOwnerFactionChanged;

        m_IsSubscribedInteractionCommands = false;
        m_IsSubscribedLogicTechApplied = false;
        m_IsSubscribedBuildingOwnership = false;
        m_PendingPresentationAudio.Clear();
    }

    private void TrySubscribeInteractionCommands()
    {
        if (m_IsSubscribedInteractionCommands)
            return;

        LogicInteractionCommandService.CommandApplying += OnInteractionCommandApplying;
        m_IsSubscribedInteractionCommands = true;
    }

    private void OnInteractionCommandApplying(LogicInteractionCommand command)
    {
        EnsureInteractionApplyWindow();
        if (!EntityRegistry.TryGet(command.TargetEntityId, out IEntityContext context))
        {
            throw new InvalidOperationException(
                $"Interaction command target is not registered. entity={command.TargetEntityId.Value}, sequence={command.Sequence}.");
        }

        if (!context.TryGetLogicBuilding(out IBuildingLogicContext owner))
        {
            throw new InvalidOperationException(
                $"Interaction command target is not a building. entity={command.TargetEntityId.Value}.");
        }
        if (!string.Equals(owner.BuildingInstanceId, command.TargetBuildingInstanceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Interaction command target identity mismatch. entity={command.TargetEntityId.Value}, expected={command.TargetBuildingInstanceId}, actual={owner.BuildingInstanceId}.");
        }

        bool applied;
        switch (command.ActionKind)
        {
            case LogicInteractionActionKind.ConstructBuilding:
                applied = ApplyScheduledConstructBuilding(owner, command.PrimaryId);
                break;
            case LogicInteractionActionKind.UpgradeBuilding:
            {
                applied = m_TechManager.ApplyScheduledUpgradeBuilding(owner, command.PrimaryId, command.SecondaryId);
                break;
            }
            case LogicInteractionActionKind.ResearchTech:
            {
                applied = m_TechManager.ApplyScheduledResearchTech(owner, command.PrimaryId);
                break;
            }
            case LogicInteractionActionKind.RecycleBuilding:
                applied = ApplyScheduledRecycleBuilding(owner);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command.ActionKind), command.ActionKind, "Unknown interaction action kind.");
        }

        if (!applied)
        {
            throw new InvalidOperationException(
                $"Interaction command failed during its effective frame. sequence={command.Sequence}, action={command.ActionKind}, entity={command.TargetEntityId.Value}.");
        }
    }

    private static bool CanRecycleBuildingForApply(IBuildingLogicContext owner)
    {
        if (owner == null || owner.BuildingData == null)
            return false;
        if (owner.OwnerFactionId != EntitySideHelper.PlayerFactionId)
            return false;
        if (owner.BuildingData.Lv <= 0 || owner.BuildingData.Type == BuilType.Base)
            return false;
        return InGameDataModel.IsBuildPhase((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase));
    }

    private static void EnsureInteractionApplyWindow()
    {
        if (!LogicInteractionCommandService.IsApplyingFrame)
            throw new InvalidOperationException("Building interaction mutation requires the logic interaction command apply window.");
    }

    private void OnLogicTechEffectApplied(LogicTechEffectCommand command)
    {
        InvalidateUnlockedArchetypeCache();
    }

    private void OnLogicBuildingOwnerFactionChanged(
        IBuildingLogicContext building,
        int oldFactionId,
        int newFactionId)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        EnsureCapturedBuildingCostRecord(building, oldFactionId, newFactionId);
        ApplyBaseOwnershipEffects(building, oldFactionId, newFactionId);

        if (oldFactionId == EntitySideHelper.PlayerFactionId || newFactionId == EntitySideHelper.PlayerFactionId)
            InvalidateUnlockedArchetypeCache();
    }

    private static void EnsureCapturedBuildingCostRecord(
        IBuildingLogicContext building,
        int oldFactionId,
        int newFactionId)
    {
        if (newFactionId != EntitySideHelper.PlayerFactionId || oldFactionId == EntitySideHelper.PlayerFactionId)
            return;
        if (building.BuildingData == null)
            throw new InvalidOperationException($"Logic building {building.LogicEntityId.Value} has no BuildingData.");

        InGameDataModel.EnsureBuildingCostSpentFromOriginalCosts(building.BuildingInstanceId, building.BuildingData);
    }

    private void ApplyBaseOwnershipEffects(
        IBuildingLogicContext building,
        int oldFactionId,
        int newFactionId)
    {
        if (building.BuildingData == null)
            throw new InvalidOperationException($"Logic building {building.LogicEntityId.Value} has no BuildingData.");

        if (building.BuildingData.Type != BuilType.Base)
            return;
        if (string.IsNullOrWhiteSpace(building.StrongholdId))
            throw new InvalidOperationException($"Base building {building.LogicEntityId.Value} has no stable stronghold id.");

        m_BaseMilestoneTechService.ReduceForDemolishedBase(building.BuildingData, building.BuildingInstanceId);
        m_BaseMilestoneTechService.GrantForBuiltBase(building.BuildingData, building.BuildingInstanceId, newFactionId);

        int supplyDelta = CalculateBaseSupplyCapacity(building.BuildingData, newFactionId)
                        - CalculateBaseSupplyCapacity(building.BuildingData, oldFactionId);
        if (supplyDelta != 0)
            InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, supplyDelta, true);
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

    private void SubscribeLogicTechAppliedEvent()
    {
        if (m_IsSubscribedLogicTechApplied)
            return;

        LogicTechEffectCommandService.EffectApplied += OnLogicTechEffectApplied;
        m_IsSubscribedLogicTechApplied = true;
    }

    private bool TrySubscribeBuildingOwnershipEvent()
    {
        if (m_IsSubscribedBuildingOwnership)
            return true;

        LogicBuildingOwnershipEventService.OwnerFactionChanged += OnLogicBuildingOwnerFactionChanged;
        m_IsSubscribedBuildingOwnership = true;
        return true;
    }

    private void InvalidateUnlockedArchetypeCache()
    {
        m_PlayerUnlockedBaseArchesCache = null;
    }
}
