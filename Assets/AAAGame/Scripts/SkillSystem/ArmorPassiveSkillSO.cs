using UnityEngine;

[CreateAssetMenu(fileName = "ArmorPassiveSkillSO", menuName = "Skills/Passive/Armor")]
public sealed class ArmorPassiveSkillSO : PassiveSkillSO
{
    public override void Apply(IEntityContext owner)
    {
        ReplaceBuff(owner, new FlatDefBonusBuff(GetValue(0)));
    }

    public override void Remove(IEntityContext owner)
    {
        RemoveBuff(owner);
    }
}
