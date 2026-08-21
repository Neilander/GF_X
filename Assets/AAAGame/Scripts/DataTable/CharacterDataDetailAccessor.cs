using System;

public static class CharacterDataDetailAccessor
{
    public static WeaponData[] GetWeaponDatas(CharacterDataDetail row)
    {
        return GetWeaponDatas(row, 1);
    }

    public static WeaponData[] GetWeaponDatas(CharacterDataDetail row, Fix64 level)
    {
        return GetWeaponDatas(row, NormalizeLevel(level));
    }

    public static WeaponData[] GetWeaponDatas(CharacterDataDetail row, int level)
    {
        if (row == null)
        {
            return Array.Empty<WeaponData>();
        }

        return new[] { GetWeaponData(row, level) };
    }

    public static WeaponData GetWeaponDataByIndex(CharacterDataDetail row, int weaponIndex)
    {
        return GetWeaponDataByIndex(row, weaponIndex, 1);
    }

    public static WeaponData GetWeaponDataByIndex(CharacterDataDetail row, int weaponIndex, Fix64 level)
    {
        return GetWeaponDataByIndex(row, weaponIndex, NormalizeLevel(level));
    }

    public static WeaponData GetWeaponDataByIndex(CharacterDataDetail row, int weaponIndex, int level)
    {
        WeaponData[] weaponDatas = GetWeaponDatas(row, level);

        if (weaponIndex < 0 || weaponIndex >= weaponDatas.Length)
        {
            GF.LogError($"请求的武器索引 {weaponIndex} 超出范围，返回默认武器");
            return weaponDatas[0];
        }

        return weaponDatas[weaponIndex];
    }



    public static Fix64 GetMainValue(CharacterDataDetail row, CreatureMainProperty property)
    {
        return GetMainValue(row, property, 1);
    }

    public static Fix64 GetMainValue(CharacterDataDetail row, CreatureMainProperty property, Fix64 level)
    {
        return GetMainValue(row, property, NormalizeLevel(level));
    }

    public static Fix64 GetMainValue(CharacterDataDetail row, CreatureMainProperty property, int level)
    {
        if (row == null)
        {
            return Fix64.Zero;
        }

        return property switch
        {
            CreatureMainProperty.Def => PickLevelValue(level, row.Lv1Def, row.Lv2Def, row.Lv3Def),
            CreatureMainProperty.Health => PickLevelValue(level, row.Lv1HP, row.Lv2HP, row.Lv3HP),
            CreatureMainProperty.Speed => PickLevelValue(level, row.Lv1MoveSpeed, row.Lv2MoveSpeed, row.Lv3MoveSpeed),
            CreatureMainProperty.CollisionRadius => GetCollisionRadiusBySize(row.Size),
            CreatureMainProperty.TurnRate => row.TurnRate,
            CreatureMainProperty.Sight => row.Sight,
            CreatureMainProperty.StatusResistance => Fix64.Zero,
            CreatureMainProperty.WeightLevel => GetWeightLevelBySize(row.Size),
            _ => Fix64.Zero
        };
    }

    public static int NormalizeLevel(Fix64 level)
    {
        int value = (int)Fix64.Floor(level);
        if (value < 1) return 1;
        if (value > 3) return 3;
        return value;
    }

