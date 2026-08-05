using System;

public static class LogicInteractionHoldService
{
    public const int HoldThresholdFrames = 38;

    private static readonly int[] s_HeldFrameCounts = new int[3];
    private static readonly bool[] s_LockedUntilRelease = new bool[3];
    private static Func<InputKey, bool> s_CanExecute;
    private static Func<InputKey, bool> s_Execute;

    public static bool IsActive { get; private set; }
    public static ulong LastProcessedFrame { get; private set; }
    public static bool HasConsumer => s_CanExecute != null && s_Execute != null;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicInteractionHoldService.BeginTimeline failed: service is already active.");

        IsActive = true;
        ClearFrameState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        IsActive = false;
        ClearFrameState();
        s_CanExecute = null;
        s_Execute = null;
    }

    public static void RegisterConsumer(Func<InputKey, bool> canExecute, Func<InputKey, bool> execute)
    {
        EnsureActive();
        if (canExecute == null)
            throw new ArgumentNullException(nameof(canExecute));
        if (execute == null)
            throw new ArgumentNullException(nameof(execute));
        if (HasConsumer)
            throw new InvalidOperationException("LogicInteractionHoldService.RegisterConsumer failed: a consumer is already registered.");

        s_CanExecute = canExecute;
        s_Execute = execute;
        ClearHoldState();
    }

    public static void UnregisterConsumer(Func<InputKey, bool> canExecute, Func<InputKey, bool> execute)
    {
        EnsureActive();
        if (s_CanExecute != canExecute || s_Execute != execute)
            throw new InvalidOperationException("LogicInteractionHoldService.UnregisterConsumer failed: consumer identity mismatch.");

        s_CanExecute = null;
        s_Execute = null;
        ClearHoldState();
    }

    public static void ProcessFrame(LogicInputFrame frame)
    {
        EnsureActive();
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        ulong expectedFrame = checked(LastProcessedFrame + 1);
        if (frame.FrameId != expectedFrame)
        {
            throw new InvalidOperationException(
                $"LogicInteractionHoldService.ProcessFrame failed: non-contiguous frame. expected={expectedFrame}, actual={frame.FrameId}.");
        }

        for (int index = 0; index < s_HeldFrameCounts.Length; index++)
            ProcessKey(frame, index);
        LastProcessedFrame = frame.FrameId;
    }

    public static Fix64 GetProgress(InputKey key)
    {
        EnsureActive();
        int index = ValidateKey(key);
        return (Fix64)s_HeldFrameCounts[index] / HoldThresholdFrames;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        ClearFrameState();
    }

    public static void ResetFrameTimeline()
    {
        EnsureActive();
        ClearFrameState();
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        EnsureActive();
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(LastProcessedFrame);
        for (int i = 0; i < s_HeldFrameCounts.Length; i++)
        {
            hasher.Add(s_HeldFrameCounts[i]);
            hasher.Add(s_LockedUntilRelease[i]);
        }
    }

    private static void ProcessKey(LogicInputFrame frame, int index)
    {
        InputKey key = (InputKey)index;
        LogicInputButton button = (LogicInputButton)index;
        if (!frame.IsHeld(button) || frame.WasReleased(button))
        {
            s_HeldFrameCounts[index] = 0;
            s_LockedUntilRelease[index] = false;
            return;
        }
        if (!HasConsumer || !s_CanExecute(key))
        {
            s_HeldFrameCounts[index] = 0;
            s_LockedUntilRelease[index] = false;
            return;
        }
        if (s_LockedUntilRelease[index])
            return;

        s_HeldFrameCounts[index] = Math.Min(HoldThresholdFrames, checked(s_HeldFrameCounts[index] + 1));
        if (s_HeldFrameCounts[index] < HoldThresholdFrames)
            return;

        if (!s_Execute(key))
            throw new InvalidOperationException($"LogicInteractionHoldService failed to execute completed hold for {key}.");
        s_LockedUntilRelease[index] = true;
    }

    private static int ValidateKey(InputKey key)
    {
        int index = (int)key;
        if (index < 0 || index >= s_HeldFrameCounts.Length)
            throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown interaction input key.");
        return index;
    }

    private static void ClearFrameState()
    {
        LastProcessedFrame = 0;
        ClearHoldState();
    }

    private static void ClearHoldState()
    {
        Array.Clear(s_HeldFrameCounts, 0, s_HeldFrameCounts.Length);
        Array.Clear(s_LockedUntilRelease, 0, s_LockedUntilRelease.Length);
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicInteractionHoldService operation failed: service is not active.");
    }
}
