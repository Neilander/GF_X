using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;

public abstract class PassiveSkillSO : SkillEffectSO
{
    internal PassiveSkillSO CreateRuntimeSnapshot()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Passive skill assets cannot be snapshotted during a logic frame.");

        SkillRuntimeSnapshotValidation.ValidateReferenceFields(this);
        PassiveSkillSO snapshot = Instantiate(this);
        snapshot.hideFlags = HideFlags.HideAndDontSave;
        return snapshot;
    }

    public abstract void Apply(IEntityContext owner);
    public abstract void Remove(IEntityContext owner);

    protected string BuffId => $"skill_passive_{skillId}";

    protected void ReplaceBuff(IEntityContext owner, BuffCallback module)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        if (owner.BuffComp == null)
            throw new InvalidOperationException($"Passive skill requires BuffComp. skillId={skillId}, owner={owner.CharacterKey}");

        owner.BuffComp.RemoveBuff(BuffId);
        var buffData = BuffData.Create(
            BuffId,
            Fix64.Zero,
            true,
            1,
            new List<BuffCallback> { module });
        owner.BuffComp.AddBuff(buffData, owner);
    }

    protected void RemoveBuff(IEntityContext owner)
    {
        if (owner == null || owner.BuffComp == null)
            return;

        owner.BuffComp.RemoveBuff(BuffId);
    }
}

public sealed class SkillCurrentHealthDamageBuff : BuffCallback
{
    private readonly Fix64 m_Percent;

    public SkillCurrentHealthDamageBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (target is not IEntityContext entity || m_Percent <= Fix64.Zero)
            return baseDamage;

        return baseDamage + entity.HealthValue * m_Percent / (Fix64)100;
    }
}

public sealed class SkillCheerSquadBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 ScanIntervalSeconds = Fix64.FromRaw(1024);

    private readonly int m_UnitsPerStep;
    private readonly Fix64 m_AttackPerStep;
    private readonly Fix64 m_AttackSpeedPercentPerStep;
    private Fix64 m_Timer;
    private int m_AppliedSteps;
    private Fix64 m_AppliedAttackSpeedFactor = Fix64.One;

    public SkillCheerSquadBuff(int unitsPerStep, Fix64 attackPerStep, Fix64 attackSpeedPercentPerStep)
    {
        m_UnitsPerStep = Math.Max(1, unitsPerStep);
        m_AttackPerStep = attackPerStep;
        m_AttackSpeedPercentPerStep = attackSpeedPercentPerStep;
    }

    public override void OnAdd()
    {
        Recalculate();
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += deltaTime;
        if (m_Timer < ScanIntervalSeconds)
            return;

        m_Timer = Fix64.Zero;
        Recalculate();
    }

    public override void OnRemove()
    {
        ApplySteps(0);
    }

    private void Recalculate()
    {
        int friendlyCount = 0;
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext entity = all[i];
            if (entity != null && entity.Alive && hostEntity != null && entity.Side == hostEntity.Side)
                friendlyCount++;
        }

        ApplySteps(friendlyCount / m_UnitsPerStep);
    }

    private void ApplySteps(int steps)
    {
        if (steps == m_AppliedSteps || hostEntity?.WeaponComp?.Data == null)
            return;

        Weapon weapon = hostEntity.WeaponComp.Data;
        int delta = steps - m_AppliedSteps;
        weapon.ApplyAdditive(WeaponStatId.Atk, m_AttackPerStep * delta);

        if (m_AppliedAttackSpeedFactor != Fix64.One)
            weapon.ApplyMultiplier(WeaponStatId.Interval, Fix64.One / m_AppliedAttackSpeedFactor);

        Fix64 attackSpeedPercent = m_AttackSpeedPercentPerStep * steps;
        m_AppliedAttackSpeedFactor = attackSpeedPercent != Fix64.Zero
            ? Fix64.One / (Fix64.One + attackSpeedPercent / (Fix64)100)
            : Fix64.One;

        if (m_AppliedAttackSpeedFactor != Fix64.One)
            weapon.ApplyMultiplier(WeaponStatId.Interval, m_AppliedAttackSpeedFactor);

        m_AppliedSteps = steps;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_AppliedSteps);
        hasher.Add(m_AppliedAttackSpeedFactor.RawValue);
    }
}

public sealed class SkillHigherHealthSplashBuff : BuffCallback
{
    private readonly Fix64 m_Radius;
    private readonly Fix64 m_DamagePercent;
    private bool m_ApplyingSplash;

    public SkillHigherHealthSplashBuff(Fix64 radius, Fix64 damagePercent)
    {
        m_Radius = radius;
        m_DamagePercent = damagePercent;
    }

    public override void OnAttackCompleted(IEntityContext target)
    {
        if (m_ApplyingSplash || hostEntity == null || target is not IEntityContext mainTarget)
            return;

        Fix64 mainMax = mainTarget.CreatureProperties.GetProperty(CreatureMainProperty.Health);
        if (mainMax <= Fix64.Zero)
            return;

        Fix64 mainRatio = mainTarget.HealthValue / mainMax;
        Fix64 radius = (m_Radius);
        var all = EntityRegistry.AllEntities;
        m_ApplyingSplash = true;
        try
        {
            for (int i = 0; i < all.Count; i++)
            {
                IEntityContext candidate = all[i];
                if (candidate == null || ReferenceEquals(candidate, target))
                    continue;
                if (!candidate.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(hostEntity, candidate))
                    continue;
                if (FixVector2.Distance(
                        candidate.LogicFramePositionFixed(),
                        mainTarget.LogicFramePositionFixed()) > radius)
                    continue;

                Fix64 candidateMax = candidate.CreatureProperties.GetProperty(CreatureMainProperty.Health);
                if (candidateMax <= Fix64.Zero || candidate.HealthValue / candidateMax <= mainRatio)
                    continue;

                Fix64 damage = hostEntity.WeaponComp.Data.Atk * m_DamagePercent / (Fix64)100;
                DamageHelper.DoDamage(candidate, new Damage(hostEntity, damage, HealthModifyType.reduce), hostEntity);
            }
        }
        finally
        {
            m_ApplyingSplash = false;
        }
    }
}
