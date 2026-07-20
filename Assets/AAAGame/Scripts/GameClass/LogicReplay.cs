using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class LogicStateHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong m_Hash = OffsetBasis;

    public ulong Hash => m_Hash;

    public void Add(bool value)
    {
        Add(value ? (byte)1 : (byte)0);
    }

    public void Add(byte value)
    {
        m_Hash ^= value;
        m_Hash *= Prime;
    }

    public void Add(int value)
    {
        Add(unchecked((uint)value));
    }

    public void Add(uint value)
    {
        for (int i = 0; i < sizeof(uint); i++)
            Add((byte)(value >> (i * 8)));
    }

    public void Add(long value)
    {
        Add(unchecked((ulong)value));
    }

    public void Add(ulong value)
    {
        for (int i = 0; i < sizeof(ulong); i++)
            Add((byte)(value >> (i * 8)));
    }

    public void Add(string value)
    {
        if (value == null)
        {
            Add(-1);
            return;
        }

        Add(value.Length);
        for (int i = 0; i < value.Length; i++)
            Add((uint)value[i]);
    }

    public static ulong ComputeInputHash(LogicInputFrame frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));

        var hasher = new LogicStateHasher();
        hasher.Add(0x494E50555446524DUL);
        hasher.Add(frame.FrameId);
        hasher.Add(frame.PlayerId);
        hasher.Add(frame.WorldMove.x.RawValue);
        hasher.Add(frame.WorldMove.y.RawValue);
        hasher.Add(frame.SelectScreenPosition.x.RawValue);
        hasher.Add(frame.SelectScreenPosition.y.RawValue);
        hasher.Add(frame.HeldBits);
        hasher.Add(frame.PressedBits);
        hasher.Add(frame.ReleasedBits);
        hasher.Add(frame.FirstSequence);
        hasher.Add(frame.LastSequence);
        hasher.Add(frame.Checksum);

        for (int i = 0; i < LogicInputTimeline.ButtonCount; i++)
            hasher.Add(frame.GetPressCount((LogicInputButton)i));

        hasher.Add(frame.Events.Count);
        for (int i = 0; i < frame.Events.Count; i++)
        {
            RawInputEvent inputEvent = frame.Events[i];
            hasher.Add(inputEvent.Sequence);
            hasher.Add((int)inputEvent.Kind);
            hasher.Add((int)inputEvent.Button);
            hasher.Add(inputEvent.Vector.x.RawValue);
            hasher.Add(inputEvent.Vector.y.RawValue);
        }

        return hasher.Hash;
    }

    public static ulong ComputeTimeControlHash(LogicTimeControlSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var hasher = new LogicStateHasher();
        hasher.Add(0x54494D454354524CUL);
        hasher.Add(snapshot.CurrentFrame);
        hasher.Add(snapshot.LastAcceptedSequence);
        hasher.Add(snapshot.Revision);
        hasher.Add(snapshot.BasePlaybackScaleUnits);

        hasher.Add(snapshot.BulletTimeScales.Count);
        for (int i = 0; i < snapshot.BulletTimeScales.Count; i++)
        {
            BulletTimeScaleSnapshot entry = snapshot.BulletTimeScales[i];
            hasher.Add(entry.SourceId);
            hasher.Add(entry.ScaleUnits);
        }

        hasher.Add(snapshot.PauseSources.Count);
        for (int i = 0; i < snapshot.PauseSources.Count; i++)
            hasher.Add(snapshot.PauseSources[i]);

        hasher.Add(snapshot.PendingTimeScaleCommands.Count);
        for (int i = 0; i < snapshot.PendingTimeScaleCommands.Count; i++)
            AddTimeScaleCommand(hasher, snapshot.PendingTimeScaleCommands[i]);

        return hasher.Hash;
    }

    public static ulong ComputeFrameHash(
        ulong frameId,
        ulong inputHash,
        ulong timeControlHash,
        ulong gameplayStateHash)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x4C4F47494346524DUL);
        hasher.Add(frameId);
        hasher.Add(inputHash);
        hasher.Add(timeControlHash);
        hasher.Add(gameplayStateHash);
        return hasher.Hash;
    }

    public static ulong ComputeTimeScaleCommandHash(TimeScaleCommand command)
    {
        var hasher = new LogicStateHasher();
        AddTimeScaleCommand(hasher, command);
        return hasher.Hash;
    }

    public static ulong ComputePauseControlCommandHash(PauseControlCommand command)
    {
        var hasher = new LogicStateHasher();
        hasher.Add(0x5041555345434D44UL);
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add((int)command.Kind);
        hasher.Add(command.SourceId);
        return hasher.Hash;
    }

    private static void AddTimeScaleCommand(LogicStateHasher hasher, TimeScaleCommand command)
    {
        hasher.Add(0x54494D4553434D44UL);
        hasher.Add(command.EffectiveFrame);
        hasher.Add(command.Sequence);
        hasher.Add((int)command.Kind);
        hasher.Add(command.SourceId);
        hasher.Add(command.ScaleUnits);
    }
}

