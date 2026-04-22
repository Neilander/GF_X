using GameFramework.Event;
using System;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 阶段管理器
/// 处理游戏阶段切换逻辑
/// </summary>
public class PhaseManager : GameFrameworkComponent
{
    public static event Action<GamePhase, GamePhase> OnPhaseChanged;
    private const float EnemyPresetClusterRadius = 3f;
    private const float EnemyPresetClusterMinDistance = 1.2f;

    /// <summary>
    /// 获取当前游戏阶段
    /// </summary>
    public static GamePhase CurrentPhase
    {
        get
        {
            return (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        }
    }

    /// <summary>
    /// 游戏开始时，按当前阶段执行一次进入逻辑。
    /// </summary>
    public static void EnterCurrentPhaseOnGameStart()
    {
        GamePhase currentPhase = CurrentPhase;
        switch (currentPhase)
        {
            case GamePhase.Build:
                HandleEnterBuildPhase(true);
                break;
            case GamePhase.Invade:
                HandleEnterInvadePhase();
                break;
            case GamePhase.Defend:
                HandleEnterDefendPhase();
                break;
        }

        Debug.Log($"Initial phase entered: {currentPhase}");
    }

    /// <summary>
    /// 切换到下一个阶段
    /// </summary>
    public static void SwitchToNextPhase()
    {
        GamePhase currentPhase = CurrentPhase;
        GamePhase nextPhase;

        switch (currentPhase)
        {
            case GamePhase.Build:
                nextPhase = GamePhase.Invade;
                break;
            case GamePhase.Invade:
                nextPhase = GamePhase.Build;
                break;
            case GamePhase.Defend:
                nextPhase = GamePhase.Build;
                break;
            default:
                nextPhase = GamePhase.Build;
                break;
        }

        SwitchToPhase(nextPhase);
    }

    /// <summary>
    /// 切换到指定阶段
    /// </summary>
    public static void SwitchToPhase(GamePhase phase)
    {
        GamePhase oldPhase = CurrentPhase;
        if (oldPhase == phase)
        {
            return;
        }

        TryAdvanceDayOnBuildTransition(oldPhase, phase);

        // 设置新阶段
        InGameDataModel.SetPhase(phase);

        // 处理阶段切换逻辑
        HandlePhaseTransition(oldPhase, phase);

        // 触发阶段切换事件
        OnPhaseChanged?.Invoke(oldPhase, phase);

        Debug.Log($"Phase switched from {oldPhase} to {phase}");
    }

    private static void TryAdvanceDayOnBuildTransition(GamePhase oldPhase, GamePhase newPhase)
    {
        if (newPhase != GamePhase.Build)
        {
            return;
        }

        InGameDataModel.TryModifyValue(IngameValueType.Day, 1);
        int currentDay = InGameDataModel.GetValue(IngameValueType.Day);
        Debug.Log($"Day advanced to {currentDay} when entering {newPhase} from {oldPhase}");
    }

    /// <summary>
    /// 处理阶段切换逻辑
    /// </summary>
    private static void HandlePhaseTransition(GamePhase oldPhase, GamePhase newPhase)
    {
        switch (newPhase)
        {
            case GamePhase.Build:
                HandleEnterBuildPhase();
                break;
            case GamePhase.Invade:
                HandleEnterInvadePhase();
                break;
            case GamePhase.Defend:
                HandleEnterDefendPhase();
                break;
        }
    }

    /// <summary>
    /// 进入建造阶段
    /// </summary>
    private static void HandleEnterBuildPhase(bool isFirstPhase = false)
    {
        // 关闭卡牌界面
        CardSetup cardSetup = GameEntry.GetComponent<CardSetup>();
        if (cardSetup != null)
        {
            cardSetup.CardSystemShutdown();
        }

        // 统一移除 Creature 组内全部 Soldier
        RemoveAllSoldiers();

        RewardManager.HandleEnterBuildPhaseReward(isFirstPhase);
    }

    /// <summary>
    /// 进入进攻阶段
    /// </summary>
    private static void HandleEnterInvadePhase()
    {
        // 打开卡牌界面
        CardSetup cardSetup = GameEntry.GetComponent<CardSetup>();
        if (cardSetup != null)
        {
            cardSetup.CardSystemSetup();
            cardSetup.OpenCardUI();
        }

        // 每个部队建筑生成卡牌
        GenerateCardsFromArmyBuildings();

        // 生成敌方小兵
        SpawnEnemySoldiers();
    }

    /// <summary>
    /// 进入防御阶段
    /// </summary>
    private static void HandleEnterDefendPhase()
    {
        // 防御阶段逻辑
    }

    /// <summary>
    /// 旧隐藏/显示单位逻辑已废弃，统一走 SoldierFactory 的移除入口。
    /// </summary>
    private static void RemoveAllSoldiers()
    {
        SoldierFactory.RemoveAllSoldiersInCreatureGroup();
    }

    /// <summary>
    /// 从部队建筑生成卡牌
    /// </summary>
    private static void GenerateCardsFromArmyBuildings()
    {
        // 获取所有部队建筑
        var ingameData = GF.DataModel.GetOrCreate<InGameDataModel>();
        var cardSetup = GameEntry.GetComponent<CardSetup>();
        if (cardSetup != null)
        {
            foreach (var building in ingameData.Buildings)
            {
                if (building.CurrentStronghold == null)
                    continue;

                if (building.buildingData.Type == BuilType.Army
                    && building.CurrentStronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                {
                    cardSetup.GenerateCardToDeck(building);
                }
            }
        }
    }

    /// <summary>
    /// 生成敌方小兵
    /// </summary>
    private static void SpawnEnemySoldiers()
    {
        var presetPoints = GameObject.FindObjectsOfType<EntityPresetPoint>();
        int spawnedCount = 0;

        foreach (var point in presetPoints)
        {
            if (point == null || point.PointType != EntityPresetPointType.Unit)
            {
                continue;
            }

            var stronghold = LevelEntity.GetStrongholdAtWorldPosition(point.Position);
            if (stronghold == null || stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
            {
                continue;
            }

            if (!UnitTypeHelper.TryParseUnitType(point.Identifier, out var unitType))
            {
                Debug.LogWarning($"Skip unit preset point '{point.name}': invalid identifier '{point.Identifier}'.");
                continue;
            }

            int count = point.UnitSpawnCount;
            if (count <= 0)
            {
                Debug.LogWarning($"Skip unit preset point '{point.name}': UnitSpawnCount={count}.");
                continue;
            }

            bool spawnSuccess = ClusterSpawnSystem.SpawnCluster(
                point.Position,
                count,
                EnemyPresetClusterRadius,
                EnemyPresetClusterMinDistance,
                unitType,
                SideType.EnemySide,
                BrainType.EnemyAI);

            if (spawnSuccess)
            {
                spawnedCount += count;
            }
            else
            {
                Debug.LogWarning($"Cluster spawn failed at preset point '{point.name}', requestedCount={count}.");
            }
        }

        Debug.Log($"Spawned {spawnedCount} enemy soldiers from enemy stronghold unit preset points");
    }
}
