using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("AAAGame.Tests.Editor")]

public enum LogicInputButton
{
    InteractionPrimary = 0,
    InteractionSecondary = 1,
    InteractionTertiary = 2,
    PlayerAttack = 3,
}

public enum RawInputEventKind
{
    WorldMoveChanged = 0,
    ButtonPressed = 1,
    ButtonReleased = 2,
    ButtonPulse = 3,
    ResetGameplayState = 4,
    ButtonHeldSet = 5,
    ButtonHeldCleared = 6,
}

public readonly struct RawInputEvent
{
    internal RawInputEvent(
        ulong sequence,
        double timestamp,
        RawInputEventKind kind,
        LogicInputButton button,
        FixVector2 vector)
    {
        Sequence = sequence;
        Timestamp = timestamp;
        Kind = kind;
        Button = button;
        Vector = vector;
    }

    public ulong Sequence { get; }
    public double Timestamp { get; }
    public RawInputEventKind Kind { get; }
    public LogicInputButton Button { get; }
    public FixVector2 Vector { get; }
}

public sealed class LogicInputFrame
{
    private static readonly ReadOnlyCollection<RawInputEvent> s_NoEvents =
        Array.AsReadOnly(Array.Empty<RawInputEvent>());
    private static readonly int[] s_NoPressCounts = new int[LogicInputTimeline.ButtonCount];

    public static readonly LogicInputFrame Empty = new LogicInputFrame(
        0,
        0,
        FixVector2.Zero,
        0,
        0,
        0,
        s_NoPressCounts,
        s_NoEvents,
        0,
        0,
        0);

    internal LogicInputFrame(
        ulong frameId,
        uint playerId,
        FixVector2 worldMove,
        ulong heldBits,
        ulong pressedBits,
        ulong releasedBits,
        int[] pressCounts,
        IReadOnlyList<RawInputEvent> events,
        ulong firstSequence,
        ulong lastSequence,
        uint checksum,
        bool isReusable = false)
    {
        FrameId = frameId;
        PlayerId = playerId;
        WorldMove = worldMove;
        HeldBits = heldBits;
        PressedBits = pressedBits;
        ReleasedBits = releasedBits;
        m_PressCounts = pressCounts;
        Events = events;
        FirstSequence = firstSequence;
        LastSequence = lastSequence;
        Checksum = checksum;
        m_IsReusable = isReusable;
    }

    private int[] m_PressCounts;
    private readonly bool m_IsReusable;

    public ulong FrameId { get; private set; }
    public uint PlayerId { get; private set; }
    public FixVector2 WorldMove { get; private set; }
    public ulong HeldBits { get; private set; }
    public ulong PressedBits { get; private set; }
    public ulong ReleasedBits { get; private set; }
    public IReadOnlyList<RawInputEvent> Events { get; private set; }
    public ulong FirstSequence { get; private set; }
    public ulong LastSequence { get; private set; }
    public uint Checksum { get; private set; }

    public bool IsHeld(LogicInputButton button)
    {
        return (HeldBits & LogicInputTimeline.GetButtonBit(button)) != 0;
    }

    public bool WasPressed(LogicInputButton button)
    {
        return (PressedBits & LogicInputTimeline.GetButtonBit(button)) != 0;
    }

    public bool WasReleased(LogicInputButton button)
    {
        return (ReleasedBits & LogicInputTimeline.GetButtonBit(button)) != 0;
    }

    public int GetPressCount(LogicInputButton button)
    {
        return m_PressCounts[LogicInputTimeline.ValidateButton(button)];
    }

    internal void ResetReusable(
        ulong frameId,
        uint playerId,
        FixVector2 worldMove,
        ulong heldBits,
        ulong pressedBits,
        ulong releasedBits,
        int[] pressCounts,
        IReadOnlyList<RawInputEvent> events,
        ulong firstSequence,
        ulong lastSequence,
        uint checksum)
    {
        if (!m_IsReusable)
            throw new InvalidOperationException("LogicInputFrame.ResetReusable failed: frame is immutable.");

        FrameId = frameId;
        PlayerId = playerId;
        WorldMove = worldMove;
        HeldBits = heldBits;
        PressedBits = pressedBits;
        ReleasedBits = releasedBits;
        m_PressCounts = pressCounts;
        Events = events;
        FirstSequence = firstSequence;
        LastSequence = lastSequence;
        Checksum = checksum;
    }

