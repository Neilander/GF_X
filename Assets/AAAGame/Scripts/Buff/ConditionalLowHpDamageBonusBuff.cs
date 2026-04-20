using UnityEngine;

/// <summary>
/// 条件型伤害加成 Buff：对生命百分比低于阈值的目标造成额外伤害。
/// 由 DamageHelper 在出手伤害时通过 ModifyOutgoingDamage 钩子调用。
/// </summary>
public sealed class ConditionalLowHpDamageBonusBuff : BuffCallback
{
    private readonly Fix64 m_HpThresholdPercent;   // 例如 40 表示 40%
    private readonly Fix64 m_BonusAmount;           // 例如 2 表示 +2 点伤害

    public ConditionalLowHpDamageBonusBuff(Fix64 hpThresholdPercent, Fix64 bonusAmount)
    {
        m_HpThresholdPercent = hpThresholdPercent;
        m_BonusAmount = bonusAmount;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (!(target is GeneralCreature gc) || gc.CreaturePropertyManager == null)
            return baseDamage;

        Fix64 cur = gc.HealthValue;
        Fix64 max = gc.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        if (max <= Fix64.Zero) return baseDamage;

        Fix64 hpPercent = (cur / max) * (Fix64)100;
        if (hpPercent < m_HpThresholdPercent)
        {
            Debug.Log($"[ConditionalLowHpDamageBonus] target={gc.CharacterKey} HP%={(float)hpPercent:F1}% < {(float)m_HpThresholdPercent}%, damage {(float)baseDamage} -> {(float)(baseDamage + m_BonusAmount)}");
            return baseDamage + m_BonusAmount;
        }
        return baseDamage;
    }
}