public sealed class LogicReplayFrameRecord
{
    internal LogicReplayFrameRecord(
        LogicInputFrame inputFrame,
        ulong inputHash,
        ulong timeControlHash,
        ulong gameplayStateHash,
        ulong fullHash)
    {
        InputFrame = inputFrame;
        InputHash = inputHash;
        TimeControlHash = timeControlHash;
        GameplayStateHash = gameplayStateHash;
        FullHash = fullHash;
    }

    public ulong FrameId => InputFrame.FrameId;
    public LogicInputFrame InputFrame { get; }
    public ulong InputHash { get; }
    public ulong TimeControlHash { get; }
    public ulong GameplayStateHash { get; }
    public ulong FullHash { get; }
}

public sealed class LogicReplayLog
{
    public const int CurrentProtocolVersion = 4;
    public const string CurrentContentVersion = "Avenge-30Hz-v4";

    internal LogicReplayLog(
        LogicTimeControlSnapshot initialTimeControlSnapshot,
        LogicReplayFrameRecord[] frames,
        TimeScaleCommand[] timeScaleCommands,
        PauseControlCommand[] pauseControlCommands,
        LogicPhaseCommand[] phaseCommands,
        LogicTechEffectCommand[] techEffectCommands,
        LogicInteractionCommand[] interactionCommands,
        LogicEntityLifecycleCommand[] lifecycleCommands,
        LogicObstacleCommand[] obstacleCommands)
    {
        ProtocolVersion = CurrentProtocolVersion;
        ContentVersion = CurrentContentVersion;
        LogicFrameRate = LogicFrameRuntime.FrameRate;
        InitialRandomSeed = 0;
        InitialTimeControlSnapshot = initialTimeControlSnapshot;
        Frames = Array.AsReadOnly((LogicReplayFrameRecord[])frames.Clone());
        TimeScaleCommands = Array.AsReadOnly((TimeScaleCommand[])timeScaleCommands.Clone());
        PauseControlCommands = Array.AsReadOnly((PauseControlCommand[])pauseControlCommands.Clone());
        PhaseCommands = Array.AsReadOnly((LogicPhaseCommand[])phaseCommands.Clone());
        TechEffectCommands = Array.AsReadOnly((LogicTechEffectCommand[])techEffectCommands.Clone());
        InteractionCommands = Array.AsReadOnly((LogicInteractionCommand[])interactionCommands.Clone());
        LifecycleCommands = Array.AsReadOnly((LogicEntityLifecycleCommand[])lifecycleCommands.Clone());
        ObstacleCommands = Array.AsReadOnly((LogicObstacleCommand[])obstacleCommands.Clone());
    }

