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
        Debug.Log($"[LifetimeDeltaBuff.OnAdd] 开始 host={hostEntity?.CharacterKey} id={hostEntity?.Id} delta={(float)m_Seconds}s");

        if (hostEntity == null)
        {
            Debug.LogWarning($"[LifetimeDeltaBuff] hostEntity 为 null");
            return;
        }

        var rawComp = hostEntity.BuffComp;
        Debug.Log($"[LifetimeDeltaBuff] hostEntity.BuffComp 类型: {rawComp?.GetType().Name ?? "null"}");

        var comp = rawComp as CharacterBuffComp;
        if (comp == null)
        {
            Debug.LogWarning($"[LifetimeDeltaBuff] BuffComp 不是 CharacterBuffComp, 类型={rawComp?.GetType().FullName}");
            return;
        }

        var timedDeath = comp.GetTimedDeathBuff();
        if (timedDeath == null)
        {
            Debug.LogWarning($"[LifetimeDeltaBuff] 宿主 {hostEntity.CharacterKey} 没有 TimedDeathBuff（找不到 id='timed_death'），寿命变化无效");
            return;
        }

        Fix64 before = timedDeath.EffectiveDuration;
        Fix64 baseDur = timedDeath.BaseDuration;
        Debug.Log($"[LifetimeDeltaBuff] 调 ApplyAdditive 前 Base={(float)baseDur} Effective={(float)before}");

        timedDeath.ApplyAdditive(m_Seconds);

        Fix64 after = timedDeath.EffectiveDuration;
        Debug.Log($"[LifetimeDeltaBuff] host={hostEntity.CharacterKey} Duration: {(float)before} -> {(float)after} (delta={(float)m_Seconds}s)");
    }
}
