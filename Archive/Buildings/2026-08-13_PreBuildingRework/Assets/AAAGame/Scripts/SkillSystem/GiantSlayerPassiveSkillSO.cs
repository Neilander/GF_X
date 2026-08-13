using UnityEngine;

[CreateAssetMenu(fileName = "GiantSlayerPassiveSkillSO", menuName = "Skills/Passive/Giant Slayer")]
public sealed class GiantSlayerPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(IEntityContext owner)
    {
        ReplaceBuff(owner, new SkillCurrentHealthDamageBuff(GetValue(0)));
    }

    public override void Remove(IEntityContext owner)
    {
        RemoveBuff(owner);
    }
}
