using UnityEngine;

[CreateAssetMenu(fileName = "NeatAndTidyPassiveSkillSO", menuName = "Skills/Passive/Neat And Tidy")]
public sealed class NeatAndTidyPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(IEntityContext owner)
    {
        ReplaceBuff(owner, new SkillHigherHealthSplashBuff(GetAreaRangeValue(), GetValue(0)));
    }

    public override void Remove(IEntityContext owner)
    {
        RemoveBuff(owner);
    }
}
