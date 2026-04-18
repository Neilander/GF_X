using GameFramework.Event;
using GameFramework;
/// <summary>
/// 关卡数据改变通知事件
/// </summary>
public class IngameValueChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(IngameValueChangedEventArgs).GetHashCode();
    public override int Id => EventId;
    public IngameValueType DataType { get; private set; }
    public int OldValue { get; private set; }
    public int Value { get; private set; }

    public static IngameValueChangedEventArgs Create(IngameValueType type, int oldV, int newV)
    {
        var instance = ReferencePool.Acquire<IngameValueChangedEventArgs>();
        instance.DataType = type;
        instance.OldValue = oldV;
        instance.Value = newV;
        return instance;
    }
    public override void Clear()
    {
        DataType = default;
        Value = 0;
        OldValue = 0;
    }
}