    internal LogicInputFrame Freeze()
    {
        if (!m_IsReusable)
            return this;

        int[] pressCounts = (int[])m_PressCounts.Clone();
        var events = new RawInputEvent[Events.Count];
        for (int i = 0; i < events.Length; i++)
            events[i] = Events[i];
        return new LogicInputFrame(
            FrameId,
            PlayerId,
            WorldMove,
            HeldBits,
            PressedBits,
            ReleasedBits,
            pressCounts,
            Array.AsReadOnly(events),
            FirstSequence,
            LastSequence,
            Checksum);
    }
}

public sealed class LogicInputTimeline
{
    public const int ButtonCount = (int)LogicInputButton.PlayerAttack + 1;

    private const double TimestampBoundaryEpsilonSeconds = 1e-8d;
    private static readonly RawInputEvent[] s_NoEventArray = Array.Empty<RawInputEvent>();
    private static readonly ReadOnlyCollection<RawInputEvent> s_NoEvents = Array.AsReadOnly(s_NoEventArray);
    private static readonly int[] s_NoPressCounts = new int[ButtonCount];

    private readonly uint m_PlayerId;
    private readonly List<RawInputEvent> m_PendingEvents = new List<RawInputEvent>();
    private readonly int[] m_RuntimePressCounts = new int[ButtonCount];
    private readonly List<RawInputEvent> m_RuntimeEvents = new List<RawInputEvent>();
    private readonly ReadOnlyCollection<RawInputEvent> m_ReadOnlyRuntimeEvents;
    private readonly LogicInputFrame m_RuntimeFrame;
    private ulong m_NextSequence;
    private ulong m_LastSealedFrame;
    private double m_LastCutoff;
    private FixVector2 m_WorldMove;
    private ulong m_HeldBits;
    private bool m_IsStarted;

    public LogicInputTimeline(uint playerId = 0)
    {
        m_PlayerId = playerId;
        m_ReadOnlyRuntimeEvents = m_RuntimeEvents.AsReadOnly();
        m_RuntimeFrame = new LogicInputFrame(
            0,
            playerId,
            FixVector2.Zero,
            0,
            0,
            0,
            m_RuntimePressCounts,
            m_ReadOnlyRuntimeEvents,
            0,
            0,
            0,
            true);
        CurrentFrame = LogicInputFrame.Empty;
    }

    public bool IsStarted => m_IsStarted;
    public LogicInputFrame CurrentFrame { get; private set; }
    public int PendingEventCount => m_PendingEvents.Count;
    public int RetainedSealedFrameCount => CurrentFrame.FrameId == 0 ? 0 : 1;
    public ulong LateEventCount { get; private set; }
    public RawInputEventKind LastLateEventKind { get; private set; }
    public double LastLateEventTimestamp { get; private set; }
    public double LastLateEventPreviousCutoff { get; private set; }

    public void Begin(
        double startRealtime,
        FixVector2 initialWorldMove,
        ulong initialHeldBits)
    {
        ValidateTimestamp(startRealtime, nameof(startRealtime));
        ValidateHeldBits(initialHeldBits);

        m_PendingEvents.Clear();
        m_NextSequence = 0;
        m_LastSealedFrame = 0;
        m_LastCutoff = startRealtime;
        m_WorldMove = initialWorldMove;
        m_HeldBits = initialHeldBits;
        LateEventCount = 0;
        LastLateEventKind = default;
        LastLateEventTimestamp = 0d;
        LastLateEventPreviousCutoff = 0d;
        CurrentFrame = LogicInputFrame.Empty;
        m_IsStarted = true;
    }

