using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InjectionActiveSkillSO", menuName = "Skills/Active/Injection")]
public sealed class InjectionActiveSkillSO : InstantActiveSkillSO
{
    protected override void ApplyInstant(MAEntity caster)
    {
        if (caster == null)
            throw new InvalidOperationException($"Injection caster missing. skillId={skillId}");

        float radius = GetAreaRangeWorld();
        float duration = GetDurationSeconds();
        if (radius <= 0f || duration <= 0f)
            throw new InvalidOperationException($"Injection values invalid. skillId={skillId}");

        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is not MAEntity target)
                continue;
            if (!target.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(caster, target))
                continue;
            if (Vector3.Distance(caster.Position, target.Position) > radius)
                continue;
            if (target.BuffComp == null)
                throw new InvalidOperationException($"Injection target missing BuffComp. target={target.CharacterKey}");

            target.BuffComp.RemoveBuff(GetFearBuffId(target.Id));
            target.BuffComp.AddBuff(
                BuffData.Create(GetFearBuffId(target.Id), duration, false, 1, new List<BuffCallback> { new FearMoveAwayBuff(caster) }),
                target);
        }
    }

    private string GetFearBuffId(int targetId)
    {
        return $"skill_active_{skillId}_fear_{targetId}";
    }
}

public sealed class FearMoveAwayBuff : BuffCallback, ICapability
{
    private readonly IEntityContext m_Source;
    private bool m_MoveLocked;

    public FearMoveAwayBuff(IEntityContext source)
    {
        m_Source = source;
    }

    public override bool IsNegativeStatus => true;

    public override void OnAdd()
    {
        if (hostEntity == null || hostEntity.moveComp == null)
            throw new InvalidOperationException("FearMoveAwayBuff requires host moveComp.");

        hostEntity.LockComp(hostEntity.moveComp, this);
        m_MoveLocked = true;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (hostEntity == null || m_Source == null || hostEntity.durationMoveEffectComp == null)
            return;

        FixVector2 direction = hostEntity.LogicFramePositionFixed() - m_Source.LogicFramePositionFixed();
        if (FixVector2.SqrMagnitude(direction) == Fix64.Zero)
            direction = LogicEntityFrameSnapshotService.GetRequiredForward(hostEntity);

        direction = direction.GetNormalized();
        hostEntity.durationMoveEffectComp.StartDurationAdditionalMove((Fix64)0.1f, direction * (Fix64)3);
    }

    public override void OnRemove()
    {
        if (!m_MoveLocked || hostEntity == null || hostEntity.moveComp == null)
            return;

        hostEntity.ResumeComp(hostEntity.moveComp, this);
        m_MoveLocked = false;
    }

    public void ShutDown() { }
    public void Resume() { }
}
