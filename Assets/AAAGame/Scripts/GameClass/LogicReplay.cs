using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class LogicStateHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong m_Hash = OffsetBasis;

    public ulong Hash => m_Hash;

    public void Reset()
    {
        m_Hash = OffsetBasis;
    }

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
            hasher.Add(entry.ExpirationFrameExclusive);
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
        hasher.Add(command.DurationTicks);
    }
}

public sealed class LogicReplayFrameRecord
{
    internal LogicReplayFrameRecord(
        LogicInputFrame inputFrame,
        ulong inputHash,
        ulong timeControlHash,
        LogicGameplayStateDigest gameplayDigest,
        ulong fullHash)
    {
        if (gameplayDigest.HasDetails && gameplayDigest.FrameId != inputFrame.FrameId)
        {
            throw new InvalidOperationException(
                $"LogicReplayFrameRecord gameplay digest frame mismatch. input={inputFrame.FrameId}, gameplay={gameplayDigest.FrameId}.");
        }

        InputFrame = inputFrame;
        InputHash = inputHash;
        TimeControlHash = timeControlHash;
        GameplayDigest = gameplayDigest;
        GameplayStateHash = gameplayDigest.GameplayStateHash;
        FullHash = fullHash;
    }

    public ulong FrameId => InputFrame.FrameId;
    public LogicInputFrame InputFrame { get; }
    public ulong InputHash { get; }
    public ulong TimeControlHash { get; }
    public LogicGameplayStateDigest GameplayDigest { get; }
    public ulong GameplayStateHash { get; }
    public ulong FullHash { get; }
}

public sealed class LogicReplayLog
{
    public const int CurrentProtocolVersion = 105;
    public const string CurrentContentVersion = "Avenge-30Hz-v105";

