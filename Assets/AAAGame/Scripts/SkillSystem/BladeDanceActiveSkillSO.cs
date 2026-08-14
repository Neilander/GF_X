using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BladeDanceActiveSkillSO", menuName = "Skills/Active/Blade Dance")]
public sealed class BladeDanceActiveSkillSO : InstantActiveSkillSO
{
    protected override void ApplyInstant(IEntityContext caster)
    {
        Fix64 duration = GetDurationLogicTime();
        Fix64 attackSpeedPercent = GetValue(0);
        Fix64 lifeStealPercent = GetValue(1);
        if (duration <= Fix64.Zero)
            throw new InvalidOperationException($"BladeDance duration invalid. skillId={skillId}");

        AddTimedBuff(
            caster,
            new List<BuffCallback>
            {
                new AttackSpeedBonusBuff(attackSpeedPercent),
                new AttackLifeStealPercentBuff(lifeStealPercent),
            },
            duration);
    }

    private void AddTimedBuff(IEntityContext caster, List<BuffCallback> modules, Fix64 duration)
    {
        if (caster == null || caster.BuffComp == null)
            throw new InvalidOperationException($"Active skill requires BuffComp. skillId={skillId}");

        string buffId = $"skill_active_{skillId}";
        caster.BuffComp.RemoveBuff(buffId);
        caster.BuffComp.AddBuff(BuffData.Create(buffId, duration, false, 1, modules), caster);
    }
}
