using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "JustPassingByActiveSkillSO", menuName = "Skills/Active/Just Passing By")]
public sealed class JustPassingByActiveSkillSO : TargetPositionActiveSkillSO
{
    private const float RefreshDurationPaddingSeconds = 0.2f;

    protected override void ApplyAtPosition(MAEntity caster, Vector3 position, IReadOnlyList<ISelectable> selectedTargets)
    {
        float radius = GetAreaRangeWorld();
        Fix64 attackSpeedPercent = GetValue(0);
        float duration = GetDurationSeconds();
        if (radius <= 0f || duration <= 0f)
            throw new InvalidOperationException($"JustPassingBy values invalid. skillId={skillId}");

        AddCasterAreaBuff(
            caster,
            new FriendlyAttackSpeedAreaBuff(position, radius, attackSpeedPercent, duration + RefreshDurationPaddingSeconds, skillId),
            duration);
    }

    private void AddCasterAreaBuff(MAEntity caster, BuffCallback module, float duration)
    {
        if (caster == null || caster.BuffComp == null)
            throw new InvalidOperationException($"Active area skill requires caster BuffComp. skillId={skillId}");

        string buffId = $"skill_active_area_{skillId}";
        caster.BuffComp.RemoveBuff(buffId);
        caster.BuffComp.AddBuff(BuffData.Create(buffId, duration, false, 1, new List<BuffCallback> { module }), caster);
    }
}
