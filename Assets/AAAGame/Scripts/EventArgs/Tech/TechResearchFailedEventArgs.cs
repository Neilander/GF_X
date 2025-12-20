using GameFramework;
using GameFramework.Event;

/// <summary>
/// 科技研究失败事件
/// </summary>
public class TechResearchFailedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(TechResearchFailedEventArgs).GetHashCode();
    public override int Id => EventId;

    public string TechId { get; private set; }
    public TechResearchFailReason Reason { get; private set; }

    public static TechResearchFailedEventArgs Create(string techId, TechResearchFailReason reason)
    {
        var instance = ReferencePool.Acquire<TechResearchFailedEventArgs>();
        instance.TechId = techId;
        instance.Reason = reason;
        return instance;
    }

    public override void Clear()
    {
        TechId = null;
        Reason = TechResearchFailReason.None;
    }
}
