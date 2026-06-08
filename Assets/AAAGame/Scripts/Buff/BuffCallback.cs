using System;
using System.Collections.Generic;
using GameFramework;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// Buff回调基类（纯C#类，不需要GameObject）
/// </summary>
public abstract class BuffCallback
{
    /// <summary>
    /// Buff数据
    /// </summary>
    protected BuffData buffData;
    
    /// <summary>
    /// 宿主实体
    /// </summary>
    protected MAEntity hostEntity;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public virtual void Initialize(BuffData data, MAEntity entity)
    {
        buffData = data;
        hostEntity = entity;
    }

    public virtual void OnAdd() { }
    public virtual void OnRemove() { }
    public virtual void OnAddStack(int oldStack, int newStack) { }
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnDurationEnd() { }
    public virtual void OnHostDead() { }
    public virtual void OnKill(MAEntity target) { }
    public virtual void OnAttackStarted(IEntityContext target) { }
    public virtual void OnAttackCompleted(IEntityContext target) { }
    public virtual void OnAttackInterrupted(AttackInterruptReason reason, IEntityContext target) { }

    /// <summary>
    /// 宿主对 target 造成伤害前的钩子，允许调整最终伤害值。
    /// 由 DamageHelper.DoDamage 遍历 attacker 身上所有 BuffCallback 时调用。
    /// 默认透传，子类可按条件修改。
    /// </summary>
    public virtual Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage) => baseDamage;

    /// <summary>
    /// 宿主受到伤害前的钩子，允许调整最终伤害值。
    /// 由 DamageHelper.DoDamage 遍历 target 身上所有 BuffCallback 时调用。
    /// 默认透传，子类可按条件修改。
    /// </summary>
    public virtual Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType) => baseDamage;

    /// <summary>
    /// 暴击伤害加成百分比。基础暴击伤害来自 GameConfig.BaseCriticalDamageRate。
    /// </summary>
    public virtual Fix64 GetCriticalDamageBonusPercent() => Fix64.Zero;

    public virtual void Clear()
    {
        buffData = null;
        hostEntity = null;
    }
}

public static class CriticalDamageUtility
{
    private const string BaseCriticalDamageRateKey = "BaseCriticalDamageRate";
    private const float DefaultBaseCriticalDamageRate = 50f;

    public static Fix64 ApplyCriticalDamage(MAEntity attacker, Fix64 baseDamage)
    {
        if (attacker == null)
            throw new InvalidOperationException("CriticalDamageUtility.ApplyCriticalDamage failed: attacker is null.");

        Fix64 criticalPercent = (Fix64)(GF.Config != null
            ? GF.Config.GetFloat(BaseCriticalDamageRateKey, DefaultBaseCriticalDamageRate)
            : DefaultBaseCriticalDamageRate);

        if (attacker.BuffComp is CharacterBuffComp buffComp)
        {
            foreach (var module in buffComp.EnumerateAllModules())
            {
                criticalPercent += module.GetCriticalDamageBonusPercent();
            }
        }

        return baseDamage * (Fix64.One + criticalPercent / (Fix64)100);
    }
}

public sealed class PercentAttackBonusBuff : BuffCallback
{
    private readonly Fix64 m_Percent;
    private Fix64 m_AppliedPercentAdd;
    private bool m_Applied;

    public PercentAttackBonusBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var weapon = hostEntity?.weaponComp?.Data;
        if (weapon == null || m_Percent == Fix64.Zero)
            return;

        m_AppliedPercentAdd = m_Percent / (Fix64)100;
        weapon.ApplyPercentAdd(WeaponStatId.Atk, m_AppliedPercentAdd);
        m_Applied = true;
    }

    public override void OnRemove()
    {
        base.OnRemove();
        if (!m_Applied)
            return;

        var weapon = hostEntity?.weaponComp?.Data;
        if (weapon != null)
            weapon.ApplyPercentAdd(WeaponStatId.Atk, -m_AppliedPercentAdd);

        m_Applied = false;
    }
}

public sealed class RevertibleMoveSpeedBonusBuff : BuffCallback
{
    private readonly Fix64 m_Bonus;
    private IPropertyModifier m_Modifier;

    public RevertibleMoveSpeedBonusBuff(Fix64 bonus)
    {
        m_Bonus = bonus;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_Bonus == Fix64.Zero)
            return;

