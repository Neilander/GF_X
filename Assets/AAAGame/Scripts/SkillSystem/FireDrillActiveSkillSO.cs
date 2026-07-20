using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FireDrillActiveSkillSO", menuName = "Skills/Active/Fire Drill")]
public sealed class FireDrillActiveSkillSO : TargetPositionActiveSkillSO
{
    private const float RefreshDurationPaddingSeconds = 0.2f;

    protected override void ApplyAtPosition(IEntityContext caster, Vector3 position, IReadOnlyList<ISelectable> selectedTargets)
    {
        float radius = GetAreaRangeWorld();
        Fix64 missChancePercent = GetValue(0);
        float duration = GetDurationSeconds();
        if (radius <= 0f || duration <= 0f)
            throw new InvalidOperationException($"FireDrill values invalid. skillId={skillId}");

        AddCasterAreaBuff(
            caster,
            new EnemyBlindAreaBuff(position, radius, missChancePercent, duration + RefreshDurationPaddingSeconds, skillId),
            duration);
    }

    private void AddCasterAreaBuff(IEntityContext caster, BuffCallback module, float duration)
    {
        if (caster == null || caster.BuffComp == null)
            throw new InvalidOperationException($"Active area skill requires caster BuffComp. skillId={skillId}");

        string buffId = $"skill_active_area_{skillId}";
        caster.BuffComp.RemoveBuff(buffId);
        caster.BuffComp.AddBuff(BuffData.Create(buffId, duration, false, 1, new List<BuffCallback> { module }), caster);
    }
}
