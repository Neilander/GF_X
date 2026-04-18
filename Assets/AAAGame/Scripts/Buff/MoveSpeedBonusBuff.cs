/// <summary>
/// 固定移动速度提升 Buff：作用于 CreatureMainProperty.Speed。
/// 表里的数值直接加到 Speed 上（内部会做距离单位缩放，+60 就是放大后的舒服数值）。
/// 模板复刻自 FlatHealthBonusBuff。
/// </summary>
public sealed class MoveSpeedBonusBuff : BuffCallback
{
    private readonly Fix64 m_Bonus;

    public MoveSpeedBonusBuff(Fix64 bonus)
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

        Fix64 before = propertyManager.GetProperty(CreatureMainProperty.Speed);
        propertyManager.ModifyMainPropertyValueBuff(
            CreatureMainProperty.Speed,
            PropertyDirectAdditiveModifier.Create(m_Bonus),
            true);
        Fix64 after = propertyManager.GetProperty(CreatureMainProperty.Speed);
        UnityEngine.Debug.Log($"[MoveSpeedBonusBuff] host={creature.CharacterKey} Speed: {(float)before} -> {(float)after} (delta={(float)m_Bonus})");
    }
}
