using GameFramework;
using GameFramework.Event;

public sealed class CloseSideTipEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(CloseSideTipEventArgs).GetHashCode();
    public override int Id => EventId;

    public string TipId { get; private set; }

    public static CloseSideTipEventArgs Create(string tipId)
    {
        CloseSideTipEventArgs e = ReferencePool.Acquire<CloseSideTipEventArgs>();
        e.TipId = tipId;
        return e;
    }

    public override void Clear()
    {
        TipId = null;
    }
}
