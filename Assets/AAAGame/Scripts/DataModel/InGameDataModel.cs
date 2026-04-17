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
    Build,
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

    public const string P_LevelData = "LevelData";
    public LevelData lvData;
    private Dictionary<IngameValueType, int> m_IngameValue;
    private readonly List<Stronghold> m_Strongholds = new();
    private readonly HashSet<BuildingEntity> m_Buildings = new();
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
        m_IngameValue[IngameValueType.Coin] = lvData.InitResource;
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
        int initMaxSupply = GF.Config.GetInt(InitMaxSupplyConfigKey, 0);
        m_IngameValue = new Dictionary<IngameValueType, int>
        {
            [IngameValueType.Phase] = (int)GamePhase.Build,
            [IngameValueType.Day] = 1,
            [IngameValueType.Coin] = 0,
            [IngameValueType.CurrentSupply] = 0,
            [IngameValueType.MaxSupply] = Mathf.Max(0, initMaxSupply),
        };

        UnlockedTechIds = new string[0];
        Factions = new Dictionary<int, Faction>();
        m_TechOwnerContextsById.Clear();
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

    public static bool HasUnlockedTech(string techId, string buildingContextKey)
    {
        if (string.IsNullOrWhiteSpace(techId) || string.IsNullOrWhiteSpace(buildingContextKey))
            return false;

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        return dataModel.m_TechOwnerContextsById.TryGetValue(techId, out var owners) && owners.Contains(buildingContextKey);
    }


    public static bool UnlockTech(string techId, bool isStackable, string buildingContextKey, int ownerFactionId = EntitySideHelper.PlayerFactionId)
    {
        if (string.IsNullOrWhiteSpace(techId) || string.IsNullOrWhiteSpace(buildingContextKey))
            return false;

        var dataModel = GF.DataModel.GetDataModel<InGameDataModel>();
        if (!dataModel.m_TechOwnerContextsById.TryGetValue(techId, out var owners) || owners == null)
        {
            owners = new HashSet<string>();
            dataModel.m_TechOwnerContextsById[techId] = owners;
        }

        if (owners.Contains(buildingContextKey))
            return false;

        // stackable 表示全局可叠加（可由多个建筑实例同时拥有同一 tech）。
        if (owners.Count > 0 && !isStackable)
            return false;

        owners.Add(buildingContextKey);

        dataModel.EnsureUnlockedTechIdCached(techId);

        GF.Event.Fire(dataModel, TechUnlockedEventArgs.Create(techId, ownerFactionId, buildingContextKey));
        return true;
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

    private bool HasAnyContextTech(string techId)
    {
        return !string.IsNullOrWhiteSpace(techId)
               && m_TechOwnerContextsById.TryGetValue(techId, out var owners)
               && owners != null
               && owners.Count > 0;
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
        building.SetStronghold(stronghold);
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

        building.SetStronghold(null);
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
        if (m_SupplyEventsSubscribed || GF.Event == null)
            return;

        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccessForSupply);
        GF.Event.Subscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityCompleteForSupply);
        m_SupplyEventsSubscribed = true;
    }

    private void UnsubscribeSupplyTrackingEvents()
    {
        if (!m_SupplyEventsSubscribed || GF.Event == null)
            return;

        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccessForSupply);
        GF.Event.Unsubscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityCompleteForSupply);
        m_SupplyEventsSubscribed = false;
    }

    private void OnShowEntitySuccessForSupply(object sender, GameEventArgs e)
    {
        var args = e as ShowEntitySuccessEventArgs;
        if (args?.Entity?.Logic is not MAEntity)
            return;

        RefreshCurrentSupplyFromFriendlyUnitsInternal(true);
    }

    private void OnHideEntityCompleteForSupply(object sender, GameEventArgs e)
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
            if (EntityRegistry.AllEntities[i] is not MAEntity entity)
                continue;

            if (entity is BuildingEntity)
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

    private static bool TryGetEntitySupply(MAEntity entity, out int supply)
    {
        supply = 0;
        if (entity == null || entity.CharacterData == null)
            return false;

        supply = Mathf.Max(0, entity.CharacterData.Supply);
        return supply > 0;
    }

    public static int GetBaseProvideSupplyPerLevel()
    {
        return Mathf.Max(0, GF.Config.GetInt(BaseProvideSupplyConfigKey, 0));
    }

    public static string GetResourceSprite(IngameValueType resourceType)
    {
        return "UI/IconMisc/Icon_Star_On.png"; //先占位
    }
}