    internal LogicReplayLog(
        LogicTimeControlSnapshot initialTimeControlSnapshot,
        LogicReplayFrameRecord[] frames,
        TimeScaleCommand[] timeScaleCommands,
        PauseControlCommand[] pauseControlCommands,
        LogicPhaseCommand[] phaseCommands,
        LogicTechEffectCommand[] techEffectCommands,
        LogicInteractionCommand[] interactionCommands,
        LogicCardCommand[] cardCommands,
        LogicSkillSlotCommand[] skillSlotCommands,
        LogicSkillCastCommand[] skillCastCommands,
        LogicInGameValueCommand[] inGameValueCommands,
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
        CardCommands = Array.AsReadOnly((LogicCardCommand[])cardCommands.Clone());
        SkillSlotCommands = Array.AsReadOnly((LogicSkillSlotCommand[])skillSlotCommands.Clone());
        SkillCastCommands = Array.AsReadOnly((LogicSkillCastCommand[])skillCastCommands.Clone());
        InGameValueCommands = Array.AsReadOnly((LogicInGameValueCommand[])inGameValueCommands.Clone());
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
    public ReadOnlyCollection<LogicCardCommand> CardCommands { get; }
    public ReadOnlyCollection<LogicSkillSlotCommand> SkillSlotCommands { get; }
    public ReadOnlyCollection<LogicSkillCastCommand> SkillCastCommands { get; }
    public ReadOnlyCollection<LogicInGameValueCommand> InGameValueCommands { get; }
    public ReadOnlyCollection<LogicEntityLifecycleCommand> LifecycleCommands { get; }
    public ReadOnlyCollection<LogicObstacleCommand> ObstacleCommands { get; }

    public static void RequireCurrentVersion(int protocolVersion, string contentVersion)
    {
        if (protocolVersion != CurrentProtocolVersion)
        {
            throw new InvalidOperationException(
                $"Logic replay protocol mismatch. expected={CurrentProtocolVersion}, actual={protocolVersion}.");
        }

        if (!string.Equals(contentVersion, CurrentContentVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Logic replay content mismatch. expected='{CurrentContentVersion}', actual='{contentVersion}'.");
        }
    }
}

public sealed class LogicReplayRecorder
{
    private readonly List<LogicReplayFrameRecord> m_Frames = new List<LogicReplayFrameRecord>();
    private readonly List<TimeScaleCommand> m_TimeScaleCommands = new List<TimeScaleCommand>();
    private readonly List<PauseControlCommand> m_PauseControlCommands = new List<PauseControlCommand>();
    private readonly List<LogicPhaseCommand> m_PhaseCommands = new List<LogicPhaseCommand>();
    private readonly List<LogicTechEffectCommand> m_TechEffectCommands = new List<LogicTechEffectCommand>();
    private readonly List<LogicInteractionCommand> m_InteractionCommands = new List<LogicInteractionCommand>();
    private readonly List<LogicCardCommand> m_CardCommands = new List<LogicCardCommand>();
    private readonly List<LogicSkillSlotCommand> m_SkillSlotCommands = new List<LogicSkillSlotCommand>();
    private readonly List<LogicSkillCastCommand> m_SkillCastCommands = new List<LogicSkillCastCommand>();
    private readonly List<LogicInGameValueCommand> m_InGameValueCommands = new List<LogicInGameValueCommand>();
    private readonly List<LogicEntityLifecycleCommand> m_LifecycleCommands = new List<LogicEntityLifecycleCommand>();
    private readonly List<LogicObstacleCommand> m_ObstacleCommands = new List<LogicObstacleCommand>();

    private LogicTimeControlSnapshot m_InitialTimeControlSnapshot;
    private ulong m_NextFrame;
    private bool m_TracksPhases;
    private bool m_TracksTechEffects;
    private bool m_TracksInteractions;
    private bool m_TracksCards;
    private bool m_TracksSkillSlots;
    private bool m_TracksSkillCasts;
    private bool m_TracksInGameValues;
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
        m_CardCommands.Clear();
        m_SkillSlotCommands.Clear();
        m_SkillCastCommands.Clear();
        m_InGameValueCommands.Clear();
        m_LifecycleCommands.Clear();
        m_ObstacleCommands.Clear();
        m_InitialTimeControlSnapshot = LogicTimeControlService.CaptureSnapshot();
        m_NextFrame = checked(m_InitialTimeControlSnapshot.CurrentFrame + 1);
        LogicTimeControlService.TimeScaleCommandAccepted += OnTimeScaleCommandAccepted;
        LogicTimeControlService.PauseControlCommandApplied += OnPauseControlCommandApplied;
        m_TracksPhases = LogicPhaseCommandService.IsActive;
        m_TracksTechEffects = LogicTechEffectCommandService.IsActive;
        m_TracksInteractions = LogicInteractionCommandService.IsActive;
        m_TracksCards = LogicCardCommandService.IsActive;
        m_TracksSkillSlots = LogicSkillSlotCommandService.IsActive;
        m_TracksSkillCasts = LogicSkillCastCommandService.IsActive;
        m_TracksInGameValues = LogicInGameValueCommandService.IsActive;
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
        if (m_TracksCards)
        {
            for (int i = 0; i < LogicCardCommandService.History.Count; i++)
                m_CardCommands.Add(LogicCardCommandService.History[i]);
            LogicCardCommandService.CommandRecorded += OnCardCommandRecorded;
        }
        if (m_TracksSkillSlots)
        {
            for (int i = 0; i < LogicSkillSlotCommandService.History.Count; i++)
                m_SkillSlotCommands.Add(LogicSkillSlotCommandService.History[i]);
            LogicSkillSlotCommandService.CommandRecorded += OnSkillSlotCommandRecorded;
        }
        if (m_TracksSkillCasts)
        {
            for (int i = 0; i < LogicSkillCastCommandService.History.Count; i++)
                m_SkillCastCommands.Add(LogicSkillCastCommandService.History[i]);
            LogicSkillCastCommandService.CommandRecorded += OnSkillCastCommandRecorded;
        }
        if (m_TracksInGameValues)
        {
            for (int i = 0; i < LogicInGameValueCommandService.History.Count; i++)
                m_InGameValueCommands.Add(LogicInGameValueCommandService.History[i]);
            LogicInGameValueCommandService.CommandRecorded += OnInGameValueCommandRecorded;
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
        return RecordFrame(inputFrame, LogicGameplayStateDigest.FromOpaqueHash(gameplayStateHash));
    }

    public LogicReplayFrameRecord RecordFrame(
        LogicInputFrame inputFrame,
        LogicGameplayStateDigest gameplayDigest)
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
            gameplayDigest.GameplayStateHash);
        LogicInputFrame recordedInputFrame = inputFrame.Freeze();
        var record = new LogicReplayFrameRecord(
            recordedInputFrame,
            inputHash,
            timeControlHash,
            gameplayDigest,
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
        if (m_TracksCards)
            LogicCardCommandService.CommandRecorded -= OnCardCommandRecorded;
        if (m_TracksSkillSlots)
            LogicSkillSlotCommandService.CommandRecorded -= OnSkillSlotCommandRecorded;
        if (m_TracksSkillCasts)
            LogicSkillCastCommandService.CommandRecorded -= OnSkillCastCommandRecorded;
        if (m_TracksInGameValues)
            LogicInGameValueCommandService.CommandRecorded -= OnInGameValueCommandRecorded;
        if (m_TracksLifecycle)
            LogicEntityLifecycleService.CommandRecorded -= OnLifecycleCommandRecorded;
        if (m_TracksObstacles)
            LogicObstacleCommandService.CommandRecorded -= OnObstacleCommandRecorded;
        m_TracksPhases = false;
        m_TracksTechEffects = false;
        m_TracksInteractions = false;
        m_TracksCards = false;
        m_TracksSkillSlots = false;
        m_TracksSkillCasts = false;
        m_TracksInGameValues = false;
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
            m_CardCommands.ToArray(),
            m_SkillSlotCommands.ToArray(),
            m_SkillCastCommands.ToArray(),
            m_InGameValueCommands.ToArray(),
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

    private void OnCardCommandRecorded(LogicCardCommand command)
    {
        m_CardCommands.Add(command);
    }

    private void OnSkillSlotCommandRecorded(LogicSkillSlotCommand command)
    {
        m_SkillSlotCommands.Add(command);
    }

    private void OnSkillCastCommandRecorded(LogicSkillCastCommand command)
    {
        m_SkillCastCommands.Add(command);
    }

    private void OnInGameValueCommandRecorded(LogicInGameValueCommand command)
    {
        m_InGameValueCommands.Add(command);
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
    public const string RuntimeRecordingCommandLineArgument = "-recordLogicReplay";

    private static LogicReplayRecorder s_Recorder;
    private static readonly bool s_CommandLineRecordingRequested =
        HasCommandLineArgument(RuntimeRecordingCommandLineArgument);

    public static bool IsRecording => s_Recorder != null && s_Recorder.IsRecording;
    public static bool RuntimeSessionRecordingEnabled { get; private set; }
    public static bool ShouldRecordRuntimeSession =>
        RuntimeSessionRecordingEnabled || s_CommandLineRecordingRequested;
    public static LogicReplayLog LastCompletedLog { get; private set; }

    public static void SetRuntimeSessionRecordingEnabled(bool enabled)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException(
                "LogicReplayRuntime.SetRuntimeSessionRecordingEnabled failed: recording is already active.");
        }

        RuntimeSessionRecordingEnabled = enabled;
    }

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

    public static LogicReplayFrameRecord RecordFrame(
        LogicInputFrame inputFrame,
        LogicGameplayStateDigest gameplayDigest)
    {
        if (!IsRecording)
            throw new InvalidOperationException("LogicReplayRuntime.RecordFrame failed: no recorder is active.");
        return s_Recorder.RecordFrame(inputFrame, gameplayDigest);
    }

    public static LogicReplayLog EndRecording()
    {
        if (!IsRecording)
            throw new InvalidOperationException("LogicReplayRuntime.EndRecording failed: no recorder is active.");

        LastCompletedLog = s_Recorder.End();
        s_Recorder = null;
        return LastCompletedLog;
    }

    private static bool HasCommandLineArgument(string expected)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], expected, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

public sealed class LogicReplayInputSource
{
    private readonly LogicReplayLog m_Log;
    private int m_NextIndex;

