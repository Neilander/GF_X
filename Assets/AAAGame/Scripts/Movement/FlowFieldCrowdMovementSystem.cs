using System;
using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

public static class FlowFieldCrowdMovementSystem
{
    private const int AnyAgentTypeId = int.MinValue;
    private const float MaxAgentAvoidBackwardSpeedRatio = 0.35f;
    private const float MaxAgentAvoidLateralSpeedRatio = 0.75f;
    private const float EdgeRecoveryMinSpeedRatio = 0.28f;

    private sealed class RuntimeConfig
    {
        public float NavigationCellSize;
        public float NavigationBoundsPadding = 0.6f;
        public int SectorSizeInCells = 12;
        public int PortalNarrowWidthCells = 2;
        public int FlowTileCacheLimit = 256;
        public float CrowdPredictionTime = 0.35f;
        public float LaneBiasStrength = 0.22f;
        public float BoundaryAvoidanceWeight = 1f;
        public float BottleneckSwitchCooldown = 0.35f;
        public float BottleneckWaitTimeout = 1.25f;
        public float BottleneckInfluenceDistance = 2.2f;
        public bool DrawNavigationDebug = true;
        public bool DrawFlowFieldDebug;
        public bool StrictNoFallback = true;
    }

    private sealed class NavigationWorld
    {
        public int Version;
        public int AgentTypeId;
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public bool[] BaseWalkableMask;
        public bool[] WalkableMask;
        public Vector3[] CellNavAnchors;
        public int SectorSizeInCells;
        public int SectorCountX;
        public int SectorCountY;
        public SectorData[] Sectors;
        public PortalData[] Portals;

        public bool IsWalkable(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height && WalkableMask[x + y * Width];
        }

        public int GetIndex(int x, int y)
        {
            return x + y * Width;
        }

        public Vector3 GridToWorldCenter(int x, int y)
        {
            return new Vector3(
                Origin.x + (x + 0.5f) * CellSize,
                Origin.y,
                Origin.z + (y + 0.5f) * CellSize);
        }

        public bool WorldToGrid(Vector3 position, out int x, out int y)
        {
            Vector3 local = position - Origin;
            x = Mathf.FloorToInt(local.x / CellSize);
            y = Mathf.FloorToInt(local.z / CellSize);
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool TryGetSectorId(int cellX, int cellY, out int sectorId)
        {
            sectorId = -1;
            if (cellX < 0 || cellX >= Width || cellY < 0 || cellY >= Height)
                return false;

            int sectorX = Mathf.Clamp(cellX / SectorSizeInCells, 0, SectorCountX - 1);
            int sectorY = Mathf.Clamp(cellY / SectorSizeInCells, 0, SectorCountY - 1);
            sectorId = sectorY * SectorCountX + sectorX;
            return sectorId >= 0 && sectorId < Sectors.Length;
        }
    }

    private sealed class SectorData
    {
        public int SectorId;
        public int StartX;
        public int StartY;
        public int Width;
        public int Height;
        public Vector3 Center;
        public int DirtyVersion;
        public readonly List<int> PortalIds = new List<int>(8);
        public readonly List<PortalTransition> PortalTransitions = new List<PortalTransition>(16);
    }

    private sealed class PortalData
    {
        public int PortalId;
        public int SectorAId;
        public int SectorBId;
        public Vector2Int[] CellsA;
        public Vector2Int[] CellsB;
        public Vector3 WorldCenter;
        public int WidthCells;
        public bool IsNarrow;
        public bool IsVerticalBoundary;
    }

    private sealed class PortalTransition
    {
        public int FromPortalId;
        public int ToPortalId;
        public float Cost;
    }

    private sealed class PathHandle
    {
        public int WorldVersion;
        public int GoalX;
        public int GoalY;
        public int[] SectorIds;
        public int[] PortalIds;
        public int CurrentSectorIndex;

        public bool MatchesGoal(int goalX, int goalY)
        {
            return GoalX == goalX && GoalY == goalY;
        }
    }

    private sealed class AgentNavState
    {
        public int AgentId;
        public int CurrentSectorId = -1;
        public Vector2Int CurrentCell;
        public PathHandle PathHandle;
        public int CurrentTileKeyHash;
        public Vector3 CurrentFlowDirection;
        public Vector3 DesiredVelocity;
        public Vector3 ResolvedVelocity;
        public Vector3 LastGoalWorld;
        public bool HasGoal;
    }

    private sealed class AgentRuntimeData
    {
        public int Id;
        public string CharacterKey;
        public Vector3 Position;
        public float Radius;
        public SideType Side;
        public bool IgnoreAgentCollision;
        public bool IsLeader;
        public int GroupId = -1;
        public GroupMoveCoordinator.AgentState State = GroupMoveCoordinator.AgentState.Idle;
        public int AgentTypeId;
        public string EntityTypeName;
        public string MoveCompTypeName;
        public string RegistrationSource;
        public bool IsSyntheticRegistration;
        public readonly AgentNavState NavState = new AgentNavState();
    }

    private enum TileGoalKind
    {
        FinalGoal = 0,
        Portal = 1
    }

    private enum DesiredDirectionSource
    {
        LineOfSight = 0,
        FlowField = 1,
        TileTargetFallback = 2,
        Zero = 3
    }

    private readonly struct DesiredDirectionResolution
    {
        public readonly Vector3 Direction;
        public readonly DesiredDirectionSource Source;
        public readonly Vector3 TileTargetPosition;
        public readonly Vector2 Flow;
        public readonly bool HasLineOfSight;
        public readonly float Integration;

        public DesiredDirectionResolution(
            Vector3 direction,
            DesiredDirectionSource source,
            Vector3 tileTargetPosition,
            Vector2 flow,
            bool hasLineOfSight,
            float integration)
        {
            Direction = direction;
            Source = source;
            TileTargetPosition = tileTargetPosition;
            Flow = flow;
            HasLineOfSight = hasLineOfSight;
            Integration = integration;
        }
    }

    private readonly struct PortalTargetResolution
    {
        public readonly Vector3 TargetPosition;
        public readonly int VisibleCandidateCount;
        public readonly int SelectedPairIndex;
        public readonly bool UsedOppositeCenter;
        public readonly string CandidateSummary;

        public PortalTargetResolution(
            Vector3 targetPosition,
            int visibleCandidateCount,
            int selectedPairIndex,
            bool usedOppositeCenter,
            string candidateSummary)
        {
            TargetPosition = targetPosition;
            VisibleCandidateCount = visibleCandidateCount;
            SelectedPairIndex = selectedPairIndex;
            UsedOppositeCenter = usedOppositeCenter;
            CandidateSummary = candidateSummary;
        }
    }

    private readonly struct FlowTileCacheKey : IEquatable<FlowTileCacheKey>
    {
        public readonly int WorldVersion;
        public readonly int SectorId;
        public readonly TileGoalKind GoalKind;
        public readonly int GoalId;
        public readonly int DownstreamGoalHint;
        public readonly int AgentTypeId;
        public readonly int DirtyVersion;

        public FlowTileCacheKey(int worldVersion, int sectorId, TileGoalKind goalKind, int goalId, int downstreamGoalHint, int agentTypeId, int dirtyVersion)
        {
            WorldVersion = worldVersion;
            SectorId = sectorId;
            GoalKind = goalKind;
            GoalId = goalId;
            DownstreamGoalHint = downstreamGoalHint;
            AgentTypeId = agentTypeId;
            DirtyVersion = dirtyVersion;
        }

        public bool Equals(FlowTileCacheKey other)
        {
            return WorldVersion == other.WorldVersion
                   && SectorId == other.SectorId
                   && GoalKind == other.GoalKind
                   && GoalId == other.GoalId
                   && DownstreamGoalHint == other.DownstreamGoalHint
                   && AgentTypeId == other.AgentTypeId
                   && DirtyVersion == other.DirtyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is FlowTileCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ SectorId;
                hash = (hash * 397) ^ (int)GoalKind;
                hash = (hash * 397) ^ GoalId;
                hash = (hash * 397) ^ DownstreamGoalHint;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ DirtyVersion;
                return hash;
            }
        }
    }

    private readonly struct IntegrationSeed
    {
        public readonly Vector2Int Cell;
        public readonly float Cost;

        public IntegrationSeed(Vector2Int cell, float cost)
        {
            Cell = cell;
            Cost = cost;
        }
    }

    private sealed class FlowTileCacheEntry
    {
        public FlowTileCacheKey Key;
        public int StartX;
        public int StartY;
        public int Width;
        public int Height;
        public float[] Integration;
        public Vector2[] FlowDirections;
        public bool[] HasLineOfSight;
        public Vector2Int[] GoalCells;
        public int LastUsedFrame;

        public int GetLocalIndex(int worldX, int worldY)
        {
            return (worldX - StartX) + (worldY - StartY) * Width;
        }
    }

    private sealed class BottleneckRuntimeState
    {
        public int PortalId;
        public int CurrentDirection;
        public float SwitchBlockedUntil;
        public int OccupiedCount;
        public int LastFrameTouched = -1;
        public readonly Dictionary<int, float> WaitingStartTimes = new Dictionary<int, float>();
    }

    private sealed class TestTerrainOverride
    {
        public int AgentTypeId;
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public bool[] WalkableMask;
    }

    private sealed class CircleObstacle
    {
        public int Id;
        public Vector3 Position;
        public float Radius;
    }

    private sealed class BoxObstacle
    {
        public int Id;
        public Vector3 Center;
        public Vector3 HalfExtents;
    }

    private struct QueueNode
    {
        public int Index;
        public float Cost;
    }

    private sealed class MinHeap
    {
        private readonly List<QueueNode> _items = new List<QueueNode>(256);

        public int Count => _items.Count;

        public void Clear()
        {
            _items.Clear();
        }

        public void Push(int index, float cost)
        {
            _items.Add(new QueueNode { Index = index, Cost = cost });
            SiftUp(_items.Count - 1);
        }

        public QueueNode Pop()
        {
            QueueNode root = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            if (_items.Count > 0)
                SiftDown(0);
            return root;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_items[parent].Cost <= _items[index].Cost)
                    return;

                (_items[parent], _items[index]) = (_items[index], _items[parent]);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int best = index;
                if (left < _items.Count && _items[left].Cost < _items[best].Cost)
                    best = left;
                if (right < _items.Count && _items[right].Cost < _items[best].Cost)
                    best = right;
                if (best == index)
                    return;

                (_items[best], _items[index]) = (_items[index], _items[best]);
                index = best;
            }
        }
    }

    private readonly struct BottleneckDecision
    {
        public readonly float SpeedScale;
        public readonly Vector3 LaneBias;
        public readonly Vector3 QueueBias;

        public BottleneckDecision(float speedScale, Vector3 laneBias, Vector3 queueBias)
        {
            SpeedScale = speedScale;
            LaneBias = laneBias;
            QueueBias = queueBias;
        }
    }

    private static readonly BottleneckDecision FreeMoveDecision = new BottleneckDecision(1f, Vector3.zero, Vector3.zero);

    private static readonly RuntimeConfig Config = new RuntimeConfig();
    private static readonly Dictionary<int, AgentRuntimeData> Agents = new Dictionary<int, AgentRuntimeData>();
    private static readonly Dictionary<int, CircleObstacle> CircleObstacles = new Dictionary<int, CircleObstacle>();
    private static readonly Dictionary<int, BoxObstacle> BoxObstacles = new Dictionary<int, BoxObstacle>();
    private static readonly Dictionary<FlowTileCacheKey, FlowTileCacheEntry> FlowTileCache = new Dictionary<FlowTileCacheKey, FlowTileCacheEntry>();
    private static readonly Dictionary<int, BottleneckRuntimeState> Bottlenecks = new Dictionary<int, BottleneckRuntimeState>();
    private static readonly MinHeap OpenSet = new MinHeap();
    private static readonly Dictionary<int, float> PortalNodeGScore = new Dictionary<int, float>();
    private static readonly Dictionary<int, int> PortalNodeCameFrom = new Dictionary<int, int>();

    private static readonly int[] NeighborOffsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] NeighborOffsetY = { -1, -1, -1, 0, 0, 1, 1, 1 };
    private static readonly int[] CardinalOffsetX = { -1, 1, 0, 0 };
    private static readonly int[] CardinalOffsetY = { 0, 0, -1, 1 };

    private sealed class WorldRuntimeState
    {
        public NavigationWorld World;
        public bool IsDirty = true;
        public readonly HashSet<int> DirtyRuntimeObstacleSectors = new HashSet<int>();
    }

    private static readonly Dictionary<int, WorldRuntimeState> WorldStates = new Dictionary<int, WorldRuntimeState>();
    private static NavigationWorld _world;
    private static WorldRuntimeState _activeWorldState;
    private static int _nextWorldVersion = 1;
    private static int _lastBottleneckFrame = -1;
    private static string _lastWorldDirtyReason = "initial";
    private static TestTerrainOverride _testTerrainOverride;
    private static bool _hasTestTimeOverride;
    private static int _testFrameCount;
    private static float _testTime;

    public static void ResetAll()
    {
        Agents.Clear();
        CircleObstacles.Clear();
        BoxObstacles.Clear();
        FlowTileCache.Clear();
        Bottlenecks.Clear();
        WorldStates.Clear();
        _world = null;
        _activeWorldState = null;
        _nextWorldVersion = 1;
        _lastBottleneckFrame = -1;
    }

#if UNITY_EDITOR
    public static void SetEditorTestNavigationSource(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask)
    {
        SetEditorTestNavigationSource(AnyAgentTypeId, width, height, cellSize, origin, walkableMask);
    }

    public static void SetEditorTestNavigationSource(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] walkableMask)
    {
        if (walkableMask == null)
            throw new InvalidOperationException("SetEditorTestNavigationSource failed: walkableMask is null.");
        if (walkableMask.Length != width * height)
            throw new InvalidOperationException(
                $"SetEditorTestNavigationSource failed: mask length {walkableMask.Length} does not match {width}x{height}.");

        _testTerrainOverride = new TestTerrainOverride
        {
            AgentTypeId = agentTypeId,
            Width = width,
            Height = height,
            CellSize = cellSize,
            Origin = origin,
            WalkableMask = (bool[])walkableMask.Clone()
        };
        MarkWorldDirty();
    }

    public static void ClearEditorTestNavigationSource()
    {
        _testTerrainOverride = null;
        MarkWorldDirty();
    }

    public static void SetEditorTestClock(int frameCount, float time)
    {
        _hasTestTimeOverride = true;
        _testFrameCount = frameCount;
        _testTime = time;
    }

    public static void ClearEditorTestClock()
    {
        _hasTestTimeOverride = false;
        _testFrameCount = 0;
        _testTime = 0f;
    }
