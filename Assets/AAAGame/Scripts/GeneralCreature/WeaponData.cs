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
    public float Damage = 10f;
    public float AttackInterval = 1.5f;
    public WeaponType Type = WeaponType.Melee;
    public float AttackRange = 150f;
    public float ProjectileSpeed = 0f;
    public float WindUp = 0.4f;
    public float WindDown = 0.5f;
    public float SplashRadius = 0f;
    public float ManaCost = 0f;
}
