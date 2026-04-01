using GameFramework;
using UnityEngine;

/// <summary>
/// Buff回调基类
/// </summary>
public abstract class BuffCallback : MonoBehaviour
{
    /// <summary>
    /// Buff数据
    /// </summary>
    protected BuffData buffData;
    
    /// <summary>
    /// 宿主实体
    /// </summary>
    protected MAEntity hostEntity;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public virtual void Initialize(BuffData data, MAEntity entity)
    {
        buffData = data;
        hostEntity = entity;
    }
    
    /// <summary>
    /// Buff添加时调用
    /// </summary>
    public virtual void OnAdd()
    {
        
    }
    
    /// <summary>
    /// Buff移除时调用
    /// </summary>
    public virtual void OnRemove()
    {
        
    }
    
    /// <summary>
    /// Buff叠加时调用
    /// </summary>
    public virtual void OnAddStack(int oldStack, int newStack)
    {
        
    }
    
    /// <summary>
    /// Buff每帧更新
    /// </summary>
    public virtual void OnUpdate(float deltaTime)
    {
        
    }
    
    /// <summary>
    /// Buff持续时间结束时调用
    /// </summary>
    public virtual void OnDurationEnd()
    {
        
    }
    
    /// <summary>
    /// 宿主死亡时调用
    /// </summary>
    public virtual void OnHostDead()
    {
        
    }
    
    /// <summary>
    /// 宿主击杀目标时调用
    /// </summary>
    public virtual void OnKill(MAEntity target)
    {
        
    }
    
    /// <summary>
    /// 清理资源
    /// </summary>
    public virtual void Clear()
    {
        buffData = null;
        hostEntity = null;
    }
}