using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UrgentRequestActiveSkillSO", menuName = "Skills/Active/Urgent Request")]
public sealed class UrgentRequestActiveSkillSO : TargetPositionActiveSkillSO
{
    private static readonly Fix64 SpawnMinDistance = Fix64.FromRaw(2868);

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

        Fix64 attackBonus = GetValue(1);
        Fix64 healthBonus = GetValue(2);

        Fix64 radius = ClusterSpawnSystem.CalculateAutoSpawnRadiusFixed(count);
        List<FixVector2> spawnPositions = new List<FixVector2>(count);
        int agentTypeId = ClusterSpawnSystem.ResolveAgentTypeId(UnitType.Unit_Intern);
        if (!ClusterSpawnSystem.TryGetSpawnPositionsFixed(
                position,
                count,
                radius,
                SpawnMinDistance,
                spawnPositions,
                true,
                agentTypeId))
        {
            throw new InvalidOperationException($"UrgentRequest spawn failed. skillId={skillId}, count={count}, position={position}");
        }

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            SoldierFactory.ShowCurrentBattleTroopFixed(
                UnitType.Unit_Intern,
                spawnPositions[i],
                0.05f,
                caster.Side,
                BrainType.SoldierAI,
                configureParams: entityParams =>
                {
                    entityParams.StartBuffs ??= new List<BuffData>();
                    entityParams.StartBuffs.Add(BuffData.Create(
                        UnitSupplyExemptionBuff.BuffId,
                        Fix64.Zero,
                        true,
                        1,
                        new List<BuffCallback>
                        {
                            new UnitSupplyExemptionBuff(),
                            new FlatAttackBonusBuff(attackBonus),
                            new FlatHealthBonusBuff(healthBonus),
                        }));
                },
                unitLevel: 1);
        }
    }
}
