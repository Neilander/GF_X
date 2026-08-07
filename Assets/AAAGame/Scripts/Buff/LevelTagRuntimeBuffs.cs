using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;

public sealed class WeaponTypeIncomingDamageReductionBuff : BuffCallback
{
    private readonly Fix64 m_ReductionPercent;
    private readonly bool m_Ranged;

    public WeaponTypeIncomingDamageReductionBuff(Fix64 reductionPercent, bool ranged)
    {
        m_ReductionPercent = reductionPercent;
        m_Ranged = ranged;
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (modType != HealthModifyType.reduce || baseDamage <= Fix64.Zero || m_ReductionPercent <= Fix64.Zero)
            return baseDamage;
        if (attacker?.WeaponComp?.Data == null)
            return baseDamage;

        WeaponType weaponType = attacker.WeaponComp.Data.Type;
        if (WeaponTargetRules.IsHealingWeapon(weaponType))
            return baseDamage;

        bool matched = m_Ranged ? IsRangedWeapon(weaponType) : IsMeleeWeapon(weaponType);
        if (!matched)
            return baseDamage;

        Fix64 multiplier = Fix64.One - m_ReductionPercent / (Fix64)100;
        return baseDamage * (multiplier < Fix64.Zero ? Fix64.Zero : multiplier);
    }

    private static bool IsRangedWeapon(WeaponType weaponType)
    {
        return weaponType == WeaponType.Projectile
               || weaponType == WeaponType.InstantRanged
               || weaponType == WeaponType.CleaveRanged
               || weaponType == WeaponType.SelfAoE
               || weaponType == WeaponType.Special;
    }

    private static bool IsMeleeWeapon(WeaponType weaponType)
    {
        return weaponType == WeaponType.Melee || weaponType == WeaponType.CleaveMelee;
    }
}

public sealed class AttackLifeStealPercentBuff : BuffCallback
{
    private readonly Fix64 m_Percent;

    public AttackLifeStealPercentBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (m_Percent <= Fix64.Zero || baseDamage <= Fix64.Zero)
            return baseDamage;
        if (hostEntity?.WeaponComp?.Data == null || WeaponTargetRules.IsHealingWeapon(hostEntity.WeaponComp.Data.Type))
            return baseDamage;
        if (target is not IEntityContext targetContext || !EntityCombatTeamHelper.IsEnemy(hostEntity, targetContext))
            return baseDamage;
        if (hostEntity == null)
            return baseDamage;

        hostEntity.Heal(baseDamage * m_Percent / (Fix64)100);
        return baseDamage;
    }
}

public sealed class DayScalingHeroStatsBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 RefreshInterval = Fix64.FromRaw(1024);

    private readonly Fix64 m_AttackPercentPerDay;
    private readonly Fix64 m_HealthPercentPerDay;
    private Fix64 m_Timer;
    private int m_AppliedDays = -1;
    private Fix64 m_AppliedAttackPercent;
    private Fix64 m_AppliedHealthPercent;
    private IPropertyModifier m_HealthModifier;

    public DayScalingHeroStatsBuff(Fix64 attackPercentPerDay, Fix64 healthPercentPerDay)
    {
        m_AttackPercentPerDay = attackPercentPerDay;
        m_HealthPercentPerDay = healthPercentPerDay;
    }

    public override void OnAdd()
    {
        Refresh(true);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < RefreshInterval)
            return;

        m_Timer = Fix64.Zero;
        Refresh(false);
    }

    public override void OnRemove()
    {
        ApplyAttackPercent(Fix64.Zero);
        ApplyHealthPercent(Fix64.Zero);
        m_AppliedDays = -1;
    }

    private void Refresh(bool force)
    {
        int daysPassed = Math.Max(0, InGameDataModel.GetValue(IngameValueType.Day) - 1);
        if (!force && daysPassed == m_AppliedDays)
            return;

        m_AppliedDays = daysPassed;
        ApplyAttackPercent(m_AttackPercentPerDay * (Fix64)daysPassed);
        ApplyHealthPercent(m_HealthPercentPerDay * (Fix64)daysPassed);
    }

    private void ApplyAttackPercent(Fix64 percent)
    {
        Weapon weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null)
            return;

        if (m_AppliedAttackPercent != Fix64.Zero)
            weapon.ApplyPercentAdd(WeaponStatId.Atk, -m_AppliedAttackPercent / (Fix64)100);

        m_AppliedAttackPercent = percent;
        if (m_AppliedAttackPercent != Fix64.Zero)
            weapon.ApplyPercentAdd(WeaponStatId.Atk, m_AppliedAttackPercent / (Fix64)100);
    }

    private void ApplyHealthPercent(Fix64 percent)
    {
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null)
            return;

        if (m_HealthModifier != null)
        {
            propertyManager.ModifyMainPropertyMul(CreatureMainProperty.Health, NormalBaseValueTp.Buff, m_HealthModifier, false);
            m_HealthModifier = null;
        }

        m_AppliedHealthPercent = percent;
        if (m_AppliedHealthPercent == Fix64.Zero)
            return;

        m_HealthModifier = PropertyDirectAdditiveModifier.Create(m_AppliedHealthPercent / (Fix64)100);
        propertyManager.ModifyMainPropertyMul(CreatureMainProperty.Health, NormalBaseValueTp.Buff, m_HealthModifier, true);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_AttackPercentPerDay.RawValue);
        hasher.Add(m_HealthPercentPerDay.RawValue);
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_AppliedDays);
        hasher.Add(m_AppliedAttackPercent.RawValue);
        hasher.Add(m_AppliedHealthPercent.RawValue);
        hasher.Add(m_HealthModifier != null);
    }
}

public sealed class NegativeStatusMarkerBuff : BuffCallback
{
    public override bool IsNegativeStatus => true;
}

public sealed class CapturedStrongholdTrainingProviderBuff : BuffCallback, ISourceBuildingUnitBuffProvider
{
    public int CaptureDay { get; }
    public int DurationDays { get; }
    public Fix64 AttackPercent { get; }
    public Fix64 HealthPercent { get; }

    public CapturedStrongholdTrainingProviderBuff(int captureDay, int durationDays, Fix64 attackPercent, Fix64 healthPercent)
    {
        CaptureDay = Math.Max(1, captureDay);
        DurationDays = Math.Max(0, durationDays);
        AttackPercent = attackPercent;
        HealthPercent = healthPercent;
    }

    public bool CanProvideUnitBuffs()
    {
        if (DurationDays <= 0)
            return false;

        int currentDay = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
        return currentDay - CaptureDay < DurationDays;
    }

    public void CreateUnitBuffModules(List<BuffCallback> modules)
    {
        if (modules == null || !CanProvideUnitBuffs())
            return;

        if (AttackPercent != Fix64.Zero)
            modules.Add(new PercentAttackBonusBuff(AttackPercent));
        if (HealthPercent != Fix64.Zero)
            modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, HealthPercent));
    }
}
