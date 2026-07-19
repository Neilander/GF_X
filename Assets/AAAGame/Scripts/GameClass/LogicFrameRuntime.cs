using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public interface ILogicFrameUpdate
{
    int LogicFrameOrder { get; }
    void OnLogicFrameUpdate(Fix64 deltaTime);
}

public interface ILogicFrameStableOrder
{
    long LogicFrameStableKey { get; }
}

public static class LogicFrameRuntime
{
    private sealed class ListenerEntry
    {
        public ILogicFrameUpdate Listener;
        public int Order;
        public bool HasStableKey;
        public long StableKey;
        public long Sequence;
    }

    public const int FrameRate = LogicFrameClock.FrameRate;

    private static readonly Fix64 s_FixedDeltaTime = Fix64.One / (Fix64)FrameRate;
    private static readonly List<ListenerEntry> s_Listeners = new List<ListenerEntry>();
    private static readonly List<ListenerEntry> s_TickSnapshot = new List<ListenerEntry>();
    private static readonly Dictionary<ILogicFrameUpdate, ListenerEntry> s_ListenerLookup = new Dictionary<ILogicFrameUpdate, ListenerEntry>();

    private static SimulationMode s_PreviousPhysicsSimulationMode;
    private static long s_NextSequence;

    public static bool IsActive { get; private set; }
    public static bool IsTimelineRunning { get; private set; }
    public static bool IsTicking { get; private set; }
    public static ulong CurrentFrame { get; private set; }
    public static Fix64 FixedDeltaTime => s_FixedDeltaTime;
    public static Fix64 ElapsedTime { get; private set; }
    public static double Interpolation { get; private set; }
    public static int LastRenderFrameTickCount { get; private set; }
    public static double BacklogSeconds { get; private set; }
    public static int ListenerCount => s_Listeners.Count;

    public static void Register(ILogicFrameUpdate listener)
    {
        if (listener == null)
            throw new ArgumentNullException(nameof(listener));
        if (s_ListenerLookup.ContainsKey(listener))
            throw new InvalidOperationException($"LogicFrameRuntime.Register failed: listener is already registered. type={listener.GetType().FullName}.");

        bool hasStableKey = listener is ILogicFrameStableOrder;
        long stableKey = hasStableKey
            ? ((ILogicFrameStableOrder)listener).LogicFrameStableKey
            : 0;
        var entry = new ListenerEntry
        {
            Listener = listener,
            Order = listener.LogicFrameOrder,
            HasStableKey = hasStableKey,
            StableKey = stableKey,
            Sequence = s_NextSequence++,
        };

        int insertIndex = s_Listeners.Count;
        for (int i = 0; i < s_Listeners.Count; i++)
        {
            ListenerEntry existing = s_Listeners[i];
            int comparison = CompareEntries(entry, existing);
            if (comparison == 0)
            {
                throw new InvalidOperationException(
                    $"LogicFrameRuntime.Register failed: duplicate stable order key. order={entry.Order}, stableKey={entry.StableKey}, " +
                    $"existing={existing.Listener.GetType().FullName}, incoming={listener.GetType().FullName}.");
            }
            if (comparison < 0)
            {
                insertIndex = i;
                break;
            }
        }

        s_Listeners.Insert(insertIndex, entry);
        s_ListenerLookup.Add(listener, entry);
    }

    private static int CompareEntries(ListenerEntry left, ListenerEntry right)
    {
        int orderComparison = left.Order.CompareTo(right.Order);
        if (orderComparison != 0)
            return orderComparison;

        if (left.HasStableKey && right.HasStableKey)
            return left.StableKey.CompareTo(right.StableKey);
        if (left.HasStableKey != right.HasStableKey)
            return left.HasStableKey ? -1 : 1;
        return left.Sequence.CompareTo(right.Sequence);
    }

    public static void Unregister(ILogicFrameUpdate listener)
    {
        if (listener == null)
            throw new ArgumentNullException(nameof(listener));
        if (!s_ListenerLookup.TryGetValue(listener, out ListenerEntry entry))
            throw new InvalidOperationException($"LogicFrameRuntime.Unregister failed: listener is not registered. type={listener.GetType().FullName}.");

        s_ListenerLookup.Remove(listener);
        s_Listeners.Remove(entry);
    }

