/// <summary>
/// 固定护甲/防御提升 Buff：作用于 CreatureMainProperty.Def。
/// 模板复刻自 FlatHealthBonusBuff。
/// </summary>
public sealed class FlatDefBonusBuff : BuffCallback
{
    private readonly Fix64 m_Bonus;
    private IPropertyModifier m_Modifier;

    public FlatDefBonusBuff(Fix64 bonus)
    {
        m_Bonus = bonus;
    }

    public override void OnAdd()
    {
        base.OnAdd();

        var creature = hostEntity;
        var propertyManager = creature?.CreatureProperties;
        if (propertyManager == null)
            throw new System.InvalidOperationException($"FlatDefBonusBuff requires a property manager. host={creature?.CharacterKey}");
        if (m_Bonus == Fix64.Zero)
            return;

        Fix64 before = propertyManager.GetProperty(CreatureMainProperty.Def);
        m_Modifier = PropertyDirectAdditiveModifier.Create(m_Bonus);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Def, m_Modifier, true);
        Fix64 after = propertyManager.GetProperty(CreatureMainProperty.Def);
        UnityEngine.Debug.Log($"[FlatDefBonusBuff] host={creature.CharacterKey} Def: {(float)before} -> {(float)after} (delta={(float)m_Bonus})");
    }

    public override void OnRemove()
    {
        base.OnRemove();
        if (m_Modifier == null)
            return;
        var propertyManager = hostEntity?.CreatureProperties
                              ?? throw new System.InvalidOperationException("FlatDefBonusBuff host property manager disappeared before removal.");
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Def, m_Modifier, false);
        m_Modifier = null;
    }
}
