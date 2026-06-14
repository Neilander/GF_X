using UnityEngine;

[CreateAssetMenu(fileName = "MoveSpeedPassiveSkillSO", menuName = "Skills/Passive/Move Speed")]
public sealed class MoveSpeedPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(MAEntity owner)
    {
        ReplaceBuff(owner, new SkillMoveSpeedPercentBuff(GetValue(0)));
    }

    public override void Remove(MAEntity owner)
    {
        RemoveBuff(owner);
    }
}
