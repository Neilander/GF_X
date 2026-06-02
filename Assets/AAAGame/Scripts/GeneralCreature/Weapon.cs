using System;
/// <summary>
/// 武器类型：近战、弹道投射物、即时远程（如扇形AOE）。
/// </summary>
public enum WeaponType
{
    None,
    Melee,
    Projectile,
    InstantRanged,
    CleaveMelee,
    CleaveRanged,
    SelfAoE,
    HealMelee,
    HealProjectile,
    Special,
}

/// <summary>
/// 武器属性枚举：用于 Buff 等外部调用方指定目标 stat。
/// 顺序与 Weapon 内部的 m_Stats 数组索引一致。
/// </summary>
public enum WeaponStatId
{
    Atk,
    Interval,
    Range,
    ProjectileSpeed,
    WindUp,
    WindDown,
    SplashRadius,
    SplitAngle,
    SplitDist,
    ProjectileCount,
    AmmunitionCapacity,
}

/// <summary>
/// 运行时武器：每项数值用 SimpleStat 包装 (Base + Additive + Multiplier)。
/// Buff 通过 ApplyAdditive / ApplyMultiplier 修改；基础值由 WeaponData 初始化后不再改变。
/// Create 的 propertyManager 参数保留用于兼容旧调用，忽略不用。
/// </summary>
public class Weapon
{
    public WeaponType Type { get; private set; }

    private SimpleStat[] m_Stats;

    public Fix64 Atk => m_Stats[(int)WeaponStatId.Atk].Value;
    public Fix64 Interval => m_Stats[(int)WeaponStatId.Interval].Value;
    public Fix64 Range => m_Stats[(int)WeaponStatId.Range].Value;
    public Fix64 ProjectileSpeed => m_Stats[(int)WeaponStatId.ProjectileSpeed].Value;
    public Fix64 WindUp => m_Stats[(int)WeaponStatId.WindUp].Value;
    public Fix64 WindDown => m_Stats[(int)WeaponStatId.WindDown].Value;
    public Fix64 SplashRadius => m_Stats[(int)WeaponStatId.SplashRadius].Value;
    public Fix64 SplitAngle => m_Stats[(int)WeaponStatId.SplitAngle].Value;
    public Fix64 SplitDist => m_Stats[(int)WeaponStatId.SplitDist].Value;
    public Fix64 ProjectileCount => m_Stats[(int)WeaponStatId.ProjectileCount].Value;
    public Fix64 AmmunitionCapacity => m_Stats[(int)WeaponStatId.AmmunitionCapacity].Value;

    public Fix64 GetStat(WeaponStatId stat) => m_Stats[(int)stat].Value;

    /// <summary>
    /// 对指定 stat 追加加法叠加值。Buff OnAdd 传 +delta，OnRemove 传 -delta 撤销。
    /// </summary>
    public void ApplyAdditive(WeaponStatId stat, Fix64 delta)
    {
        m_Stats[(int)stat].Additive += delta;
    }

    /// <summary>
    /// 对指定 stat 追加乘法叠加系数。Buff OnAdd 传 factor，OnRemove 传 1/factor 撤销。
    /// </summary>
    public void ApplyMultiplier(WeaponStatId stat, Fix64 factor)
    {
        m_Stats[(int)stat].Multiplier *= factor;
    }

    /// <summary>
    /// 对指定 stat 追加百分比加法栈（0.25 = +25%）。
    /// 多个 buff 并列累加到 PercentSum，最终 (Base+Additive)*(1+Sum)*Multiplier。
    /// OnAdd 传 +delta，OnRemove 传 -delta 撤销。
    /// </summary>
    public void ApplyPercentAdd(WeaponStatId stat, Fix64 delta)
    {
        m_Stats[(int)stat].PercentSum += delta;
    }

    public static Weapon Create(string idPrefix, WeaponData data, PropertyManager propertyManager = null)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        // propertyManager 参数保留用于调用点兼容，但此版本实现不再使用它。
        _ = idPrefix;
        _ = propertyManager;

        var stats = new SimpleStat[11];
        stats[(int)WeaponStatId.Atk] = SimpleStat.From(data.Atk);
        stats[(int)WeaponStatId.Interval] = SimpleStat.From(data.Interval);
        stats[(int)WeaponStatId.Range] = SimpleStat.From(data.Range);
        stats[(int)WeaponStatId.ProjectileSpeed] = SimpleStat.From(data.ProjectileSpeed);
        stats[(int)WeaponStatId.WindUp] = SimpleStat.From(data.WindUp);
        stats[(int)WeaponStatId.WindDown] = SimpleStat.From(data.WindDown);
        stats[(int)WeaponStatId.SplashRadius] = SimpleStat.From(data.SplashRadius);
        stats[(int)WeaponStatId.SplitAngle] = SimpleStat.From(data.SplitAngle);
        stats[(int)WeaponStatId.SplitDist] = SimpleStat.From(data.SplitDist);
        stats[(int)WeaponStatId.ProjectileCount] = SimpleStat.From(data.ProjectileCount);
        stats[(int)WeaponStatId.AmmunitionCapacity] = SimpleStat.From(data.AmmunitionCapacity);

        return new Weapon
        {
            Type = data.Type,
            m_Stats = stats,
        };
    }
}
