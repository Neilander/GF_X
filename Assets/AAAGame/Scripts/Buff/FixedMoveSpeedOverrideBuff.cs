/// <summary>
/// 固定移速覆盖 Buff：将 Speed 白值层直接覆盖为指定值。
/// </summary>
public sealed class FixedMoveSpeedOverrideBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_TargetSpeed;
    private IPropertyModifier m_Modifier;

    public FixedMoveSpeedOverrideBuff(Fix64 targetSpeed)
    {
        if (targetSpeed <= Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(targetSpeed), targetSpeed.RawValue, "Target speed must be positive.");

        m_TargetSpeed = targetSpeed;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null)
            throw new System.InvalidOperationException("FixedMoveSpeedOverrideBuff.OnAdd failed: host property manager is missing.");
        if (m_Modifier != null)
            throw new System.InvalidOperationException("FixedMoveSpeedOverrideBuff.OnAdd failed: modifier is already applied.");

        m_Modifier = PropertyOverrideModifier.Create(m_TargetSpeed);
        propertyManager.UnsafeModifyAnyProperty(CreatureMainProperty.Speed.ToString(), m_Modifier, true);
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Modifier == null)
            throw new System.InvalidOperationException("FixedMoveSpeedOverrideBuff.OnRemove failed: host property manager or modifier is missing.");

        propertyManager.UnsafeModifyAnyProperty(CreatureMainProperty.Speed.ToString(), m_Modifier, false);
        m_Modifier = null;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_TargetSpeed.RawValue);
        hasher.Add(m_Modifier != null);
    }
}