    private static WeaponData GetWeaponData(CharacterDataDetail row, int level)
    {
        WeaponData lv1 = new WeaponData(
            row.Lv1Weapon1Type,
            row.Lv1Weapon1Atk,
            row.Lv1Weapon1Interval,
            row.Lv1Weapon1Range,
            row.Lv1Weapon1ProjectileSpeed,
            row.Lv1Weapon1WindUp,
            row.Lv1Weapon1WindDown,
            row.Lv1Weapon1SplashRadius,
            row.Lv1Weapon1SplitAngle,
            row.Lv1Weapon1SplitDist,
            row.Lv1Weapon1ProjectileCount,
            row.Lv1Weapon1AmmunitionCapacity,
            row.Lv1Weapon1UniqueValues);

        if (level <= 1)
        {
            return lv1;
        }

        WeaponData lv2 = WithFallback(
            lv1,
            row.Lv2Weapon1Type,
            row.Lv2Weapon1Atk,
            row.Lv2Weapon1Interval,
            row.Lv2Weapon1Range,
            row.Lv2Weapon1ProjectileSpeed,
            row.Lv2Weapon1WindUp,
            row.Lv2Weapon1WindDown,
            row.Lv2Weapon1SplashRadius,
            row.Lv2Weapon1SplitAngle,
            row.Lv2Weapon1SplitDist,
            row.Lv2Weapon1ProjectileCount,
            row.Lv2Weapon1AmmunitionCapacity,
            row.Lv2Weapon1UniqueValues);

        if (level == 2)
        {
            return lv2;
        }

        return WithFallback(
            lv2,
            row.Lv3Weapon1Type,
            row.Lv3Weapon1Atk,
            row.Lv3Weapon1Interval,
            row.Lv3Weapon1Range,
            row.Lv3Weapon1ProjectileSpeed,
            row.Lv3Weapon1WindUp,
            row.Lv3Weapon1WindDown,
            row.Lv3Weapon1SplashRadius,
            row.Lv3Weapon1SplitAngle,
            row.Lv3Weapon1SplitDist,
            row.Lv3Weapon1ProjectileCount,
            row.Lv3Weapon1AmmunitionCapacity,
            row.Lv3Weapon1UniqueValues);
    }

    private static WeaponData WithFallback(
        WeaponData previous,
        WeaponType type,
        Fix64 atk,
        Fix64 interval,
        Fix64 range,
        Fix64 projectileSpeed,
        Fix64 windUp,
        Fix64 windDown,
        Fix64 splashRadius,
        Fix64 splitAngle,
        Fix64 splitDist,
        Fix64 projectileCount,
        Fix64 ammunitionCapacity,
        Fix64[] uniqueValues)
    {
        return new WeaponData(
            type != default ? type : previous.Type,
            PickValue(atk, previous.Atk),
            PickValue(interval, previous.Interval),
            PickValue(range, previous.Range),
            PickValue(projectileSpeed, previous.ProjectileSpeed),
            PickValue(windUp, previous.WindUp),
            PickValue(windDown, previous.WindDown),
            PickValue(splashRadius, previous.SplashRadius),
            PickValue(splitAngle, previous.SplitAngle),
            PickValue(splitDist, previous.SplitDist),
            PickValue(projectileCount, previous.ProjectileCount),
            PickValue(ammunitionCapacity, previous.AmmunitionCapacity),
            uniqueValues != null && uniqueValues.Length > 0 ? uniqueValues : previous.UniqueValues);
    }

    private static Fix64 PickLevelValue(int level, Fix64 lv1, Fix64 lv2, Fix64 lv3)
    {
        if (level <= 1) return lv1;
        if (level == 2) return PickValue(lv2, lv1);

        Fix64 inheritedLv2 = PickValue(lv2, lv1);
        return PickValue(lv3, inheritedLv2);
    }

    private static Fix64 PickValue(Fix64 value, Fix64 fallback)
    {
        return value != Fix64.Zero ? value : fallback;
    }

    private const string SmallUnitCollisionRadiusKey = "SmallUnitCollisionRadius";
    private const string MediumUnitCollisionRadiusKey = "MediumUnitCollisionRadius";
    private const string LargeUnitCollisionRadiusKey = "LargeUnitCollisionRadius";
    private const string SuperLargeUnitCollisionRadiusKey = "SuperLargeUnitCollisionRadius";
    private static Fix64 GetCollisionRadiusBySize(UnitSize size)
    {
        string configKey = size switch
        {
            UnitSize.Small => SmallUnitCollisionRadiusKey,
            UnitSize.Medium => MediumUnitCollisionRadiusKey,
            UnitSize.Large => LargeUnitCollisionRadiusKey,
            UnitSize.SuperLarge => SuperLargeUnitCollisionRadiusKey,
            _ => throw new InvalidOperationException($"Character collision radius config is undefined for unit size '{size}'.")
        };

        return FixedConfigReader.ReadRequiredPositiveFixedConfig(configKey);
    }

    private static Fix64 GetWeightLevelBySize(UnitSize size)
    {
        return size switch
        {
            UnitSize.Small => (Fix64)1,
            UnitSize.Medium => (Fix64)2,
            UnitSize.Large => (Fix64)3,
            UnitSize.SuperLarge => (Fix64)4,
            _ => Fix64.One
        };
    }
}
