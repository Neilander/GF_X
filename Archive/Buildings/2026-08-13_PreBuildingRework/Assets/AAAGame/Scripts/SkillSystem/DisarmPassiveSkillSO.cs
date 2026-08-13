using UnityEngine;

[CreateAssetMenu(fileName = "DisarmPassiveSkillSO", menuName = "Skills/Passive/Disarm")]
public sealed class DisarmPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(IEntityContext owner)
    {
        ReplaceBuff(owner, new SkillDisarmOnHitBuff(BuffId, GetValue(0), GetValue(1), GetDurationLogicTime()));
    }

    public override void Remove(IEntityContext owner)
    {
        RemoveBuff(owner);
    }
}
