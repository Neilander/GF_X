using GameFramework.Event;
using GameFramework;
/// <summary>
/// 玩家数据改变通知事件
/// </summary>
public class ItemAmountChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(ItemAmountChangedEventArgs).GetHashCode();
    public override int Id => EventId;
    public string ItemIdentifier { get; private set; }
    public int OldValue { get; private set; }
    public int Value { get; private set; }

    public static ItemAmountChangedEventArgs Create(string itemIdentifier, int oldV, int newV)
    {
        var instance = ReferencePool.Acquire<ItemAmountChangedEventArgs>();
        instance.ItemIdentifier = itemIdentifier;
        instance.OldValue = oldV;
        instance.Value = newV;
        return instance;
    }
    public override void Clear()
    {
        ItemIdentifier = null;
        Value = 0;
        OldValue = 0;
    }
}
