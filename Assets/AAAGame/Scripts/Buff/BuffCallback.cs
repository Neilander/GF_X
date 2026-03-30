/// <summary>
/// Buff回调基类（纯C#类，不需要GameObject）
/// </summary>
public abstract class BuffCallback
{
    protected BuffData buffData;
    protected MAEntity hostEntity;

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
