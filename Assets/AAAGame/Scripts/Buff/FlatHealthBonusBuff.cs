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

        var creature = hostEntity;
        var propertyManager = creature?.CreatureProperties;
        if (propertyManager == null || m_BonusHealth <= Fix64.Zero)
            return;

        Fix64 before = propertyManager.GetProperty(CreatureMainProperty.Health);
        propertyManager.ModifyMainPropertyValueBuff(
            CreatureMainProperty.Health,
            PropertyDirectAdditiveModifier.Create(m_BonusHealth),
            true);
        Fix64 after = propertyManager.GetProperty(CreatureMainProperty.Health);
        Debug.Log($"[FlatHealthBonusBuff] host={creature.CharacterKey} Health: {(float)before} -> {(float)after} (delta={(float)m_BonusHealth})");
    }
}
