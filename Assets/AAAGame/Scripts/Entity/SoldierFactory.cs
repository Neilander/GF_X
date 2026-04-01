using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 士兵工厂类
/// 封装创建不同类型士兵的方法
/// </summary>
public static class SoldierFactory
{
    /// <summary>
    /// 创建士兵单位
    /// </summary>
    /// <param name="index">单位类型索引</param>
    /// <param name="position">出生位置</param>
    /// <param name="side">阵营</param>
    /// <param name="brainType">AI类型</param>
    public static SoldierEntity ShowSoldier(string index, Vector3 position, SideType side = SideType.PlayerSide, BrainType brainType = BrainType.SoldierAI)
    {
        EntityParams paramsData = EntityParams.Create(position: position);
        paramsData.Side = side;
        paramsData.BrainType = brainType;
        paramsData.Index = index; // 将index传递给实体
        
        // 根据index选择不同的单位配置
        string prefabName = GetSoldierPrefabName(index);
        
        // 创建实体
        int entityId = GF.Entity.ShowEntity<SoldierEntity>(prefabName, Const.EntityGroup.Level, paramsData);
        Entity entityObj = GF.Entity.GetEntity(entityId);
        SoldierEntity entity = entityObj?.gameObject.GetComponent<SoldierEntity>();
        
        // 添加防御性检查
        if (entity == null)
        {
            GF.LogError($"SoldierFactory: 创建单位失败，index={index}");
            return null;
        }
        
        // 根据单位类型添加初始Buff
        AddInitialBuffs(entity, index);
        
        return entity;
    }
    
    /// <summary>
    /// 根据index获取预制体名称
    /// </summary>
    private static string GetSoldierPrefabName(string index)
    {
        switch (index)
        {
            case "coder": // 码农单位
                return "TestCreature"; // 暂时使用现有的测试模型
            case "bone_reaper": // 剔骨狂魔单位
                return "TestCreature"; // 暂时使用现有的测试模型
            default:
                return "TestCreature"; // 默认测试模型
        }
    }
    
    /// <summary>
    /// 添加初始Buff
    /// </summary>
    private static void AddInitialBuffs(SoldierEntity entity, string index)
    {
        // 添加防御性检查
        if (entity == null)
        {
            GF.LogError($"SoldierFactory.AddInitialBuffs: entity is null, index={index}");
            return;
        }
        
        BuffManager buffManager = entity.GetComponent<BuffManager>();
        if (buffManager == null)
        {
            buffManager = entity.gameObject.AddComponent<BuffManager>();
            buffManager.Initialize(entity);
        }
        
        switch (index)
        {
            case "coder": // 码农单位 - 添加定时死亡Buff
                BuffData timedDeathBuff = TimedDeathBuff.CreateTimedDeath(35f);
                buffManager.AddBuff(timedDeathBuff);
                break;
                
            case "bone_reaper": // 剔骨狂魔单位 - 添加击杀回复Buff
                BuffData onKillHealBuff = OnKillHealBuff.CreateOnKillHeal(3f);
                buffManager.AddBuff(onKillHealBuff);
                break;
        }
    }
}