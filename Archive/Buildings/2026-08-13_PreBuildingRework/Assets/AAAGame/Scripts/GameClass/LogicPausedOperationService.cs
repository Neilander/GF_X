using System;

public static class LogicPausedOperationService
{
    public static bool IsExecuting { get; private set; }

    public static bool CanResolveImmediately =>
        LogicTimeControlService.IsActive
        && LogicTimeControlService.HasPause(LogicTimeControlSources.InGameUiPause);

    public static T Execute<T>(Func<T> operation)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        if (!CanResolveImmediately)
            throw new InvalidOperationException("Paused operations require the in-game UI pause token.");
        if (LogicTimeControlService.CurrentFrame == 0)
            throw new InvalidOperationException("Paused operations require at least one completed logic frame.");
        if (IsExecuting)
            throw new InvalidOperationException("LogicPausedOperationService.Execute failed: nested settlement detected.");
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicPausedOperationService.Execute cannot run during a logic frame.");

        ulong frame = LogicTimeControlService.CurrentFrame;
        if (LogicFrameRuntime.IsActive
            && (!LogicFrameRuntime.IsTimelineRunning || LogicFrameRuntime.CurrentFrame != frame))
        {
            throw new InvalidOperationException(
                $"Paused operation frame mismatch. time={frame}, runtime={LogicFrameRuntime.CurrentFrame}, running={LogicFrameRuntime.IsTimelineRunning}.");
        }
        ulong techEffectSequence = LogicTechEffectCommandService.IsActive
            ? LogicTechEffectCommandService.LastSequence
            : 0;
        ulong lifecycleSequence = LogicEntityLifecycleService.IsActive
            ? LogicEntityLifecycleService.LastSequence
            : 0;
        ulong obstacleSequence = LogicObstacleCommandService.IsActive
            ? LogicObstacleCommandService.LastSequence
            : 0;
        IsExecuting = true;
        try
        {
            T result = operation();
            if (LogicTechEffectCommandService.IsActive)
                LogicTechEffectCommandService.ApplyPausedOperation(frame, techEffectSequence);
            if (LogicEntityLifecycleService.IsActive)
                LogicEntityLifecycleService.ApplyPausedOperation(frame, lifecycleSequence);
            if (LogicObstacleCommandService.IsActive)
                LogicObstacleCommandService.ApplyPausedOperation(frame, obstacleSequence);
            if (LogicInteractionAuthorityService.IsActive)
                LogicInteractionAuthorityService.ReconcilePausedOperation();
            return result;
        }
        finally
        {
            IsExecuting = false;
        }
    }
}
