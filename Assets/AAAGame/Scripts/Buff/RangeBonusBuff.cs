/// <summary>
/// 固定射程提升 Buff：作用于单位的 Weapon.Range（SimpleStat.Additive）。
/// 表里单位一般是"码"；WeaponData.Range 已经按同一标准存储。
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
        var weapon = hostEntity?.weaponComp?.Data;
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
        var weapon = hostEntity?.weaponComp?.Data;
        if (weapon == null || m_Bonus == Fix64.Zero)
            return;

        weapon.ApplyAdditive(WeaponStatId.Range, -m_Bonus);
    }
}