        m_Modifier = PropertyDirectAdditiveModifier.Create(m_Bonus);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_Modifier, true);
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_Modifier == null)
            return;

        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_Modifier, false);
        m_Modifier = null;
    }
}

public sealed class PercentDamageReductionBuff : BuffCallback
{
    private readonly Fix64 m_ReductionPercent;

    public PercentDamageReductionBuff(Fix64 reductionPercent)
    {
        m_ReductionPercent = reductionPercent;
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (modType != HealthModifyType.reduce || baseDamage <= Fix64.Zero || m_ReductionPercent <= Fix64.Zero)
            return baseDamage;

        Fix64 multiplier = Fix64.One - m_ReductionPercent / (Fix64)100;
        if (multiplier < Fix64.Zero)
            multiplier = Fix64.Zero;

        return baseDamage * multiplier;
    }
}

public sealed class ExcessDamageReductionBuff : BuffCallback
{
    private readonly Fix64 m_Threshold;
    private readonly Fix64 m_ExcessReductionPercent;

    public ExcessDamageReductionBuff(Fix64 threshold, Fix64 excessReductionPercent)
    {
        m_Threshold = threshold;
        m_ExcessReductionPercent = excessReductionPercent;
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (modType != HealthModifyType.reduce || baseDamage <= m_Threshold || m_ExcessReductionPercent <= Fix64.Zero)
            return baseDamage;

        Fix64 multiplier = Fix64.One - m_ExcessReductionPercent / (Fix64)100;
        if (multiplier < Fix64.Zero)
            multiplier = Fix64.Zero;

        return m_Threshold + (baseDamage - m_Threshold) * multiplier;
    }
}

public sealed class AlwaysCriticalDamageBuff : BuffCallback
{
    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        return CriticalDamageUtility.ApplyCriticalDamage(hostEntity, baseDamage);
    }
}

public sealed class FirstHitPerTargetCriticalBuff : BuffCallback
{
    private readonly HashSet<int> m_HitTargetIds = new HashSet<int>();

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (!(target is EntityBase targetEntity))
            throw new InvalidOperationException($"FirstHitPerTargetCriticalBuff failed: target has no entity id. host={hostEntity?.CharacterKey}, target={target?.CharacterKey}.");

        if (!m_HitTargetIds.Add(targetEntity.Id))
            return baseDamage;

        return CriticalDamageUtility.ApplyCriticalDamage(hostEntity, baseDamage);
    }
}

public sealed class HighHealthTargetCriticalBuff : BuffCallback
{
    private readonly Fix64 m_HpThresholdPercent;

    public HighHealthTargetCriticalBuff(Fix64 hpThresholdPercent)
    {
        m_HpThresholdPercent = hpThresholdPercent;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (!(target is GeneralCreature creature) || creature.CreaturePropertyManager == null)
            return baseDamage;

        Fix64 max = creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        if (max <= Fix64.Zero)
            return baseDamage;

        Fix64 hpPercent = creature.HealthValue / max * (Fix64)100;
        if (hpPercent < m_HpThresholdPercent)
            return baseDamage;

        return CriticalDamageUtility.ApplyCriticalDamage(hostEntity, baseDamage);
    }
}

public sealed class HealthDrainOverTimeBuff : BuffCallback
{
    private readonly Fix64 m_DamagePerSecond;
    private float m_ElapsedSeconds;

    public HealthDrainOverTimeBuff(Fix64 damagePerSecond)
    {
        m_DamagePerSecond = damagePerSecond;
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (hostEntity == null || !hostEntity.Alive || m_DamagePerSecond <= Fix64.Zero || deltaTime <= 0f)
            return;

        m_ElapsedSeconds += deltaTime;
        while (m_ElapsedSeconds >= 1f && hostEntity != null && hostEntity.Alive)
        {
            m_ElapsedSeconds -= 1f;
            hostEntity.TakeDamage(m_DamagePerSecond, HealthModifyType.reduce);
        }
    }
}

public sealed class AmmoDepletedDeathBuff : BuffCallback
{
    private readonly float m_DelaySeconds;
    private float m_Timer;