    public LogicReplayInputSource(LogicReplayLog log)
    {
        m_Log = log ?? throw new ArgumentNullException(nameof(log));
        LogicReplayLog.RequireCurrentVersion(m_Log.ProtocolVersion, m_Log.ContentVersion);
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
                return new LogicReplayDivergence(
                    true,
                    expectedFrame.FrameId,
                    FindInputDivergenceField(expectedFrame.InputFrame, actualFrame.InputFrame));
            if (expectedFrame.TimeControlHash != actualFrame.TimeControlHash)
                return new LogicReplayDivergence(true, expectedFrame.FrameId, "TimeControlHash");
            if (expectedFrame.GameplayStateHash != actualFrame.GameplayStateHash)
            {
                return new LogicReplayDivergence(
                    true,
                    expectedFrame.FrameId,
                    FindGameplayDivergenceField(expectedFrame.GameplayDigest, actualFrame.GameplayDigest));
            }
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

    private static string FindInputDivergenceField(LogicInputFrame expected, LogicInputFrame actual)
    {
        if (expected == null || actual == null) return "InputFrame";
        if (expected.PlayerId != actual.PlayerId) return "Input.PlayerId";
        if (expected.WorldMove.x != actual.WorldMove.x) return "Input.WorldMove.X";
        if (expected.WorldMove.y != actual.WorldMove.y) return "Input.WorldMove.Y";
        if (expected.HeldBits != actual.HeldBits) return "Input.HeldBits";
        if (expected.PressedBits != actual.PressedBits) return "Input.PressedBits";
        if (expected.ReleasedBits != actual.ReleasedBits) return "Input.ReleasedBits";
        if (expected.FirstSequence != actual.FirstSequence) return "Input.FirstSequence";
        if (expected.LastSequence != actual.LastSequence) return "Input.LastSequence";

        for (int i = 0; i < LogicInputTimeline.ButtonCount; i++)
        {
            var button = (LogicInputButton)i;
            if (expected.GetPressCount(button) != actual.GetPressCount(button))
                return $"Input.PressCount[{button}]";
        }

        if (expected.Events.Count != actual.Events.Count) return "Input.EventCount";
        for (int i = 0; i < expected.Events.Count; i++)
        {
            RawInputEvent left = expected.Events[i];
            RawInputEvent right = actual.Events[i];
            if (left.Sequence != right.Sequence) return $"Input.Events[{i}].Sequence";
            if (left.Kind != right.Kind) return $"Input.Events[{i}].Kind";
            if (left.Button != right.Button) return $"Input.Events[{i}].Button";
            if (left.Vector.x != right.Vector.x) return $"Input.Events[{i}].Vector.X";
            if (left.Vector.y != right.Vector.y) return $"Input.Events[{i}].Vector.Y";
        }

        return expected.Checksum != actual.Checksum ? "Input.Checksum" : "InputHash";
    }

    private static string FindGameplayDivergenceField(
        LogicGameplayStateDigest expected,
        LogicGameplayStateDigest actual)
    {
        if (!expected.HasDetails || !actual.HasDetails)
            return "GameplayStateHash";
        if (expected.FrameId != actual.FrameId) return "Gameplay.FrameId";
        if (expected.EconomyHash != actual.EconomyHash) return "Gameplay.Economy";
        if (expected.CommandsHash != actual.CommandsHash) return "Gameplay.Commands";
        if (expected.WorldRulesHash != actual.WorldRulesHash) return "Gameplay.WorldRules";
        if (expected.EntitiesHash != actual.EntitiesHash) return "Gameplay.Entities";
        if (expected.LifecycleHash != actual.LifecycleHash) return "Gameplay.Lifecycle";
        if (expected.ObstaclesHash != actual.ObstaclesHash) return "Gameplay.Obstacles";
        if (expected.DamageEventsHash != actual.DamageEventsHash) return "Gameplay.DamageEvents";
        if (expected.ProjectilesHash != actual.ProjectilesHash) return "Gameplay.Projectiles";

        string navigationField = FindNavigationDivergenceField(expected.Navigation, actual.Navigation);
        if (navigationField != null)
            return navigationField;
        if (expected.AllocatorsHash != actual.AllocatorsHash) return "Gameplay.Allocators";
        return "GameplayStateHash";
    }

    private static string FindNavigationDivergenceField(
        LogicNavigationAuthorityDigest expected,
        LogicNavigationAuthorityDigest actual)
    {
        if (!expected.HasDetails || !actual.HasDetails)
            return expected.FixedCorridorBuildsHash != actual.FixedCorridorBuildsHash
                ? "Gameplay.Navigation"
                : null;
        if (expected.WorldAndConfigHash != actual.WorldAndConfigHash) return "Gameplay.Navigation.WorldAndConfig";
        if (expected.CheckpointLiveStateHash != actual.CheckpointLiveStateHash) return "Gameplay.Navigation.CheckpointLiveState";
        if (expected.WorldProgressHash != actual.WorldProgressHash) return "Gameplay.Navigation.WorldProgress";
        if (expected.RuntimeObstaclesHash != actual.RuntimeObstaclesHash) return "Gameplay.Navigation.RuntimeObstacles";
        if (expected.AgentsHash != actual.AgentsHash) return "Gameplay.Navigation.Agents";
        if (expected.CachesHash != actual.CachesHash) return "Gameplay.Navigation.Caches";
        if (expected.FlowTilesHash != actual.FlowTilesHash) return "Gameplay.Navigation.FlowTiles";
        if (expected.FlowTileBuildQueueHash != actual.FlowTileBuildQueueHash) return "Gameplay.Navigation.FlowTileBuildQueue";
        if (expected.SharedGoalBuildQueueHash != actual.SharedGoalBuildQueueHash) return "Gameplay.Navigation.SharedGoalBuildQueue";
        if (expected.MovingTargetAnchorsHash != actual.MovingTargetAnchorsHash) return "Gameplay.Navigation.MovingTargetAnchors";
        if (expected.GoalReservationsHash != actual.GoalReservationsHash) return "Gameplay.Navigation.GoalReservations";
        if (expected.FixedPortalOwnersHash != actual.FixedPortalOwnersHash) return "Gameplay.Navigation.FixedPortalOwners";
        if (expected.FixedCorridorBuildsHash != actual.FixedCorridorBuildsHash) return "Gameplay.Navigation.FixedCorridorBuilds";
        return null;
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

        if (expected.CardCommands.Count != actual.CardCommands.Count)
            return new LogicReplayDivergence(true, 0, "CardCommandCount");
        for (int i = 0; i < expected.CardCommands.Count; i++)
        {
            LogicCardCommand left = expected.CardCommands[i];
            LogicCardCommand right = actual.CardCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.Kind != right.Kind
                || left.CardRuntimeId != right.CardRuntimeId
                || left.SelectedPosition != right.SelectedPosition)
            {
                return new LogicReplayDivergence(true, Math.Min(left.EffectiveFrame, right.EffectiveFrame), "CardCommand");
            }
        }

        if (expected.SkillSlotCommands.Count != actual.SkillSlotCommands.Count)
            return new LogicReplayDivergence(true, 0, "SkillSlotCommandCount");
        for (int i = 0; i < expected.SkillSlotCommands.Count; i++)
        {
            LogicSkillSlotCommand left = expected.SkillSlotCommands[i];
            LogicSkillSlotCommand right = actual.SkillSlotCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.FromIndex != right.FromIndex
                || left.ToIndex != right.ToIndex)
            {
                return new LogicReplayDivergence(
                    true,
                    Math.Min(left.EffectiveFrame, right.EffectiveFrame),
                    "SkillSlotCommand");
            }
        }

