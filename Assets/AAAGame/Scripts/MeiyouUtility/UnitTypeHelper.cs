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
