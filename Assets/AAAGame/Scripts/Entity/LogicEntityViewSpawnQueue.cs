using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class LogicEntityViewSpawnQueue
{
    private const int MaxInFlightRequests = 4;
    private const int MaxDispatchesPerRenderFrame = 4;

    private sealed class PendingView
    {
        public readonly string PrefabName;
        public readonly Const.EntityGroup EntityGroup;
        public readonly EntityParams Params;

        public PendingView(string prefabName, Const.EntityGroup entityGroup, EntityParams entityParams)
        {
            PrefabName = prefabName;
            EntityGroup = entityGroup;
            Params = entityParams;
        }
    }

    private readonly struct PendingHide
    {
        public PendingHide(int viewId, int earliestRenderFrame)
        {
            ViewId = viewId;
            EarliestRenderFrame = earliestRenderFrame;
        }

        public int ViewId { get; }
        public int EarliestRenderFrame { get; }
    }

    private static readonly Queue<PendingView> s_Pending = new Queue<PendingView>();
    private static readonly Dictionary<int, PendingView> s_InFlight = new Dictionary<int, PendingView>();
    private static readonly List<int> s_CancelViewIds = new List<int>();
    private static readonly Queue<PendingHide> s_PendingHides = new Queue<PendingHide>();
    private static readonly HashSet<int> s_PendingHideViewIds = new HashSet<int>();

    private static string s_Failure;
    private static int s_BatchStartRenderFrame;
    private static int s_BatchRequested;
    private static int s_BatchShown;
    private static int s_BatchCanceled;
    private static int s_LastCompletionRenderFrame = -1;
    private static int s_CompletedThisRenderFrame;
    private static int s_MaxCompletedInRenderFrame;

    public static bool IsActive { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int InFlightCount => s_InFlight.Count;
	public static int PendingHideCount => s_PendingHides.Count;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicEntityViewSpawnQueue.BeginTimeline failed: queue is already active.");
        if (GF.Event == null)
            throw new InvalidOperationException("LogicEntityViewSpawnQueue.BeginTimeline failed: GF.Event is null.");

        ClearMetrics();
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        GF.Event.Subscribe(ShowEntityFailureEventArgs.EventId, OnShowEntityFailure);
        IsActive = true;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        CancelAll("timeline end");
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        GF.Event.Unsubscribe(ShowEntityFailureEventArgs.EventId, OnShowEntityFailure);
        IsActive = false;
        ClearMetrics();
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        CancelAll("world transition");
        ClearMetrics();
    }

    public static int EnqueueSoldier(string prefabName, Const.EntityGroup entityGroup, EntityParams entityParams)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(prefabName))
            throw new ArgumentException("Soldier view prefab name is empty.", nameof(prefabName));
        if (entityParams == null)
            throw new ArgumentNullException(nameof(entityParams));
        if (!entityParams.LogicEntityId.IsValid || entityParams.LogicEntityState == null)
            throw new InvalidOperationException("Soldier view request has no configured logic entity.");
        if (entityParams.Id <= 0)
            throw new InvalidOperationException("Soldier view request has an invalid framework entity id.");

        if (s_Pending.Count == 0 && s_InFlight.Count == 0 && s_BatchRequested == 0)
            s_BatchStartRenderFrame = Time.frameCount;

        s_Pending.Enqueue(new PendingView(prefabName, entityGroup, entityParams));
        s_BatchRequested++;
        return entityParams.Id;
    }

	public static void EnqueueHide(int viewId)
	{
		EnsureActive();
		if (viewId <= 0)
			throw new ArgumentOutOfRangeException(nameof(viewId));
		if (!s_PendingHideViewIds.Add(viewId))
			throw new InvalidOperationException($"LogicEntityViewSpawnQueue view {viewId} is already pending hide.");

		s_PendingHides.Enqueue(new PendingHide(viewId, checked(Time.frameCount + 2)));
	}

    public static void UpdateRenderFrame()
    {
        EnsureActive();
        if (s_Failure != null)
            throw new InvalidOperationException(s_Failure);

		DispatchPendingHides();
        CancelDespawnedRequests();

        int dispatched = 0;
        while (s_Pending.Count > 0
               && s_InFlight.Count < MaxInFlightRequests
               && dispatched < MaxDispatchesPerRenderFrame)
        {
            PendingView request = s_Pending.Dequeue();
            if (request.Params.LogicEntityState.IsDespawnCommitted)
            {
                ReferencePool.Release(request.Params);
                s_BatchCanceled++;
                continue;
            }

            int viewId = request.Params.Id;
            s_InFlight.Add(viewId, request);
            try
            {
                long requestStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                int actualViewId = GF.Entity.ShowEntity<SoldierEntity>(
                    request.PrefabName,
                    request.EntityGroup,
                    request.Params);
                UnityGameFramework.Runtime.MainThreadFrameProfiler.Record(
                    UnityGameFramework.Runtime.MainThreadPerfScope.EntityShowRequest,
                    System.Diagnostics.Stopwatch.GetTimestamp() - requestStartTicks);
                if (actualViewId != viewId)
                {
                    throw new InvalidOperationException(
                        $"LogicEntityViewSpawnQueue dispatch returned a different view id. expected={viewId}, actual={actualViewId}.");
                }
            }
            catch
            {
                s_InFlight.Remove(viewId);
                ReferencePool.Release(request.Params);
                throw;
            }

            dispatched++;
        }

        TryCompleteBatch();
    }

	private static void DispatchPendingHides()
	{
		while (s_PendingHides.Count > 0
		       && s_PendingHides.Peek().EarliestRenderFrame <= Time.frameCount)
		{
			PendingHide request = s_PendingHides.Dequeue();
			if (!s_PendingHideViewIds.Remove(request.ViewId))
				throw new InvalidOperationException($"LogicEntityViewSpawnQueue lost pending hide identity. view={request.ViewId}.");
			GF.Entity.HideEntity(request.ViewId);
		}
	}

    private static void CancelDespawnedRequests()
    {
        s_CancelViewIds.Clear();
        foreach (KeyValuePair<int, PendingView> pair in s_InFlight)
        {
            if (pair.Value.Params.LogicEntityState.IsDespawnCommitted)
                s_CancelViewIds.Add(pair.Key);
        }

        for (int i = 0; i < s_CancelViewIds.Count; i++)
        {
            int viewId = s_CancelViewIds[i];
            PendingView request = s_InFlight[viewId];
            GF.Entity.HideEntity(viewId);
            s_InFlight.Remove(viewId);
            ReferencePool.Release(request.Params);
            s_BatchCanceled++;
        }
        s_CancelViewIds.Clear();
    }

    private static void CancelAll(string reason)
    {
        int pendingCount = s_Pending.Count;
        int inFlightCount = s_InFlight.Count;

        while (s_Pending.Count > 0)
            ReferencePool.Release(s_Pending.Dequeue().Params);

        s_CancelViewIds.Clear();
        foreach (int viewId in s_InFlight.Keys)
            s_CancelViewIds.Add(viewId);
        for (int i = 0; i < s_CancelViewIds.Count; i++)
        {
            int viewId = s_CancelViewIds[i];
            PendingView request = s_InFlight[viewId];
            GF.Entity.HideEntity(viewId);
            ReferencePool.Release(request.Params);
        }
        s_InFlight.Clear();
        s_CancelViewIds.Clear();

		while (s_PendingHides.Count > 0)
		{
			PendingHide request = s_PendingHides.Dequeue();
			GF.Entity.HideEntity(request.ViewId);
		}
		s_PendingHideViewIds.Clear();

        if (pendingCount > 0 || inFlightCount > 0)
        {
            Log.Info(
                "[LogicViewSpawn] Canceled requests. reason={0}, pending={1}, inFlight={2}.",
                reason,
                pendingCount,
                inFlightCount);
        }
    }

    private static void OnShowEntitySuccess(object sender, GameEventArgs gameEventArgs)
    {
        if (gameEventArgs is not ShowEntitySuccessEventArgs args || args.Entity == null)
            throw new InvalidOperationException("LogicEntityViewSpawnQueue received an invalid show-success event.");
        if (!s_InFlight.TryGetValue(args.Entity.Id, out PendingView request))
            return;

        if (!ReferenceEquals(args.UserData, request.Params))
        {
            s_Failure = $"LogicEntityViewSpawnQueue show-success userData mismatch. view={args.Entity.Id}.";
            return;
        }

        s_InFlight.Remove(args.Entity.Id);
        s_BatchShown++;
        RecordCompletionFrame();
        TryCompleteBatch();
    }

    private static void OnShowEntityFailure(object sender, GameEventArgs gameEventArgs)
    {
        if (gameEventArgs is not ShowEntityFailureEventArgs args)
            throw new InvalidOperationException("LogicEntityViewSpawnQueue received an invalid show-failure event.");
        if (!s_InFlight.Remove(args.EntityId))
            return;

        s_Failure =
            $"LogicEntityViewSpawnQueue failed to show entity view. view={args.EntityId}, asset={args.EntityAssetName}, error={args.ErrorMessage}";
    }

    private static void RecordCompletionFrame()
    {
        int renderFrame = Time.frameCount;
        if (s_LastCompletionRenderFrame != renderFrame)
        {
            s_LastCompletionRenderFrame = renderFrame;
            s_CompletedThisRenderFrame = 0;
        }

        s_CompletedThisRenderFrame++;
        s_MaxCompletedInRenderFrame = Math.Max(s_MaxCompletedInRenderFrame, s_CompletedThisRenderFrame);
    }

    private static void TryCompleteBatch()
    {
        if (s_BatchRequested == 0 || s_Pending.Count > 0 || s_InFlight.Count > 0)
            return;

        Log.Info(
            "[LogicViewSpawn] Batch complete. requested={0}, shown={1}, canceled={2}, renderFrames={3}, maxCompletedPerFrame={4}, maxInFlight={5}.",
            s_BatchRequested,
            s_BatchShown,
            s_BatchCanceled,
            Math.Max(1, Time.frameCount - s_BatchStartRenderFrame + 1),
            s_MaxCompletedInRenderFrame,
            MaxInFlightRequests);
        ClearMetrics();
    }

    private static void ClearMetrics()
    {
        s_Failure = null;
        s_BatchStartRenderFrame = 0;
        s_BatchRequested = 0;
        s_BatchShown = 0;
        s_BatchCanceled = 0;
        s_LastCompletionRenderFrame = -1;
        s_CompletedThisRenderFrame = 0;
        s_MaxCompletedInRenderFrame = 0;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicEntityViewSpawnQueue operation failed: queue is not active.");
        if (GF.Entity == null)
            throw new InvalidOperationException("LogicEntityViewSpawnQueue operation failed: GF.Entity is null.");
    }
}
