using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public enum LogicInputButton
{
    InteractionPrimary = 0,
    InteractionSecondary = 1,
    InteractionTertiary = 2,
    OpenTechTree = 3,
    PlayerAttack = 4,
    Skill1 = 5,
    Skill2 = 6,
    Skill3 = 7,
    Skill4 = 8,
    Skill5 = 9,
    SkillConfirm = 10,
    Build1 = 11,
    Build2 = 12,
    Build3 = 13,
}

public enum RawInputEventKind
{
    WorldMoveChanged = 0,
    ButtonPressed = 1,
    ButtonReleased = 2,
    ButtonPulse = 3,
    SelectScreenPositionChanged = 4,
    ResetGameplayState = 5,
    ButtonHeldSet = 6,
    ButtonHeldCleared = 7,
    SelectWorldPositionChanged = 8,
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
        FixVector2.Zero,
        false,
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
        FixVector2 selectScreenPosition,
        bool hasSelectWorldPosition,
        FixVector2 selectWorldPosition,
        ulong heldBits,
        ulong pressedBits,
        ulong releasedBits,
        int[] pressCounts,
        ReadOnlyCollection<RawInputEvent> events,
        ulong firstSequence,
        ulong lastSequence,
        uint checksum)
    {
        FrameId = frameId;
        PlayerId = playerId;
        WorldMove = worldMove;
        SelectScreenPosition = selectScreenPosition;
        HasSelectWorldPosition = hasSelectWorldPosition;
        SelectWorldPosition = selectWorldPosition;
        HeldBits = heldBits;
        PressedBits = pressedBits;
        ReleasedBits = releasedBits;
        m_PressCounts = pressCounts;
        Events = events;
        FirstSequence = firstSequence;
        LastSequence = lastSequence;
        Checksum = checksum;
    }

    private readonly int[] m_PressCounts;

    public ulong FrameId { get; }
    public uint PlayerId { get; }
    public FixVector2 WorldMove { get; }
    public FixVector2 SelectScreenPosition { get; }
    public bool HasSelectWorldPosition { get; }
    public FixVector2 SelectWorldPosition { get; }
    public ulong HeldBits { get; }
    public ulong PressedBits { get; }
    public ulong ReleasedBits { get; }
    public IReadOnlyList<RawInputEvent> Events { get; }
    public ulong FirstSequence { get; }
    public ulong LastSequence { get; }
    public uint Checksum { get; }

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
}

public sealed class LogicInputTimeline
{
    public const int ButtonCount = (int)LogicInputButton.Build3 + 1;

    private readonly uint m_PlayerId;
    private readonly List<RawInputEvent> m_PendingEvents = new List<RawInputEvent>();
    private readonly Dictionary<ulong, LogicInputFrame> m_SealedFrames =
        new Dictionary<ulong, LogicInputFrame>();

    private ulong m_NextSequence;
    private ulong m_LastSealedFrame;
    private double m_LastCutoff;
    private FixVector2 m_WorldMove;
    private FixVector2 m_SelectScreenPosition;
    private bool m_HasSelectWorldPosition;
    private FixVector2 m_SelectWorldPosition;
    private ulong m_HeldBits;
    private bool m_IsStarted;

    public LogicInputTimeline(uint playerId = 0)
    {
        m_PlayerId = playerId;
        CurrentFrame = LogicInputFrame.Empty;
    }

    public bool IsStarted => m_IsStarted;
    public LogicInputFrame CurrentFrame { get; private set; }
    public int PendingEventCount => m_PendingEvents.Count;
    public ulong LateEventCount { get; private set; }

    public void Begin(
        double startRealtime,
        FixVector2 initialWorldMove,
        ulong initialHeldBits,
        FixVector2 initialSelectScreenPosition,
        bool hasInitialSelectWorldPosition,
        FixVector2 initialSelectWorldPosition)
    {
        ValidateTimestamp(startRealtime, nameof(startRealtime));
        ValidateHeldBits(initialHeldBits);

        m_PendingEvents.Clear();
        m_SealedFrames.Clear();
        m_NextSequence = 0;
        m_LastSealedFrame = 0;
        m_LastCutoff = startRealtime;
        m_WorldMove = initialWorldMove;
        m_SelectScreenPosition = initialSelectScreenPosition;
        m_HasSelectWorldPosition = hasInitialSelectWorldPosition;
        m_SelectWorldPosition = initialSelectWorldPosition;
        m_HeldBits = initialHeldBits;
        LateEventCount = 0;
        CurrentFrame = LogicInputFrame.Empty;
        m_IsStarted = true;
    }

    public void Clear()
    {
        m_PendingEvents.Clear();
        m_SealedFrames.Clear();
        m_NextSequence = 0;
        m_LastSealedFrame = 0;
        m_LastCutoff = 0d;
        m_WorldMove = FixVector2.Zero;
        m_SelectScreenPosition = FixVector2.Zero;
        m_HasSelectWorldPosition = false;
        m_SelectWorldPosition = FixVector2.Zero;
        m_HeldBits = 0;
        LateEventCount = 0;
        CurrentFrame = LogicInputFrame.Empty;
        m_IsStarted = false;
    }

