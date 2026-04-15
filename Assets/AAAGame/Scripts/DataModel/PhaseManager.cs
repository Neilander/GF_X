using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 阶段管理器
/// 处理游戏阶段切换逻辑
/// </summary>
public class PhaseManager : GameFrameworkComponent
{
    public static event Action<GamePhase, GamePhase> OnPhaseChanged;

    // 跟踪被隐藏的敌人实体
    private static List<int> _hiddenEnemyEntities = new List<int>();
    
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
                            // 记录被隐藏的敌人实体ID
                            if (!_hiddenEnemyEntities.Contains(entity.Id))
                            {
                                _hiddenEnemyEntities.Add(entity.Id);
                            }
                            // 隐藏血条
                            HideHealthBar(entity.Id);
                            // 禁用实体GameObject（而不是隐藏实体）
                            entity.gameObject.SetActive(false);
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
    /// 显示指定实体的血条
    /// </summary>
    private static void ShowHealthBar(int entityId)
    {
        string healthBarName = $"HealthBar_{entityId}";
        GameObject healthBarObj = GameObject.Find(healthBarName);
        
        if (healthBarObj != null)
        {
            // 血条存在，直接激活
            healthBarObj.SetActive(true);
            Debug.Log($"Shown existing health bar for entity {entityId}");
        }
        else
        {
            // 血条不存在，需要重新创建
            var entity = GF.Entity.GetEntity(entityId);
            if (entity != null && entity.Logic is GeneralCreature creature)
            {
                float currentHealth = (float)creature.HealthValue;
                float maxHealth = (float)creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                bool isFriendly = creature.Side == SideType.PlayerSide;
                
                HealthBarComp.Create(entityId, creature.transform, currentHealth, maxHealth, isFriendly);
                Debug.Log($"Created new health bar for entity {entityId}");
            }
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
                // 调用建筑的harvest函数获取资源
                building.Harvest();
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
                    // 这里根据建筑等级生成对应数量的卡牌
                    int cardCount = building.buildingData.Lv >= 1 ? building.buildingData.Lv : 1;
                    for (int i = 0; i < cardCount; i++)
                    {
                        // 调用CardSetup的方法来生成卡牌
                        cardSetup.GenerateCard();
                        Debug.Log($"Army building {building.buildingData.Identifier} generated card {i+1}");
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// 生成敌方小兵
    /// </summary>
    private static void SpawnEnemySoldiers()
    {
        // 先尝试重新显示之前隐藏的敌人
        if (_hiddenEnemyEntities.Count > 0)
        {
            var entityManager = GF.Entity;
            if (entityManager != null)
            {
                // 重新显示所有被隐藏的敌人
                foreach (int entityId in _hiddenEnemyEntities)
                {
                    // 获取实体
                    var entity = entityManager.GetEntity(entityId);
                    if (entity != null)
                    {
                        // 启用实体GameObject
                        entity.gameObject.SetActive(true);
                        // 显示血条
                        ShowHealthBar(entityId);
                    }
                }
                Debug.Log($"Re-showing {_hiddenEnemyEntities.Count} hidden enemy soldiers");
                // 清空隐藏列表
                _hiddenEnemyEntities.Clear();
                return;
            }
        }

        // 如果没有隐藏的敌人，则创建新的敌人
        // 获取所有建筑
        var ingameData = GF.DataModel.GetOrCreate<InGameDataModel>();
        var buildings = ingameData.StrongholdBuildings;
        
        // 筛选出敌方建筑（OwnerFactionID != 0，假设0是玩家方）
        var enemyBuildings = new List<BuildingEntity>();
        foreach (var building in buildings)
        {
            if (building.OwnerFactionID != 0)
            {
                enemyBuildings.Add(building);
            }
        }
        
        // 如果有敌方建筑，基于敌方建筑位置生成敌人
        if (enemyBuildings.Count > 0)
        {
            // 生成敌方小兵
            for (int i = 0; i < 5; i++)
            {
                // 随机选择一个敌方建筑作为生成位置
                BuildingEntity targetBuilding = enemyBuildings[UnityEngine.Random.Range(0, enemyBuildings.Count)];
                
                // 在建筑中心位置生成敌人（小范围随机）
                Vector3 spawnPos = targetBuilding.transform.position + new Vector3(
                    UnityEngine.Random.Range(-1, 1),
                    0,
                    UnityEngine.Random.Range(-1, 1)
                );
                
                SoldierFactory.ShowSoldier(UnitType.Unit_BoneButcher, spawnPos, SideType.EnemySide, BrainType.EnemyAI);
            }
        }
        else
        {
            // 如果没有敌方建筑，使用默认位置生成敌人
            for (int i = 0; i < 5; i++)
            {
                Vector3 spawnPos = new Vector3(
                    UnityEngine.Random.Range(-10, 10),
                    0,
                    UnityEngine.Random.Range(-10, 10)
                );
                SoldierFactory.ShowSoldier(UnitType.Unit_BoneButcher, spawnPos, SideType.EnemySide, BrainType.EnemyAI);
            }
        }
        Debug.Log("Spawning enemy soldiers");
    }
}
