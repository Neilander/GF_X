using GameFramework;
using GameFramework.Event;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using UnityEngine;
using UnityGameFramework.Runtime;

public enum GamePhase
{
    BuildBeforeInvade,
    BuildBeforeDefend,
    Invade,
    Defend
}

public enum IngameValueType
{
    Phase,
    Day,
    Coin,
    CurrentSupply,
    MaxSupply
}

/// <summary>
/// 关卡数据模型类, 储存运行时关卡数据
/// </summary>
public class InGameDataModel : DataModelBase
{
    private const string InitMaxSupplyConfigKey = "InitMaxSupply";
    private const string BaseProvideSupplyConfigKey = "BaseProvideSupply";
    private const string ResourcePointInitialAmountConfigKey = "ResourcePointInitialAmount";

    public const string P_LevelData = "LevelData";
    public LevelData lvData;
    private Dictionary<IngameValueType, int> m_IngameValue;
    private readonly List<Stronghold> m_Strongholds = new();
    private readonly HashSet<BuildingEntity> m_Buildings = new();
    // 建筑点位橙髓存量：key=BuildingInstanceId。升级/回收沿用同 id，因此存量可跨建筑形态保持。
    private readonly Dictionary<string, int> m_ProductionBuildingCoinReservesByInstanceId = new(StringComparer.Ordinal);
    // 建筑实际建造/升级花费：key=BuildingInstanceId。回收按历史实际花费返钱。
    private readonly Dictionary<string, int> m_BuildingCostSpentByInstanceId = new(StringComparer.Ordinal);
    private bool m_SupplyEventsSubscribed;
    // techId -> 已拥有该科技的建筑实例集合。
    // 全局层数 = 集合 Count；单建筑是否拥有 = 集合 Contains(buildingInstanceId)。
    private readonly Dictionary<string, HashSet<string>> m_TechOwnerContextsById = new();

    public string[] UnlockedTechIds { get; private set; }
    public Dictionary<int, Faction> Factions { get; private set; }
    public IReadOnlyList<Stronghold> Strongholds => m_Strongholds;
    public IReadOnlyCollection<BuildingEntity> Buildings => m_Buildings;
    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        ResetData();
        SubscribeSupplyTrackingEvents();

        lvData = userdata.Get(P_LevelData) as LevelData;
        m_IngameValue[IngameValueType.Phase] = (int)lvData.StartPhase;
        m_IngameValue[IngameValueType.Coin] = Mathf.Max(0, lvData.InitResource + LevelTagRuntime.GetInitialCoinDelta());
        Factions = new Dictionary<int, Faction> { { 0, new Faction(0) }, { 1, new Faction(1) } };   // 通常玩家势力key为0，敌对势力为1、2等。TODO：后续可根据 lvData.StartFactions 来初始化。

