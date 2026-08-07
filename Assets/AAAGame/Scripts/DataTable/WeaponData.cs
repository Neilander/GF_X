using System;

/// <summary>
/// 单位武器配表数据。
/// 创建后通常不在运行时修改，作为属性系统与攻击系统的共享输入。
/// </summary>
public class WeaponData
{
    public WeaponType Type { get; }
    public Fix64 Atk { get; }
    public Fix64 Interval { get; }
    public Fix64 Range { get; }
    public Fix64 ProjectileSpeed { get; }
    public Fix64 WindUp { get; }
    public Fix64 WindDown { get; }
    public Fix64 SplashRadius { get; }
    public Fix64 SplitAngle { get; }
    public Fix64 SplitDist { get; }
    public Fix64 ProjectileCount { get; }
    public Fix64 AmmunitionCapacity { get; }
    public Fix64[] UniqueValues { get; }

    // 兼容旧命名访问。
    public Fix64 Damage => Atk;
    public Fix64 AttackInterval => Interval;
    public Fix64 AttackRange => Range;

    public WeaponData(
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
        Type = type;
        Atk = atk;
        Interval = interval;
        Range = range;
        ProjectileSpeed = projectileSpeed;
        WindUp = windUp;
        WindDown = windDown;
        SplashRadius = splashRadius;
        SplitAngle = splitAngle;
        SplitDist = splitDist;
        ProjectileCount = projectileCount;
        AmmunitionCapacity = ammunitionCapacity;
        UniqueValues = uniqueValues != null ? (Fix64[])uniqueValues.Clone() : Array.Empty<Fix64>();
    }

    public Weapon ToWeapon(string idPrefix, PropertyManager propertyManager = null)
    {
        return Weapon.Create(idPrefix, this, propertyManager);
    }
}
