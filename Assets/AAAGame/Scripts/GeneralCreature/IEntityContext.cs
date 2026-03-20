using UnityEngine;

/// <summary>
/// 实体上下文接口：组件和 Brain 通过此接口访问实体，而非直接依赖 MAEntity。
/// 真实版由 MAEntity 实现（映射到 Transform/CharacterController 等），
/// 测试版由 SimEntityContext 实现（纯数据，无 Unity 引擎依赖）。
/// </summary>
public interface IEntityContext
{
    Vector3 Position { get; set; }
    Quaternion Rotation { get; set; }
    SideType Side { get; }
    bool Alive { get; }
    string ReferenceId { get; }

    IControlBrain Brain { get; }
    IMoveExecutor MoveExecutor { get; }

    // 组件引用（供 Brain 等访问）
    IMoveComp MoveComp { get; }
    IAtkComp AtkComp { get; }
    ITargetingComp TargetComp { get; }
    WeaponComp WeaponComp { get; }

    // 属性查询
    float GetProperty(CreatureMainProperty prop);

    // 受伤
    void TakeDamage(float damage, HealthModifyType modType);

    // 组件锁定
    bool CanRun(ICapability cap);
    void LockComp(ICapability toLock, ICapability locker);
    void ResumeComp(ICapability toResume, ICapability locker);
}