        RefreshCurrentSupplyFromFriendlyUnitsInternal(false);
    }

    protected override void OnRelease()
    {
        UnsubscribeSupplyTrackingEvents();
        ResetData();
        base.OnRelease();
    }

    public void ResetData()
    {
        lvData = null;
        int initMaxSupply = GF.Config.GetInt(InitMaxSupplyConfigKey, 0) + LevelTagRuntime.GetInitialMaxSupplyDelta();
        m_IngameValue = new Dictionary<IngameValueType, int>
        {
            [IngameValueType.Phase] = (int)GamePhase.BuildBeforeInvade,
            [IngameValueType.Day] = 1,
            [IngameValueType.Coin] = 0,
            [IngameValueType.CurrentSupply] = 0,
            [IngameValueType.MaxSupply] = Mathf.Max(0, initMaxSupply),
        };

        UnlockedTechIds = new string[0];
        Factions = new Dictionary<int, Faction>();
        m_TechOwnerContextsById.Clear();
        m_Buildings.Clear();
        m_ProductionBuildingCoinReservesByInstanceId.Clear();
        m_BuildingCostSpentByInstanceId.Clear();

        for (int i = 0; i < m_Strongholds.Count; i++)
        {
            if (m_Strongholds[i] != null)
            {
                m_Strongholds[i].Buildings.Clear();
            }
        }

        m_Strongholds.Clear();
    }

    private static InGameDataModel GetModel()
    {
        return GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
    }

    public static int GetValue(IngameValueType type)
    {
        var dataModel = GetModel();
        if (dataModel == null || dataModel.m_IngameValue == null)
            return 0;

        return dataModel.m_IngameValue.TryGetValue(type, out var value) ? value : 0;
    }

    public static void SetValue(IngameValueType type, int value, bool triggerEvent = true)
    {
        var dataModel = GetModel();
        if (dataModel == null)
            return;

        if (dataModel.m_IngameValue == null)
            dataModel.m_IngameValue = new Dictionary<IngameValueType, int>();

        int oldValue = GetValue(type);
        dataModel.m_IngameValue[type] = value;

        if (triggerEvent && oldValue != value)
        {
            if (type == IngameValueType.Phase)
            {
                GF.Event.Fire(
                    dataModel,
                    IngamePhaseChangedEventArgs.Create((GamePhase)oldValue, (GamePhase)value));
            }

            GF.Event.Fire(dataModel, IngameValueChangedEventArgs.Create(type, oldValue, value));
        }
    }

    public static bool TryModifyValue(IngameValueType type, int delta, bool triggerEvent = true)
    {
        if (delta == 0)
            return true;

        int oldValue = GetValue(type);
        long targetValue = (long)oldValue + delta;
        if (targetValue < 0)
            return false;

        if (targetValue > int.MaxValue)
            targetValue = int.MaxValue;

        SetValue(type, (int)targetValue, triggerEvent);
        return true;
    }

    public static void SetPhase(GamePhase phase, bool triggerEvent = true)
    {
        SetValue(IngameValueType.Phase, (int)phase, triggerEvent);
    }

    public static bool IsBuildPhase(GamePhase phase)
    {
        return phase == GamePhase.BuildBeforeInvade || phase == GamePhase.BuildBeforeDefend;
    }

    public static int EnsureProductionBuildingCoinReserves(string buildingInstanceId, int? initialAmount = null)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return 0;

        var dataModel = GetModel();
        if (dataModel == null)
            return 0;

        if (dataModel.m_ProductionBuildingCoinReservesByInstanceId.TryGetValue(buildingInstanceId, out int current))
            return current;

        int defaultValue = GF.Config != null ? GF.Config.GetInt(ResourcePointInitialAmountConfigKey, 0) : 0;
        int resolved = initialAmount.HasValue ? initialAmount.Value : defaultValue;
        resolved = LevelTagRuntime.ModifyResourcePointInitialAmount(resolved);
        resolved = Mathf.Max(0, resolved);

        dataModel.m_ProductionBuildingCoinReservesByInstanceId[buildingInstanceId] = resolved;
        return resolved;
    }

    public static int GetProductionBuildingCoinReserves(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return 0;

        var dataModel = GetModel();
        if (dataModel == null)
            return 0;

        return EnsureProductionBuildingCoinReserves(buildingInstanceId);
    }

    public static int ConsumeProductionBuildingCoinReserves(string buildingInstanceId, int consumeAmount)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId) || consumeAmount <= 0)
            return 0;

        var dataModel = GetModel();
        if (dataModel == null)
            return 0;

        int current = EnsureProductionBuildingCoinReserves(buildingInstanceId);
        int consumed = Mathf.Min(current, consumeAmount);
        dataModel.m_ProductionBuildingCoinReservesByInstanceId[buildingInstanceId] = current - consumed;
        return consumed;
    }

    public static void RecordBuildingCostSpent(string buildingInstanceId, int cost)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId) || cost <= 0)
            return;

        var dataModel = GetModel();
        if (dataModel == null)
            return;

        dataModel.m_BuildingCostSpentByInstanceId.TryGetValue(buildingInstanceId, out int current);
        long total = (long)current + cost;
        dataModel.m_BuildingCostSpentByInstanceId[buildingInstanceId] = total > int.MaxValue ? int.MaxValue : (int)total;
    }

    public static int GetBuildingCostSpent(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return 0;

        var dataModel = GetModel();
        if (dataModel == null)
            return 0;

        return dataModel.m_BuildingCostSpentByInstanceId.TryGetValue(buildingInstanceId, out int cost) ? Mathf.Max(0, cost) : 0;
    }

    public static void EnsureBuildingCostSpentFromOriginalCosts(string buildingInstanceId, BuildingData buildingData)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId) || buildingData == null || buildingData.Lv <= 0)
            return;

        if (GetBuildingCostSpent(buildingInstanceId) > 0)
            return;

        int originalCost = CalculateOriginalBuildingCostSum(buildingData);
        if (originalCost <= 0)
            return;

        var dataModel = GetModel();
        if (dataModel == null)
            return;

        dataModel.m_BuildingCostSpentByInstanceId[buildingInstanceId] = originalCost;
    }

    public static void ResetBuildingCostSpent(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return;

        var dataModel = GetModel();
        if (dataModel == null)
            return;

        dataModel.m_BuildingCostSpentByInstanceId.Remove(buildingInstanceId);
    }

    public static int CalculateOriginalBuildingCostSum(BuildingData buildingData)
    {
        if (buildingData == null || buildingData.Lv <= 0)
            return 0;

        long total = 0;
        for (int lv = 1; lv <= buildingData.Lv; lv++)
        {
            string levelIdentifier = ReplaceBuildingLevel(buildingData.Identifier, lv);
            BuildingData levelData = !string.IsNullOrWhiteSpace(levelIdentifier)
                ? BuildingDataModel.GetBuildingData(levelIdentifier)
                : null;
            if (levelData == null)
                continue;

            total += Mathf.Max(0, levelData.Cost);
            if (total >= int.MaxValue)
                return int.MaxValue;
        }

        return (int)total;
    }

    private static string ReplaceBuildingLevel(string identifier, int lv)
    {
        if (string.IsNullOrWhiteSpace(identifier) || lv <= 0)
            return null;

        int lvIndex = identifier.LastIndexOf("_Lv", StringComparison.Ordinal);
        if (lvIndex < 0)
            return null;

        return identifier.Substring(0, lvIndex + 3) + lv;
    }

    public static int GetCurrentSupply()
    {
        return GetValue(IngameValueType.CurrentSupply);
    }

    public static int GetMaxSupply()
    {
        return GetValue(IngameValueType.MaxSupply);
    }

    public static bool HasEnoughSupplyFor(int requiredSupply)
    {
        if (requiredSupply <= 0)
            return true;

        return GetCurrentSupply() + requiredSupply <= GetMaxSupply();
    }

    public static void RefreshCurrentSupplyFromFriendlyUnits(bool triggerEvent = true)
    {
        var dataModel = GetModel();
        if (dataModel == null)
            return;

        dataModel.RefreshCurrentSupplyFromFriendlyUnitsInternal(triggerEvent);
    }

    public static bool HasUnlockedTech(string techId)
    {
        if (string.IsNullOrWhiteSpace(techId))
            return false;

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        return dataModel.m_TechOwnerContextsById.TryGetValue(techId, out var owners) && owners != null && owners.Count > 0;
    }

    public static bool IsSkillUnlocked(string skillId)
    {
        return SkillRuntimeDataModel.IsUnlocked(skillId);
    }

    public static bool HasUnlockedTech(string techId, string buildingContextKey)
    {
        if (string.IsNullOrWhiteSpace(techId) || string.IsNullOrWhiteSpace(buildingContextKey))
            return false;

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        return dataModel.m_TechOwnerContextsById.TryGetValue(techId, out var owners) && owners.Contains(buildingContextKey);
    }

    public static List<string> GetUnlockedTechIdsForBuilding(string buildingContextKey)
    {
        List<string> results = new();
        if (string.IsNullOrWhiteSpace(buildingContextKey))
            return results;

        var dataModel = GetModel();
        if (dataModel == null)
            return results;

        foreach (var pair in dataModel.m_TechOwnerContextsById)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                continue;

            if (pair.Value.Contains(buildingContextKey))
                results.Add(pair.Key);
        }

        return results;
    }

    public static bool HasUnlockedTech(string techId, int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(techId) || ownerFactionId < 0)
            return false;

        var dataModel = GetModel();
        return dataModel != null && dataModel.HasUnlockedTechInFaction(techId, ownerFactionId);
    }


    public static bool UnlockTech(string techId, bool isStackable, string buildingContextKey, int ownerFactionId = EntitySideHelper.PlayerFactionId)
    {
        return TryScheduleTechUnlock(techId, isStackable, buildingContextKey, ownerFactionId, false);
    }

    public static bool UnlockTechInCurrentInteractionFrame(
        string techId,
        bool isStackable,
        string buildingContextKey,
        int ownerFactionId = EntitySideHelper.PlayerFactionId)
    {
        if (!LogicInteractionCommandService.IsApplyingFrame)
            throw new InvalidOperationException("Current-frame tech unlock requires the interaction command apply window.");

        return TryScheduleTechUnlock(techId, isStackable, buildingContextKey, ownerFactionId, true);
    }

    private static bool TryScheduleTechUnlock(
        string techId,
        bool isStackable,
        string buildingContextKey,
        int ownerFactionId,
        bool currentInteractionFrame)
    {
        if (string.IsNullOrWhiteSpace(techId) || string.IsNullOrWhiteSpace(buildingContextKey))
            return false;

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        dataModel.m_TechOwnerContextsById.TryGetValue(techId, out var owners);

        if (owners != null && owners.Contains(buildingContextKey))
            return false;
        if (LogicTechEffectCommandService.HasPending(techId, buildingContextKey))
            return false;

        // stackable 表示全局可叠加（可由多个建筑实例同时拥有同一 tech）。
        if (!isStackable && ((owners != null && owners.Count > 0) || LogicTechEffectCommandService.HasPending(techId)))
            return false;

        if (currentInteractionFrame)
        {
            LogicTechEffectCommandService.ScheduleForCurrentInteractionFrame(
                techId,
                isStackable,
                ownerFactionId,
                buildingContextKey);
        }
        else
        {
            LogicTechEffectCommandService.ScheduleForNextFrame(
                techId,
                isStackable,
                ownerFactionId,
                buildingContextKey);
        }
        return true;
    }

    public static void ApplyScheduledTechUnlock(LogicTechEffectCommand command)
    {
        if (!LogicTechEffectCommandService.IsApplyingFrame)
            throw new InvalidOperationException("InGameDataModel.ApplyScheduledTechUnlock requires the logic tech command apply window.");

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        if (!dataModel.m_TechOwnerContextsById.TryGetValue(command.TechId, out var owners) || owners == null)
        {
            owners = new HashSet<string>();
            dataModel.m_TechOwnerContextsById[command.TechId] = owners;
        }
        if (owners.Contains(command.SourceBuildingInstanceId))
            throw new InvalidOperationException($"Tech '{command.TechId}' is already owned by '{command.SourceBuildingInstanceId}'.");
        if (owners.Count > 0 && !command.IsStackable)
            throw new InvalidOperationException($"Non-stackable tech '{command.TechId}' already has an owner.");

        owners.Add(command.SourceBuildingInstanceId);

        dataModel.EnsureUnlockedTechIdCached(command.TechId);
    }

    public static bool ReduceTechStack(string techId, string buildingContextKey, int amount = 1)
    {
        if (string.IsNullOrWhiteSpace(techId) || string.IsNullOrWhiteSpace(buildingContextKey) || amount <= 0)
            return false;

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        if (!dataModel.m_TechOwnerContextsById.TryGetValue(techId, out var owners) || owners == null || owners.Count == 0)
            return false;

        bool removed = owners.Remove(buildingContextKey);
        if (!removed)
            return false;

        if (owners.Count == 0)
            dataModel.m_TechOwnerContextsById.Remove(techId);

        dataModel.TryRemoveUnlockedTechIdIfNoContext(techId);
        return true;
    }

    public static int GetUnlockedTechStackCount(string techId, string buildingContextKey)
    {
        if (string.IsNullOrWhiteSpace(techId))
            return 0;

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        return dataModel.m_TechOwnerContextsById.TryGetValue(techId, out var owners) && owners != null ? owners.Count : 0;
    }

    private void EnsureUnlockedTechIdCached(string techId)
    {
        if (UnlockedTechIds == null || Array.IndexOf(UnlockedTechIds, techId) < 0)
        {
            var techIds = new List<string>(UnlockedTechIds ?? Array.Empty<string>()) { techId };
            UnlockedTechIds = techIds.ToArray();
        }
    }

    private void TryRemoveUnlockedTechIdIfNoContext(string techId)
    {
        if (!HasAnyContextTech(techId) || UnlockedTechIds == null)
        {
            if (UnlockedTechIds == null)
                return;

            var techIds = new List<string>(UnlockedTechIds);
            if (techIds.Remove(techId))
                UnlockedTechIds = techIds.ToArray();
        }
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        InGameDataModel dataModel = GetModel()
                                    ?? throw new InvalidOperationException("InGameDataModel deterministic state requires an active model.");
        hasher.Add(0x494E47414D454441UL);
        for (int type = 0; type <= (int)IngameValueType.MaxSupply; type++)
        {
            var valueType = (IngameValueType)type;
            hasher.Add(type);
            hasher.Add(dataModel.m_IngameValue != null && dataModel.m_IngameValue.TryGetValue(valueType, out int value) ? value : 0);
        }

        var techIds = new List<string>(dataModel.m_TechOwnerContextsById.Keys);
        techIds.Sort(StringComparer.Ordinal);
        hasher.Add(techIds.Count);
        for (int i = 0; i < techIds.Count; i++)
        {
            string techId = techIds[i];
            hasher.Add(techId);
            HashSet<string> ownerSet = dataModel.m_TechOwnerContextsById[techId];
            if (ownerSet == null)
                throw new InvalidOperationException($"Tech owner set is null. techId='{techId}'.");
            var owners = new List<string>(ownerSet);
            owners.Sort(StringComparer.Ordinal);
            hasher.Add(owners.Count);
            for (int ownerIndex = 0; ownerIndex < owners.Count; ownerIndex++)
                hasher.Add(owners[ownerIndex]);
        }

        AddSortedStringIntDictionary(hasher, dataModel.m_ProductionBuildingCoinReservesByInstanceId);
        AddSortedStringIntDictionary(hasher, dataModel.m_BuildingCostSpentByInstanceId);
    }

    private static void AddSortedStringIntDictionary(LogicStateHasher hasher, Dictionary<string, int> values)
    {
        var keys = new List<string>(values.Keys);
        keys.Sort(StringComparer.Ordinal);
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            hasher.Add(keys[i]);
            hasher.Add(values[keys[i]]);
        }
    }

    private bool HasAnyContextTech(string techId)
    {
        return !string.IsNullOrWhiteSpace(techId)
               && m_TechOwnerContextsById.TryGetValue(techId, out var owners)
               && owners != null
               && owners.Count > 0;
    }

    private bool HasUnlockedTechInFaction(string techId, int ownerFactionId)
    {
        if (!m_TechOwnerContextsById.TryGetValue(techId, out var owners)
            || owners == null
            || owners.Count == 0)
        {
            return false;
        }

        foreach (var building in m_Buildings)
        {
            if (building == null || building.OwnerFactionID != ownerFactionId)
                continue;

            if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
                continue;

            if (owners.Contains(building.BuildingInstanceId))
                return true;
        }

        return false;
    }

    public static IReadOnlyList<Stronghold> GetStrongholds()
    {
        var dataModel = GetModel();
        return dataModel != null ? dataModel.Strongholds : Array.Empty<Stronghold>();
    }

    public static void SetStrongholds(List<Stronghold> strongholds)
    {
        var dataModel = GetModel();
        if (dataModel == null)
            return;

        dataModel.ClearStrongholdRuntimeDataInternal();

        if (strongholds == null)
            return;

        for (int i = 0; i < strongholds.Count; i++)
        {
            var stronghold = strongholds[i];
            if (stronghold == null)
                continue;

            stronghold.Buildings.Clear();
            dataModel.m_Strongholds.Add(stronghold);
        }
    }

    public static void RegisterBuilding(BuildingEntity building)
    {
        var dataModel = GetModel();
        if (dataModel == null)
            return;

        UnregisterBuilding(building);

        var stronghold = LevelEntity.GetStrongholdAtWorldPosition(building.transform.position);
        building.SetStronghold(stronghold, false);
        if (stronghold != null)
        {
            stronghold.Buildings.Add(building);
        }
        dataModel.m_Buildings.Add(building);
    }

    public static void UnregisterBuilding(BuildingEntity building)
    {
        var dataModel = GetModel();
        if (dataModel == null)
            return;

        var stronghold = building.CurrentStronghold;
        if (stronghold != null)
        {
            stronghold.Buildings.Remove(building);
        }

        building.SetStronghold(null, false);
        dataModel.m_Buildings.Remove(building);
    }

    public static void ClearStrongholdRuntimeData()
    {
        var dataModel = GetModel();
        if (dataModel == null)
            return;

        dataModel.ClearStrongholdRuntimeDataInternal();
    }

    private void ClearStrongholdRuntimeDataInternal()
    {
        m_Buildings.Clear();

        for (int i = 0; i < m_Strongholds.Count; i++)
        {
            if (m_Strongholds[i] != null)
            {
                m_Strongholds[i].Buildings.Clear();
            }
        }

        m_Strongholds.Clear();
    }

    private void SubscribeSupplyTrackingEvents()
    {
        if (m_SupplyEventsSubscribed)
            return;

        EntityRegistry.Changed += OnLogicEntityRegistryChangedForSupply;
        m_SupplyEventsSubscribed = true;
    }

    private void UnsubscribeSupplyTrackingEvents()
    {
        if (!m_SupplyEventsSubscribed)
            return;

        EntityRegistry.Changed -= OnLogicEntityRegistryChangedForSupply;
        m_SupplyEventsSubscribed = false;
    }

    private void OnLogicEntityRegistryChangedForSupply()
    {
        RefreshCurrentSupplyFromFriendlyUnitsInternal(true);
    }

    private void RefreshCurrentSupplyFromFriendlyUnitsInternal(bool triggerEvent)
    {
        int totalSupply = CalculateFriendlyUnitSupply();
        SetValue(IngameValueType.CurrentSupply, totalSupply, triggerEvent);
    }

    private static int CalculateFriendlyUnitSupply()
    {
        if (EntityRegistry.AllEntities == null || EntityRegistry.AllEntities.Count == 0)
            return 0;

        long total = 0;
        for (int i = 0; i < EntityRegistry.AllEntities.Count; i++)
        {
            IEntityContext entity = EntityRegistry.AllEntities[i];
            if (entity == null || entity is IBuildingLogicContext)
                continue;

            if (!entity.Alive || entity.Side != SideType.PlayerSide)
                continue;

            if (!TryGetEntitySupply(entity, out int supply))
                continue;

            total += supply;
            if (total >= int.MaxValue)
                return int.MaxValue;
        }

        return (int)total;
    }

    private static bool TryGetEntitySupply(IEntityContext entity, out int supply)
    {
        supply = 0;
        if (entity == null || entity.CharacterData == null)
            return false;

        supply = Mathf.Max(0, entity.CharacterData.Supply);
        return supply > 0;
    }

    public static int GetBaseProvideSupplyPerLevel()
    {
        return Mathf.Max(0, GF.Config.GetInt(BaseProvideSupplyConfigKey, 0) + LevelTagRuntime.GetBaseProvideSupplyPerLevelDelta());
    }

    public static string GetResourceSprite(IngameValueType resourceType)
    {
        return "UI/IconMisc/Icon_Star_On.png"; //先占位
    }
}
