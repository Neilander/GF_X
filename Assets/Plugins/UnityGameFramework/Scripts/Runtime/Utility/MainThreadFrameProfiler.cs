using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

namespace UnityGameFramework.Runtime
{
    public enum MainThreadPerfScope
    {
        GameFrameworkUpdate = 0,
        FlowGroupMove = 1,
        FlowConfig = 2,
        FlowSourceGate = 3,
        Fog3Update = 4,
        Fog3Visibility = 5,
        Fog3OverlayRender = 6,
        Fog3EnemyVisibility = 7,
        InteractionTrigger = 8,
        InteractionCleanup = 9,
        EntityUpdate = 10,
        EntityBase = 11,
        EntityBuff = 12,
        EntityScale = 13,
        EntityAgentPosition = 14,
        EntityBrain = 15,
        EntityTargeting = 16,
        EntityAttack = 17,
        EntityDurationMove = 18,
        EntityMoveComp = 19,
        EntityMoveExecutor = 20,
        EntityAnimator = 21,
        EntityRotation = 22,
        SoldierPostUpdate = 23,
        SoldierMinimap = 24,
        SoldierDebugDraw = 25,
        MoveExecutorConstraint = 26,
        MoveExecutorControllerMove = 27,
        CharacterMoveSteering = 28,
        CharacterMoveIdleRecovery = 29,
        CharacterMoveSetInput = 30,
        CharacterMoveDebugLog = 31,
        CharacterMovePrepare = 32,
        Count = 33
    }

