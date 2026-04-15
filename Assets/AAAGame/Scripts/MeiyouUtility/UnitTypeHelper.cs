using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class UnitTypeHelper
{

    /// <summary>
    /// 根据index获取预制体名称
    /// </summary>
    public static string GetSoldierPrefabName(UnitType index)
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

}
