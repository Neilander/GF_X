using System;
using System.Collections.Generic;

public static class LogicInteractionTargetStateService
{
    private static readonly Dictionary<int, int> s_TargetByActor = new Dictionary<int, int>();

    public static bool IsActive { get; private set; }
    public static int ActorCount => s_TargetByActor.Count;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicInteractionTargetStateService.BeginTimeline failed: service is already active.");

        s_TargetByActor.Clear();
        IsActive = true;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        s_TargetByActor.Clear();
        IsActive = false;
    }

    public static void SetTarget(LogicEntityId actorId, LogicEntityId targetId)
    {
        EnsureActive();
        if (!LogicFrameRuntime.IsTicking)
            throw new InvalidOperationException("Interaction target state can only change during a logic Tick.");
        if (!actorId.IsValid)
            throw new ArgumentException("Actor id must be valid.", nameof(actorId));

        s_TargetByActor[actorId.Value] = targetId.IsValid ? targetId.Value : 0;
    }

    public static void RemoveActor(LogicEntityId actorId)
    {
        EnsureActive();
        if (!actorId.IsValid)
            throw new ArgumentException("Actor id must be valid.", nameof(actorId));

        s_TargetByActor.Remove(actorId.Value);
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (!LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("Interaction target reset requires paused logic time.");

        s_TargetByActor.Clear();
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        EnsureActive();
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        var actorIds = new List<int>(s_TargetByActor.Keys);
        actorIds.Sort();
        hasher.Add(actorIds.Count);
        for (int i = 0; i < actorIds.Count; i++)
        {
            int actorId = actorIds[i];
            hasher.Add(actorId);
            hasher.Add(s_TargetByActor[actorId]);
        }
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicInteractionTargetStateService operation failed: service is not active.");
    }
}