    public int ProtocolVersion { get; }
    public string ContentVersion { get; }
    public int LogicFrameRate { get; }
    public ulong InitialRandomSeed { get; }
    public LogicTimeControlSnapshot InitialTimeControlSnapshot { get; }
    public ReadOnlyCollection<LogicReplayFrameRecord> Frames { get; }
    public ReadOnlyCollection<TimeScaleCommand> TimeScaleCommands { get; }
    public ReadOnlyCollection<PauseControlCommand> PauseControlCommands { get; }
    public ReadOnlyCollection<LogicPhaseCommand> PhaseCommands { get; }
    public ReadOnlyCollection<LogicTechEffectCommand> TechEffectCommands { get; }
    public ReadOnlyCollection<LogicInteractionCommand> InteractionCommands { get; }
    public ReadOnlyCollection<LogicEntityLifecycleCommand> LifecycleCommands { get; }
    public ReadOnlyCollection<LogicObstacleCommand> ObstacleCommands { get; }
}

public sealed class LogicReplayRecorder
{
    private readonly List<LogicReplayFrameRecord> m_Frames = new List<LogicReplayFrameRecord>();
    private readonly List<TimeScaleCommand> m_TimeScaleCommands = new List<TimeScaleCommand>();
    private readonly List<PauseControlCommand> m_PauseControlCommands = new List<PauseControlCommand>();
    private readonly List<LogicPhaseCommand> m_PhaseCommands = new List<LogicPhaseCommand>();
    private readonly List<LogicTechEffectCommand> m_TechEffectCommands = new List<LogicTechEffectCommand>();
    private readonly List<LogicInteractionCommand> m_InteractionCommands = new List<LogicInteractionCommand>();
    private readonly List<LogicEntityLifecycleCommand> m_LifecycleCommands = new List<LogicEntityLifecycleCommand>();
    private readonly List<LogicObstacleCommand> m_ObstacleCommands = new List<LogicObstacleCommand>();

    private LogicTimeControlSnapshot m_InitialTimeControlSnapshot;
    private ulong m_NextFrame;
    private bool m_TracksPhases;
    private bool m_TracksTechEffects;
    private bool m_TracksInteractions;
    private bool m_TracksLifecycle;
    private bool m_TracksObstacles;

    public bool IsRecording { get; private set; }

    public void Begin()
    {
        if (IsRecording)
            throw new InvalidOperationException("LogicReplayRecorder.Begin failed: recorder is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicReplayRecorder.Begin failed: time-control timeline is not active.");

        m_Frames.Clear();
        m_TimeScaleCommands.Clear();
        m_PauseControlCommands.Clear();
        m_PhaseCommands.Clear();
        m_TechEffectCommands.Clear();
        m_InteractionCommands.Clear();
        m_LifecycleCommands.Clear();
        m_ObstacleCommands.Clear();
        m_InitialTimeControlSnapshot = LogicTimeControlService.CaptureSnapshot();
        m_NextFrame = checked(m_InitialTimeControlSnapshot.CurrentFrame + 1);
        LogicTimeControlService.TimeScaleCommandAccepted += OnTimeScaleCommandAccepted;
        LogicTimeControlService.PauseControlCommandApplied += OnPauseControlCommandApplied;
        m_TracksPhases = LogicPhaseCommandService.IsActive;
        m_TracksTechEffects = LogicTechEffectCommandService.IsActive;
        m_TracksInteractions = LogicInteractionCommandService.IsActive;
        m_TracksLifecycle = LogicEntityLifecycleService.IsActive;
        m_TracksObstacles = LogicObstacleCommandService.IsActive;
        if (m_TracksPhases)
        {
            for (int i = 0; i < LogicPhaseCommandService.History.Count; i++)
                m_PhaseCommands.Add(LogicPhaseCommandService.History[i]);
            LogicPhaseCommandService.CommandRecorded += OnPhaseCommandRecorded;
        }
        if (m_TracksTechEffects)
        {
            for (int i = 0; i < LogicTechEffectCommandService.History.Count; i++)
                m_TechEffectCommands.Add(LogicTechEffectCommandService.History[i]);
            LogicTechEffectCommandService.CommandRecorded += OnTechEffectCommandRecorded;
        }
        if (m_TracksInteractions)
        {
            for (int i = 0; i < LogicInteractionCommandService.History.Count; i++)
                m_InteractionCommands.Add(LogicInteractionCommandService.History[i]);
            LogicInteractionCommandService.CommandRecorded += OnInteractionCommandRecorded;
        }
        if (m_TracksLifecycle)
        {
            for (int i = 0; i < LogicEntityLifecycleService.Commands.Count; i++)
                m_LifecycleCommands.Add(LogicEntityLifecycleService.Commands[i]);
            LogicEntityLifecycleService.CommandRecorded += OnLifecycleCommandRecorded;
        }
        if (m_TracksObstacles)
        {
            for (int i = 0; i < LogicObstacleCommandService.History.Count; i++)
                m_ObstacleCommands.Add(LogicObstacleCommandService.History[i]);
            LogicObstacleCommandService.CommandRecorded += OnObstacleCommandRecorded;
        }
        IsRecording = true;
    }

