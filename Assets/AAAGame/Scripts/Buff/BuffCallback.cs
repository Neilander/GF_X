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
    protected IEntityContext hostEntity;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public virtual void Initialize(BuffData data, IEntityContext entity)
    {
        buffData = data;
        hostEntity = entity;
    }

    public virtual void OnAdd() { }
    public virtual void OnRemove() { }
    public virtual void OnAddStack(int oldStack, int newStack) { }
    public virtual void OnUpdate(Fix64 deltaTime) { }
    public virtual void OnDurationEnd() { }
    public virtual void OnHostDead() { }
    public virtual void OnKill(IEntityContext target) { }
    public virtual void OnHealed(Fix64 amount) { }
    public virtual void OnAttackStarted(IEntityContext target) { }
    public virtual void OnAttackCompleted(IEntityContext target) { }
    public virtual void OnAttackInterrupted(AttackInterruptReason reason, IEntityContext target) { }
    public virtual bool CanStartAttack() => true;

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

    public virtual bool IsNegativeStatus => false;

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

public interface ISourceBuildingUnitBuffProvider
{
    bool CanProvideUnitBuffs();
    void CreateUnitBuffModules(List<BuffCallback> modules);
}

public static class CriticalDamageUtility
{
    private const string BaseCriticalDamageRateKey = "BaseCriticalDamageRate";
    private static Fix64 s_BaseCriticalDamageRate;
    private static bool s_Prepared;

    public static void PrepareRuntimeDependencies()
    {
        if (s_Prepared)
            return;
        s_BaseCriticalDamageRate = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(BaseCriticalDamageRateKey);
        s_Prepared = true;
    }

    public static Fix64 ApplyCriticalDamage(IEntityContext attacker, Fix64 baseDamage)
    {
        if (attacker == null)
            throw new InvalidOperationException("CriticalDamageUtility.ApplyCriticalDamage failed: attacker is null.");

        if (!s_Prepared)
            throw new InvalidOperationException("CriticalDamageUtility runtime dependencies were not prepared.");
        Fix64 criticalPercent = s_BaseCriticalDamageRate;

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

public sealed class CriticalDamageBonusBuff : BuffCallback
{
    private readonly Fix64 m_BonusPercent;

    public CriticalDamageBonusBuff(Fix64 bonusPercent)
    {
        m_BonusPercent = bonusPercent;
    }

    public override Fix64 GetCriticalDamageBonusPercent() => m_BonusPercent;
}

public sealed class HealOnOutgoingDamageBuff : BuffCallback
{
    private readonly Fix64 m_HealPerHit;

    public HealOnOutgoingDamageBuff(Fix64 healPerHit)
    {
        m_HealPerHit = healPerHit;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (m_HealPerHit <= Fix64.Zero)
            return baseDamage;

        hostEntity?.Heal(m_HealPerHit);

        return baseDamage;
    }
}

public sealed class KnockbackOnOutgoingDamageBuff : BuffCallback
{
    private readonly Fix64 m_PushLevel;

    public KnockbackOnOutgoingDamageBuff(Fix64 pushLevel)
    {
        m_PushLevel = pushLevel;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (target is not IEntityContext targetEntity)
            return baseDamage;
        if (hostEntity == null)
            throw new InvalidOperationException("Knockback buff is not initialized.");
        if (hostEntity.BuffComp is not CharacterBuffComp buffComp)
            throw new InvalidOperationException($"Knockback source has no CharacterBuffComp. source={hostEntity.LogicEntityId.Value}.");

        Fix64 totalLevel = Fix64.Zero;
        KnockbackOnOutgoingDamageBuff first = null;
        foreach (BuffCallback module in buffComp.EnumerateAllModules())
        {
            if (module is not KnockbackOnOutgoingDamageBuff knockback)
                continue;
            first ??= knockback;
            totalLevel += knockback.m_PushLevel;
        }
        if (first == null)
            throw new InvalidOperationException("Knockback buff is missing from its host BuffComp.");
        if (!ReferenceEquals(first, this))
            return baseDamage;
        if (targetEntity.DurationMoveEffectComp == null)
            throw new InvalidOperationException($"Knockback target has no displacement component. target={targetEntity.LogicEntityId.Value}.");

        FixVector2 direction = LogicEntityFrameSnapshotService.GetRequiredPosition(targetEntity)
                               - LogicEntityFrameSnapshotService.GetRequiredPosition(hostEntity);
        if (FixVector2.SqrMagnitude(direction) == Fix64.Zero)
            direction = LogicEntityFrameSnapshotService.GetRequiredForward(hostEntity);
        direction = direction.GetNormalized();
        targetEntity.DurationMoveEffectComp.TryApplyKnockback(direction, totalLevel);
        return baseDamage;
    }
}

public sealed class PercentAttackBonusBuff : BuffCallback, ILogicDeterministicStateContributor
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
        var weapon = hostEntity?.WeaponComp?.Data;
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

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon != null)
            weapon.ApplyPercentAdd(WeaponStatId.Atk, -m_AppliedPercentAdd);

