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
        Debug.Log($"[LifetimePercentBuff.OnAdd] 开始 host={hostEntity?.CharacterKey} id={hostEntity?.Id} percent={(float)m_Percent * 100f}%");

        if (hostEntity == null)
        {
            Debug.LogWarning($"[LifetimePercentBuff] hostEntity 为 null");
            return;
        }

        var rawComp = hostEntity.BuffComp;
        Debug.Log($"[LifetimePercentBuff] hostEntity.BuffComp 类型: {rawComp?.GetType().Name ?? "null"}");

        var comp = rawComp as CharacterBuffComp;
        if (comp == null)
        {
            Debug.LogWarning($"[LifetimePercentBuff] BuffComp 不是 CharacterBuffComp, 类型={rawComp?.GetType().FullName}");
            return;
        }

        var timedDeath = comp.GetTimedDeathBuff();
        if (timedDeath == null)
        {
            Debug.LogWarning($"[LifetimePercentBuff] 宿主 {hostEntity.CharacterKey} 没有 TimedDeathBuff（找不到 id='timed_death'），寿命变化无效");
            return;
        }

        Fix64 before = timedDeath.EffectiveDuration;
        Fix64 baseDur = timedDeath.BaseDuration;
        Debug.Log($"[LifetimePercentBuff] 调 ApplyPercent 前 Base={(float)baseDur} Effective={(float)before}");

        timedDeath.ApplyPercent(m_Percent);

        Fix64 after = timedDeath.EffectiveDuration;
        Debug.Log($"[LifetimePercentBuff] host={hostEntity.CharacterKey} Duration: {(float)before} -> {(float)after} (percent={(float)m_Percent * 100f}%)");
    }
}
