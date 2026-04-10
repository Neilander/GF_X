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
    /// <param name="teamId">队伍ID</param>
    /// <param name="brainType">AI类型</param>
    public static int ShowSoldier(UnitType index, Vector3 position, int teamId = 0, BrainType brainType = BrainType.SoldierAI, int factionId = -1)
    {


        EntityParams paramsData = EntityParams.Create(position: position);
        paramsData.FactionId = factionId;
        paramsData.TeamId = teamId;

        if (paramsData.FactionId >= 0)
            paramsData.TeamId = EntityCombatTeamHelper.ResolveTeamIdByFaction(paramsData.FactionId);

        paramsData.BrainType = brainType;
        paramsData.Index = index.ToString();

        string prefabName = GetSoldierPrefabName(index);

        // 添加初始Buff到StartBuffs列表
        paramsData.StartBuffs = new System.Collections.Generic.List<BuffData>();
        AddInitialBuffs(paramsData.StartBuffs, index);

        // 移除OnShowCallback，因为CreaturePropertyManager在回调执行后才初始化
        // 改为在BuffTestProcedure的OnShowEntitySuccess回调中设置生命值

        int entityId = GF.Entity.ShowEntity<SoldierEntity>(prefabName, index == UnitType.Unit_Hero ? Const.EntityGroup.Player : Const.EntityGroup.Creature, paramsData);
        return entityId;
    }

    /// <summary>
    /// 根据index获取预制体名称
    /// </summary>
    private static string GetSoldierPrefabName(UnitType index)
    {
        // 使用简单的gujia名称，与CharacterTestProcedure保持一致
        return "gujia";
    }

    /// <summary>
    /// 添加初始Buff到列表中
    /// </summary>
    private static void AddInitialBuffs(System.Collections.Generic.List<BuffData> buffList, UnitType index)
    {
        switch (index)
        {
            case UnitType.Unit_Coder:
                buffList.Add(TimedDeathBuff.CreateTimedDeath(35f));
                break;

            case UnitType.Unit_BoneButcher:
                buffList.Add(OnKillHealBuff.CreateOnKillHeal(3f));
                break;
        }
    }
}