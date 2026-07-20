using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct LogicPhaseCommand
{
    public LogicPhaseCommand(ulong effectiveFrame, ulong sequence, GamePhase phase)
    {
        EffectiveFrame = effectiveFrame;
        Sequence = sequence;
        Phase = phase;
    }

    public ulong EffectiveFrame { get; }
    public ulong Sequence { get; }
    public GamePhase Phase { get; }

    public LogicPhaseCommand WithEffectiveFrame(ulong effectiveFrame)
    {
        return new LogicPhaseCommand(effectiveFrame, Sequence, Phase);
    }
}

public static class LogicPhaseCommandService
{
    private static readonly List<LogicPhaseCommand> s_History = new List<LogicPhaseCommand>();
    private static readonly ReadOnlyCollection<LogicPhaseCommand> s_ReadOnlyHistory = s_History.AsReadOnly();
    private static readonly List<LogicPhaseCommand> s_Pending = new List<LogicPhaseCommand>();
    private static readonly List<LogicPhaseCommand> s_Due = new List<LogicPhaseCommand>();
    private static ulong s_LastSequence;

    public static bool IsActive { get; private set; }
    public static bool IsInitialized { get; private set; }
    public static bool IsApplyingFrame { get; private set; }
    public static bool IsWorldTransitionActive { get; private set; }
    public static GamePhase CurrentPhase { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static IReadOnlyList<LogicPhaseCommand> History => s_ReadOnlyHistory;
    public static event Action<LogicPhaseCommand> CommandRecorded;
    public static event Action<GamePhase, GamePhase> PhaseApplied;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicPhaseCommandService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicPhaseCommandService.BeginTimeline failed: logic time control is not active.");

        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicPhaseCommandService.EndTimeline failed: a command frame is being applied.");

        IsActive = false;
        ClearState();
    }

    public static void SetInitialPhase(GamePhase phase)
    {
        EnsureActive();
        ValidatePhase(phase);
        bool mayInitialize = LogicTimeControlService.CurrentFrame == 0
                             || (IsWorldTransitionActive && LogicTimeControlService.IsPaused);
        if (!mayInitialize || IsApplyingFrame || s_Pending.Count != 0)
        {
            throw new InvalidOperationException(
                $"LogicPhaseCommandService.SetInitialPhase failed: initial phase window is closed. frame={LogicTimeControlService.CurrentFrame}, pending={s_Pending.Count}, worldTransition={IsWorldTransitionActive}.");
        }

        CurrentPhase = phase;
        IsInitialized = true;
    }

    public static LogicPhaseCommand ScheduleForNextFrame(GamePhase phase)
    {
        EnsureReady();
        ValidatePhase(phase);
        var command = new LogicPhaseCommand(
            checked(LogicTimeControlService.CurrentFrame + 1),
            checked(s_LastSequence + 1),
            phase);
        s_LastSequence = command.Sequence;
        s_Pending.Add(command);
        s_History.Add(command);
        CommandRecorded?.Invoke(command);
        return command;
    }

    public static void ApplyFrame(ulong frameId)
    {
        ApplyFrame(frameId, command => PhaseManager.ApplyScheduledPhase(command.Phase));
    }

#if UNITY_EDITOR
    public static void ApplyFrameForTests(ulong frameId, Action<LogicPhaseCommand> sink)
    {
        ApplyFrame(frameId, sink);
    }
#endif

    private static void ApplyFrame(ulong frameId, Action<LogicPhaseCommand> sink)
    {
        EnsureReady();
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (frameId != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicPhaseCommandService.ApplyFrame failed: time-control frame mismatch. time={LogicTimeControlService.CurrentFrame}, requested={frameId}.");
        }
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicPhaseCommandService.ApplyFrame failed: nested command frame detected.");

        IsApplyingFrame = true;
        try
        {
            s_Due.Clear();
            for (int i = 0; i < s_Pending.Count; i++)
            {
                LogicPhaseCommand command = s_Pending[i];
                if (command.EffectiveFrame < frameId)
                {
                    throw new InvalidOperationException(
                        $"LogicPhaseCommandService.ApplyFrame failed: command missed its frame. sequence={command.Sequence}, effective={command.EffectiveFrame}, current={frameId}.");
                }
                if (command.EffectiveFrame == frameId)
                    s_Due.Add(command);
            }

            s_Due.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicPhaseCommand command = s_Due[i];
                GamePhase oldPhase = CurrentPhase;
                sink(command);
                CurrentPhase = command.Phase;
                if (oldPhase != CurrentPhase)
                    PhaseApplied?.Invoke(oldPhase, CurrentPhase);
            }

            for (int i = s_Pending.Count - 1; i >= 0; i--)
            {
                if (s_Pending[i].EffectiveFrame == frameId)
                    s_Pending.RemoveAt(i);
            }
            LastAppliedFrame = frameId;
        }
        finally
        {
            IsApplyingFrame = false;
            s_Due.Clear();
        }
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("LogicPhaseCommandService.ResetForWorldTransition failed: logic time is not paused.");
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicPhaseCommandService.ResetForWorldTransition failed: a command frame is being applied.");

        ClearState();
        IsWorldTransitionActive = true;
    }

    public static void ResetFrameTimeline()
    {
        EnsureReady();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException("LogicPhaseCommandService.ResetFrameTimeline failed: time-control frame is not zero.");
        if (s_Pending.Count != 0)
            throw new InvalidOperationException($"LogicPhaseCommandService.ResetFrameTimeline failed: {s_Pending.Count} pending commands remain.");

        LastAppliedFrame = 0;
        IsWorldTransitionActive = false;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        EnsureReady();
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add((int)CurrentPhase);
        hasher.Add(s_LastSequence);
        hasher.Add(LastAppliedFrame);
        hasher.Add(s_Pending.Count);
        for (int i = 0; i < s_Pending.Count; i++)
        {
            LogicPhaseCommand command = s_Pending[i];
            hasher.Add(command.EffectiveFrame);
            hasher.Add(command.Sequence);
            hasher.Add((int)command.Phase);
        }
    }

    private static void ValidatePhase(GamePhase phase)
    {
        if (!Enum.IsDefined(typeof(GamePhase), phase))
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown game phase.");
    }

    private static void EnsureReady()
    {
        EnsureActive();
        if (!IsInitialized)
            throw new InvalidOperationException("LogicPhaseCommandService operation failed: initial phase is not set.");
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicPhaseCommandService operation failed: service is not active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicPhaseCommandService operation failed: logic time control is not active.");
    }

    private static void ClearState()
    {
        IsInitialized = false;
        IsApplyingFrame = false;
        IsWorldTransitionActive = false;
        CurrentPhase = default;
        LastAppliedFrame = 0;
        s_LastSequence = 0;
        s_History.Clear();
        s_Pending.Clear();
        s_Due.Clear();
    }
}
