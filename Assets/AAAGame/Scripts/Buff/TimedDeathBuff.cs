using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 定时死亡Buff
/// 到期后自动调用宿主死亡逻辑
/// </summary>
public class TimedDeathBuff : BuffCallback
{
    public override void OnDurationEnd()
    {
        base.OnDurationEnd();

        MAEntity currentHost = hostEntity;

        if (currentHost != null && currentHost.Alive)
        {
            Entity entity = GF.Entity.GetEntity(currentHost.Id);
            if (entity != null && entity.gameObject != null)
            {
                SoldierEntity soldier = entity.gameObject.GetComponent<SoldierEntity>();
                if (soldier != null)
                {
                    soldier.TakeDamage(soldier.health, HealthModifyType.reduce);
                    soldier.OnDead();

                    float maxHealth = (float)soldier.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                    GF.Event.Fire(soldier, CreatureHealthChangedEventArgs.Create(currentHost.Id, 0f, maxHealth, -maxHealth));
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
