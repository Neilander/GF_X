using UnityEngine;

[CreateAssetMenu(fileName = "DisarmPassiveSkillSO", menuName = "Skills/Passive/Disarm")]
public sealed class DisarmPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(MAEntity owner)
    {
        ReplaceBuff(owner, new SkillDisarmOnHitBuff(BuffId, GetValue(0), GetValue(1), GetDurationSeconds()));
    }

    public override void Remove(MAEntity owner)
    {
        RemoveBuff(owner);
    }
}
