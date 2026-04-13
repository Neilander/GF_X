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
        
        // 设置新阶段
        InGameDataModel.SetPhase(phase);
        
        // 处理阶段切换逻辑
        HandlePhaseTransition(oldPhase, phase);
        
        // 触发阶段切换事件
        OnPhaseChanged?.Invoke(oldPhase, phase);
        
        Debug.Log($"Phase switched from {oldPhase} to {phase}");
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
    private static void HandleEnterBuildPhase()
    {
        // 关闭卡牌界面
        CardSetup cardSetup = GameEntry.GetComponent<CardSetup>();
        if (cardSetup != null)
        {
            cardSetup.CardSystemShutdown();
        }
        
        // 隐藏双方小兵
        HideAllSoldiers();
        
        // 资源建筑提供收入
        ProvideResourceIncome();
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
    /// 隐藏所有小兵
    /// </summary>
    private static void HideAllSoldiers()
    {
        // 隐藏所有士兵实体
        var entityManager = GF.Entity;
        if (entityManager != null)
        {
            // 遍历所有实体组
            var entityGroups = entityManager.GetAllEntityGroups();
            foreach (var group in entityGroups)
            {
                // 获取该组的所有实体
                var entities = group.GetAllEntities();
                foreach (UnityGameFramework.Runtime.Entity entity in entities)
                {
                    if (entity.Logic is SoldierEntity soldierEntity)
                    {
                        // 只隐藏敌方小兵，不隐藏玩家操控的角色
                        // 假设玩家操控的角色是玩家方的单位
                        if (soldierEntity.Side == SideType.EnemySide)
                        {
                            // 先隐藏血条
                            HideHealthBar(entity.Id);
                            // 再隐藏实体
                            entityManager.HideEntity(entity.Id);
                        }
                    }
                }
            }
        }
        Debug.Log("Hiding all enemy soldiers");
    }

    /// <summary>
    /// 隐藏指定实体的血条
    /// </summary>
    private static void HideHealthBar(int entityId)
    {
        string healthBarName = $"HealthBar_{entityId}";
        GameObject healthBarObj = GameObject.Find(healthBarName);
        if (healthBarObj != null)
        {
            healthBarObj.SetActive(false);
            Debug.Log($"Hidden health bar for entity {entityId}");
        }
    }
    
    /// <summary>
    /// 资源建筑提供收入
    /// </summary>
    private static void ProvideResourceIncome()
    {
        // 获取所有资源建筑
        var ingameData = GF.DataModel.GetOrCreate<InGameDataModel>();
        foreach (var building in ingameData.StrongholdBuildings)
        {
            if (building.buildingData.Type == BuilType.Prod)
            {
                // 每个资源建筑根据production属性提供收入
                int income = building.buildingData.Production;
                InGameDataModel.SetValue(IngameValueType.Coin, 
                    InGameDataModel.GetValue(IngameValueType.Coin) + income, true);
                Debug.Log($"Production building {building.buildingData.Identifier} provided {income} coins");
            }
        }
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
            foreach (var building in ingameData.StrongholdBuildings)
            {
                if (building.buildingData.Type == BuilType.Army)
                {
                    // 每个部队建筑生成对应的卡牌
                    // 这里简化实现，实际应该根据建筑类型生成对应卡牌
                    Debug.Log($"Army building {building.buildingData.Identifier} generated cards");
                }
            }
        }
    }
    
    /// <summary>
    /// 生成敌方小兵
    /// </summary>
    private static void SpawnEnemySoldiers()
    {
        // 生成敌方小兵
        // 这里简化实现，实际应该根据游戏平衡生成合适数量的敌方小兵
        for (int i = 0; i < 5; i++)
        {
            // 在随机位置生成敌方小兵
            Vector3 spawnPos = new Vector3(
                UnityEngine.Random.Range(-10, 10),
                0,
                UnityEngine.Random.Range(-10, 10)
            );
            SoldierFactory.ShowSoldier(UnitType.Unit_BoneButcher, spawnPos, SideType.EnemySide, BrainType.EnemyAI);
        }
        Debug.Log("Spawning enemy soldiers");
    }
}
