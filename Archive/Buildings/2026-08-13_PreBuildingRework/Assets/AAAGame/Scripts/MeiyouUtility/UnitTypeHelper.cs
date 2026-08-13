using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class UnitTypeHelper
{
    /// <summary>
    /// 将 Identifier 解析为 UnitType。
    /// </summary>
    public static bool TryParseUnitType(string identifier, out UnitType unitType)
    {
        return TryParseUnitTypeAndLevel(identifier, out unitType, out _);
    }

    /// <summary>
    /// 将 Identifier 解析为 UnitType 和单位等级。支持 Unit_Foo_Lv2 / Foo_Lv2，未写等级默认 1 级。
    /// </summary>
    public static bool TryParseUnitTypeAndLevel(string identifier, out UnitType unitType, out int level)
    {
        identifier = string.IsNullOrWhiteSpace(identifier) ? string.Empty : identifier.Trim();
        level = 1;

        string unitIdentifier = identifier;
        int levelSeparator = identifier.LastIndexOf("_Lv", StringComparison.OrdinalIgnoreCase);
        if (levelSeparator >= 0)
        {
            string rawLevel = identifier.Substring(levelSeparator + 3);
            if (!int.TryParse(rawLevel, out level) || level < 1)
            {
                unitType = default;
                return false;
            }

            if (level > 3)
            {
                level = 3;
            }

            unitIdentifier = identifier.Substring(0, levelSeparator);
        }

        if (Enum.TryParse(unitIdentifier, true, out unitType))
        {
            return true;
        }

        if (Enum.TryParse($"Unit_{unitIdentifier}", true, out unitType))
        {
            return true;
        }

        unitType = default;
        return false;
    }

}
