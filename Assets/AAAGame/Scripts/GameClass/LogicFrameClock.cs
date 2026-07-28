using System;

public sealed class LogicFrameClock
{
    public const int FrameRate = 30;
    public const double FrameDurationSeconds = 1d / FrameRate;

    // Repeated 1/30 additions accumulate a few nanoseconds over long sessions.
    // Treat values within 10 ns as the same real-time boundary so an exact Tick deadline
    // cannot slip to the next render pump after a long run.
    private const double BoundaryEpsilon = 1e-8d;

    private double m_LastRealtime;
    private double m_Accumulator;
    private bool m_IsStarted;

    public ulong Frame { get; private set; }
    public double AccumulatorSeconds => m_Accumulator;
    public double Interpolation => m_Accumulator / FrameDurationSeconds;

    public void Start(double realtime)
    {
        if (double.IsNaN(realtime) || double.IsInfinity(realtime))
            throw new ArgumentOutOfRangeException(nameof(realtime), realtime, "Realtime must be finite.");

        m_LastRealtime = realtime;
        m_Accumulator = 0d;
        Frame = 0;
        m_IsStarted = true;
    }

    public int Advance(double realtime, double timeScale, Action<ulong> tick)
    {
        if (tick == null)
            throw new ArgumentNullException(nameof(tick));

        return Advance(realtime, () => timeScale, (frame, _) => tick(frame));
    }

    public void RebaseRealtimePreservingAccumulator(double realtime)
    {
        if (!m_IsStarted)
            throw new InvalidOperationException("LogicFrameClock.RebaseRealtimePreservingAccumulator failed: clock is not started.");
        if (double.IsNaN(realtime) || double.IsInfinity(realtime))
            throw new ArgumentOutOfRangeException(nameof(realtime), realtime, "Realtime must be finite.");
        if (realtime < m_LastRealtime)
        {
            throw new InvalidOperationException(
                $"LogicFrameClock.RebaseRealtimePreservingAccumulator failed: realtime moved backwards. previous={m_LastRealtime:R}, current={realtime:R}.");
        }

        m_LastRealtime = realtime;
    }

    public int Advance(double realtime, Func<double> getTimeScale, Action<ulong, double> tick)
    {
        if (!m_IsStarted)
            throw new InvalidOperationException("LogicFrameClock.Advance failed: clock is not started.");
        if (getTimeScale == null)
            throw new ArgumentNullException(nameof(getTimeScale));
        if (tick == null)
            throw new ArgumentNullException(nameof(tick));
        if (double.IsNaN(realtime) || double.IsInfinity(realtime))
            throw new ArgumentOutOfRangeException(nameof(realtime), realtime, "Realtime must be finite.");
        if (realtime < m_LastRealtime)
            throw new InvalidOperationException($"LogicFrameClock.Advance failed: realtime moved backwards. previous={m_LastRealtime:R}, current={realtime:R}.");

        double realtimeCursor = m_LastRealtime;
        int tickCount = 0;
        while (realtimeCursor < realtime)
        {
            double timeScale = getTimeScale();
            ValidateTimeScale(timeScale);
            if (timeScale <= 0d)
            {
                realtimeCursor = realtime;
                break;
            }

            double scaledUntilTick = FrameDurationSeconds - m_Accumulator;
            if (scaledUntilTick < 0d && scaledUntilTick > -BoundaryEpsilon)
                scaledUntilTick = 0d;

            double realUntilTick = scaledUntilTick / timeScale;
            double realRemaining = realtime - realtimeCursor;
            if (realUntilTick > realRemaining + BoundaryEpsilon)
            {
                m_Accumulator += realRemaining * timeScale;
                realtimeCursor = realtime;
                break;
            }

            realtimeCursor += Math.Max(0d, realUntilTick);
            m_Accumulator = 0d;
            Frame++;
            tick(Frame, Math.Min(realtimeCursor, realtime));
            tickCount = checked(tickCount + 1);
        }

        if (m_Accumulator > 0d && m_Accumulator <= BoundaryEpsilon)
            m_Accumulator = 0d;

        m_LastRealtime = realtime;
        return tickCount;
    }

    private static void ValidateTimeScale(double timeScale)
    {
        if (double.IsNaN(timeScale) || double.IsInfinity(timeScale) || timeScale < 0d)
            throw new ArgumentOutOfRangeException(nameof(timeScale), timeScale, "Time scale must be finite and non-negative.");
    }
}
