/// <summary>
/// 固定射程提升 Buff：作用于单位的 Weapon.Range（SimpleStat.Additive）。
/// 表里单位为地块格；WeaponData.Range 直接存储同量纲值。
/// </summary>
public sealed class RangeBonusBuff : BuffCallback
{
    private readonly Fix64 m_Bonus;

    public RangeBonusBuff(Fix64 bonus)
    {
        m_Bonus = bonus;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_Bonus == Fix64.Zero)
            return;

        Fix64 before = weapon.Range;
        weapon.ApplyAdditive(WeaponStatId.Range, m_Bonus);
        Fix64 after = weapon.Range;
        UnityEngine.Debug.Log($"[RangeBonusBuff] host={hostEntity?.CharacterKey} Range: {(float)before} -> {(float)after} (delta={(float)m_Bonus})");
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_Bonus == Fix64.Zero)
            return;

        weapon.ApplyAdditive(WeaponStatId.Range, -m_Bonus);
    }
}
