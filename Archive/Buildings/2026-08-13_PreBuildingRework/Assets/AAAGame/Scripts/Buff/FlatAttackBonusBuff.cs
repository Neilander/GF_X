/// <summary>
/// 固定攻击力提升 Buff：作用于单位的 Weapon.Atk（SimpleStat.Additive）。
/// </summary>
public sealed class FlatAttackBonusBuff : BuffCallback
{
    private readonly Fix64 m_Bonus;

    public FlatAttackBonusBuff(Fix64 bonus)
    {
        m_Bonus = bonus;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_Bonus == Fix64.Zero)
            return;

        Fix64 before = weapon.Atk;
        weapon.ApplyAdditive(WeaponStatId.Atk, m_Bonus);
        Fix64 after = weapon.Atk;
        UnityEngine.Debug.Log($"[FlatAttackBonusBuff] host={hostEntity?.CharacterKey} Atk: {(float)before} -> {(float)after} (delta={(float)m_Bonus})");
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_Bonus == Fix64.Zero)
            return;

        weapon.ApplyAdditive(WeaponStatId.Atk, -m_Bonus);
    }
}