    public void Clear()
    {
        m_PendingEvents.Clear();
        m_NextSequence = 0;
        m_LastSealedFrame = 0;
        m_LastCutoff = 0d;
        m_WorldMove = FixVector2.Zero;
        m_HeldBits = 0;
        LateEventCount = 0;
        LastLateEventKind = default;
        LastLateEventTimestamp = 0d;
        LastLateEventPreviousCutoff = 0d;
        CurrentFrame = LogicInputFrame.Empty;
        m_IsStarted = false;
    }

    public void EnqueueWorldMove(double timestamp, FixVector2 worldMove)
    {
        Enqueue(timestamp, RawInputEventKind.WorldMoveChanged, default, worldMove);
    }

#if UNITY_EDITOR
    public void EnqueueEditorWorldMoveForNextFrame(FixVector2 worldMove)
    {
        if (!m_IsStarted)
            throw new InvalidOperationException("LogicInputTimeline cannot inject editor movement before the timeline starts.");

        Enqueue(
            m_LastCutoff + TimestampBoundaryEpsilonSeconds * 2d,
            RawInputEventKind.WorldMoveChanged,
            default,
            worldMove);
    }
#endif

    public void EnqueueButtonPressed(double timestamp, LogicInputButton button)
    {
        ValidateButton(button);
        Enqueue(timestamp, RawInputEventKind.ButtonPressed, button, FixVector2.Zero);
    }

    public void EnqueueButtonReleased(double timestamp, LogicInputButton button)
    {
        ValidateButton(button);
        Enqueue(timestamp, RawInputEventKind.ButtonReleased, button, FixVector2.Zero);
    }

    public void EnqueueButtonPulse(double timestamp, LogicInputButton button)
    {
        ValidateButton(button);
        Enqueue(timestamp, RawInputEventKind.ButtonPulse, button, FixVector2.Zero);
    }

    public void EnqueueButtonHeldState(double timestamp, LogicInputButton button, bool isHeld)
    {
        ValidateButton(button);
        Enqueue(
            timestamp,
            isHeld ? RawInputEventKind.ButtonHeldSet : RawInputEventKind.ButtonHeldCleared,
            button,
            FixVector2.Zero);
    }

    public void EnqueueResetGameplayState(double timestamp)
    {
        Enqueue(timestamp, RawInputEventKind.ResetGameplayState, default, FixVector2.Zero);
    }

    public LogicInputFrame Seal(ulong frameId, double cutoffRealtime)
    {
        return SealCore(frameId, cutoffRealtime, false);
    }

    internal LogicInputFrame SealReusable(ulong frameId, double cutoffRealtime)
    {
        return SealCore(frameId, cutoffRealtime, true);
    }

