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