    public AmmoDepletedDeathBuff(float delaySeconds)
    {
        m_DelaySeconds = delaySeconds;
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (hostEntity == null || !hostEntity.Alive)
            return;

        var weaponComp = hostEntity.weaponComp;
        if (weaponComp == null || !weaponComp.HasAmmunition || weaponComp.CurrentAmmo > 0)
        {
            m_Timer = 0f;
            return;
        }

        m_Timer += deltaTime;
        if (m_Timer < m_DelaySeconds)
            return;

        hostEntity.TakeDamage(hostEntity.HealthValue, HealthModifyType.reduce);
    }
}

public sealed class NearbyEnemyAttackLockBuff : BuffCallback, ICapability
{
    private const float ScanIntervalSeconds = 0.1f;

    private readonly Fix64 m_Radius;
    private float m_ScanTimer;
    private bool m_AttackLocked;

    public NearbyEnemyAttackLockBuff(Fix64 radius)
    {
        m_Radius = radius;
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (deltaTime <= 0f)
            return;

        m_ScanTimer += deltaTime;
        if (m_ScanTimer < ScanIntervalSeconds)
            return;

        m_ScanTimer = 0f;
        bool hasNearbyEnemy = HasNearbyEnemy();
        if (hasNearbyEnemy)
            LockAttack();
        else
            UnlockAttack();
    }

    public override void OnRemove()
    {
        base.OnRemove();
        UnlockAttack();
    }

    public override void OnHostDead()
    {
        base.OnHostDead();
        UnlockAttack();
    }

    private bool HasNearbyEnemy()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("NearbyEnemyAttackLockBuff.HasNearbyEnemy failed: hostEntity is null.");

        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new InvalidOperationException("NearbyEnemyAttackLockBuff.HasNearbyEnemy failed: EntityRegistry.AllEntities is null.");

        float radius = DistanceUnitConverter.ConvertToWorldFloat(m_Radius);
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, hostEntity))
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(hostEntity, candidate))
                continue;

            if (hostEntity.DistanceToTargetSurface(candidate) <= radius)
                return true;
        }

        return false;
    }

    private void LockAttack()
    {
        if (m_AttackLocked)
            return;

        if (hostEntity == null || hostEntity.atkComp == null)
            throw new InvalidOperationException($"NearbyEnemyAttackLockBuff.LockAttack failed: missing atkComp. host={hostEntity?.CharacterKey}.");

        hostEntity.LockComp(hostEntity.atkComp, this);
        m_AttackLocked = true;
    }

    private void UnlockAttack()
    {
        if (!m_AttackLocked)
            return;

        if (hostEntity == null || hostEntity.atkComp == null)
            throw new InvalidOperationException("NearbyEnemyAttackLockBuff.UnlockAttack failed: hostEntity or atkComp is null.");

        hostEntity.ResumeComp(hostEntity.atkComp, this);
        m_AttackLocked = false;
    }

    public void ShutDown() { }
    public void Resume() { }
}

public sealed class LateRiderChargeBuff : BuffCallback
{
    private const float ScanIntervalSeconds = 0.1f;

    private readonly Fix64 m_MinDistance;
    private readonly Fix64 m_MaxDistance;
    private readonly Fix64 m_MoveSpeedBonus;
    private readonly Fix64 m_AttackBonus;
    private readonly float m_CooldownSeconds;

    private float m_ScanTimer;
    private float m_CooldownTimer;
    private bool m_Charging;
    private bool m_MoveApplied;
    private bool m_AttackApplied;
    private IPropertyModifier m_MoveModifier;
    private IEntityContext m_ChargeTarget;

    public LateRiderChargeBuff(
        Fix64 minDistance,
        Fix64 maxDistance,
        Fix64 moveSpeedBonus,
        Fix64 attackBonus,
        float cooldownSeconds)
    {
        m_MinDistance = minDistance;
        m_MaxDistance = maxDistance;
        m_MoveSpeedBonus = moveSpeedBonus;
        m_AttackBonus = attackBonus;
        m_CooldownSeconds = cooldownSeconds;
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (deltaTime <= 0f)
            return;

        if (m_CooldownTimer > 0f)
            m_CooldownTimer = UnityEngine.Mathf.Max(0f, m_CooldownTimer - deltaTime);

        if (m_Charging)
        {
            UpdateActiveCharge();
            return;
        }

        m_ScanTimer += deltaTime;
        if (m_ScanTimer < ScanIntervalSeconds || m_CooldownTimer > 0f)
            return;

        m_ScanTimer = 0f;
        TryEnterCharge();
    }

