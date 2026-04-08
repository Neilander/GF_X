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
    Coin
}

/// <summary>
/// 关卡数据模型类, 储存运行时关卡数据
/// </summary>
public class InGameDataModel : DataModelBase
{
    public const string P_StartPhase = "StartPhase";
    public const string P_StartCoins = "StartCoins";
    public const string P_StartFactions = "StartFactions";
    private Dictionary<IngameValueType, int> m_IngameValue;
    // techId -> 已拥有该科技的建筑实例集合。
    // 全局层数 = 集合 Count；单建筑是否拥有 = 集合 Contains(buildingInstanceId)。
    private readonly Dictionary<string, HashSet<string>> m_TechOwnerContextsById = new();

    public string[] UnlockedTechIds { get; private set; }
    public Dictionary<int, Faction> Factions { get; private set; }
    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        ResetData();
        SetPhase((GamePhase)(userdata.Get(P_StartPhase) ?? GamePhase.Build));
        SetValue(IngameValueType.Coin, (int)(userdata.Get(P_StartCoins) ?? 0), true);
        Factions = (userdata.Get(P_StartFactions) as Dictionary<int, Faction>) ?? new Dictionary<int, Faction>();
    }
    public void ResetData()
    {
        m_IngameValue = new Dictionary<IngameValueType, int>
        {
            [IngameValueType.Phase] = (int)GamePhase.Build,
            [IngameValueType.Day] = 1,
            [IngameValueType.Coin] = 0,
        };

        UnlockedTechIds = new string[0];
        Factions = new Dictionary<int, Faction>();
        m_TechOwnerContextsById.Clear();
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
            GF.Event.Fire(dataModel, IngameValueChangedEventArgs.Create(type, oldValue, value));
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


    public static bool UnlockTech(string techId, bool isStackable, string buildingContextKey)
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

        GF.Event.Fire(dataModel, TechUnlockedEventArgs.Create(techId));
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
public static string GetResourceSprite(IngameValueType resourceType)
    {
        return "UI/IconMisc/Icon_Star_On.png"; //先占位
    }
}
