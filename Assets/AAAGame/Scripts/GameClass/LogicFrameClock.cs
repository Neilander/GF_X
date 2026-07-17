using System;

public sealed class LogicFrameClock
{
    public const int FrameRate = 30;
    public const double FrameDurationSeconds = 1d / FrameRate;

    private const double BoundaryEpsilon = 1e-10d;

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
        if (!m_IsStarted)
            throw new InvalidOperationException("LogicFrameClock.Advance failed: clock is not started.");
        if (tick == null)
            throw new ArgumentNullException(nameof(tick));
        if (double.IsNaN(realtime) || double.IsInfinity(realtime))
            throw new ArgumentOutOfRangeException(nameof(realtime), realtime, "Realtime must be finite.");
        if (double.IsNaN(timeScale) || double.IsInfinity(timeScale) || timeScale < 0d)
            throw new ArgumentOutOfRangeException(nameof(timeScale), timeScale, "Time scale must be finite and non-negative.");
        if (realtime < m_LastRealtime)
            throw new InvalidOperationException($"LogicFrameClock.Advance failed: realtime moved backwards. previous={m_LastRealtime:R}, current={realtime:R}.");

        double elapsed = realtime - m_LastRealtime;
        m_LastRealtime = realtime;
        m_Accumulator += elapsed * timeScale;

        int tickCount = 0;
        while (m_Accumulator + BoundaryEpsilon >= FrameDurationSeconds)
        {
            m_Accumulator -= FrameDurationSeconds;
            if (m_Accumulator < 0d && m_Accumulator > -BoundaryEpsilon)
                m_Accumulator = 0d;

            Frame++;
            tick(Frame);
            tickCount = checked(tickCount + 1);
        }

        return tickCount;
    }
}
