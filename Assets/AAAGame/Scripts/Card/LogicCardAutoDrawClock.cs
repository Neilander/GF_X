using System;

public interface ILogicCardRuntimeStateContributor
{
    void WriteDeterministicState(LogicStateHasher hasher);
}

public static class LogicCardRuntimeState
{
    private static ILogicCardRuntimeStateContributor s_Contributor;

    public static bool IsBound => s_Contributor != null;

    public static void Bind(ILogicCardRuntimeStateContributor contributor)
    {
        if (contributor == null)
            throw new ArgumentNullException(nameof(contributor));
        if (s_Contributor != null)
            throw new InvalidOperationException("LogicCardRuntimeState already has a bound contributor.");
        s_Contributor = contributor;
    }

    public static void Unbind(ILogicCardRuntimeStateContributor contributor)
    {
        if (contributor == null)
            throw new ArgumentNullException(nameof(contributor));
        if (!ReferenceEquals(s_Contributor, contributor))
            throw new InvalidOperationException("LogicCardRuntimeState contributor mismatch during unbind.");
        s_Contributor = null;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(s_Contributor != null);
        s_Contributor?.WriteDeterministicState(hasher);
    }
}

public sealed class LogicCardAutoDrawClock
{
    private static readonly Fix64 DefaultInterval = (Fix64)0.15f;

    public LogicCardAutoDrawClock()
        : this(DefaultInterval)
    {
    }

    public LogicCardAutoDrawClock(Fix64 interval)
    {
        if (interval <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        long ticks = (long)Fix64.Ceiling(interval / LogicFrameRuntime.FixedDeltaTime);
        if (ticks <= 0)
            throw new InvalidOperationException("Card auto-draw interval did not resolve to a positive tick count.");
        IntervalTicks = (ulong)ticks;
    }

    public ulong IntervalTicks { get; }
    public ulong NextEligibleFrame { get; private set; }

    public void Reset()
    {
        NextEligibleFrame = 0;
    }

    public bool IsDue(ulong frameId)
    {
        if (frameId == 0)
            throw new ArgumentOutOfRangeException(nameof(frameId));
        return NextEligibleFrame == 0 || frameId >= NextEligibleFrame;
    }

    public void RecordDraw(ulong frameId)
    {
        if (!IsDue(frameId))
        {
            throw new InvalidOperationException(
                $"Card auto-draw committed before its eligible frame. frame={frameId}, eligible={NextEligibleFrame}.");
        }

        NextEligibleFrame = checked(frameId + IntervalTicks);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(IntervalTicks);
        hasher.Add(NextEligibleFrame);
    }
}