    private LogicInputFrame SealCore(ulong frameId, double cutoffRealtime, bool reuseRuntimeFrame)
    {
        if (!m_IsStarted)
            throw new InvalidOperationException("LogicInputTimeline.Seal failed: timeline is not started.");
        if (frameId != m_LastSealedFrame + 1)
            throw new InvalidOperationException(
                $"LogicInputTimeline.Seal failed: non-contiguous frame. expected={m_LastSealedFrame + 1}, actual={frameId}.");

        ValidateTimestamp(cutoffRealtime, nameof(cutoffRealtime));
        if (cutoffRealtime < m_LastCutoff)
            throw new InvalidOperationException(
                $"LogicInputTimeline.Seal failed: cutoff moved backwards. previous={m_LastCutoff:R}, current={cutoffRealtime:R}.");

        int eventCount = FindConsumableEventCount(cutoffRealtime);
        RawInputEvent[] immutableEvents = reuseRuntimeFrame || eventCount == 0
            ? s_NoEventArray
            : new RawInputEvent[eventCount];
        int[] pressCounts = reuseRuntimeFrame
            ? m_RuntimePressCounts
            : eventCount == 0 ? s_NoPressCounts : new int[ButtonCount];
        if (reuseRuntimeFrame)
        {
            Array.Clear(m_RuntimePressCounts, 0, m_RuntimePressCounts.Length);
            m_RuntimeEvents.Clear();
        }
        ulong pressedBits = 0;
        ulong releasedBits = 0;

        for (int i = 0; i < eventCount; i++)
        {
            RawInputEvent inputEvent = m_PendingEvents[i];
            if (reuseRuntimeFrame)
                m_RuntimeEvents.Add(inputEvent);
            else
                immutableEvents[i] = inputEvent;

            switch (inputEvent.Kind)
            {
                case RawInputEventKind.WorldMoveChanged:
                    m_WorldMove = inputEvent.Vector;
                    break;
                case RawInputEventKind.ButtonPressed:
                    ApplyPress(inputEvent.Button, pressCounts, ref pressedBits);
                    m_HeldBits |= GetButtonBit(inputEvent.Button);
                    break;
                case RawInputEventKind.ButtonReleased:
                    releasedBits |= GetButtonBit(inputEvent.Button);
                    m_HeldBits &= ~GetButtonBit(inputEvent.Button);
                    break;
                case RawInputEventKind.ButtonPulse:
                    ApplyPress(inputEvent.Button, pressCounts, ref pressedBits);
                    break;
                case RawInputEventKind.ResetGameplayState:
                    releasedBits |= m_HeldBits;
                    m_HeldBits = 0;
                    m_WorldMove = FixVector2.Zero;
                    break;
                case RawInputEventKind.ButtonHeldSet:
                    m_HeldBits |= GetButtonBit(inputEvent.Button);
                    break;
                case RawInputEventKind.ButtonHeldCleared:
                    m_HeldBits &= ~GetButtonBit(inputEvent.Button);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"LogicInputTimeline.Seal failed: unsupported event kind {inputEvent.Kind}.");
            }
        }

        ulong firstSequence = eventCount > 0 ? m_PendingEvents[0].Sequence : 0;
        ulong lastSequence = eventCount > 0 ? m_PendingEvents[eventCount - 1].Sequence : 0;
        if (eventCount > 0)
            m_PendingEvents.RemoveRange(0, eventCount);

        IReadOnlyList<RawInputEvent> readonlyEvents = reuseRuntimeFrame
            ? m_ReadOnlyRuntimeEvents
            : eventCount == 0 ? s_NoEvents : Array.AsReadOnly(immutableEvents);
        uint checksum = ComputeChecksum(
            frameId,
            m_PlayerId,
            m_WorldMove,
            m_HeldBits,
            pressedBits,
            releasedBits,
            pressCounts,
            readonlyEvents);

        LogicInputFrame frame;
        if (reuseRuntimeFrame)
        {
            m_RuntimeFrame.ResetReusable(
                frameId,
                m_PlayerId,
                m_WorldMove,
                m_HeldBits,
                pressedBits,
                releasedBits,
                pressCounts,
                readonlyEvents,
                firstSequence,
                lastSequence,
                checksum);
            frame = m_RuntimeFrame;
        }
        else
        {
            frame = new LogicInputFrame(
                frameId,
                m_PlayerId,
                m_WorldMove,
                m_HeldBits,
                pressedBits,
                releasedBits,
                pressCounts,
                readonlyEvents,
                firstSequence,
                lastSequence,
                checksum);
        }