    public LogicReplayFrameRecord RecordFrame(LogicInputFrame inputFrame, ulong gameplayStateHash = 0)
    {
        if (!IsRecording)
            throw new InvalidOperationException("LogicReplayRecorder.RecordFrame failed: recorder is not active.");
        if (inputFrame == null)
            throw new ArgumentNullException(nameof(inputFrame));
        if (inputFrame.FrameId != m_NextFrame)
        {
            throw new InvalidOperationException(
                $"LogicReplayRecorder.RecordFrame failed: non-contiguous frame. expected={m_NextFrame}, actual={inputFrame.FrameId}.");
        }

        LogicTimeControlSnapshot timeSnapshot = LogicTimeControlService.CaptureSnapshot();
        if (timeSnapshot.CurrentFrame != inputFrame.FrameId)
        {
            throw new InvalidOperationException(
                $"LogicReplayRecorder.RecordFrame failed: input/time frames differ. input={inputFrame.FrameId}, time={timeSnapshot.CurrentFrame}.");
        }

        ulong inputHash = LogicStateHasher.ComputeInputHash(inputFrame);
        ulong timeControlHash = LogicStateHasher.ComputeTimeControlHash(timeSnapshot);
        ulong fullHash = LogicStateHasher.ComputeFrameHash(
            inputFrame.FrameId,
            inputHash,
            timeControlHash,
            gameplayStateHash);
        var record = new LogicReplayFrameRecord(
            inputFrame,
            inputHash,
            timeControlHash,
            gameplayStateHash,
            fullHash);
        m_Frames.Add(record);
        m_NextFrame = checked(m_NextFrame + 1);
        return record;
    }

    public LogicReplayLog End()
    {
        if (!IsRecording)
            throw new InvalidOperationException("LogicReplayRecorder.End failed: recorder is not active.");

        LogicTimeControlService.TimeScaleCommandAccepted -= OnTimeScaleCommandAccepted;
        LogicTimeControlService.PauseControlCommandApplied -= OnPauseControlCommandApplied;
        if (m_TracksPhases)
            LogicPhaseCommandService.CommandRecorded -= OnPhaseCommandRecorded;
        if (m_TracksTechEffects)
            LogicTechEffectCommandService.CommandRecorded -= OnTechEffectCommandRecorded;
        if (m_TracksInteractions)
            LogicInteractionCommandService.CommandRecorded -= OnInteractionCommandRecorded;
        if (m_TracksLifecycle)
            LogicEntityLifecycleService.CommandRecorded -= OnLifecycleCommandRecorded;
        if (m_TracksObstacles)
            LogicObstacleCommandService.CommandRecorded -= OnObstacleCommandRecorded;
        m_TracksPhases = false;
        m_TracksTechEffects = false;
        m_TracksInteractions = false;
        m_TracksLifecycle = false;
        m_TracksObstacles = false;
        IsRecording = false;
        return new LogicReplayLog(
            m_InitialTimeControlSnapshot,
            m_Frames.ToArray(),
            m_TimeScaleCommands.ToArray(),
            m_PauseControlCommands.ToArray(),
            m_PhaseCommands.ToArray(),
            m_TechEffectCommands.ToArray(),
            m_InteractionCommands.ToArray(),
            m_LifecycleCommands.ToArray(),
            m_ObstacleCommands.ToArray());
    }

