using UnityEngine;

/// <summary>
/// 远程武器SO：实现弹道攻击逻辑
/// </summary>
[CreateAssetMenu(fileName = "RangedWeaponSO", menuName = "Weapon SO/Ranged")]
public class RangedWeaponSO : BaseWeaponSO
{
    [Header("弹道设置")]
    [SerializeField] private string _projectileName = "Projectile";
    // Deprecated: projectile speed now comes from WeaponData.ProjectileSpeed (data table / weapon stats).
    // Keep this serialized field only for backward compatibility with existing SO assets.
    [SerializeField] private float _projectileSpeed = 10f;
    [SerializeField] private string _hitVfxName;

    public string ProjectileName => _projectileName;
    // Deprecated: runtime no longer reads projectile speed from SO.
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
        GF.Entity.ShowEntity<Projectile>(_projectileName, Const.EntityGroup.Bullet, projectileParams);

        // 远程攻击发射音效（命中音由 Projectile.HitTarget 单独播 basicAttack）
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play("rangeAttack");

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