    public override void OnAttackCompleted(IEntityContext target)
    {
        base.OnAttackCompleted(target);
        if (m_Charging)
            ExitCharge(true);
    }

    public override void OnAttackInterrupted(AttackInterruptReason reason, IEntityContext target)
    {
        base.OnAttackInterrupted(reason, target);
        if (m_Charging)
            ExitCharge(true);
    }

    public override void OnRemove()
    {
        base.OnRemove();
        if (m_Charging)
            ExitCharge(false);
    }

    public override void OnHostDead()
    {
        base.OnHostDead();
        if (m_Charging)
            ExitCharge(false);
    }

    private void TryEnterCharge()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("LateRiderChargeBuff.TryEnterCharge failed: hostEntity is null.");

        IEntityContext target = hostEntity.targetComp?.CurrentTarget;
        if (!WeaponTargetRules.IsValidTargetForCurrentWeapon(hostEntity, target))
            return;

        float minDistance = DistanceUnitConverter.ConvertToWorldFloat(m_MinDistance);
        float maxDistance = DistanceUnitConverter.ConvertToWorldFloat(m_MaxDistance);
        float distance = hostEntity.DistanceToTargetSurface(target);
        if (distance < minDistance || distance > maxDistance)
            return;

        EnterCharge(target);
    }

    private void UpdateActiveCharge()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("LateRiderChargeBuff.UpdateActiveCharge failed: hostEntity is null.");

        if (hostEntity.atkComp != null && hostEntity.atkComp.IsAttacking)
            return;

        if (!WeaponTargetRules.IsValidTargetForCurrentWeapon(hostEntity, m_ChargeTarget))
        {
            ExitCharge(true);
            return;
        }

        if (hostEntity.targetComp?.CurrentTarget != m_ChargeTarget)
        {
            ExitCharge(true);
            return;
        }

        if (!hostEntity.CanRun(hostEntity.moveComp) || !hostEntity.CanRun(hostEntity.atkComp))
            ExitCharge(true);
    }

    private void EnterCharge(IEntityContext target)
    {
        ApplyMoveBonus();
        ApplyAttackBonus();
        m_ChargeTarget = target;
        m_Charging = true;
    }

    private void ExitCharge(bool startCooldown)
    {
        RemoveAttackBonus();
        RemoveMoveBonus();
        m_ChargeTarget = null;
        m_Charging = false;
        m_ScanTimer = 0f;
        if (startCooldown)
            m_CooldownTimer = m_CooldownSeconds;
    }

    private void ApplyMoveBonus()
    {
        if (m_MoveSpeedBonus == Fix64.Zero)
            return;

        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null)
            throw new InvalidOperationException($"LateRiderChargeBuff.ApplyMoveBonus failed: missing property manager. host={hostEntity?.CharacterKey}.");

        m_MoveModifier = PropertyDirectAdditiveModifier.Create(m_MoveSpeedBonus);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_MoveModifier, true);
        m_MoveApplied = true;
    }

    private void RemoveMoveBonus()
    {
        if (!m_MoveApplied)
            return;

        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_MoveModifier == null)
            throw new InvalidOperationException($"LateRiderChargeBuff.RemoveMoveBonus failed: missing property manager or modifier. host={hostEntity?.CharacterKey}.");

        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_MoveModifier, false);
        m_MoveModifier = null;
        m_MoveApplied = false;
    }

    private void ApplyAttackBonus()
    {
        if (m_AttackBonus == Fix64.Zero)
            return;

        var weapon = hostEntity?.weaponComp?.Data;
        if (weapon == null)
            throw new InvalidOperationException($"LateRiderChargeBuff.ApplyAttackBonus failed: missing weapon. host={hostEntity?.CharacterKey}.");

        weapon.ApplyAdditive(WeaponStatId.Atk, m_AttackBonus);
        m_AttackApplied = true;
    }

    private void RemoveAttackBonus()
    {
        if (!m_AttackApplied)
            return;

        var weapon = hostEntity?.weaponComp?.Data;
        if (weapon == null)
            throw new InvalidOperationException($"LateRiderChargeBuff.RemoveAttackBonus failed: missing weapon. host={hostEntity?.CharacterKey}.");

        weapon.ApplyAdditive(WeaponStatId.Atk, -m_AttackBonus);
        m_AttackApplied = false;
    }
}
