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
    private static bool s_IsExecutingFrame;
    private static ulong s_ExecutingFrame;

    public static bool IsActive { get; private set; }
    public static bool IsTimelineRunning { get; private set; }
    public static bool IsTicking { get; private set; }
    public static bool IsExecutingFrame => s_IsExecutingFrame || IsTicking;
    public static ulong CurrentFrame { get; private set; }
    public static Fix64 FixedDeltaTime => s_FixedDeltaTime;
    public static Fix64 ElapsedTime { get; private set; }
    public static double Interpolation { get; private set; }
    public static int LastRenderFrameTickCount { get; private set; }
    public static double BacklogSeconds { get; private set; }
    public static double DeferredRealtimeSeconds { get; private set; }
    public static int ListenerCount => s_Listeners.Count;

    public static event Action Began;
    public static event Action Ending;
    public static event Action Ended;

    public static void Register(ILogicFrameUpdate listener)
    {
        if (listener == null)
            throw new ArgumentNullException(nameof(listener));
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.Register failed: runtime is not active.");
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
        DeferredRealtimeSeconds = 0d;
        s_IsExecutingFrame = false;
        s_ExecutingFrame = 0;
        IsActive = true;
        IsTimelineRunning = false;
        Began?.Invoke();
        Log.Info("[LogicFrame] Runtime begin. rate={0}, fixedDelta={1}, physicsModeBefore={2}.", FrameRate, (double)s_FixedDeltaTime, s_PreviousPhysicsSimulationMode);
    }

    public static void End()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.End failed: runtime is not active.");
        if (IsExecutingFrame)
            throw new InvalidOperationException("LogicFrameRuntime.End failed: a logic frame is running.");

        Ending?.Invoke();
        Physics.simulationMode = s_PreviousPhysicsSimulationMode;
        IsActive = false;
        IsTimelineRunning = false;
        CurrentFrame = 0;
        ElapsedTime = Fix64.Zero;
        Interpolation = 0d;
        LastRenderFrameTickCount = 0;
        BacklogSeconds = 0d;
        DeferredRealtimeSeconds = 0d;
        s_IsExecutingFrame = false;
        s_ExecutingFrame = 0;
        Ended?.Invoke();
        Log.Info("[LogicFrame] Runtime end. physicsModeRestored={0}.", s_PreviousPhysicsSimulationMode);
    }

    public static void BeginFrameExecution(ulong frame)
    {
        if (!IsActive || !IsTimelineRunning)
            throw new InvalidOperationException("LogicFrameRuntime.BeginFrameExecution requires an active timeline.");
        if (IsExecutingFrame)
            throw new InvalidOperationException("LogicFrameRuntime.BeginFrameExecution failed: nested logic frame detected.");
        if (frame != CurrentFrame + 1)
        {
            throw new InvalidOperationException(
                $"LogicFrameRuntime.BeginFrameExecution failed: non-contiguous frame. expected={CurrentFrame + 1}, actual={frame}.");
        }

        s_ExecutingFrame = frame;
        s_IsExecutingFrame = true;
    }

    public static void EndFrameExecution(ulong frame)
    {
        if (!s_IsExecutingFrame)
            throw new InvalidOperationException("LogicFrameRuntime.EndFrameExecution failed: no logic frame is executing.");
        if (IsTicking)
            throw new InvalidOperationException("LogicFrameRuntime.EndFrameExecution failed: listener tick is still running.");
        if (frame != s_ExecutingFrame)
        {
            throw new InvalidOperationException(
                $"LogicFrameRuntime.EndFrameExecution failed: frame mismatch. expected={s_ExecutingFrame}, actual={frame}.");
        }

        s_ExecutingFrame = 0;
        s_IsExecutingFrame = false;
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
        if (s_IsExecutingFrame && frame != s_ExecutingFrame)
        {
            throw new InvalidOperationException(
                $"LogicFrameRuntime.Tick failed: execution frame mismatch. expected={s_ExecutingFrame}, actual={frame}.");
        }

        CurrentFrame = frame;
        ElapsedTime = s_FixedDeltaTime * (Fix64)(long)frame;
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long setupStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        s_TickSnapshot.Clear();
        s_TickSnapshot.AddRange(s_Listeners);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.LogicFrameListenerSnapshot,
                System.Diagnostics.Stopwatch.GetTimestamp() - setupStartTicks);
        }

        IsTicking = true;
        try
        {
            long callbacksStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            long listenerTicks = 0L;
            for (int i = 0; i < s_TickSnapshot.Count; i++)
            {
                ListenerEntry entry = s_TickSnapshot[i];
                if (!s_ListenerLookup.ContainsKey(entry.Listener))
                    continue;

                if (!profile)
                {
                    entry.Listener.OnLogicFrameUpdate(s_FixedDeltaTime);
                    continue;
                }

                long listenerStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    entry.Listener.OnLogicFrameUpdate(s_FixedDeltaTime);
                }
                finally
                {
                    long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - listenerStartTicks;
                    listenerTicks += elapsedTicks;
                    MainThreadFrameProfiler.RecordLogicFrameListener(
                        entry.Listener.GetType(),
                        elapsedTicks);
                }
            }
            if (profile)
            {
                long callbacksElapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - callbacksStartTicks;
                long unattributedTicks = callbacksElapsedTicks - listenerTicks;
                if (unattributedTicks < 0L)
                {
                    throw new InvalidOperationException(
                        $"LogicFrameRuntime.Tick listener timing is inconsistent. callbacks={callbacksElapsedTicks}, listeners={listenerTicks}.");
                }
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameListenerAttributed,
                    listenerTicks);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameListenerCallbacks,
                    callbacksElapsedTicks);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameListenerUnattributed,
                    unattributedTicks);
            }

        }
        finally
        {
            IsTicking = false;
            s_TickSnapshot.Clear();
        }
    }

    public static void SyncPresentationPhysics()
    {
        if (!IsActive || !IsTimelineRunning)
            throw new InvalidOperationException("LogicFrameRuntime.SyncPresentationPhysics requires an active timeline.");
        if (IsExecutingFrame)
            throw new InvalidOperationException("LogicFrameRuntime.SyncPresentationPhysics cannot run during a logic frame.");
#if UNITY_EDITOR
        if (EditorLogicRuntimeStressGate.SuppressPhysicsSimulation)
            return;
#endif

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long physicsSyncStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        Physics.SyncTransforms();
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.RenderFramePhysicsSync,
                System.Diagnostics.Stopwatch.GetTimestamp() - physicsSyncStartTicks);
        }
    }

    public static void ResetTimeline()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.ResetTimeline failed: runtime is not active.");
        if (IsExecutingFrame)
            throw new InvalidOperationException("LogicFrameRuntime.ResetTimeline failed: a logic frame is running.");

        CurrentFrame = 0;
        ElapsedTime = Fix64.Zero;
        Interpolation = 0d;
        LastRenderFrameTickCount = 0;
        BacklogSeconds = 0d;
        DeferredRealtimeSeconds = 0d;
        IsTimelineRunning = false;
    }

    public static void StartTimeline()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.StartTimeline failed: runtime is not active.");
        if (IsExecutingFrame)
            throw new InvalidOperationException("LogicFrameRuntime.StartTimeline failed: a logic frame is running.");
        if (IsTimelineRunning)
            throw new InvalidOperationException("LogicFrameRuntime.StartTimeline failed: timeline is already running.");

        IsTimelineRunning = true;
    }

    public static void CompleteRenderFrame(
        int tickCount,
        double backlogSeconds,
        double deferredRealtimeSeconds,
        double interpolation)
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicFrameRuntime.CompleteRenderFrame failed: runtime is not active.");
        if (IsExecutingFrame)
            throw new InvalidOperationException("LogicFrameRuntime.CompleteRenderFrame cannot run during a logic frame.");
        if (tickCount < 0)
            throw new ArgumentOutOfRangeException(nameof(tickCount));
        if (backlogSeconds < 0d || double.IsNaN(backlogSeconds) || double.IsInfinity(backlogSeconds))
            throw new ArgumentOutOfRangeException(nameof(backlogSeconds));
        if (deferredRealtimeSeconds < 0d
            || double.IsNaN(deferredRealtimeSeconds)
            || double.IsInfinity(deferredRealtimeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(deferredRealtimeSeconds));
        }
        if (interpolation < 0d || interpolation >= 1d + 1e-8d || double.IsNaN(interpolation) || double.IsInfinity(interpolation))
            throw new ArgumentOutOfRangeException(nameof(interpolation));

        LastRenderFrameTickCount = tickCount;
        BacklogSeconds = backlogSeconds;
        DeferredRealtimeSeconds = deferredRealtimeSeconds;
        Interpolation = Math.Min(interpolation, 1d);
    }
}
