using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;

public abstract class PositionAreaRefreshBuff : BuffCallback
{
    private static readonly Fix64 ScanIntervalSeconds = (Fix64)0.2f;

    private readonly FixVector2 m_Center;
    private readonly Fix64 m_Radius;
    private Fix64 m_Timer;

    protected PositionAreaRefreshBuff(FixVector2 center, Fix64 radius)
    {
        m_Center = center;
        m_Radius = radius;
    }

    public override void OnAdd()
    {
        RefreshTargets();
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += deltaTime;
        if (m_Timer < ScanIntervalSeconds)
            return;

        m_Timer = Fix64.Zero;
        RefreshTargets();
    }

    private void RefreshTargets()
    {
        if (hostEntity == null)
            throw new InvalidOperationException($"{GetType().Name} requires hostEntity.");

        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new InvalidOperationException($"{GetType().Name} requires EntityRegistry.AllEntities.");

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext target = all[i];
            if (target == null)
                throw new InvalidOperationException($"{GetType().Name} encountered a null entity in EntityRegistry.");
            if (!target.Alive)
                continue;
            if (!IsTargetValid(hostEntity, target))
                continue;
            if (FixVector2.Distance(m_Center, target.LogicFramePositionFixed()) > m_Radius)
                continue;

            RefreshTarget(target);
        }
    }

    protected abstract bool IsTargetValid(IEntityContext caster, IEntityContext target);
    protected abstract void RefreshTarget(IEntityContext target);
}

public sealed class FriendlyAttackSpeedAreaBuff : PositionAreaRefreshBuff
{
    private readonly Fix64 m_AttackSpeedPercent;
    private readonly Fix64 m_TargetBuffDuration;
    private readonly string m_SkillId;

    public FriendlyAttackSpeedAreaBuff(FixVector2 center, Fix64 radius, Fix64 attackSpeedPercent, Fix64 targetBuffDuration, string skillId)
        : base(center, radius)
    {
        m_AttackSpeedPercent = attackSpeedPercent;
        m_TargetBuffDuration = targetBuffDuration;
        m_SkillId = skillId;
    }

    protected override bool IsTargetValid(IEntityContext caster, IEntityContext target)
    {
        return caster.Side == target.Side;
    }

    protected override void RefreshTarget(IEntityContext target)
    {
        if (target.BuffComp == null)
            throw new InvalidOperationException($"FriendlyAttackSpeedAreaBuff target missing BuffComp. target={target.CharacterKey}");

        string buffId = $"skill_area_target_{m_SkillId}";
        target.BuffComp.RemoveBuff(buffId);
        target.BuffComp.AddBuff(
            BuffData.Create(buffId, m_TargetBuffDuration, false, 1, new List<BuffCallback> { new AttackSpeedBonusBuff(m_AttackSpeedPercent) }),
            target);
    }
}

public sealed class EnemyBlindAreaBuff : PositionAreaRefreshBuff
{
    private readonly Fix64 m_MissChancePercent;
    private readonly Fix64 m_TargetBuffDuration;
    private readonly string m_SkillId;

    public EnemyBlindAreaBuff(FixVector2 center, Fix64 radius, Fix64 missChancePercent, Fix64 targetBuffDuration, string skillId)
        : base(center, radius)
    {
        m_MissChancePercent = missChancePercent;
        m_TargetBuffDuration = targetBuffDuration;
        m_SkillId = skillId;
    }

    protected override bool IsTargetValid(IEntityContext caster, IEntityContext target)
    {
        return target.IsAttackTargetable() && EntityCombatTeamHelper.IsEnemy(caster, target);
    }

    protected override void RefreshTarget(IEntityContext target)
    {
        if (target.BuffComp == null)
            throw new InvalidOperationException($"EnemyBlindAreaBuff target missing BuffComp. target={target.CharacterKey}");

        string buffId = $"skill_area_target_{m_SkillId}";
        target.BuffComp.RemoveBuff(buffId);
        target.BuffComp.AddBuff(
            BuffData.Create(buffId, m_TargetBuffDuration, false, 1, new List<BuffCallback> { new BlindAttackMissBuff(m_MissChancePercent) }),
            target);
    }
}