    private void OnTimeScaleCommandAccepted(TimeScaleCommand command)
    {
        m_TimeScaleCommands.Add(command);
    }

    private void OnPauseControlCommandApplied(PauseControlCommand command)
    {
        m_PauseControlCommands.Add(command);
    }

    private void OnPhaseCommandRecorded(LogicPhaseCommand command)
    {
        m_PhaseCommands.Add(command);
    }

    private void OnTechEffectCommandRecorded(LogicTechEffectCommand command)
    {
        m_TechEffectCommands.Add(command);
    }

    private void OnInteractionCommandRecorded(LogicInteractionCommand command)
    {
        m_InteractionCommands.Add(command);
    }

    private void OnLifecycleCommandRecorded(LogicEntityLifecycleCommand command)
    {
        m_LifecycleCommands.Add(command);
    }

    private void OnObstacleCommandRecorded(LogicObstacleCommand command)
    {
        m_ObstacleCommands.Add(command);
    }
}

public static class LogicReplayRuntime
{
    private static LogicReplayRecorder s_Recorder;

    public static bool IsRecording => s_Recorder != null && s_Recorder.IsRecording;
    public static LogicReplayLog LastCompletedLog { get; private set; }

    public static void BeginRecording()
    {
        if (IsRecording)
            throw new InvalidOperationException("LogicReplayRuntime.BeginRecording failed: a recorder is already active.");

        s_Recorder = new LogicReplayRecorder();
        s_Recorder.Begin();
    }

    public static LogicReplayFrameRecord RecordFrame(LogicInputFrame inputFrame, ulong gameplayStateHash = 0)
    {
        if (!IsRecording)
            throw new InvalidOperationException("LogicReplayRuntime.RecordFrame failed: no recorder is active.");
        return s_Recorder.RecordFrame(inputFrame, gameplayStateHash);
    }

    public static LogicReplayLog EndRecording()
    {
        if (!IsRecording)
            throw new InvalidOperationException("LogicReplayRuntime.EndRecording failed: no recorder is active.");

        LastCompletedLog = s_Recorder.End();
        s_Recorder = null;
        return LastCompletedLog;
    }
}

public sealed class LogicReplayInputSource
{
    private readonly LogicReplayLog m_Log;
    private int m_NextIndex;

