using UnityEngine;

/// <summary>
/// 直接攻击工厂：创建 DirectAtkComp，配置武器数据。
/// 在 Unity 编辑器中：右键 → Create → Atk Factory → DirectAtk
/// </summary>
[CreateAssetMenu(fileName = "DirectAtkFactory", menuName = "Atk Factory/DirectAtk")]
public class DirectAtkCompFactory : AtkCompFactory
{
    [Header("武器配置")]
    public float damage = 10f;
    public float attackInterval = 1.5f;
    public WeaponType weaponType = WeaponType.Melee;
    public float attackRange = 150f;
    public float projectileSpeed = 0f;
    public float windUp = 0.4f;
    public float windDown = 0.5f;
    public float splashRadius = 0f;
    public float manaCost = 0f;

    public override IAtkComp CreateAtkComp(MAEntity gmo)
    {
        var weapon = new WeaponData
        {
            Damage = damage,
            AttackInterval = attackInterval,
            Type = weaponType,
            AttackRange = attackRange,
            ProjectileSpeed = projectileSpeed,
            WindUp = windUp,
            WindDown = windDown,
            SplashRadius = splashRadius,
            ManaCost = manaCost
        };

        var comp = new DirectAtkComp(weapon);
        gmo.SetAtkComp(comp);
        comp.Init(gmo);

        // 同时创建 WeaponComp 供 Brain 等组件查询武器信息
        var wc = new WeaponComp(weapon);
        gmo.SetWeaponComp(wc);

        return comp;
    }
}
