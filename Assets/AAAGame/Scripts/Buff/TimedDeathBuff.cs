using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 定时死亡 Buff。
/// 内部使用 Base + Additive + PercentSum 分层结构，顺序无关：
///   EffectiveDuration = (Base + Additive) * (1 + PercentSum)
/// 科技带来的寿命加减通过 ApplyAdditive / ApplyPercent 修改，自动同步到 BuffData.duration 和 remainingTime。
/// 到期后自动调用宿主死亡逻辑。
/// </summary>
public class TimedDeathBuff : BuffCallback
{
    private Fix64 m_BaseDuration;
    private Fix64 m_AdditiveDuration;
    private Fix64 m_PercentSum;

    /// <summary>(Base + Additive) * (1 + PercentSum)</summary>
    public Fix64 EffectiveDuration =>
        (m_BaseDuration + m_AdditiveDuration) * (Fix64.One + m_PercentSum);

    public Fix64 BaseDuration => m_BaseDuration;

    public override void OnAdd()
    {
        base.OnAdd();
        if (buffData != null)
        {
            m_BaseDuration = buffData.duration;
            Debug.Log($"[TimedDeathBuff.OnAdd] host={hostEntity?.CharacterKey} id={hostEntity?.LogicEntityId.Value} BaseDuration 锁定为 {(float)m_BaseDuration}s, remainingTime={buffData.remainingTime}s");
        }
        else
        {
            Debug.LogWarning("[TimedDeathBuff.OnAdd] buffData 为 null");
        }
    }

    /// <summary>固定秒数累加（正负均可）</summary>
    public void ApplyAdditive(Fix64 seconds)
    {
        ApplyDelta(() => m_AdditiveDuration += seconds);
    }

    /// <summary>百分比加法栈累加（1.0 = +100%）</summary>
    public void ApplyPercent(Fix64 percent)
    {
        ApplyDelta(() => m_PercentSum += percent);
    }

    private void ApplyDelta(System.Action modify)
    {
        if (buffData == null)
        {
            Debug.LogWarning($"[TimedDeathBuff.ApplyDelta] buffData 为 null，无法修改");
            return;
        }

        Fix64 oldFinal = EffectiveDuration;
        Fix64 oldDuration = buffData.duration;
        Fix64 oldRemaining = buffData.remainingTime;

        modify();

        Fix64 newFinal = EffectiveDuration;
        Fix64 delta = newFinal - oldFinal;

        buffData.duration = newFinal;
        buffData.remainingTime = Fix64.Max(Fix64.Zero, oldRemaining + delta);

        Debug.Log($"[TimedDeathBuff.ApplyDelta] Base={(float)m_BaseDuration} Additive={(float)m_AdditiveDuration} Pct={(float)m_PercentSum} " +
                  $"| EffectiveDuration {(float)oldFinal} -> {(float)newFinal} (delta={(float)delta}) " +
                  $"| buffData.duration {oldDuration} -> {buffData.duration} " +
                  $"| buffData.remainingTime {oldRemaining} -> {buffData.remainingTime}");
    }

    public override void OnDurationEnd()
    {
        base.OnDurationEnd();

        // 保存宿主引用，供本次到期结算完整使用。
        IEntityContext currentHost = hostEntity;

        if (currentHost != null && currentHost.Alive)
        {
            GF.Log($"TimedDeathBuff[宿主ID={currentHost.LogicEntityId.Value}]: 定时死亡Buff生效，单位即将死亡");

            if (currentHost is IBuildingLogicContext)
                throw new System.InvalidOperationException($"TimedDeathBuff.OnDurationEnd failed: host {currentHost.LogicEntityId.Value} is a building.");

            GF.Log($"TimedDeathBuff[宿主ID={currentHost.LogicEntityId.Value}]: 单位类型: {currentHost.CharacterKey}, 当前生命值: {(float)currentHost.HealthValue}");
            DamageHelper.DoDirectDamage(currentHost, currentHost.HealthValue, HealthModifyType.reduce);
            GF.Log($"TimedDeathBuff[宿主ID={currentHost.LogicEntityId.Value}]: 单位已死亡并提交销毁命令");
        }
    }

    public static BuffData CreateTimedDeath(float duration)
    {
        return BuffData.Create(
            id: "timed_death",
            duration: duration,
            isForever: false,
            maxStack: 1,
            modules: new List<BuffCallback> { new TimedDeathBuff() }
        );
    }
}
