using UnityEngine;

/// <summary>
/// 固定生命值提升 Buff。
/// 当前用于全局科技给新出生单位附加额外生命值。
/// </summary>
public sealed class FlatHealthBonusBuff : BuffCallback
{
    private readonly Fix64 m_BonusHealth;

    public FlatHealthBonusBuff(Fix64 bonusHealth)
    {
        m_BonusHealth = bonusHealth;
    }

    public override void OnAdd()
    {
        base.OnAdd();

        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_BonusHealth <= Fix64.Zero)
            return;

        propertyManager.ModifyMainPropertyValueBuff(
            CreatureMainProperty.Health,
            PropertyDirectAdditiveModifier.Create(m_BonusHealth),
            true);
        
        /*
        propertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(m_BonusHealth),
            true);*/
        
        Debug.Log("[生命提升]"+m_BonusHealth+" z这么多 "+propertyManager.GetProperty(CreatureMainProperty.Health)+ propertyManager.GetProperty(CreatureCurrentProperty.HealthCurrent));
    }
}