        CurrentFrame = frame;
        m_LastSealedFrame = frameId;
        m_LastCutoff = cutoffRealtime;
        return frame;
    }

    public static ulong GetButtonBit(LogicInputButton button)
    {
        return 1UL << ValidateButton(button);
    }

    internal static int ValidateButton(LogicInputButton button)
    {
        int index = (int)button;
        if (index < 0 || index >= ButtonCount)
            throw new ArgumentOutOfRangeException(nameof(button), button, "Invalid logic input button.");
        return index;
    }

    private void Enqueue(
        double timestamp,
        RawInputEventKind kind,
        LogicInputButton button,
        FixVector2 vector)
    {
        ValidateTimestamp(timestamp, nameof(timestamp));
        if (m_IsStarted && timestamp <= m_LastCutoff + TimestampBoundaryEpsilonSeconds)
        {
            LateEventCount = checked(LateEventCount + 1);
            LastLateEventKind = kind;
            LastLateEventTimestamp = timestamp;
            LastLateEventPreviousCutoff = m_LastCutoff;
        }

        var inputEvent = new RawInputEvent(
            checked(++m_NextSequence),
            timestamp,
            kind,
            button,
            vector);

        int insertIndex = m_PendingEvents.Count;
        while (insertIndex > 0 && Compare(inputEvent, m_PendingEvents[insertIndex - 1]) < 0)
            insertIndex--;
        m_PendingEvents.Insert(insertIndex, inputEvent);
    }

    private int FindConsumableEventCount(double cutoffRealtime)
    {
        int count = 0;
        while (count < m_PendingEvents.Count
               && m_PendingEvents[count].Timestamp <= cutoffRealtime + TimestampBoundaryEpsilonSeconds)
            count++;
        return count;
    }

    private static int Compare(RawInputEvent left, RawInputEvent right)
    {
        int timestampOrder = left.Timestamp.CompareTo(right.Timestamp);
        return timestampOrder != 0 ? timestampOrder : left.Sequence.CompareTo(right.Sequence);
    }

    private static void ApplyPress(LogicInputButton button, int[] pressCounts, ref ulong pressedBits)
    {
        int buttonIndex = ValidateButton(button);
        pressCounts[buttonIndex] = checked(pressCounts[buttonIndex] + 1);
        pressedBits |= GetButtonBit(button);
    }

    private static void ValidateTimestamp(double timestamp, string parameterName)
    {
        if (double.IsNaN(timestamp) || double.IsInfinity(timestamp) || timestamp < 0d)
            throw new ArgumentOutOfRangeException(parameterName, timestamp, "Input timestamp must be finite and non-negative.");
    }

    private static void ValidateHeldBits(ulong heldBits)
    {
        ulong validMask = (1UL << ButtonCount) - 1UL;
        if ((heldBits & ~validMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(heldBits), heldBits, "Held bits contain an unknown button.");
    }

    private static uint ComputeChecksum(
        ulong frameId,
        uint playerId,
        FixVector2 worldMove,
        ulong heldBits,
        ulong pressedBits,
        ulong releasedBits,
        int[] pressCounts,
        IReadOnlyList<RawInputEvent> events)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime = 16777619u;
        uint hash = offsetBasis;

        AddHash(ref hash, frameId, prime);
        AddHash(ref hash, playerId, prime);
        AddHash(ref hash, unchecked((ulong)worldMove.x.RawValue), prime);
        AddHash(ref hash, unchecked((ulong)worldMove.y.RawValue), prime);
        AddHash(ref hash, heldBits, prime);
        AddHash(ref hash, pressedBits, prime);
        AddHash(ref hash, releasedBits, prime);

        for (int i = 0; i < pressCounts.Length; i++)
            AddHash(ref hash, unchecked((ulong)pressCounts[i]), prime);
        for (int i = 0; i < events.Count; i++)
        {
            RawInputEvent inputEvent = events[i];
            AddHash(ref hash, inputEvent.Sequence, prime);
            AddHash(ref hash, unchecked((ulong)inputEvent.Kind), prime);
            AddHash(ref hash, unchecked((ulong)inputEvent.Button), prime);
            AddHash(ref hash, unchecked((ulong)inputEvent.Vector.x.RawValue), prime);
            AddHash(ref hash, unchecked((ulong)inputEvent.Vector.y.RawValue), prime);
        }

        return hash;
    }

    private static void AddHash(ref uint hash, ulong value, uint prime)
    {
        for (int i = 0; i < sizeof(ulong); i++)
        {
            hash ^= (byte)(value >> (i * 8));
            hash *= prime;
        }
    }
}
