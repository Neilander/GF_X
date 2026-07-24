using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UrgentRequestActiveSkillSO", menuName = "Skills/Active/Urgent Request")]
public sealed class UrgentRequestActiveSkillSO : TargetPositionActiveSkillSO
{
    private const float SpawnMinDistance = 0.7f;

    protected override void ApplyAtPosition(IEntityContext caster, FixVector2 position, IReadOnlyList<ISelectable> selectedTargets)
    {
        if (caster == null)
            throw new InvalidOperationException($"UrgentRequest caster missing. skillId={skillId}");

        Fix64 countValue = GetValue(0);
        if (countValue <= Fix64.Zero)
            throw new InvalidOperationException($"UrgentRequest count invalid. skillId={skillId}, raw={countValue.RawValue}");

        long roundedCount = checked((countValue.RawValue + (1L << (Fix64.FRACTIONAL_PLACES - 1))) >> Fix64.FRACTIONAL_PLACES);
        int count = checked((int)roundedCount);
        if (count <= 0)
            throw new InvalidOperationException($"UrgentRequest count invalid. skillId={skillId}, count={count}");

        float radius = ClusterSpawnSystem.CalculateAutoSpawnRadius(count);
        List<FixVector2> spawnPositions = new List<FixVector2>(count);
        int agentTypeId = ClusterSpawnSystem.ResolveAgentTypeId(UnitType.Unit_Intern);
        if (!ClusterSpawnSystem.TryGetSpawnPositionsFixed(
                position,
                count,
                (Fix64)radius,
                (Fix64)SpawnMinDistance,
                spawnPositions,
                true,
                agentTypeId))
        {
            throw new InvalidOperationException($"UrgentRequest spawn failed. skillId={skillId}, count={count}, position={position}");
        }

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            SoldierFactory.ShowSoldierFixed(
                UnitType.Unit_Intern,
                spawnPositions[i],
                0.05f,
                caster.Side,
                BrainType.SoldierAI,
                unitLevel: 1);
        }
    }
}