        m_Applied = false;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Percent.RawValue);
        hasher.Add(m_AppliedPercentAdd.RawValue);
        hasher.Add(m_Applied);
    }
}

public sealed class RevertibleMoveSpeedBonusBuff : BuffCallback, ILogicDeterministicStateContributor
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
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Bonus == Fix64.Zero)
            return;

        m_Modifier = PropertyDirectAdditiveModifier.Create(m_Bonus);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_Modifier, true);
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Modifier == null)
            return;

        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_Modifier, false);
        m_Modifier = null;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Bonus.RawValue);
        hasher.Add(m_Modifier != null);
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

public sealed class FirstHitPerTargetCriticalBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly HashSet<int> m_HitTargetIds = new HashSet<int>();
    private readonly List<int> m_DeterministicHitTargetIds = new List<int>();

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (!(target is IEntityContext targetEntity) || !targetEntity.LogicEntityId.IsValid)
            throw new InvalidOperationException($"FirstHitPerTargetCriticalBuff failed: target has no valid logic entity id. host={hostEntity?.CharacterKey}, target={target?.CharacterKey}.");

        if (!m_HitTargetIds.Add(targetEntity.LogicEntityId.Value))
            return baseDamage;

        return CriticalDamageUtility.ApplyCriticalDamage(hostEntity, baseDamage);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        m_DeterministicHitTargetIds.Clear();
        m_DeterministicHitTargetIds.AddRange(m_HitTargetIds);
        m_DeterministicHitTargetIds.Sort();
        hasher.Add(m_DeterministicHitTargetIds.Count);
        for (int i = 0; i < m_DeterministicHitTargetIds.Count; i++)
            hasher.Add(m_DeterministicHitTargetIds[i]);
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
        if (target is not IEntityContext creature || creature.CreatureProperties == null)
            return baseDamage;

        Fix64 max = creature.CreatureProperties.GetProperty(CreatureMainProperty.Health);
        if (max <= Fix64.Zero)
            return baseDamage;

        Fix64 hpPercent = creature.HealthValue / max * (Fix64)100;
        if (hpPercent < m_HpThresholdPercent)
            return baseDamage;

        return CriticalDamageUtility.ApplyCriticalDamage(hostEntity, baseDamage);
    }
}

public sealed class HealthDrainOverTimeBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_DamagePerSecond;
    private Fix64 m_ElapsedSeconds;

    public HealthDrainOverTimeBuff(Fix64 damagePerSecond)
    {
        m_DamagePerSecond = damagePerSecond;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (hostEntity == null || !hostEntity.Alive || m_DamagePerSecond <= Fix64.Zero || deltaTime <= Fix64.Zero)
            return;
        if (hostEntity.IsOutOfCombat)
            return;

        m_ElapsedSeconds += (Fix64)deltaTime;
        while (m_ElapsedSeconds >= Fix64.One && hostEntity != null && hostEntity.Alive)
        {
            m_ElapsedSeconds -= Fix64.One;
            DamageHelper.DoDirectDamage(hostEntity, m_DamagePerSecond, HealthModifyType.reduce);
        }
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_ElapsedSeconds.RawValue);
    }
}

public sealed class AmmoDepletedDeathBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_DelaySeconds;
    private Fix64 m_Timer;

    public AmmoDepletedDeathBuff(Fix64 delaySeconds)
    {
        m_DelaySeconds = delaySeconds;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (hostEntity == null || !hostEntity.Alive)
            return;

        var weaponComp = hostEntity.WeaponComp;
        if (weaponComp == null || !weaponComp.HasAmmunition || weaponComp.CurrentAmmo > 0)
        {
            m_Timer = Fix64.Zero;
            return;
        }

        m_Timer += (Fix64)deltaTime;
        if (m_Timer < m_DelaySeconds)
            return;

        DamageHelper.DoDirectDamage(hostEntity, hostEntity.HealthValue, HealthModifyType.reduce);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
    }
}

public sealed class NearbyEnemyAttackLockBuff : BuffCallback, ICapability, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanIntervalSeconds = Fix64.FromRaw(410);

    private readonly Fix64 m_Radius;
    private Fix64 m_ScanTimer;
    private bool m_AttackLocked;

    public NearbyEnemyAttackLockBuff(Fix64 radius)
    {
        m_Radius = radius;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (deltaTime <= Fix64.Zero)
            return;

        m_ScanTimer += (Fix64)deltaTime;
        if (m_ScanTimer < ScanIntervalSeconds)
            return;

        m_ScanTimer = Fix64.Zero;
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

        Fix64 radius = DistanceUnitConverter.ConvertToWorld(m_Radius);
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || ReferenceEquals(candidate, hostEntity))
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(hostEntity, candidate))
                continue;

            if (LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(hostEntity, candidate) <= radius)
                return true;
        }

        return false;
    }

    private void LockAttack()
    {
        if (m_AttackLocked)
            return;

        if (hostEntity == null || hostEntity.AtkComp == null)
            throw new InvalidOperationException($"NearbyEnemyAttackLockBuff.LockAttack failed: missing atkComp. host={hostEntity?.CharacterKey}.");

        hostEntity.LockComp(hostEntity.AtkComp, this);
        m_AttackLocked = true;
    }

    private void UnlockAttack()
    {
        if (!m_AttackLocked)
            return;

        if (hostEntity == null || hostEntity.AtkComp == null)
            throw new InvalidOperationException("NearbyEnemyAttackLockBuff.UnlockAttack failed: hostEntity or atkComp is null.");

        hostEntity.ResumeComp(hostEntity.AtkComp, this);
        m_AttackLocked = false;
    }

    public void ShutDown() { }
    public void Resume() { }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_ScanTimer.RawValue);
        hasher.Add(m_AttackLocked);
    }
}