        if (expected.SkillCastCommands.Count != actual.SkillCastCommands.Count)
            return new LogicReplayDivergence(true, 0, "SkillCastCommandCount");
        for (int i = 0; i < expected.SkillCastCommands.Count; i++)
        {
            LogicSkillCastCommand left = expected.SkillCastCommands[i];
            LogicSkillCastCommand right = actual.SkillCastCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.CasterId != right.CasterId
                || left.SlotIndex != right.SlotIndex
                || left.HasRequestedWorldPosition != right.HasRequestedWorldPosition
                || left.RequestedWorldPosition != right.RequestedWorldPosition)
            {
                return new LogicReplayDivergence(
                    true,
                    Math.Min(left.EffectiveFrame, right.EffectiveFrame),
                    "SkillCastCommand");
            }
        }

        if (expected.InGameValueCommands.Count != actual.InGameValueCommands.Count)
            return new LogicReplayDivergence(true, 0, "InGameValueCommandCount");
        for (int i = 0; i < expected.InGameValueCommands.Count; i++)
        {
            LogicInGameValueCommand left = expected.InGameValueCommands[i];
            LogicInGameValueCommand right = actual.InGameValueCommands[i];
            if (left.EffectiveFrame != right.EffectiveFrame
                || left.Sequence != right.Sequence
                || left.ValueType != right.ValueType
                || left.Delta != right.Delta)
            {
                return new LogicReplayDivergence(
                    true,
                    Math.Min(left.EffectiveFrame, right.EffectiveFrame),
                    "InGameValueCommand");
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
                || left.EntityId != right.EntityId)
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
