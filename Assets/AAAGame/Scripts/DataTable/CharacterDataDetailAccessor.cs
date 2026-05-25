using System;

public static class CharacterDataDetailAccessor
{

    public static WeaponData[] GetWeaponDatas(CharacterDataDetail row)
    {
        if (row == null)
        {
            return Array.Empty<WeaponData>();
        }

        WeaponData weapon1 = new WeaponData(
            row.Weapon1Type,
            row.Weapon1Atk,
            row.Weapon1Interval,
            row.Weapon1Range,
            row.Weapon1Speed,
            row.Weapon1WindUp,
            row.Weapon1WindDown,
            row.Weapon1SplashRadius,
            row.Weapon1SplitAngle,
            row.Weapon1SplitDist,
            row.Weapon1ProjectileCount,
            row.Weapon1AmmunitionCapacity,
            row.Weapon1UniqueValues);
        if (row.Weapon2Atk == Fix64.Zero) return new[] { weapon1 };
        WeaponData weapon2 = new WeaponData(
            row.Weapon2Type,
            row.Weapon2Atk,
            row.Weapon2Interval,
            row.Weapon2Range,
            row.Weapon2Speed,
            row.Weapon2WindUp,
            row.Weapon2WindDown,
            row.Weapon2SplashRadius,
            row.Weapon2SplitAngle,
            row.Weapon2SplitDist,
            row.Weapon2ProjectileCount,
            row.Weapon2AmmunitionCapacity,
            row.Weapon2UniqueValues);

        return new[] { weapon1, weapon2 };
    }

    public static WeaponData GetWeaponDataByIndex(CharacterDataDetail row, int weaponIndex)
    {
        WeaponData[] weaponDatas = GetWeaponDatas(row);

        if (weaponIndex < 0 || weaponIndex >= weaponDatas.Length)
        {
            GF.LogError($"请求的武器索引 {weaponIndex} 超出范围，返回默认武器");
            return weaponDatas[0];
        }

        return weaponDatas[weaponIndex];
    }



    public static Fix64 GetMainValue(CharacterDataDetail row, CreatureMainProperty property)
    {
        if (row == null)
        {
            return Fix64.Zero;
        }

        return property switch
        {
            CreatureMainProperty.Def => row.Def,
            CreatureMainProperty.Health => row.Health,
            CreatureMainProperty.Speed => row.Speed,
            CreatureMainProperty.CollisionRadius => GetCollisionRadiusBySize(row.Size),
            CreatureMainProperty.TurnRate => row.TurnRate,
            CreatureMainProperty.Sight => row.Sight,
            _ => Fix64.Zero
        };
    }

    private const string SmallUnitCollisionRadiusKey = "SmallUnitCollisionRadius";
    private const string MediumUnitCollisionRadiusKey = "MediumUnitCollisionRadius";
    private const string LargeUnitCollisionRadiusKey = "LargeUnitCollisionRadius";
    private static Fix64 GetCollisionRadiusBySize(UnitSize size)
    {
        float radius = size switch
        {
            UnitSize.Small => GF.Config.GetFloat(SmallUnitCollisionRadiusKey, 0f),
            UnitSize.Medium => GF.Config.GetFloat(MediumUnitCollisionRadiusKey, 0f),
            UnitSize.Large => GF.Config.GetFloat(LargeUnitCollisionRadiusKey, 0f),
            _ => 0f
        };

        return (Fix64)radius;
    }
}
