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
            m_BaseDuration = (Fix64)buffData.duration;
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
        if (buffData == null) return;
        Fix64 oldFinal = EffectiveDuration;
        modify();
        Fix64 newFinal = EffectiveDuration;
        Fix64 delta = newFinal - oldFinal;

        buffData.duration = (float)newFinal;
        buffData.remainingTime = Mathf.Max(0f, buffData.remainingTime + (float)delta);
    }

    public override void OnDurationEnd()
    {
        base.OnDurationEnd();

        // 保存宿主引用，防止在调用OnDead()时被清空
        MAEntity currentHost = hostEntity;

        if (currentHost != null && currentHost.Alive)
        {
            GF.Log($"TimedDeathBuff[宿主ID={currentHost.Id}]: 定时死亡Buff生效，单位即将死亡");

            Entity entity = GF.Entity.GetEntity(currentHost.Id);
            if (entity != null && entity.gameObject != null)
            {
                SoldierEntity soldier = entity.gameObject.GetComponent<SoldierEntity>();
                if (soldier != null)
                {
                    GF.Log($"TimedDeathBuff[宿主ID={currentHost.Id}]: 单位类型: {soldier.CharacterKey}, 当前生命值: {(float)soldier.HealthValue}");

                    soldier.TakeDamage(soldier.HealthValue, HealthModifyType.reduce);
                    soldier.OnDead();

                    float maxHealth = (float)soldier.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                    GF.Event.Fire(soldier, CreatureHealthChangedEventArgs.Create(currentHost.Id, 0f, maxHealth, -maxHealth));

                    GF.Log($"TimedDeathBuff[宿主ID={currentHost.Id}]: 单位已死亡并隐藏");
                }
            }

            Entity targetEntity = GF.Entity.GetEntity(currentHost.Id);
            if (targetEntity != null)
            {
                GF.Entity.HideEntity(targetEntity.Id);
            }
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
