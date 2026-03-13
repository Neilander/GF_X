using UnityEngine;

public interface ITargetingComp:ICapability
{
    void Init(MAEntity entity);
    void UpdateTargeting();
    
    // 供外部（如 AI Brain 或 UI）读取的当前目标
    CompCreature CurrentTarget { get; } 
    CompCreature FollowTarget { get; } // 新增：供友军读取的跟随目标

    // 索敌和遗忘半径，允许在运行时被改变（方便 Debugger 实时调整）
    float AggroRange { get; set; }
    float ForgetRange { get; set; }
    
    float FollowSearchRange { get; set; } // 新增：寻找玩家的视野半径
}