using UnityEngine;

/// <summary>
/// 武器 ScriptableObject 基类：定义武器的执行接口和通用资源引用。
/// 子类实现 Execute 来定义具体的伤害/效果逻辑。
/// </summary>
public abstract class BaseWeaponSO : ScriptableObject
{
    public WeaponType Type;
    [Header("资源引用")]
    [SerializeField] private GameObject _attackVfxPrefab;
    [SerializeField] private AudioClip _attackSfx;

    public GameObject AttackVfxPrefab => _attackVfxPrefab;
    public AudioClip AttackSfx => _attackSfx;

    /// <summary>
    /// 对目标执行武器效果（伤害、特效等）。
    /// </summary>
    /// <param name="target">受击目标的实体上下文</param>
    /// <param name="weaponData">武器数值数据</param>
    public abstract void Execute(IEntityContext target, WeaponData weaponData);
}
