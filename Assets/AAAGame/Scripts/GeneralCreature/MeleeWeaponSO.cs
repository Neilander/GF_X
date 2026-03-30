using UnityEngine;

/// <summary>
/// 近战武器 SO：直接对目标造成伤害。
/// </summary>
[CreateAssetMenu(fileName = "MeleeWeaponSO", menuName = "Weapon SO/Melee")]
public class MeleeWeaponSO : BaseWeaponSO
{
    public override void Execute(IEntityContext attacker, IEntityContext target, WeaponData weaponData)
    {
        // 对目标造成伤害
        target.TakeDamage(weaponData.Damage, HealthModifyType.reduce);

        // TODO: 播放攻击特效
        // TODO: 播放攻击音效
    }
}