    public static void Begin()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.Begin failed: runtime is already active.");

        s_PreviousPhysicsSimulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        Physics.SyncTransforms();

        CurrentFrame = 0;
        ElapsedTime = Fix64.Zero;
        Interpolation = 0d;
        LastRenderFrameTickCount = 0;
        BacklogSeconds = 0d;
        IsActive = true;
        IsTimelineRunning = false;
        Log.Info("[LogicFrame] Runtime begin. rate={0}, fixedDelta={1}, physicsModeBefore={2}.", FrameRate, (double)s_FixedDeltaTime, s_PreviousPhysicsSimulationMode);
    }

    public static void End()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.End failed: runtime is not active.");
        if (IsTicking)
            throw new InvalidOperationException("LogicFrameRuntime.End failed: a logic frame is running.");

        Physics.simulationMode = s_PreviousPhysicsSimulationMode;
        IsActive = false;
        IsTimelineRunning = false;
        CurrentFrame = 0;
        ElapsedTime = Fix64.Zero;
        Interpolation = 0d;
        LastRenderFrameTickCount = 0;
        BacklogSeconds = 0d;
        Log.Info("[LogicFrame] Runtime end. physicsModeRestored={0}.", s_PreviousPhysicsSimulationMode);
    }

    public static void Tick(ulong frame)
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.Tick failed: runtime is not active.");
        if (!IsTimelineRunning)
            throw new InvalidOperationException("LogicFrameRuntime.Tick failed: timeline is not running.");
        if (IsTicking)
            throw new InvalidOperationException("LogicFrameRuntime.Tick failed: nested logic tick detected.");
        if (frame != CurrentFrame + 1)
            throw new InvalidOperationException($"LogicFrameRuntime.Tick failed: non-contiguous frame. expected={CurrentFrame + 1}, actual={frame}.");

        CurrentFrame = frame;
        ElapsedTime = s_FixedDeltaTime * (Fix64)(long)frame;
        s_TickSnapshot.Clear();
        s_TickSnapshot.AddRange(s_Listeners);

        IsTicking = true;
        try
        {
            for (int i = 0; i < s_TickSnapshot.Count; i++)
            {
                ListenerEntry entry = s_TickSnapshot[i];
                if (!s_ListenerLookup.ContainsKey(entry.Listener))
                    continue;

                entry.Listener.OnLogicFrameUpdate(s_FixedDeltaTime);
            }

            Physics.SyncTransforms();
            Physics.Simulate((float)LogicFrameClock.FrameDurationSeconds);
        }
        finally
        {
            IsTicking = false;
            s_TickSnapshot.Clear();
        }
    }

    public static void ResetTimeline()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.ResetTimeline failed: runtime is not active.");
        if (IsTicking)
            throw new InvalidOperationException("LogicFrameRuntime.ResetTimeline failed: a logic frame is running.");

        CurrentFrame = 0;
        ElapsedTime = Fix64.Zero;
        Interpolation = 0d;
        LastRenderFrameTickCount = 0;
        BacklogSeconds = 0d;
        IsTimelineRunning = false;
    }

    public static void StartTimeline()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.StartTimeline failed: runtime is not active.");
        if (IsTicking)
            throw new InvalidOperationException("LogicFrameRuntime.StartTimeline failed: a logic frame is running.");
        if (IsTimelineRunning)
            throw new InvalidOperationException("LogicFrameRuntime.StartTimeline failed: timeline is already running.");

        IsTimelineRunning = true;
    }

    public static void CompleteRenderFrame(int tickCount, double backlogSeconds, double interpolation)
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.CompleteRenderFrame failed: runtime is not active.");
        if (tickCount < 0)
            throw new ArgumentOutOfRangeException(nameof(tickCount));
        if (backlogSeconds < 0d || double.IsNaN(backlogSeconds) || double.IsInfinity(backlogSeconds))
            throw new ArgumentOutOfRangeException(nameof(backlogSeconds));
        if (interpolation < 0d || interpolation >= 1d + 1e-8d || double.IsNaN(interpolation) || double.IsInfinity(interpolation))
            throw new ArgumentOutOfRangeException(nameof(interpolation));

        LastRenderFrameTickCount = tickCount;
        BacklogSeconds = backlogSeconds;
        Interpolation = Math.Min(interpolation, 1d);
    }
}