    public void EnqueueWorldMove(double timestamp, FixVector2 worldMove)
    {
        Enqueue(timestamp, RawInputEventKind.WorldMoveChanged, default, worldMove);
    }

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

    public void EnqueueSelectScreenPosition(double timestamp, FixVector2 screenPosition)
    {
        Enqueue(timestamp, RawInputEventKind.SelectScreenPositionChanged, default, screenPosition);
    }

    public void EnqueueSelectWorldPosition(double timestamp, FixVector2 worldPosition)
    {
        Enqueue(timestamp, RawInputEventKind.SelectWorldPositionChanged, default, worldPosition);
    }

    public void EnqueueResetGameplayState(double timestamp)
    {
        Enqueue(timestamp, RawInputEventKind.ResetGameplayState, default, FixVector2.Zero);
    }

    public LogicInputFrame Seal(ulong frameId, double cutoffRealtime)
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
        var frameEvents = new RawInputEvent[eventCount];
        var pressCounts = new int[ButtonCount];
        ulong pressedBits = 0;
        ulong releasedBits = 0;

        for (int i = 0; i < eventCount; i++)
        {
            RawInputEvent inputEvent = m_PendingEvents[i];
            frameEvents[i] = inputEvent;

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
                case RawInputEventKind.SelectScreenPositionChanged:
                    m_SelectScreenPosition = inputEvent.Vector;
                    break;
                case RawInputEventKind.SelectWorldPositionChanged:
                    m_HasSelectWorldPosition = true;
                    m_SelectWorldPosition = inputEvent.Vector;
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

        if (eventCount > 0)
            m_PendingEvents.RemoveRange(0, eventCount);

        ulong firstSequence = eventCount > 0 ? frameEvents[0].Sequence : 0;
        ulong lastSequence = eventCount > 0 ? frameEvents[eventCount - 1].Sequence : 0;
        var readonlyEvents = Array.AsReadOnly(frameEvents);
        uint checksum = ComputeChecksum(
            frameId,
            m_PlayerId,
            m_WorldMove,
            m_SelectScreenPosition,
            m_HasSelectWorldPosition,
            m_SelectWorldPosition,
            m_HeldBits,
            pressedBits,
            releasedBits,
            pressCounts,
            frameEvents);

        var frame = new LogicInputFrame(
            frameId,
            m_PlayerId,
            m_WorldMove,
            m_SelectScreenPosition,
            m_HasSelectWorldPosition,
            m_SelectWorldPosition,
            m_HeldBits,
            pressedBits,
            releasedBits,
            pressCounts,
            readonlyEvents,
            firstSequence,
            lastSequence,
            checksum);

        m_SealedFrames.Add(frameId, frame);
        CurrentFrame = frame;
        m_LastSealedFrame = frameId;
        m_LastCutoff = cutoffRealtime;
        return frame;
    }

    public bool TryGetFrame(ulong frameId, out LogicInputFrame frame)
    {
        return m_SealedFrames.TryGetValue(frameId, out frame);
    }

    public LogicInputFrame GetFrame(ulong frameId)
    {
        if (!m_SealedFrames.TryGetValue(frameId, out LogicInputFrame frame))
            throw new KeyNotFoundException($"Logic input frame is not sealed. frame={frameId}.");
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
        if (m_IsStarted && timestamp <= m_LastCutoff)
            LateEventCount = checked(LateEventCount + 1);

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
        while (count < m_PendingEvents.Count && m_PendingEvents[count].Timestamp <= cutoffRealtime)
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
        FixVector2 selectScreenPosition,
        bool hasSelectWorldPosition,
        FixVector2 selectWorldPosition,
        ulong heldBits,
        ulong pressedBits,
        ulong releasedBits,
        int[] pressCounts,
        RawInputEvent[] events)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime = 16777619u;
        uint hash = offsetBasis;

        AddHash(ref hash, frameId, prime);
        AddHash(ref hash, playerId, prime);
        AddHash(ref hash, unchecked((ulong)worldMove.x.RawValue), prime);
        AddHash(ref hash, unchecked((ulong)worldMove.y.RawValue), prime);
        AddHash(ref hash, unchecked((ulong)selectScreenPosition.x.RawValue), prime);
        AddHash(ref hash, unchecked((ulong)selectScreenPosition.y.RawValue), prime);
        AddHash(ref hash, hasSelectWorldPosition ? 1UL : 0UL, prime);
        AddHash(ref hash, unchecked((ulong)selectWorldPosition.x.RawValue), prime);
        AddHash(ref hash, unchecked((ulong)selectWorldPosition.y.RawValue), prime);
        AddHash(ref hash, heldBits, prime);
        AddHash(ref hash, pressedBits, prime);
        AddHash(ref hash, releasedBits, prime);

        for (int i = 0; i < pressCounts.Length; i++)
            AddHash(ref hash, unchecked((ulong)pressCounts[i]), prime);
        for (int i = 0; i < events.Length; i++)
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
