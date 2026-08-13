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
    private static readonly Action<LogicPhaseCommand> s_RuntimeSink = ApplyScheduledPhase;
    private static readonly Comparison<LogicPhaseCommand> s_CommandComparison = CompareCommands;
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

    public static GamePhase GetRequiredCurrentPhase()
    {
        EnsureReady();
        return CurrentPhase;
    }

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
        if (IsWorldTransitionActive && LogicTimeControlService.CurrentFrame == 0)
            IsWorldTransitionActive = false;
    }

    public static LogicPhaseCommand ScheduleForNextFrame(GamePhase phase)
    {
        EnsureReady();
        ValidatePhase(phase);
        return Schedule(phase, checked(LogicTimeControlService.CurrentFrame + 1));
    }

    public static LogicPhaseCommand Submit(GamePhase phase)
    {
        if (!LogicPausedOperationService.CanResolveImmediately
            || LogicPausedOperationService.IsExecuting)
        {
            return ScheduleForNextFrame(phase);
        }

        EnsureReady();
        ValidatePhase(phase);
        return LogicPausedOperationService.Execute(() =>
        {
            LogicPhaseCommand command = RecordCommand(phase, LogicTimeControlService.CurrentFrame, false);
            ApplyImmediate(command, s_RuntimeSink);
            return command;
        });
    }

#if UNITY_EDITOR
    public static LogicPhaseCommand ScheduleForEditorGate(GamePhase phase, ulong effectiveFrame)
    {
        EnsureReady();
        ValidatePhase(phase);
        if (effectiveFrame <= LogicTimeControlService.CurrentFrame)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveFrame),
                effectiveFrame,
                $"Editor gate phase command must target a future frame. current={LogicTimeControlService.CurrentFrame}.");
        }

        return Schedule(phase, effectiveFrame);
    }
#endif

    private static LogicPhaseCommand Schedule(GamePhase phase, ulong effectiveFrame)
    {
        return RecordCommand(phase, effectiveFrame, true);
    }

    private static LogicPhaseCommand RecordCommand(GamePhase phase, ulong effectiveFrame, bool pending)
    {
        var command = new LogicPhaseCommand(
            effectiveFrame,
            checked(s_LastSequence + 1),
            phase);
        s_LastSequence = command.Sequence;
        if (pending)
            s_Pending.Add(command);
        s_History.Add(command);
        CommandRecorded?.Invoke(command);
        return command;
    }

    private static void ApplyImmediate(LogicPhaseCommand command, Action<LogicPhaseCommand> sink)
    {
        if (!LogicPausedOperationService.IsExecuting)
            throw new InvalidOperationException("Immediate phase application requires a paused-operation settlement.");
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));
        if (IsApplyingFrame)
            throw new InvalidOperationException("LogicPhaseCommandService.ApplyImmediate failed: nested command application detected.");

        IsApplyingFrame = true;
        try
        {
            GamePhase oldPhase = CurrentPhase;
            CurrentPhase = command.Phase;
            try
            {
                sink(command);
            }
            catch
            {
                CurrentPhase = oldPhase;
                throw;
            }
            if (oldPhase != CurrentPhase)
                PhaseApplied?.Invoke(oldPhase, CurrentPhase);
            LastAppliedFrame = command.EffectiveFrame;
        }
        finally
        {
            IsApplyingFrame = false;
        }
    }

#if UNITY_EDITOR
    public static LogicPhaseCommand SubmitForTests(GamePhase phase, Action<LogicPhaseCommand> sink)
    {
        if (!LogicPausedOperationService.CanResolveImmediately
            || LogicPausedOperationService.IsExecuting)
        {
            return ScheduleForNextFrame(phase);
        }

        EnsureReady();
        ValidatePhase(phase);
        return LogicPausedOperationService.Execute(() =>
        {
            LogicPhaseCommand command = RecordCommand(phase, LogicTimeControlService.CurrentFrame, false);
            ApplyImmediate(command, sink);
            return command;
        });
    }
#endif

    public static void ApplyFrame(ulong frameId)
    {
        ApplyFrame(frameId, s_RuntimeSink);
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

            s_Due.Sort(s_CommandComparison);
            for (int i = 0; i < s_Due.Count; i++)
            {
                LogicPhaseCommand command = s_Due[i];
                GamePhase oldPhase = CurrentPhase;
                CurrentPhase = command.Phase;
                try
                {
                    sink(command);
                }
                catch
                {
                    CurrentPhase = oldPhase;
                    throw;
                }
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

    private static void ApplyScheduledPhase(LogicPhaseCommand command)
    {
        PhaseManager.ApplyScheduledPhase(command.Phase);
    }

    private static int CompareCommands(LogicPhaseCommand left, LogicPhaseCommand right)
    {
        return left.Sequence.CompareTo(right.Sequence);
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
