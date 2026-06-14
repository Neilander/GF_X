using UnityEngine;

[CreateAssetMenu(fileName = "GiantSlayerPassiveSkillSO", menuName = "Skills/Passive/Giant Slayer")]
public sealed class GiantSlayerPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(MAEntity owner)
    {
        ReplaceBuff(owner, new SkillCurrentHealthDamageBuff(GetValue(0)));
    }

    public override void Remove(MAEntity owner)
    {
        RemoveBuff(owner);
    }
}
