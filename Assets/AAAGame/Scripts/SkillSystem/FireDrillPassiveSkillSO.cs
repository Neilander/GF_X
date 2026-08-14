using System;
using UnityEngine;

[CreateAssetMenu(fileName = "FireDrillPassiveSkillSO", menuName = "Skills/Passive/Fire Drill")]
public sealed class FireDrillPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(IEntityContext owner)
    {
        Fix64 attackPercentPerStack = GetValue(0);
        Fix64 maxStacksValue = GetValue(1);
        Fix64 radius = GetAreaRangeWorldFixed();
        int maxStacks = checked((int)maxStacksValue);
        if (attackPercentPerStack <= Fix64.Zero || maxStacks <= 0 || radius <= Fix64.Zero)
            throw new InvalidOperationException($"FireDrill values invalid. skillId={skillId}");

        ReplaceBuff(owner, new FireDrillDeathStackBuff(skillId, attackPercentPerStack, maxStacks, radius));
    }

    public override void Remove(IEntityContext owner)
    {
        RemoveBuff(owner);
    }
}

public sealed class FireDrillDeathStackBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly string m_SkillId;
    private readonly Fix64 m_AttackPercentPerStack;
    private readonly int m_MaxStacks;
    private readonly Fix64 m_Radius;
    private Fix64 m_AppliedAttackPercent;

    public FireDrillDeathStackBuff(string skillId, Fix64 attackPercentPerStack, int maxStacks, Fix64 radius)
    {
        m_SkillId = skillId;
        m_AttackPercentPerStack = attackPercentPerStack;
        m_MaxStacks = maxStacks;
        m_Radius = radius;
    }

    public override void OnAdd()
    {
        if (hostEntity?.WeaponComp?.Data == null)
            throw new InvalidOperationException($"FireDrill requires a host weapon. skillId={m_SkillId}");

        LogicUnitDeathEventService.UnitDied += OnUnitDied;
        ApplyStackAttack(SkillRuntimeDataModel.GetRuntimeStackCount(m_SkillId));
    }

    public override void OnRemove()
    {
        LogicUnitDeathEventService.UnitDied -= OnUnitDied;
        ApplyStackAttack(0);
    }

    private void OnUnitDied(IEntityContext victim)
    {
        if (victim == null)
            throw new InvalidOperationException($"FireDrill received a null death event. skillId={m_SkillId}");
        if (!EntityCombatTeamHelper.IsEnemy(hostEntity, victim))
            return;
        if (FixVector2.Distance(hostEntity.LogicFramePositionFixed(), victim.LogicFramePositionFixed()) > m_Radius)
            return;

        int current = SkillRuntimeDataModel.GetRuntimeStackCount(m_SkillId);
        if (current >= m_MaxStacks)
            return;

        int next = current + 1;
        ApplyStackAttack(next);
        SkillRuntimeDataModel.SetRuntimeStackCount(m_SkillId, next);
    }

    private void ApplyStackAttack(int stackCount)
    {
        Weapon weapon = hostEntity?.WeaponComp?.Data
                        ?? throw new InvalidOperationException($"FireDrill host weapon disappeared. skillId={m_SkillId}");
        if (m_AppliedAttackPercent != Fix64.Zero)
            weapon.ApplyPercentAdd(WeaponStatId.Atk, -m_AppliedAttackPercent / (Fix64)100);

        m_AppliedAttackPercent = m_AttackPercentPerStack * (Fix64)stackCount;
        if (m_AppliedAttackPercent != Fix64.Zero)
            weapon.ApplyPercentAdd(WeaponStatId.Atk, m_AppliedAttackPercent / (Fix64)100);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_SkillId);
        hasher.Add(m_AttackPercentPerStack.RawValue);
        hasher.Add(m_MaxStacks);
        hasher.Add(m_Radius.RawValue);
        hasher.Add(m_AppliedAttackPercent.RawValue);
    }
}