    public LogicReplayInputSource(LogicReplayLog log)
    {
        m_Log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public LogicInputFrame ReadFrame(ulong frameId)
    {
        if (m_NextIndex >= m_Log.Frames.Count)
            throw new InvalidOperationException($"LogicReplayInputSource has no frame {frameId}.");

        LogicReplayFrameRecord record = m_Log.Frames[m_NextIndex];
        if (record.FrameId != frameId)
        {
            throw new InvalidOperationException(
                $"LogicReplayInputSource frame mismatch. expected={record.FrameId}, actual={frameId}.");
        }

        m_NextIndex++;
        return record.InputFrame;
    }
}

public readonly struct LogicReplayDivergence
{
    public LogicReplayDivergence(bool hasDivergence, ulong frameId, string field)
    {
        HasDivergence = hasDivergence;
        FrameId = frameId;
        Field = field;
    }

    public bool HasDivergence { get; }
    public ulong FrameId { get; }
    public string Field { get; }
}

public static class LogicReplayComparer
{
    public static LogicReplayDivergence FindFirstDivergence(LogicReplayLog expected, LogicReplayLog actual)
    {
        if (expected == null)
            throw new ArgumentNullException(nameof(expected));
        if (actual == null)
            throw new ArgumentNullException(nameof(actual));
        if (expected.ProtocolVersion != actual.ProtocolVersion)
            return new LogicReplayDivergence(true, 0, "ProtocolVersion");
        if (!string.Equals(expected.ContentVersion, actual.ContentVersion, StringComparison.Ordinal))
            return new LogicReplayDivergence(true, 0, "ContentVersion");
        if (expected.LogicFrameRate != actual.LogicFrameRate)
            return new LogicReplayDivergence(true, 0, "LogicFrameRate");
        if (expected.InitialRandomSeed != actual.InitialRandomSeed)
            return new LogicReplayDivergence(true, 0, "InitialRandomSeed");

        ulong expectedInitialHash = LogicStateHasher.ComputeTimeControlHash(expected.InitialTimeControlSnapshot);
        ulong actualInitialHash = LogicStateHasher.ComputeTimeControlHash(actual.InitialTimeControlSnapshot);
        if (expectedInitialHash != actualInitialHash)
        {
            return new LogicReplayDivergence(
                true,
                expected.InitialTimeControlSnapshot.CurrentFrame,
                "InitialTimeControlHash");
        }

        int sharedFrameCount = Math.Min(expected.Frames.Count, actual.Frames.Count);
        for (int i = 0; i < sharedFrameCount; i++)
        {
            LogicReplayFrameRecord expectedFrame = expected.Frames[i];
            LogicReplayFrameRecord actualFrame = actual.Frames[i];
            if (expectedFrame.FrameId != actualFrame.FrameId)
                return new LogicReplayDivergence(true, Math.Min(expectedFrame.FrameId, actualFrame.FrameId), "FrameId");
            if (expectedFrame.InputHash != actualFrame.InputHash)
                return new LogicReplayDivergence(true, expectedFrame.FrameId, "InputHash");
            if (expectedFrame.TimeControlHash != actualFrame.TimeControlHash)
                return new LogicReplayDivergence(true, expectedFrame.FrameId, "TimeControlHash");
            if (expectedFrame.GameplayStateHash != actualFrame.GameplayStateHash)
                return new LogicReplayDivergence(true, expectedFrame.FrameId, "GameplayStateHash");
            if (expectedFrame.FullHash != actualFrame.FullHash)
                return new LogicReplayDivergence(true, expectedFrame.FrameId, "FullHash");
        }

        if (expected.Frames.Count != actual.Frames.Count)
        {
            ulong frameId = sharedFrameCount > 0
                ? checked(expected.Frames[sharedFrameCount - 1].FrameId + 1)
                : checked(expected.InitialTimeControlSnapshot.CurrentFrame + 1);
            return new LogicReplayDivergence(true, frameId, "FrameCount");
        }

        LogicReplayDivergence commandDivergence = CompareCommands(expected, actual);
        return commandDivergence.HasDivergence
            ? commandDivergence
            : new LogicReplayDivergence(false, 0, null);
    }

    private static LogicReplayDivergence CompareCommands(LogicReplayLog expected, LogicReplayLog actual)
    {
        int sharedTimeScaleCount = Math.Min(expected.TimeScaleCommands.Count, actual.TimeScaleCommands.Count);
        for (int i = 0; i < sharedTimeScaleCount; i++)
        {
            TimeScaleCommand expectedCommand = expected.TimeScaleCommands[i];
            TimeScaleCommand actualCommand = actual.TimeScaleCommands[i];
            if (LogicStateHasher.ComputeTimeScaleCommandHash(expectedCommand)
                != LogicStateHasher.ComputeTimeScaleCommandHash(actualCommand))
            {
                return new LogicReplayDivergence(
                    true,
                    Math.Min(expectedCommand.EffectiveFrame, actualCommand.EffectiveFrame),
                    "TimeScaleCommand");
            }
        }

        if (expected.TimeScaleCommands.Count != actual.TimeScaleCommands.Count)
            return new LogicReplayDivergence(true, 0, "TimeScaleCommandCount");

        int sharedPauseCount = Math.Min(expected.PauseControlCommands.Count, actual.PauseControlCommands.Count);
        for (int i = 0; i < sharedPauseCount; i++)
        {
            PauseControlCommand expectedCommand = expected.PauseControlCommands[i];
            PauseControlCommand actualCommand = actual.PauseControlCommands[i];
            if (LogicStateHasher.ComputePauseControlCommandHash(expectedCommand)
                != LogicStateHasher.ComputePauseControlCommandHash(actualCommand))
            {
                return new LogicReplayDivergence(
                    true,
                    Math.Min(expectedCommand.EffectiveFrame, actualCommand.EffectiveFrame),
                    "PauseControlCommand");
            }
        }

        if (expected.PauseControlCommands.Count != actual.PauseControlCommands.Count)
            return new LogicReplayDivergence(true, 0, "PauseControlCommandCount");

        if (expected.PhaseCommands.Count != actual.PhaseCommands.Count)
            return new LogicReplayDivergence(true, 0, "PhaseCommandCount");
        for (int i = 0; i < expected.PhaseCommands.Count; i++)
        {
            LogicPhaseCommand left = expected.PhaseCommands[i];
            LogicPhaseCommand right = actual.PhaseCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.Phase != right.Phase)
            {
                return new LogicReplayDivergence(true, Math.Min(left.EffectiveFrame, right.EffectiveFrame), "PhaseCommand");
            }
        }

