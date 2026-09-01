using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using AAAGame.FlowPath;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;

public static partial class FlowFieldCrowdMovementSystem
{
    private static void EnsureFixedPortalOwnerFrame()
    {
        if (_world == null)
            throw new InvalidOperationException("EnsureFixedPortalOwnerFrame failed: world is null.");

        int frame = GetFrameCount();
        if (FixedPortalOwnerEvaluatedFrameByWorld.TryGetValue(_world.Version, out int evaluatedFrame)
            && evaluatedFrame == frame)
        {
            return;
        }

        CommitFixedPortalOwnerSnapshotForActiveWorld();
    }

    public static void CommitFixedPortalOwnerSnapshots()
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long snapshotStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        NavigationWorld previousWorld = _world;
        WorldRuntimeState previousActiveWorldState = _activeWorldState;
        FlowBuildQueueWorldScratch.Clear();
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state != null && state.World != null && !state.IsDirty && state.BuildJob == null)
                FlowBuildQueueWorldScratch.Add(state);
        }
        FlowBuildQueueWorldScratch.Sort((left, right) => left.AgentTypeId.CompareTo(right.AgentTypeId));

        try
        {
            for (int i = 0; i < FlowBuildQueueWorldScratch.Count; i++)
            {
                ActivateFlowBuildQueueWorld(FlowBuildQueueWorldScratch[i]);
                CommitFixedPortalOwnerSnapshotForActiveWorld();
            }
        }
        finally
        {
            FlowBuildQueueWorldScratch.Clear();
            _world = previousWorld;
            _activeWorldState = previousActiveWorldState;
            if (profile)
                _perf.FixedPortalOwnerSnapshotTicks += Stopwatch.GetTimestamp() - snapshotStartTicks;
        }
    }

    private static void CommitFixedPortalOwnerSnapshotForActiveWorld()
    {
        if (_world == null || _activeWorldState == null)
            throw new InvalidOperationException("CommitFixedPortalOwnerSnapshotForActiveWorld failed: active world is null.");

        int frame = GetFrameCount();
        if (FixedPortalOwnerEvaluatedFrameByWorld.TryGetValue(_world.Version, out int evaluatedFrame)
            && evaluatedFrame == frame)
        {
            return;
        }

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        FixedCorridorLookup corridorLookup = GetOrCreateFixedCorridorLookup(_world);
        long phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        int corridorOperations = AdvanceFixedCorridorBuilds(
            _world,
            corridorLookup,
            FixedCorridorBuildOperationQuota);
        _perf.FixedCorridorBuildOperations = checked(
            _perf.FixedCorridorBuildOperations + corridorOperations);
        if (profile)
            _perf.FixedCorridorBuildTicks += Stopwatch.GetTimestamp() - phaseStartTicks;

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        RefreshPendingFixedCorridorParticipants(_world.Version);
        if (profile)
            _perf.FixedPortalParticipantRefreshTicks += Stopwatch.GetTimestamp() - phaseStartTicks;

        phaseStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;

        FixedPortalParticipantScratch.Clear();
        foreach (KeyValuePair<FixedPortalOwnerKey, FixedPortalParticipantIndex> pair in FixedPortalParticipantIndexByKey)
        {
            if (pair.Key.WorldVersion != _world.Version || pair.Key.AgentTypeId != _world.AgentTypeId)
                continue;

            FixedPortalParticipantIndex index = pair.Value
                ?? throw new InvalidOperationException("Fixed portal participant index contains a null entry.");
            if (index.IsEmpty)
                throw new InvalidOperationException("Fixed portal participant index contains an empty entry.");
            FixedPortalParticipantScratch.Add(pair.Key, new FixedPortalParticipantSnapshot
            {
                PositiveMinimumAgentId = index.PositiveAgentIds.Count > 0 ? index.PositiveAgentIds.Min : int.MaxValue,
                NegativeMinimumAgentId = index.NegativeAgentIds.Count > 0 ? index.NegativeAgentIds.Min : int.MaxValue,
                PositiveOnPortal = index.PositiveOnPortalAgentIds.Count > 0,
                NegativeOnPortal = index.NegativeOnPortalAgentIds.Count > 0,
            });
        }

        FixedPortalOwnerKeyScratch.Clear();
        foreach (FixedPortalOwnerKey key in FixedPortalOwners.Keys)
        {
            if (key.WorldVersion == _world.Version && !FixedPortalParticipantScratch.ContainsKey(key))
                FixedPortalOwnerKeyScratch.Add(key);
        }
        for (int i = 0; i < FixedPortalOwnerKeyScratch.Count; i++)
            FixedPortalOwners.Remove(FixedPortalOwnerKeyScratch[i]);

        FixedPortalOwnerKeyScratch.Clear();
        foreach (FixedPortalOwnerKey key in FixedPortalParticipantScratch.Keys)
            FixedPortalOwnerKeyScratch.Add(key);
        FixedPortalOwnerKeyScratch.Sort(FixedPortalOwnerKeyComparison);
        for (int i = 0; i < FixedPortalOwnerKeyScratch.Count; i++)
        {
            FixedPortalOwnerKey key = FixedPortalOwnerKeyScratch[i];
            FixedPortalParticipantSnapshot participants = FixedPortalParticipantScratch[key];
            if (!FixedPortalOwners.TryGetValue(key, out FixedPortalOwnerState state))
            {
                state = new FixedPortalOwnerState
                {
                    Key = key,
                    OwnerDirection = ResolveInitialFixedPortalOwnerDirection(participants),
                    OwnerSinceFrame = frame,
                };
                FixedPortalOwners.Add(key, state);
            }
            else
            {
                UpdateFixedPortalOwner(state, participants, frame);
            }

            state.PositiveMinimumAgentId = participants.PositiveMinimumAgentId;
            state.NegativeMinimumAgentId = participants.NegativeMinimumAgentId;
            state.LastEvaluatedFrame = frame;
        }

        FixedPortalOwnerEvaluatedFrameByWorld[_world.Version] = frame;
        if (profile)
            _perf.FixedPortalOwnerResolveTicks += Stopwatch.GetTimestamp() - phaseStartTicks;
    }

    private static void RefreshPendingFixedCorridorParticipants(int worldVersion)
    {
        if (!PendingFixedCorridorParticipantAgentIdsByWorld.TryGetValue(worldVersion, out SortedSet<int> pendingAgentIds)
            || pendingAgentIds.Count == 0)
        {
            return;
        }

        FixedPortalParticipationAgentIdScratch.Clear();
        foreach (int agentId in pendingAgentIds)
            FixedPortalParticipationAgentIdScratch.Add(agentId);
        for (int i = 0; i < FixedPortalParticipationAgentIdScratch.Count; i++)
        {
            int agentId = FixedPortalParticipationAgentIdScratch[i];
            if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
                throw new InvalidOperationException($"Pending fixed corridor participant is missing agent={agentId}.");
            UpdateFixedPortalParticipation(agent);
        }
        FixedPortalParticipationAgentIdScratch.Clear();
    }

    private static void UpdateFixedPortalParticipation(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("UpdateFixedPortalParticipation failed: agent is null.");
#if UNITY_EDITOR
        _testFixedPortalParticipationUpdateCount++;
#endif

        FixedCorridorResolution resolution = ResolveFixedPortalTraversal(
            agent,
            out FixedPortalOwnerKey key,
            out int direction,
            out int distanceInCells);
        if (resolution == FixedCorridorResolution.Pending)
        {
            RemoveFixedPortalParticipation(agent.Id);
            AddPendingFixedCorridorParticipant(_world.Version, agent.Id);
            return;
        }

        RemovePendingFixedCorridorParticipant(agent.Id);
        if (resolution != FixedCorridorResolution.Ready || distanceInCells > FixedPortalInfluenceCells)
        {
            RemoveFixedPortalParticipation(agent.Id);
            return;
        }

        bool onPortal = distanceInCells == 0;
        if (FixedPortalParticipationByAgent.TryGetValue(agent.Id, out FixedPortalParticipation current)
            && current.Key.Equals(key)
            && current.Direction == direction
            && current.OnPortal == onPortal)
        {
            return;
        }

        RemoveFixedPortalParticipation(agent.Id);
        if (!FixedPortalParticipantIndexByKey.TryGetValue(key, out FixedPortalParticipantIndex index))
        {
            index = new FixedPortalParticipantIndex();
            FixedPortalParticipantIndexByKey.Add(key, index);
        }

        SortedSet<int> directionIds = direction > 0 ? index.PositiveAgentIds : index.NegativeAgentIds;
        if (!directionIds.Add(agent.Id))
            throw new InvalidOperationException($"Fixed portal participant index already contains agent={agent.Id}.");
        if (onPortal)
        {
            SortedSet<int> onPortalIds = direction > 0 ? index.PositiveOnPortalAgentIds : index.NegativeOnPortalAgentIds;
            if (!onPortalIds.Add(agent.Id))
                throw new InvalidOperationException($"Fixed portal on-portal index already contains agent={agent.Id}.");
        }

        FixedPortalParticipationByAgent.Add(agent.Id, new FixedPortalParticipation
        {
            Key = key,
            Direction = direction,
            OnPortal = onPortal,
        });
    }

    private static void RemoveFixedPortalParticipation(int agentId)
    {
        RemovePendingFixedCorridorParticipant(agentId);
        if (!FixedPortalParticipationByAgent.TryGetValue(agentId, out FixedPortalParticipation participation))
            return;
        if (!FixedPortalParticipantIndexByKey.TryGetValue(participation.Key, out FixedPortalParticipantIndex index))
            throw new InvalidOperationException($"Fixed portal participant index is missing for agent={agentId}.");

        SortedSet<int> directionIds = participation.Direction > 0 ? index.PositiveAgentIds : index.NegativeAgentIds;
        if (!directionIds.Remove(agentId))
            throw new InvalidOperationException($"Fixed portal participant direction index is missing agent={agentId}.");
        if (participation.OnPortal)
        {
            SortedSet<int> onPortalIds = participation.Direction > 0 ? index.PositiveOnPortalAgentIds : index.NegativeOnPortalAgentIds;
            if (!onPortalIds.Remove(agentId))
                throw new InvalidOperationException($"Fixed portal on-portal index is missing agent={agentId}.");
        }

        FixedPortalParticipationByAgent.Remove(agentId);
        if (index.IsEmpty)
            FixedPortalParticipantIndexByKey.Remove(participation.Key);
    }

    private static void AddPendingFixedCorridorParticipant(int worldVersion, int agentId)
    {
        if (!PendingFixedCorridorParticipantAgentIdsByWorld.TryGetValue(worldVersion, out SortedSet<int> pendingAgentIds))
        {
            pendingAgentIds = new SortedSet<int>();
            PendingFixedCorridorParticipantAgentIdsByWorld.Add(worldVersion, pendingAgentIds);
        }
        pendingAgentIds.Add(agentId);
    }

    private static void RemovePendingFixedCorridorParticipant(int agentId)
    {
        FixedPortalOwnerKeyScratch.Clear();
        int emptyWorldVersion = -1;
        foreach (KeyValuePair<int, SortedSet<int>> pair in PendingFixedCorridorParticipantAgentIdsByWorld)
        {
            if (!pair.Value.Remove(agentId))
                continue;
            if (pair.Value.Count == 0)
                emptyWorldVersion = pair.Key;
            break;
        }
        if (emptyWorldVersion >= 0)
            PendingFixedCorridorParticipantAgentIdsByWorld.Remove(emptyWorldVersion);
    }

    private static FixVector2 ApplyFixedPortalOwner(AgentRuntimeData agent, FixVector2 velocity)
    {
        if (velocity == FixVector2.Zero)
            return velocity;
        FixedCorridorResolution resolution = ResolveFixedPortalTraversal(
            agent,
            out FixedPortalOwnerKey key,
            out int direction,
            out int distanceInCells);
        if (resolution == FixedCorridorResolution.Pending)
        {
            agent.NavState.LastFixedFlowResult = "bottleneck-owner-pending";
            agent.NavState.LastFixedFlowVelocity = FixVector2.Zero;
            return FixVector2.Zero;
        }
        if (resolution != FixedCorridorResolution.Ready
            || distanceInCells > FixedPortalInfluenceCells
            || !FixedPortalOwners.TryGetValue(key, out FixedPortalOwnerState state)
            || state.OwnerDirection == direction)
        {
            return velocity;
        }

        agent.NavState.LastFixedFlowResult =
            $"bottleneck-owner-wait kind={key.Kind} id={key.LocalBottleneckId} owner={state.OwnerDirection} direction={direction} frame={state.LastEvaluatedFrame}";
        agent.NavState.LastFixedFlowVelocity = FixVector2.Zero;
        return FixVector2.Zero;
    }

    private static FixedCorridorResolution ResolveFixedPortalTraversal(
        AgentRuntimeData agent,
        out FixedPortalOwnerKey key,
        out int direction,
        out int distanceInCells)
    {
        key = default;
        direction = 0;
        distanceInCells = int.MaxValue;
        if (agent?.NavState?.PathHandle == null || !agent.NavState.HasGoal || _world == null)
            return FixedCorridorResolution.Absent;

        PathHandle handle = agent.NavState.PathHandle;
        int portalId = ResolveCurrentDownstreamPortalId(handle);
        if (portalId >= 0 && TryGetPortalById(_world, portalId, out PortalData portal) && portal.IsNarrow)
        {
            int currentSectorId = agent.NavState.CurrentSectorId;
            if (currentSectorId == portal.SectorAId)
                direction = 1;
            else if (currentSectorId == portal.SectorBId)
                direction = -1;
            else
                return FixedCorridorResolution.Absent;

            distanceInCells = ResolveFixedPortalCellDistance(agent.NavState.CurrentCell, portal);
            key = new FixedPortalOwnerKey(_world.Version, _world.AgentTypeId, portalId);
            return FixedCorridorResolution.Ready;
        }

        return ResolveFixedCorridorTraversal(agent, out key, out direction, out distanceInCells);
    }

    private static FixedCorridorResolution ResolveFixedCorridorTraversal(
        AgentRuntimeData agent,
        out FixedPortalOwnerKey key,
        out int direction,
        out int distanceInCells)
    {
        key = default;
        direction = 0;
        distanceInCells = int.MaxValue;
        if (agent?.NavState?.PathHandle == null || !agent.NavState.HasGoal || _world == null)
            return FixedCorridorResolution.Absent;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle.WorldVersion != _world.Version)
            return FixedCorridorResolution.Absent;
        if (!_world.IsWalkable(agent.NavState.CurrentCell.x, agent.NavState.CurrentCell.y))
        {
            throw new InvalidOperationException(
                $"Fixed corridor owner cannot resolve non-walkable current cell ({agent.NavState.CurrentCell.x},{agent.NavState.CurrentCell.y}) for agent {agent.Id}.");
        }

        FixedCorridorLookup lookup = GetOrCreateFixedCorridorLookup(_world);
        FixedCorridorResolution resolution = ResolveFixedCorridorNearCell(
                _world,
                lookup,
                agent.NavState.CurrentCell,
                out FixedCorridorDescriptor descriptor,
                out distanceInCells);
        if (resolution != FixedCorridorResolution.Ready)
            return resolution;

        long distanceToA = ResolveFixedCorridorEndpointDistance(_world, handle.GoalX, handle.GoalY, descriptor.EndpointACellIndices);
        long distanceToB = ResolveFixedCorridorEndpointDistance(_world, handle.GoalX, handle.GoalY, descriptor.EndpointBCellIndices);
        if (distanceToA == distanceToB)
            return FixedCorridorResolution.Absent;

        direction = distanceToB < distanceToA ? 1 : -1;
        long currentDistanceToDestination = direction > 0
            ? ResolveFixedCorridorEndpointDistance(
                _world,
                agent.NavState.CurrentCell.x,
                agent.NavState.CurrentCell.y,
                descriptor.EndpointBCellIndices)
            : ResolveFixedCorridorEndpointDistance(
                _world,
                agent.NavState.CurrentCell.x,
                agent.NavState.CurrentCell.y,
                descriptor.EndpointACellIndices);
        if (currentDistanceToDestination == 0)
            return FixedCorridorResolution.Absent;

        key = descriptor.Key;
        return FixedCorridorResolution.Ready;
    }

    private static FixedCorridorLookup GetOrCreateFixedCorridorLookup(NavigationWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (world.Version <= 0)
            throw new InvalidOperationException("Fixed corridor lookup requires a committed navigation world version.");
        if (FixedCorridorLookupByWorldVersion.TryGetValue(world.Version, out FixedCorridorLookup existing))
            return existing;
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("Fixed corridor lookup encountered an invalid walkable mask.");

        int narrowWidth = Math.Max(1, ResolvePortalNarrowWidthCells(world));
        var lookup = new FixedCorridorLookup(narrowWidth);
        FixedCorridorLookupByWorldVersion.Add(world.Version, lookup);
        return lookup;
    }

    private static FixedCorridorResolution ResolveFixedCorridorDescriptor(
        NavigationWorld world,
        FixedCorridorLookup lookup,
        int startIndex,
        out FixedCorridorDescriptor descriptor)
    {
        descriptor = null;
        int cellCount = checked(world.Width * world.Height);
        if (startIndex < 0 || startIndex >= cellCount)
            throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex, "Fixed corridor cell index is outside the navigation world.");
        if (lookup == null)
            throw new ArgumentNullException(nameof(lookup));
        if (lookup.ResolvedNonCorridorCells.Contains(startIndex))
            return FixedCorridorResolution.Absent;
        if (lookup.ComponentIdByCell.TryGetValue(startIndex, out int componentId))
        {
            if (!lookup.CompletedComponentIds.Contains(componentId))
                return FixedCorridorResolution.Pending;
            return lookup.DescriptorByComponentId.TryGetValue(componentId, out descriptor)
                ? FixedCorridorResolution.Ready
                : FixedCorridorResolution.Absent;
        }
        if (!IsFixedCorridorCandidate(world, lookup, startIndex))
        {
            lookup.ResolvedNonCorridorCells.Add(startIndex);
            return FixedCorridorResolution.Absent;
        }

        if (lookup.PendingStartIndices.Add(startIndex))
            lookup.PendingStartContentHash ^= ComputeFixedCorridorCellToken(startIndex);
        return FixedCorridorResolution.Pending;
    }

    private static int AdvanceFixedCorridorBuilds(
        NavigationWorld world,
        FixedCorridorLookup lookup,
        int operationQuota)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (lookup == null)
            throw new ArgumentNullException(nameof(lookup));
        if (operationQuota <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationQuota));

        int operations = 0;
        while (operations < operationQuota)
        {
            if (lookup.ActiveBuildJob == null && !TryStartNextFixedCorridorBuildJob(world, lookup))
                break;

            FixedCorridorBuildJob job = lookup.ActiveBuildJob;
            if (job.Head >= job.Queue.Count)
            {
                CompleteFixedCorridorBuildJob(world, lookup, job);
                lookup.ActiveBuildJob = null;
                continue;
            }

            int currentIndex = job.Queue[job.Head++];
            _fixedCorridorExpandedCellCount++;
            operations++;
            int currentX = currentIndex % world.Width;
            int currentY = currentIndex / world.Width;
            for (int offsetIndex = 0; offsetIndex < CardinalOffsetX.Length; offsetIndex++)
            {
                int nextX = currentX + CardinalOffsetX[offsetIndex];
                int nextY = currentY + CardinalOffsetY[offsetIndex];
                if (!world.IsWalkable(nextX, nextY)
                    || !CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                {
                    continue;
                }

                int nextIndex = world.GetIndex(nextX, nextY);
                if (IsFixedCorridorCandidate(world, lookup, nextIndex))
                    AddFixedCorridorBuildCell(lookup, job, nextIndex);
                else
                    AddFixedCorridorEndpointCell(
                        job.ExternalEndpointCells,
                        ref job.ExternalEndpointContentHash,
                        ref job.ExternalEndpointOverflow,
                        nextIndex,
                        lookup.NarrowWidth);
            }

            byte orientation = GetFixedCorridorOrientation(world, lookup, currentIndex);
            bool horizontalTerminal = (orientation & 1) != 0
                                      && (!HasFixedCorridorCandidateNeighbor(world, lookup, currentX, currentY, -1, 0)
                                          || !HasFixedCorridorCandidateNeighbor(world, lookup, currentX, currentY, 1, 0));
            bool verticalTerminal = (orientation & 2) != 0
                                    && (!HasFixedCorridorCandidateNeighbor(world, lookup, currentX, currentY, 0, -1)
                                        || !HasFixedCorridorCandidateNeighbor(world, lookup, currentX, currentY, 0, 1));
            if (horizontalTerminal || verticalTerminal)
                AddFixedCorridorEndpointCell(
                    job.TerminalEndpointCells,
                    ref job.TerminalEndpointContentHash,
                    ref job.TerminalEndpointOverflow,
                    currentIndex,
                    lookup.NarrowWidth);
        }

        if (lookup.ActiveBuildJob != null
            && lookup.ActiveBuildJob.Head >= lookup.ActiveBuildJob.Queue.Count)
        {
            CompleteFixedCorridorBuildJob(world, lookup, lookup.ActiveBuildJob);
            lookup.ActiveBuildJob = null;
        }

        return operations;
    }

    private static bool TryStartNextFixedCorridorBuildJob(NavigationWorld world, FixedCorridorLookup lookup)
    {
        while (lookup.PendingStartIndices.Count > 0)
        {
            int startIndex = lookup.PendingStartIndices.Min;
            lookup.PendingStartIndices.Remove(startIndex);
            lookup.PendingStartContentHash ^= ComputeFixedCorridorCellToken(startIndex);
            if (lookup.ResolvedNonCorridorCells.Contains(startIndex)
                || lookup.ComponentIdByCell.ContainsKey(startIndex))
            {
                continue;
            }
            if (!IsFixedCorridorCandidate(world, lookup, startIndex))
            {
                lookup.ResolvedNonCorridorCells.Add(startIndex);
                continue;
            }

            var job = new FixedCorridorBuildJob
            {
                ComponentId = lookup.NextComponentId++,
                StartIndex = startIndex,
                MinimumCellIndex = startIndex,
            };
            AddFixedCorridorBuildCell(lookup, job, startIndex);
            lookup.ActiveBuildJob = job;
            return true;
        }

        return false;
    }

    private static void AddFixedCorridorBuildCell(
        FixedCorridorLookup lookup,
        FixedCorridorBuildJob job,
        int cellIndex)
    {
        if (lookup.ComponentIdByCell.TryGetValue(cellIndex, out int existingComponentId))
        {
            if (existingComponentId != job.ComponentId)
            {
                throw new InvalidOperationException(
                    $"Fixed corridor incremental build encountered overlapping components existing={existingComponentId} active={job.ComponentId} cell={cellIndex}.");
            }
            return;
        }

        lookup.ComponentIdByCell.Add(cellIndex, job.ComponentId);
        lookup.ComponentAssignmentContentHash ^= ComputeFixedCorridorComponentCellToken(job.ComponentId, cellIndex);
        job.Queue.Add(cellIndex);
        job.MinimumCellIndex = Math.Min(job.MinimumCellIndex, cellIndex);
        job.ComponentCellContentHash ^= ComputeFixedCorridorCellToken(cellIndex);
    }

    private static void AddFixedCorridorEndpointCell(
        HashSet<int> cells,
        ref ulong contentHash,
        ref bool overflow,
        int cellIndex,
        int narrowWidth)
    {
        if (overflow || cells.Contains(cellIndex))
            return;
        int maximumEndpointCells = checked(Math.Max(1, narrowWidth) * 8);
        if (cells.Count >= maximumEndpointCells)
        {
            overflow = true;
            cells.Clear();
            contentHash = 0;
            return;
        }
        if (cells.Add(cellIndex))
            contentHash ^= ComputeFixedCorridorCellToken(cellIndex);
    }

    private static ulong ComputeFixedCorridorCellToken(int cellIndex)
    {
        ulong token = 14695981039346656037UL;
        AddAuthorityToken(ref token, cellIndex);
        return token;
    }

    private static ulong ComputeFixedCorridorComponentCellToken(int componentId, int cellIndex)
    {
        ulong token = 14695981039346656037UL;
        AddAuthorityToken(ref token, componentId);
        AddAuthorityToken(ref token, cellIndex);
        return token;
    }

    private static void CompleteFixedCorridorBuildJob(
        NavigationWorld world,
        FixedCorridorLookup lookup,
        FixedCorridorBuildJob job)
    {
        bool endpointOverflow = job.ExternalEndpointOverflow
                                || (job.ExternalEndpointCells.Count == 0 && job.TerminalEndpointOverflow);
        HashSet<int> endpointCells = job.ExternalEndpointCells.Count > 0
            ? job.ExternalEndpointCells
            : job.TerminalEndpointCells;
        List<int[]> endpointGroups = endpointOverflow
            ? new List<int[]>()
            : BuildFixedCorridorCellGroups(world, endpointCells);
        FixedCorridorDescriptor descriptor = null;

        if (endpointGroups.Count == 2)
        {
            endpointGroups.Sort((left, right) => left[0].CompareTo(right[0]));
            descriptor = new FixedCorridorDescriptor
            {
                Key = new FixedPortalOwnerKey(world.Version, world.AgentTypeId, 2, job.MinimumCellIndex),
                ComponentId = job.ComponentId,
                ComponentCellCount = job.Queue.Count,
                EndpointACellIndices = endpointGroups[0],
                EndpointBCellIndices = endpointGroups[1],
            };
            lookup.DescriptorByComponentId.Add(job.ComponentId, descriptor);
            lookup.DescriptorContentHash ^= ComputeFixedCorridorDescriptorToken(descriptor);
        }

        lookup.CompletedComponentIds.Add(job.ComponentId);
        lookup.CompletedComponentContentHash ^= ComputeFixedCorridorCellToken(job.ComponentId);
    }

    private static ulong ComputeFixedCorridorDescriptorToken(FixedCorridorDescriptor descriptor)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));
        ulong token = 14695981039346656037UL;
        AddAuthorityToken(ref token, descriptor.Key.WorldVersion);
        AddAuthorityToken(ref token, descriptor.Key.AgentTypeId);
        AddAuthorityToken(ref token, descriptor.Key.Kind);
        AddAuthorityToken(ref token, descriptor.Key.LocalBottleneckId);
        AddAuthorityToken(ref token, descriptor.ComponentId);
        AddAuthorityToken(ref token, descriptor.ComponentCellCount);
        for (int i = 0; i < descriptor.EndpointACellIndices.Length; i++)
            AddAuthorityToken(ref token, descriptor.EndpointACellIndices[i]);
        AddAuthorityToken(ref token, int.MinValue);
        for (int i = 0; i < descriptor.EndpointBCellIndices.Length; i++)
            AddAuthorityToken(ref token, descriptor.EndpointBCellIndices[i]);
        return token;
    }

    private static bool IsFixedCorridorCandidate(NavigationWorld world, FixedCorridorLookup lookup, int cellIndex)
    {
        if (lookup.CandidateByCell.TryGetValue(cellIndex, out bool existing))
            return existing;

        byte orientation = GetFixedCorridorOrientation(world, lookup, cellIndex);
        bool candidate = orientation != 0 || IsFixedCorridorCornerConnector(world, lookup, cellIndex);
        lookup.CandidateByCell.Add(cellIndex, candidate);
        return candidate;
    }

    private static byte GetFixedCorridorOrientation(NavigationWorld world, FixedCorridorLookup lookup, int cellIndex)
    {
        if (lookup.OrientationsByCell.TryGetValue(cellIndex, out byte existing))
            return existing;

        _fixedCorridorClassifiedCellCount++;
        int cellX = cellIndex % world.Width;
        int cellY = cellIndex / world.Width;
        byte orientation = 0;
        if (world.IsWalkable(cellX, cellY))
        {
            if (MeasureFixedCorridorSpan(world, cellX, cellY, 0, 1, lookup.NarrowWidth) <= lookup.NarrowWidth)
                orientation |= 1;
            if (MeasureFixedCorridorSpan(world, cellX, cellY, 1, 0, lookup.NarrowWidth) <= lookup.NarrowWidth)
                orientation |= 2;
        }

        lookup.OrientationsByCell.Add(cellIndex, orientation);
        return orientation;
    }

    private static bool IsFixedCorridorCornerConnector(
        NavigationWorld world,
        FixedCorridorLookup lookup,
        int startIndex)
    {
        bool hasHorizontalSeed = false;
        bool hasVerticalSeed = false;
        int head = 0;
        int tail = 0;
        lookup.ConnectorQueue[tail] = startIndex;
        lookup.ConnectorDistances[tail++] = 0;
        while (head < tail)
        {
            int currentIndex = lookup.ConnectorQueue[head];
            int currentDistance = lookup.ConnectorDistances[head++];
            byte orientation = GetFixedCorridorOrientation(world, lookup, currentIndex);
            hasHorizontalSeed |= (orientation & 1) != 0;
            hasVerticalSeed |= (orientation & 2) != 0;
            if (hasHorizontalSeed && hasVerticalSeed)
                return true;
            if (currentDistance >= lookup.NarrowWidth)
                continue;

            int currentX = currentIndex % world.Width;
            int currentY = currentIndex / world.Width;
            for (int offsetIndex = 0; offsetIndex < CardinalOffsetX.Length; offsetIndex++)
            {
                int nextX = currentX + CardinalOffsetX[offsetIndex];
                int nextY = currentY + CardinalOffsetY[offsetIndex];
                if (!world.IsWalkable(nextX, nextY)
                    || !CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                {
                    continue;
                }

                int nextIndex = world.GetIndex(nextX, nextY);
                bool alreadyQueued = false;
                for (int i = 0; i < tail; i++)
                {
                    if (lookup.ConnectorQueue[i] == nextIndex)
                    {
                        alreadyQueued = true;
                        break;
                    }
                }
                if (alreadyQueued)
                    continue;
                if (tail >= lookup.ConnectorQueue.Length)
                    throw new InvalidOperationException("Fixed corridor connector search exceeded its bounded queue.");
                lookup.ConnectorQueue[tail] = nextIndex;
                lookup.ConnectorDistances[tail++] = currentDistance + 1;
            }
        }

        return false;
    }

    private static int MeasureFixedCorridorSpan(
        NavigationWorld world,
        int startX,
        int startY,
        int stepX,
        int stepY,
        int limit)
    {
        if (!world.IsWalkable(startX, startY))
            throw new InvalidOperationException($"Fixed corridor span requires walkable cell ({startX},{startY}).");
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        int count = 1;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int currentX = startX;
            int currentY = startY;
            while (count <= limit)
            {
                int nextX = currentX + stepX * sign;
                int nextY = currentY + stepY * sign;
                if (!world.IsWalkable(nextX, nextY)
                    || !CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                {
                    break;
                }

                count++;
                currentX = nextX;
                currentY = nextY;
            }
        }

        return count;
    }

    private static bool HasFixedCorridorCandidateNeighbor(
        NavigationWorld world,
        FixedCorridorLookup lookup,
        int fromX,
        int fromY,
        int offsetX,
        int offsetY)
    {
        int toX = fromX + offsetX;
        int toY = fromY + offsetY;
        return world.IsWalkable(toX, toY)
               && CanTraverseNeighborCells(world, fromX, fromY, toX, toY)
               && IsFixedCorridorCandidate(world, lookup, world.GetIndex(toX, toY));
    }

    private static List<int[]> BuildFixedCorridorCellGroups(NavigationWorld world, HashSet<int> cellIndices)
    {
        if (cellIndices == null)
            throw new ArgumentNullException(nameof(cellIndices));

        var groups = new List<int[]>();
        var visited = new HashSet<int>();
        var orderedCells = new List<int>(cellIndices);
        orderedCells.Sort();
        for (int startCellIndex = 0; startCellIndex < orderedCells.Count; startCellIndex++)
        {
            int startIndex = orderedCells[startCellIndex];
            if (!visited.Add(startIndex))
                continue;

            var group = new List<int>();
            var queue = new List<int> { startIndex };
            for (int head = 0; head < queue.Count; head++)
            {
                int currentIndex = queue[head];
                group.Add(currentIndex);
                int currentX = currentIndex % world.Width;
                int currentY = currentIndex / world.Width;
                for (int offsetIndex = 0; offsetIndex < CardinalOffsetX.Length; offsetIndex++)
                {
                    int nextX = currentX + CardinalOffsetX[offsetIndex];
                    int nextY = currentY + CardinalOffsetY[offsetIndex];
                    if (!world.IsWalkable(nextX, nextY)
                        || !CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                    {
                        continue;
                    }

                    int nextIndex = world.GetIndex(nextX, nextY);
                    if (!cellIndices.Contains(nextIndex) || !visited.Add(nextIndex))
                        continue;
                    queue.Add(nextIndex);
                }
            }

            group.Sort();
            groups.Add(group.ToArray());
        }

        groups.Sort((left, right) => left[0].CompareTo(right[0]));
        return groups;
    }

    private static FixedCorridorResolution ResolveFixedCorridorNearCell(
        NavigationWorld world,
        FixedCorridorLookup lookup,
        Vector2Int startCell,
        out FixedCorridorDescriptor descriptor,
        out int distanceInCells)
    {
        descriptor = null;
        distanceInCells = int.MaxValue;
        if (lookup == null)
            throw new InvalidOperationException("Fixed corridor resolver encountered invalid lookup data.");

        int queueCapacity = 1 + 2 * FixedPortalInfluenceCells * (FixedPortalInfluenceCells + 1);
        var queue = new int[queueCapacity];
        var distances = new int[queueCapacity];
        int head = 0;
        int tail = 0;
        bool hasPending = false;
        queue[tail] = world.GetIndex(startCell.x, startCell.y);
        distances[tail++] = 0;
        while (head < tail)
        {
            int currentIndex = queue[head];
            int currentDistance = distances[head++];
            FixedCorridorResolution resolution = ResolveFixedCorridorDescriptor(world, lookup, currentIndex, out FixedCorridorDescriptor candidate);
            hasPending |= resolution == FixedCorridorResolution.Pending;
            if (resolution == FixedCorridorResolution.Ready
                && (descriptor == null
                    || currentDistance < distanceInCells
                    || (currentDistance == distanceInCells
                        && CompareFixedPortalOwnerKeys(candidate.Key, descriptor.Key) < 0)))
            {
                descriptor = candidate;
                distanceInCells = currentDistance;
            }

            if (currentDistance >= FixedPortalInfluenceCells)
                continue;

            int currentX = currentIndex % world.Width;
            int currentY = currentIndex / world.Width;
            for (int offsetIndex = 0; offsetIndex < CardinalOffsetX.Length; offsetIndex++)
            {
                int nextX = currentX + CardinalOffsetX[offsetIndex];
                int nextY = currentY + CardinalOffsetY[offsetIndex];
                if (!world.IsWalkable(nextX, nextY)
                    || !CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                {
                    continue;
                }

                int nextIndex = world.GetIndex(nextX, nextY);
                bool alreadyQueued = false;
                for (int i = 0; i < tail; i++)
                {
                    if (queue[i] == nextIndex)
                    {
                        alreadyQueued = true;
                        break;
                    }
                }
                if (alreadyQueued)
                    continue;
                if (tail >= queueCapacity)
                    throw new InvalidOperationException("Fixed corridor resolver exceeded its bounded influence queue.");
                queue[tail] = nextIndex;
                distances[tail++] = currentDistance + 1;
            }
        }

        if (hasPending)
            return FixedCorridorResolution.Pending;
        return descriptor != null ? FixedCorridorResolution.Ready : FixedCorridorResolution.Absent;
    }

    private static long ResolveFixedCorridorEndpointDistance(
        NavigationWorld world,
        int goalX,
        int goalY,
        int[] endpointCellIndices)
    {
        if (endpointCellIndices == null || endpointCellIndices.Length == 0)
            throw new InvalidOperationException("Fixed corridor descriptor has no endpoint cells.");

        long best = long.MaxValue;
        for (int i = 0; i < endpointCellIndices.Length; i++)
        {
            int index = endpointCellIndices[i];
            if (index < 0 || index >= world.Width * world.Height)
                throw new InvalidOperationException("Fixed corridor descriptor contains an invalid endpoint cell.");
            int endpointX = index % world.Width;
            int endpointY = index / world.Width;
            long distance = Math.Abs((long)goalX - endpointX) + Math.Abs((long)goalY - endpointY);
            if (distance < best)
                best = distance;
        }

        return best;
    }

    private static int ResolveFixedPortalCellDistance(Vector2Int cell, PortalData portal)
    {
        int best = int.MaxValue;
        ResolveFixedPortalCellDistance(cell, portal.CellsA, ref best);
        ResolveFixedPortalCellDistance(cell, portal.CellsB, ref best);
        if (best == int.MaxValue)
            throw new InvalidOperationException($"Fixed portal owner cannot resolve cells for portal {portal.PortalId}.");
        return best;
    }

    private static void ResolveFixedPortalCellDistance(Vector2Int cell, Vector2Int[] portalCells, ref int best)
    {
        if (portalCells == null)
            return;
        for (int i = 0; i < portalCells.Length; i++)
        {
            int distance = Math.Max(
                Math.Abs(cell.x - portalCells[i].x),
                Math.Abs(cell.y - portalCells[i].y));
            if (distance < best)
                best = distance;
        }
    }

    private static int ResolveInitialFixedPortalOwnerDirection(FixedPortalParticipantSnapshot participants)
    {
        bool hasPositive = participants.PositiveMinimumAgentId != int.MaxValue;
        bool hasNegative = participants.NegativeMinimumAgentId != int.MaxValue;
        if (!hasPositive && !hasNegative)
            throw new InvalidOperationException("Fixed portal owner requires at least one participant.");
        if (!hasNegative)
            return 1;
        if (!hasPositive)
            return -1;
        return participants.PositiveMinimumAgentId < participants.NegativeMinimumAgentId ? 1 : -1;
    }

    private static void UpdateFixedPortalOwner(
        FixedPortalOwnerState state,
        FixedPortalParticipantSnapshot participants,
        int frame)
    {
        bool positiveAvailable = participants.PositiveMinimumAgentId != int.MaxValue;
        bool negativeAvailable = participants.NegativeMinimumAgentId != int.MaxValue;
        bool ownerAvailable = state.OwnerDirection > 0 ? positiveAvailable : negativeAvailable;
        bool opposingAvailable = state.OwnerDirection > 0 ? negativeAvailable : positiveAvailable;
        bool ownerOnPortal = state.OwnerDirection > 0 ? participants.PositiveOnPortal : participants.NegativeOnPortal;
        int ownerElapsed = Math.Max(0, frame - state.OwnerSinceFrame);
        bool canRelease = ownerElapsed >= FixedPortalMinimumOwnerTicks && !ownerOnPortal;
        bool maximumElapsed = ownerElapsed >= FixedPortalMaximumOwnerTicks;
        if (opposingAvailable && canRelease && (!ownerAvailable || maximumElapsed))
        {
            state.OwnerDirection = -state.OwnerDirection;
            state.OwnerSinceFrame = frame;
        }
    }

    private static int CompareFixedPortalOwnerKeys(FixedPortalOwnerKey left, FixedPortalOwnerKey right)
    {
        int result = left.WorldVersion.CompareTo(right.WorldVersion);
        if (result != 0)
            return result;
        result = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (result != 0)
            return result;
        result = left.Kind.CompareTo(right.Kind);
        return result != 0 ? result : left.LocalBottleneckId.CompareTo(right.LocalBottleneckId);
    }

    private static void ClearFixedPortalOwnersForWorld(int worldVersion)
    {
        if (worldVersion <= 0)
            return;

        FixedPortalOwnerKeyScratch.Clear();
        foreach (FixedPortalOwnerKey key in FixedPortalOwners.Keys)
        {
            if (key.WorldVersion == worldVersion)
                FixedPortalOwnerKeyScratch.Add(key);
        }
        for (int i = 0; i < FixedPortalOwnerKeyScratch.Count; i++)
            FixedPortalOwners.Remove(FixedPortalOwnerKeyScratch[i]);
        FixedPortalOwnerKeyScratch.Clear();

        FixedPortalParticipationAgentIdScratch.Clear();
        foreach (KeyValuePair<int, FixedPortalParticipation> pair in FixedPortalParticipationByAgent)
        {
            if (pair.Value.Key.WorldVersion == worldVersion)
                FixedPortalParticipationAgentIdScratch.Add(pair.Key);
        }
        for (int i = 0; i < FixedPortalParticipationAgentIdScratch.Count; i++)
            RemoveFixedPortalParticipation(FixedPortalParticipationAgentIdScratch[i]);
        FixedPortalParticipationAgentIdScratch.Clear();
        PendingFixedCorridorParticipantAgentIdsByWorld.Remove(worldVersion);
        FixedPortalOwnerEvaluatedFrameByWorld.Remove(worldVersion);
        FixedCorridorLookupByWorldVersion.Remove(worldVersion);
    }

    public static void LogCombatClusterDiagnostic(IEntityContext self, IEntityContext target, bool moveLockedByAttack, float attackRange)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Attack))
            return;
        if (self == null)
            throw new InvalidOperationException("LogCombatClusterDiagnostic failed: self is null.");
        if (_world == null)
            throw new InvalidOperationException("LogCombatClusterDiagnostic failed: world is null.");

        int selfId = ResolveAgentId(self);
        if (!Agents.TryGetValue(selfId, out AgentRuntimeData agent))
            return;

        int frame = GetFrameCount();
        if (agent.NavState.LastCombatClusterDiagnosticFrame >= 0
            && frame - agent.NavState.LastCombatClusterDiagnosticFrame < 20)
        {
            return;
        }

        agent.NavState.LastCombatClusterDiagnosticFrame = frame;
        Vector3 targetPos = target != null ? target.Position : Vector3.zero;
        float selfToTarget = target != null ? self.DistanceToTargetSurface(target) : float.PositiveInfinity;
        float clusterRadius = Mathf.Max(agent.Radius * 3f, attackRange * 1.5f);
        List<AgentRuntimeData> nearby = CollectNearbyDynamicNeighbors(agent, clusterRadius);
        CombatClusterScratch.Clear();
        for (int i = 0; i < nearby.Count; i++)
        {
            AgentRuntimeData other = nearby[i];
            if (other.Id == agent.Id)
                continue;
            if (target != null && ResolveAgentId(target) == other.Id)
                continue;
            if (!CanIncludeInSpatialDiagnostics(other))
                continue;

            Vector3 delta = other.Position - agent.Position;
            delta.y = 0f;
            if (delta.magnitude > clusterRadius)
                continue;

            CombatClusterScratch.Add(other);
        }

        CombatClusterScratch.Sort((left, right) =>
        {
            float leftDist = (left.Position - agent.Position).sqrMagnitude;
            float rightDist = (right.Position - agent.Position).sqrMagnitude;
            return leftDist.CompareTo(rightDist);
        });

        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        builder.Append("[FlowCombatClusterDiag] frame=").Append(frame)
            .Append(" selfKey=").Append(agent.CharacterKey)
            .Append(" selfId=").Append(agent.Id)
            .Append(" selfPos=").Append(agent.Position)
            .Append(" target=").Append(target != null ? target.CharacterKey : "null")
            .Append(" targetPos=").Append(targetPos)
            .Append(" selfToTarget=").Append(selfToTarget.ToString("F3"))
            .Append(" attackRange=").Append(attackRange.ToString("F3"))
            .Append(" moveLockedByAttack=").Append(moveLockedByAttack)
            .Append(" moveCanRun=").Append(self.CanRun(self.MoveComp))
            .Append(" moveMode=").Append(agent.NavState.LastMovementMode)
            .Append(" intent=").Append(agent.HasNavigationIntent)
            .Append(" near=[");

        for (int i = 0; i < CombatClusterScratch.Count && i < 10; i++)
        {
            if (i > 0)
                builder.Append(" | ");

            AgentRuntimeData other = CombatClusterScratch[i];
            Vector3 delta = other.Position - agent.Position;
            delta.y = 0f;
            builder.Append("id=").Append(other.Id)
                .Append(",key=").Append(other.CharacterKey)
                .Append(",pos=").Append(other.Position)
                .Append(",dist=").Append(delta.magnitude.ToString("F3"))
                .Append(",radius=").Append(other.Radius.ToString("F3"))
                .Append(",intent=").Append(other.HasNavigationIntent)
                .Append(",moveMode=").Append(other.NavState.LastMovementMode)
                .Append(",moveComp=").Append(other.MoveCompTypeName);
        }

        if (CombatClusterScratch.Count == 0)
            builder.Append("none");
        builder.Append("]");
        Debug.LogWarning(builder.ToString());
    }

    public static void LogConstraintFailureDiagnostic(
        IEntityContext self,
        Vector3 currentPosition,
        Vector3 desiredHorizontalDisplacement,
        Vector3 inputVelocity,
        string executorReason)
    {
        if (!IsMovementDiagnosticsEnabled())
            return;
        if (self == null)
            throw new InvalidOperationException("FlowFieldCrowdMovementSystem.LogConstraintFailureDiagnostic failed: self is null.");

        int agentId = ResolveAgentId(self);
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
        {
            LogMissingConstraintAgentDiagnostic(self, agentId, currentPosition, desiredHorizontalDisplacement, inputVelocity, executorReason);
            return;
        }

        int frameCount = GetFrameCount();
        if (agent.NavState.LastConstraintDiagnosticFrame >= 0
            && frameCount - agent.NavState.LastConstraintDiagnosticFrame < 20)
        {
            return;
        }

        agent.NavState.LastConstraintDiagnosticFrame = frameCount;
        agent.Position = self.Position;
        agent.Radius = ResolveCollisionRadius(self);
        agent.NavState.LastMovementMode = self.MoveExecutor?.MovementMode ?? MovementMode.Normal;

        AgentNavState nav = agent.NavState;
        string gridEdge = BuildGridEdgeDiagnostic(currentPosition, nav.LastSteeringDesiredDirection);
        string cell = BuildAgentCellDiagnostic(agent, currentPosition);
        string executorGrid = BuildExecutorGridSegmentDiagnostic(currentPosition, desiredHorizontalDisplacement, inputVelocity);
        Debug.LogWarning(
            $"[FlowConstraintDiag] reason={executorReason} key={agent.CharacterKey} id={agent.Id} frame={frameCount} " +
            $"pos={currentPosition} agentPos={agent.Position} radius={agent.Radius:F3} agentType={agent.AgentTypeId} mode={nav.LastMovementMode} " +
            $"inputVelocity={inputVelocity} desiredDisp={desiredHorizontalDisplacement} {executorGrid} " +
            $"steerFrame={nav.LastSteeringFrame} goal={nav.LastSteeringGoal} desiredSrc={nav.LastSteeringDesiredSource} " +
            $"desiredDir={nav.LastSteeringDesiredDirection} desiredVel={nav.LastSteeringDesiredVelocity} " +
            $"los={nav.LastSteeringHasLineOfSight} baseVel={nav.LastSteeringBaseVelocity} " +
            $"resultPreClamp={nav.LastSteeringResultPreClamp} result={nav.LastSteeringResult} maxSpeed={nav.LastSteeringMaxSpeed:F3} " +
            $"fixedResult={nav.LastFixedFlowResult} fixedVelocity={nav.LastFixedFlowVelocity} {cell} {gridEdge}");
    }

}
