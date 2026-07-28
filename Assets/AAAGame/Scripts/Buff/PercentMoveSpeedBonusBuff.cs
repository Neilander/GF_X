using UnityEngine;

/// <summary>
/// 百分比移动速度 Buff：作用于 CreatureMainProperty.Speed 的 Mul-Buff 乘区。
/// 传入 percent 0.25 表示 +25%。多个来源并列累加到 Mul-Buff（百分比加法栈）。
/// </summary>
public sealed class PercentMoveSpeedBonusBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_Percent;
    private IPropertyModifier m_Modifier;

    public PercentMoveSpeedBonusBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var creature = hostEntity;
        var pm = creature?.CreatureProperties;
        if (pm == null || m_Percent == Fix64.Zero) return;

        Fix64 before = pm.GetProperty(CreatureMainProperty.Speed);
        m_Modifier = PropertyDirectAdditiveModifier.Create(m_Percent);
        pm.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, true);
        Fix64 after = pm.GetProperty(CreatureMainProperty.Speed);
        Debug.Log($"[PercentMoveSpeedBonusBuff] host={creature.CharacterKey} Speed: {(float)before} -> {(float)after} (percent={(float)m_Percent * 100f}%)");
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var pm = hostEntity?.CreatureProperties;
        if (pm == null || m_Modifier == null) return;

        pm.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, false);
        m_Modifier = null;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Percent.RawValue);
        hasher.Add(m_Modifier != null);
    }
}

public sealed class RampedPercentMoveSpeedBonusBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_TargetPercent;
    private readonly Fix64 m_RampDuration;
    private readonly bool m_StartFull;
    private Fix64 m_CurrentPercent;
    private Fix64 m_Elapsed;
    private IPropertyModifier m_Modifier;
    private ValueProperty m_MulBuffProperty;

    public RampedPercentMoveSpeedBonusBuff(Fix64 targetPercent, float rampDuration, bool startFull = false)
    {
        m_TargetPercent = targetPercent;
        m_RampDuration = (Fix64)rampDuration;
        m_StartFull = startFull;
    }

    public override void OnAdd()
    {
        base.OnAdd();

        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_TargetPercent == Fix64.Zero)
            return;

        m_CurrentPercent = m_StartFull ? m_TargetPercent : Fix64.Zero;
        m_Elapsed = m_StartFull ? m_RampDuration : Fix64.Zero;
        string propertyId = PropertyHelper.ModName(
            CreatureMainProperty.Speed.ToString(),
            nameof(NormalComputeTp.Mul),
            nameof(NormalBaseValueTp.Buff));
        m_MulBuffProperty = propertyManager.propertyManager.GetValueProperty(propertyId);
        m_Modifier = PropertyDirectAdditiveModifier.Create(() => m_CurrentPercent);
        propertyManager.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, true);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        base.OnUpdate(deltaTime);

        if (m_Modifier == null)
            return;

        m_Elapsed += Fix64.Max(Fix64.Zero, deltaTime);
        Fix64 t = m_RampDuration <= Fix64.Zero
            ? Fix64.One
            : Fix64.Clamp(m_Elapsed / m_RampDuration, Fix64.Zero, Fix64.One);
        SetCurrentPercent(m_TargetPercent * t);
    }

    public override void OnRemove()
    {
        base.OnRemove();

        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null || m_Modifier == null)
            return;

        propertyManager.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, false);
        m_Modifier = null;
        m_MulBuffProperty = null;
    }

    private void SetCurrentPercent(Fix64 value)
    {
        if (m_CurrentPercent == value)
            return;

        m_CurrentPercent = value;
        m_MulBuffProperty?.MakeDirty();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_TargetPercent.RawValue);
        hasher.Add(m_RampDuration.RawValue);
        hasher.Add(m_StartFull);
        hasher.Add(m_CurrentPercent.RawValue);
        hasher.Add(m_Elapsed.RawValue);
        hasher.Add(m_Modifier != null);
        hasher.Add(m_MulBuffProperty != null);
    }
}

public sealed class HeroOutOfCombatMoveSpeedBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 Delay = (Fix64)2;
    private static readonly Fix64 RampDuration = (Fix64)1;
    private static readonly Fix64 TargetPercent = Fix64.One;
    private bool m_InitialGrace = true;
    private Fix64 m_CurrentPercent;
    private IPropertyModifier m_Modifier;
    private ValueProperty m_MulBuffProperty;

    public override void OnAdd()
    {
        CreaturePropertyManager properties = hostEntity?.CreatureProperties
            ?? throw new System.InvalidOperationException("HeroOutOfCombatMoveSpeedBuff requires creature properties.");
        string propertyId = PropertyHelper.ModName(
            CreatureMainProperty.Speed.ToString(),
            nameof(NormalComputeTp.Mul),
            nameof(NormalBaseValueTp.Buff));
        m_MulBuffProperty = properties.propertyManager.GetValueProperty(propertyId);
        m_CurrentPercent = TargetPercent;
        m_Modifier = PropertyDirectAdditiveModifier.Create(() => m_CurrentPercent);
        properties.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, true);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (!hostEntity.Alive || !hostEntity.IsOutOfCombat)
        {
            m_InitialGrace = false;
            SetPercent(Fix64.Zero);
            return;
        }
        if (m_InitialGrace)
        {
            SetPercent(TargetPercent);
            return;
        }

        Fix64 elapsed = hostEntity.OutOfCombatElapsedLogicTime;
        Fix64 ramp = Fix64.Clamp((elapsed - Delay) / RampDuration, Fix64.Zero, Fix64.One);
        SetPercent(TargetPercent * ramp);
    }

    public override void OnRemove()
    {
        if (m_Modifier != null && hostEntity?.CreatureProperties != null)
        {
            hostEntity.CreatureProperties.ModifyMainPropertyMul(
                CreatureMainProperty.Speed,
                NormalBaseValueTp.Buff,
                m_Modifier,
                false);
        }
        m_Modifier = null;
        m_MulBuffProperty = null;
    }

    private void SetPercent(Fix64 value)
    {
        if (m_CurrentPercent == value)
            return;
        m_CurrentPercent = value;
        m_MulBuffProperty.MakeDirty();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_InitialGrace);
        hasher.Add(m_CurrentPercent.RawValue);
    }
}
