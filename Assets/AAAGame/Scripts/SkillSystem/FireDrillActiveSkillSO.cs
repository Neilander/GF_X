using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FireDrillActiveSkillSO", menuName = "Skills/Active/Fire Drill")]
public sealed class FireDrillActiveSkillSO : TargetPositionActiveSkillSO
{
    private static readonly Fix64 RefreshDurationPaddingSeconds = Fix64.FromRaw(820);

    protected override void ApplyAtPosition(IEntityContext caster, FixVector2 position, IReadOnlyList<ISelectable> selectedTargets)
    {
        Fix64 radius = GetAreaRangeWorldFixed();
        Fix64 missChancePercent = GetValue(0);
        Fix64 duration = GetDurationLogicTime();
        if (radius <= Fix64.Zero || duration <= Fix64.Zero)
            throw new InvalidOperationException($"FireDrill values invalid. skillId={skillId}");

        AddCasterAreaBuff(
            caster,
            new EnemyBlindAreaBuff(position, radius, missChancePercent, duration + RefreshDurationPaddingSeconds, skillId),
            duration);
    }

    private void AddCasterAreaBuff(IEntityContext caster, BuffCallback module, Fix64 duration)
    {
        if (caster == null || caster.BuffComp == null)
            throw new InvalidOperationException($"Active area skill requires caster BuffComp. skillId={skillId}");

        string buffId = $"skill_active_area_{skillId}";
        caster.BuffComp.RemoveBuff(buffId);
        caster.BuffComp.AddBuff(BuffData.Create(buffId, duration, false, 1, new List<BuffCallback> { module }), caster);
    }
}