        if (expected.TechEffectCommands.Count != actual.TechEffectCommands.Count)
            return new LogicReplayDivergence(true, 0, "TechEffectCommandCount");
        for (int i = 0; i < expected.TechEffectCommands.Count; i++)
        {
            LogicTechEffectCommand left = expected.TechEffectCommands[i];
            LogicTechEffectCommand right = actual.TechEffectCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || !string.Equals(left.TechId, right.TechId, StringComparison.Ordinal)
                || left.IsStackable != right.IsStackable
                || left.OwnerFactionId != right.OwnerFactionId
                || !string.Equals(left.SourceBuildingInstanceId, right.SourceBuildingInstanceId, StringComparison.Ordinal))
            {
                return new LogicReplayDivergence(true, Math.Min(left.EffectiveFrame, right.EffectiveFrame), "TechEffectCommand");
            }
        }

        if (expected.InteractionCommands.Count != actual.InteractionCommands.Count)
            return new LogicReplayDivergence(true, 0, "InteractionCommandCount");
        for (int i = 0; i < expected.InteractionCommands.Count; i++)
        {
            LogicInteractionCommand left = expected.InteractionCommands[i];
            LogicInteractionCommand right = actual.InteractionCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.ActionKind != right.ActionKind
                || left.TargetEntityId != right.TargetEntityId
                || !string.Equals(left.TargetBuildingInstanceId, right.TargetBuildingInstanceId, StringComparison.Ordinal)
                || !string.Equals(left.PrimaryId, right.PrimaryId, StringComparison.Ordinal)
                || !string.Equals(left.SecondaryId, right.SecondaryId, StringComparison.Ordinal))
            {
                return new LogicReplayDivergence(true, Math.Min(left.EffectiveFrame, right.EffectiveFrame), "InteractionCommand");
            }
        }

        if (expected.LifecycleCommands.Count != actual.LifecycleCommands.Count)
            return new LogicReplayDivergence(true, 0, "LifecycleCommandCount");
        for (int i = 0; i < expected.LifecycleCommands.Count; i++)
        {
            LogicEntityLifecycleCommand left = expected.LifecycleCommands[i];
            LogicEntityLifecycleCommand right = actual.LifecycleCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.Kind != right.Kind
                || left.EntityId != right.EntityId
                || left.ViewEntityId != right.ViewEntityId)
            {
                return new LogicReplayDivergence(true, Math.Min(left.EffectiveFrame, right.EffectiveFrame), "LifecycleCommand");
            }
        }

        if (expected.ObstacleCommands.Count != actual.ObstacleCommands.Count)
            return new LogicReplayDivergence(true, 0, "ObstacleCommandCount");
        for (int i = 0; i < expected.ObstacleCommands.Count; i++)
        {
            LogicObstacleCommand left = expected.ObstacleCommands[i];
            LogicObstacleCommand right = actual.ObstacleCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.Kind != right.Kind
                || left.StableObstacleId != right.StableObstacleId
                || left.Center != right.Center
                || left.HalfExtents != right.HalfExtents
                || left.Radius != right.Radius)
            {
                return new LogicReplayDivergence(true, Math.Min(left.EffectiveFrame, right.EffectiveFrame), "ObstacleCommand");
            }
        }

        return new LogicReplayDivergence(false, 0, null);
    }
}
