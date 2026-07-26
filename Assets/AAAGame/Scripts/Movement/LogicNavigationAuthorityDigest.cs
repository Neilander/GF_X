public readonly struct LogicNavigationAuthorityDigest
{
    public LogicNavigationAuthorityDigest(
        ulong worldAndConfigHash,
        ulong checkpointLiveStateHash,
        ulong worldProgressHash,
        ulong runtimeObstaclesHash,
        ulong agentsHash,
        ulong cachesHash,
        ulong flowTilesHash,
        ulong flowTileBuildQueueHash,
        ulong sharedGoalBuildQueueHash,
        ulong movingTargetAnchorsHash,
        ulong goalReservationsHash,
        ulong fixedPortalOwnersHash,
        ulong fixedCorridorBuildsHash)
    {
        HasDetails = true;
        WorldAndConfigHash = worldAndConfigHash;
        CheckpointLiveStateHash = checkpointLiveStateHash;
        WorldProgressHash = worldProgressHash;
        RuntimeObstaclesHash = runtimeObstaclesHash;
        AgentsHash = agentsHash;
        CachesHash = cachesHash;
        FlowTilesHash = flowTilesHash;
        FlowTileBuildQueueHash = flowTileBuildQueueHash;
        SharedGoalBuildQueueHash = sharedGoalBuildQueueHash;
        MovingTargetAnchorsHash = movingTargetAnchorsHash;
        GoalReservationsHash = goalReservationsHash;
        FixedPortalOwnersHash = fixedPortalOwnersHash;
        FixedCorridorBuildsHash = fixedCorridorBuildsHash;
    }

    public bool HasDetails { get; }
    public ulong WorldAndConfigHash { get; }
    public ulong CheckpointLiveStateHash { get; }
    public ulong WorldProgressHash { get; }
    public ulong RuntimeObstaclesHash { get; }
    public ulong AgentsHash { get; }
    public ulong CachesHash { get; }
    public ulong FlowTilesHash { get; }
    public ulong FlowTileBuildQueueHash { get; }
    public ulong SharedGoalBuildQueueHash { get; }
    public ulong MovingTargetAnchorsHash { get; }
    public ulong GoalReservationsHash { get; }
    public ulong FixedPortalOwnersHash { get; }
    public ulong FixedCorridorBuildsHash { get; }
}
