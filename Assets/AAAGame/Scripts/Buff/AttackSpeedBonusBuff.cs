/// <summary>
/// 攻击速度百分比提升 Buff（作用于 Weapon.Interval 的 Multiplier）。
/// 传入 percent 为百分比整数，如 80 表示 +80% 攻速。
/// 换算：新 Interval = 原 Interval * (1 / (1 + percent/100))
/// ——攻速提升 80% 等价于 Interval 缩短为原来的 1/1.8 ≈ 55.6%。
/// </summary>
public sealed class AttackSpeedBonusBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_Percent;
    private Fix64 m_AppliedFactor;
    private bool m_Applied;

    public AttackSpeedBonusBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_Percent == Fix64.Zero)
            return;

        Fix64 hundred = (Fix64)100;
        Fix64 denom = Fix64.One + (m_Percent / hundred);
        if (denom == Fix64.Zero)
            return;

        m_AppliedFactor = Fix64.One / denom;
        weapon.ApplyMultiplier(WeaponStatId.Interval, m_AppliedFactor);
        m_Applied = true;
    }

    public override void OnRemove()
    {
        base.OnRemove();
        if (!m_Applied)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_AppliedFactor == Fix64.Zero)
            return;

        weapon.ApplyMultiplier(WeaponStatId.Interval, Fix64.One / m_AppliedFactor);
        m_Applied = false;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Percent.RawValue);
        hasher.Add(m_AppliedFactor.RawValue);
        hasher.Add(m_Applied);
    }
}
