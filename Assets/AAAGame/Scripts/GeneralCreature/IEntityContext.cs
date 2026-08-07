using UnityEngine;
using AAAGame.Scripts.BuffSystem;
using System.Collections.Generic;

/// <summary>
/// 实体上下文接口：组件和 Brain 通过此接口访问实体，而非直接依赖 MAEntity。
/// 真实逻辑由 LogicEntityState 实现，MAEntity 只代理读取并承载表现绑定；
/// 测试版由 SimEntityContext 实现（纯数据，无 Unity 引擎依赖）。
/// </summary>
public interface IEntityContext : ITargetable
{
    LogicEntityId LogicEntityId { get; }
    FixVector2 PositionFixed { get; }
    FixVector2 ForwardFixed { get; }
    LogicCombatShape CombatShape { get; }
    Vector3 Position { get; }
    Quaternion Rotation { get; }
    new SideType Side { get; }
    new bool Alive { get; }
    new string CharacterKey { get; }
    CharacterDataDetail CharacterData { get; }
    CreaturePropertyManager CreatureProperties { get; }
    new Fix64 HealthValue { get; }
    int TauntLevel { get; set; }

    IControlBrain Brain { get; }
    IMoveExecutor MoveExecutor { get; }

    // 组件引用（供 Brain 等访问）
    IMoveComp MoveComp { get; }
    IAtkComp AtkComp { get; }
    ITargetingComp TargetComp { get; }
    IBuffComp BuffComp { get; }
    WeaponComp WeaponComp { get; }
    IDurationMoveEffectComp DurationMoveEffectComp { get; }
    void SetMoveComp(IMoveComp moveComp);
    void SetAtkComp(IAtkComp atkComp);
    void SetTargetingComp(ITargetingComp targetingComp);
    void SetWeaponComp(WeaponComp weaponComp);

    // 脱战状态：没有有效警戒目标且没有正在攻击时为 true，供后续 Buff 做延迟触发查询。
    bool IsOutOfCombat { get; }
    Fix64 OutOfCombatElapsedLogicTime { get; }
    float OutOfCombatElapsedSeconds { get; }

    // 属性查询（逻辑层统一使用 Fix64）
    Fix64 GetProperty(CreatureMainProperty prop);

    // 受伤（逻辑层统一使用 Fix64）
    new void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null);
    void Heal(Fix64 amount);
    bool RegisterInvincibleSource(string sourceId);
    bool UnregisterInvincibleSource(string sourceId);

    // 组件锁定
    bool CanRun(ICapability cap);
    void LockComp(ICapability toLock, ICapability locker);
    void ResumeComp(ICapability toResume, ICapability locker);
}

public interface IBuildingLogicContext : IEntityContext
{
    BuildingData BuildingData { get; }
    BuildingExtraProps ProductionProps { get; }
    string BuildingInstanceId { get; }
    string StrongholdId { get; }
    int OwnerFactionId { get; }
    int GetArmyForce();
    int GetArmyForceWithoutRuntimeRules();
    int GetArmySupplyPerUnit();
    int GetArmyOccupiedSupply();
    void SetArmyForceBase(int value);
    void SetArmySupplyPerUnitBase(int value);
    void ModifyArmyForce(IPropertyModifier modifier, bool ifAdd = true);
    void ModifyArmySupplyPerUnit(IPropertyModifier modifier, bool ifAdd = true);
    IReadOnlyList<LogicInteractionOptionDescriptor> InteractionOptions { get; }
    bool IsDisabled { get; }
    bool IsPhaseProtected { get; }
    bool IsPermanentlyInvincible { get; }
    bool HasPermanentNoAttackCapability { get; }
    bool BlocksLogicMovement { get; }
    bool IsGameEndConditionBuilding { get; }
    bool IsNavigationStaticBaked { get; }
    event System.Action<int, int> OwnerFactionChanged;
    void SetOwnerFaction(int ownerFactionId);
    void RestoreBuildingToFullHealth();
    void SetCollisionBlockingByBuff(bool blocksMovement);
    void SetPermanentStealthByBuff(bool enabled);
    void SetPermanentInvincibilityByBuff(bool enabled);
    void SetPhaseProtectionByBuff(bool enabled);
}

public interface IHeroLogicContext : IEntityContext
{
    bool IsHeroEntity { get; }
    bool IsGhostState { get; }
    void SetGhostStateByBuff(bool enabled);
    void RestoreFromGhostState();
}
