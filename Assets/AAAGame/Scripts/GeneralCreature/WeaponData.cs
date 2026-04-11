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
/// 武器配置数据：定义一把武器的攻击属性。
/// 对应配表中的武器列（攻击力、攻击间隔、攻击距离、前摇、后摇等）。
/// </summary>
public class WeaponData
{
    public Fix64 Damage = (Fix64)10;
    public Fix64 AttackInterval = (Fix64)1.5f;
    public WeaponType Type = WeaponType.Melee;
    public Fix64 AttackRange = (Fix64)600;
    public Fix64 ProjectileSpeed = Fix64.Zero;
    public Fix64 WindUp = (Fix64)0.4f;
    public Fix64 WindDown = (Fix64)0.5f;
    public Fix64 SplashRadius = Fix64.Zero;
    public Fix64 ManaCost = Fix64.Zero;
}
