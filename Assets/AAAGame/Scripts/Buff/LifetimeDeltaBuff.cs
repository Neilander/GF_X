using UnityEngine;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// 寿命加减 Buff（固定秒数）：挂上后立即把 delta 秒数累加到宿主的 TimedDeathBuff.Additive 上。
/// 顺序无关，总效果 = (Base + Σ Additive) × (1 + Σ PercentSum)。
/// 宿主没有 TimedDeath 时不生效（warning）。
/// </summary>
public sealed class LifetimeDeltaBuff : BuffCallback
{
    private readonly Fix64 m_Seconds;

    public LifetimeDeltaBuff(Fix64 seconds)
    {
        m_Seconds = seconds;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        var comp = hostEntity?.BuffComp as CharacterBuffComp;
        var timedDeath = comp?.GetTimedDeathBuff();
        if (timedDeath == null)
        {
            Debug.LogWarning($"[LifetimeDeltaBuff] 宿主 {hostEntity?.CharacterKey} 没有 TimedDeathBuff，寿命变化无效");
            return;
        }

        Fix64 before = timedDeath.EffectiveDuration;
        timedDeath.ApplyAdditive(m_Seconds);
        Fix64 after = timedDeath.EffectiveDuration;
        Debug.Log($"[LifetimeDeltaBuff] host={hostEntity?.CharacterKey} Duration: {(float)before} -> {(float)after} (delta={(float)m_Seconds}s)");
    }
}
