using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InjectionActiveSkillSO", menuName = "Skills/Active/Injection")]
public sealed class InjectionActiveSkillSO : InstantActiveSkillSO
{
    protected override void ApplyInstant(IEntityContext caster)
    {
        if (caster == null)
            throw new InvalidOperationException($"Injection caster missing. skillId={skillId}");

        Fix64 radius = GetAreaRangeWorldFixed();
        Fix64 duration = GetDurationLogicTime();
        if (radius <= Fix64.Zero || duration <= Fix64.Zero)
            throw new InvalidOperationException($"Injection values invalid. skillId={skillId}");

        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext target = all[i];
            if (target == null)
                throw new InvalidOperationException("Injection encountered a null entity in EntityRegistry.");
            if (!target.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(caster, target))
                continue;
            if (FixVector2.Distance(caster.LogicFramePositionFixed(), target.LogicFramePositionFixed()) > radius)
                continue;
            if (target.BuffComp == null)
                throw new InvalidOperationException($"Injection target missing BuffComp. target={target.CharacterKey}");

            target.BuffComp.RemoveBuff(GetFearBuffId(target.LogicEntityId.Value));
            target.BuffComp.AddBuff(
                BuffData.Create(GetFearBuffId(target.LogicEntityId.Value), duration, false, 1, new List<BuffCallback> { new FearMoveAwayBuff(caster) }),
                target);
        }
    }

    private string GetFearBuffId(int targetId)
    {
        return $"skill_active_{skillId}_fear_{targetId}";
    }
}

public sealed class FearMoveAwayBuff : BuffCallback, ICapability, ILogicDeterministicStateContributor
{
    private readonly IEntityContext m_Source;
    private bool m_MoveLocked;

    public FearMoveAwayBuff(IEntityContext source)
    {
        m_Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override bool IsNegativeStatus => true;

    public override void OnAdd()
    {
        if (hostEntity == null || hostEntity.MoveComp == null)
            throw new InvalidOperationException("FearMoveAwayBuff requires host moveComp.");

        hostEntity.LockComp(hostEntity.MoveComp, this);
        m_MoveLocked = true;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (hostEntity == null || hostEntity.DurationMoveEffectComp == null)
            throw new InvalidOperationException("FearMoveAwayBuff requires host entity and duration move component.");

        FixVector2 direction = hostEntity.LogicFramePositionFixed() - m_Source.LogicFramePositionFixed();
        if (FixVector2.SqrMagnitude(direction) == Fix64.Zero)
            direction = LogicEntityFrameSnapshotService.GetRequiredForward(hostEntity);

        direction = direction.GetNormalized();
        hostEntity.DurationMoveEffectComp.StartDurationAdditionalMove(Fix64.FromRaw(410), direction * (Fix64)3);
    }

    public override void OnRemove()
    {
        if (!m_MoveLocked || hostEntity == null || hostEntity.MoveComp == null)
            return;

        hostEntity.ResumeComp(hostEntity.MoveComp, this);
        m_MoveLocked = false;
    }

    public void ShutDown() { }
    public void Resume() { }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (!m_Source.LogicEntityId.IsValid)
            throw new InvalidOperationException("FearMoveAwayBuff source has an invalid logic entity id.");
        hasher.Add(m_Source.LogicEntityId.Value);
        hasher.Add(m_MoveLocked);
    }
}
