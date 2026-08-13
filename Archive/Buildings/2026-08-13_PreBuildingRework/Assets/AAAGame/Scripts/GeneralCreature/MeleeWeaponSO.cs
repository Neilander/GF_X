using UnityEngine;

/// <summary>
/// 近战武器 SO：通过 DamageHelper 对目标造成伤害，走统一 buff 钩子链路。
/// </summary>
[CreateAssetMenu(fileName = "MeleeWeaponSO", menuName = "Weapon SO/Melee")]
public class MeleeWeaponSO : BaseWeaponSO
{
    public override void Execute(IEntityContext attacker, IEntityContext target, WeaponData weaponData)
    {
        var damage = new Damage(attacker as ITargetable, weaponData.Damage, HealthModifyType.reduce);
        DamageHelper.DoDamage(target as ITargetable, damage, attacker);

        // TODO: 播放攻击特效
        // TODO: 播放攻击音效
    }
}
