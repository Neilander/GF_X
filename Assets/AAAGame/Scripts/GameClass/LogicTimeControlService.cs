using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public enum TimeScaleCommandKind
{
    SetBasePlaybackScale = 0,
    SetBulletTimeScale = 1,
    RemoveBulletTimeScale = 2,
}

public readonly struct TimeScaleCommand
{
    public TimeScaleCommand(
        ulong effectiveFrame,
        ulong sequence,
        TimeScaleCommandKind kind,
        int sourceId,
        int scaleUnits)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        Kind = kind;
        SourceId = sourceId;
        ScaleUnits = scaleUnits;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public TimeScaleCommandKind Kind { get; }
    public int SourceId { get; }
    public int ScaleUnits { get; }
}

public enum PauseControlCommandKind
{
    Acquire = 0,
    Release = 1,
}

public readonly struct PauseControlCommand
{
    public PauseControlCommand(
        ulong effectiveFrame,
        ulong sequence,
        PauseControlCommandKind kind,
        int sourceId)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        Kind = kind;
        SourceId = sourceId;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public PauseControlCommandKind Kind { get; }
    public int SourceId { get; }
}

public readonly struct BulletTimeScaleSnapshot
{
    public BulletTimeScaleSnapshot(int sourceId, int scaleUnits)
    {
        SourceId = sourceId;
        ScaleUnits = scaleUnits;
    }

    public int SourceId { get; }
    public int ScaleUnits { get; }
}

public sealed class LogicTimeControlSnapshot
{
    internal LogicTimeControlSnapshot(
        ulong currentFrame,
        ulong lastAcceptedSequence,
        int revision,
        int basePlaybackScaleUnits,
        BulletTimeScaleSnapshot[] bulletTimeScales,
        int[] pauseSources,
        TimeScaleCommand[] pendingTimeScaleCommands)
    {
        CurrentFrame = currentFrame;
        LastAcceptedSequence = lastAcceptedSequence;
        Revision = revision;
        BasePlaybackScaleUnits = basePlaybackScaleUnits;
        BulletTimeScales = Array.AsReadOnly((BulletTimeScaleSnapshot[])bulletTimeScales.Clone());
        PauseSources = Array.AsReadOnly((int[])pauseSources.Clone());
        PendingTimeScaleCommands = Array.AsReadOnly((TimeScaleCommand[])pendingTimeScaleCommands.Clone());
    }

    public ulong CurrentFrame { get; }
    public ulong LastAcceptedSequence { get; }
    public int Revision { get; }
    public int BasePlaybackScaleUnits { get; }
    public ReadOnlyCollection<BulletTimeScaleSnapshot> BulletTimeScales { get; }
    public ReadOnlyCollection<int> PauseSources { get; }
    public ReadOnlyCollection<TimeScaleCommand> PendingTimeScaleCommands { get; }
}

public static class LogicTimeControlService
{
    public const int ScaleUnitsPerOne = 10000;
    public const int NormalScaleUnits = ScaleUnitsPerOne;
    public const int MinBulletTimeScaleUnits = 1;
    public const int MaxBasePlaybackScaleUnits = ScaleUnitsPerOne * 8;

    private static readonly SortedDictionary<int, int> s_BulletTimeScales =
        new SortedDictionary<int, int>();
    private static readonly SortedSet<int> s_PauseSources = new SortedSet<int>();
    private static readonly List<TimeScaleCommand> s_PendingTimeScaleCommands =
        new List<TimeScaleCommand>();

    private static int s_BasePlaybackScaleUnits = NormalScaleUnits;
    private static int s_BulletTimeScaleUnits = NormalScaleUnits;
    private static ulong s_LastAcceptedSequence;

    public static bool IsActive { get; private set; }
    public static bool IsPaused => s_PauseSources.Count > 0;
    public static int BasePlaybackScaleUnits => s_BasePlaybackScaleUnits;
    public static int BulletTimeScaleUnits => s_BulletTimeScaleUnits;
    public static ulong CurrentFrame { get; private set; }
    public static ulong LastAcceptedSequence => s_LastAcceptedSequence;
    public static int Revision { get; private set; }

