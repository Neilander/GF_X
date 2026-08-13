using UnityEngine;

[CreateAssetMenu(fileName = "CheerSquadPassiveSkillSO", menuName = "Skills/Passive/Cheer Squad")]
public sealed class CheerSquadPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(IEntityContext owner)
    {
        ReplaceBuff(owner, new SkillCheerSquadBuff((int)GetValue(0), GetValue(1), GetValue(2)));
    }

    public override void Remove(IEntityContext owner)
    {
        RemoveBuff(owner);
    }
}