    public static class MainThreadFrameProfiler
    {
        private const int SlowFrameMilliseconds = 30;
        private const int TrackedLogMilliseconds = 4;
        private const int ForceLogFrameMilliseconds = 200;
        private const int ForceTrackedLogMilliseconds = 50;
        private const int MinLogFrameInterval = 30;
        private static readonly long SlowFrameTicks = Stopwatch.Frequency * SlowFrameMilliseconds / 1000;
        private static readonly long TrackedLogTicks = Stopwatch.Frequency * TrackedLogMilliseconds / 1000;
        private static readonly long ForceLogFrameTicks = Stopwatch.Frequency * ForceLogFrameMilliseconds / 1000;
        private static readonly long ForceTrackedLogTicks = Stopwatch.Frequency * ForceTrackedLogMilliseconds / 1000;
        private static readonly long[] ScopeTicks = new long[(int)MainThreadPerfScope.Count];
        private static readonly int[] ScopeCalls = new int[(int)MainThreadPerfScope.Count];
        private static readonly ProfilerMarkerSampler[] MarkerSamplers =
        {
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PlayerLoop"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "EarlyUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "FixedUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PreUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "Update.ScriptRunBehaviourUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Scripts, "BehaviourUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PreLateUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PostLateUpdate"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PlayerSendFrameStarted"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "PlayerSendFrameComplete"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "Camera.Render"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "RenderPipelineManager.DoRenderLoop_Internal"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "RenderLoop.Draw"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "Gfx.PresentFrame"),
            new ProfilerMarkerSampler(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
            new ProfilerMarkerSampler(ProfilerCategory.Gui, "Canvas.BuildBatch"),
            new ProfilerMarkerSampler(ProfilerCategory.Gui, "Canvas.SendWillRenderCanvases"),
            new ProfilerMarkerSampler(ProfilerCategory.Internal, "WaitForTargetFPS"),
            new ProfilerMarkerSampler(ProfilerCategory.Physics, "Physics.Simulate"),
            new ProfilerMarkerSampler(ProfilerCategory.Memory, "GC.Collect"),
        };

        private static bool _initialized;
        private static bool _recordersInitialized;
        private static int _frame;
        private static int _lastLogFrame = -100000;
        private static long _frameStartTicks;

        public static void PulseFrame()
        {
            EnsureFrame();
        }

        public static void Record(MainThreadPerfScope scope, long ticks)
        {
            if (ticks <= 0)
                return;

            EnsureFrame();
            int index = (int)scope;
            if (index < 0 || index >= ScopeTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown main thread perf scope.");

            ScopeTicks[index] += ticks;
            ScopeCalls[index]++;
        }

        private static void EnsureFrame()
        {
            int frame = Time.frameCount;
            long now = Stopwatch.GetTimestamp();
            if (!_initialized)
            {
                _initialized = true;
                _frame = frame;
                _frameStartTicks = now;
                return;
            }

            if (_frame == frame)
                return;

            long frameTicks = now - _frameStartTicks;
            Flush(frameTicks);
            Array.Clear(ScopeTicks, 0, ScopeTicks.Length);
            Array.Clear(ScopeCalls, 0, ScopeCalls.Length);
            _frame = frame;
            _frameStartTicks = now;
        }

        private static void Flush(long frameTicks)
        {
            long trackedTicks = 0;
            for (int i = 0; i <= (int)MainThreadPerfScope.EntityUpdate; i++)
                trackedTicks += ScopeTicks[i];

            EnsureRecorders();

            bool slowFrame = frameTicks >= SlowFrameTicks;
            bool enoughTracked = trackedTicks >= TrackedLogTicks;
            bool forceLog = frameTicks >= ForceLogFrameTicks || trackedTicks >= ForceTrackedLogTicks;
            if (!slowFrame && !enoughTracked)
                return;
            if (!forceLog && _frame - _lastLogFrame < MinLogFrameInterval)
                return;
            _lastLogFrame = _frame;

            double frameMs = TicksToMs(frameTicks);
            double trackedMs = TicksToMs(trackedTicks);
            double untrackedMs = Math.Max(0.0, frameMs - trackedMs);
            UnityEngine.Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[MainPerf] frame={0} frameDt={1:F3}ms tracked={2:F3}ms untracked={3:F3}ms " +
                "gf={4:F3}ms/{5} flow={6:F3}ms/{7} flowConfig={8:F3}ms/{9} flowSource={10:F3}ms/{11} " +
                "fogUpdate={12:F3}ms/{13} fogVisibility={14:F3}ms/{15} fogOverlay={16:F3}ms/{17} fogEnemy={18:F3}ms/{19} " +
                "interactionTrigger={20:F3}ms/{21} interactionCleanup={22:F3}ms/{23} entity={24:F3}ms/{25} " +
                "entityDetail({26}) moveExecDetail({27}) characterMoveDetail({28}) env(targetFps={29},vSync={30},screen={31}x{32},focused={33}) markers({34})",
                _frame,
                frameMs,
                trackedMs,
                untrackedMs,
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.GameFrameworkUpdate]), ScopeCalls[(int)MainThreadPerfScope.GameFrameworkUpdate],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.FlowGroupMove]), ScopeCalls[(int)MainThreadPerfScope.FlowGroupMove],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.FlowConfig]), ScopeCalls[(int)MainThreadPerfScope.FlowConfig],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.FlowSourceGate]), ScopeCalls[(int)MainThreadPerfScope.FlowSourceGate],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3Update]), ScopeCalls[(int)MainThreadPerfScope.Fog3Update],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3Visibility]), ScopeCalls[(int)MainThreadPerfScope.Fog3Visibility],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3OverlayRender]), ScopeCalls[(int)MainThreadPerfScope.Fog3OverlayRender],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.Fog3EnemyVisibility]), ScopeCalls[(int)MainThreadPerfScope.Fog3EnemyVisibility],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.InteractionTrigger]), ScopeCalls[(int)MainThreadPerfScope.InteractionTrigger],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.InteractionCleanup]), ScopeCalls[(int)MainThreadPerfScope.InteractionCleanup],
                TicksToMs(ScopeTicks[(int)MainThreadPerfScope.EntityUpdate]), ScopeCalls[(int)MainThreadPerfScope.EntityUpdate],
                BuildScopeSummary(MainThreadPerfScope.EntityBase, MainThreadPerfScope.SoldierDebugDraw),
                BuildScopeSummary(MainThreadPerfScope.MoveExecutorConstraint, MainThreadPerfScope.MoveExecutorControllerMove),
                BuildScopeSummary(MainThreadPerfScope.CharacterMoveSteering, MainThreadPerfScope.CharacterMovePrepare),
                Application.targetFrameRate,
                QualitySettings.vSyncCount,
                Screen.width,
                Screen.height,
                Application.isFocused,
                BuildMarkerSummary());
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void EnsureRecorders()
        {
            if (_recordersInitialized)
                return;

            _recordersInitialized = true;
            for (int i = 0; i < MarkerSamplers.Length; i++)
                MarkerSamplers[i].Initialize();
        }

        private static string BuildMarkerSummary()
        {
            string result = string.Empty;
            for (int i = 0; i < MarkerSamplers.Length; i++)
            {
                if (!MarkerSamplers[i].TryReadMilliseconds(out double milliseconds))
                    continue;

                if (result.Length > 0)
                    result += ",";
                result += MarkerSamplers[i].Name + "=" + milliseconds.ToString("F3") + "ms";
            }

            return result.Length > 0 ? result : "none";
        }

        private static string BuildScopeSummary(MainThreadPerfScope first, MainThreadPerfScope last)
        {
            string result = string.Empty;
            int firstIndex = (int)first;
            int lastIndex = (int)last;
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                if (ScopeCalls[i] <= 0)
                    continue;

                if (result.Length > 0)
                    result += ",";
                result += ((MainThreadPerfScope)i).ToString() + "=" + TicksToMs(ScopeTicks[i]).ToString("F3") + "ms/" + ScopeCalls[i];
            }

            return result.Length > 0 ? result : "none";
        }

        private struct ProfilerMarkerSampler
        {
            public readonly ProfilerCategory Category;
            public readonly string Name;
            private ProfilerRecorder _recorder;
            private bool _initialized;

            public ProfilerMarkerSampler(ProfilerCategory category, string name)
            {
                Category = category;
                Name = name;
                _recorder = default;
                _initialized = false;
            }

            public void Initialize()
            {
                if (_initialized)
                    return;

                _initialized = true;
                try
                {
                    _recorder = ProfilerRecorder.StartNew(Category, Name, 1);
                }
                catch (Exception)
                {
                    _recorder = default;
                }
            }

            public bool TryReadMilliseconds(out double milliseconds)
            {
                milliseconds = 0.0;
                if (!_recorder.Valid)
                    return false;

                milliseconds = _recorder.LastValue / 1000000.0;
                return true;
            }
        }
    }
}
