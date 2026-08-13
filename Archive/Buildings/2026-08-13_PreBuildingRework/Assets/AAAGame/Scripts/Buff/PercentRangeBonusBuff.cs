using UnityEngine;

/// <summary>
/// 百分比射程 Buff：作用于 Weapon.Range 的 PercentSum 字段（百分比加法栈）。
/// 传入 percent 0.15 表示 +15%。多个来源累加到 PercentSum，两个 +15% → final ×1.3。
/// </summary>
public sealed class PercentRangeBonusBuff : BuffCallback
{
    private readonly Fix64 m_Percent;

    public PercentRangeBonusBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_Percent == Fix64.Zero) return;

        Fix64 before = weapon.Range;
        weapon.ApplyPercentAdd(WeaponStatId.Range, m_Percent);
        Fix64 after = weapon.Range;
        Debug.Log($"[PercentRangeBonusBuff] host={hostEntity?.CharacterKey} Range: {(float)before} -> {(float)after} (percent={(float)m_Percent * 100f}%)");
    }

    public override void OnRemove()
    {
        base.OnRemove();
        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null || m_Percent == Fix64.Zero) return;

        weapon.ApplyPercentAdd(WeaponStatId.Range, -m_Percent);
    }
}
