using UnityEngine;

/// <summary>
/// 百分比生命上限 Buff：作用于 CreatureMainProperty.Health 的 Mul-Buff 乘区。
/// 传入 percent 0.25 表示 +25%。
/// 多个来源并列累加到 Mul-Buff，两个 +25% → final ×1.5（百分比加法栈）。
/// </summary>
public sealed class PercentHealthBonusBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_Percent;
    private IPropertyModifier m_Modifier;

    public PercentHealthBonusBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var creature = hostEntity;
        var pm = creature?.CreatureProperties;
        if (pm == null || m_Percent == Fix64.Zero) return;

        Fix64 before = pm.GetProperty(CreatureMainProperty.Health);
        m_Modifier = PropertyDirectAdditiveModifier.Create(m_Percent);
        pm.ModifyMainPropertyMul(CreatureMainProperty.Health, NormalBaseValueTp.Buff, m_Modifier, true);
        Fix64 after = pm.GetProperty(CreatureMainProperty.Health);
        Debug.Log($"[PercentHealthBonusBuff] host={creature.CharacterKey} Health: {(float)before} -> {(float)after} (percent={(float)m_Percent * 100f}%)");
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var pm = hostEntity?.CreatureProperties;
        if (pm == null || m_Modifier == null) return;

        pm.ModifyMainPropertyMul(CreatureMainProperty.Health, NormalBaseValueTp.Buff, m_Modifier, false);
        m_Modifier = null;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Percent.RawValue);
        hasher.Add(m_Modifier != null);
    }
}
