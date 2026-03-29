using UnityEngine;

/// <summary>
/// 远程武器SO：实现弹道攻击逻辑
/// </summary>
[CreateAssetMenu(fileName = "RangedWeaponSO", menuName = "Weapon SO/Ranged")]
public class RangedWeaponSO : BaseWeaponSO
{
    [Header("弹道设置")]
    [SerializeField] private string _projectileName = "Projectile";
    [SerializeField] private float _projectileSpeed = 10f;
    [SerializeField] private string _hitVfxName;

    public string ProjectileName => _projectileName;
    public float ProjectileSpeed => _projectileSpeed;
    public string HitVfxName => _hitVfxName;

    public override void Execute(IEntityContext attacker, IEntityContext target, WeaponData weaponData)
    {
        if (target == null || !target.Alive)
        {
            return;
        }

        // 创建弹道参数
        EntityParams projectileParams = EntityParams.Create();
        projectileParams.position = attacker.Position + Vector3.up * 0.5f;
        projectileParams.Attacker = attacker;
        projectileParams.Target = target;
        projectileParams.WeaponData = weaponData;
        projectileParams.WeaponSO = this;

        // 使用对象池显示弹道
        GF.Entity.ShowEntity<Projectile>(_projectileName, Const.EntityGroup.Default, projectileParams);

        // 播放攻击特效
        if (!string.IsNullOrEmpty(AttackVfxName))
        {
            var vfxParams = EntityParams.Create(attacker.Position + Vector3.up * 0.5f);
            GF.Entity.ShowEffect(AttackVfxName, vfxParams, 2f);
        }

        // 播放攻击音效
        if (AttackSfx != null)
        {
            AudioSource.PlayClipAtPoint(AttackSfx, attacker.Position);
        }
    }
}