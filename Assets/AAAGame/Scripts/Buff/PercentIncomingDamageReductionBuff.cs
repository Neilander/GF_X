using System;
using AAAGame.Scripts.BuffSystem;

public sealed class PercentIncomingDamageReductionBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_Percent;

    public PercentIncomingDamageReductionBuff(Fix64 percent)
    {
        if (percent < Fix64.Zero || percent > (Fix64)100)
            throw new ArgumentOutOfRangeException(nameof(percent));
        m_Percent = percent;
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (modType != HealthModifyType.reduce || baseDamage <= Fix64.Zero || m_Percent == Fix64.Zero)
            return baseDamage;
        return baseDamage * (Fix64.One - m_Percent / (Fix64)100);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(m_Percent.RawValue);
    }
}
