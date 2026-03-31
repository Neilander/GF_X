using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// 士兵工厂类
/// 封装创建不同类型士兵的方法
/// </summary>
public static class SoldierFactory
{
    /// <summary>
    /// 创建士兵单位（异步，通过 OnShowCallback 在实体创建完成后自动添加 Buff）
    /// </summary>
    /// <param name="index">单位类型索引</param>
    /// <param name="position">出生位置</param>
    /// <param name="side">阵营</param>
    /// <param name="brainType">AI类型</param>
    public static int ShowSoldier(string index, Vector3 position, SideType side = SideType.PlayerSide, BrainType brainType = BrainType.SoldierAI)
    {
        EntityParams paramsData = EntityParams.Create(position: position);
        paramsData.Side = side;
        paramsData.BrainType = brainType;
        paramsData.Index = index;
        
        string prefabName = GetSoldierPrefabName(index);

        // 添加初始Buff到StartBuffs列表
        paramsData.StartBuffs = new System.Collections.Generic.List<BuffData>();
        AddInitialBuffs(paramsData.StartBuffs, index);

        return GF.Entity.ShowEntity<SoldierEntity>(prefabName, Const.EntityGroup.Level, paramsData);
    }
    
    /// <summary>
    /// 根据index获取预制体名称
    /// </summary>
    private static string GetSoldierPrefabName(string index)
    {
        switch (index)
        {
            case "coder":
                return "TestCreature";
            case "bone_reaper":
                return "TestCreature";
            default:
                return "TestCreature";
        }
    }
    
    /// <summary>
    /// 添加初始Buff到列表中
    /// </summary>
    private static void AddInitialBuffs(System.Collections.Generic.List<BuffData> buffList, string index)
    {
        switch (index)
        {
            case "coder":
                buffList.Add(TimedDeathBuff.CreateTimedDeath(35f));
                break;
                
            case "bone_reaper":
                buffList.Add(OnKillHealBuff.CreateOnKillHeal(3f));
                break;
        }
    }
}