#endif

    public static void SetConfig(GroupMoveConfig config)
    {
        if (config == null)
            return;

        bool requiresRebuild = Config.SectorSizeInCells != config.SectorSizeInCells
                               || Config.PortalNarrowWidthCells != config.PortalNarrowWidthCells
                               || !Mathf.Approximately(Config.NavigationCellSize, config.NavigationCellSize)
                               || !Mathf.Approximately(Config.NavigationBoundsPadding, config.NavigationBoundsPadding);

        Config.NavigationCellSize = Mathf.Max(0f, config.NavigationCellSize);
        Config.NavigationBoundsPadding = Mathf.Max(0f, config.NavigationBoundsPadding);
        Config.SectorSizeInCells = Mathf.Max(4, config.SectorSizeInCells);
        Config.PortalNarrowWidthCells = Mathf.Max(1, config.PortalNarrowWidthCells);
        Config.FlowTileCacheLimit = Mathf.Max(16, config.FlowTileCacheLimit);
        Config.CrowdPredictionTime = Mathf.Max(0.05f, config.CrowdPredictionTime);
        Config.LaneBiasStrength = Mathf.Max(0f, config.LaneBiasStrength);
        Config.BoundaryAvoidanceWeight = Mathf.Max(0f, config.BoundaryAvoidanceWeight);
        Config.BottleneckSwitchCooldown = Mathf.Max(0f, config.BottleneckSwitchCooldown);
        Config.BottleneckWaitTimeout = Mathf.Max(0.1f, config.BottleneckWaitTimeout);
        Config.BottleneckInfluenceDistance = Mathf.Max(0.1f, config.BottleneckInfluenceDistance);
        Config.DrawNavigationDebug = config.DrawNavigationDebug;
        Config.DrawFlowFieldDebug = config.DrawFlowFieldDebug;
        Config.StrictNoFallback = true;

        if (requiresRebuild)
            MarkWorldDirty();
    }

    public static void MarkWorldDirty(string reason = null)
    {
        _lastWorldDirtyReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;

        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            state.IsDirty = true;
            state.DirtyRuntimeObstacleSectors.Clear();
        }

        FlowTileCache.Clear();
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
            pair.Value.NavState.PathHandle = null;

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            Debug.Log($"[FlowWorld] MarkWorldDirty reason={_lastWorldDirtyReason} states={WorldStates.Count} agents={Agents.Count}");
    }

    private static void MarkRuntimeObstacleDirty(Bounds bounds)
    {
        if (WorldStates.Count == 0)
        {
            MarkWorldDirty();
            return;
        }

        foreach (KeyValuePair<int, WorldRuntimeState> pair in WorldStates)
        {
            WorldRuntimeState state = pair.Value;
            if (state.World == null || state.IsDirty)
                continue;

            CollectDirtySectors(state.World, bounds, state.DirtyRuntimeObstacleSectors, includeNeighbors: true);
        }
    }

    public static void RegisterAgent(MAEntity entity, bool isLeader, float radius)
    {
        if (entity == null)
            return;

        int id = entity.GetInstanceID();
        if (!Agents.TryGetValue(id, out AgentRuntimeData agent))
        {
            agent = new AgentRuntimeData
            {
                Id = id
            };
            agent.NavState.AgentId = id;
            Agents.Add(id, agent);
        }

        agent.CharacterKey = entity.CharacterKey;
        agent.Position = entity.Position;
        agent.Radius = Mathf.Max(0.05f, radius);
        agent.Side = entity.Side;
        agent.IsLeader = isLeader;
        agent.AgentTypeId = entity.navAgentTypeID;
        agent.EntityTypeName = entity.GetType().Name;
        agent.MoveCompTypeName = entity.moveComp?.GetType().Name ?? "null";
        agent.RegistrationSource = "RegisterAgent";
        agent.IsSyntheticRegistration = false;
    }

    public static void UnregisterAgent(int agentId)
    {
        Agents.Remove(agentId);
    }

    public static void UpdateAgent(MAEntity entity, float radius)
    {
        if (entity == null)
            return;

        int id = entity.GetInstanceID();
        if (!Agents.TryGetValue(id, out AgentRuntimeData agent))
        {
            RegisterAgent(entity, false, radius);
            return;
        }

        agent.CharacterKey = entity.CharacterKey;
        agent.Position = entity.Position;
        agent.Radius = Mathf.Max(0.05f, radius);
        agent.Side = entity.Side;
        agent.AgentTypeId = entity.navAgentTypeID;
        agent.EntityTypeName = entity.GetType().Name;
        agent.MoveCompTypeName = entity.moveComp?.GetType().Name ?? "null";
        if (string.IsNullOrEmpty(agent.RegistrationSource))
            agent.RegistrationSource = "UpdateAgent";
    }

    public static void SetAgentSide(int agentId, SideType side)
    {
        if (Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            agent.Side = side;
    }

    public static void SetAgentLeader(int agentId, bool isLeader)
    {
        if (Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            agent.IsLeader = isLeader;
    }

    public static void SetAgentGroup(int agentId, int groupId)
    {
        if (Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            agent.GroupId = groupId;
    }

    public static void SetAgentState(int agentId, GroupMoveCoordinator.AgentState state)
    {
        if (Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            agent.State = state;
    }

    public static void SetAgentIgnoreCollision(int agentId, bool ignore)
    {
        if (Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            agent.IgnoreAgentCollision = ignore;
    }

    public static void RegisterCircleObstacle(int obstacleId, Vector3 position, float radius)
    {
        float clampedRadius = Mathf.Max(0.01f, radius);
        CircleObstacles[obstacleId] = new CircleObstacle
        {
            Id = obstacleId,
            Position = position,
            Radius = clampedRadius
        };
        MarkRuntimeObstacleDirty(new Bounds(position, new Vector3(clampedRadius * 2f, 0f, clampedRadius * 2f)));
    }

    public static void RegisterBoxObstacle(int obstacleId, Vector3 center, Vector3 halfExtents)
    {
        BoxObstacles[obstacleId] = new BoxObstacle
        {
            Id = obstacleId,
            Center = center,
            HalfExtents = halfExtents
        };
        MarkRuntimeObstacleDirty(new Bounds(center, halfExtents * 2f));
    }

    public static void UnregisterObstacle(int obstacleId)
    {
        if (CircleObstacles.TryGetValue(obstacleId, out CircleObstacle circle))
        {
            CircleObstacles.Remove(obstacleId);
            MarkRuntimeObstacleDirty(new Bounds(circle.Position, new Vector3(circle.Radius * 2f, 0f, circle.Radius * 2f)));
            return;
        }

        if (BoxObstacles.TryGetValue(obstacleId, out BoxObstacle box))
        {
            BoxObstacles.Remove(obstacleId);
            MarkRuntimeObstacleDirty(new Bounds(box.Center, box.HalfExtents * 2f));
        }
    }

    public static bool IsPositionOccupiedByAgent(Vector3 position, float requiredDistance)
    {
        float requiredDistanceSq = requiredDistance * requiredDistance;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData agent = pair.Value;
            if (agent.IgnoreAgentCollision)
                continue;

            Vector3 offset = agent.Position - position;
            offset.y = 0f;
            if (offset.sqrMagnitude < requiredDistanceSq)
                return true;
        }

        return false;
    }

    public static bool TryGetSteeringVelocity(IEntityContext self, Vector3 goalPosition, float maxSpeed, out Vector3 velocity)
    {
        if (self == null)
            throw new InvalidOperationException("FlowFieldCrowdMovementSystem.TryGetSteeringVelocity failed: self is null.");

        if (maxSpeed <= 0.0001f)
        {
            velocity = Vector3.zero;
            return true;
        }

        int selfId = ResolveAgentId(self);
        bool debugMove = GameDebugSettings.IsEnabled(DebugCategory.Move);
        EnsureBottleneckFrame();

        if (!Agents.TryGetValue(selfId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(self);
            agent = Agents[selfId];
        }
        else
        {
            agent.Position = self.Position;
            agent.Radius = ResolveCollisionRadius(self);
        }

        int preferredAgentTypeId = agent.AgentTypeId;
        if (!TryEnsureWorldBuilt(preferredAgentTypeId))
        {
            return FailNoFallback(self, "world unavailable", goalPosition, out velocity);
        }

        if (!_world.WorldToGrid(self.Position, out int startX, out int startY))
        {
            return FailNoFallback(self, $"start not on grid pos={self.Position}", goalPosition, out velocity);
        }

        if (!_world.IsWalkable(startX, startY)
            && !TryFindNearestWalkable(_world, startX, startY, 3, out startX, out startY))
        {
            return FailNoFallback(self, $"start blocked and no nearby walkable originalPos={self.Position} | {BuildStartCellDiagnostics(self, self.Position)}", goalPosition, out velocity);
        }

        if (!TryResolveGoalCell(_world, goalPosition, out int goalX, out int goalY))
        {
            return FailNoFallback(self, BuildGoalResolutionFailure(self, goalPosition), goalPosition, out velocity);
        }

        if (!_world.TryGetSectorId(startX, startY, out int startSectorId)
            || !_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
        {
            return FailNoFallback(self, $"sector resolve failed start=({startX},{startY}) goal=({goalX},{goalY})", goalPosition, out velocity);
        }

        agent.NavState.CurrentCell = new Vector2Int(startX, startY);
        agent.NavState.CurrentSectorId = startSectorId;

        if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY))
        {
            return FailNoFallback(self, $"path handle build failed startSector={startSectorId} goalSector={goalSectorId}", goalPosition, out velocity);
        }

        if (!TryAdvancePathToCurrentSector(agent, startSectorId))
        {
            agent.NavState.PathHandle = null;
            if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY))
            {
                return FailNoFallback(self, $"path handle rebuild failed startSector={startSectorId} goalSector={goalSectorId}", goalPosition, out velocity);
            }
        }

        if (!TryBuildOrGetTile(agent, goalX, goalY, out FlowTileCacheEntry tile, out TileGoalKind goalKind, out int downstreamPortalId))
        {
            return FailNoFallback(self, $"tile build failed sector={startSectorId} goal=({goalX},{goalY})", goalPosition, out velocity);
        }

        if (!IsCellReachableInTile(tile, startX, startY))
        {
            agent.NavState.PathHandle = null;
            if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY)
                || !TryBuildOrGetTile(agent, goalX, goalY, out tile, out goalKind, out downstreamPortalId)
                || !IsCellReachableInTile(tile, startX, startY))
            {
                return FailNoFallback(self, $"tile reachability failed start=({startX},{startY}) sector={startSectorId}", goalPosition, out velocity);
            }
        }

        DesiredDirectionResolution desiredResolution = ResolveDesiredDirection(self.CharacterKey, tile, startX, startY, goalPosition, goalKind);
        Vector3 desiredDirection = desiredResolution.Direction;
        Vector3 desiredVelocity = desiredDirection * maxSpeed;

        BottleneckDecision bottleneckDecision = FreeMoveDecision;
        if (goalKind == TileGoalKind.Portal && downstreamPortalId >= 0)
            bottleneckDecision = EvaluateBottleneck(agent, downstreamPortalId, startSectorId, desiredDirection);

        velocity = ResolveCrowdSteering(self, agent, goalPosition, desiredVelocity, desiredResolution, tile, bottleneckDecision);
        UpdateResolvedVelocity(selfId, goalPosition, desiredVelocity, velocity);

        if (debugMove)
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[{self.CharacterKey}] Flow steer start=({startX},{startY}) goal=({goalX},{goalY}) " +
                $"sector={startSectorId}->{goalSectorId} goalKind={goalKind} portal={downstreamPortalId} " +
                $"desiredDir={desiredDirection} desiredSrc={desiredResolution.Source} flow={desiredResolution.Flow} " +
                $"los={desiredResolution.HasLineOfSight} tileTarget={desiredResolution.TileTargetPosition} " +
                $"desiredVel={desiredVelocity} resolvedVel={velocity} " +
                $"pathSectorIndex={agent.NavState.PathHandle?.CurrentSectorIndex ?? -1}");
        }
        return true;
    }

    private static bool FailNoFallback(IEntityContext self, string reason, Vector3 goalPosition, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        UpdateResolvedVelocity(ResolveAgentId(self), goalPosition, Vector3.zero, velocity);
        string message = $"[{self.CharacterKey}] Flow strict fail: {reason}";
        GameDebugSettings.Log(DebugCategory.Move, message);
        Debug.LogError(message);
        if (Config.StrictNoFallback)
            throw new InvalidOperationException(message);
        return true;
    }

    public static void DrawGizmos()
    {
        if (!Config.DrawNavigationDebug || _world == null)
            return;

        if (_world.Sectors != null)
        {
            Gizmos.color = new Color(0.15f, 0.7f, 0.9f, 0.7f);
            for (int i = 0; i < _world.Sectors.Length; i++)
            {
                SectorData sector = _world.Sectors[i];
                Vector3 size = new Vector3(sector.Width * _world.CellSize, 0.02f, sector.Height * _world.CellSize);
                Gizmos.DrawWireCube(
                    _world.Origin + new Vector3((sector.StartX + sector.Width * 0.5f) * _world.CellSize, 0f, (sector.StartY + sector.Height * 0.5f) * _world.CellSize),
                    size);
            }
        }

        if (_world.Portals != null)
        {
            for (int i = 0; i < _world.Portals.Length; i++)
            {
                PortalData portal = _world.Portals[i];
                Gizmos.color = portal.IsNarrow ? Color.red : Color.green;
                Gizmos.DrawSphere(portal.WorldCenter + Vector3.up * 0.05f, _world.CellSize * 0.18f);
            }
        }

        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData agent = pair.Value;
            if (agent.NavState.PathHandle == null || agent.NavState.PathHandle.SectorIds == null)
                continue;

            Gizmos.color = agent.IgnoreAgentCollision ? Color.gray : Color.yellow;
            Gizmos.DrawLine(agent.Position + Vector3.up * 0.1f, agent.Position + Vector3.up * 0.1f + agent.NavState.CurrentFlowDirection);

            int[] portals = agent.NavState.PathHandle.PortalIds;
            for (int i = agent.NavState.PathHandle.CurrentSectorIndex; i < portals.Length; i++)
            {
                if (portals[i] < 0 || portals[i] >= _world.Portals.Length)
                    continue;

                Vector3 center = _world.Portals[portals[i]].WorldCenter + Vector3.up * 0.05f;
                Gizmos.DrawSphere(center, _world.CellSize * 0.1f);
            }
        }
    }

    private static int GetFrameCount()
    {
        return _hasTestTimeOverride ? _testFrameCount : Time.frameCount;
    }

    private static float GetTime()
    {
        return _hasTestTimeOverride ? _testTime : Time.time;
    }

    private static void RegisterSyntheticAgent(IEntityContext self)
    {
        int agentId = ResolveAgentId(self);
        Agents[agentId] = new AgentRuntimeData
        {
            Id = agentId,
            CharacterKey = self.CharacterKey,
            Position = self.Position,
            Radius = ResolveCollisionRadius(self),
            Side = self.Side,
            AgentTypeId = self is MAEntity ma ? ma.navAgentTypeID : 0,
            EntityTypeName = self.GetType().Name,
            MoveCompTypeName = self.MoveComp?.GetType().Name ?? "null",
            RegistrationSource = "RegisterSyntheticAgent",
            IsSyntheticRegistration = true
        };
        Agents[agentId].NavState.AgentId = agentId;
    }

    private static bool ShouldParticipateInDynamicAvoidance(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("ShouldParticipateInDynamicAvoidance failed: agent is null.");

        if (agent.IgnoreAgentCollision)
            return false;

        return agent.MoveCompTypeName != nameof(NoMoveComp);
    }

    private static bool ShouldAvoidAsDynamicNeighbor(IEntityContext selfContext, AgentRuntimeData selfAgent, AgentRuntimeData otherAgent)
    {
        if (selfContext == null)
            throw new InvalidOperationException("ShouldAvoidAsDynamicNeighbor failed: selfContext is null.");
        if (selfAgent == null)
            throw new InvalidOperationException("ShouldAvoidAsDynamicNeighbor failed: selfAgent is null.");
        if (otherAgent == null)
            throw new InvalidOperationException("ShouldAvoidAsDynamicNeighbor failed: otherAgent is null.");

        if (!ShouldParticipateInDynamicAvoidance(selfAgent))
            return false;
        if (!ShouldParticipateInDynamicAvoidance(otherAgent))
            return false;

        IEntityContext currentTarget = selfContext.TargetComp?.CurrentTarget;
        if (currentTarget != null && ResolveAgentId(currentTarget) == otherAgent.Id)
            return false;

        IEntityContext followTarget = selfContext.TargetComp?.FollowTarget;
        if (followTarget != null && ResolveAgentId(followTarget) == otherAgent.Id)
            return false;

        return true;
    }

    private static bool TryEnsureWorldBuilt(int preferredAgentTypeId)
    {
        int agentTypeId = ResolvePreferredAgentTypeId(preferredAgentTypeId);
        WorldRuntimeState state = GetOrCreateWorldState(agentTypeId);
        _activeWorldState = state;
        _world = state.World;

        if (!state.IsDirty && state.World != null)
        {
            if (state.DirtyRuntimeObstacleSectors.Count > 0)
                ApplyRuntimeObstacleDirty(state);

            _world = state.World;
            return true;
        }

        if (!TryResolveTerrainSource(agentTypeId, out int width, out int height, out float cellSize, out Vector3 origin, out bool[] walkableMask))
            return false;

        bool[] runtimeWalkableMask = (bool[])walkableMask.Clone();
        ApplyRuntimeHardObstacles(runtimeWalkableMask, width, height, cellSize, origin);

        state.World = BuildWorld(agentTypeId, width, height, cellSize, origin, walkableMask, runtimeWalkableMask);
        state.World.Version = _nextWorldVersion++;
        state.IsDirty = false;
        state.DirtyRuntimeObstacleSectors.Clear();
        FlowTileCache.Clear();
        _world = state.World;

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            int walkableCount = 0;
            for (int i = 0; i < runtimeWalkableMask.Length; i++)
            {
                if (runtimeWalkableMask[i])
                    walkableCount++;
            }

            Debug.Log(
                $"[FlowWorld] Rebuilt worldVersion={state.World.Version} agentType={agentTypeId} size={width}x{height} cellSize={cellSize:F3} " +
                $"origin={origin} walkable={walkableCount}/{runtimeWalkableMask.Length} dirtyReason={_lastWorldDirtyReason}");
        }
        return true;
    }

    private static WorldRuntimeState GetOrCreateWorldState(int agentTypeId)
    {
        if (!WorldStates.TryGetValue(agentTypeId, out WorldRuntimeState state))
        {
            state = new WorldRuntimeState();
            WorldStates.Add(agentTypeId, state);
        }

        return state;
    }

    private static bool TryResolveTerrainSource(int agentTypeId, out int width, out int height, out float cellSize, out Vector3 origin, out bool[] walkableMask)
    {
        width = 0;
        height = 0;
        cellSize = 1f;
        origin = Vector3.zero;
        walkableMask = null;

        if (_testTerrainOverride != null)
        {
            if (_testTerrainOverride.AgentTypeId != AnyAgentTypeId && _testTerrainOverride.AgentTypeId != agentTypeId)
            {
                walkableMask = null;
                return false;
            }

            width = _testTerrainOverride.Width;
            height = _testTerrainOverride.Height;
            cellSize = _testTerrainOverride.CellSize;
            origin = _testTerrainOverride.Origin;
            walkableMask = (bool[])_testTerrainOverride.WalkableMask.Clone();
            return true;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.indices == null || triangulation.indices.Length == 0)
            return false;

        float agentRadius = ResolveAgentTypeRadius(agentTypeId);
        cellSize = Config.NavigationCellSize > 0.0001f
            ? Config.NavigationCellSize
            : Mathf.Max(0.12f, agentRadius * 0.75f);

        Bounds navBounds = CalculateNavMeshBounds(triangulation);
        float padding = Mathf.Max(Config.NavigationBoundsPadding, agentRadius + cellSize);
        navBounds.Expand(new Vector3(padding * 2f, 0f, padding * 2f));

        origin = new Vector3(navBounds.min.x, 0f, navBounds.min.z);
        width = Mathf.Max(1, Mathf.CeilToInt(navBounds.size.x / cellSize));
        height = Mathf.Max(1, Mathf.CeilToInt(navBounds.size.z / cellSize));
        walkableMask = new bool[width * height];

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float sampleRadius = ResolveWalkableRasterSampleRadius(cellSize, agentRadius);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                walkableMask[x + y * width] = TrySampleCellWalkable(origin, cellSize, x, y, sampleRadius, filter);
            }
        }

        return true;
    }

    private static bool TrySampleCellWalkable(Vector3 origin, float cellSize, int x, int y, float sampleRadius, NavMeshQueryFilter filter)
    {
        Vector3 center = new Vector3(origin.x + (x + 0.5f) * cellSize, origin.y, origin.z + (y + 0.5f) * cellSize);
        if (!NavMesh.SamplePosition(center, out NavMeshHit navHit, sampleRadius, filter))
            return false;

        Vector2 centerXZ = new Vector2(center.x, center.z);
        Vector2 navXZ = new Vector2(navHit.position.x, navHit.position.z);
        float maxAnchorOffset = Mathf.Max(0.08f, cellSize * 0.45f);
        return Vector2.Distance(centerXZ, navXZ) <= maxAnchorOffset;
    }

    private static float ResolveWalkableRasterSampleRadius(float cellSize, float agentRadius)
    {
        float cellLimited = cellSize * 0.75f;
        float agentLimited = agentRadius > 0.0001f ? agentRadius * 0.5f : cellLimited;
        return Mathf.Max(0.2f, cellLimited, agentLimited);
    }

    private static string BuildCellProbeDiagnostics(Vector3 origin, float cellSize, int x, int y, float sampleRadius, NavMeshQueryFilter filter)
    {
        Vector3 center = new Vector3(origin.x + (x + 0.5f) * cellSize, origin.y, origin.z + (y + 0.5f) * cellSize);
        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        bool hit = NavMesh.SamplePosition(center, out NavMeshHit navHit, sampleRadius, filter);
        builder.Append("centerProbe={p=");
        builder.Append(center);
        builder.Append(" hit=");
        builder.Append(hit);
        if (hit)
        {
            builder.Append(" nav=");
            builder.Append(navHit.position);
            builder.Append(" dist=");
            builder.Append(Vector3.Distance(center, navHit.position).ToString("F3"));
        }
        builder.Append("}");
        return builder.ToString();
    }

    private static string BuildWalkableNeighborhoodDiagnostics(NavigationWorld world, int centerX, int centerY, int radius, NavMeshQueryFilter filter, float sampleRadius)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("neighborhood=[");
        bool first = true;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                    continue;

                if (!first)
                    builder.Append("; ");
                first = false;

                int index = world.GetIndex(x, y);
                Vector3 cellCenter = world.GridToWorldCenter(x, y);
                bool centerNavHit = NavMesh.SamplePosition(cellCenter, out NavMeshHit navHit, sampleRadius, filter);
                builder.Append("(");
                builder.Append(x);
                builder.Append(",");
                builder.Append(y);
                builder.Append(")");
                builder.Append("{walk=");
                builder.Append(world.WalkableMask[index]);
                builder.Append(",base=");
                builder.Append(world.BaseWalkableMask[index]);
                builder.Append(",center=");
                builder.Append(cellCenter);
                builder.Append(",navHit=");
                builder.Append(centerNavHit);
                if (centerNavHit)
                {
                    builder.Append(",nav=");
                    builder.Append(navHit.position);
                }
                builder.Append("}");
            }
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static string BuildNearestWalkableSearchDiagnostics(NavigationWorld world, int startX, int startY, int radius)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        builder.Append("nearestSearch=[");
        bool first = true;
        for (int r = 1; r <= radius; r++)
        {
            int foundCount = 0;
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    int nx = startX + x;
                    int ny = startY + y;
                    if (!world.IsWalkable(nx, ny))
                        continue;

                    foundCount++;
                }
            }

            if (!first)
                builder.Append("; ");
            first = false;
            builder.Append("r=");
            builder.Append(r);
            builder.Append(" found=");
            builder.Append(foundCount);
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static void ApplyRuntimeHardObstacles(bool[] walkableMask, int width, int height, float cellSize, Vector3 origin)
    {
        foreach (CircleObstacle circle in CircleObstacles.Values)
            BlockCellsByCircle(walkableMask, width, height, cellSize, origin, circle.Position, circle.Radius);

        foreach (BoxObstacle box in BoxObstacles.Values)
            BlockCellsByBounds(walkableMask, width, height, cellSize, origin, new Bounds(box.Center, box.HalfExtents * 2f));
    }

    private static void BlockCellsByCircle(bool[] walkableMask, int width, int height, float cellSize, Vector3 origin, Vector3 center, float radius)
    {
        Bounds bounds = new Bounds(center, new Vector3(radius * 2f, 0f, radius * 2f));
        int minX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - origin.x) / cellSize), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - origin.x) / cellSize), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - origin.z) / cellSize), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - origin.z) / cellSize), 0, height - 1);
        float blockRadius = radius + cellSize * 0.45f;
        float blockRadiusSq = blockRadius * blockRadius;

        Vector2 centerXZ = new Vector2(center.x, center.z);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 cellCenter = new Vector2(origin.x + (x + 0.5f) * cellSize, origin.z + (y + 0.5f) * cellSize);
                if ((cellCenter - centerXZ).sqrMagnitude <= blockRadiusSq)
                    walkableMask[x + y * width] = false;
            }
        }
    }

    private static void BlockCellsByBounds(bool[] walkableMask, int width, int height, float cellSize, Vector3 origin, Bounds bounds)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - origin.x) / cellSize), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - origin.x) / cellSize), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - origin.z) / cellSize), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - origin.z) / cellSize), 0, height - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
                walkableMask[x + y * width] = false;
        }
    }

    private static NavigationWorld BuildWorld(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] baseWalkableMask, bool[] walkableMask)
    {
        NavigationWorld world = new NavigationWorld
        {
            AgentTypeId = agentTypeId,
            Width = width,
            Height = height,
            CellSize = cellSize,
            Origin = origin,
            BaseWalkableMask = baseWalkableMask,
            WalkableMask = walkableMask,
            CellNavAnchors = new Vector3[width * height],
            SectorSizeInCells = Config.SectorSizeInCells
        };

        BuildCellNavAnchors(world);

        world.SectorCountX = Mathf.CeilToInt((float)width / world.SectorSizeInCells);
        world.SectorCountY = Mathf.CeilToInt((float)height / world.SectorSizeInCells);
        world.Sectors = new SectorData[world.SectorCountX * world.SectorCountY];

        for (int sectorY = 0; sectorY < world.SectorCountY; sectorY++)
        {
            for (int sectorX = 0; sectorX < world.SectorCountX; sectorX++)
            {
                int sectorId = sectorY * world.SectorCountX + sectorX;
                int startX = sectorX * world.SectorSizeInCells;
                int startY = sectorY * world.SectorSizeInCells;
                int sectorWidth = Mathf.Min(world.SectorSizeInCells, width - startX);
                int sectorHeight = Mathf.Min(world.SectorSizeInCells, height - startY);

                world.Sectors[sectorId] = new SectorData
                {
                    SectorId = sectorId,
                    StartX = startX,
                    StartY = startY,
                    Width = sectorWidth,
                    Height = sectorHeight,
                    DirtyVersion = 1,
                    Center = origin + new Vector3(
                        (startX + sectorWidth * 0.5f) * cellSize,
                        0f,
                        (startY + sectorHeight * 0.5f) * cellSize)
                };
            }
        }

        RebuildPortalsAndTransitions(world);
        return world;
    }

    private static void RebuildPortalsAndTransitions(NavigationWorld world)
    {
        for (int i = 0; i < world.Sectors.Length; i++)
        {
            world.Sectors[i].PortalIds.Clear();
            world.Sectors[i].PortalTransitions.Clear();
        }

        List<PortalData> portals = new List<PortalData>();
        BuildVerticalPortals(world, portals);
        BuildHorizontalPortals(world, portals);
        world.Portals = portals.ToArray();

        for (int i = 0; i < world.Portals.Length; i++)
        {
            PortalData portal = world.Portals[i];
            world.Sectors[portal.SectorAId].PortalIds.Add(portal.PortalId);
            world.Sectors[portal.SectorBId].PortalIds.Add(portal.PortalId);
        }

        BuildSectorPortalTransitions(world);
    }

    private static void ApplyRuntimeObstacleDirty(WorldRuntimeState state)
    {
        if (state == null || state.World == null || state.DirtyRuntimeObstacleSectors.Count == 0)
            return;

        NavigationWorld world = state.World;
        foreach (int sectorId in state.DirtyRuntimeObstacleSectors)
        {
            ResetSectorWalkableFromBase(world, world.Sectors[sectorId]);
            world.Sectors[sectorId].DirtyVersion++;
        }

        foreach (CircleObstacle circle in CircleObstacles.Values)
            BlockCellsByCircle(world.WalkableMask, world.Width, world.Height, world.CellSize, world.Origin, circle.Position, circle.Radius);

        foreach (BoxObstacle box in BoxObstacles.Values)
            BlockCellsByBounds(world.WalkableMask, world.Width, world.Height, world.CellSize, world.Origin, new Bounds(box.Center, box.HalfExtents * 2f));

        RebuildPortalsAndTransitions(world);
        world.Version = _nextWorldVersion++;
        state.DirtyRuntimeObstacleSectors.Clear();
        FlowTileCache.Clear();
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
            pair.Value.NavState.PathHandle = null;

        _world = world;
    }

    private static void ResetSectorWalkableFromBase(NavigationWorld world, SectorData sector)
    {
        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            int rowStart = y * world.Width;
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = rowStart + x;
                world.WalkableMask[index] = world.BaseWalkableMask[index];
            }
        }
    }

    private static void CollectDirtySectors(NavigationWorld world, Bounds bounds, HashSet<int> dirtySectors, bool includeNeighbors)
    {
        if (world == null)
            return;

        int minCellX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - world.Origin.x) / world.CellSize), 0, world.Width - 1);
        int maxCellX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - world.Origin.x) / world.CellSize), 0, world.Width - 1);
        int minCellY = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - world.Origin.z) / world.CellSize), 0, world.Height - 1);
        int maxCellY = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - world.Origin.z) / world.CellSize), 0, world.Height - 1);

        int minSectorX = Mathf.Clamp(minCellX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
        int maxSectorX = Mathf.Clamp(maxCellX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
        int minSectorY = Mathf.Clamp(minCellY / world.SectorSizeInCells, 0, world.SectorCountY - 1);
        int maxSectorY = Mathf.Clamp(maxCellY / world.SectorSizeInCells, 0, world.SectorCountY - 1);

        int padding = includeNeighbors ? 1 : 0;
        for (int sectorY = Mathf.Max(0, minSectorY - padding); sectorY <= Mathf.Min(world.SectorCountY - 1, maxSectorY + padding); sectorY++)
        {
            for (int sectorX = Mathf.Max(0, minSectorX - padding); sectorX <= Mathf.Min(world.SectorCountX - 1, maxSectorX + padding); sectorX++)
            {
                dirtySectors.Add(sectorY * world.SectorCountX + sectorX);
            }
        }
    }

    private static void BuildVerticalPortals(NavigationWorld world, List<PortalData> portals)
    {
        for (int sectorY = 0; sectorY < world.SectorCountY; sectorY++)
        {
            for (int sectorX = 0; sectorX < world.SectorCountX - 1; sectorX++)
            {
                SectorData sectorA = world.Sectors[sectorY * world.SectorCountX + sectorX];
                SectorData sectorB = world.Sectors[sectorY * world.SectorCountX + sectorX + 1];
                int boundaryAX = sectorA.StartX + sectorA.Width - 1;
                int boundaryBX = sectorB.StartX;

                List<Vector2Int> cellsA = null;
                List<Vector2Int> cellsB = null;
                for (int y = Mathf.Max(sectorA.StartY, sectorB.StartY); y < Mathf.Min(sectorA.StartY + sectorA.Height, sectorB.StartY + sectorB.Height); y++)
                {
                    bool passable = CanTraverseNeighborCells(world, boundaryAX, y, boundaryBX, y);
                    if (passable)
                    {
                        cellsA ??= new List<Vector2Int>(8);
                        cellsB ??= new List<Vector2Int>(8);
                        cellsA.Add(new Vector2Int(boundaryAX, y));
                        cellsB.Add(new Vector2Int(boundaryBX, y));
                        continue;
                    }

                    FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, true);
                    cellsA = null;
                    cellsB = null;
                }

                FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, true);
            }
        }
    }

    private static void BuildHorizontalPortals(NavigationWorld world, List<PortalData> portals)
    {
        for (int sectorY = 0; sectorY < world.SectorCountY - 1; sectorY++)
        {
            for (int sectorX = 0; sectorX < world.SectorCountX; sectorX++)
            {
                SectorData sectorA = world.Sectors[sectorY * world.SectorCountX + sectorX];
                SectorData sectorB = world.Sectors[(sectorY + 1) * world.SectorCountX + sectorX];
                int boundaryAY = sectorA.StartY + sectorA.Height - 1;
                int boundaryBY = sectorB.StartY;

                List<Vector2Int> cellsA = null;
                List<Vector2Int> cellsB = null;
                for (int x = Mathf.Max(sectorA.StartX, sectorB.StartX); x < Mathf.Min(sectorA.StartX + sectorA.Width, sectorB.StartX + sectorB.Width); x++)
                {
                    bool passable = CanTraverseNeighborCells(world, x, boundaryAY, x, boundaryBY);
                    if (passable)
                    {
                        cellsA ??= new List<Vector2Int>(8);
                        cellsB ??= new List<Vector2Int>(8);
                        cellsA.Add(new Vector2Int(x, boundaryAY));
                        cellsB.Add(new Vector2Int(x, boundaryBY));
                        continue;
                    }

                    FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, false);
                    cellsA = null;
                    cellsB = null;
                }

                FlushPortalRun(world, portals, sectorA.SectorId, sectorB.SectorId, cellsA, cellsB, false);
            }
        }
    }

    private static void FlushPortalRun(
        NavigationWorld world,
        List<PortalData> portals,
        int sectorAId,
        int sectorBId,
        List<Vector2Int> cellsA,
        List<Vector2Int> cellsB,
        bool isVerticalBoundary)
    {
        if (cellsA == null || cellsB == null || cellsA.Count == 0 || cellsB.Count == 0)
            return;

        Vector3 worldA = world.GridToWorldCenter(cellsA[0].x, cellsA[0].y);
        Vector3 worldB = world.GridToWorldCenter(cellsB[cellsB.Count - 1].x, cellsB[cellsB.Count - 1].y);
        Vector3 center = (worldA + worldB) * 0.5f;

        PortalData portal = new PortalData
        {
            PortalId = portals.Count,
            SectorAId = sectorAId,
            SectorBId = sectorBId,
            CellsA = cellsA.ToArray(),
            CellsB = cellsB.ToArray(),
            WorldCenter = center,
            WidthCells = cellsA.Count,
            IsNarrow = cellsA.Count <= Config.PortalNarrowWidthCells,
            IsVerticalBoundary = isVerticalBoundary
        };
        portals.Add(portal);
    }

    private static void BuildSectorPortalTransitions(NavigationWorld world)
    {
        for (int sectorIndex = 0; sectorIndex < world.Sectors.Length; sectorIndex++)
        {
            SectorData sector = world.Sectors[sectorIndex];
            sector.PortalTransitions.Clear();
            if (sector.PortalIds.Count <= 1)
                continue;

            for (int fromIndex = 0; fromIndex < sector.PortalIds.Count; fromIndex++)
            {
                int fromPortalId = sector.PortalIds[fromIndex];
                Vector2Int[] fromCells = GetPortalCellsForSector(world.Portals[fromPortalId], sector.SectorId);
                float[] integration = BuildSectorIntegrationField(world, sector, fromCells);
                for (int toIndex = 0; toIndex < sector.PortalIds.Count; toIndex++)
                {
                    int toPortalId = sector.PortalIds[toIndex];
                    if (toPortalId == fromPortalId)
                        continue;

                    Vector2Int[] toCells = GetPortalCellsForSector(world.Portals[toPortalId], sector.SectorId);
                    float traversalCost = ResolveMinimumIntegrationCost(sector, integration, toCells);
                    if (float.IsPositiveInfinity(traversalCost))
                        continue;

                    sector.PortalTransitions.Add(new PortalTransition
                    {
                        FromPortalId = fromPortalId,
                        ToPortalId = toPortalId,
                        Cost = traversalCost
                    });
                }
            }
        }
    }

    private static Vector2Int[] GetPortalCellsForSector(PortalData portal, int sectorId)
    {
        if (portal.SectorAId == sectorId)
            return portal.CellsA;
        if (portal.SectorBId == sectorId)
            return portal.CellsB;

        throw new InvalidOperationException($"GetPortalCellsForSector failed: sector {sectorId} is not connected to portal {portal.PortalId}.");
    }

    private static int GetOppositeSectorId(PortalData portal, int sectorId)
    {
        if (portal.SectorAId == sectorId)
            return portal.SectorBId;
        if (portal.SectorBId == sectorId)
            return portal.SectorAId;

        throw new InvalidOperationException($"GetOppositeSectorId failed: sector {sectorId} is not connected to portal {portal.PortalId}.");
    }

    private static float[] BuildSectorIntegrationField(NavigationWorld world, SectorData sector, Vector2Int[] goalCells)
    {
        if (goalCells == null || goalCells.Length == 0)
            throw new InvalidOperationException($"BuildSectorIntegrationField failed: sector {sector.SectorId} has no goal cells.");

        IntegrationSeed[] seeds = new IntegrationSeed[goalCells.Length];
        for (int i = 0; i < goalCells.Length; i++)
            seeds[i] = new IntegrationSeed(goalCells[i], 0f);
        return BuildSectorIntegrationField(world, sector, seeds);
    }

    private static float[] BuildSectorIntegrationField(NavigationWorld world, SectorData sector, IntegrationSeed[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            throw new InvalidOperationException($"BuildSectorIntegrationField failed: sector {sector.SectorId} has no seeds.");

        float[] integration = new float[sector.Width * sector.Height];
        for (int i = 0; i < integration.Length; i++)
            integration[i] = float.PositiveInfinity;

        OpenSet.Clear();
        for (int i = 0; i < seeds.Length; i++)
        {
            Vector2Int goalCell = seeds[i].Cell;
            if (!IsInsideSector(sector, goalCell.x, goalCell.y))
                continue;

            int localIndex = GetSectorLocalIndex(sector, goalCell.x, goalCell.y);
            float seedCost = seeds[i].Cost;
            if (seedCost >= integration[localIndex])
                continue;

            integration[localIndex] = seedCost;
            OpenSet.Push(localIndex, seedCost);
        }

        while (OpenSet.Count > 0)
        {
            QueueNode node = OpenSet.Pop();
            if (node.Cost > integration[node.Index] + 0.0001f)
                continue;

            int localX = node.Index % sector.Width;
            int localY = node.Index / sector.Width;
            int worldX = sector.StartX + localX;
            int worldY = sector.StartY + localY;

            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextWorldX = worldX + NeighborOffsetX[i];
                int nextWorldY = worldY + NeighborOffsetY[i];
                if (!IsInsideSector(sector, nextWorldX, nextWorldY) || !world.IsWalkable(nextWorldX, nextWorldY))
                    continue;

                bool diagonal = NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0;
                if (!CanTraverseNeighborCells(world, worldX, worldY, nextWorldX, nextWorldY))
                    continue;

                int nextLocalIndex = GetSectorLocalIndex(sector, nextWorldX, nextWorldY);
                float stepCost = diagonal ? 1.41421356f : 1f;
                float newCost = integration[node.Index] + stepCost;
                if (newCost >= integration[nextLocalIndex])
                    continue;

                integration[nextLocalIndex] = newCost;
                OpenSet.Push(nextLocalIndex, newCost);
            }
        }

        return integration;
    }

    private static float ResolveMinimumIntegrationCost(SectorData sector, float[] integration, Vector2Int[] targetCells)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < targetCells.Length; i++)
        {
            Vector2Int cell = targetCells[i];
            if (!IsInsideSector(sector, cell.x, cell.y))
                continue;

            float cost = integration[GetSectorLocalIndex(sector, cell.x, cell.y)];
            if (cost < best)
                best = cost;
        }

        return best;
    }

    private static bool IsInsideSector(SectorData sector, int worldX, int worldY)
    {
        return worldX >= sector.StartX
               && worldX < sector.StartX + sector.Width
               && worldY >= sector.StartY
               && worldY < sector.StartY + sector.Height;
    }

    private static int GetSectorLocalIndex(SectorData sector, int worldX, int worldY)
    {
        return (worldX - sector.StartX) + (worldY - sector.StartY) * sector.Width;
    }

    private static bool IsCellReachableInTile(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        int localIndex = tile.GetLocalIndex(worldX, worldY);
        return !float.IsPositiveInfinity(tile.Integration[localIndex]);
    }

    private static int EncodePortalNode(int sectorId, int portalId)
    {
        return (sectorId << 16) | (portalId & 0xFFFF);
    }

    private static void DecodePortalNode(int node, out int sectorId, out int portalId)
    {
        sectorId = node >> 16;
        portalId = node & 0xFFFF;
    }

    private static Vector3 ResolveDirectFallbackVelocity(IEntityContext self, Vector3 goalPosition, float maxSpeed)
    {
        Vector3 toGoal = goalPosition - self.Position;
        toGoal.y = 0f;
        if (toGoal.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        Vector3 desired = toGoal.normalized * maxSpeed;
        return desired;
    }

    private static bool EnsurePathHandle(AgentRuntimeData agent, int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        PathHandle handle = agent.NavState.PathHandle;
        if (handle != null
            && handle.WorldVersion == _world.Version
            && handle.MatchesGoal(goalX, goalY))
        {
            return true;
        }

        handle = BuildPathHandle(startSectorId, goalSectorId, startX, startY, goalX, goalY);
        agent.NavState.PathHandle = handle;
        return handle != null;
    }

    private static PathHandle BuildPathHandle(int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];

        if (startSectorId == goalSectorId)
        {
            float[] sectorGoalIntegration = BuildSectorIntegrationField(_world, startSector, new[] { new Vector2Int(goalX, goalY) });
            if (float.IsPositiveInfinity(sectorGoalIntegration[GetSectorLocalIndex(startSector, startX, startY)]))
                return null;

            return new PathHandle
            {
                WorldVersion = _world.Version,
                GoalX = goalX,
                GoalY = goalY,
                SectorIds = new[] { startSectorId },
                PortalIds = Array.Empty<int>(),
                CurrentSectorIndex = 0
            };
        }

        float[] startIntegration = BuildSectorIntegrationField(_world, startSector, new[] { new Vector2Int(startX, startY) });
        float[] goalIntegration = BuildSectorIntegrationField(_world, goalSector, new[] { new Vector2Int(goalX, goalY) });

        PortalNodeGScore.Clear();
        PortalNodeCameFrom.Clear();
        OpenSet.Clear();

        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            float startCost = ResolveMinimumIntegrationCost(
                startSector,
                startIntegration,
                GetPortalCellsForSector(_world.Portals[portalId], startSectorId));
            if (float.IsPositiveInfinity(startCost))
                continue;

            int node = EncodePortalNode(startSectorId, portalId);
            PortalNodeGScore[node] = startCost;
            OpenSet.Push(node, startCost);
        }

        if (OpenSet.Count == 0)
            return null;

        float bestGoalCost = float.PositiveInfinity;
        int bestGoalNode = int.MinValue;

        while (OpenSet.Count > 0)
        {
            QueueNode node = OpenSet.Pop();
            int currentNode = node.Index;
            if (!PortalNodeGScore.TryGetValue(currentNode, out float currentCost) || node.Cost > currentCost + 0.001f)
                continue;

            DecodePortalNode(currentNode, out int currentSectorId, out int currentPortalId);

            if (currentSectorId == goalSectorId)
            {
                float goalCost = ResolveMinimumIntegrationCost(
                    goalSector,
                    goalIntegration,
                    GetPortalCellsForSector(_world.Portals[currentPortalId], goalSectorId));
                if (!float.IsPositiveInfinity(goalCost) && currentCost + goalCost < bestGoalCost)
                {
                    bestGoalCost = currentCost + goalCost;
                    bestGoalNode = currentNode;
                }
            }

            PortalData currentPortal = _world.Portals[currentPortalId];
            int oppositeSectorId = GetOppositeSectorId(currentPortal, currentSectorId);
            int crossNode = EncodePortalNode(oppositeSectorId, currentPortalId);
            float crossCost = currentCost + 1f;
            if (!PortalNodeGScore.TryGetValue(crossNode, out float existingCrossCost) || crossCost < existingCrossCost)
            {
                PortalNodeGScore[crossNode] = crossCost;
                PortalNodeCameFrom[crossNode] = currentNode;
                OpenSet.Push(crossNode, crossCost);
            }

            SectorData sector = _world.Sectors[currentSectorId];
            for (int i = 0; i < sector.PortalTransitions.Count; i++)
            {
                PortalTransition transition = sector.PortalTransitions[i];
                if (transition.FromPortalId != currentPortalId)
                    continue;

                int nextNode = EncodePortalNode(currentSectorId, transition.ToPortalId);
                float tentative = currentCost + transition.Cost;
                if (PortalNodeGScore.TryGetValue(nextNode, out float oldScore) && tentative >= oldScore)
                    continue;

                PortalNodeGScore[nextNode] = tentative;
                PortalNodeCameFrom[nextNode] = currentNode;
                OpenSet.Push(nextNode, tentative);
            }
        }

        if (bestGoalNode == int.MinValue)
            return null;

        List<int> portalNodes = new List<int>(16);
        int cursor = bestGoalNode;
        portalNodes.Add(cursor);
        while (PortalNodeCameFrom.TryGetValue(cursor, out int parent))
        {
            cursor = parent;
            portalNodes.Add(cursor);
        }

        portalNodes.Reverse();

        List<int> sectorIds = new List<int>(8) { startSectorId };
        List<int> portalIds = new List<int>(8);
        for (int i = 0; i < portalNodes.Count - 1; i++)
        {
            DecodePortalNode(portalNodes[i], out int fromSectorId, out int fromPortalId);
            DecodePortalNode(portalNodes[i + 1], out int toSectorId, out int toPortalId);
            if (fromPortalId != toPortalId || fromSectorId == toSectorId)
                continue;

            portalIds.Add(fromPortalId);
            sectorIds.Add(toSectorId);
        }

        if (portalIds.Count == 0 || sectorIds[sectorIds.Count - 1] != goalSectorId)
            return null;

        return new PathHandle
        {
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = sectorIds.ToArray(),
            PortalIds = portalIds.ToArray(),
            CurrentSectorIndex = 0
        };
    }

    private static bool TryAdvancePathToCurrentSector(AgentRuntimeData agent, int currentSectorId)
    {
        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return false;

        for (int i = handle.CurrentSectorIndex; i < handle.SectorIds.Length; i++)
        {
            if (handle.SectorIds[i] != currentSectorId)
                continue;

            handle.CurrentSectorIndex = i;
            return true;
        }

        return false;
    }

    private static bool TryBuildOrGetTile(
        AgentRuntimeData agent,
        int goalX,
        int goalY,
        out FlowTileCacheEntry tile,
        out TileGoalKind goalKind,
        out int downstreamPortalId)
    {
        tile = null;
        goalKind = TileGoalKind.FinalGoal;
        downstreamPortalId = -1;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null)
            return false;

        int sectorPathIndex = handle.CurrentSectorIndex;
        int sectorId = handle.SectorIds[sectorPathIndex];
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(handle, sectorPathIndex, goalX, goalY, agent.AgentTypeId, out goalKind, out downstreamPortalId);
        if (FlowTileCache.TryGetValue(key, out tile))
        {
            tile.LastUsedFrame = GetFrameCount();
            return true;
        }

        tile = BuildTileForSector(key, handle, sectorPathIndex, goalX, goalY, agent.AgentTypeId);
        if (tile == null)
            return false;

        FlowTileCache[key] = tile;
        tile.LastUsedFrame = GetFrameCount();
        TrimTileCache();
        agent.NavState.CurrentTileKeyHash = key.GetHashCode();
        return true;
    }

    private static FlowTileCacheKey CreateTileCacheKeyForPathSegment(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        out TileGoalKind goalKind,
        out int downstreamPortalId)
    {
        if (handle == null)
            throw new InvalidOperationException("CreateTileCacheKeyForPathSegment failed: handle is null.");

        int sectorId = handle.SectorIds[sectorPathIndex];
        int goalId;
        int downstreamGoalHint;
        if (sectorPathIndex >= handle.SectorIds.Length - 1)
        {
            goalKind = TileGoalKind.FinalGoal;
            downstreamPortalId = -1;
            goalId = _world.GetIndex(goalX, goalY);
            downstreamGoalHint = 0;
        }
        else
        {
            goalKind = TileGoalKind.Portal;
            downstreamPortalId = handle.PortalIds[sectorPathIndex];
            goalId = downstreamPortalId;
            downstreamGoalHint = ResolveDownstreamGoalHint(handle, sectorPathIndex, goalX, goalY);
        }

        return new FlowTileCacheKey(
            _world.Version,
            sectorId,
            goalKind,
            goalId,
            downstreamGoalHint,
            agentTypeId,
            _world.Sectors[sectorId].DirtyVersion);
    }

    private static int ResolveDownstreamGoalHint(PathHandle handle, int sectorPathIndex, int goalX, int goalY)
    {
        int nextSectorIndex = sectorPathIndex + 1;
        if (nextSectorIndex >= handle.SectorIds.Length - 1)
            return ~_world.GetIndex(goalX, goalY);

        return handle.PortalIds[nextSectorIndex];
    }

    private static bool TryGetOrBuildTileForPathSegment(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        out FlowTileCacheEntry tile)
    {
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(handle, sectorPathIndex, goalX, goalY, agentTypeId, out _, out _);
        if (FlowTileCache.TryGetValue(key, out tile))
        {
            tile.LastUsedFrame = GetFrameCount();
            return true;
        }

        tile = BuildTileForSector(key, handle, sectorPathIndex, goalX, goalY, agentTypeId);
        if (tile == null)
            return false;

        FlowTileCache[key] = tile;
        tile.LastUsedFrame = GetFrameCount();
        TrimTileCache();
        return true;
    }

    private static FlowTileCacheEntry BuildTileForSector(FlowTileCacheKey key, PathHandle handle, int sectorPathIndex, int goalX, int goalY, int agentTypeId)
    {
        SectorData sector = _world.Sectors[key.SectorId];
        Vector2Int[] goalCells = ResolveGoalCells(sector, key, goalX, goalY);
        if (goalCells == null || goalCells.Length == 0)
            return null;

        IntegrationSeed[] seeds = BuildIntegrationSeedsForTile(key, handle, sectorPathIndex, goalX, goalY, goalCells, agentTypeId);
        if (seeds == null || seeds.Length == 0)
            throw new InvalidOperationException($"BuildTileForSector failed: sector {sector.SectorId} has no integration seeds goalKind={key.GoalKind} goalId={key.GoalId}.");

        FlowTileCacheEntry tile = new FlowTileCacheEntry
        {
            Key = key,
            StartX = sector.StartX,
            StartY = sector.StartY,
            Width = sector.Width,
            Height = sector.Height,
            Integration = new float[sector.Width * sector.Height],
            FlowDirections = new Vector2[sector.Width * sector.Height],
            HasLineOfSight = new bool[sector.Width * sector.Height],
            GoalCells = goalCells
        };

        tile.Integration = BuildSectorIntegrationField(_world, sector, seeds);
        for (int i = 0; i < tile.FlowDirections.Length; i++)
        {
            tile.FlowDirections[i] = Vector2.zero;
            tile.HasLineOfSight[i] = false;
        }

        for (int worldY = tile.StartY; worldY < tile.StartY + tile.Height; worldY++)
        {
            for (int worldX = tile.StartX; worldX < tile.StartX + tile.Width; worldX++)
            {
                if (!_world.IsWalkable(worldX, worldY))
                    continue;

                int localIndex = tile.GetLocalIndex(worldX, worldY);
                tile.HasLineOfSight[localIndex] = HasLineOfSightToAnyGoal(tile, worldX, worldY);

                float bestCost = tile.Integration[localIndex];
                Vector2 bestDirection = Vector2.zero;
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int nextWorldX = worldX + NeighborOffsetX[i];
                    int nextWorldY = worldY + NeighborOffsetY[i];
                    if (!IsInsideSector(tile, nextWorldX, nextWorldY) || !_world.IsWalkable(nextWorldX, nextWorldY))
                        continue;

                    if (!CanTraverseNeighborCells(_world, worldX, worldY, nextWorldX, nextWorldY))
                        continue;

                    float candidate = tile.Integration[tile.GetLocalIndex(nextWorldX, nextWorldY)];
                    if (candidate >= bestCost)
                        continue;

                    bestCost = candidate;
                    bestDirection = new Vector2(nextWorldX - worldX, nextWorldY - worldY).normalized;
                }

                tile.FlowDirections[localIndex] = bestDirection;
            }
        }

        return tile;
    }

    private static IntegrationSeed[] BuildIntegrationSeedsForTile(
        FlowTileCacheKey key,
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        Vector2Int[] goalCells,
        int agentTypeId)
    {
        if (key.GoalKind == TileGoalKind.FinalGoal)
        {
            IntegrationSeed[] finalSeeds = new IntegrationSeed[goalCells.Length];
            for (int i = 0; i < goalCells.Length; i++)
                finalSeeds[i] = new IntegrationSeed(goalCells[i], 0f);
            return finalSeeds;
        }

        if (handle == null)
            throw new InvalidOperationException($"BuildIntegrationSeedsForTile failed: portal tile missing handle sector={key.SectorId} goalId={key.GoalId}.");

        int nextSectorIndex = sectorPathIndex + 1;
        if (nextSectorIndex >= handle.SectorIds.Length)
            throw new InvalidOperationException($"BuildIntegrationSeedsForTile failed: portal tile has no downstream sector currentSector={key.SectorId} goalId={key.GoalId}.");

        if (!TryGetOrBuildTileForPathSegment(handle, nextSectorIndex, goalX, goalY, agentTypeId, out FlowTileCacheEntry downstreamTile))
            throw new InvalidOperationException($"BuildIntegrationSeedsForTile failed: downstream tile unavailable currentSector={key.SectorId} nextSector={handle.SectorIds[nextSectorIndex]}.");

        PortalData portal = _world.Portals[key.GoalId];
        int downstreamSectorId = handle.SectorIds[nextSectorIndex];
        Vector2Int[] downstreamCells = GetPortalCellsForSector(portal, downstreamSectorId);
        if (goalCells.Length != downstreamCells.Length)
            throw new InvalidOperationException(
                $"BuildIntegrationSeedsForTile failed: portal {portal.PortalId} side cell count mismatch current={goalCells.Length} downstream={downstreamCells.Length}.");

        IntegrationSeed[] seeds = new IntegrationSeed[goalCells.Length];
        for (int i = 0; i < goalCells.Length; i++)
        {
            Vector2Int downstreamCell = downstreamCells[i];
            float downstreamCost = downstreamTile.Integration[downstreamTile.GetLocalIndex(downstreamCell.x, downstreamCell.y)];
            if (float.IsPositiveInfinity(downstreamCost))
                throw new InvalidOperationException(
                    $"BuildIntegrationSeedsForTile failed: downstream cell unreachable portal={portal.PortalId} nextSector={downstreamSectorId} cell={downstreamCell}.");

            seeds[i] = new IntegrationSeed(goalCells[i], downstreamCost + 1f);
        }

        return seeds;
    }

    private static Vector2Int[] ResolveGoalCells(SectorData sector, FlowTileCacheKey key, int goalX, int goalY)
    {
        if (key.GoalKind == TileGoalKind.FinalGoal)
            return new[] { new Vector2Int(goalX, goalY) };

        return GetPortalCellsForSector(_world.Portals[key.GoalId], sector.SectorId);
    }

    private static bool IsInsideSector(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return worldX >= tile.StartX
               && worldX < tile.StartX + tile.Width
               && worldY >= tile.StartY
               && worldY < tile.StartY + tile.Height;
    }

    private static bool HasLineOfSightToAnyGoal(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            Vector2Int goal = tile.GoalCells[i];
            if (HasGridLineOfSight(_world, worldX, worldY, goal.x, goal.y))
                return true;
        }

        return false;
    }

    private static void TrimTileCache()
    {
        if (FlowTileCache.Count <= Config.FlowTileCacheLimit)
            return;

        int threshold = GetFrameCount() - 120;
        List<FlowTileCacheKey> expiredKeys = new List<FlowTileCacheKey>();
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            if (pair.Value.LastUsedFrame < threshold)
                expiredKeys.Add(pair.Key);
        }

        for (int i = 0; i < expiredKeys.Count; i++)
            FlowTileCache.Remove(expiredKeys[i]);

        if (FlowTileCache.Count <= Config.FlowTileCacheLimit)
            return;

        List<FlowTileCacheKey> keys = new List<FlowTileCacheKey>(FlowTileCache.Keys);
        keys.Sort((a, b) => FlowTileCache[a].LastUsedFrame.CompareTo(FlowTileCache[b].LastUsedFrame));
        int removeCount = FlowTileCache.Count - Config.FlowTileCacheLimit;
        for (int i = 0; i < removeCount; i++)
            FlowTileCache.Remove(keys[i]);
    }

    private static DesiredDirectionResolution ResolveDesiredDirection(string characterKey, FlowTileCacheEntry tile, int currentX, int currentY, Vector3 goalPosition, TileGoalKind goalKind)
    {
        int localIndex = tile.GetLocalIndex(currentX, currentY);
        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);
        PortalTargetResolution portalTarget = ResolveTileTargetPosition(characterKey, tile, currentX, currentY, goalPosition, goalKind);
        Vector3 tileTargetPosition = portalTarget.TargetPosition;
        Vector3 toTileTarget = tileTargetPosition - currentCenter;
        toTileTarget.y = 0f;
        bool hasLineOfSight = tile.HasLineOfSight[localIndex];
        Vector2 flow = tile.FlowDirections[localIndex];
        float integration = tile.Integration[localIndex];
        Vector3 flowDirection = new Vector3(flow.x, 0f, flow.y);
        Vector3 lineOfSightDirection = toTileTarget.sqrMagnitude > 0.0001f ? toTileTarget.normalized : Vector3.zero;
        DesiredDirectionResolution resolution;

        if (tile.HasLineOfSight[localIndex] && toTileTarget.sqrMagnitude > 0.0001f)
        {
            resolution = new DesiredDirectionResolution(
                lineOfSightDirection,
                DesiredDirectionSource.LineOfSight,
                tileTargetPosition,
                flow,
                hasLineOfSight,
                integration);
        }
        else if (flow.sqrMagnitude > 0.0001f)
        {
            resolution = new DesiredDirectionResolution(
                flowDirection,
                DesiredDirectionSource.FlowField,
                tileTargetPosition,
                flow,
                hasLineOfSight,
                integration);
        }
        else
        {
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                bool isPortalGoalCell = false;
                for (int i = 0; i < tile.GoalCells.Length; i++)
                {
                    if (tile.GoalCells[i].x == currentX && tile.GoalCells[i].y == currentY)
                    {
                        isPortalGoalCell = true;
                        break;
                    }
                }

                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowDesiredDirZero] sector={tile.Key.SectorId} goalKind={goalKind} goalId={tile.Key.GoalId} " +
                    $"cell=({currentX},{currentY}) localIndex={localIndex} los={hasLineOfSight} " +
                    $"flow={flow} targetPos={tileTargetPosition} toTarget={toTileTarget} " +
                    $"integration={integration:F3} isGoalCell={isPortalGoalCell} goalCells={FormatGoalCells(tile.GoalCells)}");
            }

            if (toTileTarget.sqrMagnitude > 0.0001f)
            {
                resolution = new DesiredDirectionResolution(
                    lineOfSightDirection,
                    DesiredDirectionSource.TileTargetFallback,
                    tileTargetPosition,
                    flow,
                    hasLineOfSight,
                    integration);
            }
            else
            {
                resolution = new DesiredDirectionResolution(
                    Vector3.zero,
                    DesiredDirectionSource.Zero,
                    tileTargetPosition,
                    flow,
                    hasLineOfSight,
                    integration);
            }
        }

        if (ShouldLogPortalDiagnostics(characterKey, goalKind))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPortalResolve] key={characterKey} sector={tile.Key.SectorId} goalId={tile.Key.GoalId} " +
                $"cell=({currentX},{currentY}) center={currentCenter} localIndex={localIndex} integration={integration:F3} " +
                $"source={resolution.Source} hasLOS={hasLineOfSight} flow={flow} flowDir={flowDirection} " +
                $"tileTarget={tileTargetPosition} toTarget={toTileTarget} losDir={lineOfSightDirection} " +
                $"flowDotLos={Vector3.Dot(flowDirection, lineOfSightDirection):F3} " +
                $"visibleCandidates={portalTarget.VisibleCandidateCount} selectedPair={portalTarget.SelectedPairIndex} " +
                $"usedOppositeCenter={portalTarget.UsedOppositeCenter} goalCells={FormatGoalCells(tile.GoalCells)}");
        }

        return resolution;
    }

    private static PortalTargetResolution ResolveTileTargetPosition(string characterKey, FlowTileCacheEntry tile, int currentX, int currentY, Vector3 goalPosition, TileGoalKind goalKind)
    {
        if (goalKind == TileGoalKind.FinalGoal)
            return new PortalTargetResolution(goalPosition, 0, -1, false, string.Empty);

        if (goalKind != TileGoalKind.Portal || tile.GoalCells == null || tile.GoalCells.Length == 0)
            throw new InvalidOperationException($"ResolveTileTargetPosition failed: invalid portal tile target sector={tile.Key.SectorId} goalId={tile.Key.GoalId}");

        PortalData portal = _world.Portals[tile.Key.GoalId];
        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, tile.Key.SectorId));
        if (currentSideCells.Length != oppositeSideCells.Length)
            throw new InvalidOperationException(
                $"ResolveTileTargetPosition failed: portal {portal.PortalId} side cell count mismatch current={currentSideCells.Length} opposite={oppositeSideCells.Length}");

        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);
        float bestGoalCost = float.PositiveInfinity;
        float bestDistanceSq = float.MaxValue;
        Vector3 bestTarget = Vector3.zero;
        bool foundVisibleGoalCell = false;
        int visibleCandidateCount = 0;
        int selectedPairIndex = -1;
        bool logPortal = ShouldLogPortalDiagnostics(characterKey, goalKind);
        System.Text.StringBuilder candidateBuilder = logPortal
            ? new System.Text.StringBuilder(currentSideCells.Length * 96)
            : null;
        for (int i = 0; i < currentSideCells.Length; i++)
        {
            Vector2Int goalCell = currentSideCells[i];
            Vector2Int oppositeCell = oppositeSideCells[i];
            Vector3 candidate = _world.GridToWorldCenter(oppositeCell.x, oppositeCell.y);
            bool visible = HasGridLineOfSight(_world, currentX, currentY, goalCell.x, goalCell.y);
            float distanceSq = (candidate - currentCenter).sqrMagnitude;
            float goalCost = tile.Integration[tile.GetLocalIndex(goalCell.x, goalCell.y)];

            if (candidateBuilder != null)
            {
                if (candidateBuilder.Length > 0)
                    candidateBuilder.Append(" | ");

                candidateBuilder.Append("i=");
                candidateBuilder.Append(i);
                candidateBuilder.Append(" goal=");
                candidateBuilder.Append('(').Append(goalCell.x).Append(',').Append(goalCell.y).Append(')');
                candidateBuilder.Append(" opp=");
                candidateBuilder.Append('(').Append(oppositeCell.x).Append(',').Append(oppositeCell.y).Append(')');
                candidateBuilder.Append(" visible=");
                candidateBuilder.Append(visible);
                candidateBuilder.Append(" goalCost=");
                candidateBuilder.Append(goalCost.ToString("F3"));
                candidateBuilder.Append(" distSq=");
                candidateBuilder.Append(distanceSq.ToString("F3"));
                candidateBuilder.Append(" cand=");
                candidateBuilder.Append(candidate);
            }

            if (!visible)
                continue;

            if (float.IsPositiveInfinity(goalCost))
            {
                throw new InvalidOperationException(
                    $"ResolveTileTargetPosition failed: visible portal goal cell has infinite integration sector={tile.Key.SectorId} goalId={tile.Key.GoalId} cell=({goalCell.x},{goalCell.y}).");
            }

            visibleCandidateCount++;
            bool betterCost = goalCost < bestGoalCost - 0.001f;
            bool sameCostCloser = Mathf.Abs(goalCost - bestGoalCost) <= 0.001f && distanceSq < bestDistanceSq;
            if (!betterCost && !sameCostCloser)
                continue;

            bestGoalCost = goalCost;
            bestDistanceSq = distanceSq;
            bestTarget = candidate;
            foundVisibleGoalCell = true;
            selectedPairIndex = i;
        }

        if (foundVisibleGoalCell)
        {
            if (logPortal)
            {
                GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPortalTarget] key={characterKey} sector={tile.Key.SectorId} goalId={tile.Key.GoalId} " +
                $"portalCenter={portal.WorldCenter} vertical={portal.IsVerticalBoundary} widthCells={portal.WidthCells} " +
                $"currentCell=({currentX},{currentY}) currentCenter={currentCenter} " +
                $"currentSide={FormatGoalCells(currentSideCells)} oppositeSide={FormatGoalCells(oppositeSideCells)} " +
                $"selection=LowestVisibleCost selectedPair={selectedPairIndex} selectedGoalCost={bestGoalCost:F3} visibleCount={visibleCandidateCount} " +
                $"target={bestTarget} candidates={candidateBuilder}");
            }

            return new PortalTargetResolution(bestTarget, visibleCandidateCount, selectedPairIndex, false, candidateBuilder?.ToString() ?? string.Empty);
        }

        Vector3 oppositePortalCenter = Vector3.zero;
        for (int i = 0; i < oppositeSideCells.Length; i++)
            oppositePortalCenter += _world.GridToWorldCenter(oppositeSideCells[i].x, oppositeSideCells[i].y);
        oppositePortalCenter /= oppositeSideCells.Length;

        if (logPortal)
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPortalTarget] key={characterKey} sector={tile.Key.SectorId} goalId={tile.Key.GoalId} " +
                $"portalCenter={portal.WorldCenter} vertical={portal.IsVerticalBoundary} widthCells={portal.WidthCells} " +
                $"currentCell=({currentX},{currentY}) currentCenter={currentCenter} " +
                $"currentSide={FormatGoalCells(currentSideCells)} oppositeSide={FormatGoalCells(oppositeSideCells)} " +
                $"selection=OppositeCenter selectedPair=-1 visibleCount=0 target={oppositePortalCenter} candidates={candidateBuilder}");
        }

        return new PortalTargetResolution(oppositePortalCenter, 0, -1, true, candidateBuilder?.ToString() ?? string.Empty);
    }

    private static bool ShouldLogPortalDiagnostics(string characterKey, TileGoalKind goalKind)
    {
        return goalKind == TileGoalKind.Portal
               && GameDebugSettings.IsEnabled(DebugCategory.Move)
               && !string.IsNullOrEmpty(characterKey)
               && GameDebugSettings.ShouldLogMovementForCharacter(characterKey);
    }

    private static string FormatGoalCells(Vector2Int[] cells)
    {
        if (cells == null || cells.Length == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(cells.Length * 12);
        builder.Append('[');
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append('(');
            builder.Append(cells[i].x);
            builder.Append(',');
            builder.Append(cells[i].y);
            builder.Append(')');
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static BottleneckDecision EvaluateBottleneck(AgentRuntimeData agent, int portalId, int currentSectorId, Vector3 desiredDirection)
    {
        PortalData portal = _world.Portals[portalId];
        if (!portal.IsNarrow)
            return FreeMoveDecision;

        if (!Bottlenecks.TryGetValue(portalId, out BottleneckRuntimeState state))
        {
            state = new BottleneckRuntimeState
            {
                PortalId = portalId
            };
            Bottlenecks.Add(portalId, state);
        }

        int frameCount = GetFrameCount();
        float time = GetTime();
        if (state.LastFrameTouched != frameCount)
        {
            state.LastFrameTouched = frameCount;
            state.OccupiedCount = 0;
        }

        int direction = portal.SectorAId == currentSectorId ? 1 : -1;
        float distanceToPortal = Vector3.Distance(agent.Position, portal.WorldCenter);
        if (distanceToPortal > Config.BottleneckInfluenceDistance)
            return FreeMoveDecision;

        Vector3 corridorAxis = ResolvePortalCorridorAxis(portal, desiredDirection);

        Vector3 laneBias = Vector3.Cross(Vector3.up, corridorAxis).normalized * Config.LaneBiasStrength;
        int laneSeed = (agent.Id * 73856093) ^ (portal.PortalId * 19349663);
        if ((laneSeed & 1) == 0)
            laneBias = -laneBias;

        if (state.CurrentDirection == 0)
        {
            state.CurrentDirection = direction;
            state.SwitchBlockedUntil = time + Config.BottleneckSwitchCooldown;
        }

        if (state.CurrentDirection != direction)
        {
            if (!state.WaitingStartTimes.TryGetValue(agent.Id, out float waitStart))
            {
                waitStart = time;
                state.WaitingStartTimes[agent.Id] = waitStart;
            }

            bool canSwitch = time >= state.SwitchBlockedUntil && state.OccupiedCount == 0;
            bool timedOut = time - waitStart >= Config.BottleneckWaitTimeout && state.OccupiedCount == 0;
            if (canSwitch || timedOut)
            {
                state.CurrentDirection = direction;
                state.SwitchBlockedUntil = time + Config.BottleneckSwitchCooldown;
                state.WaitingStartTimes.Remove(agent.Id);
            }
            else
            {
                Vector3 queueBias = ResolveQueueBias(state, portal, corridorAxis, laneBias, direction, agent, time);
                return new BottleneckDecision(0f, laneBias, queueBias);
            }
        }

        state.WaitingStartTimes.Remove(agent.Id);
        if (distanceToPortal <= Config.BottleneckInfluenceDistance * 0.85f)
            state.OccupiedCount++;

        return new BottleneckDecision(1f, laneBias, Vector3.zero);
    }

    private static Vector3 ResolvePortalCorridorAxis(PortalData portal, Vector3 desiredDirection)
    {
        if (portal.IsVerticalBoundary)
            return new Vector3(0f, 0f, desiredDirection.z >= 0f ? 1f : -1f);

        return new Vector3(desiredDirection.x >= 0f ? 1f : -1f, 0f, 0f);
    }

    private static Vector3 ResolveQueueBias(
        BottleneckRuntimeState state,
        PortalData portal,
        Vector3 corridorAxis,
        Vector3 laneBias,
        int direction,
        AgentRuntimeData agent,
        float time)
    {
        int queueRank = 0;
        foreach (KeyValuePair<int, float> pair in state.WaitingStartTimes)
        {
            if (pair.Key == agent.Id)
                continue;
            if (!Agents.TryGetValue(pair.Key, out AgentRuntimeData otherAgent))
                continue;
            if (otherAgent.NavState.CurrentSectorId != agent.NavState.CurrentSectorId)
                continue;
            if (pair.Value <= time && pair.Value < state.WaitingStartTimes[agent.Id])
                queueRank++;
        }

        float slotSpacing = Mathf.Max(agent.Radius * 2.2f, _world.CellSize * 0.9f);
        float portalClearance = Mathf.Max(agent.Radius * 1.35f, _world.CellSize * 0.75f);
        Vector3 queueTarget = portal.WorldCenter
                              - corridorAxis * (portalClearance + queueRank * slotSpacing)
                              + laneBias.normalized * Mathf.Max(agent.Radius * 0.6f, _world.CellSize * 0.15f);
        Vector3 toSlot = queueTarget - agent.Position;
        toSlot.y = 0f;
        if (toSlot.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        float slotSpeed = Mathf.Min(toSlot.magnitude, Mathf.Max(0.6f, slotSpacing));
        return toSlot.normalized * slotSpeed;
    }

    private static Vector3 ResolveCrowdSteering(
        IEntityContext selfContext,
        AgentRuntimeData self,
        Vector3 goalPosition,
        Vector3 desiredVelocity,
        DesiredDirectionResolution desiredResolution,
        FlowTileCacheEntry tile,
        BottleneckDecision bottleneck)
    {
        if (desiredVelocity.sqrMagnitude <= 0.0001f && bottleneck.QueueBias.sqrMagnitude <= 0.0001f)
            return Vector3.zero;
        if (selfContext == null)
            throw new InvalidOperationException("ResolveCrowdSteering failed: selfContext is null.");

        bool debugMove = GameDebugSettings.IsEnabled(DebugCategory.Move)
                         && !string.IsNullOrEmpty(self.CharacterKey)
                         && GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey);
        float maxSpeed = desiredVelocity.magnitude;
        if (maxSpeed <= 0.0001f)
            maxSpeed = Mathf.Max(self.NavState.ResolvedVelocity.magnitude, 0.6f);
        Vector3 desiredDirection = desiredVelocity.normalized;
        if (desiredDirection.sqrMagnitude <= 0.0001f && bottleneck.QueueBias.sqrMagnitude > 0.0001f)
            desiredDirection = bottleneck.QueueBias.normalized;
        float avoidRadius = Mathf.Max(self.Radius * 4f, maxSpeed * Config.CrowdPredictionTime + self.Radius * 1.5f);
        string[] topAvoidLogs = debugMove ? new string[3] : null;
        float[] topAvoidScores = debugMove ? new float[3] : null;

        Vector3 agentAvoidance = Vector3.zero;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData other = pair.Value;
            if (other.Id == self.Id)
                continue;
            if (!ShouldAvoidAsDynamicNeighbor(selfContext, self, other))
                continue;

            Vector3 relativePos = other.Position - self.Position;
            relativePos.y = 0f;
            float currentDistance = relativePos.magnitude;
            if (currentDistance > avoidRadius || currentDistance <= 0.0001f)
                continue;

            Vector3 otherVelocity = other.NavState.ResolvedVelocity;
            Vector3 relativeVel = otherVelocity - desiredVelocity;
            relativeVel.y = 0f;
            float relativeSpeedSq = relativeVel.sqrMagnitude;
            float timeToClosest = relativeSpeedSq > 0.0001f
                ? Mathf.Clamp(-Vector3.Dot(relativePos, relativeVel) / relativeSpeedSq, 0f, Config.CrowdPredictionTime)
                : 0f;

            Vector3 closestOffset = relativePos + relativeVel * timeToClosest;
            float separationDistance = closestOffset.magnitude;
            float combinedRadius = self.Radius + other.Radius;

            if (separationDistance > avoidRadius && currentDistance > avoidRadius)
                continue;

            Vector3 away = separationDistance > 0.0001f ? -closestOffset.normalized : -relativePos.normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, relativePos).normalized;
            if (Vector3.Dot(tangent, desiredDirection) < 0f)
                tangent = -tangent;

            float overlap = Mathf.Clamp01((combinedRadius - separationDistance) / Mathf.Max(combinedRadius, 0.001f));
            float proximity = Mathf.Clamp01(1f - Mathf.Min(currentDistance, separationDistance) / avoidRadius);
            float opposing = otherVelocity.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01((-Vector3.Dot(desiredDirection, otherVelocity.normalized) + 1f) * 0.5f)
                : 0.5f;

            Vector3 awayContribution = away * (proximity * proximity * maxSpeed + overlap * maxSpeed * 2.5f);
            Vector3 tangentContribution = tangent * proximity * opposing * maxSpeed * 0.75f;
            Vector3 totalContribution = awayContribution + tangentContribution;
            agentAvoidance += totalContribution;

            if (debugMove)
            {
                TryInsertTopAvoidContribution(
                    topAvoidLogs,
                    topAvoidScores,
                    totalContribution.sqrMagnitude,
                    $"other={other.CharacterKey} otherType={other.EntityTypeName} otherMoveComp={other.MoveCompTypeName} " +
                    $"otherSource={other.RegistrationSource} synthetic={other.IsSyntheticRegistration} " +
                    $"otherPos={other.Position} otherVel={otherVelocity} " +
                    $"dist={currentDistance:F3} sep={separationDistance:F3} ttc={timeToClosest:F3} " +
                    $"proximity={proximity:F3} overlap={overlap:F3} opposing={opposing:F3} " +
                    $"away={away} tangent={tangent} awayContrib={awayContribution} tangentContrib={tangentContribution} total={totalContribution}");
            }
        }

        Vector3 boundaryAvoidance = Vector3.zero;
        Vector3 edgeNormal = Vector3.zero;
        float edgeDistance = float.MaxValue;
        if (tile != null)
            boundaryAvoidance = ResolveBoundaryAvoidance(
                self.Position,
                desiredDirection,
                self.Radius,
                maxSpeed,
                self.AgentTypeId,
                out edgeNormal,
                out edgeDistance);

        Vector3 clampedAgentAvoidance = ClampAgentAvoidance(agentAvoidance, desiredDirection, maxSpeed);

        Vector3 baseVelocity = desiredVelocity * bottleneck.SpeedScale + bottleneck.QueueBias;
        Vector3 avoidance = clampedAgentAvoidance + boundaryAvoidance;
        Vector3 laneVelocity = bottleneck.LaneBias * maxSpeed;
        Vector3 result = baseVelocity + avoidance * 0.75f + laneVelocity;
        result = ApplyEdgeRecovery(result, desiredDirection, edgeNormal, edgeDistance, self.Radius, maxSpeed);

        Vector3 toGoal = goalPosition - self.Position;
        toGoal.y = 0f;
        if (bottleneck.SpeedScale > 0f && toGoal.sqrMagnitude > 0.0001f && Vector3.Dot(result, toGoal) < 0f)
            result = desiredVelocity * 0.35f + avoidance * 0.25f;

        if (debugMove)
        {
            Debug.Log(
                $"[FlowSteeringBreakdown] key={self.CharacterKey} pos={self.Position} desiredVel={desiredVelocity} " +
                $"baseVel={baseVelocity} queueBias={bottleneck.QueueBias} laneVel={laneVelocity} " +
                $"agentAvoid={agentAvoidance} clampedAgentAvoid={clampedAgentAvoidance} boundaryAvoid={boundaryAvoidance} " +
                $"combinedAvoid={avoidance} edgeNormal={edgeNormal} edgeDist={edgeDistance:F3} resultPreClamp={result} maxSpeed={maxSpeed:F3}");

            float forwardSpeed = desiredDirection.sqrMagnitude > 0.0001f ? Vector3.Dot(result, desiredDirection) : 0f;
            float lateralSpeed = desiredDirection.sqrMagnitude > 0.0001f
                ? Vector3.ProjectOnPlane(result, desiredDirection).magnitude
                : result.magnitude;
            bool anomalousSteering = agentAvoidance.magnitude > maxSpeed * 1.2f
                                     || boundaryAvoidance.magnitude > maxSpeed * 0.45f
                                     || forwardSpeed < maxSpeed * 0.35f
                                     || lateralSpeed > maxSpeed * 0.65f;
            if (anomalousSteering)
            {
                Debug.Log(
                    $"[FlowSteeringAnomaly] key={self.CharacterKey} pos={self.Position} desiredDir={desiredDirection} " +
                    $"desiredSrc={desiredResolution.Source} desiredFlow={desiredResolution.Flow} desiredLos={desiredResolution.HasLineOfSight} " +
                    $"tileTarget={desiredResolution.TileTargetPosition} integration={desiredResolution.Integration:F3} " +
                    $"forwardSpeed={forwardSpeed:F3} lateralSpeed={lateralSpeed:F3} maxSpeed={maxSpeed:F3} " +
                    $"agentAvoidMag={agentAvoidance.magnitude:F3} boundaryAvoidMag={boundaryAvoidance.magnitude:F3} " +
                    $"edgeNormal={edgeNormal} edgeDist={edgeDistance:F3} topAvoiders={FormatTopAvoidContributors(topAvoidLogs, topAvoidScores)}");
            }

            bool nearEdge = edgeDistance < Mathf.Max(self.Radius * 2.2f, _world.CellSize * 1.4f);
            bool wallHugTendency = nearEdge
                                   && desiredDirection.sqrMagnitude > 0.0001f
                                   && Vector3.Dot(desiredDirection, edgeNormal) < 0.15f
                                   && forwardSpeed < maxSpeed * 0.95f;
            if (wallHugTendency)
            {
                Debug.Log(
                    $"[FlowWallHugSample] key={self.CharacterKey} pos={self.Position} desiredDir={desiredDirection} " +
                    $"desiredSrc={desiredResolution.Source} flow={desiredResolution.Flow} los={desiredResolution.HasLineOfSight} " +
                    $"tileTarget={desiredResolution.TileTargetPosition} integration={desiredResolution.Integration:F3} " +
                    $"forwardSpeed={forwardSpeed:F3} lateralSpeed={lateralSpeed:F3} maxSpeed={maxSpeed:F3} " +
                    $"baseVel={baseVelocity} agentAvoid={agentAvoidance} clampedAgentAvoid={clampedAgentAvoidance} " +
                    $"boundaryAvoid={boundaryAvoidance} result={result} edgeNormal={edgeNormal} edgeDist={edgeDistance:F3}");
            }
        }

        return Vector3.ClampMagnitude(result, maxSpeed);
    }

    private static void TryInsertTopAvoidContribution(string[] logs, float[] scores, float score, string log)
    {
        if (logs == null || scores == null || score <= 0.0001f)
            return;

        for (int i = 0; i < scores.Length; i++)
        {
            if (score <= scores[i])
                continue;

            for (int shift = scores.Length - 1; shift > i; shift--)
            {
                scores[shift] = scores[shift - 1];
                logs[shift] = logs[shift - 1];
            }

            scores[i] = score;
            logs[i] = log;
            return;
        }
    }

    private static string FormatTopAvoidContributors(string[] logs, float[] scores)
    {
        if (logs == null || scores == null)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append('[');
        bool first = true;
        for (int i = 0; i < logs.Length; i++)
        {
            if (string.IsNullOrEmpty(logs[i]))
                continue;

            if (!first)
                builder.Append(" | ");
            first = false;
            builder.Append('#');
            builder.Append(i + 1);
            builder.Append(" score=");
            builder.Append(Mathf.Sqrt(scores[i]).ToString("F3"));
            builder.Append(' ');
            builder.Append(logs[i]);
        }

        if (first)
            builder.Append("none");
        builder.Append(']');
        return builder.ToString();
    }

    private static Vector3 ClampAgentAvoidance(Vector3 avoidance, Vector3 desiredDirection, float maxSpeed)
    {
        if (avoidance.sqrMagnitude <= 0.0001f || desiredDirection.sqrMagnitude <= 0.0001f)
            return avoidance;

        float forward = Vector3.Dot(avoidance, desiredDirection);
        Vector3 forwardComponent = desiredDirection * Mathf.Clamp(
            forward,
            -maxSpeed * MaxAgentAvoidBackwardSpeedRatio,
            maxSpeed * MaxAgentAvoidLateralSpeedRatio);

        Vector3 lateralComponent = Vector3.ProjectOnPlane(avoidance, desiredDirection);
        lateralComponent.y = 0f;
        lateralComponent = Vector3.ClampMagnitude(lateralComponent, maxSpeed * MaxAgentAvoidLateralSpeedRatio);
        return forwardComponent + lateralComponent;
    }

    private static Vector3 ResolveBoundaryAvoidance(
        Vector3 position,
        Vector3 desiredDirection,
        float radius,
        float speed,
        int agentTypeId,
        out Vector3 edgeNormal,
        out float edgeDistance)
    {
        edgeNormal = Vector3.zero;
        edgeDistance = float.MaxValue;
        if (!TryResolveNavEdgeData(position, agentTypeId, out edgeNormal, out edgeDistance))
            return Vector3.zero;

        float edgeInfluence = radius + _world.CellSize;
        if (edgeDistance >= edgeInfluence)
            return Vector3.zero;

        float pushScale = Mathf.Clamp01(1f - edgeDistance / Mathf.Max(edgeInfluence, 0.001f));
        return edgeNormal * pushScale * speed * Config.BoundaryAvoidanceWeight;
    }

    private static Vector3 ApplyEdgeRecovery(
        Vector3 velocity,
        Vector3 desiredDirection,
        Vector3 edgeNormal,
        float edgeDistance,
        float radius,
        float maxSpeed)
    {
        if (edgeNormal.sqrMagnitude <= 0.0001f || edgeDistance == float.MaxValue)
            return velocity;

        float recoveryBand = radius + _world.CellSize * 0.6f;
        if (edgeDistance >= recoveryBand)
            return velocity;

        float recoveryScale = Mathf.Clamp01(1f - edgeDistance / Mathf.Max(recoveryBand, 0.001f));
        float inwardSpeed = Vector3.Dot(velocity, edgeNormal);
        float minInwardSpeed = maxSpeed * EdgeRecoveryMinSpeedRatio * recoveryScale;
        if (inwardSpeed < minInwardSpeed)
            velocity += edgeNormal * (minInwardSpeed - inwardSpeed);

        Vector3 tangent = Vector3.ProjectOnPlane(velocity, edgeNormal);
        tangent.y = 0f;
        if (tangent.sqrMagnitude <= 0.0001f && desiredDirection.sqrMagnitude > 0.0001f)
            tangent = Vector3.ProjectOnPlane(desiredDirection * maxSpeed, edgeNormal);

        if (tangent.sqrMagnitude > 0.0001f)
        {
            tangent = Vector3.ClampMagnitude(tangent, maxSpeed);
            velocity = edgeNormal * Mathf.Max(Vector3.Dot(velocity, edgeNormal), minInwardSpeed) + tangent;
        }

        return Vector3.ClampMagnitude(velocity, maxSpeed);
    }

    private static bool TryResolveNavEdgeData(Vector3 position, int agentTypeId, out Vector3 edgeNormal, out float edgeDistance)
    {
        edgeNormal = Vector3.zero;
        edgeDistance = float.MaxValue;

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = NavMesh.AllAreas
        };

        float sampleRadius = Mathf.Max(0.2f, _world.CellSize * 0.75f);
        if (!NavMesh.SamplePosition(position, out NavMeshHit navHit, sampleRadius, filter))
            return false;

        if (!NavMesh.FindClosestEdge(navHit.position, out NavMeshHit edgeHit, NavMesh.AllAreas))
            return false;

        edgeNormal = edgeHit.normal;
        edgeNormal.y = 0f;
        if (edgeNormal.sqrMagnitude <= 0.0001f)
            return false;

        edgeNormal.Normalize();
        edgeDistance = edgeHit.distance;
        return true;
    }

    private static void EnsureBottleneckFrame()
    {
        int frameCount = GetFrameCount();
        if (_lastBottleneckFrame == frameCount)
            return;

        _lastBottleneckFrame = frameCount;
        foreach (BottleneckRuntimeState state in Bottlenecks.Values)
        {
            state.OccupiedCount = 0;
            state.LastFrameTouched = frameCount;
        }
    }

    private static void UpdateResolvedVelocity(int agentId, Vector3 goalPosition, Vector3 desiredVelocity, Vector3 resolvedVelocity)
    {
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return;

        agent.NavState.HasGoal = true;
        agent.NavState.LastGoalWorld = goalPosition;
        agent.NavState.DesiredVelocity = desiredVelocity;
        agent.NavState.ResolvedVelocity = resolvedVelocity;
        agent.NavState.CurrentFlowDirection = resolvedVelocity.sqrMagnitude > 0.0001f
            ? resolvedVelocity.normalized
            : desiredVelocity.sqrMagnitude > 0.0001f ? desiredVelocity.normalized : Vector3.zero;
    }

    private static bool TryResolveGoalCell(NavigationWorld world, Vector3 goalPosition, out int goalX, out int goalY)
    {
        if (world.WorldToGrid(goalPosition, out goalX, out goalY) && world.IsWalkable(goalX, goalY))
            return true;

        if (world.WorldToGrid(goalPosition, out goalX, out goalY) && TryFindNearestWalkable(world, goalX, goalY, 6, out goalX, out goalY))
            return true;

        goalX = 0;
        goalY = 0;
        return false;
    }

    private static string BuildGoalResolutionFailure(IEntityContext self, Vector3 goalPosition)
    {
        if (_world == null)
            return $"goal unresolved goal={goalPosition} diag=world-null";

        bool goalInGrid = _world.WorldToGrid(goalPosition, out int goalX, out int goalY);
        bool goalWalkable = goalInGrid && _world.IsWalkable(goalX, goalY);
        bool goalBaseWalkable = false;
        bool nearestWalkableFound = false;
        int nearestWalkableX = 0;
        int nearestWalkableY = 0;
        Vector3 nearestWalkableWorld = Vector3.zero;
        float nearestWalkableDistance = 0f;
        if (goalInGrid)
        {
            int index = _world.GetIndex(goalX, goalY);
            goalBaseWalkable = _world.BaseWalkableMask[index];
            nearestWalkableFound = TryFindNearestWalkable(_world, goalX, goalY, 6, out nearestWalkableX, out nearestWalkableY);
            if (nearestWalkableFound)
            {
                nearestWalkableWorld = _world.GridToWorldCenter(nearestWalkableX, nearestWalkableY);
                nearestWalkableDistance = Vector3.Distance(
                    new Vector3(goalPosition.x, 0f, goalPosition.z),
                    new Vector3(nearestWalkableWorld.x, 0f, nearestWalkableWorld.z));
            }
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float sampleRadius = Mathf.Max(0.2f, _world.CellSize * 1.5f);
        bool navHit = NavMesh.SamplePosition(goalPosition, out NavMeshHit navMeshHit, sampleRadius, filter);
        string navPos = navHit ? navMeshHit.position.ToString() : "none";

        int selfAgentType = self is MAEntity maEntity ? maEntity.navAgentTypeID : 0;
        Vector3 selfPosition = self != null ? self.Position : Vector3.zero;
        bool selfInGrid = _world.WorldToGrid(selfPosition, out int selfX, out int selfY);
        bool selfWalkable = selfInGrid && _world.IsWalkable(selfX, selfY);

        string currentTarget = self?.TargetComp?.CurrentTarget?.CharacterKey ?? "null";
        Vector3 currentTargetPos = self?.TargetComp?.CurrentTarget?.Position ?? Vector3.zero;

        return $"goal unresolved goal={goalPosition} " +
               $"diag=worldAgentType={_world.AgentTypeId} selfAgentType={selfAgentType} " +
               $"worldOrigin={_world.Origin} worldSize={_world.Width}x{_world.Height} cellSize={_world.CellSize:F3} " +
               $"goalCell={(goalInGrid ? $"({goalX},{goalY})" : "out")} goalWalkable={goalWalkable} baseWalkable={goalBaseWalkable} " +
               $"nearestWalkableFound={nearestWalkableFound} nearestWalkableCell={(nearestWalkableFound ? $"({nearestWalkableX},{nearestWalkableY})" : "none")} " +
               $"nearestWalkableWorld={(nearestWalkableFound ? nearestWalkableWorld.ToString() : "none")} nearestWalkableDist={nearestWalkableDistance:F3} " +
               $"navHit={navHit} navPos={navPos} sampleRadius={sampleRadius:F3} " +
               $"selfPos={selfPosition} selfCell={(selfInGrid ? $"({selfX},{selfY})" : "out")} selfWalkable={selfWalkable} " +
               $"target={currentTarget} targetPos={currentTargetPos}";
    }

    private static bool TryFindNearestWalkable(NavigationWorld world, int startX, int startY, int radius, out int resultX, out int resultY)
    {
        for (int r = 1; r <= radius; r++)
        {
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    int nx = startX + x;
                    int ny = startY + y;
                    if (!world.IsWalkable(nx, ny))
                        continue;

                    resultX = nx;
                    resultY = ny;
                    return true;
                }
            }
        }

        resultX = 0;
        resultY = 0;
        return false;
    }

    private static bool HasGridLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int cx = x0;
        int cy = y0;

        while (true)
        {
            if (!world.IsWalkable(cx, cy))
                return false;

            if ((cx != x0 || cy != y0) && !CanTraverseNeighborCells(world, x0, y0, cx, cy))
                return false;

            if (cx == x1 && cy == y1)
                return true;

            int prevX = cx;
            int prevY = cy;
            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                cx += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                cy += sy;
            }

            x0 = prevX;
            y0 = prevY;
        }
    }

    private static bool IsDiagonalPassable(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        return world.IsWalkable(fromX, toY)
               && world.IsWalkable(toX, fromY)
               && CanTraverseNeighborCells(world, fromX, fromY, fromX, toY)
               && CanTraverseNeighborCells(world, fromX, fromY, toX, fromY)
               && CanTraverseNeighborCells(world, fromX, toY, toX, toY)
               && CanTraverseNeighborCells(world, toX, fromY, toX, toY);
    }

    private static void BuildCellNavAnchors(NavigationWorld world)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float sampleRadius = ResolveWalkableRasterSampleRadius(world.CellSize, ResolveAgentTypeRadius(world.AgentTypeId));
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.BaseWalkableMask[index])
                    continue;

                Vector3 center = world.GridToWorldCenter(x, y);
                if (!NavMesh.SamplePosition(center, out NavMeshHit navHit, sampleRadius, filter))
                    throw new InvalidOperationException($"BuildCellNavAnchors failed: walkable cell ({x},{y}) missing NavMesh anchor.");

                world.CellNavAnchors[index] = navHit.position;
            }
        }
    }

    private static bool CanTraverseNeighborCells(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        if (!world.IsWalkable(fromX, fromY) || !world.IsWalkable(toX, toY))
            return false;

        int dx = Mathf.Abs(toX - fromX);
        int dy = Mathf.Abs(toY - fromY);
        if (dx > 1 || dy > 1 || (dx == 0 && dy == 0))
            return false;

        if (dx == 1 && dy == 1 && !IsDiagonalPassable(world, fromX, fromY, toX, toY))
            return false;

        int fromIndex = world.GetIndex(fromX, fromY);
        int toIndex = world.GetIndex(toX, toY);
        Vector3 fromAnchor = world.CellNavAnchors[fromIndex];
        Vector3 toAnchor = world.CellNavAnchors[toIndex];
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        return !NavMesh.Raycast(fromAnchor, toAnchor, out _, filter);
    }

    private static string BuildStartCellDiagnostics(IEntityContext self, Vector3 position)
    {
        if (_world == null)
            return "diag=world-null";

        bool worldInGrid = _world.WorldToGrid(position, out int worldX, out int worldY);
        bool worldWalkable = worldInGrid && _world.IsWalkable(worldX, worldY);

        bool baseWalkable = false;
        if (worldInGrid)
        {
            int worldIndex = _world.GetIndex(worldX, worldY);
            baseWalkable = _world.BaseWalkableMask[worldIndex];
        }

        Fog3MapData mapData = Fog3Manager.Instance != null ? Fog3Manager.Instance.MapData : null;
        bool fogInGrid = false;
        int fogX = 0;
        int fogY = 0;
        bool fogWalkable = false;
        Fog3CellState fogState = Fog3CellState.Outside;
        float fogVisibility = 0f;
        if (mapData != null)
        {
            fogInGrid = mapData.WorldToGrid(position, out fogX, out fogY);
            if (fogInGrid)
            {
                fogWalkable = mapData.IsWalkable(fogX, fogY);
                fogState = mapData.GetCellState(fogX, fogY);
                fogVisibility = mapData.GetVisibility(fogX, fogY);
            }
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float navSampleRadius = Mathf.Max(0.2f, _world.CellSize * 0.75f);
        bool navHit = NavMesh.SamplePosition(position, out NavMeshHit navMeshHit, navSampleRadius, filter);
        string navPos = navHit ? navMeshHit.position.ToString() : "none";
        int navGridX = 0;
        int navGridY = 0;
        bool navGridInWorld = navHit && _world.WorldToGrid(navMeshHit.position, out navGridX, out navGridY);
        bool navGridWalkable = navGridInWorld && _world.IsWalkable(navGridX, navGridY);
        string navGrid = navGridInWorld ? $"({navGridX},{navGridY})" : "out";
        float rasterSampleRadius = ResolveWalkableRasterSampleRadius(_world.CellSize, ResolveAgentTypeRadius(_world.AgentTypeId));
        string probeDiagnostics = worldInGrid
            ? BuildCellProbeDiagnostics(_world.Origin, _world.CellSize, worldX, worldY, rasterSampleRadius, filter)
            : "probes=out";
        string neighborhoodDiagnostics = worldInGrid
            ? BuildWalkableNeighborhoodDiagnostics(_world, worldX, worldY, 2, filter, navSampleRadius)
            : "neighborhood=out";
        string nearestSearchDiagnostics = worldInGrid
            ? BuildNearestWalkableSearchDiagnostics(_world, worldX, worldY, 6)
            : "nearestSearch=out";

        return $"diag=agentType={_world.AgentTypeId} cellSize={_world.CellSize:F3} origin={_world.Origin} size={_world.Width}x{_world.Height} " +
               $"worldCell={(worldInGrid ? $"({worldX},{worldY})" : "out")} worldWalkable={worldWalkable} " +
               $"baseWalkable={baseWalkable} fogCell={(fogInGrid ? $"({fogX},{fogY})" : "out")} fogWalkable={fogWalkable} " +
               $"fogState={fogState} fogVis={fogVisibility:F2} navHit={navHit} navPos={navPos} navGrid={navGrid} navGridWalkable={navGridWalkable} " +
               $"{probeDiagnostics} {nearestSearchDiagnostics} {neighborhoodDiagnostics}";
    }

    private static int ResolveAgentId(IEntityContext entity)
    {
        return entity is MAEntity ma ? ma.GetInstanceID() : entity.GetHashCode();
    }

    private static int ResolvePreferredAgentTypeId(int preferredAgentTypeId)
    {
        if (preferredAgentTypeId != MAEntity.UnknownNavAgentTypeId)
            return preferredAgentTypeId;

        int firstAgentType = MAEntity.UnknownNavAgentTypeId;
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (firstAgentType == MAEntity.UnknownNavAgentTypeId)
                firstAgentType = agent.AgentTypeId;

            if (agent.AgentTypeId != MAEntity.UnknownNavAgentTypeId)
                return agent.AgentTypeId;
        }

        int settingsCount = NavMesh.GetSettingsCount();
        if (settingsCount <= 0)
            return firstAgentType;

        int fallbackAgentType = NavMesh.GetSettingsByIndex(0).agentTypeID;
        return fallbackAgentType;
    }

    private static float ResolveAgentTypeRadius(int agentTypeId)
    {
        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            return 0.5f;

        int settingsCount = NavMesh.GetSettingsCount();
        for (int i = 0; i < settingsCount; i++)
        {
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(i);
            if (settings.agentTypeID != agentTypeId)
                continue;

            return Mathf.Max(0.05f, settings.agentRadius);
        }

        return 0.5f;
    }

    private static Bounds CalculateNavMeshBounds(NavMeshTriangulation triangulation)
    {
        if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            throw new InvalidOperationException("CalculateNavMeshBounds failed: NavMesh has no vertices.");

        Bounds bounds = new Bounds(triangulation.vertices[0], Vector3.zero);
        for (int i = 1; i < triangulation.vertices.Length; i++)
            bounds.Encapsulate(triangulation.vertices[i]);

        return bounds;
    }

    private static float ResolveCollisionRadius(IEntityContext entity)
    {
        float radius = DistanceUnitConverter.ConvertToWorldFloat(entity.GetProperty(CreatureMainProperty.CollisionRadius));
        return radius > 0.0001f ? radius : 0.5f;
    }
}
