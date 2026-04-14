using System;
/// <summary>
/// 武器类型：近战、弹道投射物、即时远程（如扇形AOE）。
/// </summary>
public enum WeaponType
{
    Melee,
    Projectile,
    InstantRanged
}

/// <summary>
/// 运行时武器：武器相关属性都以 BaseValueProperty 挂载，
/// 基础值来自配表 WeaponData。
/// </summary>
public class Weapon
{
    public WeaponType Type { get; private set; }
    private BaseValueProperty AtkProperty;
    private BaseValueProperty IntervalProperty;
    private BaseValueProperty RangeProperty;
    private BaseValueProperty ProjectileSpeedProperty;
    private BaseValueProperty WindUpProperty;
    private BaseValueProperty WindDownProperty;
    private BaseValueProperty SplashRadiusProperty;
    private BaseValueProperty SplitAngleProperty;
    private BaseValueProperty SplitDistProperty;
    private BaseValueProperty ProjectileCountProperty;
    private BaseValueProperty ManaCostProperty;

    public Fix64 Atk => AtkProperty.GetValue();
    public Fix64 Interval => IntervalProperty.GetValue();
    public Fix64 Range => RangeProperty.GetValue();
    public Fix64 ProjectileSpeed => ProjectileSpeedProperty.GetValue();
    public Fix64 WindUp => WindUpProperty.GetValue();
    public Fix64 WindDown => WindDownProperty.GetValue();
    public Fix64 SplashRadius => SplashRadiusProperty.GetValue();
    public Fix64 SplitAngle => SplitAngleProperty.GetValue();
    public Fix64 SplitDist => SplitDistProperty.GetValue();
    public Fix64 ProjectileCount => ProjectileCountProperty.GetValue();
    public Fix64 ManaCost => ManaCostProperty.GetValue();

    public static Weapon Create(string idPrefix, WeaponData data, PropertyManager propertyManager = null)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        WeaponData source = data;
        PropertyManager manager = propertyManager ?? new PropertyManager();
        string prefix = string.IsNullOrWhiteSpace(idPrefix) ? "Weapon" : idPrefix;

        return new Weapon
        {
            Type = source.Type,
            AtkProperty = (BaseValueProperty)BaseValueProperty.Create(source.Atk, $"{prefix}_Atk").Register(manager),
            IntervalProperty = (BaseValueProperty)BaseValueProperty.Create(source.Interval, $"{prefix}_Interval").Register(manager),
            RangeProperty = (BaseValueProperty)BaseValueProperty.Create(source.Range, $"{prefix}_Range").Register(manager),
            ProjectileSpeedProperty = (BaseValueProperty)BaseValueProperty.Create(source.ProjectileSpeed, $"{prefix}_ProjectileSpeed").Register(manager),
            WindUpProperty = (BaseValueProperty)BaseValueProperty.Create(source.WindUp, $"{prefix}_WindUp").Register(manager),
            WindDownProperty = (BaseValueProperty)BaseValueProperty.Create(source.WindDown, $"{prefix}_WindDown").Register(manager),
            SplashRadiusProperty = (BaseValueProperty)BaseValueProperty.Create(source.SplashRadius, $"{prefix}_SplashRadius").Register(manager),
            SplitAngleProperty = (BaseValueProperty)BaseValueProperty.Create(source.SplitAngle, $"{prefix}_SplitAngle").Register(manager),
            SplitDistProperty = (BaseValueProperty)BaseValueProperty.Create(source.SplitDist, $"{prefix}_SplitDist").Register(manager),
            ProjectileCountProperty = (BaseValueProperty)BaseValueProperty.Create(source.ProjectileCount, $"{prefix}_ProjectileCount").Register(manager),
            ManaCostProperty = (BaseValueProperty)BaseValueProperty.Create(source.ManaCost, $"{prefix}_ManaCost").Register(manager)
        };
    }
}
