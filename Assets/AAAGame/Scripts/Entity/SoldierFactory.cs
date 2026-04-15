using System;
using System.Collections.Generic;
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
    /// 统一移除士兵入口：使用 HideEntity。
    /// 血条由 HideEntityComplete 事件监听链路自动清理。
    /// </summary>
    public static bool RemoveSoldier(SoldierEntity soldier)
    {
        GF.Entity.HideEntity(soldier.Entity);
        return true;
    }

    /// <summary>
    /// 移除 Creature 组中所有 SoldierEntity（不区分阵营）。
    /// </summary>
    public static void RemoveAllSoldiersInCreatureGroup()
    {
        var creatureGroup = GF.Entity.GetEntityGroup(Const.EntityGroup.Creature.ToString());
        var entities = creatureGroup.GetAllEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i] is Entity entity && entity.Logic is SoldierEntity soldier)
            {
                RemoveSoldier(soldier);
            }
        }
    }

    /// <summary>
    /// 将预设点 Identifier 解析为 UnitType。
    /// </summary>
    public static bool TryParseUnitType(string identifier, out UnitType unitType)
    {
        identifier = string.IsNullOrWhiteSpace(identifier) ? string.Empty : identifier.Trim();
        if (Enum.TryParse(identifier, true, out unitType))
        {
            return true;
        }

        if (Enum.TryParse($"Unit_{identifier}", true, out unitType))
        {
            return true;
        }

        unitType = default;
        return false;
    }

    /// <summary>
    /// 创建士兵单位（异步，通过 OnShowCallback 在实体创建完成后自动添加 Buff）
    /// </summary>
    /// <param name="index">单位类型索引</param>
    /// <param name="position">出生位置</param>
    /// <param name="side">阵营</param>
    /// <param name="brainType">AI类型</param>
    public static int ShowSoldier(UnitType unitType, Vector3 position, SideType side = SideType.PlayerSide, BrainType brainType = BrainType.SoldierAI)
    {
        string prefabName = GetSoldierPrefabName(unitType);
        string characterKey = unitType.ToString();
        Const.EntityGroup entityGroup = unitType == UnitType.Unit_Hero ? Const.EntityGroup.Player : Const.EntityGroup.Creature;

        // 添加初始Buff到StartBuffs列表
        var startBuffs = new System.Collections.Generic.List<BuffData>();
        AddInitialBuffs(startBuffs, unitType);

        // 移除OnShowCallback，因为CreaturePropertyManager在回调执行后才初始化
        // 改为在BuffTestProcedure的OnShowEntitySuccess回调中设置生命值

        return MAEntityFactory.ShowSoldier(prefabName, characterKey, position, side, brainType, entityGroup, startBuffs);
    }

    /// <summary>
    /// 根据index获取预制体名称
    /// </summary>
    private static string GetSoldierPrefabName(UnitType index)
    {
        // 根据不同的UnitType返回对应的预制体名称
        switch (index)
        {
            case UnitType.Unit_Scapegoat:
                return "背锅侠";
            case UnitType.Unit_Courier:
                return "快递员";
            case UnitType.Unit_CanMaker:
                return "易拉罐";
            case UnitType.Unit_Coder:
                return "gujia"; // 码农使用gujia预制体
            case UnitType.Unit_BoneButcher:
                return "gujia"; // 剔骨狂魔使用gujia预制体
            case UnitType.Unit_Brat:
                return "gujia"; // 熊孩子使用gujia预制体
            case UnitType.Unit_LateRider:
                return "gujia"; // 超时骑手使用gujia预制体
            case UnitType.Unit_ColdCarrier:
                return "gujia"; // 冷库搬运工使用gujia预制体
            default:
                return "gujia"; // 默认使用gujia预制体
        }
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

            case UnitType.Unit_Scapegoat:
                buffList.Add(TauntBuffCallback.CreateTaunt(1));
                break;
        }
    }
}