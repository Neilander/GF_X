/// <summary>
/// 固定移速覆盖 Buff：将 Speed 白值层直接覆盖为指定值。
/// </summary>
public sealed class FixedMoveSpeedOverrideBuff : BuffCallback
{
    private readonly Fix64 m_TargetSpeed;
    private IPropertyModifier m_Modifier;

    public FixedMoveSpeedOverrideBuff(Fix64 targetSpeed)
    {
        m_TargetSpeed = targetSpeed;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_TargetSpeed <= Fix64.Zero)
            return;

        m_Modifier = PropertyOverrideModifier.Create(m_TargetSpeed);
        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_Modifier, true);
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_Modifier == null)
            return;

        propertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Speed, m_Modifier, false);
    }
}
