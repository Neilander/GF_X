using System;
using UnityEngine;

[CreateAssetMenu(fileName = "SweepActiveSkillSO", menuName = "Skills/Active/Sweep")]
public sealed class SweepActiveSkillSO : InstantActiveSkillSO
{
    protected override void ApplyInstant(IEntityContext caster)
    {
        if (caster == null)
            throw new InvalidOperationException($"Sweep caster missing. skillId={skillId}");

        Fix64 damage = GetValue(0);
        Fix64 radius = GetAreaRangeWorldFixed();
        if (damage <= Fix64.Zero || radius <= Fix64.Zero)
            throw new InvalidOperationException($"Sweep values invalid. skillId={skillId}");

        ApplyArea(caster, damage, radius);
    }

    internal static void ApplyArea(IEntityContext caster, Fix64 damage, Fix64 radius)
    {
        if (caster == null)
            throw new ArgumentNullException(nameof(caster));
        if (damage <= Fix64.Zero || radius <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(damage), "Sweep damage and radius must be positive.");

        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext target = all[i]
                ?? throw new InvalidOperationException("Sweep encountered a null registered entity.");
            if (!target.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(caster, target))
                continue;
            if (FixVector2.Distance(caster.LogicFramePositionFixed(), target.LogicFramePositionFixed()) > radius)
                continue;

            DamageHelper.DoDamage(target, new Damage(caster, damage, HealthModifyType.reduce), caster);
        }
    }
}
