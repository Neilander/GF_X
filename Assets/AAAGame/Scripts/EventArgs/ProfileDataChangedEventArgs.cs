using GameFramework.Event;
using GameFramework;
/// <summary>
/// 生涯数据改变通知事件
/// </summary>
public class ProfileDataChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(ProfileDataChangedEventArgs).GetHashCode();
    public override int Id => EventId;
    public ProfileDataType DataType { get; private set; }
    public int OldValue { get; private set; }
    public int Value { get; private set; }

    public static ProfileDataChangedEventArgs Create(ProfileDataType type, int oldV, int newV)
    {
        var instance = ReferencePool.Acquire<ProfileDataChangedEventArgs>();
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
