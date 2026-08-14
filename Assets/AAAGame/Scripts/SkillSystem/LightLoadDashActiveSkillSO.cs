using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LightLoadDashActiveSkillSO", menuName = "Skills/Active/Light Load Dash")]
public sealed class LightLoadDashActiveSkillSO : TargetPositionActiveSkillSO
{
    private static Fix64 EndpointSnapDistance => DistanceUnitConverter.ConvertToWorld((Fix64)50);

    protected override void ApplyAtPosition(
        IEntityContext caster,
        FixVector2 position,
        IReadOnlyList<ISelectable> selectedTargets)
    {
        if (caster is not LogicEntityState state)
            throw new InvalidOperationException($"LightLoad requires an authoritative logic entity. skillId={skillId}");

        Fix64 collisionRadius = DistanceUnitConverter.ConvertToWorld(
            caster.GetProperty(CreatureMainProperty.CollisionRadius));
        int agentTypeId = ClusterSpawnSystem.ResolveAgentTypeId(UnitType.Unit_Hero);
        if (!FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
                position,
                agentTypeId,
                EndpointSnapDistance,
                collisionRadius,
                out FixVector2 destination))
        {
            throw new InvalidOperationException($"LightLoad destination is not legal. skillId={skillId}, destination={position}");
        }
        FixVector2 start = caster.LogicFramePositionFixed();
        if (!FlowFieldCrowdMovementSystem.HasBaseWalkableLineFixed(start, destination, agentTypeId))
            throw new InvalidOperationException($"LightLoad path crosses non-land cells. skillId={skillId}, from={start}, to={destination}");

        state.TeleportTo(destination);
        ApplyAreaEffect(caster, destination);
    }

    private void ApplyAreaEffect(IEntityContext caster, FixVector2 center)
    {
        Fix64 damage = GetValue(0);
        Fix64 radius = GetAreaRangeWorldFixed();
        Fix64 stunDuration = GetDurationLogicTime();
        if (damage <= Fix64.Zero || radius <= Fix64.Zero || stunDuration <= Fix64.Zero)
            throw new InvalidOperationException($"LightLoad values invalid. skillId={skillId}");

        IList<IEntityContext> all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext target = all[i]
                                    ?? throw new InvalidOperationException("LightLoad encountered a null registered entity.");
            if (!target.Alive || !target.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(caster, target))
                continue;
            if (FixVector2.Distance(center, target.LogicFramePositionFixed()) > radius)
                continue;

            DamageHelper.DoDamage(target, new Damage(caster, damage, HealthModifyType.reduce), caster);
            if (!target.Alive)
                continue;

            string buffId = $"skill_stun_{skillId}_{caster.LogicEntityId.Value}";
            target.BuffComp.RemoveBuff(buffId);
            target.BuffComp.AddBuff(
                BuffData.Create(
                    buffId,
                    stunDuration,
                    false,
                    1,
                    new List<BuffCallback> { new SkillStunBuff() }),
                target);
        }
    }
}

public sealed class SkillStunBuff : BuffCallback, ICapability, ILogicDeterministicStateContributor
{
    private bool m_Applied;

    public override bool IsNegativeStatus => true;

    public override void OnAdd()
    {
        if (hostEntity == null)
            throw new InvalidOperationException("SkillStunBuff requires a host entity.");
        hostEntity.LockComp(hostEntity.MoveComp, this);
        hostEntity.LockComp(hostEntity.AtkComp, this);
        m_Applied = true;
    }

    public override void OnRemove()
    {
        if (!m_Applied)
            return;
        if (hostEntity == null)
            throw new InvalidOperationException("SkillStunBuff host disappeared before removal.");
        hostEntity.ResumeComp(hostEntity.AtkComp, this);
        hostEntity.ResumeComp(hostEntity.MoveComp, this);
        m_Applied = false;
    }

    public void ShutDown() { }
    public void Resume() { }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Applied);
    }
}
