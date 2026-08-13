using System;

public readonly struct DeterministicProgressSnapshot
{
    public DeterministicProgressSnapshot(long progressRaw)
    {
        if (progressRaw < 0)
            throw new ArgumentOutOfRangeException(nameof(progressRaw));

        ProgressRaw = progressRaw;
    }

    public long ProgressRaw { get; }
}

public sealed class DeterministicProgressAccumulator
{
    private Fix64 m_Progress;

    public Fix64 Progress => m_Progress;

    public bool AdvanceAndConsume(Fix64 increment)
    {
        return AdvanceAndConsume(increment, Fix64.One);
    }

    public bool AdvanceAndConsume(Fix64 increment, Fix64 threshold)
    {
        if (increment < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(increment), "Progress increment cannot be negative.");
        if (threshold <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Progress threshold must be positive.");

        m_Progress += increment;
        if (m_Progress < threshold)
            return false;

        m_Progress -= threshold;
        return true;
    }

    public DeterministicProgressSnapshot CaptureSnapshot()
    {
        return new DeterministicProgressSnapshot(m_Progress.RawValue);
    }

    public void RestoreSnapshot(DeterministicProgressSnapshot snapshot)
    {
        m_Progress = Fix64.FromRaw(snapshot.ProgressRaw);
    }

    public void Reset()
    {
        m_Progress = Fix64.Zero;
    }
}

public enum DeterministicRandomStreamId
{
    World = 1,
    Entity = 2,
    Combat = 3,
    Spawn = 4,
}

public readonly struct DeterministicRandomSnapshot
{
    public DeterministicRandomSnapshot(ulong state, ulong increment)
    {
        State = state;
        Increment = increment;
    }

    public ulong State { get; }
    public ulong Increment { get; }
}

public static class DeterministicRandomService
{
    public const bool GameplayRandomEnabled = false;

    public static uint NextUInt(DeterministicRandomStreamId streamId, string reason)
    {
        throw new InvalidOperationException(
            $"DeterministicRandomService is reserved but disabled. Use deterministic progress accumulation or a stable candidate sequence. stream={streamId}, reason={reason}.");
    }
}