    public static int EffectiveSimulationScaleUnits
    {
        get
        {
            if (IsPaused)
                return 0;

            long scaled = (long)s_BasePlaybackScaleUnits * s_BulletTimeScaleUnits;
            return checked((int)(scaled / ScaleUnitsPerOne));
        }
    }

    public static double SchedulerScale => EffectiveSimulationScaleUnits / (double)ScaleUnitsPerOne;
    public static float AnimationScale => EffectiveSimulationScaleUnits / (float)ScaleUnitsPerOne;

    public static event Action Changed;
    public static event Action<TimeScaleCommand> TimeScaleCommandAccepted;
    public static event Action<PauseControlCommand> PauseControlCommandApplied;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicTimeControlService.BeginTimeline failed: service is already active.");

        IsActive = true;
        ResetState(false);
        RaiseChanged();
    }

    public static void EndTimeline()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicTimeControlService.EndTimeline failed: service is not active.");

        IsActive = false;
        ResetState(false);
        RaiseChanged();
    }

    public static void ResetFrameTimelinePreservingPauses()
    {
        EnsureActive();
        ResetState(true);
        RaiseChanged();
    }

    public static void SetBasePlaybackScale(int scaleUnits)
    {
        ValidateBasePlaybackScale(scaleUnits);
        SubmitTimeScaleCommand(new TimeScaleCommand(
            NextEffectiveFrame,
            NextSequence,
            TimeScaleCommandKind.SetBasePlaybackScale,
            0,
            scaleUnits));
    }

    public static void SetBulletTimeScale(int sourceId, int scaleUnits)
    {
        ValidatePositiveSourceId(sourceId, "Bullet-time source id must be positive.");
        ValidateBulletTimeScale(scaleUnits);
        SubmitTimeScaleCommand(new TimeScaleCommand(
            NextEffectiveFrame,
            NextSequence,
            TimeScaleCommandKind.SetBulletTimeScale,
            sourceId,
            scaleUnits));
    }

    public static void RemoveBulletTimeScale(int sourceId)
    {
        ValidatePositiveSourceId(sourceId, "Bullet-time source id must be positive.");
        SubmitTimeScaleCommand(new TimeScaleCommand(
            NextEffectiveFrame,
            NextSequence,
            TimeScaleCommandKind.RemoveBulletTimeScale,
            sourceId,
            0));
    }

    public static void SubmitTimeScaleCommand(TimeScaleCommand command)
    {
        EnsureActive();
        ValidateTimeScaleCommand(command);
        if (command.EffectiveFrame < NextEffectiveFrame)
        {
            throw new InvalidOperationException(
                $"LogicTimeControlService.SubmitTimeScaleCommand failed: command targets a completed frame. current={CurrentFrame}, effective={command.EffectiveFrame}.");
        }

        AcceptSequence(command.Sequence);
        InsertTimeScaleCommand(command);
        TimeScaleCommandAccepted?.Invoke(command);
    }

    public static void AcquirePause(int sourceId)
    {
        ValidatePositiveSourceId(sourceId, "Pause source id must be positive.");
        ApplyPauseControlCommand(new PauseControlCommand(
            NextEffectiveFrame,
            NextSequence,
            PauseControlCommandKind.Acquire,
            sourceId));
    }

    public static void ReleasePause(int sourceId)
    {
        ValidatePositiveSourceId(sourceId, "Pause source id must be positive.");
        ApplyPauseControlCommand(new PauseControlCommand(
            NextEffectiveFrame,
            NextSequence,
            PauseControlCommandKind.Release,
            sourceId));
    }

    public static void ApplyPauseControlCommand(PauseControlCommand command)
    {
        EnsureActive();
        ValidatePauseControlCommand(command);
        if (command.EffectiveFrame != NextEffectiveFrame)
        {
            throw new InvalidOperationException(
                $"LogicTimeControlService.ApplyPauseControlCommand failed: pause control must target the next logic frame. expected={NextEffectiveFrame}, actual={command.EffectiveFrame}.");
        }

        switch (command.Kind)
        {
            case PauseControlCommandKind.Acquire:
                if (s_PauseSources.Contains(command.SourceId))
                {
                    throw new InvalidOperationException(
                        $"LogicTimeControlService.ApplyPauseControlCommand failed: source already owns a pause token. sourceId={command.SourceId}.");
                }
                break;
            case PauseControlCommandKind.Release:
                if (!s_PauseSources.Contains(command.SourceId))
                {
                    throw new InvalidOperationException(
                        $"LogicTimeControlService.ApplyPauseControlCommand failed: source does not own a pause token. sourceId={command.SourceId}.");
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"LogicTimeControlService.ApplyPauseControlCommand failed: unsupported kind {command.Kind}.");
        }

        AcceptSequence(command.Sequence);
        if (command.Kind == PauseControlCommandKind.Acquire)
            s_PauseSources.Add(command.SourceId);
        else
            s_PauseSources.Remove(command.SourceId);

        MarkChanged();
        PauseControlCommandApplied?.Invoke(command);
    }

    public static void PrepareFrame(ulong frameId)
    {
        EnsureActive();
        if (frameId != NextEffectiveFrame)
        {
            throw new InvalidOperationException(
                $"LogicTimeControlService.PrepareFrame failed: non-contiguous frame. expected={NextEffectiveFrame}, actual={frameId}.");
        }

        while (s_PendingTimeScaleCommands.Count > 0)
        {
            TimeScaleCommand command = s_PendingTimeScaleCommands[0];
            if (command.EffectiveFrame < frameId)
            {
                throw new InvalidOperationException(
                    $"LogicTimeControlService.PrepareFrame failed: pending command missed its frame. effective={command.EffectiveFrame}, frame={frameId}, sequence={command.Sequence}.");
            }
            if (command.EffectiveFrame > frameId)
                break;

            ApplyTimeScaleCommand(command);
            s_PendingTimeScaleCommands.RemoveAt(0);
        }
    }

    public static void BeginFrame(ulong frameId)
    {
        PrepareFrame(frameId);
        if (IsPaused)
        {
            throw new InvalidOperationException(
                $"LogicTimeControlService.BeginFrame failed: frame cannot begin while paused. frame={frameId}.");
        }

        CurrentFrame = frameId;
    }

    public static bool HasPause(int sourceId)
    {
        return s_PauseSources.Contains(sourceId);
    }

    public static LogicTimeControlSnapshot CaptureSnapshot()
    {
        EnsureActive();

        var bulletScales = new BulletTimeScaleSnapshot[s_BulletTimeScales.Count];
        int bulletIndex = 0;
        foreach (KeyValuePair<int, int> pair in s_BulletTimeScales)
            bulletScales[bulletIndex++] = new BulletTimeScaleSnapshot(pair.Key, pair.Value);

        var pauseSources = new int[s_PauseSources.Count];
        s_PauseSources.CopyTo(pauseSources);

        return new LogicTimeControlSnapshot(
            CurrentFrame,
            s_LastAcceptedSequence,
            Revision,
            s_BasePlaybackScaleUnits,
            bulletScales,
            pauseSources,
            s_PendingTimeScaleCommands.ToArray());
    }

    public static void RestoreSnapshot(LogicTimeControlSnapshot snapshot)
    {
        EnsureActive();
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        ValidateBasePlaybackScale(snapshot.BasePlaybackScaleUnits);
        ValidateSnapshotCollections(snapshot);

        s_BulletTimeScales.Clear();
        for (int i = 0; i < snapshot.BulletTimeScales.Count; i++)
        {
            BulletTimeScaleSnapshot entry = snapshot.BulletTimeScales[i];
            s_BulletTimeScales.Add(entry.SourceId, entry.ScaleUnits);
        }

        s_PauseSources.Clear();
        for (int i = 0; i < snapshot.PauseSources.Count; i++)
            s_PauseSources.Add(snapshot.PauseSources[i]);

        s_PendingTimeScaleCommands.Clear();
        for (int i = 0; i < snapshot.PendingTimeScaleCommands.Count; i++)
            s_PendingTimeScaleCommands.Add(snapshot.PendingTimeScaleCommands[i]);

        CurrentFrame = snapshot.CurrentFrame;
        s_LastAcceptedSequence = snapshot.LastAcceptedSequence;
        Revision = snapshot.Revision;
        s_BasePlaybackScaleUnits = snapshot.BasePlaybackScaleUnits;
        RecalculateBulletTimeScale();
        RaiseChanged();
    }

    private static ulong NextEffectiveFrame => checked(CurrentFrame + 1);
    private static ulong NextSequence => checked(s_LastAcceptedSequence + 1);

    private static void ResetState(bool preservePauses)
    {
        s_BasePlaybackScaleUnits = NormalScaleUnits;
        s_BulletTimeScaleUnits = NormalScaleUnits;
        s_BulletTimeScales.Clear();
        s_PendingTimeScaleCommands.Clear();
        if (!preservePauses)
            s_PauseSources.Clear();
        CurrentFrame = 0;
        s_LastAcceptedSequence = 0;
        Revision = 0;
    }

    private static void InsertTimeScaleCommand(TimeScaleCommand command)
    {
        int index = s_PendingTimeScaleCommands.Count;
        while (index > 0 && Compare(command, s_PendingTimeScaleCommands[index - 1]) < 0)
            index--;
        s_PendingTimeScaleCommands.Insert(index, command);
    }

    private static int Compare(TimeScaleCommand left, TimeScaleCommand right)
    {
        int frameOrder = left.EffectiveFrame.CompareTo(right.EffectiveFrame);
        return frameOrder != 0 ? frameOrder : left.Sequence.CompareTo(right.Sequence);
    }

    private static void ApplyTimeScaleCommand(TimeScaleCommand command)
    {
        switch (command.Kind)
        {
            case TimeScaleCommandKind.SetBasePlaybackScale:
                if (s_BasePlaybackScaleUnits != command.ScaleUnits)
                {
                    s_BasePlaybackScaleUnits = command.ScaleUnits;
                    MarkChanged();
                }
                return;
            case TimeScaleCommandKind.SetBulletTimeScale:
                if (s_BulletTimeScales.TryGetValue(command.SourceId, out int current)
                    && current == command.ScaleUnits)
                {
                    return;
                }

                s_BulletTimeScales[command.SourceId] = command.ScaleUnits;
                RecalculateBulletTimeScale();
                MarkChanged();
                return;
            case TimeScaleCommandKind.RemoveBulletTimeScale:
                if (!s_BulletTimeScales.Remove(command.SourceId))
                {
                    throw new InvalidOperationException(
                        $"LogicTimeControlService.PrepareFrame failed: bullet-time source is not active. sourceId={command.SourceId}, sequence={command.Sequence}.");
                }

                RecalculateBulletTimeScale();
                MarkChanged();
                return;
            default:
                throw new InvalidOperationException(
                    $"LogicTimeControlService.PrepareFrame failed: unsupported command kind {command.Kind}.");
        }
    }

    private static void RecalculateBulletTimeScale()
    {
        int result = NormalScaleUnits;
        foreach (KeyValuePair<int, int> pair in s_BulletTimeScales)
        {
            if (pair.Value < result)
                result = pair.Value;
        }

        s_BulletTimeScaleUnits = result;
    }

    private static void ValidateTimeScaleCommand(TimeScaleCommand command)
    {
        if (command.EffectiveFrame == 0)
            throw new ArgumentOutOfRangeException(nameof(command), "Time-scale command frame must be positive.");
        if (command.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(command), "Time-scale command sequence must be positive.");

        switch (command.Kind)
        {
            case TimeScaleCommandKind.SetBasePlaybackScale:
                if (command.SourceId != 0)
                    throw new ArgumentOutOfRangeException(nameof(command), "Base playback command source id must be zero.");
                ValidateBasePlaybackScale(command.ScaleUnits);
                return;
            case TimeScaleCommandKind.SetBulletTimeScale:
                ValidatePositiveSourceId(command.SourceId, "Bullet-time source id must be positive.");
                ValidateBulletTimeScale(command.ScaleUnits);
                return;
            case TimeScaleCommandKind.RemoveBulletTimeScale:
                ValidatePositiveSourceId(command.SourceId, "Bullet-time source id must be positive.");
                if (command.ScaleUnits != 0)
                    throw new ArgumentOutOfRangeException(nameof(command), "Remove bullet-time command scale must be zero.");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unsupported time-scale command kind.");
        }
    }

    private static void ValidatePauseControlCommand(PauseControlCommand command)
    {
        if (command.EffectiveFrame == 0)
            throw new ArgumentOutOfRangeException(nameof(command), "Pause command frame must be positive.");
        if (command.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(command), "Pause command sequence must be positive.");
        ValidatePositiveSourceId(command.SourceId, "Pause source id must be positive.");
        if (command.Kind != PauseControlCommandKind.Acquire
            && command.Kind != PauseControlCommandKind.Release)
        {
            throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unsupported pause command kind.");
        }
    }

    private static void ValidateSnapshotCollections(LogicTimeControlSnapshot snapshot)
    {
        int previousSourceId = 0;
        for (int i = 0; i < snapshot.BulletTimeScales.Count; i++)
        {
            BulletTimeScaleSnapshot entry = snapshot.BulletTimeScales[i];
            ValidatePositiveSourceId(entry.SourceId, "Snapshot bullet-time source id must be positive.");
            ValidateBulletTimeScale(entry.ScaleUnits);
            if (entry.SourceId <= previousSourceId)
                throw new InvalidOperationException("Snapshot bullet-time sources must be strictly ordered.");
            previousSourceId = entry.SourceId;
        }

        previousSourceId = 0;
        for (int i = 0; i < snapshot.PauseSources.Count; i++)
        {
            int sourceId = snapshot.PauseSources[i];
            ValidatePositiveSourceId(sourceId, "Snapshot pause source id must be positive.");
            if (sourceId <= previousSourceId)
                throw new InvalidOperationException("Snapshot pause sources must be strictly ordered.");
            previousSourceId = sourceId;
        }

        TimeScaleCommand previous = default;
        for (int i = 0; i < snapshot.PendingTimeScaleCommands.Count; i++)
        {
            TimeScaleCommand command = snapshot.PendingTimeScaleCommands[i];
            ValidateTimeScaleCommand(command);
            if (command.EffectiveFrame <= snapshot.CurrentFrame)
                throw new InvalidOperationException("Snapshot contains a time-scale command for a completed frame.");
            if (command.Sequence > snapshot.LastAcceptedSequence)
                throw new InvalidOperationException("Snapshot command sequence exceeds the accepted sequence.");
            if (i > 0 && Compare(previous, command) >= 0)
                throw new InvalidOperationException("Snapshot time-scale commands must be strictly ordered.");
            previous = command;
        }
    }

    private static void ValidateBasePlaybackScale(int scaleUnits)
    {
        if (scaleUnits <= 0 || scaleUnits > MaxBasePlaybackScaleUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleUnits), scaleUnits,
                $"Base playback scale must be in [1, {MaxBasePlaybackScaleUnits}].");
        }
    }

    private static void ValidateBulletTimeScale(int scaleUnits)
    {
        if (scaleUnits < MinBulletTimeScaleUnits || scaleUnits > NormalScaleUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleUnits), scaleUnits,
                $"Bullet-time scale must be in [{MinBulletTimeScaleUnits}, {NormalScaleUnits}].");
        }
    }

    private static void ValidatePositiveSourceId(int sourceId, string message)
    {
        if (sourceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceId), sourceId, message);
    }

    private static void AcceptSequence(ulong sequence)
    {
        if (sequence <= s_LastAcceptedSequence)
        {
            throw new InvalidOperationException(
                $"LogicTimeControlService rejected a non-increasing command sequence. previous={s_LastAcceptedSequence}, actual={sequence}.");
        }

        s_LastAcceptedSequence = sequence;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicTimeControlService is not active.");
    }

    private static void MarkChanged()
    {
        Revision = checked(Revision + 1);
        RaiseChanged();
    }

    private static void RaiseChanged()
    {
        Changed?.Invoke();
    }
}

public static class LogicTimeControlSources
{
    public const int LevelSwitchUiPause = 1;
    public const int RuntimeLevelSwitchPause = 2;
}
