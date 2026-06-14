using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UrgentRequestActiveSkillSO", menuName = "Skills/Active/Urgent Request")]
public sealed class UrgentRequestActiveSkillSO : TargetPositionActiveSkillSO
{
    private const float SpawnMinDistance = 0.7f;

    protected override void ApplyAtPosition(MAEntity caster, Vector3 position, IReadOnlyList<ISelectable> selectedTargets)
    {
        if (caster == null)
            throw new InvalidOperationException($"UrgentRequest caster missing. skillId={skillId}");

        int count = Mathf.RoundToInt((float)GetValue(0));
        if (count <= 0)
            throw new InvalidOperationException($"UrgentRequest count invalid. skillId={skillId}, count={count}");

        float radius = ClusterSpawnSystem.CalculateAutoSpawnRadius(count);
        List<Vector3> spawnPositions = new List<Vector3>(count);
        if (!ClusterSpawnSystem.TryGetPreviewSpawnPositions(position, count, radius, SpawnMinDistance, spawnPositions, true))
            throw new InvalidOperationException($"UrgentRequest spawn failed. skillId={skillId}, count={count}, position={position}");

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            Vector3 spawnPosition = spawnPositions[i] + Vector3.up * 0.05f;
            SoldierFactory.ShowSoldier(
                UnitType.Unit_Intern,
                spawnPosition,
                caster.Side,
                BrainType.SoldierAI,
                unitLevel: 1);
        }
    }
}
