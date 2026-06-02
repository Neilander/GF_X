using UnityEngine;

/// <summary>
/// 百分比移动速度 Buff：作用于 CreatureMainProperty.Speed 的 Mul-Buff 乘区。
/// 传入 percent 0.25 表示 +25%。多个来源并列累加到 Mul-Buff（百分比加法栈）。
/// </summary>
public sealed class PercentMoveSpeedBonusBuff : BuffCallback
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
        var creature = hostEntity as GeneralCreature;
        var pm = creature?.CreaturePropertyManager;
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
        var creature = hostEntity as GeneralCreature;
        var pm = creature?.CreaturePropertyManager;
        if (pm == null || m_Modifier == null) return;

        pm.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, false);
    }
}

public sealed class RampedPercentMoveSpeedBonusBuff : BuffCallback
{
    private readonly Fix64 m_TargetPercent;
    private readonly float m_RampDuration;
    private readonly bool m_StartFull;
    private Fix64 m_CurrentPercent;
    private float m_Elapsed;
    private IPropertyModifier m_Modifier;
    private ValueProperty m_MulBuffProperty;

    public RampedPercentMoveSpeedBonusBuff(Fix64 targetPercent, float rampDuration, bool startFull = false)
    {
        m_TargetPercent = targetPercent;
        m_RampDuration = rampDuration;
        m_StartFull = startFull;
    }

    public override void OnAdd()
    {
        base.OnAdd();

        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
        if (propertyManager == null || m_TargetPercent == Fix64.Zero)
            return;

        m_CurrentPercent = m_StartFull ? m_TargetPercent : Fix64.Zero;
        m_Elapsed = m_StartFull ? m_RampDuration : 0f;
        string propertyId = PropertyHelper.ModName(
            CreatureMainProperty.Speed.ToString(),
            nameof(NormalComputeTp.Mul),
            nameof(NormalBaseValueTp.Buff));
        m_MulBuffProperty = propertyManager.propertyManager.GetValueProperty(propertyId);
        m_Modifier = PropertyDirectAdditiveModifier.Create(() => m_CurrentPercent);
        propertyManager.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, true);
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);

        if (m_Modifier == null)
            return;

        m_Elapsed += Mathf.Max(0f, deltaTime);
        float t = m_RampDuration <= 0f ? 1f : Mathf.Clamp01(m_Elapsed / m_RampDuration);
        SetCurrentPercent(m_TargetPercent * (Fix64)t);
    }

    public override void OnRemove()
    {
        base.OnRemove();

        var creature = hostEntity as GeneralCreature;
        var propertyManager = creature?.CreaturePropertyManager;
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
}
