using System;
using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑产出条件统计管理器
/// 处理兵力、建筑、击杀数的动态统计
/// </summary>
public class ProductionConditionManager : GameFrameworkComponent
{
    private readonly Dictionary<string, int> _killCountByStronghold = new Dictionary<string, int>();
    private bool _eventsSubscribed;

    private void Start()
    {
        TrySubscribeEvents();
    }

    private void OnEnable()
    {
        LevelSelectionService.LevelLoadStarted += ClearAllStatistics;
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= ClearAllStatistics;
    }

    private void Update()
    {
        if (!_eventsSubscribed)
        {
            TrySubscribeEvents();
        }
    }

    private void OnDestroy()
    {
        TryUnsubscribeEvents();
    }
    
    /// <summary>
    /// 统计同据点兵力
    /// </summary>
    public int GetTroopCountInStronghold(Stronghold stronghold)
    {
        if (stronghold == null)
        {
            return 0;
        }

        return CalculateActualTroopCount(stronghold);
    }
    
    /// <summary>
    /// 统计同据点同类建筑数量
    /// </summary>
    public int GetBuildingCountInStronghold(Stronghold stronghold, string buildingType)
    {
        if (stronghold == null || string.IsNullOrWhiteSpace(buildingType))
            return 0;

        return CalculateActualBuildingCount(stronghold, buildingType);
    }
    
    /// <summary>
    /// 统计击杀数（按天重置）
    /// </summary>
    public int GetKillCountForStronghold(Stronghold stronghold, int day)
    {
        if (stronghold == null) return 0;
        
        string key = BuildStrongholdDayKey(stronghold, day);
        if (_killCountByStronghold.TryGetValue(key, out int count))
        {
            return count;
        }
        
        return 0;
    }
    
    /// <summary>
    /// 在阶段切换时更新统计
    /// </summary>
    public void OnPhaseChanged(GamePhase newPhase)
    {
        if (InGameDataModel.IsBuildPhase(newPhase))
        {
            // 建造阶段开始时更新所有统计
            UpdateAllProductionConditions();
        }
    }
    
    /// <summary>
    /// 更新所有产出条件统计
    /// </summary>
    private void UpdateAllProductionConditions()
    {
    }
    
    /// <summary>
    /// 计算据点的实际兵力
    /// </summary>
    private int CalculateActualTroopCount(Stronghold stronghold)
    {
        if (stronghold == null || EntityRegistry.AllEntities == null)
            return 0;

        int troopCount = 0;
        SideType strongholdSide = ResolveStrongholdSide(stronghold);

        for (int i = 0; i < EntityRegistry.AllEntities.Count; i++)
        {
            if (EntityRegistry.AllEntities[i] is not MAEntity entity)
                continue;

            if (entity is BuildingEntity)
                continue;

            if (!entity.Alive)
                continue;

            if (entity.Side != strongholdSide)
                continue;

            var entityStronghold = LevelEntity.GetStrongholdAtWorldPosition(entity.Position);
            if (!ReferenceEquals(entityStronghold, stronghold))
                continue;

            troopCount++;
        }

        return troopCount;
    }
    
    /// <summary>
    /// 计算据点的实际建筑数量
    /// </summary>
    private int CalculateActualBuildingCount(Stronghold stronghold, string buildingType)
    {
        if (stronghold == null || string.IsNullOrWhiteSpace(buildingType))
            return 0;

        if (stronghold.Buildings == null || stronghold.Buildings.Count == 0)
            return 0;

        int count = 0;
        for (int i = 0; i < stronghold.Buildings.Count; i++)
        {
            var building = stronghold.Buildings[i];
            if (building == null || building.buildingData == null || !building.Alive || building.IsDisabled)
                continue;

            string identifier = building.buildingData.Identifier;
            if (string.IsNullOrWhiteSpace(identifier))
                continue;

            if (!identifier.StartsWith(buildingType, StringComparison.Ordinal))
                continue;

            count++;
        }

        return count;
    }
    
    /// <summary>
    /// 设置击杀数（由游戏事件触发）
    /// </summary>
    public void SetKillCountForStronghold(Stronghold stronghold, int day, int killCount)
    {
        if (stronghold == null) return;
        
        string key = BuildStrongholdDayKey(stronghold, day);
        _killCountByStronghold[key] = Mathf.Max(0, killCount);
    }
    
    /// <summary>
    /// 清空所有统计
    /// </summary>
    public void ClearAllStatistics()
    {
        _killCountByStronghold.Clear();
    }

    private void TrySubscribeEvents()
    {
        if (_eventsSubscribed || GF.Event == null)
            return;

        GF.Event.Subscribe(SoldierDeadEventArgs.EventId, OnSoldierDead);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        _eventsSubscribed = true;
    }

    private void TryUnsubscribeEvents()
    {
        if (!_eventsSubscribed || GF.Event == null)
            return;

        GF.Event.Unsubscribe(SoldierDeadEventArgs.EventId, OnSoldierDead);
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        _eventsSubscribed = false;
    }

    private void OnIngamePhaseChanged(object sender, GameEventArgs e)
    {
        var args = e as IngamePhaseChangedEventArgs;
        if (args == null)
            return;

        OnPhaseChanged(args.NewPhase);
    }

    private void OnSoldierDead(object sender, GameEventArgs e)
    {
        var args = e as SoldierDeadEventArgs;
        if (args == null)
            return;

        // 肉摊逻辑需要统计"敌方被击杀数"作为次日加成来源。
        if (args.VictimSide != SideType.EnemySide)
            return;

        var stronghold = LevelEntity.GetStrongholdAtWorldPosition(args.WorldPosition);
        if (stronghold == null)
            return;

        int currentDay = Mathf.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
        string key = BuildStrongholdDayKey(stronghold, currentDay);
        _killCountByStronghold.TryGetValue(key, out int currentKills);
        _killCountByStronghold[key] = currentKills + 1;
    }

    private static string BuildStrongholdDayKey(Stronghold stronghold, int day)
    {
        string strongholdId = stronghold?.strongholdData?.StrongholdId ?? "unknown";
        return $"{strongholdId}_{Mathf.Max(1, day)}";
    }

    private static SideType ResolveStrongholdSide(Stronghold stronghold)
    {
        return stronghold != null && stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId
            ? SideType.PlayerSide
            : SideType.EnemySide;
    }
}
