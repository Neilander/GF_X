using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BladeDanceActiveSkillSO", menuName = "Skills/Active/Blade Dance")]
public sealed class BladeDanceActiveSkillSO : InstantActiveSkillSO
{
    protected override void ApplyInstant(MAEntity caster)
    {
        float duration = GetDurationSeconds();
        Fix64 attackSpeedPercent = GetValue(0);
        if (duration <= 0f)
            throw new InvalidOperationException($"BladeDance duration invalid. skillId={skillId}");

        AddTimedBuff(caster, new AttackSpeedBonusBuff(attackSpeedPercent), duration);
    }

    private void AddTimedBuff(MAEntity caster, BuffCallback module, float duration)
    {
        if (caster == null || caster.BuffComp == null)
            throw new InvalidOperationException($"Active skill requires BuffComp. skillId={skillId}");

        string buffId = $"skill_active_{skillId}";
        caster.BuffComp.RemoveBuff(buffId);
        caster.BuffComp.AddBuff(BuffData.Create(buffId, duration, false, 1, new List<BuffCallback> { module }), caster);
    }
}