public sealed class LateRiderChargeBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanIntervalSeconds = Fix64.FromRaw(410);

    private readonly Fix64 m_MinDistance;
    private readonly Fix64 m_MaxDistance;
    private readonly Fix64 m_MoveSpeedBonus;
    private readonly Fix64 m_AttackBonus;
    private readonly Fix64 m_CooldownSeconds;

    private Fix64 m_ScanTimer;
    private Fix64 m_CooldownTimer;
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
        Fix64 cooldownSeconds)
    {
        m_MinDistance = minDistance;
        m_MaxDistance = maxDistance;
        m_MoveSpeedBonus = moveSpeedBonus;
        m_AttackBonus = attackBonus;
        m_CooldownSeconds = Fix64.Max(Fix64.Zero, cooldownSeconds);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (deltaTime <= Fix64.Zero)
            return;

        if (m_CooldownTimer > Fix64.Zero)
            m_CooldownTimer = Fix64.Max(Fix64.Zero, m_CooldownTimer - (Fix64)deltaTime);

        if (m_Charging)
        {
            UpdateActiveCharge();
            return;
        }

        m_ScanTimer += (Fix64)deltaTime;
        if (m_ScanTimer < ScanIntervalSeconds || m_CooldownTimer > Fix64.Zero)
            return;

        m_ScanTimer = Fix64.Zero;
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

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_ScanTimer.RawValue);
        hasher.Add(m_CooldownTimer.RawValue);
        hasher.Add(m_Charging);
        hasher.Add(m_MoveApplied);
        hasher.Add(m_AttackApplied);
        hasher.Add(m_ChargeTarget != null && m_ChargeTarget.LogicEntityId.IsValid
            ? m_ChargeTarget.LogicEntityId.Value
            : 0);
    }

    private void TryEnterCharge()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("LateRiderChargeBuff.TryEnterCharge failed: hostEntity is null.");

        IEntityContext target = hostEntity.TargetComp?.CurrentTarget;
        if (!WeaponTargetRules.IsValidTargetForCurrentWeapon(hostEntity, target))
            return;

        Fix64 minDistance = DistanceUnitConverter.ConvertToWorld(m_MinDistance);
        Fix64 maxDistance = DistanceUnitConverter.ConvertToWorld(m_MaxDistance);
        Fix64 distance = LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(hostEntity, target);
        if (distance < minDistance || distance > maxDistance)
            return;

        EnterCharge(target);
    }

    private void UpdateActiveCharge()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("LateRiderChargeBuff.UpdateActiveCharge failed: hostEntity is null.");

        if (hostEntity.AtkComp != null && hostEntity.AtkComp.IsAttacking)
            return;

        if (!WeaponTargetRules.IsValidTargetForCurrentWeapon(hostEntity, m_ChargeTarget))
        {
            ExitCharge(true);
            return;
        }

        if (hostEntity.TargetComp?.CurrentTarget != m_ChargeTarget)
        {
            ExitCharge(true);
            return;
        }

        if (!hostEntity.CanRun(hostEntity.MoveComp) || !hostEntity.CanRun(hostEntity.AtkComp))
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
        m_ScanTimer = Fix64.Zero;
        if (startCooldown)
            m_CooldownTimer = m_CooldownSeconds;
    }

    private void ApplyMoveBonus()
    {
        if (m_MoveSpeedBonus == Fix64.Zero)
            return;

        var propertyManager = hostEntity?.CreatureProperties;
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

        var propertyManager = hostEntity?.CreatureProperties;
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

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null)
            throw new InvalidOperationException($"LateRiderChargeBuff.ApplyAttackBonus failed: missing weapon. host={hostEntity?.CharacterKey}.");

        weapon.ApplyAdditive(WeaponStatId.Atk, m_AttackBonus);
        m_AttackApplied = true;
    }

    private void RemoveAttackBonus()
    {
        if (!m_AttackApplied)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null)
            throw new InvalidOperationException($"LateRiderChargeBuff.RemoveAttackBonus failed: missing weapon. host={hostEntity?.CharacterKey}.");

        weapon.ApplyAdditive(WeaponStatId.Atk, -m_AttackBonus);
        m_AttackApplied = false;
    }
}
