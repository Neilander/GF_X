using GameFramework;

/// <summary>
/// Buff回调基类（纯C#类，不需要GameObject）
/// </summary>
public abstract class BuffCallback
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

    public virtual void OnAdd() { }
    public virtual void OnRemove() { }
    public virtual void OnAddStack(int oldStack, int newStack) { }
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnDurationEnd() { }
    public virtual void OnHostDead() { }
    public virtual void OnKill(MAEntity target) { }

    public virtual void Clear()
    {
        buffData = null;
        hostEntity = null;
    }
}