using UnityEngine;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// 寿命百分比加减 Buff：挂上后把百分比累加到宿主的 TimedDeathBuff.PercentSum 上。
/// 表里 "+100%" 应该传 1.0；"+50%" 传 0.5。
/// 顺序无关，最终 EffectiveDuration = (Base + Additive) × (1 + PercentSum)。
/// </summary>
public sealed class LifetimePercentBuff : BuffCallback
{
    private readonly Fix64 m_Percent;

    public LifetimePercentBuff(Fix64 percent)
    {
        m_Percent = percent;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var comp = hostEntity?.BuffComp as CharacterBuffComp;
        var timedDeath = comp?.GetTimedDeathBuff();
        if (timedDeath == null)
        {
            Debug.LogWarning($"[LifetimePercentBuff] 宿主 {hostEntity?.CharacterKey} 没有 TimedDeathBuff，寿命变化无效");
            return;
        }

        Fix64 before = timedDeath.EffectiveDuration;
        timedDeath.ApplyPercent(m_Percent);
        Fix64 after = timedDeath.EffectiveDuration;
        Debug.Log($"[LifetimePercentBuff] host={hostEntity?.CharacterKey} Duration: {(float)before} -> {(float)after} (percent={(float)m_Percent * 100f}%)");
    }
}
