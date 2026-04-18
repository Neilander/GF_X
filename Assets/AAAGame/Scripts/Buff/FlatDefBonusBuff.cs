/// <summary>
/// 固定护甲/防御提升 Buff：作用于 CreatureMainProperty.Def。
/// 模板复刻自 FlatHealthBonusBuff。
/// </summary>
public sealed class FlatDefBonusBuff : BuffCallback
{
    private readonly Fix64 m_Bonus;

    public FlatDefBonusBuff(Fix64 bonus)
    {
        m_Bonus = bonus;
    }

    public override void OnAdd()
    {
        base.OnAdd();

        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_Bonus == Fix64.Zero)
            return;

        Fix64 before = propertyManager.GetProperty(CreatureMainProperty.Def);
        propertyManager.ModifyMainPropertyValueBuff(
            CreatureMainProperty.Def,
            PropertyDirectAdditiveModifier.Create(m_Bonus),
            true);
        Fix64 after = propertyManager.GetProperty(CreatureMainProperty.Def);
        UnityEngine.Debug.Log($"[FlatDefBonusBuff] host={creature.CharacterKey} Def: {(float)before} -> {(float)after} (delta={(float)m_Bonus})");
    }
}
