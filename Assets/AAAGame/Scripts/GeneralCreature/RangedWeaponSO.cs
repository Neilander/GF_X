using UnityEngine;

/// <summary>
/// 远程武器SO：实现弹道攻击逻辑
/// </summary>
[CreateAssetMenu(fileName = "RangedWeaponSO", menuName = "Weapon SO/Ranged")]
public class RangedWeaponSO : BaseWeaponSO
{
    [Header("弹道设置")]
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private float _projectileSpeed = 10f;
    [SerializeField] private GameObject _hitVfxPrefab;

    public GameObject ProjectilePrefab => _projectilePrefab;
    public float ProjectileSpeed => _projectileSpeed;
    public GameObject HitVfxPrefab => _hitVfxPrefab;

    public override void Execute(IEntityContext target, WeaponData weaponData)
    {
        if (target == null || !target.Alive)
        {
            return;
        }

        DirectAtkComp atkComp = weaponData.UserData as DirectAtkComp;
        if (atkComp == null)
        {
            Debug.LogError("RangedWeaponSO: weaponData.UserData is not DirectAtkComp");
            return;
        }

        MAEntity attacker = atkComp.Context as MAEntity;
        if (attacker == null)
        {
            Debug.LogError("RangedWeaponSO: attacker is not MAEntity");
            return;
        }

        // 创建弹道参数
        EntityParams projectileParams = EntityParams.Create();
        projectileParams.position = attacker.Position + Vector3.up * 0.5f;
        projectileParams.Target = target;
        projectileParams.WeaponData = weaponData;
        projectileParams.WeaponSO = this;

        // 使用对象池显示弹道
        GF.Entity.ShowEntity<Projectile>("TestProjectile", Const.EntityGroup.Default, projectileParams);

        // 播放攻击特效
        if (AttackVfxPrefab != null)
        {
            GameObject vfx = Object.Instantiate(AttackVfxPrefab, attacker.Position + Vector3.up * 0.5f, Quaternion.identity);
            Object.Destroy(vfx, 2f);
        }

        // 播放攻击音效
        if (AttackSfx != null)
        {
            AudioSource.PlayClipAtPoint(AttackSfx, attacker.Position);
        }
    }
}