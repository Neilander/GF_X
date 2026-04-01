using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 定时死亡Buff
/// 到期后自动调用宿主死亡逻辑
/// </summary>
public class TimedDeathBuff : BuffCallback{
    /// <summary>
    /// Buff持续时间结束时调用
    /// </summary>
    public override void OnDurationEnd()
    {
        base.OnDurationEnd();
        
        // 保存宿主引用，防止在调用OnDead()时被清空
        MAEntity currentHost = hostEntity;
        
        // 检查宿主是否还存活
        if (currentHost != null && currentHost.Alive)
        {
            // 获取实体并调用死亡方法
            Entity entity = GF.Entity.GetEntity(currentHost.Id);
            if (entity != null && entity.gameObject != null)
            {
                SoldierEntity soldier = entity.gameObject.GetComponent<SoldierEntity>();
                if (soldier != null)
                {
                    // 通过TakeDamage触发死亡逻辑，这样Alive会被正确设置为false
                    soldier.TakeDamage(soldier.health, HealthModifyType.reduce);
                    
                    soldier.OnDead();
                    
                    // 触发血量变化事件，让血条知道单位已死亡
                    float maxHealth = (float)soldier.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                    GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(currentHost.Id, 0f, maxHealth, -maxHealth));
                }
            }
            
            // 直接隐藏实体
            Entity targetEntity = GF.Entity.GetEntity(currentHost.Id);
            if (targetEntity != null)
            {
                GF.Entity.HideEntity(targetEntity.Id);
            }
        }
    }
    
    /// <summary>
    /// 创建定时死亡Buff数据
    /// </summary>
    public static BuffData CreateTimedDeath(float duration)
    {
        List<BuffCallback> modules = new List<BuffCallback>();
        modules.Add(new GameObject("TimedDeathBuff").AddComponent<TimedDeathBuff>());
        
        return BuffData.Create(
            id: "timed_death",
            duration: duration,
            isForever: false,
            maxStack: 1,
            modules: modules
        );
    }
    
    /// <summary>
    /// 清理资源
    /// </summary>
    public override void Clear()
    {
        base.Clear();
    }
}