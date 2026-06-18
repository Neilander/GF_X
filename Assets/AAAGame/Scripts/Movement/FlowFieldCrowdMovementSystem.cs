using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAAGame.MiniMap.FOG3;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

public static class FlowFieldCrowdMovementSystem
{
    private const int AnyAgentTypeId = int.MinValue;
    private const float MaxAgentAvoidBackwardSpeedRatio = 0.35f;
    private const float MaxAgentAvoidLateralSpeedRatio = 0.75f;
    private const float IdleOverlapRecoverySpeed = 0.65f;
    private const float EdgeRecoveryMinSpeedRatio = 0.28f;
    private const int PortalApproachProbeDepth = 2;
    private const int NavigationGoalCandidateCount = 16;
    private const int NavigationGoalRingCount = 3;
    private const float NavigationGoalOccupancyPadding = 0.35f;
    private static readonly long FlowPerfLogThresholdTicks = Stopwatch.Frequency * 4 / 1000;
    private const float WallCostBlurRadiusCells = 2.25f;
    private const int WallCostAdjacentPenalty = 7;
    private const int WallCostOuterPenalty = 3;

    private sealed class RuntimeConfig
    {
        public float NavigationCellSize;
        public float NavigationBoundsPadding = 0.6f;
        public int SectorSizeInCells = 12;
        public int PortalNarrowWidthCells = 2;
        public int FlowTileCacheLimit = 256;
        public float RuntimeRebuildBudgetMilliseconds = 1.5f;
        public float CrowdPredictionTime = 0.35f;
        public float LaneBiasStrength = 0.22f;
        public float BoundaryAvoidanceWeight = 1f;
        public float PathDirectionBlend = 0.35f;
        public float BottleneckSwitchCooldown = 0.35f;
        public float BottleneckWaitTimeout = 1.25f;
        public float BottleneckInfluenceDistance = 2.2f;
        public float BottleneckClearanceHoldTime = 0.45f;
        public bool DrawNavigationDebug = true;
        public bool DrawFlowFieldDebug;
        public bool StrictNoFallback = true;
    }

    private sealed class NavigationWorld
    {
        public int Version;
        public int AgentTypeId;
        public bool RequiresNavMeshAnchors;
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public bool[] BaseWalkableMask;
        public bool[] WalkableMask;
        public byte[] CostField;
        public Vector3[] CellNavAnchors;
        public byte[] NeighborTraversalMask;
        public int[] IslandIds;
        public int IslandCount;
        public int MainIslandId;
        public int MainIslandSize;
        public int SectorSizeInCells;
        public int SectorCountX;
        public int SectorCountY;
        public SectorData[] Sectors;
        public PortalData[] Portals;
        public Dictionary<int, PortalData> PortalsById;
        public readonly Dictionary<PortalSignature, int> PortalIdsBySignature = new Dictionary<PortalSignature, int>();
        public readonly HashSet<int> UsedPortalIds = new HashSet<int>();
        public int NextPortalId;

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

    private readonly struct PortalSignature : IEquatable<PortalSignature>
    {
        public readonly int MinSectorId;
        public readonly int MaxSectorId;
        public readonly bool IsVerticalBoundary;
        public readonly int Boundary;
        public readonly int FirstAxis;
        public readonly int LastAxis;
        public readonly int WidthCells;

        public PortalSignature(int sectorAId, int sectorBId, bool isVerticalBoundary, int boundary, int firstAxis, int lastAxis, int widthCells)
        {
            MinSectorId = Mathf.Min(sectorAId, sectorBId);
            MaxSectorId = Mathf.Max(sectorAId, sectorBId);
            IsVerticalBoundary = isVerticalBoundary;
            Boundary = boundary;
            FirstAxis = firstAxis;
            LastAxis = lastAxis;
            WidthCells = widthCells;
        }

        public bool Equals(PortalSignature other)
        {
            return MinSectorId == other.MinSectorId
                   && MaxSectorId == other.MaxSectorId
                   && IsVerticalBoundary == other.IsVerticalBoundary
                   && Boundary == other.Boundary
                   && FirstAxis == other.FirstAxis
                   && LastAxis == other.LastAxis
                   && WidthCells == other.WidthCells;
        }

        public override bool Equals(object obj)
        {
            return obj is PortalSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MinSectorId;
                hash = (hash * 397) ^ MaxSectorId;
                hash = (hash * 397) ^ (IsVerticalBoundary ? 1 : 0);
                hash = (hash * 397) ^ Boundary;
                hash = (hash * 397) ^ FirstAxis;
                hash = (hash * 397) ^ LastAxis;
                hash = (hash * 397) ^ WidthCells;
                return hash;
            }
        }
    }

    private sealed class PathHandle
    {
        public int HandleId;
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

    private readonly struct SectorPathCacheKey : IEquatable<SectorPathCacheKey>
    {
        public readonly int WorldVersion;
        public readonly int StartSectorId;
        public readonly int StartCellIndex;
        public readonly int GoalSectorId;
        public readonly int GoalCellIndex;
        public readonly int StartSectorDirtyVersion;
        public readonly int GoalSectorDirtyVersion;

        public SectorPathCacheKey(int worldVersion, int startSectorId, int startCellIndex, int goalSectorId, int goalCellIndex, int startSectorDirtyVersion, int goalSectorDirtyVersion)
        {
            WorldVersion = worldVersion;
            StartSectorId = startSectorId;
            StartCellIndex = startCellIndex;
            GoalSectorId = goalSectorId;
            GoalCellIndex = goalCellIndex;
            StartSectorDirtyVersion = startSectorDirtyVersion;
            GoalSectorDirtyVersion = goalSectorDirtyVersion;
        }

        public bool Equals(SectorPathCacheKey other)
        {
            return WorldVersion == other.WorldVersion
                   && StartSectorId == other.StartSectorId
                   && StartCellIndex == other.StartCellIndex
                   && GoalSectorId == other.GoalSectorId
                   && GoalCellIndex == other.GoalCellIndex
                   && StartSectorDirtyVersion == other.StartSectorDirtyVersion
                   && GoalSectorDirtyVersion == other.GoalSectorDirtyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is SectorPathCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ StartSectorId;
                hash = (hash * 397) ^ StartCellIndex;
                hash = (hash * 397) ^ GoalSectorId;
                hash = (hash * 397) ^ GoalCellIndex;
                hash = (hash * 397) ^ StartSectorDirtyVersion;
                hash = (hash * 397) ^ GoalSectorDirtyVersion;
                return hash;
            }
        }
    }

    private sealed class SectorPathCacheEntry
    {
        public int[] SectorIds;
        public int[] PortalIds;
        public int LastUsedFrame;
    }

    private readonly struct SectorPortalAccessKey : IEquatable<SectorPortalAccessKey>
    {
        public readonly int WorldVersion;
        public readonly int SectorId;
        public readonly int PortalId;
        public readonly int SectorDirtyVersion;

        public SectorPortalAccessKey(int worldVersion, int sectorId, int portalId, int sectorDirtyVersion)
        {
            WorldVersion = worldVersion;
            SectorId = sectorId;
            PortalId = portalId;
            SectorDirtyVersion = sectorDirtyVersion;
        }

        public bool Equals(SectorPortalAccessKey other)
        {
            return WorldVersion == other.WorldVersion
                   && SectorId == other.SectorId
                   && PortalId == other.PortalId
                   && SectorDirtyVersion == other.SectorDirtyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is SectorPortalAccessKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ SectorId;
                hash = (hash * 397) ^ PortalId;
                hash = (hash * 397) ^ SectorDirtyVersion;
                return hash;
            }
        }
    }

    private sealed class SectorPortalAccessEntry
    {
        public float[] Integration;
        public int LastUsedFrame;
    }

    private readonly struct SharedGoalFieldKey : IEquatable<SharedGoalFieldKey>
    {
        public readonly int WorldVersion;
        public readonly int AgentTypeId;
        public readonly int GoalSectorId;
        public readonly int GoalCellIndex;
        public readonly int GoalSectorDirtyVersion;

        public SharedGoalFieldKey(int worldVersion, int agentTypeId, int goalSectorId, int goalCellIndex, int goalSectorDirtyVersion)
        {
            WorldVersion = worldVersion;
            AgentTypeId = agentTypeId;
            GoalSectorId = goalSectorId;
            GoalCellIndex = goalCellIndex;
            GoalSectorDirtyVersion = goalSectorDirtyVersion;
        }

        public bool Equals(SharedGoalFieldKey other)
        {
            return WorldVersion == other.WorldVersion
                   && AgentTypeId == other.AgentTypeId
                   && GoalSectorId == other.GoalSectorId
                   && GoalCellIndex == other.GoalCellIndex
                   && GoalSectorDirtyVersion == other.GoalSectorDirtyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is SharedGoalFieldKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ GoalSectorId;
                hash = (hash * 397) ^ GoalCellIndex;
                hash = (hash * 397) ^ GoalSectorDirtyVersion;
                return hash;
            }
        }
    }

    private sealed class SharedGoalField
    {
        public SharedGoalFieldKey Key;
        public int GoalSectorId;
        public int GoalX;
        public int GoalY;
        public readonly Dictionary<int, float> NodeCosts = new Dictionary<int, float>(256);
        public readonly Dictionary<int, int> NextNodeTowardGoal = new Dictionary<int, int>(256);
        public int LastUsedFrame;
    }

    private sealed class StrictPortalWindowUnreachableException : InvalidOperationException
    {
        public StrictPortalWindowUnreachableException(string message) : base(message)
        {
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
        public Vector3 PathDirection;
        public Vector2Int PathDirectionCell = new Vector2Int(int.MinValue, int.MinValue);
        public Vector3 DesiredVelocity;
        public Vector3 PreviousResolvedVelocity;
        public Vector3 ResolvedVelocity;
        public int ResolvedVelocityFrame = -1;
        public MovementMode LastMovementMode = MovementMode.Normal;
        public Vector3 LastGoalWorld;
        public bool HasGoal;
        public int StableGoalX = -1;
        public int StableGoalY = -1;
        public int StableGoalRawX = -1;
        public int StableGoalRawY = -1;
        public Vector3 StableGoalWorld;
        public int StableGoalTargetId = int.MinValue;
        public int BottleneckLaneAxisMode;
        public float BottleneckLaneSign;
        public int LastSteeringFrame = -1;
        public Vector3 LastSteeringGoal;
        public Vector3 LastSteeringDesiredDirection;
        public Vector3 LastSteeringDesiredVelocity;
        public Vector3 LastSteeringBaseVelocity;
        public Vector3 LastSteeringAgentAvoidance;
        public Vector3 LastSteeringClampedAgentAvoidance;
        public Vector3 LastSteeringBoundaryAvoidance;
        public Vector3 LastSteeringLaneVelocity;
        public Vector3 LastSteeringResultPreClamp;
        public Vector3 LastSteeringResult;
        public Vector3 LastSteeringEdgeNormal;
        public float LastSteeringEdgeDistance = float.MaxValue;
        public DesiredDirectionSource LastSteeringDesiredSource = DesiredDirectionSource.Zero;
        public Vector2 LastSteeringFlow;
        public bool LastSteeringHasLineOfSight;
        public Vector3 LastSteeringTileTarget;
        public float LastSteeringIntegration;
        public float LastSteeringMaxSpeed;
        public int LastConstraintDiagnosticFrame = -1;
        public int LastFlowExecutionMismatchDiagnosticFrame = -1;
        public int LastCombatClusterDiagnosticFrame = -1;
        public int LastIdleOverlapDiagnosticFrame = -1;
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
        public bool HasNavigationIntent;
        public int LastAvoidanceActiveFrame = -1024;
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
        Zero = 2
    }

    private enum AvoidanceEncounterType
    {
        None = 0,
        SameLaneFollow = 1,
        SameLaneOpposing = 2,
        Crossing = 3,
        Overtaking = 4,
        StaticOverlap = 5
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

    private readonly struct NavigationGoalReservation
    {
        public readonly int SelfId;
        public readonly Vector3 Point;
        public readonly float RequiredDistance;

        public NavigationGoalReservation(int selfId, Vector3 point, float requiredDistance)
        {
            SelfId = selfId;
            Point = point;
            RequiredDistance = requiredDistance;
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

    private readonly struct CachedPortalTarget
    {
        public readonly Vector3 TargetPosition;
        public readonly int VisibleCandidateCount;
        public readonly int SelectedPairIndex;
        public readonly bool UsedOppositeCenter;

        public CachedPortalTarget(Vector3 targetPosition, int visibleCandidateCount, int selectedPairIndex, bool usedOppositeCenter)
        {
            TargetPosition = targetPosition;
            VisibleCandidateCount = visibleCandidateCount;
            SelectedPairIndex = selectedPairIndex;
            UsedOppositeCenter = usedOppositeCenter;
        }
    }

    private readonly struct FlowTileCacheKey : IEquatable<FlowTileCacheKey>
    {
        public readonly int WorldVersion;
        public readonly int SectorId;
        public readonly TileGoalKind GoalKind;
        public readonly int GoalId;
        public readonly int DownstreamGoalHint;
        public readonly int FinalGoalIndex;
        public readonly int AgentTypeId;
        public readonly int DirtyVersion;

        public FlowTileCacheKey(int worldVersion, int sectorId, TileGoalKind goalKind, int goalId, int downstreamGoalHint, int finalGoalIndex, int agentTypeId, int dirtyVersion)
        {
            WorldVersion = worldVersion;
            SectorId = sectorId;
            GoalKind = goalKind;
            GoalId = goalId;
            DownstreamGoalHint = downstreamGoalHint;
            FinalGoalIndex = finalGoalIndex;
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
                   && FinalGoalIndex == other.FinalGoalIndex
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
                hash = (hash * 397) ^ FinalGoalIndex;
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
        public bool[] WaveFrontBlocked;
        public CachedPortalTarget[] PortalTargets;
        public Vector2Int[] GoalCells;
        public int LastUsedFrame;

        public int GetLocalIndex(int worldX, int worldY)
        {
            return (worldX - StartX) + (worldY - StartY) * Width;
        }
    }

    private readonly struct FlowTileBuildKey : IEquatable<FlowTileBuildKey>
    {
        public readonly FlowTileCacheKey CacheKey;
        public readonly int PathHandleId;
        public readonly int SectorPathIndex;
        public readonly int GoalX;
        public readonly int GoalY;
        public readonly int AgentTypeId;

        public FlowTileBuildKey(FlowTileCacheKey cacheKey, int pathHandleId, int sectorPathIndex, int goalX, int goalY, int agentTypeId)
        {
            CacheKey = cacheKey;
            PathHandleId = pathHandleId;
            SectorPathIndex = sectorPathIndex;
            GoalX = goalX;
            GoalY = goalY;
            AgentTypeId = agentTypeId;
        }

        public bool Equals(FlowTileBuildKey other)
        {
            return CacheKey.Equals(other.CacheKey)
                   && PathHandleId == other.PathHandleId
                   && SectorPathIndex == other.SectorPathIndex
                   && GoalX == other.GoalX
                   && GoalY == other.GoalY
                   && AgentTypeId == other.AgentTypeId;
        }

        public override bool Equals(object obj)
        {
            return obj is FlowTileBuildKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CacheKey.GetHashCode();
                hash = (hash * 397) ^ PathHandleId;
                hash = (hash * 397) ^ SectorPathIndex;
                hash = (hash * 397) ^ GoalX;
                hash = (hash * 397) ^ GoalY;
                hash = (hash * 397) ^ AgentTypeId;
                return hash;
            }
        }
    }

    private sealed class FlowTileBuildJob
    {
        public FlowTileBuildKey BuildKey;
        public PathHandle HandleSnapshot;
        public FlowTileBuildStage Stage;
        public SectorData Sector;
        public FlowTileCacheEntry Tile;
        public FlowTileCacheEntry DownstreamTile;
        public IntegrationSeed[] Seeds;
        public Queue<int> LineOfSightOpenQueue;
        public MinHeap IntegrationOpenSet;
        public int CellCursor;
        public int PortalHandoffCursor;
        public bool WaitingForDependency;
    }

    private enum FlowTileBuildStage
    {
        Prepare = 0,
        LineOfSight = 1,
        Integration = 2,
        FlowDirections = 3,
        PortalHandoff = 4,
        Commit = 5,
        Complete = 6
    }

    private sealed class BottleneckRuntimeState
    {
        public enum OwnerState
        {
            Idle = 0,
            Holding = 1,
            Queueing = 2,
            Passing = 3,
            Switching = 4
        }

        public int BottleneckId;
        public int PortalId = -1;
        public int CorridorAxisMode;
        public Vector3 CorridorAxis;
        public int CurrentDirection;
        public int CurrentOwnerAgentId = -1;
        public int ConvoyTokenAgentId = -1;
        public int PriorityOverrideAgentId = -1;
        public OwnerState CurrentOwnerState;
        public float SwitchBlockedUntil;
        public int OccupiedCount;
        public float LastOccupiedTime = float.NegativeInfinity;
        public int LastFrameTouched = -1;
        public readonly Dictionary<int, float> WaitingStartTimes = new Dictionary<int, float>();
        public readonly Dictionary<int, float> AgentLaneSigns = new Dictionary<int, float>();
        public readonly List<int> WaitingAgentsOrdered = new List<int>(8);
    }

    private sealed class TestTerrainOverride
    {
        public int AgentTypeId;
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public bool[] WalkableMask;
        public Vector3[] CellNavAnchors;
    }

    private sealed class CircleObstacle
    {
        public int Id;
        public Vector3 Position;
        public float Radius;
    }

    private readonly struct MovingTargetAnchorKey : IEquatable<MovingTargetAnchorKey>
    {
        public readonly int TargetId;

        public MovingTargetAnchorKey(int targetId)
        {
            TargetId = targetId;
        }

        public bool Equals(MovingTargetAnchorKey other)
        {
            return TargetId == other.TargetId;
        }

        public override bool Equals(object obj)
        {
            return obj is MovingTargetAnchorKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return TargetId;
        }
    }

    private sealed class MovingTargetAnchor
    {
        public MovingTargetAnchorKey Key;
        public int RawGoalX = -1;
        public int RawGoalY = -1;
        public int ActiveGoalX = -1;
        public int ActiveGoalY = -1;
        public int ActiveGoalSectorId = -1;
        public int ActiveWorldVersion = -1;
        public Vector3 ActiveGoalWorld;
        public int LastUsedFrame = -1;
    }

    private sealed class BoxObstacle
    {
        public int Id;
        public Vector3 Center;
        public Vector3 HalfExtents;
    }

    private sealed class CostStamp
    {
        public int Id;
        public int AgentTypeId;
        public Bounds Bounds;
        public byte Cost;
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
        public readonly bool EnforceLaneCommitment;
        public readonly int ConvoyTokenAgentId;
        public readonly int PriorityOverrideAgentId;
        public readonly BottleneckRuntimeState.OwnerState OwnerState;

        public BottleneckDecision(
            float speedScale,
            Vector3 laneBias,
            Vector3 queueBias,
            bool enforceLaneCommitment,
            int convoyTokenAgentId,
            int priorityOverrideAgentId,
            BottleneckRuntimeState.OwnerState ownerState)
        {
            SpeedScale = speedScale;
            LaneBias = laneBias;
            QueueBias = queueBias;
            EnforceLaneCommitment = enforceLaneCommitment;
            ConvoyTokenAgentId = convoyTokenAgentId;
            PriorityOverrideAgentId = priorityOverrideAgentId;
            OwnerState = ownerState;
        }
    }

    private readonly struct CorridorBottleneckDescriptor
    {
        public readonly int BottleneckId;
        public readonly int SeedPortalId;
        public readonly int SectorId;
        public readonly int Direction;
        public readonly Vector3 CorridorAxis;
        public readonly Vector3 AnchorWorld;
        public readonly bool IsLongCorridor;
        public readonly bool IsCornerExit;
        public readonly float InfluenceRadius;

        public CorridorBottleneckDescriptor(
            int bottleneckId,
            int seedPortalId,
            int sectorId,
            int direction,
            Vector3 corridorAxis,
            Vector3 anchorWorld,
            bool isLongCorridor,
            bool isCornerExit,
            float influenceRadius)
        {
            BottleneckId = bottleneckId;
            SeedPortalId = seedPortalId;
            SectorId = sectorId;
            Direction = direction;
            CorridorAxis = corridorAxis;
            AnchorWorld = anchorWorld;
            IsLongCorridor = isLongCorridor;
            IsCornerExit = isCornerExit;
            InfluenceRadius = influenceRadius;
        }
    }

    private static readonly BottleneckDecision FreeMoveDecision =
        new BottleneckDecision(1f, Vector3.zero, Vector3.zero, false, -1, -1, BottleneckRuntimeState.OwnerState.Idle);

    private static readonly RuntimeConfig Config = new RuntimeConfig();
    private static readonly Dictionary<int, AgentRuntimeData> Agents = new Dictionary<int, AgentRuntimeData>();
    private static readonly Dictionary<int, CircleObstacle> CircleObstacles = new Dictionary<int, CircleObstacle>();
    private static readonly Dictionary<int, BoxObstacle> BoxObstacles = new Dictionary<int, BoxObstacle>();
    private static readonly Dictionary<int, CostStamp> CostStamps = new Dictionary<int, CostStamp>();
    private static readonly Dictionary<MovingTargetAnchorKey, MovingTargetAnchor> MovingTargetAnchors = new Dictionary<MovingTargetAnchorKey, MovingTargetAnchor>();
    private static readonly Dictionary<FlowTileCacheKey, FlowTileCacheEntry> FlowTileCache = new Dictionary<FlowTileCacheKey, FlowTileCacheEntry>();
    private static readonly HashSet<FlowTileCacheKey> PendingTileBuildKeys = new HashSet<FlowTileCacheKey>();
    private static readonly LinkedList<FlowTileBuildJob> FlowTileBuildQueue = new LinkedList<FlowTileBuildJob>();
    private static readonly HashSet<FlowTileBuildKey> PendingFlowTileBuildJobs = new HashSet<FlowTileBuildKey>();
    private static readonly Dictionary<SectorPathCacheKey, SectorPathCacheEntry> SectorPathCache = new Dictionary<SectorPathCacheKey, SectorPathCacheEntry>();
    private static readonly Dictionary<SectorPortalAccessKey, SectorPortalAccessEntry> SectorPortalAccessCache = new Dictionary<SectorPortalAccessKey, SectorPortalAccessEntry>();
    private static readonly Dictionary<SharedGoalFieldKey, SharedGoalField> SharedGoalFields = new Dictionary<SharedGoalFieldKey, SharedGoalField>();
    private static readonly Dictionary<int, BottleneckRuntimeState> Bottlenecks = new Dictionary<int, BottleneckRuntimeState>();
    private static readonly Dictionary<int, CorridorBottleneckDescriptor> CorridorBottlenecks = new Dictionary<int, CorridorBottleneckDescriptor>();
    private static readonly MinHeap OpenSet = new MinHeap();
    private static readonly Dictionary<int, float> PortalNodeGScore = new Dictionary<int, float>();
    private static readonly Dictionary<int, int> PortalNodeCameFrom = new Dictionary<int, int>();

    private static readonly int[] NeighborOffsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] NeighborOffsetY = { -1, -1, -1, 0, 0, 1, 1, 1 };
    private static readonly int[] CardinalOffsetX = { -1, 1, 0, 0 };
    private static readonly int[] CardinalOffsetY = { 0, 0, -1, 1 };
    private static readonly int[] CorridorAxisProbeOffsets = { -2, -1, 1, 2 };
    private static readonly Dictionary<int, List<AgentRuntimeData>> AgentSpatialBuckets = new Dictionary<int, List<AgentRuntimeData>>();
    private static readonly Dictionary<int, List<AgentRuntimeData>> NavigationGoalOccupancyBuckets = new Dictionary<int, List<AgentRuntimeData>>();
    private static readonly List<AgentRuntimeData> NearbyAgentScratch = new List<AgentRuntimeData>(32);
    private static readonly List<AgentRuntimeData> CombatClusterScratch = new List<AgentRuntimeData>(16);
    private static readonly List<NavigationGoalReservation> NavigationGoalReservations = new List<NavigationGoalReservation>(128);
    private static float[] WallDistanceScratch = Array.Empty<float>();
    private static int _navigationGoalReservationFrame = -1;
    private static int _lastNavigationGoalOccupancyBucketFrame = -1;
    private static int _lastNavigationGoalOccupancyBucketWorldVersion = -1;

    private static int ResolveNeighborOffsetIndex(int dx, int dy)
    {
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            if (NeighborOffsetX[i] == dx && NeighborOffsetY[i] == dy)
                return i;
        }

        return -1;
    }

    private static bool HasRawNeighborTraversal(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        if (world == null)
            throw new InvalidOperationException("HasRawNeighborTraversal failed: world is null.");
        if (!world.IsWalkable(fromX, fromY) || !world.IsWalkable(toX, toY))
            return false;

        int dx = toX - fromX;
        int dy = toY - fromY;
        int offsetIndex = ResolveNeighborOffsetIndex(dx, dy);
        if (offsetIndex < 0)
            return false;

        int fromIndex = world.GetIndex(fromX, fromY);
        return (world.NeighborTraversalMask[fromIndex] & (1 << offsetIndex)) != 0;
    }

    private sealed class WorldRuntimeState
    {
        public int AgentTypeId;
        public NavigationWorld World;
        public bool IsDirty = true;
        public readonly HashSet<int> DirtyRuntimeObstacleSectors = new HashSet<int>();
        public RuntimeDirtyRebuildJob RuntimeDirtyJob;
        public WorldBuildJob BuildJob;
    }

    private enum WorldBuildStage
    {
        Initialize = 0,
        Rasterize = 1,
        CreateWorldShell = 2,
        ApplyRuntimeObstacles = 3,
        InitializeSectors = 4,
        BuildCellNavAnchors = 5,
        NeighborMask = 6,
        SymmetrizeNeighborMask = 7,
        CostField = 8,
        IslandField = 9,
        PortalGraph = 10,
        Commit = 11,
        Complete = 12
    }

    private sealed class WorldBuildJob
    {
        public int AgentTypeId;
        public WorldBuildStage Stage;
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public bool[] BaseWalkableMask;
        public Vector3[] CellNavAnchors;
        public bool RequiresNavMeshAnchors;
        public NavMeshQueryFilter Filter;
        public float SampleRadius;
        public int RasterCursor;
        public NavigationWorld WorkingWorld;
        public List<CircleObstacle> CircleObstacles;
        public List<BoxObstacle> BoxObstacles;
        public List<CostStamp> CostStamps;
        public int ObstacleCursor;
        public bool ApplyingCircleObstacles = true;
        public int SectorCursor;
        public int CellCursor;
        public Queue<int> IslandOpenQueue;
        public int IslandScanIndex;
        public int IslandCurrentId;
        public int IslandCurrentSize;
        public int IslandMainId;
        public int IslandMainSize;
        public bool IslandBfsActive;
        public bool IslandInitialized;
        public RuntimeDirtyPortalStage PortalStage;
        public bool PortalInitialized;
        public List<PortalData> PortalRebuiltPortals;
        public int PortalAddCursor;
        public int PortalTransitionCursor;
        public int PortalTransitionFromCursor;
        public bool PortalTransitionIntegrationActive;
        public int PortalTransitionFromPortalId;
        public float[] PortalTransitionIntegration;
        public MinHeap PortalTransitionOpenSet;
        public string Reason;
    }

    private enum RuntimeDirtyRebuildStage
    {
        ResetWalkable = 0,
        ApplyObstacles = 1,
        NeighborMask = 2,
        SymmetrizeNeighborMask = 3,
        CostField = 4,
        IslandField = 5,
        PortalGraph = 6,
        Commit = 7,
        Complete = 8
    }

    private enum RuntimeDirtyPortalStage
    {
        RemoveOldPortals = 0,
        RebuildBoundaries = 1,
        AddRebuiltPortals = 2,
        RebuildTransitions = 3,
        Complete = 4
    }

    private sealed class RuntimeDirtyRebuildJob
    {
        public NavigationWorld TargetWorld;
        public NavigationWorld WorkingWorld;
        public HashSet<int> DirtySectors;
        public HashSet<int> CostDirtySectors;
        public List<int> DirtySectorIds;
        public List<int> CostDirtySectorIds;
        public List<CircleObstacle> CircleObstacles;
        public List<BoxObstacle> BoxObstacles;
        public List<CostStamp> CostStamps;
        public RuntimeDirtyRebuildStage Stage;
        public int SectorCursor;
        public int ObstacleCursor;
        public bool ApplyingCircleObstacles = true;
        public Queue<int> IslandOpenQueue;
        public int IslandScanIndex;
        public int IslandCurrentId;
        public int IslandCurrentSize;
        public int IslandMainId;
        public int IslandMainSize;
        public bool IslandBfsActive;
        public bool IslandInitialized;
        public RuntimeDirtyPortalStage PortalStage;
        public bool PortalInitialized;
        public HashSet<int> PortalTransitionDirtySectors;
        public List<int> PortalTransitionSectorIds;
        public List<PortalData> PortalRebuiltPortals;
        public HashSet<long> PortalProcessedBoundaries;
        public int PortalSectorCursor;
        public int PortalAddCursor;
        public int PortalTransitionCursor;
        public int PortalTransitionFromCursor;
        public bool PortalTransitionIntegrationActive;
        public int PortalTransitionFromPortalId;
        public float[] PortalTransitionIntegration;
        public MinHeap PortalTransitionOpenSet;
        public string Reason;
    }

    private struct FlowPerfAccumulator
    {
        public int Frame;
        public int Calls;
        public int WorldBuilds;
        public int PathBuilds;
        public int SectorPathSearches;
        public int SectorPathCacheHits;
        public int TileBuilds;
        public int NeighborChecks;
        public int PathBuildNoHandle;
        public int PathBuildWorldMismatch;
        public int PathBuildGoalSectorMismatch;
        public int PathBuildGoalCellMismatch;
        public int PathBuildInvalidHandle;
        public int PathAdvanceFailures;
        public int TileReachabilityFailures;
        public int StableGoalRaw;
        public int StableGoalReuse;
        public int StableGoalRefreshInitial;
        public int StableGoalRefreshCellDelta;
        public int TileCacheHits;
        public int TileCacheMisses;
        public int RuntimeDirtyApplications;
        public int RuntimeDirtySectorCount;
        public long TotalTicks;
        public long WorldTicks;
        public long ResolveCellsTicks;
        public long PathTicks;
        public long AdvanceTicks;
        public long TileTicks;
        public long DesiredTicks;
        public long BottleneckTicks;
        public long SteeringTicks;
        public long NeighborCollectTicks;
        public long NeighborAvoidTicks;
        public long BoundaryTicks;
        public long AgentUpdateTicks;
    }

    private static readonly Dictionary<int, WorldRuntimeState> WorldStates = new Dictionary<int, WorldRuntimeState>();
    private static readonly List<WorldRuntimeState> RuntimeRebuildQueueScratch = new List<WorldRuntimeState>(8);
    private static NavigationWorld _world;
    private static WorldRuntimeState _activeWorldState;
    private static int _nextWorldVersion = 1;
    private static int _nextPathHandleId = 1;
    private static int _lastBottleneckFrame = -1;
    private static int _lastAgentSpatialBucketFrame = -1;
    private static int _lastAgentSpatialBucketWorldVersion = -1;
    private static int _lastOverlapDiagnosticsFrame = -1;
    private static string _lastWorldDirtyReason = "initial";
    private static string _lastRuntimeObstacleDirtyReason = "none";
    private static TestTerrainOverride _testTerrainOverride;
#if UNITY_EDITOR
    private static readonly Dictionary<int, float> TestAgentTypeRadii = new Dictionary<int, float>();
#endif
    private static bool _hasTestTimeOverride;
    private static int _testFrameCount;
    private static float _testTime;
    private static FlowPerfAccumulator _perf;
    private static bool _perfInitialized;

    public static void ResetAll()
    {
        Agents.Clear();
        AgentSpatialBuckets.Clear();
        NavigationGoalOccupancyBuckets.Clear();
        NearbyAgentScratch.Clear();
        CircleObstacles.Clear();
        BoxObstacles.Clear();
        CostStamps.Clear();
        MovingTargetAnchors.Clear();
        FlowTileCache.Clear();
        PendingTileBuildKeys.Clear();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        SectorPathCache.Clear();
        SectorPortalAccessCache.Clear();
        SharedGoalFields.Clear();
        Bottlenecks.Clear();
        CorridorBottlenecks.Clear();
        WorldStates.Clear();
        RuntimeRebuildQueueScratch.Clear();
        _world = null;
        _activeWorldState = null;
        _nextWorldVersion = 1;
        _nextPathHandleId = 1;
        _lastBottleneckFrame = -1;
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentSpatialBucketWorldVersion = -1;
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
        _lastOverlapDiagnosticsFrame = -1;
#if UNITY_EDITOR
        TestAgentTypeRadii.Clear();
#endif
        _perf = default;
        _perfInitialized = false;
    }

    private static void ClearAgentFromBottlenecks(int agentId)
    {
        foreach (BottleneckRuntimeState state in Bottlenecks.Values)
        {
            state.WaitingStartTimes.Remove(agentId);
            state.AgentLaneSigns.Remove(agentId);
            if (state.CurrentOwnerAgentId == agentId)
                state.CurrentOwnerAgentId = -1;
            if (state.ConvoyTokenAgentId == agentId)
                state.ConvoyTokenAgentId = -1;
            if (state.PriorityOverrideAgentId == agentId)
                state.PriorityOverrideAgentId = -1;
            if (state.CurrentOwnerState != BottleneckRuntimeState.OwnerState.Idle)
                state.CurrentOwnerState = BottleneckRuntimeState.OwnerState.Idle;
        }
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
            WalkableMask = (bool[])walkableMask.Clone(),
            CellNavAnchors = null
        };
        MarkWorldDirty();
    }

    public static void ClearEditorTestNavigationSource()
    {
        _testTerrainOverride = null;
        MarkWorldDirty();
    }

    public static void SetEditorTestAgentTypeRadius(int agentTypeId, float radius)
    {
        TestAgentTypeRadii[agentTypeId] = Mathf.Max(0.05f, radius);
        MarkWorldDirty();
    }

    public static void ClearEditorTestAgentTypeRadii()
    {
        TestAgentTypeRadii.Clear();
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
        Config.RuntimeRebuildBudgetMilliseconds = Mathf.Max(0.05f, config.RuntimeRebuildBudgetMilliseconds);
        Config.CrowdPredictionTime = Mathf.Max(0.05f, config.CrowdPredictionTime);
        Config.LaneBiasStrength = Mathf.Max(0f, config.LaneBiasStrength);
        Config.BoundaryAvoidanceWeight = Mathf.Max(0f, config.BoundaryAvoidanceWeight);
        Config.PathDirectionBlend = Mathf.Clamp01(config.VelocitySmoothing);
        Config.BottleneckSwitchCooldown = Mathf.Max(0f, config.BottleneckSwitchCooldown);
        Config.BottleneckWaitTimeout = Mathf.Max(0.1f, config.BottleneckWaitTimeout);
        Config.BottleneckInfluenceDistance = Mathf.Max(0.1f, config.BottleneckInfluenceDistance);
        Config.BottleneckClearanceHoldTime = Mathf.Max(0.05f, config.BottleneckClearanceHoldTime);
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
            state.RuntimeDirtyJob = null;
            state.BuildJob = null;
        }

        FlowTileCache.Clear();
        PendingTileBuildKeys.Clear();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        SectorPathCache.Clear();
        SectorPortalAccessCache.Clear();
        SharedGoalFields.Clear();
        MovingTargetAnchors.Clear();
        Bottlenecks.Clear();
        CorridorBottlenecks.Clear();
        AgentSpatialBuckets.Clear();
        NavigationGoalOccupancyBuckets.Clear();
        NearbyAgentScratch.Clear();
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentSpatialBucketWorldVersion = -1;
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            pair.Value.NavState.PathHandle = null;
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            Debug.Log($"[FlowWorld] MarkWorldDirty reason={_lastWorldDirtyReason} states={WorldStates.Count} agents={Agents.Count}");
    }

    public static void PrewarmNavigationWorlds()
    {
        List<int> agentTypeIds = CollectNavigationWorldAgentTypes();
        Stopwatch stopwatch = Stopwatch.StartNew();
        Debug.Log(
            $"[FlowWorld] Prewarm begin agentTypes=[{string.Join(",", agentTypeIds)}] agents={Agents.Count} " +
            $"config(cellSize={Config.NavigationCellSize:F3}, sector={Config.SectorSizeInCells}, tileCacheLimit={Config.FlowTileCacheLimit})");
        int builtCount = 0;
        int skippedCount = 0;
        foreach (int agentTypeId in agentTypeIds)
        {
            bool hadWorld = WorldStates.TryGetValue(agentTypeId, out WorldRuntimeState existingState)
                            && existingState.World != null
                            && !existingState.IsDirty;
            if (!TryEnsureWorldBuilt(agentTypeId))
            {
                skippedCount++;
                Debug.LogWarning($"[FlowWorld] Prewarm skipped agentType={agentTypeId}: terrain source unavailable.");
                continue;
            }

            if (!hadWorld)
                builtCount++;
        }

        stopwatch.Stop();
        Debug.Log(
            $"[FlowWorld] Prewarm end elapsed={stopwatch.Elapsed.TotalMilliseconds:F3}ms built={builtCount} skipped={skippedCount} " +
            $"worldStates={WorldStates.Count} activeWorld={(_world != null ? _world.Version.ToString() : "null")}");
    }

    private static List<int> CollectNavigationWorldAgentTypes()
    {
        List<int> agentTypeIds = new List<int>(8);
        HashSet<int> seen = new HashSet<int>();

        AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(0));
        foreach (AgentRuntimeData agent in Agents.Values)
            AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agent.AgentTypeId));

        int settingsCount = NavMesh.GetSettingsCount();
        for (int i = 0; i < settingsCount; i++)
            AddNavigationWorldAgentType(agentTypeIds, seen, NavMesh.GetSettingsByIndex(i).agentTypeID);

        return agentTypeIds;
    }

    private static void AddNavigationWorldAgentType(List<int> agentTypeIds, HashSet<int> seen, int agentTypeId)
    {
        if (!seen.Add(agentTypeId))
            return;

        agentTypeIds.Add(agentTypeId);
    }

    private static void MarkRuntimeObstacleDirty(Bounds bounds)
    {
        _lastRuntimeObstacleDirtyReason =
            $"boundsCenter={bounds.center} boundsSize={bounds.size} circleCount={CircleObstacles.Count} boxCount={BoxObstacles.Count} costStampCount={CostStamps.Count}";
        if (WorldStates.Count == 0)
        {
            MarkWorldDirty();
            return;
        }

        foreach (KeyValuePair<int, WorldRuntimeState> pair in WorldStates)
        {
            WorldRuntimeState state = pair.Value;
            if (state.IsDirty)
            {
                state.BuildJob = null;
                continue;
            }

            if (state.World == null)
                continue;

            CollectDirtySectors(state.World, bounds, state.DirtyRuntimeObstacleSectors, includeNeighbors: true);
            InvalidateRuntimeDirtyJob(state);
        }
    }

    public static void ProcessRuntimeRebuildQueue()
    {
        long budgetTicks = Math.Max(1L, (long)(Stopwatch.Frequency * Config.RuntimeRebuildBudgetMilliseconds / 1000.0));
        long deadlineTicks = Stopwatch.GetTimestamp() + budgetTicks;

        RuntimeRebuildQueueScratch.Clear();
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null || state.IsDirty || state.World == null)
                continue;
            if (state.DirtyRuntimeObstacleSectors.Count == 0 && state.RuntimeDirtyJob == null)
                continue;

            RuntimeRebuildQueueScratch.Add(state);
        }

        RuntimeRebuildQueueScratch.Sort((left, right) => ResolveRuntimeDirtyPriority(right).CompareTo(ResolveRuntimeDirtyPriority(left)));
        for (int i = 0; i < RuntimeRebuildQueueScratch.Count; i++)
        {
            WorldRuntimeState state = RuntimeRebuildQueueScratch[i];
            if (!EnsureRuntimeDirtyJob(state))
                continue;
            ProcessRuntimeDirtyJob(state, deadlineTicks, forceComplete: false);
            if (Stopwatch.GetTimestamp() >= deadlineTicks)
                break;
        }
    }

    public static void ProcessWorldBuildQueue()
    {
        long budgetTicks = Math.Max(1L, (long)(Stopwatch.Frequency * Config.RuntimeRebuildBudgetMilliseconds / 1000.0));
        long deadlineTicks = Stopwatch.GetTimestamp() + budgetTicks;

        EnsureWorldBuildStatesForKnownAgentTypes();
        RuntimeRebuildQueueScratch.Clear();
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null || !state.IsDirty)
                continue;

            RuntimeRebuildQueueScratch.Add(state);
        }

        RuntimeRebuildQueueScratch.Sort((left, right) => ResolveWorldBuildPriority(right).CompareTo(ResolveWorldBuildPriority(left)));
        for (int i = 0; i < RuntimeRebuildQueueScratch.Count; i++)
        {
            WorldRuntimeState state = RuntimeRebuildQueueScratch[i];
            if (!EnsureWorldBuildJob(state))
                continue;

            ProcessWorldBuildJob(state, deadlineTicks, forceComplete: false);
            if (Stopwatch.GetTimestamp() >= deadlineTicks)
                break;
        }
    }

    public static void ProcessFlowTileBuildQueue()
    {
        if (!CanProcessFlowTileBuildQueue())
            return;

        EnqueueFlowTileBuildsForActiveAgents();
        long budgetTicks = Math.Max(1L, (long)(Stopwatch.Frequency * Config.RuntimeRebuildBudgetMilliseconds / 1000.0));
        long deadlineTicks = Stopwatch.GetTimestamp() + budgetTicks;
        ProcessFlowTileBuildQueue(deadlineTicks, forceComplete: false, requiredKey: null);
    }

    private static bool CanProcessFlowTileBuildQueue()
    {
        if (_world == null)
            return false;

        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                continue;
            if (state.IsDirty || state.BuildJob != null || state.RuntimeDirtyJob != null || state.DirtyRuntimeObstacleSectors.Count > 0)
                return false;
        }

        return true;
    }

    private static void EnqueueFlowTileBuildsForActiveAgents()
    {
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null || handle.WorldVersion != _world.Version || handle.SectorIds == null || handle.SectorIds.Length == 0)
                continue;
            if (PathReferencesMissingPortal(handle))
                continue;

            int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
            EnqueueFlowTileBuildChain(handle, sectorPathIndex, handle.GoalX, handle.GoalY, agent.AgentTypeId);
        }
    }

    private static void ProcessFlowTileBuildQueue(long deadlineTicks, bool forceComplete, FlowTileCacheKey? requiredKey)
    {
        int guard = 0;
        while (FlowTileBuildQueue.Count > 0)
        {
            FlowTileBuildJob job = FlowTileBuildQueue.First.Value;
            FlowTileBuildQueue.RemoveFirst();
            PendingFlowTileBuildJobs.Remove(job.BuildKey);
            PendingTileBuildKeys.Remove(job.BuildKey.CacheKey);

            if (FlowTileCache.ContainsKey(job.BuildKey.CacheKey))
            {
                if (requiredKey.HasValue && FlowTileCache.ContainsKey(requiredKey.Value))
                    return;
                continue;
            }

            if (IsFlowTileBuildJobStale(job))
            {
                if (requiredKey.HasValue && FlowTileCache.ContainsKey(requiredKey.Value))
                    return;
                continue;
            }

            if (!AdvanceFlowTileBuildJob(job, deadlineTicks, forceComplete))
            {
                if (job.WaitingForDependency)
                    FlowTileBuildQueue.AddLast(job);
                else
                    FlowTileBuildQueue.AddFirst(job);
                PendingFlowTileBuildJobs.Add(job.BuildKey);
                PendingTileBuildKeys.Add(job.BuildKey.CacheKey);
                return;
            }

            if (requiredKey.HasValue && FlowTileCache.ContainsKey(requiredKey.Value))
                return;
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;

            guard++;
            if (guard > Config.FlowTileCacheLimit * 8 + 1024)
                throw new InvalidOperationException("ProcessFlowTileBuildQueue failed: queue processing exceeded guard.");
        }
    }

    private static bool IsFlowTileBuildJobStale(FlowTileBuildJob job)
    {
        if (job == null || job.HandleSnapshot == null || _world == null)
            return true;
        if (job.HandleSnapshot.WorldVersion != _world.Version)
            return true;
        if (job.HandleSnapshot.SectorIds == null
            || job.BuildKey.SectorPathIndex < 0
            || job.BuildKey.SectorPathIndex >= job.HandleSnapshot.SectorIds.Length)
        {
            return true;
        }
        if (PathReferencesMissingPortal(job.HandleSnapshot))
            return true;
        if (!IsPathHandleStillLive(job.HandleSnapshot.HandleId))
            return true;

        FlowTileCacheKey expectedKey = CreateTileCacheKeyForPathSegment(
            job.HandleSnapshot,
            job.BuildKey.SectorPathIndex,
            job.BuildKey.GoalX,
            job.BuildKey.GoalY,
            job.BuildKey.AgentTypeId,
            out _,
            out _);
        return !expectedKey.Equals(job.BuildKey.CacheKey);
    }

    private static bool AdvanceFlowTileBuildJob(FlowTileBuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.Stage != FlowTileBuildStage.Complete)
        {
            switch (job.Stage)
            {
                case FlowTileBuildStage.Prepare:
                    if (!PrepareFlowTileBuildJob(job))
                        return false;
                    break;
                case FlowTileBuildStage.LineOfSight:
                    if (!AdvanceFlowTileLineOfSight(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                case FlowTileBuildStage.Integration:
                    if (!AdvanceFlowTileIntegration(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                case FlowTileBuildStage.FlowDirections:
                    if (!AdvanceFlowTileFlowDirections(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                case FlowTileBuildStage.PortalHandoff:
                    if (!AdvanceFlowTilePortalHandoff(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                case FlowTileBuildStage.Commit:
                    CommitFlowTileBuildJob(job);
                    break;
                default:
                    throw new InvalidOperationException($"AdvanceFlowTileBuildJob failed: unknown stage {job.Stage}.");
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return false;
        }

        return true;
    }

    private static bool PrepareFlowTileBuildJob(FlowTileBuildJob job)
    {
        FlowTileCacheKey key = job.BuildKey.CacheKey;
        job.WaitingForDependency = false;
        job.Sector = _world.Sectors[key.SectorId];
        Vector2Int[] goalCells = ResolveGoalCells(job.Sector, key, job.BuildKey.GoalX, job.BuildKey.GoalY);
        if (goalCells == null || goalCells.Length == 0)
            throw new InvalidOperationException($"PrepareFlowTileBuildJob failed: tile has no goal cells key={FormatTileKey(key)}.");

        if (!TryResolveDownstreamTileForTileBuild(
                key,
                job.HandleSnapshot,
                job.BuildKey.SectorPathIndex,
                job.BuildKey.GoalX,
                job.BuildKey.GoalY,
                job.BuildKey.AgentTypeId,
                out job.DownstreamTile))
        {
            job.WaitingForDependency = true;
            return false;
        }

        _perf.TileBuilds++;
        job.Seeds = BuildIntegrationSeedsForTile(
            key,
            job.HandleSnapshot,
            job.BuildKey.SectorPathIndex,
            job.BuildKey.GoalX,
            job.BuildKey.GoalY,
            goalCells,
            job.BuildKey.AgentTypeId,
            job.DownstreamTile);
        if (job.Seeds == null || job.Seeds.Length == 0)
            throw new InvalidOperationException($"PrepareFlowTileBuildJob failed: sector {job.Sector.SectorId} has no integration seeds goalKind={key.GoalKind} goalId={key.GoalId}.");

        job.Tile = new FlowTileCacheEntry
        {
            Key = key,
            StartX = job.Sector.StartX,
            StartY = job.Sector.StartY,
            Width = job.Sector.Width,
            Height = job.Sector.Height,
            Integration = new float[job.Sector.Width * job.Sector.Height],
            FlowDirections = new Vector2[job.Sector.Width * job.Sector.Height],
            HasLineOfSight = new bool[job.Sector.Width * job.Sector.Height],
            WaveFrontBlocked = new bool[job.Sector.Width * job.Sector.Height],
            PortalTargets = key.GoalKind == TileGoalKind.Portal ? new CachedPortalTarget[job.Sector.Width * job.Sector.Height] : null,
            GoalCells = goalCells
        };
        InitializeIntegrationField(job.Tile.Integration);
        job.LineOfSightOpenQueue = new Queue<int>(job.Tile.Width * job.Tile.Height);
        SeedFinalGoalLineOfSightPass(
            job.Tile,
            job.HandleSnapshot,
            job.BuildKey.SectorPathIndex,
            job.BuildKey.GoalX,
            job.BuildKey.GoalY,
            job.BuildKey.AgentTypeId,
            job.Seeds,
            job.LineOfSightOpenQueue,
            job.DownstreamTile);
        job.Stage = FlowTileBuildStage.LineOfSight;
        return true;
    }

    private static bool AdvanceFlowTileLineOfSight(FlowTileBuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.LineOfSightOpenQueue.Count > 0)
        {
            int localIndex = job.LineOfSightOpenQueue.Dequeue();
            int worldX = job.Tile.StartX + localIndex % job.Tile.Width;
            int worldY = job.Tile.StartY + localIndex / job.Tile.Width;
            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextX = worldX + CardinalOffsetX[i];
                int nextY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(job.Tile, nextX, nextY))
                    continue;

                int nextIndex = job.Tile.GetLocalIndex(nextX, nextY);
                if (job.Tile.WaveFrontBlocked[nextIndex])
                    continue;
                if (!_world.IsWalkable(nextX, nextY) || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
                {
                    MarkWaveFrontBlockedFromLosCorner(job.Tile, job.BuildKey.GoalX, job.BuildKey.GoalY, worldX, worldY, nextX, nextY);
                    continue;
                }
                if (!IsClearLosCost(_world, nextX, nextY))
                {
                    MarkWaveFrontBlockedFromLosCorner(job.Tile, job.BuildKey.GoalX, job.BuildKey.GoalY, worldX, worldY, nextX, nextY);
                    continue;
                }
                if (job.Tile.HasLineOfSight[nextIndex])
                    continue;

                job.Tile.HasLineOfSight[nextIndex] = true;
                job.Tile.Integration[nextIndex] = job.Tile.Integration[localIndex] + 1f;
                job.LineOfSightOpenQueue.Enqueue(nextIndex);
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return false;
        }

        BeginFlowTileIntegration(job);
        job.Stage = FlowTileBuildStage.Integration;
        return true;
    }

    private static void BeginFlowTileIntegration(FlowTileBuildJob job)
    {
        job.IntegrationOpenSet = new MinHeap();
        for (int i = 0; i < job.Tile.Integration.Length; i++)
        {
            if (!float.IsPositiveInfinity(job.Tile.Integration[i])
                && job.Tile.WaveFrontBlocked != null
                && job.Tile.WaveFrontBlocked[i])
            {
                job.IntegrationOpenSet.Push(i, job.Tile.Integration[i]);
            }
        }

        for (int i = 0; i < job.Seeds.Length; i++)
        {
            Vector2Int goalCell = job.Seeds[i].Cell;
            if (!IsInsideSector(job.Sector, goalCell.x, goalCell.y))
                continue;

            int localIndex = GetSectorLocalIndex(job.Sector, goalCell.x, goalCell.y);
            float seedCost = job.Seeds[i].Cost;
            if (seedCost >= job.Tile.Integration[localIndex])
                continue;

            job.Tile.Integration[localIndex] = seedCost;
            job.IntegrationOpenSet.Push(localIndex, seedCost);
        }
    }

    private static bool AdvanceFlowTileIntegration(FlowTileBuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.IntegrationOpenSet.Count > 0)
        {
            QueueNode node = job.IntegrationOpenSet.Pop();
            if (node.Cost > job.Tile.Integration[node.Index] + 0.0001f)
                continue;

            int localX = node.Index % job.Sector.Width;
            int localY = node.Index / job.Sector.Width;
            int worldX = job.Sector.StartX + localX;
            int worldY = job.Sector.StartY + localY;

            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextWorldX = worldX + CardinalOffsetX[i];
                int nextWorldY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(job.Sector, nextWorldX, nextWorldY) || !_world.IsWalkable(nextWorldX, nextWorldY))
                    continue;

                if (!CanTraverseNeighborCells(_world, nextWorldX, nextWorldY, worldX, worldY))
                    continue;

                int nextLocalIndex = GetSectorLocalIndex(job.Sector, nextWorldX, nextWorldY);
                if (job.Tile.HasLineOfSight != null && job.Tile.HasLineOfSight[nextLocalIndex])
                    continue;

                float newCost = ResolveEikonalIntegrationCost(_world, job.Sector, job.Tile.Integration, nextWorldX, nextWorldY, reverseTraversal: true);
                if (newCost >= job.Tile.Integration[nextLocalIndex])
                    continue;

                job.Tile.Integration[nextLocalIndex] = newCost;
                job.IntegrationOpenSet.Push(nextLocalIndex, newCost);
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return false;
        }

        job.CellCursor = 0;
        job.Stage = FlowTileBuildStage.FlowDirections;
        return true;
    }

    private static bool AdvanceFlowTileFlowDirections(FlowTileBuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.CellCursor < job.Tile.Width * job.Tile.Height)
        {
            int localIndex = job.CellCursor++;
            int worldX = job.Tile.StartX + localIndex % job.Tile.Width;
            int worldY = job.Tile.StartY + localIndex / job.Tile.Width;
            job.Tile.FlowDirections[localIndex] = _world.IsWalkable(worldX, worldY)
                ? ResolveFlowDirectionFromIntegration(job.Tile, worldX, worldY)
                : Vector2.zero;
            if (job.Tile.PortalTargets != null)
                job.Tile.PortalTargets[localIndex] = ResolveCachedPortalTarget(job.Tile, worldX, worldY);

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return false;
        }

        job.PortalHandoffCursor = 0;
        job.Stage = FlowTileBuildStage.PortalHandoff;
        return true;
    }

    private static bool AdvanceFlowTilePortalHandoff(FlowTileBuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job.Tile.Key.GoalKind != TileGoalKind.Portal)
        {
            job.Stage = FlowTileBuildStage.Commit;
            return true;
        }

        PortalData portal = GetPortalById(_world, job.Tile.Key.GoalId);
        int nextSectorIndex = job.BuildKey.SectorPathIndex + 1;
        if (nextSectorIndex >= job.HandleSnapshot.SectorIds.Length)
            throw new InvalidOperationException($"AdvanceFlowTilePortalHandoff failed: portal tile has no downstream sector sector={job.Tile.Key.SectorId}.");

        Vector2Int[] currentCells = GetPortalCellsForSector(portal, job.Tile.Key.SectorId);
        Vector2Int[] downstreamCells = GetPortalCellsForSector(portal, job.HandleSnapshot.SectorIds[nextSectorIndex]);
        if (currentCells.Length != downstreamCells.Length)
            throw new InvalidOperationException($"AdvanceFlowTilePortalHandoff failed: portal {portal.PortalId} side cell count mismatch current={currentCells.Length} downstream={downstreamCells.Length}.");

        while (job.PortalHandoffCursor < currentCells.Length)
        {
            int i = job.PortalHandoffCursor++;
            Vector2Int currentCell = currentCells[i];
            Vector2Int downstreamCell = downstreamCells[i];
            if (IsInsideSector(job.Tile, currentCell.x, currentCell.y)
                && _world.IsWalkable(currentCell.x, currentCell.y)
                && _world.IsWalkable(downstreamCell.x, downstreamCell.y)
                && CanTraverseNeighborCells(_world, currentCell.x, currentCell.y, downstreamCell.x, downstreamCell.y))
            {
                Vector3 from = _world.GridToWorldCenter(currentCell.x, currentCell.y);
                Vector3 to = _world.GridToWorldCenter(downstreamCell.x, downstreamCell.y);
                Vector3 direction = to - from;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    direction.Normalize();
                    job.Tile.FlowDirections[job.Tile.GetLocalIndex(currentCell.x, currentCell.y)] = new Vector2(direction.x, direction.z);
                }
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return false;
        }

        job.Stage = FlowTileBuildStage.Commit;
        return true;
    }

    private static void CommitFlowTileBuildJob(FlowTileBuildJob job)
    {
        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowTileBuild] key={FormatTileKey(job.Tile.Key)} sectorPathIndex={job.BuildKey.SectorPathIndex} sectorRect=({job.Tile.StartX},{job.Tile.StartY},{job.Tile.Width},{job.Tile.Height}) " +
                $"goalCells={FormatGoalCells(job.Tile.GoalCells)} seeds={FormatSeeds(job.Seeds)} tileSummary={DescribeTileIntegrationSummary(job.Tile)}");
        }

        FlowTileCache[job.BuildKey.CacheKey] = job.Tile;
        job.Tile.LastUsedFrame = GetFrameCount();
        TrimTileCache();
        job.Stage = FlowTileBuildStage.Complete;
    }

    private static bool IsPathHandleStillLive(int handleId)
    {
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (agent.NavState.PathHandle != null && agent.NavState.PathHandle.HandleId == handleId)
                return true;
        }

        return false;
    }

    private static int ResolveWorldBuildPriority(WorldRuntimeState state)
    {
        if (state == null)
            return 0;

        int agentTypeId = state.BuildJob != null ? state.BuildJob.AgentTypeId : state.AgentTypeId;
        int priority = 1;
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (ResolvePreferredAgentTypeId(agent.AgentTypeId) == agentTypeId)
                priority += 100;
        }

        return priority;
    }

    private static void EnsureWorldBuildStatesForKnownAgentTypes()
    {
        List<int> agentTypeIds = CollectNavigationWorldAgentTypes();
        for (int i = 0; i < agentTypeIds.Count; i++)
        {
            int agentTypeId = ResolvePreferredAgentTypeId(agentTypeIds[i]);
            GetOrCreateWorldState(agentTypeId);
        }
    }

    private static int ResolveRuntimeDirtyPriority(WorldRuntimeState state)
    {
        if (state == null || state.World == null)
            return int.MinValue;

        HashSet<int> dirtySectors = state.RuntimeDirtyJob != null
            ? state.RuntimeDirtyJob.DirtySectors
            : state.DirtyRuntimeObstacleSectors;
        if (dirtySectors == null || dirtySectors.Count == 0)
            return 0;

        NavigationWorld world = state.World;
        int priority = dirtySectors.Count;
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (ResolvePreferredAgentTypeId(agent.AgentTypeId) != world.AgentTypeId)
                continue;

            if (world.WorldToGrid(agent.Position, out int agentX, out int agentY)
                && world.TryGetSectorId(agentX, agentY, out int agentSectorId)
                && dirtySectors.Contains(agentSectorId))
            {
                priority += 1000;
                continue;
            }

            if (PathTouchesAnySector(agent.NavState.PathHandle, dirtySectors))
                priority += 100;
        }

        return priority;
    }

    public static void RegisterAgent(MAEntity entity, bool isLeader, float radius)
    {
        RegisterAgent((IEntityContext)entity, isLeader, radius);
    }

    public static void RegisterAgent(IEntityContext entity, bool isLeader, float radius)
    {
        if (entity == null)
            return;

        int id = ResolveAgentId(entity);
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
        agent.AgentTypeId = entity is MAEntity maEntity ? maEntity.navAgentTypeID : 0;
        agent.EntityTypeName = entity.GetType().Name;
        agent.MoveCompTypeName = entity.MoveComp?.GetType().Name ?? "null";
        agent.RegistrationSource = "RegisterAgent";
        agent.IsSyntheticRegistration = false;
        UpdateAgentNavigationIntent(agent, entity.MoveComp);
    }

    public static void UnregisterAgent(int agentId)
    {
        Agents.Remove(agentId);
        ClearAgentFromBottlenecks(agentId);
        _lastAgentSpatialBucketFrame = -1;
        _lastAgentSpatialBucketWorldVersion = -1;
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
    }

    public static void UpdateAgent(MAEntity entity, float radius)
    {
        BeginPerfCall();
        long startTicks = Stopwatch.GetTimestamp();
        if (entity == null)
            return;

        int id = entity.GetInstanceID();
        if (!Agents.TryGetValue(id, out AgentRuntimeData agent))
        {
            RegisterAgent((IEntityContext)entity, false, radius);
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
        UpdateAgentNavigationIntent(agent, entity.moveComp);
        _perf.AgentUpdateTicks += Stopwatch.GetTimestamp() - startTicks;
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

#if UNITY_EDITOR
    public static bool TryGetEditorTestDesiredDirection(int agentId, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        Vector3 desiredVelocity = agent.NavState.DesiredVelocity;
        if (desiredVelocity.sqrMagnitude <= 0.0001f)
            return false;

        direction = desiredVelocity.normalized;
        return true;
    }

    public static int GetEditorTestFlowTileCacheCount()
    {
        return FlowTileCache.Count;
    }

    public static void ClearEditorTestFlowTileCache()
    {
        FlowTileCache.Clear();
        PendingTileBuildKeys.Clear();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
    }

    public static bool HasEditorTestWorld()
    {
        return _world != null;
    }

    public static bool HasEditorTestPendingWorldBuild()
    {
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state != null && state.BuildJob != null)
                return true;
        }

        return false;
    }

    public static int GetEditorTestPortalCount()
    {
        return _world != null && _world.Portals != null ? _world.Portals.Length : 0;
    }

    public static int GetEditorTestFrameTileBuildCount()
    {
        return _perf.TileBuilds;
    }

    public static int GetEditorTestPendingFlowTileBuildCount()
    {
        return PendingFlowTileBuildJobs.Count;
    }

    public static bool TryGetEditorTestCachedTileCellLineOfSightState(int worldX, int worldY, out bool hasLineOfSight, out bool waveFrontBlocked)
    {
        hasLineOfSight = false;
        waveFrontBlocked = false;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY))
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            hasLineOfSight = tile.HasLineOfSight != null && tile.HasLineOfSight[localIndex];
            waveFrontBlocked = tile.WaveFrontBlocked != null && tile.WaveFrontBlocked[localIndex];
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestCachedTileCellFlowDirection(int worldX, int worldY, out Vector2 direction)
    {
        direction = Vector2.zero;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowDirections == null)
                continue;

            direction = tile.FlowDirections[tile.GetLocalIndex(worldX, worldY)];
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestCachedPortalTarget(int worldX, int worldY, out Vector3 targetPosition, out int visibleCandidateCount, out int selectedPairIndex, out bool usedOppositeCenter)
    {
        targetPosition = Vector3.zero;
        visibleCandidateCount = 0;
        selectedPairIndex = -1;
        usedOppositeCenter = false;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.PortalTargets == null)
                continue;

            CachedPortalTarget target = tile.PortalTargets[tile.GetLocalIndex(worldX, worldY)];
            targetPosition = target.TargetPosition;
            visibleCandidateCount = target.VisibleCandidateCount;
            selectedPairIndex = target.SelectedPairIndex;
            usedOppositeCenter = target.UsedOppositeCenter;
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestCostFieldValue(int worldX, int worldY, out byte cost)
    {
        cost = 0;
        if (_world == null || !_world.WorldToGrid(_world.GridToWorldCenter(worldX, worldY), out _, out _))
            return false;
        if (worldX < 0 || worldX >= _world.Width || worldY < 0 || worldY >= _world.Height)
            return false;
        if (_world.CostField == null || _world.CostField.Length != _world.Width * _world.Height)
            return false;

        cost = _world.CostField[_world.GetIndex(worldX, worldY)];
        return true;
    }

    public static bool TryGetEditorTestIslandFieldValue(int worldX, int worldY, out int islandId, out int islandCount)
    {
        islandId = 0;
        islandCount = 0;
        if (_world == null || worldX < 0 || worldX >= _world.Width || worldY < 0 || worldY >= _world.Height)
            return false;
        if (_world.IslandIds == null || _world.IslandIds.Length != _world.Width * _world.Height)
            return false;

        islandId = _world.IslandIds[_world.GetIndex(worldX, worldY)];
        islandCount = _world.IslandCount;
        return true;
    }
#endif

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

    public static void RegisterBoxCostStamp(int stampId, Vector3 center, Vector3 halfExtents, byte cost)
    {
        RegisterBoxCostStamp(stampId, AnyAgentTypeId, center, halfExtents, cost);
    }

    public static void RegisterBoxCostStamp(int stampId, int agentTypeId, Vector3 center, Vector3 halfExtents, byte cost)
    {
        if (cost == 0 || cost >= 255)
            throw new InvalidOperationException($"RegisterBoxCostStamp failed: cost must be in 1..254, cost={cost}.");

        Bounds bounds = new Bounds(center, halfExtents * 2f);
        CostStamps[stampId] = new CostStamp
        {
            Id = stampId,
            AgentTypeId = agentTypeId,
            Bounds = bounds,
            Cost = cost
        };
        MarkRuntimeObstacleDirty(bounds);
    }

    public static void UnregisterCostStamp(int stampId)
    {
        if (!CostStamps.TryGetValue(stampId, out CostStamp stamp))
            return;

        CostStamps.Remove(stampId);
        MarkRuntimeObstacleDirty(stamp.Bounds);
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
        if (_world != null)
            return IsPositionOccupiedByAgentSpatial(position, requiredDistance);

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

    public static bool IsPositionOccupiedByOtherAgent(int selfId, Vector3 position, float requiredDistance, out int blockingAgentId, out float blockingDistance)
    {
        return IsPositionOccupiedByOtherAgent(selfId, 0, position, requiredDistance, out blockingAgentId, out blockingDistance);
    }

    public static bool IsPositionOccupiedByOtherAgent(int selfId, int ignoredAgentId, Vector3 position, float requiredDistance, out int blockingAgentId, out float blockingDistance)
    {
        blockingAgentId = 0;
        blockingDistance = float.PositiveInfinity;
        if (_world != null)
            return TryFindBlockingAgentSpatial(selfId, ignoredAgentId, position, requiredDistance, out blockingAgentId, out blockingDistance);

        float requiredDistanceSq = requiredDistance * requiredDistance;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData agent = pair.Value;
            if (agent.Id == selfId || agent.Id == ignoredAgentId || agent.IgnoreAgentCollision)
                continue;

            Vector3 offset = agent.Position - position;
            offset.y = 0f;
            float distanceSq = offset.sqrMagnitude;
            if (distanceSq >= requiredDistanceSq)
                continue;

            float distance = Mathf.Sqrt(distanceSq);
            if (distance >= blockingDistance)
                continue;

            blockingDistance = distance;
            blockingAgentId = agent.Id;
        }

        return blockingAgentId != 0;
    }

    public static bool TryResolveNearestReachableGoal(
        IEntityContext self,
        Vector3 desiredGoal,
        float searchRadius,
        out Vector3 reachableGoal,
        out string failureReason)
    {
        reachableGoal = desiredGoal;
        failureReason = string.Empty;
        if (self == null)
            throw new InvalidOperationException("TryResolveNearestReachableGoal failed: self is null.");

        int selfId = ResolveAgentId(self);
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

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureReason = $"world unavailable agentType={agent.AgentTypeId}";
            return false;
        }

        if (!_world.WorldToGrid(self.Position, out int startX, out int startY))
        {
            failureReason = $"start not on grid pos={self.Position}";
            return false;
        }

        if (!_world.IsWalkable(startX, startY)
            && !TryFindNearestWalkable(_world, startX, startY, 3, out startX, out startY))
        {
            failureReason = $"start blocked and no nearby walkable pos={self.Position}";
            return false;
        }

        int startIsland = ResolveIslandIdForDiagnostics(_world, startX, startY);
        if (startIsland <= 0)
        {
            failureReason = $"start island invalid start=({startX},{startY}) island={startIsland}";
            return false;
        }

        if (!_world.WorldToGrid(desiredGoal, out int goalX, out int goalY))
        {
            failureReason = $"goal not on grid goal={desiredGoal}";
            return false;
        }

        int maxRadiusCells = Mathf.Max(1, Mathf.CeilToInt(searchRadius / Mathf.Max(_world.CellSize, 0.001f)));
        if (TryFindNearestWalkableInIslandByWorldDistance(
                _world,
                goalX,
                goalY,
                desiredGoal,
                startIsland,
                maxRadiusCells,
                allowFullIslandSearch: false,
                out int resultX,
                out int resultY,
                out float distance))
        {
            reachableGoal = _world.GridToWorldCenter(resultX, resultY);
            LogReachableGoalResolution(self, desiredGoal, startX, startY, goalX, goalY, resultX, resultY, distance, maxRadiusCells, fullIsland: false);
            LogReachableGoalTargetSnapDiagnostics(self, desiredGoal, startX, startY, goalX, goalY, resultX, resultY, distance, fullIsland: false);
            return true;
        }

        bool fullIslandFound = TryFindNearestWalkableInIslandByWorldDistance(
            _world,
            goalX,
            goalY,
            desiredGoal,
            startIsland,
            maxRadiusCells,
            allowFullIslandSearch: true,
            out int fullIslandX,
            out int fullIslandY,
            out float fullIslandDistance);
        Debug.LogWarning(BuildGoalResolutionIslandDiagnostics(
            self,
            desiredGoal,
            startX,
            startY,
            goalX,
            goalY,
            startIsland,
            maxRadiusCells,
            fullIslandFound,
            fullIslandX,
            fullIslandY,
            fullIslandDistance));
        if (fullIslandFound)
        {
            reachableGoal = _world.GridToWorldCenter(fullIslandX, fullIslandY);
            LogReachableGoalResolution(self, desiredGoal, startX, startY, goalX, goalY, fullIslandX, fullIslandY, fullIslandDistance, maxRadiusCells, fullIsland: true);
            LogReachableGoalTargetSnapDiagnostics(self, desiredGoal, startX, startY, goalX, goalY, fullIslandX, fullIslandY, fullIslandDistance, fullIsland: true);
            return true;
        }

        failureReason =
            $"no reachable goal in start island desired={desiredGoal} desiredCell=({goalX},{goalY}) start=({startX},{startY}) startIsland={startIsland} radiusCells={maxRadiusCells}";
        return false;
    }

    private static void LogReachableGoalTargetSnapDiagnostics(
        IEntityContext self,
        Vector3 desiredGoal,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int resultX,
        int resultY,
        float distance,
        bool fullIsland)
    {
        if (_world == null || self == null)
            return;

        IEntityContext target = self.TargetComp?.CurrentTarget;
        if (target == null)
            return;

        int desiredIsland = ResolveIslandIdForDiagnostics(_world, goalX, goalY);
        bool targetInGrid = _world.WorldToGrid(target.Position, out int targetX, out int targetY);
        int targetIsland = targetInGrid ? ResolveIslandIdForDiagnostics(_world, targetX, targetY) : -1;
        if (desiredIsland == targetIsland && distance <= Mathf.Max(0.35f, _world.CellSize * 1.5f))
            return;

        Vector3 resultWorld = _world.GridToWorldCenter(resultX, resultY);
        Debug.LogWarning(
            $"[FlowReachableTargetSnapDiag] self={self.CharacterKey} target={target.CharacterKey} fullIsland={fullIsland} " +
            $"selfPos={self.Position} targetPos={target.Position} desired={desiredGoal} desiredCell=({goalX},{goalY}) desiredIsland={desiredIsland} " +
            $"targetCell={(targetInGrid ? $"({targetX},{targetY})" : "out")} targetIsland={targetIsland} result=({resultX},{resultY}) resultWorld={resultWorld} " +
            $"resultIsland={ResolveIslandIdForDiagnostics(_world, resultX, resultY)} snapDistance={distance:F3} start=({startX},{startY}) " +
            $"startIsland={ResolveIslandIdForDiagnostics(_world, startX, startY)} worldVersion={_world.Version}");
    }

    private static void LogReachableGoalResolution(
        IEntityContext self,
        Vector3 desiredGoal,
        int startX,
        int startY,
        int rawGoalX,
        int rawGoalY,
        int resolvedX,
        int resolvedY,
        float distance,
        int radiusCells,
        bool fullIsland)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move)
            || self == null
            || !GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey)
            || _world == null)
        {
            return;
        }

        int startIsland = ResolveIslandIdForDiagnostics(_world, startX, startY);
        int rawGoalIsland = ResolveIslandIdForDiagnostics(_world, rawGoalX, rawGoalY);
        int resolvedIsland = ResolveIslandIdForDiagnostics(_world, resolvedX, resolvedY);
        bool changed = rawGoalX != resolvedX || rawGoalY != resolvedY;
        if (!changed && !fullIsland)
            return;

        Vector3 resolvedWorld = _world.GridToWorldCenter(resolvedX, resolvedY);
        GameDebugSettings.Log(DebugCategory.Move,
            $"[FlowReachableGoalResolve] key={self.CharacterKey} desired={desiredGoal} start=({startX},{startY}) startIsland={startIsland} " +
            $"raw=({rawGoalX},{rawGoalY}) rawIsland={rawGoalIsland} resolved=({resolvedX},{resolvedY}) resolvedIsland={resolvedIsland} " +
            $"resolvedWorld={resolvedWorld} distance={distance:F3} radiusCells={radiusCells} fullIsland={fullIsland} " +
            $"rawNav={FormatNavSampleDiagnostics(desiredGoal)} resolvedNav={FormatNavSampleDiagnostics(resolvedWorld)}");
    }

    public static bool TryGetSteeringVelocity(IEntityContext self, Vector3 goalPosition, float maxSpeed, out Vector3 velocity)
    {
        BeginPerfCall();
        long totalStartTicks = Stopwatch.GetTimestamp();
        if (self == null)
            throw new InvalidOperationException("FlowFieldCrowdMovementSystem.TryGetSteeringVelocity failed: self is null.");

        if (maxSpeed <= 0.0001f)
        {
            velocity = Vector3.zero;
            _perf.Calls++;
            _perf.TotalTicks += Stopwatch.GetTimestamp() - totalStartTicks;
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
        long sectionStartTicks = Stopwatch.GetTimestamp();
        if (!TryEnsureWorldBuilt(preferredAgentTypeId))
        {
            return FailNoFallback(self, "world unavailable", goalPosition, out velocity);
        }
        _perf.WorldTicks += Stopwatch.GetTimestamp() - sectionStartTicks;

        Vector3 occupiedGoalPosition = ResolveNavigationGoalOccupancy(self, agent, goalPosition);

        sectionStartTicks = Stopwatch.GetTimestamp();
        if (!_world.WorldToGrid(self.Position, out int startX, out int startY))
        {
            return FailNoFallback(self, $"start not on grid pos={self.Position}", occupiedGoalPosition, out velocity);
        }

        if (!_world.IsWalkable(startX, startY)
            && !TryFindNearestWalkable(_world, startX, startY, 3, out startX, out startY))
        {
            return FailNoFallback(self, $"start blocked and no nearby walkable originalPos={self.Position} | {BuildStartCellDiagnostics(self, self.Position)}", occupiedGoalPosition, out velocity);
        }

        if (!TryResolveStableGoalCell(agent, self, occupiedGoalPosition, out int goalX, out int goalY, out Vector3 stableGoalPosition))
        {
            return FailNoFallback(self, BuildGoalResolutionFailure(self, occupiedGoalPosition), occupiedGoalPosition, out velocity);
        }

        if (!_world.TryGetSectorId(startX, startY, out int startSectorId)
            || !_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
        {
            return FailNoFallback(self, $"sector resolve failed start=({startX},{startY}) goal=({goalX},{goalY})", occupiedGoalPosition, out velocity);
        }
        _perf.ResolveCellsTicks += Stopwatch.GetTimestamp() - sectionStartTicks;

        agent.NavState.CurrentCell = new Vector2Int(startX, startY);
        agent.NavState.CurrentSectorId = startSectorId;

        sectionStartTicks = Stopwatch.GetTimestamp();
        if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY))
        {
            return FailNoFallback(self, BuildPathHandleFailure(self, stableGoalPosition, startSectorId, goalSectorId, startX, startY, goalX, goalY), occupiedGoalPosition, out velocity);
        }
        _perf.PathTicks += Stopwatch.GetTimestamp() - sectionStartTicks;

        sectionStartTicks = Stopwatch.GetTimestamp();
        if (!TryAdvancePathToCurrentSector(agent, startSectorId))
        {
            _perf.PathAdvanceFailures++;
            LogPathAdvanceFailure(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY);
            agent.NavState.PathHandle = null;
            _perf.AdvanceTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
            sectionStartTicks = Stopwatch.GetTimestamp();
            if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY))
            {
                return FailNoFallback(self, $"path handle rebuild failed startSector={startSectorId} goalSector={goalSectorId}", occupiedGoalPosition, out velocity);
            }
            _perf.PathTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
        }
        else
        {
            _perf.AdvanceTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
        }

        sectionStartTicks = Stopwatch.GetTimestamp();
        if (!TryBuildOrGetTileWithStrictRepath(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY, out FlowTileCacheEntry tile, out TileGoalKind goalKind, out int downstreamPortalId, out string tileFailureReason))
        {
            return FailNoFallback(self, tileFailureReason, occupiedGoalPosition, out velocity);
        }
        _perf.TileTicks += Stopwatch.GetTimestamp() - sectionStartTicks;

        if (!IsCellReachableInTile(tile, startX, startY))
        {
            _perf.TileReachabilityFailures++;
            LogTileReachabilityFailure(agent, tile, startSectorId, goalSectorId, startX, startY, goalX, goalY, goalKind, downstreamPortalId);
            agent.NavState.PathHandle = null;
            sectionStartTicks = Stopwatch.GetTimestamp();
            if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY)
                || !TryBuildOrGetTile(agent, goalX, goalY, out tile, out goalKind, out downstreamPortalId)
                || !IsCellReachableInTile(tile, startX, startY))
            {
                return FailNoFallback(self, $"tile reachability failed start=({startX},{startY}) sector={startSectorId}", occupiedGoalPosition, out velocity);
            }
            _perf.TileTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
        }

        sectionStartTicks = Stopwatch.GetTimestamp();
        DesiredDirectionResolution desiredResolution = ResolveDesiredDirection(self.CharacterKey, tile, startX, startY, stableGoalPosition, goalKind);
        desiredResolution = ApplyPathDirectionBlend(agent, desiredResolution, startX, startY);
        Vector3 desiredDirection = desiredResolution.Direction;
        if (desiredResolution.Source == DesiredDirectionSource.Zero
            && !(goalKind == TileGoalKind.FinalGoal && startX == goalX && startY == goalY))
        {
            return FailNoFallback(
                self,
                $"tile produced zero flow start=({startX},{startY}) sector={startSectorId} goalKind={goalKind} portal={downstreamPortalId} tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)}",
                occupiedGoalPosition,
                out velocity);
        }

        Vector3 desiredVelocity = desiredDirection * maxSpeed;
        SetCurrentFrameNavigationIntent(agent, stableGoalPosition, desiredVelocity);
        _perf.DesiredTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
        LogFlowExecutionMismatchDiagnostic(
            self,
            agent,
            startX,
            startY,
            goalX,
            goalY,
            startSectorId,
            goalSectorId,
            goalPosition,
            occupiedGoalPosition,
            stableGoalPosition,
            goalKind,
            downstreamPortalId,
            tile,
            desiredResolution,
            desiredDirection,
            desiredVelocity,
            preferredAgentTypeId);

        if (debugMove)
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowBridgeDiag] key={self.CharacterKey} pos={self.Position} rawGoal={goalPosition} occupiedGoal={occupiedGoalPosition} " +
                $"stableGoal={stableGoalPosition} start=({startX},{startY}) goal=({goalX},{goalY}) sector={startSectorId}->{goalSectorId} " +
                $"handle={FormatPathHandle(agent.NavState.PathHandle)} goalKind={goalKind} portal={downstreamPortalId} " +
                $"desiredSrc={desiredResolution.Source} hasLOS={desiredResolution.HasLineOfSight} desiredDir={desiredDirection} desiredVel={desiredVelocity} " +
                $"flow={desiredResolution.Flow} tileTarget={desiredResolution.TileTargetPosition} integration={desiredResolution.Integration:F3} " +
                $"currentFlow={agent.NavState.CurrentFlowDirection} tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
                $"navPathDiag={BuildNavPathDiagnostics(self.Position, stableGoalPosition, preferredAgentTypeId)}");
        }

        BottleneckDecision bottleneckDecision = FreeMoveDecision;
        sectionStartTicks = Stopwatch.GetTimestamp();
        if (TryResolveActiveBottleneck(agent, tile, goalKind, downstreamPortalId, startSectorId, startX, startY, desiredDirection, out CorridorBottleneckDescriptor bottleneck))
            bottleneckDecision = EvaluateBottleneck(agent, bottleneck);
        _perf.BottleneckTicks += Stopwatch.GetTimestamp() - sectionStartTicks;

        sectionStartTicks = Stopwatch.GetTimestamp();
        velocity = ResolveCrowdSteering(self, agent, stableGoalPosition, desiredVelocity, desiredResolution, tile, bottleneckDecision);
        _perf.SteeringTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
        UpdateResolvedVelocity(selfId, stableGoalPosition, desiredVelocity, velocity);

        if (debugMove)
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[{self.CharacterKey}] Flow steer start=({startX},{startY}) goal=({goalX},{goalY}) rawGoal={goalPosition} occupiedGoal={occupiedGoalPosition} stableGoal={stableGoalPosition} " +
                $"sector={startSectorId}->{goalSectorId} goalKind={goalKind} portal={downstreamPortalId} " +
                $"desiredDir={desiredDirection} desiredSrc={desiredResolution.Source} flow={desiredResolution.Flow} " +
                $"los={desiredResolution.HasLineOfSight} tileTarget={desiredResolution.TileTargetPosition} " +
                $"desiredVel={desiredVelocity} resolvedVel={velocity} " +
                $"pathSectorIndex={agent.NavState.PathHandle?.CurrentSectorIndex ?? -1}");
        }
        _perf.Calls++;
        _perf.TotalTicks += Stopwatch.GetTimestamp() - totalStartTicks;
        return true;
    }

    public static bool TryGetIdleOverlapRecoveryVelocity(IEntityContext self, float maxSpeed, out Vector3 velocity)
    {
        BeginPerfCall();
        velocity = Vector3.zero;
        if (self == null)
            throw new InvalidOperationException("FlowFieldCrowdMovementSystem.TryGetIdleOverlapRecoveryVelocity failed: self is null.");
        if (self.MoveExecutor != null && self.MoveExecutor.MovementMode != MovementMode.Normal)
            return false;

        int selfId = ResolveAgentId(self);
        if (!Agents.TryGetValue(selfId, out AgentRuntimeData agent))
        {
            RegisterSyntheticAgent(self);
            agent = Agents[selfId];
        }
        else
        {
            agent.Position = self.Position;
            agent.Radius = ResolveCollisionRadius(self);
            agent.NavState.LastMovementMode = self.MoveExecutor?.MovementMode ?? MovementMode.Normal;
        }

        if (!CanUseDynamicAvoidance(agent))
            return false;

        int preferredAgentTypeId = agent.AgentTypeId;
        if (!TryEnsureWorldBuilt(preferredAgentTypeId))
            return false;

        float recoveryRadius = Mathf.Max(agent.Radius * 2.5f, _world.CellSize * 2f);
        List<AgentRuntimeData> nearbyAgents = CollectNearbyDynamicNeighbors(agent, recoveryRadius);
        Vector3 recovery = Vector3.zero;
        int overlapCount = 0;
        int skippedDynamicCount = 0;
        float maxPenetration = 0f;
        string topOverlap = "none";
        for (int i = 0; i < nearbyAgents.Count; i++)
        {
            AgentRuntimeData other = nearbyAgents[i];
            if (!CanUseDynamicAvoidance(other))
            {
                skippedDynamicCount++;
                continue;
            }

            Vector3 away = agent.Position - other.Position;
            away.y = 0f;
            float distance = away.magnitude;
            float combinedRadius = agent.Radius + other.Radius;
            float penetration = combinedRadius - distance;
            if (penetration <= Mathf.Max(0.02f, combinedRadius * 0.08f))
                continue;

            Vector3 direction = distance > 0.0001f
                ? away / distance
                : ResolveStableIdleSeparationDirection(agent, other);
            recovery += direction * Mathf.Clamp01(penetration / Mathf.Max(combinedRadius, 0.001f));
            if (penetration > maxPenetration)
            {
                maxPenetration = penetration;
                topOverlap = $"other={other.CharacterKey} id={other.Id} pos={other.Position} dist={distance:F3} combined={combinedRadius:F3} " +
                             $"otherState={other.State} otherIntent={other.HasNavigationIntent} otherParticipate={ShouldParticipateInDynamicAvoidance(other)} " +
                             $"otherMoveMode={other.NavState.LastMovementMode} otherMoveComp={other.MoveCompTypeName}";
            }
            overlapCount++;
        }

        if (overlapCount == 0 || recovery.sqrMagnitude <= 0.0001f)
        {
            LogIdleOverlapRecoveryDiagnostic(agent, false, recoveryRadius, nearbyAgents.Count, skippedDynamicCount, overlapCount, maxPenetration, topOverlap, Vector3.zero);
            return false;
        }

        float speed = Mathf.Min(Mathf.Max(0.05f, maxSpeed), IdleOverlapRecoverySpeed);
        velocity = recovery.normalized * speed;
        UpdateResolvedVelocity(selfId, self.Position, Vector3.zero, velocity);
        LogIdleOverlapRecoveryDiagnostic(agent, true, recoveryRadius, nearbyAgents.Count, skippedDynamicCount, overlapCount, maxPenetration, topOverlap, velocity);
        return true;
    }

    private static void LogIdleOverlapRecoveryDiagnostic(
        AgentRuntimeData agent,
        bool resolved,
        float recoveryRadius,
        int nearbyCount,
        int skippedDynamicCount,
        int overlapCount,
        float maxPenetration,
        string topOverlap,
        Vector3 velocity)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;
        if (agent == null)
            throw new InvalidOperationException("LogIdleOverlapRecoveryDiagnostic failed: agent is null.");

        int frame = GetFrameCount();
        if (agent.NavState.LastIdleOverlapDiagnosticFrame >= 0
            && frame - agent.NavState.LastIdleOverlapDiagnosticFrame < 20)
        {
            return;
        }

        agent.NavState.LastIdleOverlapDiagnosticFrame = frame;
        Debug.LogWarning(
            $"[FlowIdleOverlapRecovery] frame={frame} resolved={resolved} key={agent.CharacterKey} id={agent.Id} " +
            $"pos={agent.Position} radius={agent.Radius:F3} state={agent.State} intent={agent.HasNavigationIntent} " +
            $"participate={ShouldParticipateInDynamicAvoidance(agent)} moveMode={agent.NavState.LastMovementMode} " +
            $"recoveryRadius={recoveryRadius:F3} nearby={nearbyCount} skippedDynamic={skippedDynamicCount} overlaps={overlapCount} " +
            $"maxPenetration={maxPenetration:F3} velocity={velocity} top={topOverlap}");
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
            if (!CanUseDynamicAvoidance(other))
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
            .Append(" participate=").Append(ShouldParticipateInDynamicAvoidance(agent))
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
                .Append(",state=").Append(other.State)
                .Append(",intent=").Append(other.HasNavigationIntent)
                .Append(",participate=").Append(ShouldParticipateInDynamicAvoidance(other))
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
        string navEdge = BuildNavEdgeDiagnostic(currentPosition, agent.AgentTypeId);
        string cell = BuildAgentCellDiagnostic(agent, currentPosition);
        string executorRay = BuildExecutorRayDiagnostic(currentPosition, desiredHorizontalDisplacement, inputVelocity, agent.AgentTypeId);
        string lineOfSight = nav.LastSteeringDesiredSource == DesiredDirectionSource.LineOfSight
            ? BuildLineOfSightFailureDiagnostic(currentPosition, nav.LastSteeringTileTarget, agent.AgentTypeId)
            : string.Empty;

        Debug.LogWarning(
            $"[FlowConstraintDiag] reason={executorReason} key={agent.CharacterKey} id={agent.Id} frame={frameCount} " +
            $"pos={currentPosition} agentPos={agent.Position} radius={agent.Radius:F3} agentType={agent.AgentTypeId} mode={nav.LastMovementMode} " +
            $"inputVelocity={inputVelocity} desiredDisp={desiredHorizontalDisplacement} {executorRay} " +
            $"steerFrame={nav.LastSteeringFrame} goal={nav.LastSteeringGoal} desiredSrc={nav.LastSteeringDesiredSource} " +
            $"desiredDir={nav.LastSteeringDesiredDirection} desiredVel={nav.LastSteeringDesiredVelocity} flow={nav.LastSteeringFlow} " +
            $"los={nav.LastSteeringHasLineOfSight} tileTarget={nav.LastSteeringTileTarget} integration={nav.LastSteeringIntegration:F3} " +
            $"baseVel={nav.LastSteeringBaseVelocity} agentAvoid={nav.LastSteeringAgentAvoidance} " +
            $"clampedAgentAvoid={nav.LastSteeringClampedAgentAvoidance} boundaryAvoid={nav.LastSteeringBoundaryAvoidance} " +
            $"laneVel={nav.LastSteeringLaneVelocity} resultPreClamp={nav.LastSteeringResultPreClamp} result={nav.LastSteeringResult} " +
            $"steerEdgeNormal={nav.LastSteeringEdgeNormal} steerEdgeDist={nav.LastSteeringEdgeDistance:F3} maxSpeed={nav.LastSteeringMaxSpeed:F3} " +
            $"{cell} {gridEdge} {navEdge} {lineOfSight}");
    }

    private static string BuildLineOfSightFailureDiagnostic(Vector3 fromPosition, Vector3 targetPosition, int agentTypeId)
    {
        if (_world == null)
            return "[FlowLineOfSightDiag] world=null";

        bool fromInGrid = _world.WorldToGrid(fromPosition, out int fromX, out int fromY);
        bool targetInGrid = _world.WorldToGrid(targetPosition, out int targetX, out int targetY);
        if (!fromInGrid || !targetInGrid)
        {
            return $"[FlowLineOfSightDiag] fromInGrid={fromInGrid} targetInGrid={targetInGrid} " +
                   $"fromPos={fromPosition} targetPos={targetPosition}";
        }

        bool gridLos = HasGridLineOfSight(_world, fromX, fromY, targetX, targetY);
        bool navLos = HasNavAnchorLineOfSight(_world, fromX, fromY, targetX, targetY);
        return $"[FlowLineOfSightDiag] from=({fromX},{fromY}) target=({targetX},{targetY}) " +
               $"fromPos={fromPosition} targetPos={targetPosition} gridLos={gridLos} navLos={navLos} " +
               $"cells={BuildLineOfSightCellTrace(_world, fromX, fromY, targetX, targetY)} " +
               $"obstacles={BuildNearbyObstacleDiagnostics(fromPosition, targetPosition, agentTypeId)}";
    }

    private static string BuildLineOfSightCellTrace(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        if (world == null)
            return "world-null";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("[");
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int cx = x0;
        int cy = y0;
        int previousX = x0;
        int previousY = y0;
        int count = 0;
        const int maxCells = 96;

        while (true)
        {
            if (count > 0)
                builder.Append(" -> ");

            AppendCellDiagnostic(builder, world, cx, cy, previousX, previousY, count == 0);
            count++;

            if (cx == x1 && cy == y1)
                break;
            if (count >= maxCells)
            {
                builder.Append(" -> truncated");
                break;
            }

            int oldX = cx;
            int oldY = cy;
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

            previousX = oldX;
            previousY = oldY;
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static void AppendCellDiagnostic(System.Text.StringBuilder builder, NavigationWorld world, int x, int y, int previousX, int previousY, bool isFirst)
    {
        bool inBounds = x >= 0 && x < world.Width && y >= 0 && y < world.Height;
        builder.Append("(").Append(x).Append(",").Append(y).Append(")");
        if (!inBounds)
        {
            builder.Append("{out}");
            return;
        }

        int index = world.GetIndex(x, y);
        bool walkable = world.WalkableMask != null && world.WalkableMask.Length == world.Width * world.Height && world.WalkableMask[index];
        bool baseWalkable = world.BaseWalkableMask != null && world.BaseWalkableMask.Length == world.Width * world.Height && world.BaseWalkableMask[index];
        int cost = world.CostField != null && world.CostField.Length == world.Width * world.Height ? world.CostField[index] : -1;
        int island = world.IslandIds != null && world.IslandIds.Length == world.Width * world.Height ? world.IslandIds[index] : -1;
        string mask = world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height
            ? world.NeighborTraversalMask[index].ToString("X2")
            : "NA";
        bool link = isFirst || CanTraverseNeighborCells(world, previousX, previousY, x, y);
        builder.Append("{walk=").Append(walkable)
            .Append(",base=").Append(baseWalkable)
            .Append(",cost=").Append(cost)
            .Append(",island=").Append(island)
            .Append(",mask=0x").Append(mask)
            .Append(",link=").Append(link)
            .Append("}");
    }

    private static string BuildNearbyObstacleDiagnostics(Vector3 fromPosition, Vector3 targetPosition, int agentTypeId)
    {
        if (_world == null)
            return "world-null";

        Vector3 min = Vector3.Min(fromPosition, targetPosition);
        Vector3 max = Vector3.Max(fromPosition, targetPosition);
        float padding = Mathf.Max(2f, _world.CellSize * 4f);
        Bounds corridor = new Bounds((min + max) * 0.5f, max - min);
        corridor.Expand(new Vector3(padding * 2f, 0f, padding * 2f));

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("[boxes=");
        int count = 0;
        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            Bounds bounds = new Bounds(box.Center, box.HalfExtents * 2f);
            if (!IntersectsXZ(corridor, bounds))
                continue;

            if (count > 0)
                builder.Append(" | ");

            AppendBoxObstacleDiagnostic(builder, box, bounds);
            count++;
            if (count >= 12)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");

        builder.Append(" circles=");
        count = 0;
        foreach (CircleObstacle circle in CircleObstacles.Values)
        {
            Bounds bounds = new Bounds(circle.Position, new Vector3(circle.Radius * 2f, 0f, circle.Radius * 2f));
            if (!IntersectsXZ(corridor, bounds))
                continue;

            if (count > 0)
                builder.Append(" | ");

            builder.Append("{id=").Append(circle.Id)
                .Append(",pos=").Append(circle.Position)
                .Append(",r=").Append(circle.Radius.ToString("F3"))
                .Append(",grid=").Append(FormatBoundsGridRange(bounds))
                .Append("}");
            count++;
            if (count >= 12)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");

        builder.Append(",agentType=").Append(agentTypeId).Append("]");
        return builder.ToString();
    }

    private static bool IntersectsXZ(Bounds a, Bounds b)
    {
        return a.min.x <= b.max.x && a.max.x >= b.min.x
               && a.min.z <= b.max.z && a.max.z >= b.min.z;
    }

    private static void AppendBoxObstacleDiagnostic(System.Text.StringBuilder builder, BoxObstacle box, Bounds bounds)
    {
        builder.Append("{id=").Append(box.Id)
            .Append(",center=").Append(box.Center)
            .Append(",half=").Append(box.HalfExtents)
            .Append(",grid=").Append(FormatBoundsGridRange(bounds))
            .Append("}");
    }

    private static string FormatBoundsGridRange(Bounds bounds)
    {
        if (_world == null)
            return "world-null";

        int minX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - _world.Origin.x) / _world.CellSize), 0, _world.Width - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - _world.Origin.x) / _world.CellSize), 0, _world.Width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - _world.Origin.z) / _world.CellSize), 0, _world.Height - 1);
        int maxY = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - _world.Origin.z) / _world.CellSize), 0, _world.Height - 1);
        return $"({minX},{minY})-({maxX},{maxY})";
    }

    private static Vector3 ResolveStableIdleSeparationDirection(AgentRuntimeData self, AgentRuntimeData other)
    {
        unchecked
        {
            int hash = (self.Id * 397) ^ other.Id;
            float angle = (hash & 0xFFFF) / 65535f * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }
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

        DrawSectorDebug();
        DrawPortalDebug();
        if (Config.DrawFlowFieldDebug)
            DrawFlowTileDebug();
        DrawBottleneckDebug();
        DrawAgentNavigationDebug();
    }

    private static int GetFrameCount()
    {
        return _hasTestTimeOverride ? _testFrameCount : Time.frameCount;
    }

    private static float GetTime()
    {
        return _hasTestTimeOverride ? _testTime : Time.time;
    }

    private static void BeginPerfCall()
    {
        int frame = GetFrameCount();
        if (!_perfInitialized)
        {
            _perf = new FlowPerfAccumulator { Frame = frame };
            _perfInitialized = true;
            return;
        }

        if (_perf.Frame == frame)
            return;

        FlushPerfIfNeeded();
        TrimMovingTargetAnchors();
        _perf = new FlowPerfAccumulator { Frame = frame };
    }

    private static void FlushPerfIfNeeded()
    {
        if (!_perfInitialized || _perf.Calls <= 0)
            return;

        LogAgentOverlapDiagnostics(_perf.Frame);

        long diagnosticTicks = _perf.TotalTicks
                               + _perf.WorldTicks
                               + _perf.PathTicks
                               + _perf.TileTicks
                               + _perf.SteeringTicks
                               + _perf.NeighborAvoidTicks
                               + _perf.AgentUpdateTicks;
        bool shouldAlwaysLog = _perf.WorldBuilds > 0
                               || _perf.RuntimeDirtyApplications > 0
                               || _perf.PathAdvanceFailures > 0
                               || _perf.TileReachabilityFailures > 0;
        bool debugMove = GameDebugSettings.IsEnabled(DebugCategory.Move);
        if (!debugMove && diagnosticTicks < FlowPerfLogThresholdTicks && !shouldAlwaysLog)
            return;

        Debug.Log(
            $"[FlowPerf] frame={_perf.Frame} calls={_perf.Calls} worldBuilds={_perf.WorldBuilds} pathBuilds={_perf.PathBuilds} tileBuilds={_perf.TileBuilds} " +
            $"total={TicksToMs(_perf.TotalTicks):F3}ms world={TicksToMs(_perf.WorldTicks):F3}ms cells={TicksToMs(_perf.ResolveCellsTicks):F3}ms " +
            $"path={TicksToMs(_perf.PathTicks):F3}ms advance={TicksToMs(_perf.AdvanceTicks):F3}ms tile={TicksToMs(_perf.TileTicks):F3}ms " +
            $"desired={TicksToMs(_perf.DesiredTicks):F3}ms bottleneck={TicksToMs(_perf.BottleneckTicks):F3}ms steering={TicksToMs(_perf.SteeringTicks):F3}ms " +
            $"neighborCollect={TicksToMs(_perf.NeighborCollectTicks):F3}ms neighborAvoid={TicksToMs(_perf.NeighborAvoidTicks):F3}ms " +
            $"boundary={TicksToMs(_perf.BoundaryTicks):F3}ms agentUpdate={TicksToMs(_perf.AgentUpdateTicks):F3}ms neighborChecks={_perf.NeighborChecks} " +
            $"sectorPath(searches={_perf.SectorPathSearches},cacheHits={_perf.SectorPathCacheHits}) " +
            $"tileCache(hits={_perf.TileCacheHits},misses={_perf.TileCacheMisses}) " +
            $"stableGoal(raw={_perf.StableGoalRaw},reuse={_perf.StableGoalReuse},initial={_perf.StableGoalRefreshInitial},cell={_perf.StableGoalRefreshCellDelta}) " +
            $"pathReasons(noHandle={_perf.PathBuildNoHandle},worldMismatch={_perf.PathBuildWorldMismatch},goalSectorMismatch={_perf.PathBuildGoalSectorMismatch},goalCellMismatch={_perf.PathBuildGoalCellMismatch},invalid={_perf.PathBuildInvalidHandle}) " +
            $"advanceFail={_perf.PathAdvanceFailures} tileReachFail={_perf.TileReachabilityFailures} runtimeDirty(apply={_perf.RuntimeDirtyApplications},sectors={_perf.RuntimeDirtySectorCount})");
    }

    private static void LogAgentOverlapDiagnostics(int frame)
    {
        if (Agents.Count < 2)
            return;
        if (_lastOverlapDiagnosticsFrame >= 0 && frame - _lastOverlapDiagnosticsFrame < 20)
            return;

        _lastOverlapDiagnosticsFrame = frame;
        const int maxLoggedPairs = 10;
        OverlapDiagnosticRecord[] selected = new OverlapDiagnosticRecord[maxLoggedPairs];
        int overlapPairs = 0;
        int activeOverlapPairs = 0;
        Dictionary<string, int> characterPairCounts = new Dictionary<string, int>(16);
        foreach (AgentRuntimeData self in Agents.Values)
        {
            if (self.IgnoreAgentCollision)
                continue;

            foreach (AgentRuntimeData other in Agents.Values)
            {
                if (other.Id <= self.Id || other.IgnoreAgentCollision)
                    continue;

                Vector3 delta = other.Position - self.Position;
                delta.y = 0f;
                float distance = delta.magnitude;
                float combinedRadius = self.Radius + other.Radius;
                float penetration = combinedRadius - distance;
                if (penetration <= Mathf.Max(0.02f, combinedRadius * 0.08f))
                    continue;

                overlapPairs++;
                bool activePair = self.HasNavigationIntent
                                  || other.HasNavigationIntent
                                  || ShouldParticipateInDynamicAvoidance(self)
                                  || ShouldParticipateInDynamicAvoidance(other);
                if (activePair)
                    activeOverlapPairs++;

                AccumulateOverlapCharacterPair(characterPairCounts, self, other);
                float priority = penetration + (activePair ? 1000f : 0f);
                TryInsertOverlapDiagnostic(selected, new OverlapDiagnosticRecord(self, other, penetration, distance, combinedRadius, priority));
            }
        }

        if (overlapPairs == 0)
            return;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(2048);
        int loggedPairs = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            OverlapDiagnosticRecord record = selected[i];
            if (record.Self == null || record.Other == null)
                continue;

            builder.Append("[FlowAgentOverlap] frame=");
            builder.Append(frame);
            builder.Append(" penetration=");
            builder.Append(record.Penetration.ToString("F3"));
            builder.Append(" distance=");
            builder.Append(record.Distance.ToString("F3"));
            builder.Append(" combinedRadius=");
            builder.Append(record.CombinedRadius.ToString("F3"));
            builder.Append(" self={");
            AppendAgentAvoidanceDiagnostics(builder, record.Self);
            builder.Append("} other={");
            AppendAgentAvoidanceDiagnostics(builder, record.Other);
            builder.Append("}\n");
            loggedPairs++;
        }

        builder.Append("[FlowAgentOverlapSummary] frame=");
        builder.Append(frame);
        builder.Append(" overlapPairs=");
        builder.Append(overlapPairs);
        builder.Append(" activePairs=");
        builder.Append(activeOverlapPairs);
        builder.Append(" logged=");
        builder.Append(loggedPairs);
        builder.Append(" characterPairs=");
        AppendOverlapCharacterPairCounts(builder, characterPairCounts);
        Debug.LogWarning(builder.ToString());
    }

    private readonly struct OverlapDiagnosticRecord
    {
        public readonly AgentRuntimeData Self;
        public readonly AgentRuntimeData Other;
        public readonly float Penetration;
        public readonly float Distance;
        public readonly float CombinedRadius;
        public readonly float Priority;

        public OverlapDiagnosticRecord(AgentRuntimeData self, AgentRuntimeData other, float penetration, float distance, float combinedRadius, float priority)
        {
            Self = self;
            Other = other;
            Penetration = penetration;
            Distance = distance;
            CombinedRadius = combinedRadius;
            Priority = priority;
        }
    }

    private static void TryInsertOverlapDiagnostic(OverlapDiagnosticRecord[] selected, OverlapDiagnosticRecord candidate)
    {
        for (int i = 0; i < selected.Length; i++)
        {
            if (selected[i].Self != null && candidate.Priority <= selected[i].Priority)
                continue;

            for (int shift = selected.Length - 1; shift > i; shift--)
                selected[shift] = selected[shift - 1];

            selected[i] = candidate;
            return;
        }
    }

    private static void AccumulateOverlapCharacterPair(Dictionary<string, int> counts, AgentRuntimeData self, AgentRuntimeData other)
    {
        string left = string.IsNullOrEmpty(self.CharacterKey) ? "unknown" : self.CharacterKey;
        string right = string.IsNullOrEmpty(other.CharacterKey) ? "unknown" : other.CharacterKey;
        string key = string.CompareOrdinal(left, right) <= 0 ? $"{left}+{right}" : $"{right}+{left}";
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    private static void AppendOverlapCharacterPairCounts(System.Text.StringBuilder builder, Dictionary<string, int> counts)
    {
        builder.Append('[');
        bool first = true;
        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (!first)
                builder.Append(", ");
            first = false;
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
        }

        if (first)
            builder.Append("none");
        builder.Append(']');
    }

    private static void AppendAgentAvoidanceDiagnostics(System.Text.StringBuilder builder, AgentRuntimeData agent)
    {
        builder.Append("key=");
        builder.Append(agent.CharacterKey);
        builder.Append(" id=");
        builder.Append(agent.Id);
        builder.Append(" pos=");
        builder.Append(agent.Position);
        builder.Append(" radius=");
        builder.Append(agent.Radius.ToString("F3"));
        builder.Append(" side=");
        builder.Append(agent.Side);
        builder.Append(" ignore=");
        builder.Append(agent.IgnoreAgentCollision);
        builder.Append(" leader=");
        builder.Append(agent.IsLeader);
        builder.Append(" intent=");
        builder.Append(agent.HasNavigationIntent);
        builder.Append(" participate=");
        builder.Append(ShouldParticipateInDynamicAvoidance(agent));
        builder.Append(" moveMode=");
        builder.Append(agent.NavState.LastMovementMode);
        builder.Append(" moveComp=");
        builder.Append(agent.MoveCompTypeName);
        builder.Append(" entity=");
        builder.Append(agent.EntityTypeName);
        builder.Append(" source=");
        builder.Append(agent.RegistrationSource);
        builder.Append(" synthetic=");
        builder.Append(agent.IsSyntheticRegistration);
        builder.Append(" desired=");
        builder.Append(agent.NavState.DesiredVelocity);
        builder.Append(" resolved=");
        builder.Append(agent.NavState.ResolvedVelocity);
        builder.Append(" resolvedFrame=");
        builder.Append(agent.NavState.ResolvedVelocityFrame);
        builder.Append(" lastAvoidFrame=");
        builder.Append(agent.LastAvoidanceActiveFrame);
    }

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
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
        Agents[agentId].NavState.LastMovementMode = self.MoveExecutor?.MovementMode ?? MovementMode.Normal;
        UpdateAgentNavigationIntent(Agents[agentId], self.MoveComp);
    }

    private static void DrawSectorDebug()
    {
        if (_world.Sectors == null)
            return;

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

    private static void DrawPortalDebug()
    {
        if (_world.Portals == null)
            return;

        for (int i = 0; i < _world.Portals.Length; i++)
        {
            PortalData portal = _world.Portals[i];
            Gizmos.color = portal.IsNarrow ? Color.red : Color.green;
            Gizmos.DrawSphere(portal.WorldCenter + Vector3.up * 0.05f, _world.CellSize * 0.18f);
            DrawPortalCells(portal.CellsA, portal.IsNarrow ? new Color(1f, 0.35f, 0.35f, 0.35f) : new Color(0.35f, 1f, 0.35f, 0.2f), 0.015f);
            DrawPortalCells(portal.CellsB, portal.IsNarrow ? new Color(1f, 0.35f, 0.35f, 0.35f) : new Color(0.35f, 1f, 0.35f, 0.2f), 0.015f);
        }
    }

    private static void DrawPortalCells(Vector2Int[] cells, Color color, float yOffset)
    {
        if (cells == null)
            return;

        Gizmos.color = color;
        Vector3 size = new Vector3(_world.CellSize * 0.9f, 0.01f, _world.CellSize * 0.9f);
        for (int i = 0; i < cells.Length; i++)
        {
            Vector3 center = _world.GridToWorldCenter(cells[i].x, cells[i].y);
            center.y += yOffset;
            Gizmos.DrawCube(center, size);
        }
    }

    private static void DrawFlowTileDebug()
    {
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            FlowTileCacheEntry tile = pair.Value;
            DrawTileIntegrationHeat(tile);
            DrawTileFlowArrows(tile);
        }
    }

    private static void DrawTileIntegrationHeat(FlowTileCacheEntry tile)
    {
        if (tile?.Integration == null)
            return;

        float maxReachableCost = 0f;
        for (int i = 0; i < tile.Integration.Length; i++)
        {
            float cost = tile.Integration[i];
            if (!float.IsPositiveInfinity(cost) && cost > maxReachableCost)
                maxReachableCost = cost;
        }

        float normalization = Mathf.Max(maxReachableCost, 0.001f);
        Vector3 size = new Vector3(_world.CellSize * 0.88f, 0.006f, _world.CellSize * 0.88f);
        for (int worldY = tile.StartY; worldY < tile.StartY + tile.Height; worldY++)
        {
            for (int worldX = tile.StartX; worldX < tile.StartX + tile.Width; worldX++)
            {
                if (!_world.IsWalkable(worldX, worldY))
                    continue;

                int localIndex = tile.GetLocalIndex(worldX, worldY);
                float cost = tile.Integration[localIndex];
                if (float.IsPositiveInfinity(cost))
                    continue;

                float t = Mathf.Clamp01(cost / normalization);
                Gizmos.color = Color.Lerp(new Color(0.1f, 0.9f, 0.25f, 0.1f), new Color(1f, 0.55f, 0.05f, 0.16f), t);
                Vector3 center = _world.GridToWorldCenter(worldX, worldY);
                center.y += 0.01f;
                Gizmos.DrawCube(center, size);
            }
        }
    }

    private static void DrawTileFlowArrows(FlowTileCacheEntry tile)
    {
        if (tile?.FlowDirections == null)
            return;

        for (int worldY = tile.StartY; worldY < tile.StartY + tile.Height; worldY++)
        {
            for (int worldX = tile.StartX; worldX < tile.StartX + tile.Width; worldX++)
            {
                if (!_world.IsWalkable(worldX, worldY))
                    continue;

                int localIndex = tile.GetLocalIndex(worldX, worldY);
                Vector2 flow2 = tile.FlowDirections[localIndex];
                if (flow2.sqrMagnitude <= 0.0001f)
                    continue;

                Vector3 center = _world.GridToWorldCenter(worldX, worldY) + Vector3.up * 0.035f;
                Vector3 dir = new Vector3(flow2.x, 0f, flow2.y).normalized;
                Gizmos.color = tile.HasLineOfSight[localIndex]
                    ? new Color(0.2f, 0.95f, 1f, 0.85f)
                    : new Color(1f, 0.95f, 0.2f, 0.7f);
                DrawArrow(center, dir, _world.CellSize * 0.32f, _world.CellSize * 0.08f);
            }
        }
    }

    private static void DrawBottleneckDebug()
    {
        foreach (KeyValuePair<int, CorridorBottleneckDescriptor> pair in CorridorBottlenecks)
        {
            CorridorBottleneckDescriptor descriptor = pair.Value;
            Vector3 anchor = descriptor.AnchorWorld + Vector3.up * 0.08f;
            int ownerDirection = descriptor.Direction;
            bool hasRuntimeState = Bottlenecks.TryGetValue(descriptor.BottleneckId, out BottleneckRuntimeState state);
            if (hasRuntimeState && state.CurrentDirection != 0)
                ownerDirection = state.CurrentDirection;

            Gizmos.color = ownerDirection >= 0
                ? new Color(0.4f, 1f, 0.95f, 0.9f)
                : new Color(1f, 0.45f, 0.8f, 0.9f);
            Gizmos.DrawWireSphere(anchor, descriptor.InfluenceRadius);
            DrawArrow(anchor, descriptor.CorridorAxis.normalized * ownerDirection, _world.CellSize * 0.65f, _world.CellSize * 0.14f);

            if (!hasRuntimeState)
                continue;

            if (state.CurrentOwnerAgentId != -1 && Agents.TryGetValue(state.CurrentOwnerAgentId, out AgentRuntimeData ownerAgent))
            {
                Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.9f);
                Gizmos.DrawLine(anchor, ownerAgent.Position + Vector3.up * 0.18f);
            }

            if (state.ConvoyTokenAgentId != -1 && Agents.TryGetValue(state.ConvoyTokenAgentId, out AgentRuntimeData convoyAgent))
            {
                Gizmos.color = new Color(0.25f, 0.95f, 0.35f, 0.75f);
                Gizmos.DrawLine(anchor + Vector3.up * 0.05f, convoyAgent.Position + Vector3.up * 0.24f);
            }

            if (state.PriorityOverrideAgentId != -1 && Agents.TryGetValue(state.PriorityOverrideAgentId, out AgentRuntimeData overrideAgent))
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.85f);
                Gizmos.DrawLine(anchor + Vector3.up * 0.1f, overrideAgent.Position + Vector3.up * 0.28f);
            }

            Vector3 sideAxis = Vector3.Cross(Vector3.up, descriptor.CorridorAxis).normalized;
            if (sideAxis.sqrMagnitude <= 0.0001f)
                sideAxis = Vector3.right;

            for (int i = 0; i < state.WaitingAgentsOrdered.Count; i++)
            {
                if (!Agents.TryGetValue(state.WaitingAgentsOrdered[i], out AgentRuntimeData waitingAgent))
                    continue;

                Vector3 queuePoint = anchor
                                     - descriptor.CorridorAxis.normalized * (_world.CellSize * (0.9f + i * 0.65f))
                                     + sideAxis * _world.CellSize * 0.18f;
                Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.85f);
                Gizmos.DrawLine(queuePoint, waitingAgent.Position + Vector3.up * 0.14f);
                Gizmos.DrawSphere(queuePoint, _world.CellSize * 0.08f);
            }
        }
    }

    private static void DrawAgentNavigationDebug()
    {
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData agent = pair.Value;
            Vector3 origin = agent.Position + Vector3.up * 0.1f;

            Gizmos.color = agent.IgnoreAgentCollision ? Color.gray : Color.white;
            Gizmos.DrawWireSphere(origin, Mathf.Max(agent.Radius, 0.05f));

            if (_world.IsWalkable(agent.NavState.CurrentCell.x, agent.NavState.CurrentCell.y))
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.22f);
                Vector3 cellCenter = _world.GridToWorldCenter(agent.NavState.CurrentCell.x, agent.NavState.CurrentCell.y) + Vector3.up * 0.02f;
                Gizmos.DrawCube(cellCenter, new Vector3(_world.CellSize * 0.35f, 0.02f, _world.CellSize * 0.35f));
            }

            if (agent.NavState.CurrentFlowDirection.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = Color.yellow;
                DrawArrow(origin, agent.NavState.CurrentFlowDirection.normalized, _world.CellSize * 0.5f, _world.CellSize * 0.12f);
            }

            if (agent.NavState.DesiredVelocity.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = new Color(0.2f, 0.45f, 1f, 0.85f);
                DrawArrow(origin + Vector3.up * 0.03f, agent.NavState.DesiredVelocity.normalized, _world.CellSize * 0.45f, _world.CellSize * 0.1f);
            }

            if (agent.NavState.ResolvedVelocity.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
                DrawArrow(origin + Vector3.up * 0.06f, agent.NavState.ResolvedVelocity.normalized, _world.CellSize * 0.58f, _world.CellSize * 0.13f);
            }

            if (agent.NavState.LastMovementMode != MovementMode.Normal)
            {
                Gizmos.color = agent.NavState.LastMovementMode == MovementMode.Displaced
                    ? new Color(0.35f, 0.9f, 1f, 0.8f)
                    : new Color(1f, 0.2f, 0.2f, 0.8f);
                Gizmos.DrawWireCube(origin + Vector3.up * 0.1f, new Vector3(_world.CellSize * 0.25f, 0.08f, _world.CellSize * 0.25f));
            }

            if (agent.NavState.PathHandle?.PortalIds != null)
            {
                Gizmos.color = new Color(1f, 0.95f, 0.3f, 0.7f);
                int[] portals = agent.NavState.PathHandle.PortalIds;
                for (int i = agent.NavState.PathHandle.CurrentSectorIndex; i < portals.Length; i++)
                {
                    if (!TryGetPortalById(_world, portals[i], out PortalData portal))
                        continue;

                    Vector3 center = portal.WorldCenter + Vector3.up * 0.05f;
                    Gizmos.DrawSphere(center, _world.CellSize * 0.1f);
                }
            }
        }
    }

    private static bool TryGetPortalById(NavigationWorld world, int portalId, out PortalData portal)
    {
        portal = null;
        return world != null
               && world.PortalsById != null
               && world.PortalsById.TryGetValue(portalId, out portal);
    }

    private static PortalData GetPortalById(NavigationWorld world, int portalId)
    {
        if (TryGetPortalById(world, portalId, out PortalData portal))
            return portal;

        throw new InvalidOperationException($"GetPortalById failed: portal {portalId} not found.");
    }

    private static void DrawArrow(Vector3 origin, Vector3 direction, float length, float headSize)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 dir = direction.normalized;
        Vector3 tip = origin + dir * length;
        Gizmos.DrawLine(origin, tip);

        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        Gizmos.DrawLine(tip, tip - dir * headSize + right * headSize * 0.5f);
        Gizmos.DrawLine(tip, tip - dir * headSize - right * headSize * 0.5f);
    }

    private static void UpdateAgentNavigationIntent(AgentRuntimeData agent, IMoveComp moveComp)
    {
        if (agent == null)
            throw new InvalidOperationException("UpdateAgentNavigationIntent failed: agent is null.");

        bool hasIntent = false;
        bool moving = false;
        if (moveComp is CharacterMoveComp characterMoveComp)
        {
            hasIntent = characterMoveComp.HasNavigationTarget || characterMoveComp.IsMoving;
            moving = characterMoveComp.IsMoving;
        }
        else if (moveComp != null && moveComp.GetType() != typeof(NoMoveComp))
        {
            hasIntent = true;
        }

        agent.HasNavigationIntent = hasIntent;
        if (hasIntent || moving)
            agent.LastAvoidanceActiveFrame = GetFrameCount();
    }

    private static bool ShouldParticipateInDynamicAvoidance(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("ShouldParticipateInDynamicAvoidance failed: agent is null.");

        if (!CanUseDynamicAvoidance(agent))
            return false;

        if (agent.HasNavigationIntent)
            return true;

        if (agent.State == GroupMoveCoordinator.AgentState.Combat)
            return true;

        int activeAge = GetFrameCount() - agent.LastAvoidanceActiveFrame;
        if (activeAge <= 6)
        {
            return agent.NavState.DesiredVelocity.sqrMagnitude > 0.0001f
                   || agent.NavState.ResolvedVelocity.sqrMagnitude > 0.0001f
                   || agent.NavState.CurrentFlowDirection.sqrMagnitude > 0.0001f;
        }

        return false;
    }

    private static bool CanUseDynamicAvoidance(AgentRuntimeData agent)
    {
        if (agent == null)
            throw new InvalidOperationException("CanUseDynamicAvoidance failed: agent is null.");

        if (agent.IgnoreAgentCollision)
            return false;

        if (agent.MoveCompTypeName == nameof(NoMoveComp))
            return false;
        if (agent.NavState.LastMovementMode != MovementMode.Normal)
            return false;

        return true;
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
        if (!CanUseDynamicAvoidance(otherAgent))
            return false;

        IEntityContext currentTarget = selfContext.TargetComp?.CurrentTarget;
        if (currentTarget != null && ResolveAgentId(currentTarget) == otherAgent.Id)
            return false;

        IEntityContext followTarget = selfContext.TargetComp?.FollowTarget;
        if (followTarget != null && ResolveAgentId(followTarget) == otherAgent.Id)
            return false;

        return true;
    }

    private static int BuildSpatialBucketKey(int cellX, int cellY)
    {
        unchecked
        {
            return (cellX * 73856093) ^ (cellY * 19349663);
        }
    }

    private static void ResolveSpatialBucketCell(Vector3 position, out int cellX, out int cellY)
    {
        if (_world == null)
            throw new InvalidOperationException("ResolveSpatialBucketCell failed: world is null.");

        float inverseCellSize = 1f / Mathf.Max(_world.CellSize, 0.001f);
        Vector3 local = position - _world.Origin;
        cellX = Mathf.FloorToInt(local.x * inverseCellSize);
        cellY = Mathf.FloorToInt(local.z * inverseCellSize);
    }

    private static void EnsureAgentSpatialBuckets()
    {
        if (_world == null)
            throw new InvalidOperationException("EnsureAgentSpatialBuckets failed: world is null.");

        int frameCount = GetFrameCount();
        int worldVersion = _world.Version;
        if (_lastAgentSpatialBucketFrame == frameCount && _lastAgentSpatialBucketWorldVersion == worldVersion)
            return;

        _lastAgentSpatialBucketFrame = frameCount;
        _lastAgentSpatialBucketWorldVersion = worldVersion;
        AgentSpatialBuckets.Clear();
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (!CanUseDynamicAvoidance(agent))
                continue;

            ResolveSpatialBucketCell(agent.Position, out int cellX, out int cellY);
            int bucketKey = BuildSpatialBucketKey(cellX, cellY);
            if (!AgentSpatialBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
            {
                bucket = new List<AgentRuntimeData>(4);
                AgentSpatialBuckets.Add(bucketKey, bucket);
            }

            bucket.Add(agent);
        }
    }

    private static List<AgentRuntimeData> CollectNearbyDynamicNeighbors(AgentRuntimeData self, float avoidRadius)
    {
        if (self == null)
            throw new InvalidOperationException("CollectNearbyDynamicNeighbors failed: self is null.");
        if (_world == null)
            throw new InvalidOperationException("CollectNearbyDynamicNeighbors failed: world is null.");

        NearbyAgentScratch.Clear();
        EnsureAgentSpatialBuckets();

        ResolveSpatialBucketCell(self.Position, out int selfCellX, out int selfCellY);
        int searchRadiusInCells = Mathf.Max(1, Mathf.CeilToInt(avoidRadius / Mathf.Max(_world.CellSize, 0.001f)));
        float avoidRadiusSq = avoidRadius * avoidRadius;
        for (int y = selfCellY - searchRadiusInCells; y <= selfCellY + searchRadiusInCells; y++)
        {
            for (int x = selfCellX - searchRadiusInCells; x <= selfCellX + searchRadiusInCells; x++)
            {
                int bucketKey = BuildSpatialBucketKey(x, y);
                if (!AgentSpatialBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    AgentRuntimeData other = bucket[i];
                    if (other.Id == self.Id)
                        continue;

                    Vector3 delta = other.Position - self.Position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > avoidRadiusSq)
                        continue;

                    NearbyAgentScratch.Add(other);
                }
            }
        }

        return NearbyAgentScratch;
    }

    private static void EnsureNavigationGoalOccupancyBuckets()
    {
        if (_world == null)
            throw new InvalidOperationException("EnsureNavigationGoalOccupancyBuckets failed: world is null.");

        int frameCount = GetFrameCount();
        int worldVersion = _world.Version;
        if (_lastNavigationGoalOccupancyBucketFrame == frameCount && _lastNavigationGoalOccupancyBucketWorldVersion == worldVersion)
            return;

        _lastNavigationGoalOccupancyBucketFrame = frameCount;
        _lastNavigationGoalOccupancyBucketWorldVersion = worldVersion;
        NavigationGoalOccupancyBuckets.Clear();
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (agent.IgnoreAgentCollision)
                continue;

            ResolveSpatialBucketCell(agent.Position, out int cellX, out int cellY);
            int bucketKey = BuildSpatialBucketKey(cellX, cellY);
            if (!NavigationGoalOccupancyBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
            {
                bucket = new List<AgentRuntimeData>(4);
                NavigationGoalOccupancyBuckets.Add(bucketKey, bucket);
            }

            bucket.Add(agent);
        }
    }

    private static bool IsPositionOccupiedByAgentSpatial(Vector3 position, float requiredDistance)
    {
        EnsureNavigationGoalOccupancyBuckets();

        ResolveSpatialBucketCell(position, out int centerCellX, out int centerCellY);
        int searchRadiusInCells = Mathf.Max(1, Mathf.CeilToInt(requiredDistance / Mathf.Max(_world.CellSize, 0.001f)));
        float requiredDistanceSq = requiredDistance * requiredDistance;
        for (int y = centerCellY - searchRadiusInCells; y <= centerCellY + searchRadiusInCells; y++)
        {
            for (int x = centerCellX - searchRadiusInCells; x <= centerCellX + searchRadiusInCells; x++)
            {
                int bucketKey = BuildSpatialBucketKey(x, y);
                if (!NavigationGoalOccupancyBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    AgentRuntimeData agent = bucket[i];
                    Vector3 offset = agent.Position - position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude < requiredDistanceSq)
                        return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindBlockingAgentSpatial(
        int selfId,
        int ignoredAgentId,
        Vector3 position,
        float requiredDistance,
        out int blockingAgentId,
        out float blockingDistance)
    {
        blockingAgentId = 0;
        blockingDistance = float.PositiveInfinity;
        EnsureNavigationGoalOccupancyBuckets();

        ResolveSpatialBucketCell(position, out int centerCellX, out int centerCellY);
        int searchRadiusInCells = Mathf.Max(1, Mathf.CeilToInt(requiredDistance / Mathf.Max(_world.CellSize, 0.001f)));
        float requiredDistanceSq = requiredDistance * requiredDistance;
        for (int y = centerCellY - searchRadiusInCells; y <= centerCellY + searchRadiusInCells; y++)
        {
            for (int x = centerCellX - searchRadiusInCells; x <= centerCellX + searchRadiusInCells; x++)
            {
                int bucketKey = BuildSpatialBucketKey(x, y);
                if (!NavigationGoalOccupancyBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    AgentRuntimeData agent = bucket[i];
                    if (agent.Id == selfId || agent.Id == ignoredAgentId)
                        continue;

                    Vector3 offset = agent.Position - position;
                    offset.y = 0f;
                    float distanceSq = offset.sqrMagnitude;
                    if (distanceSq >= requiredDistanceSq)
                        continue;

                    float distance = Mathf.Sqrt(distanceSq);
                    if (distance >= blockingDistance)
                        continue;

                    blockingDistance = distance;
                    blockingAgentId = agent.Id;
                }
            }
        }

        return blockingAgentId != 0;
    }

    private static Vector3 ResolveNeighborPredictedVelocity(AgentRuntimeData other)
    {
        if (other == null)
            throw new InvalidOperationException("ResolveNeighborPredictedVelocity failed: other is null.");

        if (other.NavState.ResolvedVelocityFrame == GetFrameCount())
            return other.NavState.ResolvedVelocity;

        if (other.NavState.DesiredVelocity.sqrMagnitude > 0.0001f)
            return other.NavState.DesiredVelocity;

        return other.NavState.ResolvedVelocity;
    }

    private static bool TryEnsureWorldBuilt(int preferredAgentTypeId)
    {
        int agentTypeId = ResolvePreferredAgentTypeId(preferredAgentTypeId);
        WorldRuntimeState state = GetOrCreateWorldState(agentTypeId);
        _activeWorldState = state;
        _world = state.World;

        if (!state.IsDirty && state.World != null)
        {
            if (state.DirtyRuntimeObstacleSectors.Count > 0 || state.RuntimeDirtyJob != null)
                DrainRuntimeDirtyJobForNavigationQuery(state);

            _world = state.World;
            return true;
        }

        if (!EnsureWorldBuildJob(state, agentTypeId))
            return false;

        ProcessWorldBuildJob(state, long.MaxValue, forceComplete: true);
        if (state.IsDirty || state.World == null || state.BuildJob != null)
            throw new InvalidOperationException($"TryEnsureWorldBuilt failed: world build did not complete agentType={agentTypeId}.");

        _world = state.World;
        return true;
    }

    private static bool EnsureWorldBuildJob(WorldRuntimeState state)
    {
        if (state == null)
            return false;
        if (state.BuildJob != null)
            return true;

        return EnsureWorldBuildJob(state, state.AgentTypeId);
    }

    private static bool EnsureWorldBuildJob(WorldRuntimeState state, int agentTypeId)
    {
        if (state == null)
            return false;
        if (state.BuildJob != null)
            return true;

        WorldBuildJob job = new WorldBuildJob
        {
            AgentTypeId = agentTypeId,
            Stage = WorldBuildStage.Initialize,
            Reason = _lastWorldDirtyReason
        };
        state.BuildJob = job;
        return true;
    }

    private static void ProcessWorldBuildJob(WorldRuntimeState state, long deadlineTicks, bool forceComplete)
    {
        WorldBuildJob job = state.BuildJob;
        if (job == null)
            return;

        while (job.Stage != WorldBuildStage.Complete)
        {
            switch (job.Stage)
            {
                case WorldBuildStage.Initialize:
                    if (!InitializeWorldBuildJob(job))
                    {
                        state.BuildJob = null;
                        return;
                    }
                    break;
                case WorldBuildStage.Rasterize:
                    ProcessWorldBuildRasterize(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.CreateWorldShell:
                    CreateWorldBuildShell(job);
                    break;
                case WorldBuildStage.ApplyRuntimeObstacles:
                    ProcessWorldBuildObstacles(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.InitializeSectors:
                    InitializeWorldBuildSectors(job);
                    break;
                case WorldBuildStage.BuildCellNavAnchors:
                    ProcessWorldBuildCellNavAnchors(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.NeighborMask:
                    ProcessWorldBuildNeighborMask(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.SymmetrizeNeighborMask:
                    ProcessWorldBuildSymmetrizeNeighborMask(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.CostField:
                    ProcessWorldBuildCostField(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.IslandField:
                    ProcessWorldBuildIslandField(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.PortalGraph:
                    ProcessWorldBuildPortalGraph(job, deadlineTicks, forceComplete);
                    break;
                case WorldBuildStage.Commit:
                    CommitWorldBuildJob(state, job);
                    job.Stage = WorldBuildStage.Complete;
                    break;
                default:
                    throw new InvalidOperationException($"ProcessWorldBuildJob failed: unknown stage {job.Stage}.");
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        state.BuildJob = null;
    }

    private static bool InitializeWorldBuildJob(WorldBuildJob job)
    {
        if (job == null)
            throw new InvalidOperationException("InitializeWorldBuildJob failed: job is null.");

        if (_testTerrainOverride != null)
        {
            if (_testTerrainOverride.AgentTypeId != AnyAgentTypeId && _testTerrainOverride.AgentTypeId != job.AgentTypeId)
                return false;

            job.Width = _testTerrainOverride.Width;
            job.Height = _testTerrainOverride.Height;
            job.CellSize = _testTerrainOverride.CellSize;
            job.Origin = _testTerrainOverride.Origin;
            job.BaseWalkableMask = (bool[])_testTerrainOverride.WalkableMask.Clone();
            job.CellNavAnchors = _testTerrainOverride.CellNavAnchors != null
                ? (Vector3[])_testTerrainOverride.CellNavAnchors.Clone()
                : null;
            job.RequiresNavMeshAnchors = false;
            job.Stage = WorldBuildStage.CreateWorldShell;
            return true;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.indices == null || triangulation.indices.Length == 0)
            return false;

        float agentRadius = ResolveAgentTypeRadius(job.AgentTypeId);
        job.CellSize = Config.NavigationCellSize > 0.0001f
            ? Config.NavigationCellSize
            : Mathf.Max(0.12f, agentRadius * 0.75f);

        Bounds navBounds = CalculateNavMeshBounds(triangulation);
        float padding = Mathf.Max(Config.NavigationBoundsPadding, agentRadius + job.CellSize);
        navBounds.Expand(new Vector3(padding * 2f, 0f, padding * 2f));

        job.Origin = new Vector3(navBounds.min.x, 0f, navBounds.min.z);
        job.Width = Mathf.Max(1, Mathf.CeilToInt(navBounds.size.x / job.CellSize));
        job.Height = Mathf.Max(1, Mathf.CeilToInt(navBounds.size.z / job.CellSize));
        job.BaseWalkableMask = new bool[job.Width * job.Height];
        job.CellNavAnchors = new Vector3[job.Width * job.Height];
        job.Filter = new NavMeshQueryFilter
        {
            agentTypeID = job.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        job.SampleRadius = ResolveWalkableRasterSampleRadius(job.CellSize, agentRadius);
        job.RasterCursor = 0;
        job.RequiresNavMeshAnchors = true;
        job.Stage = WorldBuildStage.Rasterize;
        return true;
    }

    private static void ProcessWorldBuildRasterize(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        int cellCount = job.Width * job.Height;
        while (job.RasterCursor < cellCount)
        {
            int index = job.RasterCursor++;
            int x = index % job.Width;
            int y = index / job.Width;
            job.BaseWalkableMask[index] = TrySampleCellWalkable(
                job.Origin,
                job.CellSize,
                x,
                y,
                job.SampleRadius,
                job.Filter,
                out job.CellNavAnchors[index]);

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.Stage = WorldBuildStage.CreateWorldShell;
    }

    private static void CreateWorldBuildShell(WorldBuildJob job)
    {
        bool[] runtimeWalkableMask = (bool[])job.BaseWalkableMask.Clone();
        job.WorkingWorld = new NavigationWorld
        {
            AgentTypeId = job.AgentTypeId,
            RequiresNavMeshAnchors = job.RequiresNavMeshAnchors,
            Width = job.Width,
            Height = job.Height,
            CellSize = job.CellSize,
            Origin = job.Origin,
            BaseWalkableMask = job.BaseWalkableMask,
            WalkableMask = runtimeWalkableMask,
            CostField = new byte[job.Width * job.Height],
            CellNavAnchors = job.CellNavAnchors != null && job.CellNavAnchors.Length == job.Width * job.Height ? job.CellNavAnchors : new Vector3[job.Width * job.Height],
            NeighborTraversalMask = new byte[job.Width * job.Height],
            IslandIds = new int[job.Width * job.Height],
            SectorSizeInCells = Config.SectorSizeInCells
        };
        job.CircleObstacles = new List<CircleObstacle>(CircleObstacles.Values);
        job.BoxObstacles = new List<BoxObstacle>(BoxObstacles.Values);
        job.CostStamps = new List<CostStamp>(CostStamps.Values);
        job.ObstacleCursor = 0;
        job.ApplyingCircleObstacles = true;
        job.Stage = WorldBuildStage.ApplyRuntimeObstacles;
    }

    private static void ProcessWorldBuildObstacles(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildObstacles failed: working world is null.");

        if (job.ApplyingCircleObstacles)
        {
            while (job.ObstacleCursor < job.CircleObstacles.Count)
            {
                CircleObstacle circle = job.CircleObstacles[job.ObstacleCursor++];
                BlockCellsByCircle(world.WalkableMask, world.Width, world.Height, world.CellSize, world.Origin, circle.Position, circle.Radius);
                if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                    return;
            }

            job.ApplyingCircleObstacles = false;
            job.ObstacleCursor = 0;
        }

        while (job.ObstacleCursor < job.BoxObstacles.Count)
        {
            BoxObstacle box = job.BoxObstacles[job.ObstacleCursor++];
            BlockCellsByBounds(world.WalkableMask, world.Width, world.Height, world.CellSize, world.Origin, new Bounds(box.Center, box.HalfExtents * 2f));
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.ObstacleCursor = 0;
        job.Stage = WorldBuildStage.InitializeSectors;
    }

    private static void InitializeWorldBuildSectors(WorldBuildJob job)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("InitializeWorldBuildSectors failed: working world is null.");

        world.SectorCountX = Mathf.CeilToInt((float)world.Width / world.SectorSizeInCells);
        world.SectorCountY = Mathf.CeilToInt((float)world.Height / world.SectorSizeInCells);
        world.Sectors = new SectorData[world.SectorCountX * world.SectorCountY];

        for (int sectorY = 0; sectorY < world.SectorCountY; sectorY++)
        {
            for (int sectorX = 0; sectorX < world.SectorCountX; sectorX++)
            {
                int sectorId = sectorY * world.SectorCountX + sectorX;
                int startX = sectorX * world.SectorSizeInCells;
                int startY = sectorY * world.SectorSizeInCells;
                int sectorWidth = Mathf.Min(world.SectorSizeInCells, world.Width - startX);
                int sectorHeight = Mathf.Min(world.SectorSizeInCells, world.Height - startY);

                world.Sectors[sectorId] = new SectorData
                {
                    SectorId = sectorId,
                    StartX = startX,
                    StartY = startY,
                    Width = sectorWidth,
                    Height = sectorHeight,
                    DirtyVersion = 1,
                    Center = world.Origin + new Vector3(
                        (startX + sectorWidth * 0.5f) * world.CellSize,
                        0f,
                        (startY + sectorHeight * 0.5f) * world.CellSize)
                };
            }
        }

        job.CellCursor = 0;
        job.Stage = WorldBuildStage.BuildCellNavAnchors;
    }

    private static void ProcessWorldBuildCellNavAnchors(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildCellNavAnchors failed: working world is null.");

        int cellCount = world.Width * world.Height;
        if (!world.RequiresNavMeshAnchors)
        {
            while (job.CellCursor < cellCount)
            {
                int index = job.CellCursor++;
                if (!world.BaseWalkableMask[index])
                    continue;

                int x = index % world.Width;
                int y = index / world.Width;
                world.CellNavAnchors[index] = world.GridToWorldCenter(x, y);
                if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                    return;
            }

            job.CellCursor = 0;
            job.Stage = WorldBuildStage.NeighborMask;
            return;
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float sampleRadius = ResolveWalkableRasterSampleRadius(world.CellSize, ResolveAgentTypeRadius(world.AgentTypeId));
        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            if (!world.BaseWalkableMask[index])
                continue;

            int x = index % world.Width;
            int y = index / world.Width;
            Vector3 center = world.GridToWorldCenter(x, y);
            if (!NavMesh.SamplePosition(center, out NavMeshHit navHit, sampleRadius, filter))
                throw new InvalidOperationException($"ProcessWorldBuildCellNavAnchors failed: walkable cell ({x},{y}) missing NavMesh anchor.");

            world.CellNavAnchors[index] = navHit.position;
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.CellCursor = 0;
        job.Stage = WorldBuildStage.NeighborMask;
    }

    private static void ProcessWorldBuildNeighborMask(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildNeighborMask failed: working world is null.");

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        int cellCount = world.Width * world.Height;
        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            RebuildNeighborTraversalMaskCell(world, index % world.Width, index / world.Width, filter);
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.CellCursor = 0;
        job.Stage = WorldBuildStage.SymmetrizeNeighborMask;
    }

    private static void ProcessWorldBuildSymmetrizeNeighborMask(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildSymmetrizeNeighborMask failed: working world is null.");
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("ProcessWorldBuildSymmetrizeNeighborMask failed: neighbor traversal mask is invalid.");

        int cellCount = world.Width * world.Height;
        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            int x = index % world.Width;
            int y = index / world.Width;
            SymmetrizeNeighborTraversalMaskCell(world, x, y, "ProcessWorldBuildSymmetrizeNeighborMask");
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.SectorCursor = 0;
        job.CellCursor = 0;
        job.Stage = WorldBuildStage.CostField;
    }

    private static void ProcessWorldBuildCostField(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildCostField failed: working world is null.");

        while (job.SectorCursor < world.Sectors.Length)
        {
            RebuildCostFieldForSector(world, job.SectorCursor++, job.CostStamps);
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.SectorCursor = 0;
        job.Stage = WorldBuildStage.IslandField;
    }

    private static void ProcessWorldBuildIslandField(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        ProcessWorldBuildIslandFieldInternal(job, deadlineTicks, forceComplete);
    }

    private static void ProcessWorldBuildIslandFieldInternal(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildIslandField failed: working world is null.");

        if (!job.IslandInitialized)
        {
            Array.Clear(world.IslandIds, 0, world.IslandIds.Length);
            world.IslandCount = 0;
            world.MainIslandId = 0;
            world.MainIslandSize = 0;
            job.IslandOpenQueue = new Queue<int>(256);
            job.IslandScanIndex = 0;
            job.IslandCurrentId = 0;
            job.IslandCurrentSize = 0;
            job.IslandMainId = 0;
            job.IslandMainSize = 0;
            job.IslandBfsActive = false;
            job.IslandInitialized = true;
        }

        AdvanceIslandFieldBuild(
            world,
            job.IslandOpenQueue,
            ref job.IslandScanIndex,
            ref job.IslandCurrentId,
            ref job.IslandCurrentSize,
            ref job.IslandMainId,
            ref job.IslandMainSize,
            ref job.IslandBfsActive,
            deadlineTicks,
            forceComplete,
            out bool complete);
        if (!complete)
            return;

        LogIslandFieldDiagnostics(world, "world-build-pending");
        job.Stage = WorldBuildStage.PortalGraph;
    }

    private static void ProcessWorldBuildPortalGraph(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (world == null)
            throw new InvalidOperationException("ProcessWorldBuildPortalGraph failed: working world is null.");

        if (!job.PortalInitialized)
        {
            world.UsedPortalIds.Clear();
            for (int i = 0; i < world.Sectors.Length; i++)
            {
                world.Sectors[i].PortalIds.Clear();
                world.Sectors[i].PortalTransitions.Clear();
            }

            world.PortalsById = new Dictionary<int, PortalData>();
            job.PortalRebuiltPortals = new List<PortalData>();
            job.PortalStage = RuntimeDirtyPortalStage.RebuildBoundaries;
            job.PortalAddCursor = 0;
            job.PortalTransitionCursor = 0;
            job.PortalTransitionFromCursor = 0;
            job.PortalInitialized = true;
        }

        while (job.PortalStage != RuntimeDirtyPortalStage.Complete)
        {
            switch (job.PortalStage)
            {
                case RuntimeDirtyPortalStage.RebuildBoundaries:
                    BuildVerticalPortals(world, job.PortalRebuiltPortals);
                    BuildHorizontalPortals(world, job.PortalRebuiltPortals);
                    job.PortalStage = RuntimeDirtyPortalStage.AddRebuiltPortals;
                    break;
                case RuntimeDirtyPortalStage.AddRebuiltPortals:
                    while (job.PortalAddCursor < job.PortalRebuiltPortals.Count)
                    {
                        PortalData portal = job.PortalRebuiltPortals[job.PortalAddCursor++];
                        if (world.PortalsById.ContainsKey(portal.PortalId))
                            throw new InvalidOperationException($"ProcessWorldBuildPortalGraph failed: duplicate portal id {portal.PortalId}.");

                        world.PortalsById.Add(portal.PortalId, portal);
                        world.Sectors[portal.SectorAId].PortalIds.Add(portal.PortalId);
                        world.Sectors[portal.SectorBId].PortalIds.Add(portal.PortalId);
                        if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                            return;
                    }

                    world.Portals = job.PortalRebuiltPortals.ToArray();
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildTransitions;
                    break;
                case RuntimeDirtyPortalStage.RebuildTransitions:
                    ProcessWorldBuildPortalTransitions(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyPortalStage.RemoveOldPortals:
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildBoundaries;
                    break;
                default:
                    throw new InvalidOperationException($"ProcessWorldBuildPortalGraph failed: unknown portal stage {job.PortalStage}.");
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        RemoveStalePortalSignatures(world);
        job.Stage = WorldBuildStage.Commit;
    }

    private static void ProcessWorldBuildPortalTransitions(WorldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        while (job.PortalTransitionCursor < world.Sectors.Length)
        {
            SectorData sector = world.Sectors[job.PortalTransitionCursor];
            if (job.PortalTransitionFromCursor == 0)
                sector.PortalTransitions.Clear();

            if (sector.PortalIds.Count <= 1)
            {
                job.PortalTransitionCursor++;
                job.PortalTransitionFromCursor = 0;
                continue;
            }

            while (job.PortalTransitionFromCursor < sector.PortalIds.Count)
            {
                if (!job.PortalTransitionIntegrationActive)
                    BeginWorldBuildPortalTransitionIntegration(job, world, sector, sector.PortalIds[job.PortalTransitionFromCursor]);

                if (!AdvancePortalTransitionIntegration(
                        world,
                        sector,
                        job.PortalTransitionIntegration,
                        job.PortalTransitionOpenSet,
                        deadlineTicks,
                        forceComplete))
                {
                    return;
                }

                int fromPortalId = job.PortalTransitionFromPortalId;
                float[] integration = job.PortalTransitionIntegration;
                for (int toIndex = 0; toIndex < sector.PortalIds.Count; toIndex++)
                {
                    int toPortalId = sector.PortalIds[toIndex];
                    if (toPortalId == fromPortalId)
                        continue;

                    Vector2Int[] toCells = GetPortalCellsForSector(GetPortalById(world, toPortalId), sector.SectorId);
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

                job.PortalTransitionFromCursor++;
                job.PortalTransitionIntegrationActive = false;
                job.PortalTransitionIntegration = null;
                job.PortalTransitionOpenSet = null;
                if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                    return;
            }

            job.PortalTransitionCursor++;
            job.PortalTransitionFromCursor = 0;
        }

        job.PortalStage = RuntimeDirtyPortalStage.Complete;
    }

    private static void BeginWorldBuildPortalTransitionIntegration(WorldBuildJob job, NavigationWorld world, SectorData sector, int fromPortalId)
    {
        PortalData portal = GetPortalById(world, fromPortalId);
        Vector2Int[] fromCells = GetPortalCellsForSector(portal, sector.SectorId);
        if (fromCells == null || fromCells.Length == 0)
            throw new InvalidOperationException($"BeginWorldBuildPortalTransitionIntegration failed: portal has no cells sector={sector.SectorId} portal={fromPortalId}.");

        job.PortalTransitionFromPortalId = fromPortalId;
        job.PortalTransitionIntegration = new float[sector.Width * sector.Height];
        InitializeIntegrationField(job.PortalTransitionIntegration);
        job.PortalTransitionOpenSet = new MinHeap();
        SeedPortalTransitionIntegration(sector, fromCells, job.PortalTransitionIntegration, job.PortalTransitionOpenSet);
        job.PortalTransitionIntegrationActive = true;
    }

    private static void CommitWorldBuildJob(WorldRuntimeState state, WorldBuildJob job)
    {
        if (job.WorkingWorld == null)
            throw new InvalidOperationException("CommitWorldBuildJob failed: working world is null.");

        _perf.WorldBuilds++;
        state.World = job.WorkingWorld;
        state.World.Version = _nextWorldVersion++;
        LogIslandFieldDiagnostics(state.World, "world-build");
        state.IsDirty = false;
        state.DirtyRuntimeObstacleSectors.Clear();
        state.RuntimeDirtyJob = null;
        FlowTileCache.Clear();
        PendingTileBuildKeys.Clear();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        SectorPathCache.Clear();
        SectorPortalAccessCache.Clear();
        SharedGoalFields.Clear();
        Bottlenecks.Clear();
        CorridorBottlenecks.Clear();
        _world = state.World;

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            int walkableCount = 0;
            bool[] walkableMask = state.World.WalkableMask;
            for (int i = 0; i < walkableMask.Length; i++)
            {
                if (walkableMask[i])
                    walkableCount++;
            }

            Debug.Log(
                $"[FlowWorld] Rebuilt worldVersion={state.World.Version} agentType={job.AgentTypeId} size={job.Width}x{job.Height} cellSize={job.CellSize:F3} " +
                $"origin={job.Origin} walkable={walkableCount}/{walkableMask.Length} dirtyReason={job.Reason}");
        }
    }

    private static WorldRuntimeState GetOrCreateWorldState(int agentTypeId)
    {
        if (!WorldStates.TryGetValue(agentTypeId, out WorldRuntimeState state))
        {
            state = new WorldRuntimeState { AgentTypeId = agentTypeId };
            WorldStates.Add(agentTypeId, state);
        }

        state.AgentTypeId = agentTypeId;
        return state;
    }

    private static bool TrySampleCellWalkable(Vector3 origin, float cellSize, int x, int y, float sampleRadius, NavMeshQueryFilter filter, out Vector3 anchorPosition)
    {
        anchorPosition = Vector3.zero;
        Vector3 center = new Vector3(origin.x + (x + 0.5f) * cellSize, origin.y, origin.z + (y + 0.5f) * cellSize);
        if (!NavMesh.SamplePosition(center, out NavMeshHit navHit, sampleRadius, filter))
            return false;

        Vector2 centerXZ = new Vector2(center.x, center.z);
        Vector2 navXZ = new Vector2(navHit.position.x, navHit.position.z);
        float maxAnchorOffset = Mathf.Max(0.08f, cellSize * 0.45f);
        if (Vector2.Distance(centerXZ, navXZ) > maxAnchorOffset)
            return false;

        anchorPosition = navHit.position;
        return true;
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
                builder.Append(",island=");
                builder.Append(world.IslandIds != null && world.IslandIds.Length == world.Width * world.Height ? world.IslandIds[index] : -1);
                builder.Append(",mask=0x");
                builder.Append(world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height
                    ? world.NeighborTraversalMask[index].ToString("X2")
                    : "NA");
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

    private static void RemovePortalsTouchingSectors(NavigationWorld world, HashSet<int> affectedSectors, HashSet<int> transitionDirtySectors)
    {
        List<int> portalIdsToRemove = null;
        foreach (KeyValuePair<int, PortalData> pair in world.PortalsById)
        {
            PortalData portal = pair.Value;
            if (!affectedSectors.Contains(portal.SectorAId) && !affectedSectors.Contains(portal.SectorBId))
                continue;

            portalIdsToRemove ??= new List<int>();
            portalIdsToRemove.Add(pair.Key);
        }

        if (portalIdsToRemove == null)
            return;

        for (int i = 0; i < portalIdsToRemove.Count; i++)
        {
            int portalId = portalIdsToRemove[i];
            if (!world.PortalsById.TryGetValue(portalId, out PortalData portal))
                continue;

            world.PortalsById.Remove(portalId);
            world.UsedPortalIds.Remove(portalId);
            RemovePortalSignatureById(world, portalId);
            world.Sectors[portal.SectorAId].PortalIds.Remove(portalId);
            world.Sectors[portal.SectorBId].PortalIds.Remove(portalId);
            transitionDirtySectors.Add(portal.SectorAId);
            transitionDirtySectors.Add(portal.SectorBId);
        }
    }

    private static void RemovePortalSignatureById(NavigationWorld world, int portalId)
    {
        PortalSignature? signatureToRemove = null;
        foreach (KeyValuePair<PortalSignature, int> pair in world.PortalIdsBySignature)
        {
            if (pair.Value != portalId)
                continue;

            signatureToRemove = pair.Key;
            break;
        }

        if (signatureToRemove.HasValue)
            world.PortalIdsBySignature.Remove(signatureToRemove.Value);
    }

    private static void RebuildPortalBoundariesForSector(
        NavigationWorld world,
        int sectorId,
        HashSet<int> affectedSectors,
        HashSet<long> processedBoundaries,
        List<PortalData> portals)
    {
        if (sectorId < 0 || sectorId >= world.Sectors.Length)
            return;

        int sectorX = sectorId % world.SectorCountX;
        int sectorY = sectorId / world.SectorCountX;
        TryRebuildPortalBoundary(world, sectorId, sectorX + 1, sectorY, affectedSectors, processedBoundaries, portals);
        TryRebuildPortalBoundary(world, sectorId, sectorX - 1, sectorY, affectedSectors, processedBoundaries, portals);
        TryRebuildPortalBoundary(world, sectorId, sectorX, sectorY + 1, affectedSectors, processedBoundaries, portals);
        TryRebuildPortalBoundary(world, sectorId, sectorX, sectorY - 1, affectedSectors, processedBoundaries, portals);
    }

    private static void TryRebuildPortalBoundary(
        NavigationWorld world,
        int sectorId,
        int neighborSectorX,
        int neighborSectorY,
        HashSet<int> affectedSectors,
        HashSet<long> processedBoundaries,
        List<PortalData> portals)
    {
        if (neighborSectorX < 0 || neighborSectorX >= world.SectorCountX || neighborSectorY < 0 || neighborSectorY >= world.SectorCountY)
            return;

        int neighborSectorId = neighborSectorY * world.SectorCountX + neighborSectorX;
        if (!affectedSectors.Contains(sectorId) && !affectedSectors.Contains(neighborSectorId))
            return;

        int minSectorId = Mathf.Min(sectorId, neighborSectorId);
        int maxSectorId = Mathf.Max(sectorId, neighborSectorId);
        long boundaryKey = ((long)minSectorId << 32) | (uint)maxSectorId;
        if (!processedBoundaries.Add(boundaryKey))
            return;

        int sectorX = sectorId % world.SectorCountX;
        int sectorY = sectorId / world.SectorCountX;
        if (neighborSectorY == sectorY)
        {
            int leftId = sectorX < neighborSectorX ? sectorId : neighborSectorId;
            int rightId = sectorX < neighborSectorX ? neighborSectorId : sectorId;
            BuildVerticalBoundaryPortals(world, portals, world.Sectors[leftId], world.Sectors[rightId]);
            return;
        }

        int topId = sectorY < neighborSectorY ? sectorId : neighborSectorId;
        int bottomId = sectorY < neighborSectorY ? neighborSectorId : sectorId;
        BuildHorizontalBoundaryPortals(world, portals, world.Sectors[topId], world.Sectors[bottomId]);
    }

    private static PortalData[] BuildPortalArray(NavigationWorld world)
    {
        PortalData[] portals = new PortalData[world.PortalsById.Count];
        int index = 0;
        foreach (PortalData portal in world.PortalsById.Values)
            portals[index++] = portal;
        return portals;
    }

    private static void ApplyRuntimeObstacleDirty(WorldRuntimeState state)
    {
        if (state == null || state.World == null)
            return;

        if (!EnsureRuntimeDirtyJob(state))
            return;

        ProcessRuntimeDirtyJob(state, long.MaxValue, forceComplete: true);
    }

    private static void DrainRuntimeDirtyJobForNavigationQuery(WorldRuntimeState state)
    {
        if (state == null || state.World == null)
            throw new InvalidOperationException("DrainRuntimeDirtyJobForNavigationQuery failed: state/world is null.");

        if (!EnsureRuntimeDirtyJob(state))
            return;

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            Debug.Log(
                $"[FlowRuntimeDirtyDrain] worldVersion={state.World.Version} dirtySectors={state.RuntimeDirtyJob.DirtySectors.Count} " +
                $"stage={state.RuntimeDirtyJob.Stage} reason={state.RuntimeDirtyJob.Reason}");
        }

        ProcessRuntimeDirtyJob(state, long.MaxValue, forceComplete: true);
        if (state.RuntimeDirtyJob != null || state.DirtyRuntimeObstacleSectors.Count > 0)
            throw new InvalidOperationException("DrainRuntimeDirtyJobForNavigationQuery failed: runtime dirty job did not complete.");
    }

    private static void InvalidateRuntimeDirtyJob(WorldRuntimeState state)
    {
        if (state == null)
            throw new InvalidOperationException("InvalidateRuntimeDirtyJob failed: state is null.");

        if (state.RuntimeDirtyJob != null)
        {
            foreach (int sectorId in state.RuntimeDirtyJob.DirtySectors)
                state.DirtyRuntimeObstacleSectors.Add(sectorId);
        }

        state.RuntimeDirtyJob = null;
    }

    private static bool EnsureRuntimeDirtyJob(WorldRuntimeState state)
    {
        if (state == null || state.World == null)
            return false;
        if (state.RuntimeDirtyJob != null)
            return true;
        if (state.DirtyRuntimeObstacleSectors.Count == 0)
            return false;

        NavigationWorld world = state.World;
        HashSet<int> dirtySectors = new HashSet<int>(state.DirtyRuntimeObstacleSectors);
        HashSet<int> costDirtySectors = ExpandDirtySectorsByCellRadius(world, dirtySectors, Mathf.CeilToInt(ResolveWallCostBlurRadiusCells(world)) + 1);
        RuntimeDirtyRebuildJob job = new RuntimeDirtyRebuildJob
        {
            TargetWorld = world,
            WorkingWorld = CloneNavigationWorldForRuntimeDirty(world),
            DirtySectors = dirtySectors,
            CostDirtySectors = costDirtySectors,
            DirtySectorIds = new List<int>(dirtySectors),
            CostDirtySectorIds = new List<int>(costDirtySectors),
            CircleObstacles = new List<CircleObstacle>(CircleObstacles.Values),
            BoxObstacles = new List<BoxObstacle>(BoxObstacles.Values),
            CostStamps = new List<CostStamp>(CostStamps.Values),
            Stage = RuntimeDirtyRebuildStage.ResetWalkable,
            Reason = _lastRuntimeObstacleDirtyReason
        };

        state.RuntimeDirtyJob = job;
        state.DirtyRuntimeObstacleSectors.Clear();
        _perf.RuntimeDirtyApplications++;
        _perf.RuntimeDirtySectorCount += dirtySectors.Count;
        Debug.Log(
            $"[FlowRuntimeDirtyQueued] worldVersion={world.Version} dirtySectors={dirtySectors.Count} costSectors={costDirtySectors.Count} " +
            $"reason={job.Reason} circleCount={job.CircleObstacles.Count} boxCount={job.BoxObstacles.Count} costStampCount={CostStamps.Count}");
        return true;
    }

    private static void ProcessRuntimeDirtyJob(WorldRuntimeState state, long deadlineTicks, bool forceComplete)
    {
        RuntimeDirtyRebuildJob job = state.RuntimeDirtyJob;
        if (job == null)
            return;

        while (job.Stage != RuntimeDirtyRebuildStage.Complete)
        {
            switch (job.Stage)
            {
                case RuntimeDirtyRebuildStage.ResetWalkable:
                    ProcessRuntimeDirtyResetWalkable(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.ApplyObstacles:
                    ProcessRuntimeDirtyObstacles(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.NeighborMask:
                    ProcessRuntimeDirtyNeighborMask(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.SymmetrizeNeighborMask:
                    SymmetrizeNeighborTraversalMaskForSectors(job.WorkingWorld, job.DirtySectors);
                    job.SectorCursor = 0;
                    job.Stage = RuntimeDirtyRebuildStage.CostField;
                    break;
                case RuntimeDirtyRebuildStage.CostField:
                    ProcessRuntimeDirtyCostField(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.IslandField:
                    ProcessRuntimeDirtyIslandField(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.PortalGraph:
                    ProcessRuntimeDirtyPortalGraph(job, deadlineTicks, forceComplete);
                    break;
                case RuntimeDirtyRebuildStage.Commit:
                    CommitRuntimeDirtyJob(state, job);
                    job.Stage = RuntimeDirtyRebuildStage.Complete;
                    break;
                default:
                    throw new InvalidOperationException($"ProcessRuntimeDirtyJob failed: unknown stage {job.Stage}.");
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        state.RuntimeDirtyJob = null;
    }

    private static void ProcessRuntimeDirtyResetWalkable(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.SectorCursor < job.DirtySectorIds.Count)
        {
            int sectorId = job.DirtySectorIds[job.SectorCursor++];
            ResetSectorWalkableFromBase(job.WorkingWorld, job.WorkingWorld.Sectors[sectorId]);
            job.WorkingWorld.Sectors[sectorId].DirtyVersion++;
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.ApplyObstacles;
    }

    private static void ProcessRuntimeDirtyObstacles(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job.ApplyingCircleObstacles)
        {
            while (job.ObstacleCursor < job.CircleObstacles.Count)
            {
                CircleObstacle circle = job.CircleObstacles[job.ObstacleCursor++];
                BlockCellsByCircleInSectors(job.WorkingWorld, job.DirtySectors, circle.Position, circle.Radius);
                if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                    return;
            }

            job.ApplyingCircleObstacles = false;
            job.ObstacleCursor = 0;
        }

        while (job.ObstacleCursor < job.BoxObstacles.Count)
        {
            BoxObstacle box = job.BoxObstacles[job.ObstacleCursor++];
            BlockCellsByBoundsInSectors(job.WorkingWorld, job.DirtySectors, new Bounds(box.Center, box.HalfExtents * 2f));
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.ObstacleCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.NeighborMask;
    }

    private static void ProcessRuntimeDirtyNeighborMask(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = job.WorkingWorld.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        while (job.SectorCursor < job.DirtySectorIds.Count)
        {
            SectorData sector = job.WorkingWorld.Sectors[job.DirtySectorIds[job.SectorCursor++]];
            for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
            {
                for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
                    RebuildNeighborTraversalMaskCell(job.WorkingWorld, x, y, filter);
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.SymmetrizeNeighborMask;
    }

    private static void ProcessRuntimeDirtyCostField(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.SectorCursor < job.CostDirtySectorIds.Count)
        {
            RebuildCostFieldForSector(job.WorkingWorld, job.CostDirtySectorIds[job.SectorCursor++], job.CostStamps);
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.SectorCursor = 0;
        job.Stage = RuntimeDirtyRebuildStage.IslandField;
    }

    private static void ProcessRuntimeDirtyIslandField(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (!job.IslandInitialized)
        {
            if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
                world.IslandIds = new int[world.Width * world.Height];

            Array.Clear(world.IslandIds, 0, world.IslandIds.Length);
            world.IslandCount = 0;
            world.MainIslandId = 0;
            world.MainIslandSize = 0;
            job.IslandOpenQueue = new Queue<int>(256);
            job.IslandScanIndex = 0;
            job.IslandCurrentId = 0;
            job.IslandCurrentSize = 0;
            job.IslandMainId = 0;
            job.IslandMainSize = 0;
            job.IslandBfsActive = false;
            job.IslandInitialized = true;
        }

        AdvanceIslandFieldBuild(
            world,
            job.IslandOpenQueue,
            ref job.IslandScanIndex,
            ref job.IslandCurrentId,
            ref job.IslandCurrentSize,
            ref job.IslandMainId,
            ref job.IslandMainSize,
            ref job.IslandBfsActive,
            deadlineTicks,
            forceComplete,
            out bool complete);
        if (!complete)
            return;

        LogIslandFieldDiagnostics(world, "runtime-dirty");
        job.Stage = RuntimeDirtyRebuildStage.PortalGraph;
    }

    private static void AdvanceIslandFieldBuild(
        NavigationWorld world,
        Queue<int> openQueue,
        ref int scanIndex,
        ref int currentId,
        ref int currentSize,
        ref int mainId,
        ref int mainSize,
        ref bool bfsActive,
        long deadlineTicks,
        bool forceComplete,
        out bool complete)
    {
        if (world == null)
            throw new InvalidOperationException("AdvanceIslandFieldBuild failed: world is null.");
        if (openQueue == null)
            throw new InvalidOperationException("AdvanceIslandFieldBuild failed: open queue is null.");

        complete = false;
        while (true)
        {
            if (bfsActive)
            {
                while (openQueue.Count > 0)
                {
                    int currentIndex = openQueue.Dequeue();
                    currentSize++;
                    int currentX = currentIndex % world.Width;
                    int currentY = currentIndex / world.Width;
                    byte traversalMask = world.NeighborTraversalMask[currentIndex];
                    for (int i = 0; i < NeighborOffsetX.Length; i++)
                    {
                        if ((traversalMask & (1 << i)) == 0)
                            continue;

                        int nextX = currentX + NeighborOffsetX[i];
                        int nextY = currentY + NeighborOffsetY[i];
                        if (!world.IsWalkable(nextX, nextY))
                            continue;

                        int nextIndex = world.GetIndex(nextX, nextY);
                        if (world.IslandIds[nextIndex] != 0)
                            continue;

                        world.IslandIds[nextIndex] = currentId;
                        openQueue.Enqueue(nextIndex);
                    }

                    if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                        return;
                }

                if (currentSize > mainSize)
                {
                    mainId = currentId;
                    mainSize = currentSize;
                }

                bfsActive = false;
                currentSize = 0;
            }

            while (scanIndex < world.Width * world.Height)
            {
                int index = scanIndex++;
                if (!world.WalkableMask[index] || world.IslandIds[index] != 0)
                    continue;

                currentId++;
                world.IslandIds[index] = currentId;
                openQueue.Enqueue(index);
                bfsActive = true;
                break;
            }

            if (bfsActive)
            {
                if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                    return;

                continue;
            }

            world.IslandCount = currentId;
            world.MainIslandId = mainId;
            world.MainIslandSize = mainSize;
            complete = true;
            return;
        }
    }

    private static void ProcessRuntimeDirtyPortalGraph(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        if (!job.PortalInitialized)
        {
            job.PortalTransitionDirtySectors = new HashSet<int>(job.DirtySectors);
            job.PortalRebuiltPortals = new List<PortalData>(32);
            job.PortalProcessedBoundaries = new HashSet<long>();
            job.PortalStage = RuntimeDirtyPortalStage.RemoveOldPortals;
            job.PortalSectorCursor = 0;
            job.PortalAddCursor = 0;
            job.PortalTransitionCursor = 0;
            job.PortalTransitionFromCursor = 0;
            job.PortalInitialized = true;
        }

        while (job.PortalStage != RuntimeDirtyPortalStage.Complete)
        {
            switch (job.PortalStage)
            {
                case RuntimeDirtyPortalStage.RemoveOldPortals:
                    RemovePortalsTouchingSectors(world, job.DirtySectors, job.PortalTransitionDirtySectors);
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildBoundaries;
                    break;
                case RuntimeDirtyPortalStage.RebuildBoundaries:
                    while (job.PortalSectorCursor < job.DirtySectorIds.Count)
                    {
                        int sectorId = job.DirtySectorIds[job.PortalSectorCursor++];
                        RebuildPortalBoundariesForSector(
                            world,
                            sectorId,
                            job.DirtySectors,
                            job.PortalProcessedBoundaries,
                            job.PortalRebuiltPortals);

                        if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                            return;
                    }

                    job.PortalStage = RuntimeDirtyPortalStage.AddRebuiltPortals;
                    break;
                case RuntimeDirtyPortalStage.AddRebuiltPortals:
                    while (job.PortalAddCursor < job.PortalRebuiltPortals.Count)
                    {
                        PortalData portal = job.PortalRebuiltPortals[job.PortalAddCursor++];
                        if (world.PortalsById.ContainsKey(portal.PortalId))
                            throw new InvalidOperationException($"ProcessRuntimeDirtyPortalGraph failed: duplicate portal id {portal.PortalId}.");

                        world.PortalsById.Add(portal.PortalId, portal);
                        world.Sectors[portal.SectorAId].PortalIds.Add(portal.PortalId);
                        world.Sectors[portal.SectorBId].PortalIds.Add(portal.PortalId);
                        job.PortalTransitionDirtySectors.Add(portal.SectorAId);
                        job.PortalTransitionDirtySectors.Add(portal.SectorBId);

                        if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                            return;
                    }

                    world.Portals = BuildPortalArray(world);
                    job.PortalTransitionSectorIds = new List<int>(job.PortalTransitionDirtySectors);
                    job.PortalTransitionCursor = 0;
                    job.PortalTransitionFromCursor = 0;
                    job.PortalStage = RuntimeDirtyPortalStage.RebuildTransitions;
                    break;
                case RuntimeDirtyPortalStage.RebuildTransitions:
                    ProcessRuntimeDirtyPortalTransitions(job, deadlineTicks, forceComplete);
                    break;
                default:
                    throw new InvalidOperationException($"ProcessRuntimeDirtyPortalGraph failed: unknown portal stage {job.PortalStage}.");
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        job.Stage = RuntimeDirtyRebuildStage.Commit;
    }

    private static void ProcessRuntimeDirtyPortalTransitions(RuntimeDirtyRebuildJob job, long deadlineTicks, bool forceComplete)
    {
        NavigationWorld world = job.WorkingWorld;
        while (job.PortalTransitionCursor < job.PortalTransitionSectorIds.Count)
        {
            int sectorId = job.PortalTransitionSectorIds[job.PortalTransitionCursor];
            if (sectorId < 0 || sectorId >= world.Sectors.Length)
            {
                job.PortalTransitionCursor++;
                job.PortalTransitionFromCursor = 0;
                continue;
            }

            SectorData sector = world.Sectors[sectorId];
            if (job.PortalTransitionFromCursor == 0)
                sector.PortalTransitions.Clear();

            if (sector.PortalIds.Count <= 1)
            {
                job.PortalTransitionCursor++;
                job.PortalTransitionFromCursor = 0;
                continue;
            }

        while (job.PortalTransitionFromCursor < sector.PortalIds.Count)
        {
            if (!job.PortalTransitionIntegrationActive)
                BeginRuntimeDirtyPortalTransitionIntegration(job, world, sector, sector.PortalIds[job.PortalTransitionFromCursor]);

                if (!AdvancePortalTransitionIntegration(
                        world,
                        sector,
                        job.PortalTransitionIntegration,
                        job.PortalTransitionOpenSet,
                        deadlineTicks,
                        forceComplete))
                    return;

                int fromPortalId = job.PortalTransitionFromPortalId;
                float[] integration = job.PortalTransitionIntegration;
                for (int toIndex = 0; toIndex < sector.PortalIds.Count; toIndex++)
                {
                    int toPortalId = sector.PortalIds[toIndex];
                    if (toPortalId == fromPortalId)
                        continue;

                    Vector2Int[] toCells = GetPortalCellsForSector(GetPortalById(world, toPortalId), sector.SectorId);
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

                job.PortalTransitionFromCursor++;
                job.PortalTransitionIntegrationActive = false;
                job.PortalTransitionIntegration = null;
                job.PortalTransitionOpenSet = null;
                if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                    return;
            }

            job.PortalTransitionCursor++;
            job.PortalTransitionFromCursor = 0;
        }

        job.PortalStage = RuntimeDirtyPortalStage.Complete;
    }

    private static void BeginRuntimeDirtyPortalTransitionIntegration(RuntimeDirtyRebuildJob job, NavigationWorld world, SectorData sector, int fromPortalId)
    {
        PortalData portal = GetPortalById(world, fromPortalId);
        Vector2Int[] fromCells = GetPortalCellsForSector(portal, sector.SectorId);
        if (fromCells == null || fromCells.Length == 0)
            throw new InvalidOperationException($"BeginRuntimeDirtyPortalTransitionIntegration failed: portal has no cells sector={sector.SectorId} portal={fromPortalId}.");

        job.PortalTransitionFromPortalId = fromPortalId;
        job.PortalTransitionIntegration = new float[sector.Width * sector.Height];
        InitializeIntegrationField(job.PortalTransitionIntegration);
        job.PortalTransitionOpenSet = new MinHeap();
        SeedPortalTransitionIntegration(sector, fromCells, job.PortalTransitionIntegration, job.PortalTransitionOpenSet);
        job.PortalTransitionIntegrationActive = true;
    }

    private static void SeedPortalTransitionIntegration(SectorData sector, Vector2Int[] fromCells, float[] integration, MinHeap openSet)
    {
        for (int i = 0; i < fromCells.Length; i++)
        {
            Vector2Int cell = fromCells[i];
            if (!IsInsideSector(sector, cell.x, cell.y))
                continue;

            int localIndex = GetSectorLocalIndex(sector, cell.x, cell.y);
            if (integration[localIndex] <= 0f)
                continue;

            integration[localIndex] = 0f;
            openSet.Push(localIndex, 0f);
        }

        if (openSet.Count == 0)
            throw new InvalidOperationException($"SeedPortalTransitionIntegration failed: no valid seeds sector={sector.SectorId}.");
    }

    private static bool AdvancePortalTransitionIntegration(
        NavigationWorld world,
        SectorData sector,
        float[] integration,
        MinHeap openSet,
        long deadlineTicks,
        bool forceComplete)
    {
        if (integration == null || openSet == null)
        {
            throw new InvalidOperationException("AdvancePortalTransitionIntegration failed: integration state is not active.");
        }

        while (openSet.Count > 0)
        {
            QueueNode node = openSet.Pop();
            if (node.Cost > integration[node.Index] + 0.0001f)
                continue;

            int localX = node.Index % sector.Width;
            int localY = node.Index / sector.Width;
            int worldX = sector.StartX + localX;
            int worldY = sector.StartY + localY;

            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextWorldX = worldX + CardinalOffsetX[i];
                int nextWorldY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(sector, nextWorldX, nextWorldY) || !world.IsWalkable(nextWorldX, nextWorldY))
                    continue;

                if (!CanTraverseNeighborCells(world, worldX, worldY, nextWorldX, nextWorldY))
                    continue;

                int nextLocalIndex = GetSectorLocalIndex(sector, nextWorldX, nextWorldY);
                float newCost = ResolveEikonalIntegrationCost(world, sector, integration, nextWorldX, nextWorldY, reverseTraversal: false);
                if (newCost >= integration[nextLocalIndex])
                    continue;

                integration[nextLocalIndex] = newCost;
                openSet.Push(nextLocalIndex, newCost);
            }

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return false;
        }

        return true;
    }

    private static void CommitRuntimeDirtyJob(WorldRuntimeState state, RuntimeDirtyRebuildJob job)
    {
        NavigationWorld target = job.TargetWorld;
        NavigationWorld working = job.WorkingWorld;
        if (state.World != target)
            throw new InvalidOperationException("CommitRuntimeDirtyJob failed: target world changed while job was pending.");

        target.WalkableMask = working.WalkableMask;
        target.CostField = working.CostField;
        target.NeighborTraversalMask = working.NeighborTraversalMask;
        target.IslandIds = working.IslandIds;
        target.IslandCount = working.IslandCount;
        target.MainIslandId = working.MainIslandId;
        target.MainIslandSize = working.MainIslandSize;
        target.Sectors = working.Sectors;
        target.Portals = working.Portals;
        target.PortalsById = working.PortalsById;
        target.NextPortalId = working.NextPortalId;
        target.PortalIdsBySignature.Clear();
        foreach (KeyValuePair<PortalSignature, int> pair in working.PortalIdsBySignature)
            target.PortalIdsBySignature.Add(pair.Key, pair.Value);
        target.UsedPortalIds.Clear();
        foreach (int portalId in working.UsedPortalIds)
            target.UsedPortalIds.Add(portalId);

        InvalidateCachesForDirtySectors(job.CostDirtySectors);
        MovingTargetAnchors.Clear();
        Bottlenecks.Clear();
        CorridorBottlenecks.Clear();
        NavigationGoalOccupancyBuckets.Clear();
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            if (PathTouchesAnySector(pair.Value.NavState.PathHandle, job.CostDirtySectors)
                || PathReferencesMissingPortal(pair.Value.NavState.PathHandle))
            {
                pair.Value.NavState.PathHandle = null;
            }

            ClearStableGoal(pair.Value);
        }

        _world = target;
        Debug.Log(
            $"[FlowRuntimeDirtyCommit] worldVersion={target.Version} dirtySectors={job.DirtySectors.Count} costSectors={job.CostDirtySectors.Count} " +
            $"reason={job.Reason}");
    }

    private static NavigationWorld CloneNavigationWorldForRuntimeDirty(NavigationWorld source)
    {
        if (source == null)
            throw new InvalidOperationException("CloneNavigationWorldForRuntimeDirty failed: source is null.");

        NavigationWorld clone = new NavigationWorld
        {
            Version = source.Version,
            AgentTypeId = source.AgentTypeId,
            RequiresNavMeshAnchors = source.RequiresNavMeshAnchors,
            Width = source.Width,
            Height = source.Height,
            CellSize = source.CellSize,
            Origin = source.Origin,
            BaseWalkableMask = source.BaseWalkableMask,
            WalkableMask = (bool[])source.WalkableMask.Clone(),
            CostField = (byte[])source.CostField.Clone(),
            CellNavAnchors = source.CellNavAnchors,
            NeighborTraversalMask = (byte[])source.NeighborTraversalMask.Clone(),
            IslandIds = (int[])source.IslandIds.Clone(),
            IslandCount = source.IslandCount,
            MainIslandId = source.MainIslandId,
            MainIslandSize = source.MainIslandSize,
            SectorSizeInCells = source.SectorSizeInCells,
            SectorCountX = source.SectorCountX,
            SectorCountY = source.SectorCountY,
            Sectors = CloneSectors(source.Sectors),
            Portals = (PortalData[])source.Portals.Clone(),
            PortalsById = new Dictionary<int, PortalData>(source.PortalsById),
            NextPortalId = source.NextPortalId
        };

        foreach (KeyValuePair<PortalSignature, int> pair in source.PortalIdsBySignature)
            clone.PortalIdsBySignature.Add(pair.Key, pair.Value);
        foreach (int portalId in source.UsedPortalIds)
            clone.UsedPortalIds.Add(portalId);

        return clone;
    }

    private static SectorData[] CloneSectors(SectorData[] source)
    {
        if (source == null)
            throw new InvalidOperationException("CloneSectors failed: source is null.");

        SectorData[] clone = new SectorData[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            SectorData sector = source[i];
            SectorData copy = new SectorData
            {
                SectorId = sector.SectorId,
                StartX = sector.StartX,
                StartY = sector.StartY,
                Width = sector.Width,
                Height = sector.Height,
                Center = sector.Center,
                DirtyVersion = sector.DirtyVersion
            };
            copy.PortalIds.AddRange(sector.PortalIds);
            copy.PortalTransitions.AddRange(sector.PortalTransitions);
            clone[i] = copy;
        }

        return clone;
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

    private static void BlockCellsByCircleInSectors(NavigationWorld world, HashSet<int> sectorIds, Vector3 center, float radius)
    {
        Bounds bounds = new Bounds(center, new Vector3(radius * 2f, 0f, radius * 2f));
        float blockRadius = radius + world.CellSize * 0.45f;
        float blockRadiusSq = blockRadius * blockRadius;
        Vector2 centerXZ = new Vector2(center.x, center.z);

        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            int rawMinX = Mathf.FloorToInt((bounds.min.x - world.Origin.x) / world.CellSize);
            int rawMaxX = Mathf.FloorToInt((bounds.max.x - world.Origin.x) / world.CellSize);
            int rawMinY = Mathf.FloorToInt((bounds.min.z - world.Origin.z) / world.CellSize);
            int rawMaxY = Mathf.FloorToInt((bounds.max.z - world.Origin.z) / world.CellSize);
            int minX = Mathf.Max(rawMinX, sector.StartX);
            int maxX = Mathf.Min(rawMaxX, sector.StartX + sector.Width - 1);
            int minY = Mathf.Max(rawMinY, sector.StartY);
            int maxY = Mathf.Min(rawMaxY, sector.StartY + sector.Height - 1);
            if (minX > maxX || minY > maxY)
                continue;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 cellCenter = new Vector2(world.Origin.x + (x + 0.5f) * world.CellSize, world.Origin.z + (y + 0.5f) * world.CellSize);
                    if ((cellCenter - centerXZ).sqrMagnitude <= blockRadiusSq)
                        world.WalkableMask[x + y * world.Width] = false;
                }
            }
        }
    }

    private static void BlockCellsByBoundsInSectors(NavigationWorld world, HashSet<int> sectorIds, Bounds bounds)
    {
        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            int rawMinX = Mathf.FloorToInt((bounds.min.x - world.Origin.x) / world.CellSize);
            int rawMaxX = Mathf.FloorToInt((bounds.max.x - world.Origin.x) / world.CellSize);
            int rawMinY = Mathf.FloorToInt((bounds.min.z - world.Origin.z) / world.CellSize);
            int rawMaxY = Mathf.FloorToInt((bounds.max.z - world.Origin.z) / world.CellSize);
            int minX = Mathf.Max(rawMinX, sector.StartX);
            int maxX = Mathf.Min(rawMaxX, sector.StartX + sector.Width - 1);
            int minY = Mathf.Max(rawMinY, sector.StartY);
            int maxY = Mathf.Min(rawMaxY, sector.StartY + sector.Height - 1);
            if (minX > maxX || minY > maxY)
                continue;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                    world.WalkableMask[x + y * world.Width] = false;
            }
        }
    }

    private static void RebuildNeighborTraversalMaskForSectors(NavigationWorld world, HashSet<int> sectorIds)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildNeighborTraversalMaskForSectors failed: world is null.");

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
            {
                for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
                    RebuildNeighborTraversalMaskCell(world, x, y, filter);
            }
        }
    }

    private static void RebuildNeighborTraversalMaskCell(NavigationWorld world, int x, int y, NavMeshQueryFilter filter)
    {
        if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return;

        int fromIndex = world.GetIndex(x, y);
        if (!world.IsWalkable(x, y))
        {
            world.NeighborTraversalMask[fromIndex] = 0;
            return;
        }

        byte mask = 0;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int toX = x + NeighborOffsetX[i];
            int toY = y + NeighborOffsetY[i];
            if (!world.IsWalkable(toX, toY))
                continue;

            bool traversable;
            if (!world.RequiresNavMeshAnchors)
            {
                traversable = true;
            }
            else
            {
                Vector3 fromAnchor = world.CellNavAnchors[fromIndex];
                Vector3 toAnchor = world.CellNavAnchors[world.GetIndex(toX, toY)];
                traversable = AreNavAnchorsMutuallyTraversable(fromAnchor, toAnchor, filter);
            }

            if (traversable)
                mask |= (byte)(1 << i);
        }

        world.NeighborTraversalMask[fromIndex] = mask;
    }

    private static void SymmetrizeNeighborTraversalMaskForSectors(NavigationWorld world, HashSet<int> sectorIds)
    {
        if (world == null)
            throw new InvalidOperationException("SymmetrizeNeighborTraversalMaskForSectors failed: world is null.");
        if (sectorIds == null || sectorIds.Count == 0)
            return;
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("SymmetrizeNeighborTraversalMaskForSectors failed: neighbor traversal mask is invalid.");

        ResolveSectorBounds(world, sectorIds, out int startX, out int startY, out int endX, out int endY);
        startX = Mathf.Max(0, startX - 1);
        startY = Mathf.Max(0, startY - 1);
        endX = Mathf.Min(world.Width - 1, endX + 1);
        endY = Mathf.Min(world.Height - 1, endY + 1);

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
                SymmetrizeNeighborTraversalMaskCell(world, x, y, "SymmetrizeNeighborTraversalMaskForSectors");
        }
    }

    private static void SymmetrizeNeighborTraversalMaskCell(NavigationWorld world, int x, int y, string caller)
    {
        int fromIndex = world.GetIndex(x, y);
        if (!world.IsWalkable(x, y))
        {
            world.NeighborTraversalMask[fromIndex] = 0;
            return;
        }

        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int toX = x + NeighborOffsetX[i];
            int toY = y + NeighborOffsetY[i];
            if (!world.IsWalkable(toX, toY))
            {
                world.NeighborTraversalMask[fromIndex] &= (byte)~(1 << i);
                continue;
            }

            int oppositeIndex = ResolveNeighborOffsetIndex(-NeighborOffsetX[i], -NeighborOffsetY[i]);
            if (oppositeIndex < 0)
                throw new InvalidOperationException($"{caller} failed: missing opposite offset for {NeighborOffsetX[i]},{NeighborOffsetY[i]}.");

            int toIndex = world.GetIndex(toX, toY);
            bool forward = (world.NeighborTraversalMask[fromIndex] & (1 << i)) != 0;
            bool backward = (world.NeighborTraversalMask[toIndex] & (1 << oppositeIndex)) != 0;
            if (!forward && !backward)
                continue;

            world.NeighborTraversalMask[fromIndex] |= (byte)(1 << i);
            world.NeighborTraversalMask[toIndex] |= (byte)(1 << oppositeIndex);
        }
    }

    private static void InvalidateCachesForDirtySectors(HashSet<int> dirtySectors)
    {
        if (dirtySectors == null || dirtySectors.Count == 0)
            return;

        List<FlowTileCacheKey> tileKeysToRemove = null;
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            if (!dirtySectors.Contains(pair.Key.SectorId))
                continue;

            tileKeysToRemove ??= new List<FlowTileCacheKey>();
            tileKeysToRemove.Add(pair.Key);
        }

        if (tileKeysToRemove != null)
        {
            for (int i = 0; i < tileKeysToRemove.Count; i++)
                FlowTileCache.Remove(tileKeysToRemove[i]);
        }

        List<SectorPathCacheKey> pathKeysToRemove = null;
        foreach (KeyValuePair<SectorPathCacheKey, SectorPathCacheEntry> pair in SectorPathCache)
        {
            if (!dirtySectors.Contains(pair.Key.StartSectorId)
                && !dirtySectors.Contains(pair.Key.GoalSectorId)
                && !SectorPathTouchesAnySector(pair.Value, dirtySectors)
                && !SectorPathReferencesMissingPortal(pair.Value))
            {
                continue;
            }

            pathKeysToRemove ??= new List<SectorPathCacheKey>();
            pathKeysToRemove.Add(pair.Key);
        }

        if (pathKeysToRemove != null)
        {
            for (int i = 0; i < pathKeysToRemove.Count; i++)
                SectorPathCache.Remove(pathKeysToRemove[i]);
        }

        List<SectorPortalAccessKey> portalAccessKeysToRemove = null;
        foreach (KeyValuePair<SectorPortalAccessKey, SectorPortalAccessEntry> pair in SectorPortalAccessCache)
        {
            if (!dirtySectors.Contains(pair.Key.SectorId)
                && TryGetPortalById(_world, pair.Key.PortalId, out _))
            {
                continue;
            }

            portalAccessKeysToRemove ??= new List<SectorPortalAccessKey>();
            portalAccessKeysToRemove.Add(pair.Key);
        }

        if (portalAccessKeysToRemove != null)
        {
            for (int i = 0; i < portalAccessKeysToRemove.Count; i++)
                SectorPortalAccessCache.Remove(portalAccessKeysToRemove[i]);
        }

        List<SharedGoalFieldKey> sharedFieldKeysToRemove = null;
        foreach (KeyValuePair<SharedGoalFieldKey, SharedGoalField> pair in SharedGoalFields)
        {
            if (!dirtySectors.Contains(pair.Key.GoalSectorId)
                && !SharedGoalFieldTouchesAnySector(pair.Value, dirtySectors)
                && !SharedGoalFieldReferencesMissingPortal(pair.Value))
            {
                continue;
            }

            sharedFieldKeysToRemove ??= new List<SharedGoalFieldKey>();
            sharedFieldKeysToRemove.Add(pair.Key);
        }

        if (sharedFieldKeysToRemove != null)
        {
            for (int i = 0; i < sharedFieldKeysToRemove.Count; i++)
                SharedGoalFields.Remove(sharedFieldKeysToRemove[i]);
        }
    }

    private static bool SharedGoalFieldTouchesAnySector(SharedGoalField field, HashSet<int> dirtySectors)
    {
        if (field == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        foreach (KeyValuePair<int, int> pair in field.NextNodeTowardGoal)
        {
            DecodePortalNode(pair.Key, out int fromSectorId, out _);
            DecodePortalNode(pair.Value, out int toSectorId, out _);
            if (dirtySectors.Contains(fromSectorId) || dirtySectors.Contains(toSectorId))
                return true;
        }

        return false;
    }

    private static bool SharedGoalFieldReferencesMissingPortal(SharedGoalField field)
    {
        if (field == null)
            return false;

        foreach (KeyValuePair<int, int> pair in field.NextNodeTowardGoal)
        {
            DecodePortalNode(pair.Key, out _, out int fromPortalId);
            DecodePortalNode(pair.Value, out _, out int toPortalId);
            if (!TryGetPortalById(_world, fromPortalId, out _) || !TryGetPortalById(_world, toPortalId, out _))
                return true;
        }

        return false;
    }

    private static bool SectorPathTouchesAnySector(SectorPathCacheEntry entry, HashSet<int> dirtySectors)
    {
        if (entry == null || entry.SectorIds == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        for (int i = 0; i < entry.SectorIds.Length; i++)
        {
            if (dirtySectors.Contains(entry.SectorIds[i]))
                return true;
        }

        return false;
    }

    private static bool SectorPathReferencesMissingPortal(SectorPathCacheEntry entry)
    {
        if (entry == null || entry.PortalIds == null)
            return false;

        for (int i = 0; i < entry.PortalIds.Length; i++)
        {
            if (!TryGetPortalById(_world, entry.PortalIds[i], out _))
                return true;
        }

        return false;
    }

    private static bool PathTouchesAnySector(PathHandle handle, HashSet<int> dirtySectors)
    {
        if (handle == null || handle.SectorIds == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        for (int i = 0; i < handle.SectorIds.Length; i++)
        {
            if (dirtySectors.Contains(handle.SectorIds[i]))
                return true;
        }

        return false;
    }

    private static bool PathReferencesMissingPortal(PathHandle handle)
    {
        if (handle == null || handle.PortalIds == null)
            return false;

        for (int i = 0; i < handle.PortalIds.Length; i++)
        {
            if (!TryGetPortalById(_world, handle.PortalIds[i], out _))
                return true;
        }

        return false;
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
                BuildVerticalBoundaryPortals(world, portals, sectorA, sectorB);
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
                BuildHorizontalBoundaryPortals(world, portals, sectorA, sectorB);
            }
        }
    }

    private static void BuildVerticalBoundaryPortals(NavigationWorld world, List<PortalData> portals, SectorData sectorA, SectorData sectorB)
    {
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

    private static void BuildHorizontalBoundaryPortals(NavigationWorld world, List<PortalData> portals, SectorData sectorA, SectorData sectorB)
    {
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

        if (cellsA.Count != cellsB.Count)
            throw new InvalidOperationException($"FlushPortalRun failed: side cell count mismatch A={cellsA.Count} B={cellsB.Count}.");

        bool runIsNarrow = IsPortalBottleneck(world, sectorAId, sectorBId, cellsA, cellsB, isVerticalBoundary);
        AddPortalSegment(world, portals, sectorAId, sectorBId, cellsA, cellsB, 0, cellsA.Count, isVerticalBoundary, runIsNarrow);
    }

    private static void AddPortalSegment(
        NavigationWorld world,
        List<PortalData> portals,
        int sectorAId,
        int sectorBId,
        List<Vector2Int> runCellsA,
        List<Vector2Int> runCellsB,
        int start,
        int count,
        bool isVerticalBoundary,
        bool runIsNarrow)
    {
        if (count <= 0)
            throw new InvalidOperationException("AddPortalSegment failed: segment count must be positive.");

        List<Vector2Int> cellsA = runCellsA.GetRange(start, count);
        List<Vector2Int> cellsB = runCellsB.GetRange(start, count);
        Vector3 center = Vector3.zero;
        for (int i = 0; i < cellsA.Count; i++)
        {
            center += world.GridToWorldCenter(cellsA[i].x, cellsA[i].y);
            center += world.GridToWorldCenter(cellsB[i].x, cellsB[i].y);
        }

        center /= cellsA.Count * 2f;

        PortalData portal = new PortalData
        {
            PortalId = ResolveStablePortalId(world, BuildPortalSignature(sectorAId, sectorBId, cellsA, isVerticalBoundary)),
            SectorAId = sectorAId,
            SectorBId = sectorBId,
            CellsA = cellsA.ToArray(),
            CellsB = cellsB.ToArray(),
            WorldCenter = center,
            WidthCells = cellsA.Count,
            IsNarrow = runIsNarrow,
            IsVerticalBoundary = isVerticalBoundary
        };
        portals.Add(portal);
    }

    private static PortalSignature BuildPortalSignature(int sectorAId, int sectorBId, List<Vector2Int> cellsA, bool isVerticalBoundary)
    {
        if (cellsA == null || cellsA.Count == 0)
            throw new InvalidOperationException("BuildPortalSignature failed: cells are empty.");

        int firstAxis = isVerticalBoundary ? cellsA[0].y : cellsA[0].x;
        int lastAxis = isVerticalBoundary ? cellsA[cellsA.Count - 1].y : cellsA[cellsA.Count - 1].x;
        int boundary = isVerticalBoundary ? cellsA[0].x : cellsA[0].y;
        return new PortalSignature(sectorAId, sectorBId, isVerticalBoundary, boundary, firstAxis, lastAxis, cellsA.Count);
    }

    private static int ResolveStablePortalId(NavigationWorld world, PortalSignature signature)
    {
        if (world.PortalIdsBySignature.TryGetValue(signature, out int existingId))
        {
            world.UsedPortalIds.Add(existingId);
            return existingId;
        }

        int id = world.NextPortalId;
        while (world.UsedPortalIds.Contains(id))
        {
            id++;
            if (id > ushort.MaxValue)
                throw new InvalidOperationException("ResolveStablePortalId failed: portal id range exhausted.");
        }

        world.NextPortalId = id + 1;
        world.PortalIdsBySignature.Add(signature, id);
        world.UsedPortalIds.Add(id);
        return id;
    }

    private static void RemoveStalePortalSignatures(NavigationWorld world)
    {
        List<PortalSignature> staleSignatures = null;
        foreach (KeyValuePair<PortalSignature, int> pair in world.PortalIdsBySignature)
        {
            if (world.UsedPortalIds.Contains(pair.Value))
                continue;

            staleSignatures ??= new List<PortalSignature>();
            staleSignatures.Add(pair.Key);
        }

        if (staleSignatures == null)
            return;

        for (int i = 0; i < staleSignatures.Count; i++)
            world.PortalIdsBySignature.Remove(staleSignatures[i]);
    }

    private static bool IsPortalBottleneck(
        NavigationWorld world,
        int sectorAId,
        int sectorBId,
        List<Vector2Int> cellsA,
        List<Vector2Int> cellsB,
        bool isVerticalBoundary)
    {
        if (cellsA == null || cellsB == null || cellsA.Count == 0 || cellsB.Count == 0)
            throw new InvalidOperationException("IsPortalBottleneck failed: portal cells are empty.");

        if (cellsA.Count <= Config.PortalNarrowWidthCells)
            return true;

        int approachWidthA = ResolvePortalApproachWidth(world, world.Sectors[sectorAId], cellsA, isVerticalBoundary, sectorAId == sectorBId ? 0 : -1);
        int approachWidthB = ResolvePortalApproachWidth(world, world.Sectors[sectorBId], cellsB, isVerticalBoundary, sectorAId == sectorBId ? 0 : 1);
        int effectiveWidth = Mathf.Min(cellsA.Count, Mathf.Min(approachWidthA, approachWidthB));
        return effectiveWidth <= Config.PortalNarrowWidthCells;
    }

    private static int ResolvePortalApproachWidth(
        NavigationWorld world,
        SectorData sector,
        List<Vector2Int> portalCells,
        bool isVerticalBoundary,
        int inwardSign)
    {
        if (world == null)
            throw new InvalidOperationException("ResolvePortalApproachWidth failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("ResolvePortalApproachWidth failed: sector is null.");
        if (portalCells == null || portalCells.Count == 0)
            throw new InvalidOperationException("ResolvePortalApproachWidth failed: portalCells are empty.");
        if (inwardSign == 0)
            return portalCells.Count;

        int minWidth = portalCells.Count;
        for (int depth = 1; depth <= PortalApproachProbeDepth; depth++)
        {
            int openCount = 0;
            for (int i = 0; i < portalCells.Count; i++)
            {
                Vector2Int previous = portalCells[i];
                bool open = true;
                for (int step = 1; step <= depth; step++)
                {
                    Vector2Int current = isVerticalBoundary
                        ? new Vector2Int(portalCells[i].x + inwardSign * step, portalCells[i].y)
                        : new Vector2Int(portalCells[i].x, portalCells[i].y + inwardSign * step);
                    if (!IsInsideSector(sector, current.x, current.y)
                        || !world.IsWalkable(current.x, current.y)
                        || !CanTraverseNeighborCells(world, previous.x, previous.y, current.x, current.y))
                    {
                        open = false;
                        break;
                    }

                    previous = current;
                }

                if (open)
                    openCount++;
            }

            minWidth = Mathf.Min(minWidth, openCount);
        }

        return minWidth;
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

    private static float[] BuildSectorIntegrationField(NavigationWorld world, SectorData sector, Vector2Int[] goalCells, bool reverseTraversal = false)
    {
        if (goalCells == null || goalCells.Length == 0)
            throw new InvalidOperationException($"BuildSectorIntegrationField failed: sector {sector.SectorId} has no goal cells.");

        IntegrationSeed[] seeds = new IntegrationSeed[goalCells.Length];
        for (int i = 0; i < goalCells.Length; i++)
            seeds[i] = new IntegrationSeed(goalCells[i], 0f);
        return BuildSectorIntegrationField(world, sector, seeds, reverseTraversal);
    }

    private static float[] BuildSectorIntegrationField(NavigationWorld world, SectorData sector, IntegrationSeed[] seeds, bool reverseTraversal)
    {
        if (seeds == null || seeds.Length == 0)
            throw new InvalidOperationException($"BuildSectorIntegrationField failed: sector {sector.SectorId} has no seeds.");

        float[] integration = new float[sector.Width * sector.Height];
        InitializeIntegrationField(integration);

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

            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextWorldX = worldX + CardinalOffsetX[i];
                int nextWorldY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(sector, nextWorldX, nextWorldY) || !world.IsWalkable(nextWorldX, nextWorldY))
                    continue;

                if (!CanTraverseNeighborCells(
                        world,
                        reverseTraversal ? nextWorldX : worldX,
                        reverseTraversal ? nextWorldY : worldY,
                        reverseTraversal ? worldX : nextWorldX,
                        reverseTraversal ? worldY : nextWorldY))
                    continue;

                int nextLocalIndex = GetSectorLocalIndex(sector, nextWorldX, nextWorldY);
                float newCost = ResolveEikonalIntegrationCost(world, sector, integration, nextWorldX, nextWorldY, reverseTraversal);
                if (newCost >= integration[nextLocalIndex])
                    continue;

                integration[nextLocalIndex] = newCost;
                OpenSet.Push(nextLocalIndex, newCost);
            }
        }

        return integration;
    }

    private static void InitializeIntegrationField(float[] integration)
    {
        if (integration == null)
            throw new InvalidOperationException("InitializeIntegrationField failed: integration is null.");

        for (int i = 0; i < integration.Length; i++)
            integration[i] = float.PositiveInfinity;
    }

    private static float ResolveEikonalIntegrationCost(
        NavigationWorld world,
        SectorData sector,
        float[] integration,
        int worldX,
        int worldY,
        bool reverseTraversal)
    {
        float horizontal = float.PositiveInfinity;
        float vertical = float.PositiveInfinity;
        for (int i = 0; i < CardinalOffsetX.Length; i++)
        {
            int neighborX = worldX + CardinalOffsetX[i];
            int neighborY = worldY + CardinalOffsetY[i];
            if (!IsInsideSector(sector, neighborX, neighborY) || !world.IsWalkable(neighborX, neighborY))
                continue;
            if (!CanUseNeighborForIntegration(world, worldX, worldY, neighborX, neighborY, reverseTraversal))
                continue;

            float neighborCost = integration[GetSectorLocalIndex(sector, neighborX, neighborY)];
            if (float.IsPositiveInfinity(neighborCost))
                continue;

            if (neighborX != worldX)
                horizontal = Mathf.Min(horizontal, neighborCost);
            else
                vertical = Mathf.Min(vertical, neighborCost);
        }

        float cellCost = ResolveCellIntegrationCost(world, worldX, worldY);
        if (float.IsPositiveInfinity(cellCost))
            return float.PositiveInfinity;

        if (float.IsPositiveInfinity(horizontal))
            return float.IsPositiveInfinity(vertical) ? float.PositiveInfinity : vertical + cellCost;
        if (float.IsPositiveInfinity(vertical))
            return horizontal + cellCost;

        float a = Mathf.Min(horizontal, vertical);
        float b = Mathf.Max(horizontal, vertical);
        if (b - a >= cellCost)
            return a + cellCost;

        float discriminant = Mathf.Max(0f, 2f * cellCost * cellCost - (b - a) * (b - a));
        return (a + b + Mathf.Sqrt(discriminant)) * 0.5f;
    }

    private static bool CanUseNeighborForIntegration(
        NavigationWorld world,
        int worldX,
        int worldY,
        int neighborX,
        int neighborY,
        bool reverseTraversal)
    {
        return CanTraverseNeighborCells(
            world,
            reverseTraversal ? worldX : neighborX,
            reverseTraversal ? worldY : neighborY,
            reverseTraversal ? neighborX : worldX,
            reverseTraversal ? neighborY : worldY);
    }

    private static float ResolveCellIntegrationCost(NavigationWorld world, int worldX, int worldY)
    {
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            return 1f;

        int cost = world.CostField[world.GetIndex(worldX, worldY)];
        return cost >= 255 ? float.PositiveInfinity : Mathf.Max(1f, cost);
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

    private static float ResolvePortalAccessCost(SectorData sector, int sectorId, int portalId, int worldX, int worldY)
    {
        if (!IsInsideSector(sector, worldX, worldY))
            return float.PositiveInfinity;

        SectorPortalAccessEntry entry = GetOrBuildSectorPortalAccess(sector, sectorId, portalId);
        if (entry == null || entry.Integration == null)
            return float.PositiveInfinity;

        return entry.Integration[GetSectorLocalIndex(sector, worldX, worldY)];
    }

    private static SectorPortalAccessEntry GetOrBuildSectorPortalAccess(SectorData sector, int sectorId, int portalId)
    {
        SectorPortalAccessKey key = new SectorPortalAccessKey(
            _world.Version,
            sectorId,
            portalId,
            sector.DirtyVersion);
        if (SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry cached)
            && cached?.Integration != null)
        {
            cached.LastUsedFrame = GetFrameCount();
            return cached;
        }

        PortalData portal = GetPortalById(_world, portalId);
        Vector2Int[] portalCells = GetPortalCellsForSector(portal, sectorId);
        if (portalCells == null || portalCells.Length == 0)
            throw new InvalidOperationException($"GetOrBuildSectorPortalAccess failed: portal has no cells sector={sectorId} portal={portalId}.");

        SectorPortalAccessEntry entry = new SectorPortalAccessEntry
        {
            Integration = BuildSectorIntegrationField(_world, sector, portalCells, reverseTraversal: true),
            LastUsedFrame = GetFrameCount()
        };
        SectorPortalAccessCache[key] = entry;
        TrimSectorPortalAccessCache();
        return entry;
    }

    private static void TrimSectorPortalAccessCache()
    {
        int limit = Mathf.Max(64, Config.FlowTileCacheLimit * 4);
        if (SectorPortalAccessCache.Count <= limit)
            return;

        SectorPortalAccessKey oldestKey = default;
        int oldestFrame = int.MaxValue;
        bool found = false;
        foreach (KeyValuePair<SectorPortalAccessKey, SectorPortalAccessEntry> pair in SectorPortalAccessCache)
        {
            int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
            if (found && frame >= oldestFrame)
                continue;

            oldestKey = pair.Key;
            oldestFrame = frame;
            found = true;
        }

        if (found)
            SectorPortalAccessCache.Remove(oldestKey);
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

    private static void LogTileReachabilityFailure(
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        TileGoalKind goalKind,
        int downstreamPortalId)
    {
        PathHandle handle = agent?.NavState.PathHandle;
        SectorData startSector = _world != null && startSectorId >= 0 && startSectorId < _world.Sectors.Length
            ? _world.Sectors[startSectorId]
            : null;
        bool startWalkable = _world != null && _world.IsWalkable(startX, startY);
        bool insideTile = tile != null && IsInsideSector(tile, startX, startY);
        string startCost = "tile=null";
        string startFlow = "tile=null";
        if (tile != null && insideTile)
        {
            int localIndex = tile.GetLocalIndex(startX, startY);
            float cost = tile.Integration[localIndex];
            startCost = float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3");
            startFlow = tile.FlowDirections[localIndex].ToString();
        }

        Debug.LogError(
            $"[FlowTileReachabilityFail] agent={agent?.CharacterKey ?? "null"} start=({startX},{startY}) goal=({goalX},{goalY}) " +
            $"startSector={startSectorId} goalSector={goalSectorId} startWalkable={startWalkable} insideTile={insideTile} " +
            $"startCost={startCost} startFlow={startFlow} goalKind={goalKind} downstreamPortal={downstreamPortalId} " +
            $"sectorDirty={(startSector != null ? startSector.DirtyVersion : -1)} tileKey={(tile != null ? FormatTileKey(tile.Key) : "null")} " +
            $"tileRect={(tile != null ? $"({tile.StartX},{tile.StartY},{tile.Width},{tile.Height})" : "null")} " +
            $"tileGoals={(tile != null ? FormatGoalCells(tile.GoalCells) : "null")} goalCosts={(tile != null ? FormatIntegrationCosts(tile, tile.GoalCells) : "null")} " +
            $"tileSummary={DescribeTileIntegrationSummary(tile)} handle={FormatPathHandle(handle)}");
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

    private static bool EnsurePathHandle(AgentRuntimeData agent, int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        PathHandle handle = agent.NavState.PathHandle;
        if (handle != null
            && handle.WorldVersion == _world.Version
            && handle.SectorIds != null
            && handle.SectorIds.Length > 0
            && !PathReferencesMissingPortal(handle)
            && FindSectorIndex(handle, startSectorId, 0) >= 0
            && handle.SectorIds[handle.SectorIds.Length - 1] == goalSectorId)
        {
            if (!handle.MatchesGoal(goalX, goalY))
            {
                handle.HandleId = _nextPathHandleId++;
                handle.GoalX = goalX;
                handle.GoalY = goalY;
                _perf.PathBuildGoalCellMismatch++;
                if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowPathGoalCellUpdate] agent={agent.CharacterKey} startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(handle)}");
                }
            }

            return true;
        }

        PathHandle oldHandle = handle;
        string rebuildReason = ResolvePathHandleRebuildReason(oldHandle, startSectorId, goalSectorId, goalX, goalY);
        IncrementPathHandleRebuildReason(rebuildReason);
        handle = BuildPathHandle(startSectorId, goalSectorId, startX, startY, goalX, goalY);
        agent.NavState.PathHandle = handle;
        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathRebuild] agent={agent.CharacterKey} reason={rebuildReason} startSector={startSectorId} goalSector={goalSectorId} " +
                $"start=({startX},{startY}) goal=({goalX},{goalY}) oldHandle={FormatPathHandle(oldHandle)} result={(handle != null ? "ok" : "null")} newHandle={FormatPathHandle(handle)}");
        }
        return handle != null;
    }

    private static string ResolvePathHandleRebuildReason(PathHandle handle, int startSectorId, int goalSectorId, int goalX, int goalY)
    {
        if (handle == null)
            return "noHandle";
        if (handle.WorldVersion != _world.Version)
            return "worldMismatch";
        if (handle.SectorIds == null || handle.SectorIds.Length == 0)
            return "invalid";
        if (PathReferencesMissingPortal(handle))
            return "invalid";
        if (FindSectorIndex(handle, startSectorId, 0) < 0)
            return "startSectorMismatch";
        if (handle.SectorIds[handle.SectorIds.Length - 1] != goalSectorId)
            return "goalSectorMismatch";
        if (!handle.MatchesGoal(goalX, goalY))
            return "goalCellMismatch";
        return "invalid";
    }

    private static string BuildPathHandleFailure(IEntityContext self, Vector3 stableGoalPosition, int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        if (_world != null
            && _world.IslandIds != null
            && _world.IslandIds.Length == _world.Width * _world.Height
            && !AreCellsOnSameIsland(_world, startX, startY, goalX, goalY))
        {
            return BuildIslandMismatchFailure(self, stableGoalPosition, startX, startY, goalX, goalY);
        }

        return $"path handle build failed startSector={startSectorId} goalSector={goalSectorId}";
    }

    private static int DecodePortalSector(int node)
    {
        DecodePortalNode(node, out int sectorId, out _);
        return sectorId;
    }

    private static SharedGoalField GetOrBuildSharedGoalField(int goalSectorId, int goalX, int goalY, int agentTypeId)
    {
        SectorData goalSector = _world.Sectors[goalSectorId];
        SharedGoalFieldKey key = new SharedGoalFieldKey(
            _world.Version,
            agentTypeId,
            goalSectorId,
            _world.GetIndex(goalX, goalY),
            goalSector.DirtyVersion);

        if (SharedGoalFields.TryGetValue(key, out SharedGoalField cached)
            && !SharedGoalFieldReferencesMissingPortal(cached))
        {
            cached.LastUsedFrame = GetFrameCount();
            _perf.SectorPathCacheHits++;
            return cached;
        }

        _perf.SectorPathSearches++;
        float[] goalIntegration = BuildSectorIntegrationField(_world, goalSector, new[] { new Vector2Int(goalX, goalY) }, reverseTraversal: true);
        SharedGoalField field = BuildSharedGoalField(key, goalSectorId, goalX, goalY, goalIntegration);
        if (field == null)
            return null;

        SharedGoalFields[key] = field;
        TrimSharedGoalFields();
        return field;
    }

    private static SharedGoalField BuildSharedGoalField(SharedGoalFieldKey key, int goalSectorId, int goalX, int goalY, float[] goalIntegration)
    {
        SectorData goalSector = _world.Sectors[goalSectorId];
        SharedGoalField field = new SharedGoalField
        {
            Key = key,
            GoalSectorId = goalSectorId,
            GoalX = goalX,
            GoalY = goalY,
            LastUsedFrame = GetFrameCount()
        };

        OpenSet.Clear();
        for (int i = 0; i < goalSector.PortalIds.Count; i++)
        {
            int portalId = goalSector.PortalIds[i];
            float goalCost = ResolveMinimumIntegrationCost(
                goalSector,
                goalIntegration,
                GetPortalCellsForSector(GetPortalById(_world, portalId), goalSectorId));
            if (float.IsPositiveInfinity(goalCost))
                continue;

            int goalNode = EncodePortalNode(goalSectorId, portalId);
            field.NodeCosts[goalNode] = goalCost;
            OpenSet.Push(goalNode, goalCost);
        }

        if (OpenSet.Count == 0)
            return null;

        while (OpenSet.Count > 0)
        {
            QueueNode node = OpenSet.Pop();
            int currentNode = node.Index;
            if (!field.NodeCosts.TryGetValue(currentNode, out float currentCost) || node.Cost > currentCost + 0.001f)
                continue;

            DecodePortalNode(currentNode, out int currentSectorId, out int currentPortalId);
            PortalData currentPortal = GetPortalById(_world, currentPortalId);
            int oppositeSectorId = GetOppositeSectorId(currentPortal, currentSectorId);
            int oppositeNode = EncodePortalNode(oppositeSectorId, currentPortalId);
            AddSharedGoalReverseEdge(field, oppositeNode, currentNode, currentCost + 1f);

            SectorData sector = _world.Sectors[currentSectorId];
            for (int i = 0; i < sector.PortalTransitions.Count; i++)
            {
                PortalTransition transition = sector.PortalTransitions[i];
                if (transition.ToPortalId != currentPortalId)
                    continue;

                int predecessorNode = EncodePortalNode(currentSectorId, transition.FromPortalId);
                AddSharedGoalReverseEdge(field, predecessorNode, currentNode, currentCost + transition.Cost);
            }
        }

        return field;
    }

    private static void AddSharedGoalReverseEdge(SharedGoalField field, int predecessorNode, int nextNodeTowardGoal, float cost)
    {
        if (field.NodeCosts.TryGetValue(predecessorNode, out float existingCost) && cost >= existingCost)
            return;

        field.NodeCosts[predecessorNode] = cost;
        field.NextNodeTowardGoal[predecessorNode] = nextNodeTowardGoal;
        OpenSet.Push(predecessorNode, cost);
    }

    private static void TrimSharedGoalFields()
    {
        int limit = Mathf.Max(16, Config.FlowTileCacheLimit / 4);
        if (SharedGoalFields.Count <= limit)
            return;

        SharedGoalFieldKey oldestKey = default;
        int oldestFrame = int.MaxValue;
        bool found = false;
        foreach (KeyValuePair<SharedGoalFieldKey, SharedGoalField> pair in SharedGoalFields)
        {
            int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
            if (found && frame >= oldestFrame)
                continue;

            oldestKey = pair.Key;
            oldestFrame = frame;
            found = true;
        }

        if (found)
            SharedGoalFields.Remove(oldestKey);
    }

    private static void IncrementPathHandleRebuildReason(string reason)
    {
        switch (reason)
        {
            case "noHandle":
                _perf.PathBuildNoHandle++;
                break;
            case "worldMismatch":
                _perf.PathBuildWorldMismatch++;
                break;
            case "goalSectorMismatch":
                _perf.PathBuildGoalSectorMismatch++;
                break;
            case "goalCellMismatch":
                _perf.PathBuildGoalCellMismatch++;
                break;
            case "startSectorMismatch":
                _perf.PathBuildInvalidHandle++;
                break;
            default:
                _perf.PathBuildInvalidHandle++;
                break;
        }
    }

    private static PathHandle BuildPathHandle(int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        _perf.PathBuilds++;
        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];

        if (startSectorId == goalSectorId)
        {
            float[] sectorGoalIntegration = BuildSectorIntegrationField(_world, startSector, new[] { new Vector2Int(goalX, goalY) }, reverseTraversal: true);
            if (!float.IsPositiveInfinity(sectorGoalIntegration[GetSectorLocalIndex(startSector, startX, startY)]))
            {
                PathHandle sameSectorHandle = new PathHandle
                {
                    HandleId = _nextPathHandleId++,
                    WorldVersion = _world.Version,
                    GoalX = goalX,
                    GoalY = goalY,
                    SectorIds = new[] { startSectorId },
                    PortalIds = Array.Empty<int>(),
                    CurrentSectorIndex = 0
                };
                if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowPathHandleBuild] result=ok sameSector=true startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(sameSectorHandle)}");
                }

                return sameSectorHandle;
            }

            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowPathHandleBuild] sameSectorLocalBlocked=true startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}); trying portal route");
            }
        }

        SectorPathCacheKey sectorPathKey = CreateSectorPathCacheKey(startSectorId, startX, startY, goalSectorId, goalX, goalY);
        if (TryCreatePathHandleFromSectorPathCache(sectorPathKey, startSector, startSectorId, goalSectorId, startX, startY, goalX, goalY, out PathHandle cachedHandle))
            return cachedHandle;

        SharedGoalField sharedField = GetOrBuildSharedGoalField(goalSectorId, goalX, goalY, ResolvePreferredAgentTypeId(_world.AgentTypeId));
        if (sharedField == null)
            return null;

        int bestStartNode = int.MinValue;
        float bestStartCost = float.PositiveInfinity;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            if (!sharedField.NodeCosts.TryGetValue(startNode, out float downstreamCost))
                continue;

            float startCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            if (float.IsPositiveInfinity(startCost))
                continue;

            float totalCost = startCost + downstreamCost;
            if (totalCost >= bestStartCost)
                continue;

            bestStartCost = totalCost;
            bestStartNode = startNode;
        }

        if (bestStartNode == int.MinValue)
            return null;

        List<int> sectorIds = new List<int>(8) { startSectorId };
        List<int> portalIds = new List<int>(8);
        int cursor = bestStartNode;
        int guard = 0;
        while (sharedField.NextNodeTowardGoal.TryGetValue(cursor, out int nextNode))
        {
            DecodePortalNode(cursor, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nextNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId)
            {
                portalIds.Add(fromPortalId);
                sectorIds.Add(toSectorId);
            }

            cursor = nextNode;
            guard++;
            if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("BuildPathHandle failed: shared goal field path reconstruction exceeded guard.");
        }

        int endSectorId = DecodePortalSector(cursor);
        if (portalIds.Count == 0 || sectorIds[sectorIds.Count - 1] != goalSectorId)
        {
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowPathHandleBuild] result=null sameSector=false startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) bestStartNode={bestStartNode} bestStartCost={bestStartCost:F3} endSector={endSectorId} " +
                    $"sharedNodes={sharedField.NodeCosts.Count} sectorIdsPartial=[{string.Join(",", sectorIds)}] portalIdsPartial=[{string.Join(",", portalIds)}]");
            }
            return null;
        }

        PathHandle handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = sectorIds.ToArray(),
            PortalIds = portalIds.ToArray(),
            CurrentSectorIndex = 0
        };
        SectorPathCache[sectorPathKey] = new SectorPathCacheEntry
        {
            SectorIds = (int[])handle.SectorIds.Clone(),
            PortalIds = (int[])handle.PortalIds.Clone(),
            LastUsedFrame = GetFrameCount()
        };
        TrimSectorPathCache();

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathHandleBuild] result=ok sameSector=false startSector={startSectorId} goalSector={goalSectorId} " +
                $"start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(handle)}");
        }

        return handle;
    }

    private static void TrimSectorPathCache()
    {
        int limit = Mathf.Max(32, Config.FlowTileCacheLimit * 2);
        if (SectorPathCache.Count <= limit)
            return;

        SectorPathCacheKey oldestKey = default;
        int oldestFrame = int.MaxValue;
        bool found = false;
        foreach (KeyValuePair<SectorPathCacheKey, SectorPathCacheEntry> pair in SectorPathCache)
        {
            int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
            if (found && frame >= oldestFrame)
                continue;

            oldestKey = pair.Key;
            oldestFrame = frame;
            found = true;
        }

        if (found)
            SectorPathCache.Remove(oldestKey);
    }

    private static SectorPathCacheKey CreateSectorPathCacheKey(int startSectorId, int startX, int startY, int goalSectorId, int goalX, int goalY)
    {
        SectorData startSector = _world.Sectors[startSectorId];
        SectorData goalSector = _world.Sectors[goalSectorId];
        return new SectorPathCacheKey(
            _world.Version,
            startSectorId,
            _world.GetIndex(startX, startY),
            goalSectorId,
            _world.GetIndex(goalX, goalY),
            startSector.DirtyVersion,
            goalSector.DirtyVersion);
    }

    private static bool TryCreatePathHandleFromSectorPathCache(
        SectorPathCacheKey key,
        SectorData startSector,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        handle = null;
        if (!SectorPathCache.TryGetValue(key, out SectorPathCacheEntry cached)
            || cached.SectorIds == null
            || cached.PortalIds == null
            || cached.SectorIds.Length == 0
            || cached.PortalIds.Length == 0)
        {
            return false;
        }

        if (cached.SectorIds[0] != startSectorId || cached.SectorIds[cached.SectorIds.Length - 1] != goalSectorId)
        {
            SectorPathCache.Remove(key);
            return false;
        }

        int firstPortalId = cached.PortalIds[0];
        float startCost = ResolvePortalAccessCost(startSector, startSectorId, firstPortalId, startX, startY);
        if (float.IsPositiveInfinity(startCost))
            return false;

        cached.LastUsedFrame = GetFrameCount();
        _perf.SectorPathCacheHits++;
        handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = (int[])cached.SectorIds.Clone(),
            PortalIds = (int[])cached.PortalIds.Clone(),
            CurrentSectorIndex = 0
        };

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathCacheHit] startSector={startSectorId} goalSector={goalSectorId} start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(handle)}");
        }

        return true;
    }

    private static bool TryAdvancePathToCurrentSector(AgentRuntimeData agent, int currentSectorId)
    {
        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return false;

        int startIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        for (int i = startIndex; i < handle.SectorIds.Length; i++)
        {
            if (handle.SectorIds[i] != currentSectorId)
                continue;

            handle.CurrentSectorIndex = i;
            return true;
        }

        for (int i = startIndex - 1; i >= 0; i--)
        {
            if (handle.SectorIds[i] != currentSectorId)
                continue;

            handle.CurrentSectorIndex = i;
            return true;
        }

        return false;
    }

    private static void LogPathAdvanceFailure(
        AgentRuntimeData agent,
        int currentSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY)
    {
        PathHandle handle = agent?.NavState.PathHandle;
        int firstIndex = FindSectorIndex(handle, currentSectorId, 0);
        int forwardIndex = handle != null ? FindSectorIndex(handle, currentSectorId, Mathf.Max(0, handle.CurrentSectorIndex)) : -1;
        int currentIndexSector = handle != null
                                 && handle.SectorIds != null
                                 && handle.CurrentSectorIndex >= 0
                                 && handle.CurrentSectorIndex < handle.SectorIds.Length
            ? handle.SectorIds[handle.CurrentSectorIndex]
            : -1;

        Debug.Log(
            $"[FlowPathAdvanceFail] agent={agent?.CharacterKey ?? "null"} currentSector={currentSectorId} goalSector={goalSectorId} " +
            $"start=({startX},{startY}) goal=({goalX},{goalY}) currentIndexSector={currentIndexSector} " +
            $"firstIndex={firstIndex} forwardIndex={forwardIndex} handle={FormatPathHandle(handle)}");
    }

    private static int FindSectorIndex(PathHandle handle, int sectorId, int startIndex)
    {
        if (handle == null || handle.SectorIds == null)
            return -1;

        for (int i = Mathf.Max(0, startIndex); i < handle.SectorIds.Length; i++)
        {
            if (handle.SectorIds[i] == sectorId)
                return i;
        }

        return -1;
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
            _perf.TileCacheHits++;
            tile.LastUsedFrame = GetFrameCount();
            return true;
        }

        _perf.TileCacheMisses++;
        EnqueueFlowTileBuildChain(handle, sectorPathIndex, goalX, goalY, agent.AgentTypeId);
        ProcessFlowTileBuildQueue(long.MaxValue, forceComplete: true, requiredKey: key);
        if (!FlowTileCache.TryGetValue(key, out tile))
            return false;

        tile.LastUsedFrame = GetFrameCount();
        agent.NavState.CurrentTileKeyHash = key.GetHashCode();
        return true;
    }

    private static bool TryBuildOrGetTileWithStrictRepath(
        AgentRuntimeData agent,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out FlowTileCacheEntry tile,
        out TileGoalKind goalKind,
        out int downstreamPortalId,
        out string failureReason)
    {
        tile = null;
        goalKind = TileGoalKind.FinalGoal;
        downstreamPortalId = -1;
        failureReason = $"tile build failed sector={startSectorId} goal=({goalX},{goalY})";

        try
        {
            return TryBuildOrGetTile(agent, goalX, goalY, out tile, out goalKind, out downstreamPortalId);
        }
        catch (StrictPortalWindowUnreachableException firstException)
        {
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowStrictRepath] agent={agent.CharacterKey} stage=begin startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) oldHandle={FormatPathHandle(agent.NavState.PathHandle)} " +
                    $"reason={firstException.Message}");
            }

            agent.NavState.PathHandle = null;
            if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY))
            {
                failureReason =
                    $"strict repath failed after unreachable portal window startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) root={firstException.Message}";
                return false;
            }

            try
            {
                bool recovered = TryBuildOrGetTile(agent, goalX, goalY, out tile, out goalKind, out downstreamPortalId);
                if (recovered && GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowStrictRepath] agent={agent.CharacterKey} stage=recovered startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) goal=({goalX},{goalY}) newHandle={FormatPathHandle(agent.NavState.PathHandle)}");
                }

                if (!recovered)
                    failureReason = $"tile build failed after strict repath sector={startSectorId} goal=({goalX},{goalY})";
                return recovered;
            }
            catch (StrictPortalWindowUnreachableException secondException)
            {
                failureReason =
                    $"strict repath exhausted: downstream portal window remains unreachable after rebuild startSector={startSectorId} " +
                    $"goalSector={goalSectorId} start=({startX},{startY}) goal=({goalX},{goalY}) " +
                    $"newHandle={FormatPathHandle(agent.NavState.PathHandle)} root={secondException.Message}";
                return false;
            }
        }
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
            ResolveFinalGoalCacheIndex(handle, sectorPathIndex, goalX, goalY),
            agentTypeId,
            _world.Sectors[sectorId].DirtyVersion);
    }

    private static int ResolveFinalGoalCacheIndex(PathHandle handle, int sectorPathIndex, int goalX, int goalY)
    {
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            throw new InvalidOperationException("ResolveFinalGoalCacheIndex failed: handle is invalid.");

        int exactGoalIndex = _world.GetIndex(goalX, goalY);
        if (sectorPathIndex >= handle.SectorIds.Length - 2)
            return exactGoalIndex;

        return ~handle.SectorIds[handle.SectorIds.Length - 1];
    }

    private static int ResolveDownstreamGoalHint(PathHandle handle, int sectorPathIndex, int goalX, int goalY)
    {
        int nextSectorIndex = sectorPathIndex + 1;
        if (nextSectorIndex >= handle.SectorIds.Length - 1)
            return ~_world.GetIndex(goalX, goalY);

        int nextNextSectorIndex = nextSectorIndex + 1;
        if (nextNextSectorIndex >= handle.SectorIds.Length - 1)
            return ~handle.SectorIds[handle.SectorIds.Length - 1];

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
            _perf.TileCacheHits++;
            tile.LastUsedFrame = GetFrameCount();
            return true;
        }

        _perf.TileCacheMisses++;
        EnqueueFlowTileBuildChain(handle, sectorPathIndex, goalX, goalY, agentTypeId);
        ProcessFlowTileBuildQueue(long.MaxValue, forceComplete: true, requiredKey: key);
        if (!FlowTileCache.TryGetValue(key, out tile))
            return false;

        tile.LastUsedFrame = GetFrameCount();
        return true;
    }

    private static void EnqueueFlowTileBuildChain(
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        bool prependToFront = false)
    {
        if (handle == null)
            throw new InvalidOperationException("EnqueueFlowTileBuildChain failed: handle is null.");
        if (handle.SectorIds == null || handle.SectorIds.Length == 0)
            throw new InvalidOperationException("EnqueueFlowTileBuildChain failed: handle sector path is invalid.");
        if (sectorPathIndex < 0 || sectorPathIndex >= handle.SectorIds.Length)
            throw new InvalidOperationException($"EnqueueFlowTileBuildChain failed: sectorPathIndex out of range {sectorPathIndex}.");

        PathHandle snapshot = ClonePathHandle(handle);
        if (prependToFront)
        {
            for (int i = sectorPathIndex; i < snapshot.SectorIds.Length; i++)
                EnqueueFlowTileBuildJob(snapshot, i, goalX, goalY, agentTypeId, prependToFront: true);
            return;
        }

        for (int i = snapshot.SectorIds.Length - 1; i >= sectorPathIndex; i--)
            EnqueueFlowTileBuildJob(snapshot, i, goalX, goalY, agentTypeId, prependToFront: false);
    }

    private static void EnqueueFlowTileBuildJob(PathHandle snapshot, int sectorPathIndex, int goalX, int goalY, int agentTypeId, bool prependToFront)
    {
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(snapshot, sectorPathIndex, goalX, goalY, agentTypeId, out _, out _);
        if (FlowTileCache.ContainsKey(key))
            return;

        FlowTileBuildKey buildKey = new FlowTileBuildKey(key, snapshot.HandleId, sectorPathIndex, goalX, goalY, agentTypeId);
        if (!PendingFlowTileBuildJobs.Add(buildKey))
            return;

        PendingTileBuildKeys.Add(key);
        FlowTileBuildJob job = new FlowTileBuildJob
        {
            BuildKey = buildKey,
            HandleSnapshot = snapshot
        };
        if (prependToFront)
            FlowTileBuildQueue.AddFirst(job);
        else
            FlowTileBuildQueue.AddLast(job);
    }

    private static PathHandle ClonePathHandle(PathHandle handle)
    {
        if (handle == null)
            throw new InvalidOperationException("ClonePathHandle failed: handle is null.");

        return new PathHandle
        {
            HandleId = handle.HandleId,
            WorldVersion = handle.WorldVersion,
            GoalX = handle.GoalX,
            GoalY = handle.GoalY,
            SectorIds = handle.SectorIds != null ? (int[])handle.SectorIds.Clone() : null,
            PortalIds = handle.PortalIds != null ? (int[])handle.PortalIds.Clone() : null,
            CurrentSectorIndex = handle.CurrentSectorIndex
        };
    }

    private static bool TryResolveDownstreamTileForTileBuild(
        FlowTileCacheKey key,
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        out FlowTileCacheEntry downstreamTile)
    {
        downstreamTile = null;
        if (key.GoalKind != TileGoalKind.Portal)
            return true;
        if (handle == null)
            throw new InvalidOperationException($"TryResolveDownstreamTileForTileBuild failed: portal tile missing handle sector={key.SectorId}.");

        int nextSectorIndex = sectorPathIndex + 1;
        if (nextSectorIndex >= handle.SectorIds.Length)
            throw new InvalidOperationException($"TryResolveDownstreamTileForTileBuild failed: portal tile has no downstream sector currentSector={key.SectorId} goalId={key.GoalId}.");

        FlowTileCacheKey downstreamKey = CreateTileCacheKeyForPathSegment(handle, nextSectorIndex, goalX, goalY, agentTypeId, out _, out _);
        if (!FlowTileCache.TryGetValue(downstreamKey, out downstreamTile))
        {
            EnqueueFlowTileBuildChain(handle, nextSectorIndex, goalX, goalY, agentTypeId, prependToFront: true);
            return false;
        }

        downstreamTile.LastUsedFrame = GetFrameCount();
        return true;
    }

    private static IntegrationSeed[] BuildIntegrationSeedsForTile(
        FlowTileCacheKey key,
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        Vector2Int[] goalCells,
        int agentTypeId,
        FlowTileCacheEntry downstreamTile)
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

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowSeedBuildBegin] key={FormatTileKey(key)} sectorPathIndex={sectorPathIndex} nextSectorIndex={nextSectorIndex} " +
                $"goal=({goalX},{goalY}) goalCells={FormatGoalCells(goalCells)} handle={FormatPathHandle(handle)}");
        }

        PortalData portal = GetPortalById(_world, key.GoalId);
        int downstreamSectorId = handle.SectorIds[nextSectorIndex];
        Vector2Int[] downstreamCells = GetPortalCellsForSector(portal, downstreamSectorId);
        if (goalCells.Length != downstreamCells.Length)
            throw new InvalidOperationException(
                $"BuildIntegrationSeedsForTile failed: portal {portal.PortalId} side cell count mismatch current={goalCells.Length} downstream={downstreamCells.Length}.");

        bool downstreamIsFinalSector = nextSectorIndex >= handle.SectorIds.Length - 1;
        int nextPortalId = downstreamIsFinalSector ? -1 : handle.PortalIds[nextSectorIndex];
        SectorData downstreamSector = _world.Sectors[downstreamSectorId];
        bool seedCostUsesDownstreamTile = downstreamIsFinalSector;
        if (downstreamIsFinalSector && downstreamTile == null)
        {
            throw new InvalidOperationException(
                $"BuildIntegrationSeedsForTile failed: downstream final tile unavailable currentSector={key.SectorId} nextSector={downstreamSectorId}. " +
                $"upstreamKey={FormatTileKey(key)} handle={FormatPathHandle(handle)} sectorPathIndex={sectorPathIndex} nextSectorIndex={nextSectorIndex}.");
        }

        List<IntegrationSeed> seeds = new List<IntegrationSeed>(goalCells.Length);
        int skippedUnreachableCount = 0;
        for (int i = 0; i < goalCells.Length; i++)
        {
            Vector2Int downstreamCell = downstreamCells[i];
            float downstreamCost = downstreamIsFinalSector
                ? downstreamTile.Integration[downstreamTile.GetLocalIndex(downstreamCell.x, downstreamCell.y)]
                : ResolvePortalAccessCost(downstreamSector, downstreamSectorId, nextPortalId, downstreamCell.x, downstreamCell.y);
            if (float.IsPositiveInfinity(downstreamCost))
            {
                skippedUnreachableCount++;
                if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowSeedBuildSkip] key={FormatTileKey(key)} downstreamKey={(seedCostUsesDownstreamTile ? FormatTileKey(downstreamTile.Key) : "nextPortal:" + nextPortalId)} " +
                        $"portal={FormatPortal(portal)} upstreamCell={goalCells[i]} downstreamCell={downstreamCell} " +
                        $"downstreamCost=INF downstreamPortalCostMap={(seedCostUsesDownstreamTile ? FormatIntegrationCosts(downstreamTile, downstreamCells) : FormatSectorPortalAccessCosts(downstreamSector, downstreamSectorId, nextPortalId, downstreamCells))}");
                }
                continue;
            }

            seeds.Add(new IntegrationSeed(goalCells[i], downstreamCost + 1f));
        }

        if (seeds.Count == 0)
        {
            throw new StrictPortalWindowUnreachableException(
                $"BuildIntegrationSeedsForTile failed: no reachable downstream portal cells portal={portal.PortalId} nextSector={downstreamSectorId} nextPortal={nextPortalId}. " +
                $"upstreamKey={FormatTileKey(key)} downstreamKey={(seedCostUsesDownstreamTile ? FormatTileKey(downstreamTile.Key) : "nextPortal:" + nextPortalId)} " +
                $"handle={FormatPathHandle(handle)} sectorPathIndex={sectorPathIndex} nextSectorIndex={nextSectorIndex} " +
                $"portal={FormatPortal(portal)} upstreamCells={FormatGoalCells(goalCells)} downstreamCells={FormatGoalCells(downstreamCells)} " +
                $"downstreamPortalCosts={(seedCostUsesDownstreamTile ? FormatIntegrationCosts(downstreamTile, downstreamCells) : FormatSectorPortalAccessCosts(downstreamSector, downstreamSectorId, nextPortalId, downstreamCells))} " +
                $"downstreamGoalCells={(seedCostUsesDownstreamTile ? FormatGoalCells(downstreamTile.GoalCells) : "nextPortal:" + nextPortalId)} " +
                $"downstreamGoalCosts={(seedCostUsesDownstreamTile ? FormatIntegrationCosts(downstreamTile, downstreamTile.GoalCells) : FormatSectorPortalAccessCosts(downstreamSector, downstreamSectorId, nextPortalId, GetPortalCellsForSector(GetPortalById(_world, nextPortalId), downstreamSectorId)))} " +
                $"downstreamTileSummary={(seedCostUsesDownstreamTile ? DescribeTileIntegrationSummary(downstreamTile) : "oneTilePortalAccess")}");
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowSeedBuildEnd] key={FormatTileKey(key)} downstreamKey={(seedCostUsesDownstreamTile ? FormatTileKey(downstreamTile.Key) : "nextPortal:" + nextPortalId)} " +
                $"portal={FormatPortal(portal)} upstreamCells={FormatGoalCells(goalCells)} downstreamCells={FormatGoalCells(downstreamCells)} " +
                $"downstreamPortalCosts={(seedCostUsesDownstreamTile ? FormatIntegrationCosts(downstreamTile, downstreamCells) : FormatSectorPortalAccessCosts(downstreamSector, downstreamSectorId, nextPortalId, downstreamCells))} skippedUnreachable={skippedUnreachableCount} seeds={FormatSeeds(seeds.ToArray())}");
        }

        return seeds.ToArray();
    }

    private static Vector2Int[] ResolveGoalCells(SectorData sector, FlowTileCacheKey key, int goalX, int goalY)
    {
        if (key.GoalKind == TileGoalKind.FinalGoal)
            return new[] { new Vector2Int(goalX, goalY) };

        return GetPortalCellsForSector(GetPortalById(_world, key.GoalId), sector.SectorId);
    }

    private static bool IsInsideSector(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return worldX >= tile.StartX
               && worldX < tile.StartX + tile.Width
               && worldY >= tile.StartY
               && worldY < tile.StartY + tile.Height;
    }

    private static Vector2 ResolveFlowDirectionFromIntegration(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        int localIndex = tile.GetLocalIndex(worldX, worldY);
        float currentCost = tile.Integration[localIndex];
        if (float.IsPositiveInfinity(currentCost))
            return Vector2.zero;

        return ResolveLowestNeighborFlowDirection(tile, worldX, worldY, currentCost);
    }

    private static Vector2 ResolveLowestNeighborFlowDirection(FlowTileCacheEntry tile, int worldX, int worldY, float currentCost)
    {
        float bestCost = currentCost;
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

        return bestDirection;
    }

    private static void SeedFinalGoalLineOfSightPass(
        FlowTileCacheEntry tile,
        PathHandle handle,
        int sectorPathIndex,
        int goalX,
        int goalY,
        int agentTypeId,
        IntegrationSeed[] seeds,
        Queue<int> open,
        FlowTileCacheEntry downstreamTile)
    {
        if (tile.Key.GoalKind == TileGoalKind.FinalGoal)
        {
            if (seeds == null || seeds.Length == 0)
                throw new InvalidOperationException("SeedFinalGoalLineOfSightPass failed: final goal seeds are missing.");

            for (int i = 0; i < seeds.Length; i++)
                TrySeedLineOfSightCell(tile, seeds[i].Cell, seeds[i].Cost, open);
            return;
        }

        if (tile.Key.GoalKind != TileGoalKind.Portal)
            return;
        if (handle == null)
            throw new InvalidOperationException($"SeedFinalGoalLineOfSightPass failed: portal tile missing handle sector={tile.Key.SectorId}.");

        int nextSectorIndex = sectorPathIndex + 1;
        if (nextSectorIndex >= handle.SectorIds.Length)
            throw new InvalidOperationException($"SeedFinalGoalLineOfSightPass failed: portal tile has no downstream sector sector={tile.Key.SectorId}.");

        if (downstreamTile == null)
            throw new InvalidOperationException($"SeedFinalGoalLineOfSightPass failed: downstream tile unavailable sector={tile.Key.SectorId} nextSector={handle.SectorIds[nextSectorIndex]}.");

        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        Vector2Int[] currentCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
        Vector2Int[] downstreamCells = GetPortalCellsForSector(portal, handle.SectorIds[nextSectorIndex]);
        if (currentCells.Length != downstreamCells.Length)
            throw new InvalidOperationException($"SeedFinalGoalLineOfSightPass failed: portal {portal.PortalId} side cell count mismatch current={currentCells.Length} downstream={downstreamCells.Length}.");

        for (int i = 0; i < currentCells.Length; i++)
        {
            Vector2Int downstreamCell = downstreamCells[i];
            if (!IsInsideSector(downstreamTile, downstreamCell.x, downstreamCell.y))
                continue;

            int downstreamIndex = downstreamTile.GetLocalIndex(downstreamCell.x, downstreamCell.y);
            if (downstreamTile.WaveFrontBlocked != null && downstreamTile.WaveFrontBlocked[downstreamIndex])
            {
                Vector2Int currentCell = currentCells[i];
                if (IsInsideSector(tile, currentCell.x, currentCell.y))
                {
                    float carriedSeedCost = ResolveIntegrationSeedCost(seeds, currentCell);
                    MarkCarriedWaveFrontBlockedLine(tile, goalX, goalY, currentCell.x, currentCell.y, carriedSeedCost);
                }
            }

            if (!downstreamTile.HasLineOfSight[downstreamIndex])
                continue;

            float seedCost = ResolveIntegrationSeedCost(seeds, currentCells[i]);
            TrySeedLineOfSightCell(tile, currentCells[i], seedCost, open);
        }
    }

    private static float ResolveIntegrationSeedCost(IntegrationSeed[] seeds, Vector2Int cell)
    {
        if (seeds == null || seeds.Length == 0)
            throw new InvalidOperationException("ResolveIntegrationSeedCost failed: seeds are missing.");

        for (int i = 0; i < seeds.Length; i++)
        {
            if (seeds[i].Cell.x == cell.x && seeds[i].Cell.y == cell.y)
                return seeds[i].Cost;
        }

        throw new InvalidOperationException($"ResolveIntegrationSeedCost failed: seed not found cell=({cell.x},{cell.y}).");
    }

    private static void TrySeedLineOfSightCell(FlowTileCacheEntry tile, Vector2Int cell, float cost, Queue<int> open)
    {
        if (!IsInsideSector(tile, cell.x, cell.y) || !_world.IsWalkable(cell.x, cell.y))
            return;
        if (!IsClearLosCost(_world, cell.x, cell.y))
            return;

        int localIndex = tile.GetLocalIndex(cell.x, cell.y);
        if (tile.HasLineOfSight[localIndex])
            return;

        tile.HasLineOfSight[localIndex] = true;
        if (cost < tile.Integration[localIndex])
            tile.Integration[localIndex] = cost;
        open.Enqueue(localIndex);
    }

    private static bool IsClearLosCost(NavigationWorld world, int worldX, int worldY)
    {
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            return true;

        return world.CostField[world.GetIndex(worldX, worldY)] == 1;
    }

    private static void MarkWaveFrontBlockedFromLosCorner(FlowTileCacheEntry tile, int goalX, int goalY, int visibleX, int visibleY, int blockedX, int blockedY)
    {
        if (!IsLineOfSightCorner(tile, visibleX, visibleY, blockedX, blockedY))
            return;

        if (!TryResolveLosBlockedLineStart(tile, goalX, goalY, visibleX, visibleY, blockedX, blockedY, out int startX, out int startY))
            return;
        if (!TryResolveLosBlockedLineEnd(tile, goalX, goalY, visibleX, visibleY, blockedX, blockedY, out int endX, out int endY))
            return;

        int visibleIndex = tile.GetLocalIndex(visibleX, visibleY);
        float visibleCost = tile.Integration[visibleIndex];
        if (float.IsPositiveInfinity(visibleCost))
            return;

        float startCost = visibleCost + Mathf.Max(1, Mathf.Max(Mathf.Abs(startX - visibleX), Mathf.Abs(startY - visibleY)));
        MarkWaveFrontBlockedLine(tile, startX, startY, endX, endY, startCost);
    }

    private static void MarkCarriedWaveFrontBlockedLine(FlowTileCacheEntry tile, int goalX, int goalY, int portalX, int portalY, float startCost)
    {
        if (!TryResolveCarriedLosBlockedLineEnd(tile, goalX, goalY, portalX, portalY, out int endX, out int endY))
            return;

        MarkWaveFrontBlockedLine(tile, portalX, portalY, endX, endY, startCost);
    }

    private static bool TryResolveLosBlockedLineStart(
        FlowTileCacheEntry tile,
        int goalX,
        int goalY,
        int visibleX,
        int visibleY,
        int blockedX,
        int blockedY,
        out int startX,
        out int startY)
    {
        int directionX = blockedX - goalX;
        int directionY = blockedY - goalY;
        if (directionX == 0 && directionY == 0)
        {
            directionX = blockedX - visibleX;
            directionY = blockedY - visibleY;
        }

        if (directionX == 0 && directionY == 0)
        {
            startX = blockedX;
            startY = blockedY;
            return false;
        }

        if (directionX > 0)
            startX = Mathf.Clamp(blockedX + 1, tile.StartX, tile.StartX + tile.Width - 1);
        else if (directionX < 0)
            startX = Mathf.Clamp(blockedX - 1, tile.StartX, tile.StartX + tile.Width - 1);
        else
            startX = Mathf.Clamp(blockedX, tile.StartX, tile.StartX + tile.Width - 1);

        if (directionY > 0)
            startY = Mathf.Clamp(blockedY + 1, tile.StartY, tile.StartY + tile.Height - 1);
        else if (directionY < 0)
            startY = Mathf.Clamp(blockedY - 1, tile.StartY, tile.StartY + tile.Height - 1);
        else
            startY = Mathf.Clamp(blockedY, tile.StartY, tile.StartY + tile.Height - 1);

        return true;
    }

    private static bool IsLineOfSightCorner(FlowTileCacheEntry tile, int visibleX, int visibleY, int blockedX, int blockedY)
    {
        int dx = Mathf.Clamp(blockedX - visibleX, -1, 1);
        int dy = Mathf.Clamp(blockedY - visibleY, -1, 1);
        if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1)
            return false;

        int perpendicularAX = dy != 0 ? 1 : 0;
        int perpendicularAY = dx != 0 ? 1 : 0;
        int perpendicularBX = -perpendicularAX;
        int perpendicularBY = -perpendicularAY;

        bool blockedSideA = IsLosBlockedForCorner(tile, blockedX + perpendicularAX, blockedY + perpendicularAY);
        bool blockedSideB = IsLosBlockedForCorner(tile, blockedX + perpendicularBX, blockedY + perpendicularBY);
        if (blockedSideA != blockedSideB)
            return true;

        bool visibleSideA = IsLosBlockedForCorner(tile, visibleX + perpendicularAX, visibleY + perpendicularAY);
        bool visibleSideB = IsLosBlockedForCorner(tile, visibleX + perpendicularBX, visibleY + perpendicularBY);
        return visibleSideA != visibleSideB;
    }

    private static bool IsLosBlockedForCorner(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return !IsInsideSector(tile, worldX, worldY)
               || !_world.IsWalkable(worldX, worldY)
               || !IsClearLosCost(_world, worldX, worldY);
    }

    private static bool TryResolveLosBlockedLineEnd(
        FlowTileCacheEntry tile,
        int goalX,
        int goalY,
        int visibleX,
        int visibleY,
        int blockedX,
        int blockedY,
        out int endX,
        out int endY)
    {
        int directionX = blockedX - goalX;
        int directionY = blockedY - goalY;
        if (directionX == 0 && directionY == 0)
        {
            directionX = blockedX - visibleX;
            directionY = blockedY - visibleY;
        }

        if (directionX == 0 && directionY == 0)
        {
            endX = blockedX;
            endY = blockedY;
            return false;
        }

        float maxScale = float.PositiveInfinity;
        if (directionX > 0)
            maxScale = Mathf.Min(maxScale, (tile.StartX + tile.Width - 1 - blockedX) / (float)directionX);
        else if (directionX < 0)
            maxScale = Mathf.Min(maxScale, (tile.StartX - blockedX) / (float)directionX);

        if (directionY > 0)
            maxScale = Mathf.Min(maxScale, (tile.StartY + tile.Height - 1 - blockedY) / (float)directionY);
        else if (directionY < 0)
            maxScale = Mathf.Min(maxScale, (tile.StartY - blockedY) / (float)directionY);

        if (float.IsInfinity(maxScale) || maxScale < 0f)
            maxScale = 0f;

        endX = Mathf.Clamp(Mathf.RoundToInt(blockedX + directionX * maxScale), tile.StartX, tile.StartX + tile.Width - 1);
        endY = Mathf.Clamp(Mathf.RoundToInt(blockedY + directionY * maxScale), tile.StartY, tile.StartY + tile.Height - 1);
        return true;
    }

    private static bool TryResolveCarriedLosBlockedLineEnd(
        FlowTileCacheEntry tile,
        int goalX,
        int goalY,
        int startX,
        int startY,
        out int endX,
        out int endY)
    {
        int directionX = startX - goalX;
        int directionY = startY - goalY;
        if (directionX == 0 && directionY == 0)
        {
            endX = startX;
            endY = startY;
            return false;
        }

        float maxScale = float.PositiveInfinity;
        if (directionX > 0)
            maxScale = Mathf.Min(maxScale, (tile.StartX + tile.Width - 1 - startX) / (float)directionX);
        else if (directionX < 0)
            maxScale = Mathf.Min(maxScale, (tile.StartX - startX) / (float)directionX);

        if (directionY > 0)
            maxScale = Mathf.Min(maxScale, (tile.StartY + tile.Height - 1 - startY) / (float)directionY);
        else if (directionY < 0)
            maxScale = Mathf.Min(maxScale, (tile.StartY - startY) / (float)directionY);

        if (float.IsInfinity(maxScale) || maxScale < 0f)
            maxScale = 0f;

        endX = Mathf.Clamp(Mathf.RoundToInt(startX + directionX * maxScale), tile.StartX, tile.StartX + tile.Width - 1);
        endY = Mathf.Clamp(Mathf.RoundToInt(startY + directionY * maxScale), tile.StartY, tile.StartY + tile.Height - 1);
        return true;
    }

    private static void MarkWaveFrontBlockedLine(FlowTileCacheEntry tile, int startX, int startY, int endX, int endY, float startCost)
    {
        int x = startX;
        int y = startY;
        int dx = Mathf.Abs(endX - startX);
        int dy = Mathf.Abs(endY - startY);
        int sx = startX < endX ? 1 : -1;
        int sy = startY < endY ? 1 : -1;
        int err = dx - dy;
        float cost = startCost;

        while (true)
        {
            if (IsInsideSector(tile, x, y))
            {
                int localIndex = tile.GetLocalIndex(x, y);
                tile.WaveFrontBlocked[localIndex] = true;
                if (_world.IsWalkable(x, y) && cost < tile.Integration[localIndex])
                    tile.Integration[localIndex] = cost;
            }

            if (x == endX && y == endY)
                return;

            int oldX = x;
            int oldY = y;
            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y += sy;
            }

            cost += Mathf.Abs(x - oldX) + Mathf.Abs(y - oldY) == 2 ? 1.4142135f : 1f;
        }
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
        bool logPortalDiagnostics = ShouldLogPortalDiagnostics(characterKey, goalKind);
        PortalTargetResolution portalTarget = goalKind == TileGoalKind.FinalGoal || logPortalDiagnostics
            ? ResolveTileTargetPosition(characterKey, tile, currentX, currentY, goalPosition, goalKind)
            : new PortalTargetResolution(Vector3.zero, 0, -1, false, string.Empty);
        Vector3 tileTargetPosition = portalTarget.TargetPosition;
        Vector3 lineOfSightTargetPosition = goalPosition;
        Vector3 toLineOfSightTarget = lineOfSightTargetPosition - currentCenter;
        toLineOfSightTarget.y = 0f;
        bool hasLineOfSight = tile.HasLineOfSight[localIndex];
        Vector2 flow = tile.FlowDirections[localIndex];
        float integration = tile.Integration[localIndex];
        Vector3 flowDirection = new Vector3(flow.x, 0f, flow.y);
        Vector3 lineOfSightDirection = toLineOfSightTarget.sqrMagnitude > 0.0001f ? toLineOfSightTarget.normalized : Vector3.zero;
        DesiredDirectionResolution resolution;

        if (tile.HasLineOfSight[localIndex] && toLineOfSightTarget.sqrMagnitude > 0.0001f)
        {
            resolution = new DesiredDirectionResolution(
                lineOfSightDirection,
                DesiredDirectionSource.LineOfSight,
                lineOfSightTargetPosition,
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
                    $"flow={flow} targetPos={tileTargetPosition} losTarget={lineOfSightTargetPosition} toLosTarget={toLineOfSightTarget} " +
                    $"integration={integration:F3} isGoalCell={isPortalGoalCell} goalCells={FormatGoalCells(tile.GoalCells)}");
            }

            resolution = new DesiredDirectionResolution(
                Vector3.zero,
                DesiredDirectionSource.Zero,
                tileTargetPosition,
                flow,
                hasLineOfSight,
                integration);
        }

        if (logPortalDiagnostics)
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPortalResolve] key={characterKey} sector={tile.Key.SectorId} goalId={tile.Key.GoalId} " +
                $"cell=({currentX},{currentY}) center={currentCenter} localIndex={localIndex} integration={integration:F3} " +
                $"source={resolution.Source} hasLOS={hasLineOfSight} flow={flow} flowDir={flowDirection} " +
                $"tileTarget={tileTargetPosition} losTarget={lineOfSightTargetPosition} toLosTarget={toLineOfSightTarget} losDir={lineOfSightDirection} " +
                $"flowDotLos={Vector3.Dot(flowDirection, lineOfSightDirection):F3} " +
                $"visibleCandidates={portalTarget.VisibleCandidateCount} selectedPair={portalTarget.SelectedPairIndex} " +
                $"usedOppositeCenter={portalTarget.UsedOppositeCenter} goalCells={FormatGoalCells(tile.GoalCells)}");
        }

        return resolution;
    }

    private static DesiredDirectionResolution ApplyPathDirectionBlend(AgentRuntimeData agent, DesiredDirectionResolution resolution, int currentX, int currentY)
    {
        if (agent == null)
            throw new InvalidOperationException("ApplyPathDirectionBlend failed: agent is null.");

        AgentNavState navState = agent.NavState;
        if (resolution.Source != DesiredDirectionSource.FlowField
            || resolution.Direction.sqrMagnitude <= 0.0001f
            || Config.PathDirectionBlend >= 0.999f)
        {
            navState.PathDirection = resolution.Direction;
            navState.PathDirectionCell = new Vector2Int(currentX, currentY);
            return resolution;
        }

        Vector2Int cell = new Vector2Int(currentX, currentY);
        Vector3 rawDirection = resolution.Direction.normalized;
        if (navState.PathDirection.sqrMagnitude <= 0.0001f
            || Vector3.Dot(navState.PathDirection, rawDirection) <= 0f)
        {
            navState.PathDirection = rawDirection;
            navState.PathDirectionCell = cell;
            return resolution;
        }

        float newDirectionWeight = Mathf.Clamp01(Config.PathDirectionBlend);
        Vector3 blendedDirection = Vector3.Lerp(navState.PathDirection.normalized, rawDirection, newDirectionWeight);
        if (blendedDirection.sqrMagnitude <= 0.0001f)
            blendedDirection = rawDirection;

        blendedDirection.Normalize();
        navState.PathDirection = blendedDirection;
        navState.PathDirectionCell = cell;
        return new DesiredDirectionResolution(
            blendedDirection,
            resolution.Source,
            resolution.TileTargetPosition,
            resolution.Flow,
            resolution.HasLineOfSight,
            resolution.Integration);
    }

    private static PortalTargetResolution ResolveTileTargetPosition(string characterKey, FlowTileCacheEntry tile, int currentX, int currentY, Vector3 goalPosition, TileGoalKind goalKind)
    {
        if (goalKind == TileGoalKind.FinalGoal)
            return new PortalTargetResolution(goalPosition, 0, -1, false, string.Empty);

        if (goalKind != TileGoalKind.Portal || tile.GoalCells == null || tile.GoalCells.Length == 0)
            throw new InvalidOperationException($"ResolveTileTargetPosition failed: invalid portal tile target sector={tile.Key.SectorId} goalId={tile.Key.GoalId}");
        if (tile.PortalTargets == null || tile.PortalTargets.Length != tile.Width * tile.Height)
            throw new InvalidOperationException($"ResolveTileTargetPosition failed: portal target cache missing sector={tile.Key.SectorId} goalId={tile.Key.GoalId}.");

        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, tile.Key.SectorId));
        if (currentSideCells.Length != oppositeSideCells.Length)
            throw new InvalidOperationException(
                $"ResolveTileTargetPosition failed: portal {portal.PortalId} side cell count mismatch current={currentSideCells.Length} opposite={oppositeSideCells.Length}");

        bool logPortal = ShouldLogPortalDiagnostics(characterKey, goalKind);
        int localIndex = tile.GetLocalIndex(currentX, currentY);
        CachedPortalTarget cached = tile.PortalTargets[localIndex];
        string candidateSummary = logPortal
            ? BuildCachedPortalTargetCandidateSummary(tile, currentX, currentY, currentSideCells, oppositeSideCells, cached)
            : string.Empty;
        if (!cached.UsedOppositeCenter)
        {
            if (logPortal)
            {
                GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPortalTarget] key={characterKey} sector={tile.Key.SectorId} goalId={tile.Key.GoalId} " +
                $"portalCenter={portal.WorldCenter} vertical={portal.IsVerticalBoundary} widthCells={portal.WidthCells} " +
                $"currentCell=({currentX},{currentY}) currentCenter={_world.GridToWorldCenter(currentX, currentY)} " +
                $"currentSide={FormatGoalCells(currentSideCells)} oppositeSide={FormatGoalCells(oppositeSideCells)} " +
                $"selection=LowestVisibleCost selectedPair={cached.SelectedPairIndex} visibleCount={cached.VisibleCandidateCount} " +
                $"target={cached.TargetPosition} candidates={candidateSummary}");
            }

            return new PortalTargetResolution(cached.TargetPosition, cached.VisibleCandidateCount, cached.SelectedPairIndex, false, candidateSummary);
        }

        if (logPortal)
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPortalTarget] key={characterKey} sector={tile.Key.SectorId} goalId={tile.Key.GoalId} " +
                $"portalCenter={portal.WorldCenter} vertical={portal.IsVerticalBoundary} widthCells={portal.WidthCells} " +
                $"currentCell=({currentX},{currentY}) currentCenter={_world.GridToWorldCenter(currentX, currentY)} " +
                $"currentSide={FormatGoalCells(currentSideCells)} oppositeSide={FormatGoalCells(oppositeSideCells)} " +
                $"selection=OppositeCenter selectedPair=-1 visibleCount=0 target={cached.TargetPosition} candidates={candidateSummary}");
        }

        return new PortalTargetResolution(cached.TargetPosition, cached.VisibleCandidateCount, cached.SelectedPairIndex, true, candidateSummary);
    }

    private static CachedPortalTarget ResolveCachedPortalTarget(FlowTileCacheEntry tile, int currentX, int currentY)
    {
        if (tile == null)
            throw new InvalidOperationException("ResolveCachedPortalTarget failed: tile is null.");
        if (tile.Key.GoalKind != TileGoalKind.Portal)
            return default;
        if (!IsInsideSector(tile, currentX, currentY) || !_world.IsWalkable(currentX, currentY))
            return default;

        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, tile.Key.SectorId));
        if (currentSideCells.Length != oppositeSideCells.Length)
            throw new InvalidOperationException(
                $"ResolveCachedPortalTarget failed: portal {portal.PortalId} side cell count mismatch current={currentSideCells.Length} opposite={oppositeSideCells.Length}");

        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);
        float bestGoalCost = float.PositiveInfinity;
        float bestDistanceSq = float.MaxValue;
        Vector3 bestTarget = Vector3.zero;
        int visibleCandidateCount = 0;
        int selectedPairIndex = -1;
        for (int i = 0; i < currentSideCells.Length; i++)
        {
            Vector2Int goalCell = currentSideCells[i];
            if (!HasGridLineOfSight(_world, currentX, currentY, goalCell.x, goalCell.y))
                continue;

            float goalCost = tile.Integration[tile.GetLocalIndex(goalCell.x, goalCell.y)];
            if (float.IsPositiveInfinity(goalCost))
                continue;

            Vector2Int oppositeCell = oppositeSideCells[i];
            Vector3 candidate = _world.GridToWorldCenter(oppositeCell.x, oppositeCell.y);
            float distanceSq = (candidate - currentCenter).sqrMagnitude;
            visibleCandidateCount++;
            bool betterCost = goalCost < bestGoalCost - 0.001f;
            bool sameCostCloser = Mathf.Abs(goalCost - bestGoalCost) <= 0.001f && distanceSq < bestDistanceSq;
            if (!betterCost && !sameCostCloser)
                continue;

            bestGoalCost = goalCost;
            bestDistanceSq = distanceSq;
            bestTarget = candidate;
            selectedPairIndex = i;
        }

        if (selectedPairIndex >= 0)
            return new CachedPortalTarget(bestTarget, visibleCandidateCount, selectedPairIndex, false);

        Vector3 oppositePortalCenter = ResolveCellGroupCenter(oppositeSideCells);
        return new CachedPortalTarget(oppositePortalCenter, 0, -1, true);
    }

    private static string BuildCachedPortalTargetCandidateSummary(
        FlowTileCacheEntry tile,
        int currentX,
        int currentY,
        Vector2Int[] currentSideCells,
        Vector2Int[] oppositeSideCells,
        CachedPortalTarget cached)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(currentSideCells.Length * 96);
        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);
        for (int i = 0; i < currentSideCells.Length; i++)
        {
            if (builder.Length > 0)
                builder.Append(" | ");

            Vector2Int goalCell = currentSideCells[i];
            Vector2Int oppositeCell = oppositeSideCells[i];
            Vector3 candidate = _world.GridToWorldCenter(oppositeCell.x, oppositeCell.y);
            float distanceSq = (candidate - currentCenter).sqrMagnitude;
            float goalCost = tile.Integration[tile.GetLocalIndex(goalCell.x, goalCell.y)];
            builder.Append("i=").Append(i)
                .Append(" goal=").Append('(').Append(goalCell.x).Append(',').Append(goalCell.y).Append(')')
                .Append(" opp=").Append('(').Append(oppositeCell.x).Append(',').Append(oppositeCell.y).Append(')')
                .Append(" selected=").Append(!cached.UsedOppositeCenter && cached.SelectedPairIndex == i)
                .Append(" goalCost=").Append(goalCost.ToString("F3"))
                .Append(" distSq=").Append(distanceSq.ToString("F3"))
                .Append(" cand=").Append(candidate);
        }

        return builder.ToString();
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

    private static string FormatSeeds(IntegrationSeed[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(seeds.Length * 20);
        builder.Append('[');
        for (int i = 0; i < seeds.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append('(');
            builder.Append(seeds[i].Cell.x);
            builder.Append(',');
            builder.Append(seeds[i].Cell.y);
            builder.Append("):");
            builder.Append(seeds[i].Cost.ToString("F3"));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string FormatTileKey(FlowTileCacheKey key)
    {
        return $"(world={key.WorldVersion},sector={key.SectorId},goalKind={key.GoalKind},goalId={key.GoalId},downstreamHint={key.DownstreamGoalHint},finalGoal={key.FinalGoalIndex},agentType={key.AgentTypeId},dirty={key.DirtyVersion})";
    }

    private static string FormatPathHandle(PathHandle handle)
    {
        if (handle == null)
            return "null";

        string sectors = handle.SectorIds == null ? "null" : string.Join("->", handle.SectorIds);
        string portals = handle.PortalIds == null ? "null" : string.Join("->", handle.PortalIds);
        return $"(world={handle.WorldVersion},goal=({handle.GoalX},{handle.GoalY}),currentSectorIndex={handle.CurrentSectorIndex},sectors=[{sectors}],portals=[{portals}])";
    }

    private static string BuildCurrentTileSteeringDiagnostics(FlowTileCacheEntry tile, int currentX, int currentY)
    {
        if (tile == null)
            return "tile=null";
        if (!IsInsideSector(tile, currentX, currentY))
            return $"tileKey={FormatTileKey(tile.Key)} cell=outside rect=({tile.StartX},{tile.StartY},{tile.Width},{tile.Height})";

        int localIndex = tile.GetLocalIndex(currentX, currentY);
        float currentCost = tile.Integration[localIndex];
        Vector2 currentFlow = tile.FlowDirections[localIndex];
        bool currentLos = tile.HasLineOfSight[localIndex];
        bool currentWaveBlocked = tile.WaveFrontBlocked != null && tile.WaveFrontBlocked[localIndex];
        string goalCellState = BuildTileGoalCellStateDiagnostics(tile, currentX, currentY);

        int bestNeighborX = currentX;
        int bestNeighborY = currentY;
        float bestNeighborCost = currentCost;
        Vector2 bestNeighborFlow = currentFlow;
        bool bestNeighborFound = false;
        System.Text.StringBuilder neighborBuilder = new System.Text.StringBuilder(384);
        neighborBuilder.Append("[");
        int neighborCount = 0;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = currentX + NeighborOffsetX[i];
            int nextY = currentY + NeighborOffsetY[i];
            if (!IsInsideSector(tile, nextX, nextY) || !_world.IsWalkable(nextX, nextY))
                continue;

            if (!CanTraverseNeighborCells(_world, currentX, currentY, nextX, nextY))
                continue;

            int nextIndex = tile.GetLocalIndex(nextX, nextY);
            float nextCost = tile.Integration[nextIndex];
            Vector2 nextFlow = tile.FlowDirections[nextIndex];
            if (neighborCount > 0)
                neighborBuilder.Append(" | ");
            neighborBuilder.Append("(").Append(nextX).Append(',').Append(nextY).Append("){cost=")
                .Append(float.IsPositiveInfinity(nextCost) ? "INF" : nextCost.ToString("F3"))
                .Append(",flow=").Append(nextFlow)
                .Append(",los=").Append(tile.HasLineOfSight[nextIndex])
                .Append(",blocked=").Append(tile.WaveFrontBlocked != null && tile.WaveFrontBlocked[nextIndex])
                .Append("}");
            neighborCount++;

            if (float.IsPositiveInfinity(nextCost))
                continue;

            if (!bestNeighborFound || nextCost < bestNeighborCost - 0.0001f)
            {
                bestNeighborFound = true;
                bestNeighborX = nextX;
                bestNeighborY = nextY;
                bestNeighborCost = nextCost;
                bestNeighborFlow = nextFlow;
            }
        }

        if (neighborCount == 0)
            neighborBuilder.Append("none");
        neighborBuilder.Append(']');

        return $"tileKey={FormatTileKey(tile.Key)} cell=({currentX},{currentY}) cost={(float.IsPositiveInfinity(currentCost) ? "INF" : currentCost.ToString("F3"))} " +
               $"flow={currentFlow} los={currentLos} waveBlocked={currentWaveBlocked} {goalCellState} " +
               $"bestNeighbor=({bestNeighborX},{bestNeighborY}) bestNeighborCost={(float.IsPositiveInfinity(bestNeighborCost) ? "INF" : bestNeighborCost.ToString("F3"))} " +
               $"bestNeighborFlow={bestNeighborFlow} neighbors={neighborBuilder}";
    }

    private static string BuildTileGoalCellStateDiagnostics(FlowTileCacheEntry tile, int currentX, int currentY)
    {
        if (tile?.GoalCells == null)
            return "goalCellState=none";

        int goalIndex = -1;
        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            if (tile.GoalCells[i].x == currentX && tile.GoalCells[i].y == currentY)
            {
                goalIndex = i;
                break;
            }
        }

        if (goalIndex < 0)
            return $"isGoalCell=False goalCells={FormatGoalCells(tile.GoalCells)}";

        if (tile.Key.GoalKind != TileGoalKind.Portal)
            return $"isGoalCell=True goalIndex={goalIndex} goalCells={FormatGoalCells(tile.GoalCells)}";

        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        int oppositeSectorId = GetOppositeSectorId(portal, tile.Key.SectorId);
        Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, oppositeSectorId);
        if (goalIndex >= oppositeCells.Length)
            return $"isGoalCell=True goalIndex={goalIndex} opposite=out-of-range portal={FormatPortal(portal)}";

        Vector2Int opposite = oppositeCells[goalIndex];
        bool oppositeWalkable = _world.IsWalkable(opposite.x, opposite.y);
        bool traversable = oppositeWalkable && CanTraverseNeighborCells(_world, currentX, currentY, opposite.x, opposite.y);
        return $"isGoalCell=True goalIndex={goalIndex} opposite=({opposite.x},{opposite.y}) oppositeWalkable={oppositeWalkable} oppositeTraversable={traversable} portal={FormatPortal(portal)}";
    }

    private static string BuildNavPathDiagnostics(Vector3 fromPosition, Vector3 toPosition, int agentTypeId)
    {
        if (_world == null)
            return "navPath=world-null";

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId >= 0 ? agentTypeId : _world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float sampleRadius = Mathf.Max(0.2f, _world.CellSize * 1.5f);
        bool fromSampled = NavMesh.SamplePosition(fromPosition, out NavMeshHit fromHit, sampleRadius, filter);
        bool toSampled = NavMesh.SamplePosition(toPosition, out NavMeshHit toHit, sampleRadius, filter);
        if (!fromSampled || !toSampled)
        {
            return $"navPath=sample-miss fromSampled={fromSampled} toSampled={toSampled} fromPos={fromPosition} toPos={toPosition} sampleRadius={sampleRadius:F3}";
        }

        NavMeshPath path = new NavMeshPath();
        bool found = NavMesh.CalculatePath(fromHit.position, toHit.position, filter, path);
        int cornerCount = path.corners != null ? path.corners.Length : 0;
        Vector3 firstCorner = cornerCount > 1 ? path.corners[1] : Vector3.zero;
        Vector3 lastCorner = cornerCount > 0 ? path.corners[cornerCount - 1] : Vector3.zero;
        Vector3 firstLeg = cornerCount > 1 ? path.corners[1] - path.corners[0] : Vector3.zero;
        return $"navPath={{found={found},status={path.status},corners={cornerCount},fromHit={fromHit.position},toHit={toHit.position},firstCorner={firstCorner},lastCorner={lastCorner},firstLeg={firstLeg}}}";
    }

    private static void LogFlowExecutionMismatchDiagnostic(
        IEntityContext self,
        AgentRuntimeData agent,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int startSectorId,
        int goalSectorId,
        Vector3 rawGoalPosition,
        Vector3 occupiedGoalPosition,
        Vector3 stableGoalPosition,
        TileGoalKind goalKind,
        int downstreamPortalId,
        FlowTileCacheEntry tile,
        DesiredDirectionResolution desiredResolution,
        Vector3 desiredDirection,
        Vector3 desiredVelocity,
        int agentTypeId)
    {
        if (self == null || agent == null || _world == null)
            return;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return;

        int frame = GetFrameCount();
        if (agent.NavState.LastFlowExecutionMismatchDiagnosticFrame >= 0
            && frame - agent.NavState.LastFlowExecutionMismatchDiagnosticFrame < 10)
        {
            return;
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId >= 0 ? agentTypeId : _world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        float sampleRadius = Mathf.Max(0.2f, _world.CellSize * 1.5f);
        bool fromSampled = NavMesh.SamplePosition(self.Position, out NavMeshHit fromHit, sampleRadius, filter);
        bool goalSampled = NavMesh.SamplePosition(stableGoalPosition, out NavMeshHit goalHit, sampleRadius, filter);
        bool tileTargetSampled = NavMesh.SamplePosition(desiredResolution.TileTargetPosition, out NavMeshHit tileTargetHit, sampleRadius, filter);
        if (!fromSampled)
            return;

        Vector3 firstLegDirection = Vector3.zero;
        Vector3 firstCorner = Vector3.zero;
        Vector3 lastCorner = Vector3.zero;
        bool pathFound = false;
        NavMeshPathStatus pathStatus = NavMeshPathStatus.PathInvalid;
        int cornerCount = 0;
        float desiredDotFirstLeg = 1f;
        if (goalSampled)
        {
            NavMeshPath path = new NavMeshPath();
            pathFound = NavMesh.CalculatePath(fromHit.position, goalHit.position, filter, path);
            pathStatus = path.status;
            cornerCount = path.corners != null ? path.corners.Length : 0;
            if (cornerCount > 1)
            {
                firstCorner = path.corners[1];
                lastCorner = path.corners[cornerCount - 1];
                firstLegDirection = firstCorner - path.corners[0];
                firstLegDirection.y = 0f;
                if (firstLegDirection.sqrMagnitude > 0.0001f)
                {
                    firstLegDirection.Normalize();
                    desiredDotFirstLeg = Vector3.Dot(desiredDirection, firstLegDirection);
                }
            }
        }

        NavMeshHit goalRayHit = default;
        NavMeshHit tileRayHit = default;
        bool goalRayBlocked = goalSampled && NavMesh.Raycast(fromHit.position, goalHit.position, out goalRayHit, filter);
        bool tileRayBlocked = tileTargetSampled && NavMesh.Raycast(fromHit.position, tileTargetHit.position, out tileRayHit, filter);
        float tileTargetDistance = HorizontalDistanceXZ(self.Position, desiredResolution.TileTargetPosition);
        float goalDistance = HorizontalDistanceXZ(self.Position, stableGoalPosition);
        bool suspiciousPathDirection = pathFound
                                       && pathStatus == NavMeshPathStatus.PathComplete
                                       && cornerCount > 2
                                       && desiredDotFirstLeg < 0.35f
                                       && goalDistance > Mathf.Max(_world.CellSize * 4f, agent.Radius * 6f);
        bool suspiciousTileTarget = tileRayBlocked
                                    && tileTargetDistance > Mathf.Max(_world.CellSize * 1.5f, agent.Radius * 3f);
        bool suspiciousLos = desiredResolution.Source == DesiredDirectionSource.LineOfSight
                             && goalRayBlocked
                             && goalDistance > Mathf.Max(_world.CellSize * 4f, agent.Radius * 6f);
        if (!suspiciousPathDirection && !suspiciousTileTarget && !suspiciousLos)
            return;

        agent.NavState.LastFlowExecutionMismatchDiagnosticFrame = frame;
        string startPortalChoiceDiagnostics = BuildStartPortalChoiceDiagnostics(
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            agentTypeId,
            self.Position,
            stableGoalPosition);
        Debug.LogWarning(
            $"[FlowExecutionMismatchDiag] frame={frame} key={self.CharacterKey} id={agent.Id} pos={self.Position} " +
            $"rawGoal={rawGoalPosition} occupiedGoal={occupiedGoalPosition} stableGoal={stableGoalPosition} " +
            $"start=({startX},{startY}) goal=({goalX},{goalY}) sector={startSectorId}->{goalSectorId} " +
            $"goalKind={goalKind} portal={downstreamPortalId} handle={FormatPathHandle(agent.NavState.PathHandle)} " +
            $"desiredSrc={desiredResolution.Source} desiredDir={desiredDirection} desiredVel={desiredVelocity} " +
            $"flow={desiredResolution.Flow} los={desiredResolution.HasLineOfSight} integration={desiredResolution.Integration:F3} " +
            $"tileTarget={desiredResolution.TileTargetPosition} tileTargetDistance={tileTargetDistance:F3} " +
            $"tileRayBlocked={tileRayBlocked} tileRayHit={(tileRayBlocked ? tileRayHit.position.ToString() : "none")} " +
            $"tileRayNormal={(tileRayBlocked ? tileRayHit.normal.ToString() : "none")} " +
            $"goalRayBlocked={goalRayBlocked} goalRayHit={(goalRayBlocked ? goalRayHit.position.ToString() : "none")} " +
            $"goalRayNormal={(goalRayBlocked ? goalRayHit.normal.ToString() : "none")} " +
            $"pathFound={pathFound} pathStatus={pathStatus} corners={cornerCount} firstCorner={firstCorner} lastCorner={lastCorner} " +
            $"firstLegDir={firstLegDirection} desiredDotFirstLeg={desiredDotFirstLeg:F3} " +
            $"tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
            $"{startPortalChoiceDiagnostics} " +
            $"navPathDiag={BuildNavPathDiagnostics(self.Position, stableGoalPosition, agentTypeId)}");
        Debug.LogWarning(startPortalChoiceDiagnostics);
    }

    private static string BuildStartPortalChoiceDiagnostics(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int agentTypeId,
        Vector3 startPosition,
        Vector3 stableGoalPosition)
    {
        if (_world == null)
            return "portalChoice=world-null";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return $"portalChoice=invalid-start-sector startSector={startSectorId}";

        SharedGoalField sharedField = GetOrBuildSharedGoalField(goalSectorId, goalX, goalY, ResolvePreferredAgentTypeId(agentTypeId));
        if (sharedField == null)
            return "portalChoice=shared-field-null";

        SectorData startSector = _world.Sectors[startSectorId];
        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        builder.Append("[FlowStartPortalChoiceDiag startSector=").Append(startSectorId)
            .Append(" goalSector=").Append(goalSectorId)
            .Append(" start=(").Append(startX).Append(',').Append(startY).Append(')')
            .Append(" goal=(").Append(goalX).Append(',').Append(goalY).Append(')')
            .Append(" portals=");

        if (startSector.PortalIds.Count == 0)
        {
            builder.Append("none]");
            return builder.ToString();
        }

        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            if (i > 0)
                builder.Append(" | ");

            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            bool hasDownstream = sharedField.NodeCosts.TryGetValue(startNode, out float downstreamCost);
            float accessCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            float totalCost = hasDownstream && !float.IsPositiveInfinity(accessCost)
                ? accessCost + downstreamCost
                : float.PositiveInfinity;
            PortalData portal = TryGetPortalById(_world, portalId, out PortalData portalData) ? portalData : null;
            int oppositeSector = portal != null ? GetOppositeSectorId(portal, startSectorId) : -1;
            Vector2Int[] cells = portal != null ? GetPortalCellsForSector(portal, startSectorId) : null;
            Vector3 center = portal != null ? portal.WorldCenter : Vector3.zero;
            string portalNav = portal != null
                ? BuildPortalCandidateNavDiagnostics(startPosition, stableGoalPosition, portal, startSectorId, agentTypeId)
                : "portal=null";
            builder.Append("{id=").Append(portalId)
                .Append(",opp=").Append(oppositeSector)
                .Append(",access=").Append(float.IsPositiveInfinity(accessCost) ? "INF" : accessCost.ToString("F3"))
                .Append(",downstream=").Append(hasDownstream ? downstreamCost.ToString("F3") : "missing")
                .Append(",total=").Append(float.IsPositiveInfinity(totalCost) ? "INF" : totalCost.ToString("F3"))
                .Append(",center=").Append(center)
                .Append(",cells=").Append(FormatGoalCells(cells))
                .Append(",nav=").Append(portalNav)
                .Append('}');
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static string BuildPortalCandidateNavDiagnostics(Vector3 startPosition, Vector3 stableGoalPosition, PortalData portal, int sectorId, int agentTypeId)
    {
        Vector2Int[] currentCells = GetPortalCellsForSector(portal, sectorId);
        Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, sectorId));
        Vector3 currentCenter = ResolveCellGroupCenter(currentCells);
        Vector3 oppositeCenter = ResolveCellGroupCenter(oppositeCells);
        string toCurrent = BuildNavPathDiagnostics(startPosition, currentCenter, agentTypeId);
        string toOpposite = BuildNavPathDiagnostics(startPosition, oppositeCenter, agentTypeId);
        string currentToGoal = BuildNavPathDiagnostics(currentCenter, stableGoalPosition, agentTypeId);
        string oppositeToGoal = BuildNavPathDiagnostics(oppositeCenter, stableGoalPosition, agentTypeId);
        bool startInGrid = _world.WorldToGrid(startPosition, out int startX, out int startY);
        bool gridLosCurrent = startInGrid
                              && currentCells.Length > 0
                              && HasGridLineOfSight(_world, startX, startY, currentCells[0].x, currentCells[0].y);
        string losTrace = currentCells.Length > 0 && startInGrid
            ? BuildLineOfSightCellTrace(_world, startX, startY, currentCells[0].x, currentCells[0].y)
            : "trace=unavailable";
        return "{currentCenter=" + currentCenter
               + ",oppositeCenter=" + oppositeCenter
               + ",gridLosFirstCell=" + gridLosCurrent
               + ",losTrace=" + losTrace
               + ",toCurrent=" + toCurrent
               + ",toOpposite=" + toOpposite
               + ",currentToGoal=" + currentToGoal
               + ",oppositeToGoal=" + oppositeToGoal
               + "}";
    }

    private static Vector3 ResolveCellGroupCenter(Vector2Int[] cells)
    {
        if (cells == null || cells.Length == 0)
            return Vector3.zero;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < cells.Length; i++)
            center += _world.GridToWorldCenter(cells[i].x, cells[i].y);
        return center / cells.Length;
    }

    private static string FormatPortal(PortalData portal)
    {
        if (portal == null)
            return "null";

        return $"(id={portal.PortalId},sectorA={portal.SectorAId},sectorB={portal.SectorBId},vertical={portal.IsVerticalBoundary},width={portal.WidthCells},narrow={portal.IsNarrow},cellsA={FormatGoalCells(portal.CellsA)},cellsB={FormatGoalCells(portal.CellsB)})";
    }

    private static string FormatIntegrationCosts(FlowTileCacheEntry tile, Vector2Int[] cells)
    {
        if (tile == null)
            return "tile=null";
        if (cells == null || cells.Length == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(cells.Length * 24);
        builder.Append('[');
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            Vector2Int cell = cells[i];
            builder.Append('(');
            builder.Append(cell.x);
            builder.Append(',');
            builder.Append(cell.y);
            builder.Append(")=");

            if (!IsInsideSector(tile, cell.x, cell.y))
            {
                builder.Append("outside");
                continue;
            }

            float cost = tile.Integration[tile.GetLocalIndex(cell.x, cell.y)];
            builder.Append(float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3"));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string FormatSectorPortalAccessCosts(SectorData sector, int sectorId, int portalId, Vector2Int[] cells)
    {
        if (sector == null)
            return "sector=null";
        if (portalId < 0)
            return "portal=none";
        if (cells == null || cells.Length == 0)
            return "[]";

        SectorPortalAccessEntry entry = GetOrBuildSectorPortalAccess(sector, sectorId, portalId);
        System.Text.StringBuilder builder = new System.Text.StringBuilder(cells.Length * 24);
        builder.Append('[');
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            Vector2Int cell = cells[i];
            builder.Append('(');
            builder.Append(cell.x);
            builder.Append(',');
            builder.Append(cell.y);
            builder.Append(")=");

            if (!IsInsideSector(sector, cell.x, cell.y))
            {
                builder.Append("outside");
                continue;
            }

            float cost = entry.Integration[GetSectorLocalIndex(sector, cell.x, cell.y)];
            builder.Append(float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3"));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string DescribeTileIntegrationSummary(FlowTileCacheEntry tile)
    {
        if (tile == null || tile.Integration == null || tile.Integration.Length == 0)
            return "empty";

        int reachableCount = 0;
        int unreachableCount = 0;
        float minCost = float.PositiveInfinity;
        float maxCost = 0f;
        for (int i = 0; i < tile.Integration.Length; i++)
        {
            float cost = tile.Integration[i];
            if (float.IsPositiveInfinity(cost))
            {
                unreachableCount++;
                continue;
            }

            reachableCount++;
            if (cost < minCost)
                minCost = cost;
            if (cost > maxCost)
                maxCost = cost;
        }

        return $"reachable={reachableCount} unreachable={unreachableCount} minCost={(reachableCount > 0 ? minCost.ToString("F3") : "n/a")} maxCost={(reachableCount > 0 ? maxCost.ToString("F3") : "n/a")}";
    }

    private static BottleneckDecision EvaluateBottleneck(AgentRuntimeData agent, CorridorBottleneckDescriptor descriptor)
    {
        if (!Bottlenecks.TryGetValue(descriptor.BottleneckId, out BottleneckRuntimeState state))
        {
            state = new BottleneckRuntimeState
            {
                BottleneckId = descriptor.BottleneckId,
                PortalId = descriptor.SeedPortalId,
                CorridorAxis = descriptor.CorridorAxis,
                CorridorAxisMode = ResolveCorridorAxisMode(descriptor.CorridorAxis)
            };
            Bottlenecks.Add(descriptor.BottleneckId, state);
        }

        int frameCount = GetFrameCount();
        float time = GetTime();
        if (state.LastFrameTouched != frameCount)
        {
            state.LastFrameTouched = frameCount;
            state.OccupiedCount = 0;
        }

        RefreshBottleneckWaitingOrder(state);
        UpdateBottleneckPriorityOverride(state);

        Vector3 anchor = ResolveBottleneckAnchor(state, descriptor);
        float distanceToAnchor = Vector3.Distance(agent.Position, anchor);
        bool shouldApplyLaneBias = HasBottleneckTrafficContext(agent, descriptor, anchor);
        Vector3 laneBias = shouldApplyLaneBias ? ResolveStableLaneBias(descriptor.CorridorAxis) : Vector3.zero;
        bool nearBottleneck = distanceToAnchor <= descriptor.InfluenceRadius;
        if (!nearBottleneck)
        {
            state.CurrentOwnerState = state.WaitingAgentsOrdered.Count > 0
                ? BottleneckRuntimeState.OwnerState.Queueing
                : BottleneckRuntimeState.OwnerState.Idle;
            return BuildBottleneckDecision(state, 1f, ApplyCommittedLaneBias(state, agent, laneBias, anchor), Vector3.zero, true);
        }

        if (state.CurrentDirection == 0)
        {
            state.CurrentDirection = descriptor.Direction;
            state.SwitchBlockedUntil = time + Config.BottleneckSwitchCooldown;
            state.CurrentOwnerState = BottleneckRuntimeState.OwnerState.Switching;
        }

        bool isPriorityOverride = state.PriorityOverrideAgentId == agent.Id;
        bool keepsConvoyToken = state.ConvoyTokenAgentId == agent.Id || SharesConvoyToken(state, agent);
        bool isQueueHead = state.WaitingAgentsOrdered.Count > 0 && state.WaitingAgentsOrdered[0] == agent.Id;
        bool shouldYield = state.CurrentDirection != descriptor.Direction && !isPriorityOverride && !keepsConvoyToken;
        if (shouldYield)
        {
            if (!state.WaitingStartTimes.TryGetValue(agent.Id, out float waitStart))
            {
                waitStart = time;
                state.WaitingStartTimes[agent.Id] = waitStart;
            }

            bool bottleneckRecentlyOccupied = time - state.LastOccupiedTime < Config.BottleneckClearanceHoldTime;
            bool sameDirectionConvoyIncoming = HasIncomingDirectionalTraffic(agent, descriptor, anchor, state.CurrentDirection);
            bool canSwitch =
                time >= state.SwitchBlockedUntil
                && state.OccupiedCount == 0
                && !bottleneckRecentlyOccupied
                && isQueueHead
                && (!sameDirectionConvoyIncoming || isPriorityOverride);
            bool timedOut = time - waitStart >= Config.BottleneckWaitTimeout
                            && state.OccupiedCount == 0
                            && !bottleneckRecentlyOccupied
                            && isQueueHead
                            && !sameDirectionConvoyIncoming;
            if (canSwitch || timedOut)
            {
                state.CurrentDirection = descriptor.Direction;
                state.CurrentOwnerAgentId = agent.Id;
                state.ConvoyTokenAgentId = ResolveConvoyTokenAgent(state, agent);
                state.SwitchBlockedUntil = time + Config.BottleneckSwitchCooldown;
                state.CurrentOwnerState = BottleneckRuntimeState.OwnerState.Switching;
                state.WaitingStartTimes.Remove(agent.Id);
                RefreshBottleneckWaitingOrder(state);
            }
            else
            {
                state.CurrentOwnerState = BottleneckRuntimeState.OwnerState.Queueing;
                Vector3 committedLaneBias = ApplyCommittedLaneBias(state, agent, laneBias, anchor);
                Vector3 queueBias = ResolveQueueBias(state, descriptor, anchor, committedLaneBias, agent, time);
                return BuildBottleneckDecision(state, 0f, committedLaneBias, queueBias, true);
            }
        }

        state.WaitingStartTimes.Remove(agent.Id);
        RefreshBottleneckWaitingOrder(state);
        if (distanceToAnchor <= descriptor.InfluenceRadius * 0.85f)
        {
            state.OccupiedCount++;
            state.LastOccupiedTime = time;
            state.CurrentOwnerAgentId = agent.Id;
            state.ConvoyTokenAgentId = ResolveConvoyTokenAgent(state, agent);
            state.CurrentOwnerState = BottleneckRuntimeState.OwnerState.Passing;
        }
        else if (state.CurrentOwnerAgentId == agent.Id || keepsConvoyToken)
        {
            state.CurrentOwnerState = BottleneckRuntimeState.OwnerState.Holding;
        }
        else
        {
            state.CurrentOwnerState = BottleneckRuntimeState.OwnerState.Idle;
        }

        return BuildBottleneckDecision(state, 1f, ApplyCommittedLaneBias(state, agent, laneBias, anchor), Vector3.zero, true);
    }

    private static Vector3 ResolvePortalCorridorAxis(PortalData portal, Vector3 desiredDirection)
    {
        if (portal.IsVerticalBoundary)
            return new Vector3(desiredDirection.x >= 0f ? 1f : -1f, 0f, 0f);

        return new Vector3(0f, 0f, desiredDirection.z >= 0f ? 1f : -1f);
    }

    private static bool TryResolveActiveBottleneck(
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        TileGoalKind goalKind,
        int downstreamPortalId,
        int currentSectorId,
        int currentX,
        int currentY,
        Vector3 desiredDirection,
        out CorridorBottleneckDescriptor descriptor)
    {
        descriptor = default;
        if (TryResolvePortalDrivenBottleneck(agent, goalKind, downstreamPortalId, currentSectorId, desiredDirection, out descriptor))
            return true;

        if (TryResolveNearbyPortalBottleneck(agent, currentSectorId, currentX, currentY, desiredDirection, out descriptor))
            return true;

        return TryResolveCorridorBottleneck(agent, tile, currentSectorId, currentX, currentY, desiredDirection, out descriptor);
    }

    private static bool TryResolveNearbyPortalBottleneck(
        AgentRuntimeData agent,
        int currentSectorId,
        int currentX,
        int currentY,
        Vector3 desiredDirection,
        out CorridorBottleneckDescriptor descriptor)
    {
        descriptor = default;
        if (agent == null || desiredDirection.sqrMagnitude <= 0.0001f)
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.PortalIds == null || handle.PortalIds.Length == 0)
            return false;

        for (int i = 0; i < handle.PortalIds.Length; i++)
        {
            int portalId = handle.PortalIds[i];
            if (!TryGetPortalById(_world, portalId, out PortalData portal) || !portal.IsNarrow)
                continue;

            if (portal.SectorAId != currentSectorId && portal.SectorBId != currentSectorId)
                continue;

            Vector3 toPortal = portal.WorldCenter - _world.GridToWorldCenter(currentX, currentY);
            toPortal.y = 0f;
            if (toPortal.magnitude > _world.CellSize * _world.SectorSizeInCells + Config.BottleneckInfluenceDistance)
                continue;

            Vector3 corridorAxis = ResolvePortalCorridorAxis(portal, desiredDirection);
            if (Vector3.Dot(desiredDirection.normalized, corridorAxis) <= 0.05f)
                continue;

            int direction = portal.SectorAId == currentSectorId ? 1 : -1;
            descriptor = BuildPortalBottleneckDescriptor(portalId, currentSectorId, direction, corridorAxis);
            return true;
        }

        return false;
    }

    private static bool TryResolvePortalDrivenBottleneck(
        AgentRuntimeData agent,
        TileGoalKind goalKind,
        int downstreamPortalId,
        int currentSectorId,
        Vector3 desiredDirection,
        out CorridorBottleneckDescriptor descriptor)
    {
        descriptor = default;
        if (goalKind == TileGoalKind.Portal && downstreamPortalId >= 0)
        {
            PortalData currentPortal = GetPortalById(_world, downstreamPortalId);
            if (!currentPortal.IsNarrow)
                return false;

            int direction = currentPortal.SectorAId == currentSectorId ? 1 : -1;
            Vector3 corridorAxis = ResolvePortalCorridorAxis(currentPortal, desiredDirection);
            descriptor = BuildPortalBottleneckDescriptor(downstreamPortalId, currentSectorId, direction, corridorAxis);
            return true;
        }

        PathHandle handle = agent.NavState.PathHandle;
        if (handle == null || handle.PortalIds == null || handle.PortalIds.Length == 0)
            return false;

        int previousPortalIndex = handle.CurrentSectorIndex - 1;
        if (previousPortalIndex < 0 || previousPortalIndex >= handle.PortalIds.Length)
            return false;

        int previousPortalId = handle.PortalIds[previousPortalIndex];
        if (!TryGetPortalById(_world, previousPortalId, out PortalData previousPortal))
            return false;
        if (!previousPortal.IsNarrow)
            return false;

        if (previousPortal.SectorAId != currentSectorId && previousPortal.SectorBId != currentSectorId)
            return false;

        int previousSectorId = handle.SectorIds[previousPortalIndex];
        int travelDirection;
        if (previousSectorId == previousPortal.SectorAId && currentSectorId == previousPortal.SectorBId)
            travelDirection = 1;
        else if (previousSectorId == previousPortal.SectorBId && currentSectorId == previousPortal.SectorAId)
            travelDirection = -1;
        else
            return false;

        if (agent.NavState.CurrentFlowDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 corridorAxis = previousPortal.IsVerticalBoundary
                ? new Vector3(travelDirection >= 0 ? 1f : -1f, 0f, 0f)
                : new Vector3(0f, 0f, travelDirection >= 0 ? 1f : -1f);
            if (Vector3.Dot(agent.NavState.CurrentFlowDirection, corridorAxis * travelDirection) <= 0.05f)
                return false;
        }

        descriptor = BuildPortalBottleneckDescriptor(
            previousPortalId,
            currentSectorId,
            travelDirection,
            previousPortal.IsVerticalBoundary
                ? new Vector3(travelDirection >= 0 ? 1f : -1f, 0f, 0f)
                : new Vector3(0f, 0f, travelDirection >= 0 ? 1f : -1f));
        return true;
    }

    private static Vector3 ResolveStableLaneBias(Vector3 corridorAxis)
    {
        Vector3 sideAxis = Vector3.Cross(Vector3.up, corridorAxis);
        if (sideAxis.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        return sideAxis.normalized * Config.LaneBiasStrength;
    }

    private static CorridorBottleneckDescriptor BuildPortalBottleneckDescriptor(int portalId, int sectorId, int direction, Vector3 corridorAxis)
    {
        PortalData portal = GetPortalById(_world, portalId);
        int bottleneckId = -1 - portalId;
        float influenceRadius = Mathf.Max(
            Config.BottleneckInfluenceDistance,
            _world.CellSize * _world.SectorSizeInCells + Config.BottleneckInfluenceDistance);
        return new CorridorBottleneckDescriptor(
            bottleneckId,
            portalId,
            sectorId,
            direction,
            corridorAxis.normalized,
            portal.WorldCenter,
            false,
            false,
            influenceRadius);
    }

    private static bool TryResolveCorridorBottleneck(
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int currentSectorId,
        int currentX,
        int currentY,
        Vector3 desiredDirection,
        out CorridorBottleneckDescriptor descriptor)
    {
        descriptor = default;
        if (agent == null || tile == null || desiredDirection.sqrMagnitude <= 0.0001f)
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle != null && handle.PortalIds != null)
        {
            for (int i = 0; i < handle.PortalIds.Length; i++)
            {
                if (TryGetPortalById(_world, handle.PortalIds[i], out PortalData portal) && portal.IsNarrow)
                    return false;
            }
        }

        Vector3 corridorAxis = Mathf.Abs(desiredDirection.x) >= Mathf.Abs(desiredDirection.z)
            ? new Vector3(Mathf.Sign(desiredDirection.x == 0f ? 1f : desiredDirection.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(desiredDirection.z == 0f ? 1f : desiredDirection.z));
        int axisMode = ResolveCorridorAxisMode(corridorAxis);
        if (axisMode == 0)
            return false;

        if (!TryMeasureCorridorSpan(currentX, currentY, axisMode, out int laneMin, out int laneMax, out int spanWidth))
            return false;

        bool narrow = spanWidth <= Config.PortalNarrowWidthCells;
        if (!narrow)
            return false;

        bool isLongCorridor = TryMeasureCorridorRunLength(currentX, currentY, axisMode, laneMin, laneMax, out int forwardRun, out int backwardRun);
        bool isCornerExit = !isLongCorridor && IsCornerExitBottleneck(tile, currentX, currentY, axisMode, laneMin, laneMax, corridorAxis);
        if (!isLongCorridor && !isCornerExit)
            return false;

        int runMin = axisMode == 1 ? currentX - backwardRun : currentY - backwardRun;
        int runMax = axisMode == 1 ? currentX + forwardRun : currentY + forwardRun;
        int laneCenter = (laneMin + laneMax) / 2;
        int runCenter = (runMin + runMax) / 2;
        int anchorX = axisMode == 1 ? runCenter : laneCenter;
        int anchorY = axisMode == 1 ? laneCenter : runCenter;
        Vector3 flow = agentFlowOrDesired(tile, currentX, currentY, desiredDirection);
        int direction = axisMode == 1
            ? (flow.x >= 0f ? 1 : -1)
            : (flow.z >= 0f ? 1 : -1);
        int bottleneckId = BuildCorridorBottleneckId(axisMode, laneMin, laneMax, runMin, runMax, isCornerExit);
        int runLength = runMax - runMin + 1;
        float influenceRadius = isLongCorridor
            ? Mathf.Max(Config.BottleneckInfluenceDistance, _world.CellSize * (runLength * 0.5f) + Config.BottleneckInfluenceDistance)
            : Mathf.Max(Config.BottleneckInfluenceDistance, _world.CellSize * 1.8f);
        descriptor = new CorridorBottleneckDescriptor(
            bottleneckId,
            -1,
            currentSectorId,
            direction,
            corridorAxis,
            _world.GridToWorldCenter(anchorX, anchorY),
            isLongCorridor,
            isCornerExit,
            influenceRadius);
        CorridorBottlenecks[bottleneckId] = descriptor;
        return true;
    }

    private static Vector3 agentFlowOrDesired(FlowTileCacheEntry tile, int currentX, int currentY, Vector3 desiredDirection)
    {
        if (tile != null)
        {
            Vector2 flow = tile.FlowDirections[tile.GetLocalIndex(currentX, currentY)];
            Vector3 flow3 = new Vector3(flow.x, 0f, flow.y);
            if (flow3.sqrMagnitude > 0.0001f)
                return flow3.normalized;
        }

        return desiredDirection.normalized;
    }

    private static int ResolveCorridorAxisMode(Vector3 corridorAxis)
    {
        if (Mathf.Abs(corridorAxis.x) > 0.5f)
            return 1;
        if (Mathf.Abs(corridorAxis.z) > 0.5f)
            return 2;
        return 0;
    }

    private static int BuildCorridorBottleneckId(int axisMode, int spanMin, int spanMax, int runMin, int runMax, bool isCornerExit)
    {
        unchecked
        {
            int hash = axisMode;
            hash = (hash * 397) ^ spanMin;
            hash = (hash * 397) ^ spanMax;
            hash = (hash * 397) ^ runMin;
            hash = (hash * 397) ^ runMax;
            hash = (hash * 397) ^ (isCornerExit ? 1 : 0);
            if (hash == 0)
                hash = 1;
            return hash;
        }
    }

    private static bool TryMeasureCorridorSpan(int currentX, int currentY, int axisMode, out int spanMin, out int spanMax, out int spanWidth)
    {
        if (!_world.IsWalkable(currentX, currentY))
            throw new InvalidOperationException($"TryMeasureCorridorSpan failed: current cell ({currentX},{currentY}) is not walkable.");

        spanMin = axisMode == 1 ? currentY : currentX;
        spanMax = spanMin;
        int fixedCoord = axisMode == 1 ? currentX : currentY;
        for (int i = 0; i < CorridorAxisProbeOffsets.Length; i++)
        {
            int offset = CorridorAxisProbeOffsets[i];
            int probe = spanMin + offset;
            int probeX = axisMode == 1 ? fixedCoord : probe;
            int probeY = axisMode == 1 ? probe : fixedCoord;
            if (!_world.IsWalkable(probeX, probeY))
                continue;

            if (axisMode == 1)
            {
                spanMin = Mathf.Min(spanMin, probeY);
                spanMax = Mathf.Max(spanMax, probeY);
            }
            else
            {
                spanMin = Mathf.Min(spanMin, probeX);
                spanMax = Mathf.Max(spanMax, probeX);
            }
        }

        while (true)
        {
            int next = spanMin - 1;
            int probeX = axisMode == 1 ? fixedCoord : next;
            int probeY = axisMode == 1 ? next : fixedCoord;
            if (!_world.IsWalkable(probeX, probeY) || !CanTraverseNeighborCells(_world, axisMode == 1 ? fixedCoord : spanMin, axisMode == 1 ? spanMin : fixedCoord, probeX, probeY))
                break;
            spanMin = next;
        }

        while (true)
        {
            int next = spanMax + 1;
            int probeX = axisMode == 1 ? fixedCoord : next;
            int probeY = axisMode == 1 ? next : fixedCoord;
            if (!_world.IsWalkable(probeX, probeY) || !CanTraverseNeighborCells(_world, axisMode == 1 ? fixedCoord : spanMax, axisMode == 1 ? spanMax : fixedCoord, probeX, probeY))
                break;
            spanMax = next;
        }

        spanWidth = spanMax - spanMin + 1;
        return spanWidth > 0;
    }

    private static bool TryMeasureCorridorRunLength(int currentX, int currentY, int axisMode, int spanMin, int spanMax, out int forwardRun, out int backwardRun)
    {
        forwardRun = MeasureCorridorRun(currentX, currentY, axisMode, spanMin, spanMax, 1);
        backwardRun = MeasureCorridorRun(currentX, currentY, axisMode, spanMin, spanMax, -1);
        int totalRun = forwardRun + backwardRun + 1;
        int threshold = Mathf.Max(4, Config.PortalNarrowWidthCells * 3 + 1);
        return totalRun >= threshold;
    }

    private static int MeasureCorridorRun(int currentX, int currentY, int axisMode, int spanMin, int spanMax, int direction)
    {
        int run = 0;
        while (true)
        {
            int step = run + 1;
            int probeX = axisMode == 1 ? currentX + step * direction : currentX;
            int probeY = axisMode == 1 ? currentY : currentY + step * direction;
            if (!_world.IsWalkable(probeX, probeY))
                break;

            if (!TryMeasureCorridorSpan(probeX, probeY, axisMode, out int probeMin, out int probeMax, out int probeWidth))
                break;
            if (probeWidth > Config.PortalNarrowWidthCells || probeMin != spanMin || probeMax != spanMax)
                break;

            int fromX = axisMode == 1 ? currentX + run * direction : currentX;
            int fromY = axisMode == 1 ? currentY : currentY + run * direction;
            if (!CanTraverseNeighborCells(_world, fromX, fromY, probeX, probeY))
                break;

            run++;
        }

        return run;
    }

    private static bool IsCornerExitBottleneck(
        FlowTileCacheEntry tile,
        int currentX,
        int currentY,
        int axisMode,
        int spanMin,
        int spanMax,
        Vector3 corridorAxis)
    {
        if (tile == null || !tile.HasLineOfSight[tile.GetLocalIndex(currentX, currentY)])
            return false;

        int lateralOpenCount = 0;
        int lateralDirection = 0;
        for (int side = -1; side <= 1; side += 2)
        {
            int probeX = axisMode == 1 ? currentX : currentX + side;
            int probeY = axisMode == 1 ? currentY + side : currentY;
            if (!_world.IsWalkable(probeX, probeY))
                continue;
            lateralOpenCount++;
            lateralDirection = side;
        }

        if (lateralOpenCount != 1)
            return false;

        int forwardX = axisMode == 1 ? currentX + (corridorAxis.x >= 0f ? 1 : -1) : currentX;
        int forwardY = axisMode == 1 ? currentY : currentY + (corridorAxis.z >= 0f ? 1 : -1);
        if (!_world.IsWalkable(forwardX, forwardY))
            return false;

        int cornerProbeX = axisMode == 1 ? forwardX : currentX + lateralDirection;
        int cornerProbeY = axisMode == 1 ? currentY + lateralDirection : forwardY;
        return !_world.IsWalkable(cornerProbeX, cornerProbeY);
    }

    private static Vector3 ResolveBottleneckAnchor(BottleneckRuntimeState state, CorridorBottleneckDescriptor descriptor)
    {
        return descriptor.AnchorWorld;
    }

    private static BottleneckDecision BuildBottleneckDecision(
        BottleneckRuntimeState state,
        float speedScale,
        Vector3 laneBias,
        Vector3 queueBias,
        bool enforceLaneCommitment)
    {
        return new BottleneckDecision(
            speedScale,
            laneBias,
            queueBias,
            enforceLaneCommitment,
            state.ConvoyTokenAgentId,
            state.PriorityOverrideAgentId,
            state.CurrentOwnerState);
    }

    private static void UpdateBottleneckPriorityOverride(BottleneckRuntimeState state)
    {
        state.PriorityOverrideAgentId = -1;
        int bestPriority = int.MinValue;
        for (int i = 0; i < state.WaitingAgentsOrdered.Count; i++)
        {
            int waitingAgentId = state.WaitingAgentsOrdered[i];
            if (!Agents.TryGetValue(waitingAgentId, out AgentRuntimeData waitingAgent))
                continue;

            int priority = ResolveBottleneckPriorityScore(waitingAgent);
            if (priority > bestPriority && priority > 1)
            {
                bestPriority = priority;
                state.PriorityOverrideAgentId = waitingAgentId;
            }
        }
    }

    private static bool SharesConvoyToken(BottleneckRuntimeState state, AgentRuntimeData agent)
    {
        if (state.ConvoyTokenAgentId < 0 || agent == null)
            return false;
        if (!Agents.TryGetValue(state.ConvoyTokenAgentId, out AgentRuntimeData tokenAgent))
            return false;

        if (tokenAgent.GroupId >= 0 && tokenAgent.GroupId == agent.GroupId && tokenAgent.GroupId != -1)
            return true;

        return tokenAgent.IsLeader && agent.GroupId == tokenAgent.Id;
    }

    private static int ResolveConvoyTokenAgent(BottleneckRuntimeState state, AgentRuntimeData agent)
    {
        if (agent == null)
            return -1;
        if (agent.IsLeader)
            return agent.Id;
        if (agent.GroupId >= 0)
            return agent.GroupId;
        return agent.Id;
    }

    private static int ResolveBottleneckPriorityScore(AgentRuntimeData agent)
    {
        int score = 1;
        if (agent == null)
            return score;
        if (agent.IsLeader)
            score += 3;

        switch (agent.State)
        {
            case GroupMoveCoordinator.AgentState.Combat:
                score += 2;
                break;
            case GroupMoveCoordinator.AgentState.Follow:
                score += 1;
                break;
        }

        return score;
    }

    private static Vector3 ResolveQueueBias(
        BottleneckRuntimeState state,
        CorridorBottleneckDescriptor descriptor,
        Vector3 anchor,
        Vector3 laneBias,
        AgentRuntimeData agent,
        float time)
    {
        int queueRank = ResolveWaitingQueueRank(state, agent.Id);

        float slotSpacing = Mathf.Max(agent.Radius * 2.2f, _world.CellSize * 0.9f);
        float bottleneckClearance = Mathf.Max(agent.Radius * 1.35f, _world.CellSize * 0.75f);
        Vector3 queueTarget = anchor
                              - descriptor.CorridorAxis * (bottleneckClearance + queueRank * slotSpacing)
                              + laneBias.normalized * Mathf.Max(agent.Radius * 0.6f, _world.CellSize * 0.15f);
        Vector3 toSlot = queueTarget - agent.Position;
        toSlot.y = 0f;
        if (toSlot.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        float slotSpeed = Mathf.Min(toSlot.magnitude, Mathf.Max(0.6f, slotSpacing));
        return toSlot.normalized * slotSpeed;
    }

    private static Vector3 ApplyCommittedLaneBias(BottleneckRuntimeState state, AgentRuntimeData agent, Vector3 laneBias, Vector3 anchor)
    {
        if (laneBias.sqrMagnitude <= 0.0001f)
            return laneBias;

        Vector3 laneAxis = laneBias.normalized;
        if (!state.AgentLaneSigns.TryGetValue(agent.Id, out float sign))
        {
            if (agent.NavState.BottleneckLaneAxisMode == state.CorridorAxisMode
                && Mathf.Abs(agent.NavState.BottleneckLaneSign) > 0.5f)
            {
                sign = Mathf.Sign(agent.NavState.BottleneckLaneSign);
            }
            else
            {
                sign = Vector3.Dot(agent.Position - anchor, laneAxis) >= 0f ? 1f : -1f;
                if (Mathf.Approximately(sign, 0f))
                    sign = 1f;
            }

            state.AgentLaneSigns[agent.Id] = sign;
        }

        agent.NavState.BottleneckLaneAxisMode = state.CorridorAxisMode;
        agent.NavState.BottleneckLaneSign = sign;
        return laneAxis * (laneBias.magnitude * sign);
    }

    private static bool HasBottleneckTrafficContext(AgentRuntimeData self, CorridorBottleneckDescriptor descriptor, Vector3 anchor)
    {
        float contextRadius = descriptor.InfluenceRadius * 1.35f;
        float lateralTolerance = Mathf.Max(self.Radius * 4f, _world.CellSize * 1.25f);
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData other = pair.Value;
            if (other.Id == self.Id || other.IgnoreAgentCollision)
                continue;

            Vector3 toOther = other.Position - self.Position;
            toOther.y = 0f;
            if (toOther.sqrMagnitude > contextRadius * contextRadius)
                continue;

            Vector3 otherFromAnchor = other.Position - anchor;
            otherFromAnchor.y = 0f;
            if (otherFromAnchor.sqrMagnitude > contextRadius * contextRadius)
                continue;

            float lateral = Vector3.ProjectOnPlane(toOther, descriptor.CorridorAxis).magnitude;
            if (lateral > lateralTolerance)
                continue;

            return true;
        }

        return false;
    }

    private static bool HasIncomingDirectionalTraffic(AgentRuntimeData self, CorridorBottleneckDescriptor descriptor, Vector3 anchor, int direction)
    {
        float forwardWindow = descriptor.InfluenceRadius * 1.75f;
        float lateralTolerance = Mathf.Max(self.Radius * 3.5f, _world.CellSize * 1.15f);
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            AgentRuntimeData other = pair.Value;
            if (other.Id == self.Id || other.IgnoreAgentCollision)
                continue;

            Vector3 offset = other.Position - anchor;
            offset.y = 0f;
            float lateral = Vector3.ProjectOnPlane(offset, descriptor.CorridorAxis).magnitude;
            if (lateral > lateralTolerance)
                continue;

            float forward = Vector3.Dot(offset, descriptor.CorridorAxis) * direction;
            if (forward < _world.CellSize * 0.1f || forward > forwardWindow)
                continue;

            Vector3 flowDirection = other.NavState.CurrentFlowDirection;
            if (flowDirection.sqrMagnitude <= 0.0001f)
                flowDirection = other.NavState.DesiredVelocity.normalized;
            if (flowDirection.sqrMagnitude <= 0.0001f)
                continue;

            if (Vector3.Dot(flowDirection, descriptor.CorridorAxis * direction) <= 0.45f)
                continue;

            return true;
        }

        return false;
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
        long steeringSectionTicks = Stopwatch.GetTimestamp();
        List<AgentRuntimeData> nearbyAgents = CollectNearbyDynamicNeighbors(self, avoidRadius);
        _perf.NeighborCollectTicks += Stopwatch.GetTimestamp() - steeringSectionTicks;
        _perf.NeighborChecks += nearbyAgents.Count;
        for (int i = 0; i < nearbyAgents.Count; i++)
        {
            AgentRuntimeData other = nearbyAgents[i];
            if (!ShouldAvoidAsDynamicNeighbor(selfContext, self, other))
                continue;

            long avoidStartTicks = Stopwatch.GetTimestamp();
            Vector3 awayContribution = ResolvePredictiveAvoidanceContribution(
                self,
                other,
                desiredDirection,
                desiredVelocity,
                maxSpeed,
                avoidRadius,
                out string avoidDebug,
                out Vector3 tangentContribution,
                out Vector3 brakeContribution,
                out float currentDistance,
                out float separationDistance,
                out float timeToClosest,
                out AvoidanceEncounterType encounterType,
                out float riskScore,
                out float crossingScore,
                out float imminenceScore);
            _perf.NeighborAvoidTicks += Stopwatch.GetTimestamp() - avoidStartTicks;
            Vector3 totalContribution = awayContribution + tangentContribution + brakeContribution;
            agentAvoidance += totalContribution;

            if (debugMove)
            {
                TryInsertTopAvoidContribution(
                    topAvoidLogs,
                    topAvoidScores,
                    totalContribution.sqrMagnitude,
                    $"other={other.CharacterKey} otherType={other.EntityTypeName} otherMoveComp={other.MoveCompTypeName} " +
                    $"otherSource={other.RegistrationSource} synthetic={other.IsSyntheticRegistration} " +
                    $"otherPos={other.Position} otherVel={ResolveNeighborPredictedVelocity(other)} " +
                    $"encounter={encounterType} dist={currentDistance:F3} sep={separationDistance:F3} ttc={timeToClosest:F3} " +
                    $"risk={riskScore:F3} crossing={crossingScore:F3} imminence={imminenceScore:F3} " +
                    $"{avoidDebug} " +
                    $"awayContrib={awayContribution} tangentContrib={tangentContribution} brakeContrib={brakeContribution} total={totalContribution}");
            }
        }

        Vector3 boundaryAvoidance = Vector3.zero;
        Vector3 edgeNormal = Vector3.zero;
        float edgeDistance = float.MaxValue;
        if (tile != null)
        {
            long boundaryStartTicks = Stopwatch.GetTimestamp();
            boundaryAvoidance = ResolveBoundaryAvoidance(
                self.Position,
                desiredDirection,
                self.Radius,
                maxSpeed,
                self.AgentTypeId,
                out edgeNormal,
                out edgeDistance);
            _perf.BoundaryTicks += Stopwatch.GetTimestamp() - boundaryStartTicks;
        }

        Vector3 clampedAgentAvoidance = ClampAgentAvoidance(agentAvoidance, desiredDirection, maxSpeed);

        Vector3 baseVelocity = desiredVelocity * bottleneck.SpeedScale + bottleneck.QueueBias;
        Vector3 avoidance = clampedAgentAvoidance + boundaryAvoidance;
        Vector3 laneVelocity = bottleneck.LaneBias * maxSpeed;
        Vector3 result = baseVelocity + avoidance * 0.75f + laneVelocity;
        result = ApplyEdgeRecovery(result, desiredDirection, edgeNormal, edgeDistance, self.Radius, maxSpeed);
        result = ApplyLaneCommitment(result, laneVelocity, bottleneck, edgeNormal, edgeDistance, self.Radius, maxSpeed);

        Vector3 toGoal = goalPosition - self.Position;
        toGoal.y = 0f;
        if (bottleneck.SpeedScale > 0f && toGoal.sqrMagnitude > 0.0001f && Vector3.Dot(result, toGoal) < 0f)
            result = desiredVelocity * 0.35f + avoidance * 0.25f;

        Vector3 clampedResult = Vector3.ClampMagnitude(result, maxSpeed);
        StoreSteeringDiagnostic(
            self,
            goalPosition,
            desiredDirection,
            desiredVelocity,
            desiredResolution,
            baseVelocity,
            agentAvoidance,
            clampedAgentAvoidance,
            boundaryAvoidance,
            laneVelocity,
            result,
            clampedResult,
            edgeNormal,
            edgeDistance,
            maxSpeed);

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
        return clampedResult;
    }

    private static void StoreSteeringDiagnostic(
        AgentRuntimeData agent,
        Vector3 goalPosition,
        Vector3 desiredDirection,
        Vector3 desiredVelocity,
        DesiredDirectionResolution desiredResolution,
        Vector3 baseVelocity,
        Vector3 agentAvoidance,
        Vector3 clampedAgentAvoidance,
        Vector3 boundaryAvoidance,
        Vector3 laneVelocity,
        Vector3 resultPreClamp,
        Vector3 result,
        Vector3 edgeNormal,
        float edgeDistance,
        float maxSpeed)
    {
        if (agent == null)
            throw new InvalidOperationException("StoreSteeringDiagnostic failed: agent is null.");

        AgentNavState navState = agent.NavState;
        navState.LastSteeringFrame = GetFrameCount();
        navState.LastSteeringGoal = goalPosition;
        navState.LastSteeringDesiredDirection = desiredDirection;
        navState.LastSteeringDesiredVelocity = desiredVelocity;
        navState.LastSteeringBaseVelocity = baseVelocity;
        navState.LastSteeringAgentAvoidance = agentAvoidance;
        navState.LastSteeringClampedAgentAvoidance = clampedAgentAvoidance;
        navState.LastSteeringBoundaryAvoidance = boundaryAvoidance;
        navState.LastSteeringLaneVelocity = laneVelocity;
        navState.LastSteeringResultPreClamp = resultPreClamp;
        navState.LastSteeringResult = result;
        navState.LastSteeringEdgeNormal = edgeNormal;
        navState.LastSteeringEdgeDistance = edgeDistance;
        navState.LastSteeringDesiredSource = desiredResolution.Source;
        navState.LastSteeringFlow = desiredResolution.Flow;
        navState.LastSteeringHasLineOfSight = desiredResolution.HasLineOfSight;
        navState.LastSteeringTileTarget = desiredResolution.TileTargetPosition;
        navState.LastSteeringIntegration = desiredResolution.Integration;
        navState.LastSteeringMaxSpeed = maxSpeed;
    }

    private static Vector3 ApplyLaneCommitment(
        Vector3 velocity,
        Vector3 laneVelocity,
        BottleneckDecision bottleneck,
        Vector3 edgeNormal,
        float edgeDistance,
        float radius,
        float maxSpeed)
    {
        if (!bottleneck.EnforceLaneCommitment || laneVelocity.sqrMagnitude <= 0.0001f || maxSpeed <= 0.0001f)
            return velocity;

        Vector3 laneAxis = laneVelocity.normalized;
        float currentLaneSpeed = Vector3.Dot(velocity, laneAxis);
        float desiredLaneSpeed = Vector3.Dot(laneVelocity, laneAxis);
        float minLaneSpeed = desiredLaneSpeed * 0.85f;
        if (desiredLaneSpeed >= 0f)
        {
            if (currentLaneSpeed >= minLaneSpeed)
                return velocity;
        }
        else
        {
            if (currentLaneSpeed <= minLaneSpeed)
                return velocity;
        }

        Vector3 correction = laneAxis * (minLaneSpeed - currentLaneSpeed);
        velocity += correction;
        return Vector3.ClampMagnitude(velocity, maxSpeed);
    }

    private static Vector3 ResolvePredictiveAvoidanceContribution(
        AgentRuntimeData self,
        AgentRuntimeData other,
        Vector3 desiredDirection,
        Vector3 desiredVelocity,
        float maxSpeed,
        float avoidRadius,
        out string debugSummary,
        out Vector3 tangentContribution,
        out Vector3 brakeContribution,
        out float currentDistance,
        out float separationDistance,
        out float timeToClosest,
        out AvoidanceEncounterType encounterType,
        out float riskScore,
        out float crossingScore,
        out float imminenceScore)
    {
        debugSummary = string.Empty;
        tangentContribution = Vector3.zero;
        brakeContribution = Vector3.zero;
        currentDistance = 0f;
        separationDistance = 0f;
        timeToClosest = 0f;
        encounterType = AvoidanceEncounterType.None;
        riskScore = 0f;
        crossingScore = 0f;
        imminenceScore = 0f;

        Vector3 relativePos = other.Position - self.Position;
        relativePos.y = 0f;
        currentDistance = relativePos.magnitude;
        if (currentDistance > avoidRadius || currentDistance <= 0.0001f)
            return Vector3.zero;

        Vector3 otherVelocity = ResolveNeighborPredictedVelocity(other);
        Vector3 relativeVel = otherVelocity - desiredVelocity;
        relativeVel.y = 0f;
        float relativeSpeedSq = relativeVel.sqrMagnitude;
        float relativeSpeed = Mathf.Sqrt(relativeSpeedSq);
        float predictionHorizon = relativeSpeed > 0.0001f
            ? Mathf.Clamp(currentDistance / relativeSpeed, Config.CrowdPredictionTime, Config.CrowdPredictionTime * 2f)
            : Config.CrowdPredictionTime;
        timeToClosest = relativeSpeedSq > 0.0001f
            ? Mathf.Clamp(-Vector3.Dot(relativePos, relativeVel) / relativeSpeedSq, 0f, predictionHorizon)
            : 0f;

        Vector3 closestOffset = relativePos + relativeVel * timeToClosest;
        separationDistance = closestOffset.magnitude;
        float combinedRadius = self.Radius + other.Radius;

        if (separationDistance > avoidRadius && currentDistance > avoidRadius)
            return Vector3.zero;

        float overlap = Mathf.Clamp01((combinedRadius - separationDistance) / Mathf.Max(combinedRadius, 0.001f));
        bool approaching = Vector3.Dot(relativePos, relativeVel) < -0.0001f;
        Vector3 away = separationDistance > 0.0001f ? -closestOffset.normalized : -relativePos.normalized;
        Vector3 tangent = ResolveStableAvoidanceTangent(self, other, desiredDirection, relativePos, separationDistance, overlap, approaching);
        Vector3 brake = desiredDirection.sqrMagnitude > 0.0001f ? -desiredDirection : Vector3.zero;
        float timeWeight = approaching
            ? Mathf.Clamp01(1f - timeToClosest / Mathf.Max(predictionHorizon, 0.001f))
            : 0f;
        float separationBuffer = Mathf.Max(combinedRadius * 0.9f, avoidRadius * 0.18f);
        float separationWeight = Mathf.Clamp01(1f - Mathf.Max(0f, separationDistance - combinedRadius) / Mathf.Max(separationBuffer, 0.001f));
        float primaryRisk = overlap > 0f
            ? Mathf.Max(overlap, separationWeight)
            : Mathf.Sqrt(Mathf.Max(0f, timeWeight * separationWeight));
        float velocityAlignment = 0.5f;
        if (desiredDirection.sqrMagnitude > 0.0001f && otherVelocity.sqrMagnitude > 0.0001f)
            velocityAlignment = Mathf.Clamp01((-Vector3.Dot(desiredDirection, otherVelocity.normalized) + 1f) * 0.5f);
        crossingScore = Mathf.Clamp01(relativeSpeed / Mathf.Max(maxSpeed + otherVelocity.magnitude, 0.001f));
        float ttcScore = timeWeight;
        bool sameLaneFlow = desiredDirection.sqrMagnitude > 0.0001f
                            && otherVelocity.sqrMagnitude > 0.0001f
                            && Vector3.Dot(desiredDirection, otherVelocity.normalized) > 0.65f;
        encounterType = ClassifyAvoidanceEncounter(desiredDirection, desiredVelocity, otherVelocity, relativePos, overlap, sameLaneFlow);
        float proximity = Mathf.Clamp01(1f - Mathf.Min(currentDistance, separationDistance) / avoidRadius);
        float awayWeight;
        float tangentWeight;
        float brakeWeight;
        ResolveEncounterWeights(
            self,
            other,
            encounterType,
            approaching,
            proximity,
            overlap,
            separationWeight,
            primaryRisk,
            crossingScore,
            velocityAlignment,
            out awayWeight,
            out tangentWeight,
            out brakeWeight);

        Vector3 awayContribution = away * awayWeight * maxSpeed;
        tangentContribution = tangent * tangentWeight * maxSpeed;
        brakeContribution = brake * brakeWeight * maxSpeed;
        riskScore = primaryRisk;
        imminenceScore = ttcScore;
        debugSummary =
            $"approaching={approaching} horizon={predictionHorizon:F3} relSpeed={relativeSpeed:F3} " +
            $"encounter={encounterType} " +
            $"overlap={overlap:F3} proximity={proximity:F3} sepScore={separationWeight:F3} ttcScore={ttcScore:F3} primaryRisk={primaryRisk:F3} " +
            $"align={velocityAlignment:F3} sameLane={sameLaneFlow}";
        return awayContribution;
    }

    private static AvoidanceEncounterType ClassifyAvoidanceEncounter(
        Vector3 desiredDirection,
        Vector3 desiredVelocity,
        Vector3 otherVelocity,
        Vector3 relativePos,
        float overlap,
        bool sameLaneFlow)
    {
        if (overlap > 0.35f)
            return AvoidanceEncounterType.StaticOverlap;

        if (desiredDirection.sqrMagnitude <= 0.0001f || otherVelocity.sqrMagnitude <= 0.0001f)
            return sameLaneFlow ? AvoidanceEncounterType.SameLaneFollow : AvoidanceEncounterType.Crossing;

        float headingDot = Vector3.Dot(desiredDirection, otherVelocity.normalized);
        Vector3 lateralOffset = Vector3.ProjectOnPlane(relativePos, desiredDirection);
        bool nearLane = lateralOffset.magnitude <= Mathf.Max(_world.CellSize * 0.75f, 0.45f);

        if (headingDot > 0.72f && nearLane)
        {
            float forwardOffset = Vector3.Dot(relativePos, desiredDirection);
            return forwardOffset >= 0f ? AvoidanceEncounterType.SameLaneFollow : AvoidanceEncounterType.Overtaking;
        }

        if (headingDot < -0.45f && nearLane)
            return AvoidanceEncounterType.SameLaneOpposing;

        return AvoidanceEncounterType.Crossing;
    }

    private static void ResolveEncounterWeights(
        AgentRuntimeData self,
        AgentRuntimeData other,
        AvoidanceEncounterType encounterType,
        bool approaching,
        float proximity,
        float overlap,
        float separationWeight,
        float primaryRisk,
        float crossingScore,
        float velocityAlignment,
        out float awayWeight,
        out float tangentWeight,
        out float brakeWeight)
    {
        awayWeight = proximity * (0.12f + separationWeight * 1.15f + overlap * 2.2f + primaryRisk * 0.95f);
        tangentWeight = 0f;
        brakeWeight = 0f;
        if (!approaching)
            return;

        float leaderBias = ResolveLeaderAvoidanceBias(self, other);

        switch (encounterType)
        {
            case AvoidanceEncounterType.SameLaneFollow:
                tangentWeight = primaryRisk * 0.02f * Mathf.Lerp(1f, leaderBias, 0.35f);
                brakeWeight = primaryRisk * (0.28f + separationWeight * 0.22f) * leaderBias;
                break;
            case AvoidanceEncounterType.SameLaneOpposing:
                tangentWeight = primaryRisk * (0.18f + crossingScore * 0.6f) * Mathf.Lerp(1f, leaderBias, 0.45f);
                brakeWeight = primaryRisk * (0.12f + velocityAlignment * 0.16f) * Mathf.Lerp(1f, leaderBias, 0.6f);
                awayWeight += proximity * primaryRisk * 0.18f * Mathf.Lerp(1f, leaderBias, 0.35f);
                break;
            case AvoidanceEncounterType.Overtaking:
                tangentWeight = primaryRisk * (0.08f + (1f - crossingScore) * 0.16f) * Mathf.Lerp(1f, leaderBias, 0.25f);
                brakeWeight = primaryRisk * (0.22f + separationWeight * 0.16f) * Mathf.Lerp(1f, leaderBias, 0.7f);
                break;
            case AvoidanceEncounterType.StaticOverlap:
                tangentWeight = primaryRisk * 0.1f * Mathf.Lerp(1f, leaderBias, 0.25f);
                brakeWeight = primaryRisk * 0.18f * Mathf.Lerp(1f, leaderBias, 0.4f);
                awayWeight += overlap * 1.35f;
                break;
            default:
                tangentWeight = primaryRisk * (0.12f + crossingScore * 0.88f + velocityAlignment * 0.12f) * Mathf.Lerp(1f, leaderBias, 0.4f);
                brakeWeight = primaryRisk * (0.04f + (1f - crossingScore) * 0.12f + velocityAlignment * 0.08f) * Mathf.Lerp(1f, leaderBias, 0.5f);
                break;
        }
    }

    private static float ResolveLeaderAvoidanceBias(AgentRuntimeData self, AgentRuntimeData other)
    {
        if (self == null || other == null)
            return 1f;

        if (self.IsLeader == other.IsLeader)
            return 1f;

        return self.IsLeader ? 0.82f : 1.08f;
    }

    private static void RefreshBottleneckWaitingOrder(BottleneckRuntimeState state)
    {
        state.WaitingAgentsOrdered.Clear();
        foreach (KeyValuePair<int, float> pair in state.WaitingStartTimes)
        {
            if (!Agents.TryGetValue(pair.Key, out AgentRuntimeData waitingAgent))
                continue;
            if (waitingAgent.NavState.CurrentSectorId < 0)
                continue;

            state.WaitingAgentsOrdered.Add(pair.Key);
        }

        state.WaitingAgentsOrdered.Sort((leftId, rightId) =>
        {
            int leftPriority = Agents.TryGetValue(leftId, out AgentRuntimeData leftAgent)
                ? ResolveBottleneckPriorityScore(leftAgent)
                : 1;
            int rightPriority = Agents.TryGetValue(rightId, out AgentRuntimeData rightAgent)
                ? ResolveBottleneckPriorityScore(rightAgent)
                : 1;
            if (leftPriority != rightPriority)
                return rightPriority.CompareTo(leftPriority);

            float leftTime = state.WaitingStartTimes[leftId];
            float rightTime = state.WaitingStartTimes[rightId];
            int timeCompare = leftTime.CompareTo(rightTime);
            if (timeCompare != 0)
                return timeCompare;
            return leftId.CompareTo(rightId);
        });
    }

    private static int ResolveWaitingQueueRank(BottleneckRuntimeState state, int agentId)
    {
        for (int i = 0; i < state.WaitingAgentsOrdered.Count; i++)
        {
            if (state.WaitingAgentsOrdered[i] == agentId)
                return i;
        }

        return 0;
    }

    private static Vector3 ResolveStableAvoidanceTangent(
        AgentRuntimeData self,
        AgentRuntimeData other,
        Vector3 desiredDirection,
        Vector3 relativePos,
        float separationDistance,
        float overlap,
        bool approaching)
    {
        if (!approaching && overlap <= 0.0001f && separationDistance > Mathf.Max(self.Radius + other.Radius, 0.001f))
            return Vector3.zero;

        if (desiredDirection.sqrMagnitude > 0.0001f
            && other.NavState.CurrentFlowDirection.sqrMagnitude > 0.0001f
            && Vector3.Dot(desiredDirection, other.NavState.CurrentFlowDirection) > 0.65f)
        {
            if (overlap <= 0.0001f && separationDistance > Mathf.Max(self.Radius + other.Radius * 1.5f, 0.001f))
                return Vector3.zero;
        }

        Vector3 lateralAxis = Vector3.Cross(Vector3.up, relativePos).normalized;
        if (lateralAxis.sqrMagnitude <= 0.0001f)
            lateralAxis = desiredDirection.sqrMagnitude > 0.0001f
                ? Vector3.Cross(Vector3.up, desiredDirection).normalized
                : Vector3.zero;
        if (lateralAxis.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        if (desiredDirection.sqrMagnitude > 0.0001f && Vector3.Dot(lateralAxis, desiredDirection) < 0f)
            lateralAxis = -lateralAxis;

        return lateralAxis;
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
        if (_world == null)
            throw new InvalidOperationException("ResolveBoundaryAvoidance failed: world is null.");

        if (!TryResolveNavEdgeData(position, agentTypeId, out edgeNormal, out edgeDistance)
            && !TryResolveGridEdgeData(position, desiredDirection, out edgeNormal, out edgeDistance))
        {
            return Vector3.zero;
        }

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

        if (!NavMesh.FindClosestEdge(navHit.position, out NavMeshHit edgeHit, filter))
            return false;

        edgeNormal = edgeHit.normal;
        edgeNormal.y = 0f;
        if (edgeNormal.sqrMagnitude <= 0.0001f)
            return false;

        edgeNormal.Normalize();
        edgeDistance = edgeHit.distance;
        return true;
    }

    private static bool TryResolveGridEdgeData(Vector3 position, Vector3 desiredDirection, out Vector3 edgeNormal, out float edgeDistance)
    {
        edgeNormal = Vector3.zero;
        edgeDistance = float.MaxValue;

        if (_world == null)
            throw new InvalidOperationException("TryResolveGridEdgeData failed: world is null.");

        if (!_world.WorldToGrid(position, out int cellX, out int cellY))
            return false;

        float cellMinX = _world.Origin.x + cellX * _world.CellSize;
        float cellMaxX = cellMinX + _world.CellSize;
        float cellMinZ = _world.Origin.z + cellY * _world.CellSize;
        float cellMaxZ = cellMinZ + _world.CellSize;

        Vector3 accumulatedNormal = Vector3.zero;
        TryAccumulateGridEdge(cellX + 1, cellY, cellMaxX - position.x, Vector3.left, desiredDirection, ref accumulatedNormal, ref edgeDistance);
        TryAccumulateGridEdge(cellX - 1, cellY, position.x - cellMinX, Vector3.right, desiredDirection, ref accumulatedNormal, ref edgeDistance);
        TryAccumulateGridEdge(cellX, cellY + 1, cellMaxZ - position.z, Vector3.back, desiredDirection, ref accumulatedNormal, ref edgeDistance);
        TryAccumulateGridEdge(cellX, cellY - 1, position.z - cellMinZ, Vector3.forward, desiredDirection, ref accumulatedNormal, ref edgeDistance);

        if (edgeDistance == float.MaxValue || accumulatedNormal.sqrMagnitude <= 0.0001f)
            return false;

        edgeNormal = accumulatedNormal.normalized;
        return true;
    }

    private static void TryAccumulateGridEdge(
        int neighborX,
        int neighborY,
        float distanceToBoundary,
        Vector3 inwardNormal,
        Vector3 desiredDirection,
        ref Vector3 accumulatedNormal,
        ref float edgeDistance)
    {
        if (_world.IsWalkable(neighborX, neighborY))
            return;

        float headingPressure = desiredDirection.sqrMagnitude > 0.0001f
            ? Vector3.Dot(desiredDirection, inwardNormal)
            : 0f;
        if (headingPressure <= 0.05f && distanceToBoundary > _world.CellSize * 0.18f)
            return;

        float clampedDistance = Mathf.Max(0.001f, distanceToBoundary);
        float weight = (1f / clampedDistance) * Mathf.Max(0.35f, headingPressure);
        accumulatedNormal += inwardNormal * weight;
        edgeDistance = Mathf.Min(edgeDistance, distanceToBoundary);
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

        int frameCount = GetFrameCount();
        if (agent.NavState.ResolvedVelocityFrame != frameCount)
            agent.NavState.PreviousResolvedVelocity = agent.NavState.ResolvedVelocity;

        agent.NavState.HasGoal = true;
        agent.NavState.LastGoalWorld = goalPosition;
        agent.NavState.DesiredVelocity = desiredVelocity;
        agent.NavState.ResolvedVelocity = resolvedVelocity;
        agent.NavState.ResolvedVelocityFrame = frameCount;
        agent.NavState.CurrentFlowDirection = resolvedVelocity.sqrMagnitude > 0.0001f
            ? resolvedVelocity.normalized
            : desiredVelocity.sqrMagnitude > 0.0001f ? desiredVelocity.normalized : Vector3.zero;
        agent.HasNavigationIntent = desiredVelocity.sqrMagnitude > 0.0001f || resolvedVelocity.sqrMagnitude > 0.0001f;
        if (agent.HasNavigationIntent)
            agent.LastAvoidanceActiveFrame = frameCount;
    }

    private static void SetCurrentFrameNavigationIntent(AgentRuntimeData agent, Vector3 goalPosition, Vector3 desiredVelocity)
    {
        if (agent == null)
            throw new InvalidOperationException("SetCurrentFrameNavigationIntent failed: agent is null.");

        int frameCount = GetFrameCount();
        agent.NavState.HasGoal = true;
        agent.NavState.LastGoalWorld = goalPosition;
        agent.NavState.DesiredVelocity = desiredVelocity;
        agent.NavState.CurrentFlowDirection = desiredVelocity.sqrMagnitude > 0.0001f ? desiredVelocity.normalized : Vector3.zero;
        agent.HasNavigationIntent = desiredVelocity.sqrMagnitude > 0.0001f;
        if (agent.HasNavigationIntent)
            agent.LastAvoidanceActiveFrame = frameCount;
    }

    private static bool TryResolveStableGoalCell(
        AgentRuntimeData agent,
        IEntityContext self,
        Vector3 rawGoalPosition,
        out int goalX,
        out int goalY,
        out Vector3 stableGoalPosition)
    {
        goalX = 0;
        goalY = 0;
        stableGoalPosition = rawGoalPosition;
        if (!TryResolveGoalCell(_world, rawGoalPosition, out int rawGoalX, out int rawGoalY))
            return false;
        if (!_world.TryGetSectorId(rawGoalX, rawGoalY, out int rawGoalSectorId))
            return false;
        if (!TryResolveStartCellForReachability(self, out int startX, out int startY, out int startIsland))
            return false;
        if (!TryResolveReachableGoalCell(
                self,
                rawGoalPosition,
                rawGoalX,
                rawGoalY,
                startX,
                startY,
                startIsland,
                out int reachableGoalX,
                out int reachableGoalY,
                out Vector3 reachableGoalWorld,
                out int reachableGoalSectorId))
        {
            return false;
        }

        IEntityContext currentTarget = self?.TargetComp?.CurrentTarget;
        if (currentTarget == null || ReferenceEquals(currentTarget, self))
        {
            _perf.StableGoalRaw++;
            ClearStableGoal(agent);
            goalX = reachableGoalX;
            goalY = reachableGoalY;
            stableGoalPosition = reachableGoalWorld;
            return true;
        }

        int targetId = ResolveAgentId(currentTarget);
        int rawGoalCellIndex = _world.GetIndex(rawGoalX, rawGoalY);
        MovingTargetAnchorKey anchorKey = new MovingTargetAnchorKey(targetId);
        if (!MovingTargetAnchors.TryGetValue(anchorKey, out MovingTargetAnchor anchor))
        {
            anchor = new MovingTargetAnchor { Key = anchorKey };
            MovingTargetAnchors.Add(anchorKey, anchor);
        }

        bool hasStableGoal = anchor.ActiveGoalX >= 0
                             && anchor.ActiveGoalY >= 0
                             && anchor.ActiveWorldVersion == _world.Version;
        int cellDelta = hasStableGoal
            ? Mathf.Max(Mathf.Abs(rawGoalX - anchor.RawGoalX), Mathf.Abs(rawGoalY - anchor.RawGoalY))
            : int.MaxValue;
        bool shouldRefresh = !hasStableGoal
                             || cellDelta > 0;

        if (shouldRefresh)
        {
            if (!hasStableGoal)
                _perf.StableGoalRefreshInitial++;
            else
                _perf.StableGoalRefreshCellDelta++;

            SetMovingTargetActiveGoal(anchor, rawGoalX, rawGoalY, reachableGoalX, reachableGoalY, reachableGoalSectorId, reachableGoalWorld);
        }
        else
        {
            _perf.StableGoalReuse++;
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && self != null
            && GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowStableGoalDiag] key={self.CharacterKey} target={currentTarget.CharacterKey} targetPos={currentTarget.Position} " +
                $"rawGoal={rawGoalPosition} rawCell=({rawGoalX},{rawGoalY}) rawIsland={ResolveIslandIdForDiagnostics(_world, rawGoalX, rawGoalY)} " +
                $"reachableCell=({reachableGoalX},{reachableGoalY}) reachableIsland={ResolveIslandIdForDiagnostics(_world, reachableGoalX, reachableGoalY)} " +
                $"reachableWorld={reachableGoalWorld} start=({startX},{startY}) startIsland={startIsland} " +
                $"anchorKey=target:{targetId} rawCellIndex={rawGoalCellIndex} refresh={shouldRefresh} hasStableGoal={hasStableGoal} " +
                $"stableCell=({anchor.ActiveGoalX},{anchor.ActiveGoalY}) stableWorld={anchor.ActiveGoalWorld} worldVersion={_world.Version}");
        }

        anchor.LastUsedFrame = GetFrameCount();
        agent.NavState.StableGoalTargetId = targetId;
        agent.NavState.StableGoalRawX = anchor.RawGoalX;
        agent.NavState.StableGoalRawY = anchor.RawGoalY;
        agent.NavState.StableGoalX = anchor.ActiveGoalX;
        agent.NavState.StableGoalY = anchor.ActiveGoalY;
        agent.NavState.StableGoalWorld = anchor.ActiveGoalWorld;
        goalX = anchor.ActiveGoalX;
        goalY = anchor.ActiveGoalY;
        stableGoalPosition = anchor.ActiveGoalWorld;
        return true;
    }

    private static void SetMovingTargetActiveGoal(
        MovingTargetAnchor anchor,
        int rawGoalX,
        int rawGoalY,
        int goalX,
        int goalY,
        int goalSectorId,
        Vector3 goalWorld)
    {
        if (anchor == null)
            throw new InvalidOperationException("SetMovingTargetActiveGoal failed: anchor is null.");

        anchor.RawGoalX = rawGoalX;
        anchor.RawGoalY = rawGoalY;
        anchor.ActiveGoalX = goalX;
        anchor.ActiveGoalY = goalY;
        anchor.ActiveGoalSectorId = goalSectorId;
        anchor.ActiveGoalWorld = goalWorld;
        anchor.ActiveWorldVersion = _world.Version;
    }

    private static bool TryResolveStartCellForReachability(IEntityContext self, out int startX, out int startY, out int startIsland)
    {
        startX = 0;
        startY = 0;
        startIsland = -1;
        if (self == null || _world == null)
            return false;

        if (!_world.WorldToGrid(self.Position, out startX, out startY))
            return false;

        if (!_world.IsWalkable(startX, startY)
            && !TryFindNearestWalkable(_world, startX, startY, 3, out startX, out startY))
        {
            return false;
        }

        startIsland = ResolveIslandIdForDiagnostics(_world, startX, startY);
        return startIsland > 0;
    }

    private static bool TryResolveReachableGoalCell(
        IEntityContext self,
        Vector3 rawGoalPosition,
        int rawGoalX,
        int rawGoalY,
        int startX,
        int startY,
        int startIsland,
        out int goalX,
        out int goalY,
        out Vector3 goalWorld,
        out int goalSectorId)
    {
        goalX = rawGoalX;
        goalY = rawGoalY;
        goalWorld = rawGoalPosition;
        goalSectorId = -1;

        int rawGoalIsland = ResolveIslandIdForDiagnostics(_world, rawGoalX, rawGoalY);
        if (rawGoalIsland == startIsland)
        {
            goalWorld = _world.GridToWorldCenter(rawGoalX, rawGoalY);
            return _world.TryGetSectorId(goalX, goalY, out goalSectorId);
        }

        if (!TryFindNearestWalkableInIslandByWorldDistance(
                _world,
                rawGoalX,
                rawGoalY,
                rawGoalPosition,
                startIsland,
                radius: 8,
                allowFullIslandSearch: true,
                out goalX,
                out goalY,
                out float distance))
        {
            return false;
        }

        goalWorld = _world.GridToWorldCenter(goalX, goalY);
        if (!_world.TryGetSectorId(goalX, goalY, out goalSectorId))
            return false;

        string targetDiagnostics = BuildReachabilityTargetDiagnostics(self, rawGoalPosition, startX, startY);
        Debug.LogWarning(
            $"[FlowGoalResolvedToReachable] agent={self?.CharacterKey ?? "null"} rawGoal={rawGoalPosition} " +
            $"rawCell=({rawGoalX},{rawGoalY}) rawIsland={rawGoalIsland} start=({startX},{startY}) startIsland={startIsland} " +
            $"resolved=({goalX},{goalY}) resolvedWorld={goalWorld} resolvedDistance={distance:F3} " +
            $"worldVersion={_world.Version} islandCount={_world.IslandCount} mainIsland={_world.MainIslandId}:{_world.MainIslandSize} {targetDiagnostics} rawNav={FormatNavSampleDiagnostics(rawGoalPosition)} " +
            $"rawNeighborhood={BuildIslandNeighborhoodDiagnostics(_world, rawGoalX, rawGoalY, 2)} resolvedNeighborhood={BuildIslandNeighborhoodDiagnostics(_world, goalX, goalY, 2)}");
        return true;
    }

    private static string BuildReachabilityTargetDiagnostics(IEntityContext self, Vector3 rawGoalPosition, int startX, int startY)
    {
        IEntityContext target = self?.TargetComp?.CurrentTarget;
        if (target == null)
            return "target=null";

        bool targetInGrid = _world.WorldToGrid(target.Position, out int targetX, out int targetY);
        int targetIsland = targetInGrid ? ResolveIslandIdForDiagnostics(_world, targetX, targetY) : -1;
        bool targetIsMainIsland = targetIsland > 0 && targetIsland == _world.MainIslandId;
        float rawTargetDistance = HorizontalDistanceXZ(rawGoalPosition, target.Position);

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        Vector3 startPosition = _world.GridToWorldCenter(startX, startY);
        bool startSampled = NavMesh.SamplePosition(startPosition, out NavMeshHit startHit, Mathf.Max(0.2f, _world.CellSize * 1.5f), filter);
        bool targetSampled = NavMesh.SamplePosition(target.Position, out NavMeshHit targetHit, Mathf.Max(0.2f, _world.CellSize * 1.5f), filter);
        string navPath = "navPath=sample-miss";
        string startNavCell = "startNav=miss";
        string targetNavCell = "targetNav=miss";
        if (startSampled && targetSampled)
        {
            bool startGrid = _world.WorldToGrid(startHit.position, out int startNavX, out int startNavY);
            bool targetGrid = _world.WorldToGrid(targetHit.position, out int targetNavX, out int targetNavY);
            int startNavIsland = startGrid ? ResolveIslandIdForDiagnostics(_world, startNavX, startNavY) : -1;
            int targetNavIsland = targetGrid ? ResolveIslandIdForDiagnostics(_world, targetNavX, targetNavY) : -1;
            NavMeshPath path = new NavMeshPath();
            bool pathFound = NavMesh.CalculatePath(startHit.position, targetHit.position, filter, path);
            navPath = $"navPath={{found={pathFound},status={path.status},corners={(path.corners != null ? path.corners.Length : 0)}}}";
            startNavCell = $"startNav={{pos={startHit.position},cell=({startNavX},{startNavY}),island={startNavIsland},main={startNavIsland == _world.MainIslandId}}}";
            targetNavCell = $"targetNav={{pos={targetHit.position},cell=({targetNavX},{targetNavY}),island={targetNavIsland},main={targetNavIsland == _world.MainIslandId}}}";
        }

        return $"target={{key={target.CharacterKey},pos={target.Position},inGrid={targetInGrid},cell=({targetX},{targetY}),island={targetIsland},main={targetIsMainIsland},rawDist={rawTargetDistance:F3},{startNavCell},{targetNavCell},{navPath}}}";
    }

    private static void TrimMovingTargetAnchors()
    {
        if (MovingTargetAnchors.Count == 0)
            return;

        int expireBeforeFrame = GetFrameCount() - 300;
        List<MovingTargetAnchorKey> expiredIds = null;
        foreach (KeyValuePair<MovingTargetAnchorKey, MovingTargetAnchor> pair in MovingTargetAnchors)
        {
            if (pair.Value.LastUsedFrame >= expireBeforeFrame)
                continue;

            expiredIds ??= new List<MovingTargetAnchorKey>();
            expiredIds.Add(pair.Key);
        }

        if (expiredIds == null)
            return;

        for (int i = 0; i < expiredIds.Count; i++)
            MovingTargetAnchors.Remove(expiredIds[i]);
    }

    private static void ClearStableGoal(AgentRuntimeData agent)
    {
        if (agent == null)
            return;

        agent.NavState.StableGoalTargetId = int.MinValue;
        agent.NavState.StableGoalX = -1;
        agent.NavState.StableGoalY = -1;
        agent.NavState.StableGoalWorld = Vector3.zero;
    }

    private static Vector3 ResolveNavigationGoalOccupancy(IEntityContext self, AgentRuntimeData agent, Vector3 goalPosition)
    {
        if (self == null || agent == null)
            throw new InvalidOperationException("ResolveNavigationGoalOccupancy failed: self or agent is null.");

        int frame = GetFrameCount();
        if (_navigationGoalReservationFrame != frame)
        {
            NavigationGoalReservations.Clear();
            _navigationGoalReservationFrame = frame;
        }

        int selfId = ResolveAgentId(self);
        float selfRadius = Mathf.Max(ResolveCollisionRadius(self), agent.Radius);
        float requiredDistance = Mathf.Max(selfRadius * 2f + NavigationGoalOccupancyPadding, selfRadius + 0.2f);
        int ignoredTargetId = ResolveIgnoredGoalOccupancyTargetId(self, goalPosition);

        if (IsNavigationGoalOccupiedByOther(selfId, ignoredTargetId, goalPosition, requiredDistance, out int reservingAgentId))
        {
            Vector3 rotatedGoal = ResolveRotatedNearbyGoal(self, agent, goalPosition, ignoredTargetId, selfRadius, requiredDistance, preferLateral: true);
            if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                && GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowGoalOccupancyDiag] key={self.CharacterKey} goal={goalPosition} rotatedGoal={rotatedGoal} " +
                    $"selfPos={self.Position} selfRadius={selfRadius:F3} requiredDistance={requiredDistance:F3} " +
                    $"ignoredTargetId={ignoredTargetId} reservingAgentId={reservingAgentId} currentTarget={self.TargetComp?.CurrentTarget?.CharacterKey ?? "null"} " +
                    $"targetPos={self.TargetComp?.CurrentTarget?.Position ?? Vector3.zero}");
            }
            return rotatedGoal;
        }

        RegisterNavigationGoalReservation(selfId, goalPosition, requiredDistance);
        return goalPosition;
    }

    private static int ResolveIgnoredGoalOccupancyTargetId(IEntityContext self, Vector3 goalPosition)
    {
        IEntityContext currentTarget = self.TargetComp?.CurrentTarget;
        if (currentTarget == null)
            return 0;

        float targetDistance = HorizontalDistanceXZ(goalPosition, currentTarget.Position);
        float targetRadius = ResolveCollisionRadius(currentTarget);
        return targetDistance > Mathf.Max(targetRadius * 0.75f, 0.35f)
            ? ResolveAgentId(currentTarget)
            : 0;
    }

    private static Vector3 ResolveRotatedNearbyGoal(IEntityContext self, AgentRuntimeData agent, Vector3 goalPosition, int targetId, float selfRadius, float requiredDistance, bool preferLateral)
    {
        if (_world == null)
            return goalPosition;

        if (!_world.WorldToGrid(goalPosition, out int goalX, out int goalY))
            return goalPosition;

        if (!_world.TryGetSectorId(goalX, goalY, out _))
            return goalPosition;

        int goalIsland = ResolveIslandIdForDiagnostics(_world, goalX, goalY);
        if (goalIsland <= 0)
            return goalPosition;

        Vector3 baseDirection = self.Position - goalPosition;
        baseDirection.y = 0f;
        if (baseDirection.sqrMagnitude <= 0.0001f)
            baseDirection = Vector3.forward;
        baseDirection.Normalize();

        Vector3 bestPoint = goalPosition;
        float bestScore = float.PositiveInfinity;
        int candidateOrderStart = preferLateral ? 1 : 0;
        for (int ring = 0; ring < NavigationGoalRingCount; ring++)
        {
            float radius = Mathf.Max(selfRadius + 0.05f, requiredDistance - selfRadius * 0.5f) + ring * Mathf.Max(0.35f, selfRadius * 0.35f);
            for (int i = 0; i < NavigationGoalCandidateCount; i++)
            {
                int candidateIndex = (candidateOrderStart + i) % NavigationGoalCandidateCount;
                float angle = ResolveNavigationGoalAngle(candidateIndex);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * baseDirection;
                Vector3 candidate = goalPosition + direction * radius;
                if (!_world.WorldToGrid(candidate, out int candidateX, out int candidateY))
                    continue;

                if (!_world.IsWalkable(candidateX, candidateY))
                    continue;

                if (ResolveIslandIdForDiagnostics(_world, candidateX, candidateY) != goalIsland)
                    continue;

                if (!TryResolveStableGoalCell(agent, self, candidate, out int resolvedX, out int resolvedY, out Vector3 resolvedWorld))
                    continue;

                if (ResolveIslandIdForDiagnostics(_world, resolvedX, resolvedY) != goalIsland)
                    continue;

                bool occupied = IsNavigationGoalOccupiedByOther(ResolveAgentId(self), targetId, resolvedWorld, requiredDistance, out _);
                if (occupied)
                    continue;

                float anglePenalty = Mathf.Abs(angle) * (preferLateral ? 0.02f : 0.01f);
                if (preferLateral && Mathf.Abs(angle) < 0.001f)
                    anglePenalty += 1f;
                float score = HorizontalDistanceXZ(self.Position, resolvedWorld) + anglePenalty + ring * 0.25f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestPoint = resolvedWorld;
            }
        }

        RegisterNavigationGoalReservation(ResolveAgentId(self), bestPoint, requiredDistance);
        return bestPoint;
    }

    private static void RegisterNavigationGoalReservation(int selfId, Vector3 point, float requiredDistance)
    {
        int frame = GetFrameCount();
        if (_navigationGoalReservationFrame != frame)
        {
            NavigationGoalReservations.Clear();
            _navigationGoalReservationFrame = frame;
        }

        for (int i = NavigationGoalReservations.Count - 1; i >= 0; i--)
        {
            if (NavigationGoalReservations[i].SelfId == selfId)
                NavigationGoalReservations.RemoveAt(i);
        }

        NavigationGoalReservations.Add(new NavigationGoalReservation(selfId, point, requiredDistance));
    }

    private static bool IsNavigationGoalOccupiedByOther(
        int selfId,
        int targetId,
        Vector3 position,
        float requiredDistance,
        out int reservingAgentId)
    {
        int frame = GetFrameCount();
        if (_navigationGoalReservationFrame != frame)
        {
            NavigationGoalReservations.Clear();
            _navigationGoalReservationFrame = frame;
        }

        reservingAgentId = 0;
        float requiredDistanceSq = requiredDistance * requiredDistance;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < NavigationGoalReservations.Count; i++)
        {
            NavigationGoalReservation reservation = NavigationGoalReservations[i];
            if (reservation.SelfId == selfId)
                continue;

            Vector3 offset = reservation.Point - position;
            offset.y = 0f;
            float distanceSq = offset.sqrMagnitude;
            if (distanceSq >= requiredDistanceSq)
                continue;

            float distance = Mathf.Sqrt(distanceSq);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            reservingAgentId = reservation.SelfId;
        }

        if (TryFindBlockingAgentSpatial(selfId, targetId, position, requiredDistance, out int blockingAgentId, out float blockingDistance)
            && blockingDistance < bestDistance)
        {
            bestDistance = blockingDistance;
            reservingAgentId = blockingAgentId;
        }

        return reservingAgentId != 0;
    }

    private static float ResolveNavigationGoalAngle(int index)
    {
        if (index == 0)
            return 0f;

        int step = (index + 1) / 2;
        float sign = (index & 1) == 1 ? 1f : -1f;
        return sign * step * (360f / NavigationGoalCandidateCount);
    }

    private static float HorizontalDistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
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

    private static bool AreCellsOnSameIsland(NavigationWorld world, int startX, int startY, int goalX, int goalY)
    {
        if (world == null)
            throw new InvalidOperationException("AreCellsOnSameIsland failed: world is null.");
        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            throw new InvalidOperationException("AreCellsOnSameIsland failed: island field is missing or invalid.");
        if (!world.IsWalkable(startX, startY) || !world.IsWalkable(goalX, goalY))
            return false;

        int startIsland = world.IslandIds[world.GetIndex(startX, startY)];
        int goalIsland = world.IslandIds[world.GetIndex(goalX, goalY)];
        return startIsland > 0 && startIsland == goalIsland;
    }

    private static string BuildIslandMismatchFailure(IEntityContext self, Vector3 rawGoalPosition, int startX, int startY, int goalX, int goalY)
    {
        int startIsland = ResolveIslandIdForDiagnostics(_world, startX, startY);
        int goalIsland = ResolveIslandIdForDiagnostics(_world, goalX, goalY);
        string nearestGoalIsland = TryFindNearestCellInIsland(_world, startX, startY, goalIsland, 32, out int nearestGoalIslandX, out int nearestGoalIslandY, out int nearestGoalIslandDistance)
            ? $" nearestGoalIslandFromStart=({nearestGoalIslandX},{nearestGoalIslandY}) manhattan={nearestGoalIslandDistance}"
            : " nearestGoalIslandFromStart=none";
        string nearestStartIsland = TryFindNearestCellInIsland(_world, goalX, goalY, startIsland, 32, out int nearestStartIslandX, out int nearestStartIslandY, out int nearestStartIslandDistance)
            ? $" nearestStartIslandFromGoal=({nearestStartIslandX},{nearestStartIslandY}) manhattan={nearestStartIslandDistance}"
            : " nearestStartIslandFromGoal=none";

        Vector3 startWorld = _world != null ? _world.GridToWorldCenter(startX, startY) : Vector3.zero;
        Vector3 goalWorld = _world != null ? _world.GridToWorldCenter(goalX, goalY) : Vector3.zero;
        IEntityContext target = self?.TargetComp?.CurrentTarget;
        string targetInfo = target != null
            ? $" target={target.CharacterKey} targetSide={target.Side} targetPos={target.Position} targetMoveMode={(target.MoveExecutor != null ? target.MoveExecutor.MovementMode.ToString() : "null")}"
            : " target=null";
        string selfInfo = self != null
            ? $" self={self.CharacterKey} selfSide={self.Side} selfPos={self.Position} selfMoveMode={(self.MoveExecutor != null ? self.MoveExecutor.MovementMode.ToString() : "null")}"
            : " self=null";
        string startNav = FormatNavSampleDiagnostics(startWorld);
        string goalNav = FormatNavSampleDiagnostics(rawGoalPosition);
        string startNeighborhood = BuildIslandNeighborhoodDiagnostics(_world, startX, startY, 2);
        string goalNeighborhood = BuildIslandNeighborhoodDiagnostics(_world, goalX, goalY, 2);

        return $"island mismatch start=({startX},{startY}) island={startIsland} startWorld={startWorld} " +
               $"goal=({goalX},{goalY}) island={goalIsland} goalWorld={goalWorld} rawGoal={rawGoalPosition} " +
               $"worldVersion={_world?.Version ?? -1} agentType={_world?.AgentTypeId ?? int.MinValue} islandCount={_world?.IslandCount ?? -1} " +
               $"runtimeDirtySectors={(_activeWorldState != null ? _activeWorldState.DirtyRuntimeObstacleSectors.Count : -1)} " +
               $"runtimeDirtyReason={_lastRuntimeObstacleDirtyReason} circleObstacles={CircleObstacles.Count} boxObstacles={BoxObstacles.Count}" +
               $"{nearestGoalIsland}{nearestStartIsland}{selfInfo}{targetInfo} startNav={startNav} goalNav={goalNav} " +
               $"startNeighborhood={startNeighborhood} goalNeighborhood={goalNeighborhood}";
    }

    private static int ResolveIslandIdForDiagnostics(NavigationWorld world, int x, int y)
    {
        if (world == null || world.IslandIds == null || x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return -1;

        return world.IslandIds[world.GetIndex(x, y)];
    }

    private static string BuildIslandNeighborhoodDiagnostics(NavigationWorld world, int centerX, int centerY, int radius)
    {
        if (world == null)
            return "world-null";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("[");
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
                builder.Append("(");
                builder.Append(x);
                builder.Append(",");
                builder.Append(y);
                builder.Append("){walk=");
                builder.Append(world.WalkableMask != null && world.WalkableMask.Length == world.Width * world.Height && world.WalkableMask[index]);
                builder.Append(",base=");
                builder.Append(world.BaseWalkableMask != null && world.BaseWalkableMask.Length == world.Width * world.Height && world.BaseWalkableMask[index]);
                builder.Append(",island=");
                builder.Append(world.IslandIds != null && world.IslandIds.Length == world.Width * world.Height ? world.IslandIds[index] : -1);
                builder.Append(",mask=0x");
                builder.Append(world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height
                    ? world.NeighborTraversalMask[index].ToString("X2")
                    : "NA");
                builder.Append(",sector=");
                builder.Append(world.TryGetSectorId(x, y, out int sectorId) ? sectorId : -1);
                builder.Append("}");
            }
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static string BuildGoalResolutionIslandDiagnostics(
        IEntityContext self,
        Vector3 desiredGoal,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int startIsland,
        int radiusCells,
        bool fullIslandFound,
        int fullIslandX,
        int fullIslandY,
        float fullIslandDistance)
    {
        if (_world == null)
            return $"[FlowGoalResolutionIslandDiag] world=null desired={desiredGoal}";

        int goalIsland = ResolveIslandIdForDiagnostics(_world, goalX, goalY);
        Vector3 startWorld = _world.GridToWorldCenter(startX, startY);
        Vector3 goalWorld = _world.GridToWorldCenter(goalX, goalY);
        string fullIslandResult = fullIslandFound
            ? $"fullIslandNearest=({fullIslandX},{fullIslandY}) world={_world.GridToWorldCenter(fullIslandX, fullIslandY)} dist={fullIslandDistance:F3}"
            : "fullIslandNearest=none";
        IEntityContext target = self?.TargetComp?.CurrentTarget;
        string selfInfo = self != null
            ? $"self={self.CharacterKey} side={self.Side} pos={self.Position} moveMode={(self.MoveExecutor != null ? self.MoveExecutor.MovementMode.ToString() : "null")}"
            : "self=null";
        string targetInfo = target != null
            ? $"target={target.CharacterKey} side={target.Side} pos={target.Position} moveMode={(target.MoveExecutor != null ? target.MoveExecutor.MovementMode.ToString() : "null")}"
            : "target=null";
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float navSampleRadius = Mathf.Max(0.2f, _world.CellSize * 0.75f);
        string startNeighborhood = BuildWalkableNeighborhoodDiagnostics(_world, startX, startY, 2, filter, navSampleRadius);
        string goalNeighborhood = BuildWalkableNeighborhoodDiagnostics(_world, goalX, goalY, 2, filter, navSampleRadius);

        return $"[FlowGoalResolutionIslandDiag] local same-island goal resolution failed desired={desiredGoal} " +
               $"start=({startX},{startY}) island={startIsland} world={startWorld} " +
               $"goal=({goalX},{goalY}) island={goalIsland} world={goalWorld} radiusCells={radiusCells} " +
               $"worldVersion={_world.Version} agentType={_world.AgentTypeId} islandCount={_world.IslandCount} " +
               $"runtimeDirtySectors={(_activeWorldState != null ? _activeWorldState.DirtyRuntimeObstacleSectors.Count : -1)} " +
               $"runtimeDirtyReason={_lastRuntimeObstacleDirtyReason} circleObstacles={CircleObstacles.Count} boxObstacles={BoxObstacles.Count} " +
               $"{fullIslandResult} {selfInfo} {targetInfo} desiredNav={FormatNavSampleDiagnostics(desiredGoal)} " +
               $"startNav={FormatNavSampleDiagnostics(startWorld)} goalCellNav={FormatNavSampleDiagnostics(goalWorld)} " +
               $"startNeighborhood={startNeighborhood} startLinks={BuildNeighborLinkDiagnostics(_world, startX, startY, 2)} " +
               $"goalNeighborhood={goalNeighborhood} goalLinks={BuildNeighborLinkDiagnostics(_world, goalX, goalY, 2)}";
    }

    private static bool TryFindNearestCellInIsland(NavigationWorld world, int startX, int startY, int islandId, int radius, out int resultX, out int resultY, out int distance)
    {
        resultX = 0;
        resultY = 0;
        distance = int.MaxValue;
        if (world == null || world.IslandIds == null || islandId <= 0)
            return false;

        bool found = false;
        for (int y = startY - radius; y <= startY + radius; y++)
        {
            for (int x = startX - radius; x <= startX + radius; x++)
            {
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                    continue;

                int index = world.GetIndex(x, y);
                if (world.IslandIds[index] != islandId)
                    continue;

                int candidateDistance = Mathf.Abs(x - startX) + Mathf.Abs(y - startY);
                if (found && candidateDistance >= distance)
                    continue;

                resultX = x;
                resultY = y;
                distance = candidateDistance;
                found = true;
            }
        }

        return found;
    }

    private static bool TryFindNearestWalkableInIslandByWorldDistance(
        NavigationWorld world,
        int centerX,
        int centerY,
        Vector3 desiredWorld,
        int islandId,
        int radius,
        bool allowFullIslandSearch,
        out int resultX,
        out int resultY,
        out float distance)
    {
        resultX = 0;
        resultY = 0;
        distance = float.PositiveInfinity;
        if (world == null || world.IslandIds == null || islandId <= 0)
            return false;

        bool found = TryFindNearestWalkableInIslandByWorldDistanceInBounds(
            world,
            Mathf.Max(0, centerX - radius),
            Mathf.Min(world.Width - 1, centerX + radius),
            Mathf.Max(0, centerY - radius),
            Mathf.Min(world.Height - 1, centerY + radius),
            desiredWorld,
            islandId,
            ref resultX,
            ref resultY,
            ref distance);

        if (found || !allowFullIslandSearch)
            return found;

        return TryFindNearestWalkableInIslandByWorldDistanceInBounds(
            world,
            0,
            world.Width - 1,
            0,
            world.Height - 1,
            desiredWorld,
            islandId,
            ref resultX,
            ref resultY,
            ref distance);
    }

    private static bool TryFindNearestWalkableInIslandByWorldDistanceInBounds(
        NavigationWorld world,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector3 desiredWorld,
        int islandId,
        ref int resultX,
        ref int resultY,
        ref float distance)
    {
        bool found = false;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.IsWalkable(x, y) || world.IslandIds[index] != islandId)
                    continue;

                Vector3 worldCenter = world.GridToWorldCenter(x, y);
                float dx = worldCenter.x - desiredWorld.x;
                float dz = worldCenter.z - desiredWorld.z;
                float candidateDistance = Mathf.Sqrt(dx * dx + dz * dz);
                if (found && candidateDistance >= distance)
                    continue;

                resultX = x;
                resultY = y;
                distance = candidateDistance;
                found = true;
            }
        }

        return found;
    }

    private static string FormatNavSampleDiagnostics(Vector3 position)
    {
        if (_world == null)
            return "world-null";

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        float sampleRadius = Mathf.Max(0.2f, _world.CellSize * 1.5f);
        bool navHit = NavMesh.SamplePosition(position, out NavMeshHit navMeshHit, sampleRadius, filter);
        int navX = 0;
        int navY = 0;
        bool navGrid = navHit && _world.WorldToGrid(navMeshHit.position, out navX, out navY);
        int navIsland = navGrid ? ResolveIslandIdForDiagnostics(_world, navX, navY) : -1;
        return navHit
            ? $"hit pos={navMeshHit.position} grid={(navGrid ? $"({navX},{navY})" : "out")} island={navIsland} dist={Vector3.Distance(position, navMeshHit.position):F3}"
            : $"miss radius={sampleRadius:F3}";
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
        int startX = x0;
        int startY = y0;
        int goalX = x1;
        int goalY = y1;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int cx = x0;
        int cy = y0;
        int previousX = x0;
        int previousY = y0;
        bool first = true;

        while (true)
        {
            if (!world.IsWalkable(cx, cy))
                return false;

            if (world.CostField != null
                && world.CostField.Length == world.Width * world.Height
                && world.CostField[world.GetIndex(cx, cy)] > 1)
            {
                return false;
            }

            if (!first && !CanTraverseNeighborCells(world, previousX, previousY, cx, cy))
                return false;

            if (cx == x1 && cy == y1)
                return HasNavAnchorLineOfSight(world, startX, startY, goalX, goalY);

            previousX = cx;
            previousY = cy;
            first = false;
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
        }
    }

    private static bool HasNavAnchorLineOfSight(NavigationWorld world, int startX, int startY, int goalX, int goalY)
    {
        if (!world.RequiresNavMeshAnchors)
            return true;
        if (!world.IsWalkable(startX, startY) || !world.IsWalkable(goalX, goalY))
            return false;

        Vector3 fromAnchor = world.CellNavAnchors[world.GetIndex(startX, startY)];
        Vector3 toAnchor = world.CellNavAnchors[world.GetIndex(goalX, goalY)];
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };
        bool forwardBlocked = NavMesh.Raycast(fromAnchor, toAnchor, out _, filter);
        bool backwardBlocked = NavMesh.Raycast(toAnchor, fromAnchor, out _, filter);
        return !forwardBlocked && !backwardBlocked;
    }

    private static bool IsDiagonalPassable(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        return world.IsWalkable(fromX, toY)
               && world.IsWalkable(toX, fromY)
               && HasRawNeighborTraversal(world, fromX, fromY, fromX, toY)
               && HasRawNeighborTraversal(world, fromX, fromY, toX, fromY)
               && HasRawNeighborTraversal(world, fromX, toY, toX, toY)
               && HasRawNeighborTraversal(world, toX, fromY, toX, toY);
    }

    private static void RebuildCostFieldForSector(NavigationWorld world, int sectorId, IReadOnlyCollection<CostStamp> costStamps)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildCostFieldForSector failed: world is null.");
        if (sectorId < 0 || sectorId >= world.Sectors.Length)
            throw new InvalidOperationException($"RebuildCostFieldForSector failed: sector id out of range {sectorId}.");
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            world.CostField = new byte[world.Width * world.Height];

        SectorData sector = world.Sectors[sectorId];
        int sectorMinX = sector.StartX;
        int sectorMinY = sector.StartY;
        int sectorMaxX = sector.StartX + sector.Width - 1;
        int sectorMaxY = sector.StartY + sector.Height - 1;
        int padding = Mathf.CeilToInt(ResolveWallCostBlurRadiusCells(world)) + 1;
        int sourceMinX = Mathf.Max(0, sectorMinX - padding);
        int sourceMinY = Mathf.Max(0, sectorMinY - padding);
        int sourceMaxX = Mathf.Min(world.Width - 1, sectorMaxX + padding);
        int sourceMaxY = Mathf.Min(world.Height - 1, sectorMaxY + padding);
        RebuildCostFieldInBounds(world, sourceMinX, sourceMinY, sourceMaxX, sourceMaxY, sectorMinX, sectorMinY, sectorMaxX, sectorMaxY, costStamps);
    }

    private static void RebuildCostFieldInBounds(
        NavigationWorld world,
        int sourceMinX,
        int sourceMinY,
        int sourceMaxX,
        int sourceMaxY,
        int writeMinX,
        int writeMinY,
        int writeMaxX,
        int writeMaxY,
        IReadOnlyCollection<CostStamp> costStamps)
    {
        float[] wallDistance = GetWallDistanceScratch(world.Width * world.Height);
        float wallCostBlurRadiusCells = ResolveWallCostBlurRadiusCells(world);

        OpenSet.Clear();
        for (int y = sourceMinY; y <= sourceMaxY; y++)
        {
            for (int x = sourceMinX; x <= sourceMaxX; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index])
                {
                    if (IsInsideBounds(x, y, writeMinX, writeMinY, writeMaxX, writeMaxY))
                        world.CostField[index] = 255;
                    wallDistance[index] = 0f;
                    OpenSet.Push(index, 0f);
                    continue;
                }

                if (IsInsideBounds(x, y, writeMinX, writeMinY, writeMaxX, writeMaxY))
                    world.CostField[index] = 1;
                if (!TouchesBlockedOrUntraversableNeighbor(world, x, y))
                    continue;

                wallDistance[index] = 1f;
                OpenSet.Push(index, 1f);
            }
        }

        while (OpenSet.Count > 0)
        {
            QueueNode node = OpenSet.Pop();
            if (node.Cost > wallDistance[node.Index] + 0.0001f)
                continue;
            if (node.Cost >= wallCostBlurRadiusCells)
                continue;

            int worldX = node.Index % world.Width;
            int worldY = node.Index / world.Width;
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = worldX + NeighborOffsetX[i];
                int nextY = worldY + NeighborOffsetY[i];
                if (nextX < sourceMinX || nextX > sourceMaxX || nextY < sourceMinY || nextY > sourceMaxY)
                    continue;

                int nextIndex = world.GetIndex(nextX, nextY);
                if (!world.WalkableMask[nextIndex])
                    continue;

                float stepCost = NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0 ? 1.4142135f : 1f;
                float newDistance = node.Cost + stepCost;
                if (newDistance >= wallDistance[nextIndex] || newDistance > wallCostBlurRadiusCells)
                    continue;

                wallDistance[nextIndex] = newDistance;
                OpenSet.Push(nextIndex, newDistance);
            }
        }

        for (int y = writeMinY; y <= writeMaxY; y++)
        {
            for (int x = writeMinX; x <= writeMaxX; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index])
                {
                    world.CostField[index] = 255;
                    continue;
                }

                float distance = wallDistance[index];
                if (float.IsPositiveInfinity(distance) || distance > wallCostBlurRadiusCells)
                {
                    world.CostField[index] = 1;
                    continue;
                }

                float t = Mathf.Clamp01((wallCostBlurRadiusCells - distance) / Mathf.Max(0.001f, wallCostBlurRadiusCells - 1f));
                int penalty = Mathf.RoundToInt(Mathf.Lerp(WallCostOuterPenalty, WallCostAdjacentPenalty, t));
                world.CostField[index] = (byte)Mathf.Clamp(1 + penalty, 1, 254);
            }
        }

        ApplyCostStampsInBounds(world, writeMinX, writeMinY, writeMaxX, writeMaxY, costStamps);
    }

    private static float ResolveWallCostBlurRadiusCells(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("ResolveWallCostBlurRadiusCells failed: world is null.");

        float extraRadiusCells = Mathf.Max(0f, (ResolveAgentTypeRadius(world.AgentTypeId) - 0.5f) / Mathf.Max(0.001f, world.CellSize));
        return WallCostBlurRadiusCells + extraRadiusCells;
    }

    private static void ApplyCostStampsInBounds(NavigationWorld world, int writeMinX, int writeMinY, int writeMaxX, int writeMaxY, IReadOnlyCollection<CostStamp> costStamps)
    {
        if (costStamps == null)
            costStamps = CostStamps.Values;

        if (costStamps.Count == 0)
            return;

        foreach (CostStamp stamp in costStamps)
        {
            if (stamp.AgentTypeId != AnyAgentTypeId && stamp.AgentTypeId != world.AgentTypeId)
                continue;

            int rawMinX = Mathf.FloorToInt((stamp.Bounds.min.x - world.Origin.x) / world.CellSize);
            int rawMaxX = Mathf.FloorToInt((stamp.Bounds.max.x - world.Origin.x) / world.CellSize);
            int rawMinY = Mathf.FloorToInt((stamp.Bounds.min.z - world.Origin.z) / world.CellSize);
            int rawMaxY = Mathf.FloorToInt((stamp.Bounds.max.z - world.Origin.z) / world.CellSize);
            int minX = Mathf.Max(rawMinX, writeMinX);
            int maxX = Mathf.Min(rawMaxX, writeMaxX);
            int minY = Mathf.Max(rawMinY, writeMinY);
            int maxY = Mathf.Min(rawMaxY, writeMaxY);
            if (minX > maxX || minY > maxY)
                continue;

            for (int y = minY; y <= maxY; y++)
            {
                int rowStart = y * world.Width;
                for (int x = minX; x <= maxX; x++)
                {
                    int index = rowStart + x;
                    if (!world.WalkableMask[index] || world.CostField[index] >= 255)
                        continue;

                    world.CostField[index] = stamp.Cost;
                }
            }
        }
    }

    private static bool IsInsideBounds(int x, int y, int minX, int minY, int maxX, int maxY)
    {
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static float[] GetWallDistanceScratch(int cellCount)
    {
        if (WallDistanceScratch.Length != cellCount)
            WallDistanceScratch = new float[cellCount];

        for (int i = 0; i < WallDistanceScratch.Length; i++)
            WallDistanceScratch[i] = float.PositiveInfinity;

        return WallDistanceScratch;
    }

    private static void ResolveSectorBounds(NavigationWorld world, HashSet<int> sectorIds, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = world.Width - 1;
        minY = world.Height - 1;
        maxX = 0;
        maxY = 0;
        bool found = false;
        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            minX = Mathf.Min(minX, sector.StartX);
            minY = Mathf.Min(minY, sector.StartY);
            maxX = Mathf.Max(maxX, sector.StartX + sector.Width - 1);
            maxY = Mathf.Max(maxY, sector.StartY + sector.Height - 1);
            found = true;
        }

        if (!found)
            throw new InvalidOperationException("ResolveSectorBounds failed: sector set is empty.");
    }

    private static HashSet<int> ExpandDirtySectorsByCellRadius(NavigationWorld world, HashSet<int> sectorIds, int radiusCells)
    {
        if (world == null)
            throw new InvalidOperationException("ExpandDirtySectorsByCellRadius failed: world is null.");
        if (sectorIds == null)
            throw new InvalidOperationException("ExpandDirtySectorsByCellRadius failed: sectorIds is null.");

        HashSet<int> expanded = new HashSet<int>(sectorIds);
        if (sectorIds.Count == 0)
            return expanded;

        ResolveSectorBounds(world, sectorIds, out int minX, out int minY, out int maxX, out int maxY);
        minX = Mathf.Max(0, minX - Mathf.Max(0, radiusCells));
        minY = Mathf.Max(0, minY - Mathf.Max(0, radiusCells));
        maxX = Mathf.Min(world.Width - 1, maxX + Mathf.Max(0, radiusCells));
        maxY = Mathf.Min(world.Height - 1, maxY + Mathf.Max(0, radiusCells));

        int minSectorX = Mathf.Clamp(minX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
        int maxSectorX = Mathf.Clamp(maxX / world.SectorSizeInCells, 0, world.SectorCountX - 1);
        int minSectorY = Mathf.Clamp(minY / world.SectorSizeInCells, 0, world.SectorCountY - 1);
        int maxSectorY = Mathf.Clamp(maxY / world.SectorSizeInCells, 0, world.SectorCountY - 1);
        for (int sy = minSectorY; sy <= maxSectorY; sy++)
        {
            for (int sx = minSectorX; sx <= maxSectorX; sx++)
                expanded.Add(sy * world.SectorCountX + sx);
        }

        return expanded;
    }

    private static bool TouchesBlockedOrUntraversableNeighbor(NavigationWorld world, int centerX, int centerY)
    {
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = centerX + NeighborOffsetX[i];
            int nextY = centerY + NeighborOffsetY[i];
            if (nextX < 0 || nextX >= world.Width || nextY < 0 || nextY >= world.Height)
                return true;
            if (!world.IsWalkable(nextX, nextY))
                return true;
            if (!CanTraverseNeighborCells(world, centerX, centerY, nextX, nextY))
                return true;
        }

        return false;
    }

    private static float ResolveTraversalCost(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            return 1f;

        int fromCost = world.CostField[world.GetIndex(fromX, fromY)];
        int toCost = world.CostField[world.GetIndex(toX, toY)];
        if (fromCost >= 255 || toCost >= 255)
            return float.PositiveInfinity;

        return Mathf.Max(1f, (fromCost + toCost) * 0.5f);
    }

    private static bool AreNavAnchorsMutuallyTraversable(Vector3 fromAnchor, Vector3 toAnchor, NavMeshQueryFilter filter)
    {
        bool forwardBlocked = NavMesh.Raycast(fromAnchor, toAnchor, out _, filter);
        bool backwardBlocked = NavMesh.Raycast(toAnchor, fromAnchor, out _, filter);
        return !forwardBlocked && !backwardBlocked;
    }

    private static void LogIslandFieldDiagnostics(NavigationWorld world, string reason)
    {
        if (world == null)
            throw new InvalidOperationException("LogIslandFieldDiagnostics failed: world is null.");
        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            throw new InvalidOperationException("LogIslandFieldDiagnostics failed: island field is missing or invalid.");

        int walkableCount = 0;
        int[] islandSizes = new int[Mathf.Max(1, world.IslandCount + 1)];
        int[] islandSampleX = new int[islandSizes.Length];
        int[] islandSampleY = new int[islandSizes.Length];
        int[] islandMinX = new int[islandSizes.Length];
        int[] islandMaxX = new int[islandSizes.Length];
        int[] islandMinY = new int[islandSizes.Length];
        int[] islandMaxY = new int[islandSizes.Length];
        for (int i = 0; i < islandSampleX.Length; i++)
        {
            islandSampleX[i] = -1;
            islandSampleY[i] = -1;
            islandMinX[i] = int.MaxValue;
            islandMaxX[i] = int.MinValue;
            islandMinY[i] = int.MaxValue;
            islandMaxY[i] = int.MinValue;
        }

        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = world.GetIndex(x, y);
                if (!world.WalkableMask[index])
                    continue;

                walkableCount++;
                int islandId = world.IslandIds[index];
                if (islandId <= 0 || islandId >= islandSizes.Length)
                    continue;

                islandSizes[islandId]++;
                if (x < islandMinX[islandId])
                    islandMinX[islandId] = x;
                if (x > islandMaxX[islandId])
                    islandMaxX[islandId] = x;
                if (y < islandMinY[islandId])
                    islandMinY[islandId] = y;
                if (y > islandMaxY[islandId])
                    islandMaxY[islandId] = y;
                if (islandSampleX[islandId] < 0)
                {
                    islandSampleX[islandId] = x;
                    islandSampleY[islandId] = y;
                }
            }
        }

        int largestIslandId = 0;
        int largestIslandSize = 0;
        int smallIslandCount = 0;
        for (int islandId = 1; islandId < islandSizes.Length; islandId++)
        {
            int size = islandSizes[islandId];
            if (size > largestIslandSize)
            {
                largestIslandSize = size;
                largestIslandId = islandId;
            }

            if (size > 0 && size <= 8)
                smallIslandCount++;
        }

        if (world.IslandCount <= 1)
            return;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        builder.Append("[FlowIslandFieldDiag] reason=");
        builder.Append(reason);
        builder.Append(" worldVersion=");
        builder.Append(world.Version);
        builder.Append(" agentType=");
        builder.Append(world.AgentTypeId);
        builder.Append(" size=");
        builder.Append(world.Width);
        builder.Append("x");
        builder.Append(world.Height);
        builder.Append(" cellSize=");
        builder.Append(world.CellSize.ToString("F3"));
        builder.Append(" walkable=");
        builder.Append(walkableCount);
        builder.Append(" islandCount=");
        builder.Append(world.IslandCount);
        builder.Append(" largest=");
        builder.Append(largestIslandId);
        builder.Append(":");
        builder.Append(largestIslandSize);
        builder.Append(" smallIslands=");
        builder.Append(smallIslandCount);
        builder.Append(" samples=[");

        int appended = 0;
        for (int islandId = 1; islandId < islandSizes.Length && appended < 8; islandId++)
        {
            int size = islandSizes[islandId];
            if (size <= 0 || islandId == largestIslandId)
                continue;

            if (appended > 0)
                builder.Append(" | ");

            int sampleX = islandSampleX[islandId];
            int sampleY = islandSampleY[islandId];
            builder.Append("island=");
            builder.Append(islandId);
            builder.Append(" size=");
            builder.Append(size);
            builder.Append(" sample=(");
            builder.Append(sampleX);
            builder.Append(",");
            builder.Append(sampleY);
            builder.Append(") ");
            builder.Append("bbox=(");
            builder.Append(islandMinX[islandId]);
            builder.Append(",");
            builder.Append(islandMinY[islandId]);
            builder.Append(")-(");
            builder.Append(islandMaxX[islandId]);
            builder.Append(",");
            builder.Append(islandMaxY[islandId]);
            builder.Append(") ");
            builder.Append(BuildIslandBoundaryDiagnostics(world, islandId, largestIslandId, 6));
            builder.Append(" ");
            builder.Append(BuildNeighborLinkDiagnostics(world, sampleX, sampleY, 1, 6));
            appended++;
        }

        builder.Append("]");
        Debug.LogWarning(builder.ToString());
    }

    private static string BuildIslandBoundaryDiagnostics(NavigationWorld world, int islandId, int largestIslandId, int maxExamples)
    {
        if (world == null)
            return "boundary=world-null";
        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            return "boundary=island-invalid";

        int blockedNeighborCount = 0;
        int baseBlockedNeighborCount = 0;
        int runtimeBlockedNeighborCount = 0;
        int otherIslandNeighborCount = 0;
        int noTraversalNeighborCount = 0;
        int largestNeighborCount = 0;
        int exampleCount = 0;
        System.Text.StringBuilder examples = new System.Text.StringBuilder(768);
        examples.Append("[");

        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = world.GetIndex(x, y);
                if (world.IslandIds[index] != islandId)
                    continue;

                byte mask = world.NeighborTraversalMask[index];
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int toX = x + NeighborOffsetX[i];
                    int toY = y + NeighborOffsetY[i];
                    if (toX < 0 || toX >= world.Width || toY < 0 || toY >= world.Height)
                        continue;

                    int toIndex = world.GetIndex(toX, toY);
                    bool toWalkable = world.WalkableMask[toIndex];
                    if (!toWalkable)
                    {
                        blockedNeighborCount++;
                        bool baseWalkable = world.BaseWalkableMask != null
                                            && world.BaseWalkableMask.Length == world.WalkableMask.Length
                                            && world.BaseWalkableMask[toIndex];
                        if (baseWalkable)
                            runtimeBlockedNeighborCount++;
                        else
                            baseBlockedNeighborCount++;

                        if (exampleCount < maxExamples)
                        {
                            AppendBoundaryExamplePrefix(examples, ref exampleCount);
                            examples.Append("(");
                            examples.Append(x);
                            examples.Append(",");
                            examples.Append(y);
                            examples.Append(")->(");
                            examples.Append(toX);
                            examples.Append(",");
                            examples.Append(toY);
                            examples.Append("){blocked,base=");
                            examples.Append(baseWalkable);
                            examples.Append("}");
                        }

                        continue;
                    }

                    int toIsland = world.IslandIds[toIndex];
                    if (toIsland == islandId)
                        continue;

                    otherIslandNeighborCount++;
                    if (toIsland == largestIslandId)
                        largestNeighborCount++;

                    bool hasTraversal = (mask & (1 << i)) != 0;
                    if (!hasTraversal)
                        noTraversalNeighborCount++;

                    if (exampleCount < maxExamples)
                    {
                        AppendBoundaryExamplePrefix(examples, ref exampleCount);
                        examples.Append("(");
                        examples.Append(x);
                        examples.Append(",");
                        examples.Append(y);
                        examples.Append(")->(");
                        examples.Append(toX);
                        examples.Append(",");
                        examples.Append(toY);
                        examples.Append("){toIsland=");
                        examples.Append(toIsland);
                        examples.Append(",traverse=");
                        examples.Append(hasTraversal);
                        examples.Append("}");
                    }
                }
            }
        }

        if (exampleCount == 0)
            examples.Append("none");
        examples.Append("]");

        return "boundary={blocked="
               + blockedNeighborCount
               + ",baseBlocked="
               + baseBlockedNeighborCount
               + ",runtimeBlocked="
               + runtimeBlockedNeighborCount
               + ",otherIsland="
               + otherIslandNeighborCount
               + ",largestNeighbors="
               + largestNeighborCount
               + ",noTraversal="
               + noTraversalNeighborCount
               + ",examples="
               + examples
               + "}";
    }

    private static void AppendBoundaryExamplePrefix(System.Text.StringBuilder builder, ref int exampleCount)
    {
        if (exampleCount > 0)
            builder.Append("; ");

        exampleCount++;
    }

    private static string BuildNeighborLinkDiagnostics(NavigationWorld world, int centerX, int centerY, int radius, int maxLinks = 16)
    {
        if (world == null)
            return "links=world-null";
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            return "links=mask-invalid";

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = world.AgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        System.Text.StringBuilder builder = new System.Text.StringBuilder(768);
        builder.Append("links=[");
        int count = 0;
        for (int y = centerY - radius; y <= centerY + radius && count < maxLinks; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius && count < maxLinks; x++)
            {
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height || !world.IsWalkable(x, y))
                    continue;

                int fromIndex = world.GetIndex(x, y);
                byte mask = world.NeighborTraversalMask[fromIndex];
                int fromIsland = ResolveIslandIdForDiagnostics(world, x, y);
                for (int i = 0; i < NeighborOffsetX.Length && count < maxLinks; i++)
                {
                    int toX = x + NeighborOffsetX[i];
                    int toY = y + NeighborOffsetY[i];
                    if (toX < 0 || toX >= world.Width || toY < 0 || toY >= world.Height || !world.IsWalkable(toX, toY))
                        continue;

                    bool hasTraversal = (mask & (1 << i)) != 0;
                    int toIsland = ResolveIslandIdForDiagnostics(world, toX, toY);
                    if (hasTraversal && fromIsland == toIsland)
                        continue;

                    if (count > 0)
                        builder.Append("; ");

                    builder.Append("(");
                    builder.Append(x);
                    builder.Append(",");
                    builder.Append(y);
                    builder.Append(")->(");
                    builder.Append(toX);
                    builder.Append(",");
                    builder.Append(toY);
                    builder.Append("){fromIsland=");
                    builder.Append(fromIsland);
                    builder.Append(",toIsland=");
                    builder.Append(toIsland);
                    builder.Append(",traverse=");
                    builder.Append(hasTraversal);

                    if (world.RequiresNavMeshAnchors)
                    {
                        Vector3 fromAnchor = world.CellNavAnchors[fromIndex];
                        Vector3 toAnchor = world.CellNavAnchors[world.GetIndex(toX, toY)];
                        bool forwardBlocked = NavMesh.Raycast(fromAnchor, toAnchor, out NavMeshHit forwardHit, filter);
                        bool backwardBlocked = NavMesh.Raycast(toAnchor, fromAnchor, out NavMeshHit backwardHit, filter);
                        builder.Append(",anchorDist=");
                        builder.Append(Vector3.Distance(fromAnchor, toAnchor).ToString("F3"));
                        builder.Append(",rayFwdBlocked=");
                        builder.Append(forwardBlocked);
                        if (forwardBlocked)
                        {
                            builder.Append(",fwdHit=");
                            builder.Append(forwardHit.position);
                        }

                        builder.Append(",rayBackBlocked=");
                        builder.Append(backwardBlocked);
                        if (backwardBlocked)
                        {
                            builder.Append(",backHit=");
                            builder.Append(backwardHit.position);
                        }
                    }

                    builder.Append("}");
                    count++;
                }
            }
        }

        if (count == 0)
            builder.Append("none");
        builder.Append("]");
        return builder.ToString();
    }

    private static bool CanTraverseNeighborCells(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        if (!world.IsWalkable(fromX, fromY) || !world.IsWalkable(toX, toY))
            return false;

        int dx = Mathf.Abs(toX - fromX);
        int dy = Mathf.Abs(toY - fromY);
        if (dx > 1 || dy > 1 || (dx == 0 && dy == 0))
            return false;

        if (!HasRawNeighborTraversal(world, fromX, fromY, toX, toY))
            return false;

        if (dx == 1 && dy == 1 && !IsDiagonalPassable(world, fromX, fromY, toX, toY))
            return false;

        return true;
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

    private static string BuildAgentCellDiagnostic(AgentRuntimeData agent, Vector3 position)
    {
        if (_world == null)
            return "cell=world-null";

        bool inGrid = _world.WorldToGrid(position, out int cellX, out int cellY);
        int island = inGrid ? ResolveIslandIdForDiagnostics(_world, cellX, cellY) : -1;
        bool walkable = inGrid && _world.IsWalkable(cellX, cellY);
        bool baseWalkable = false;
        if (inGrid)
            baseWalkable = _world.BaseWalkableMask[_world.GetIndex(cellX, cellY)];

        return $"cell={(inGrid ? $"({cellX},{cellY})" : "out")} island={island} walkable={walkable} baseWalkable={baseWalkable} " +
               $"navStateCell={agent.NavState.CurrentCell} navStateSector={agent.NavState.CurrentSectorId}";
    }

    private static string BuildGridEdgeDiagnostic(Vector3 position, Vector3 desiredDirection)
    {
        if (_world == null)
            return "gridEdge=world-null";

        bool hasGridEdge = TryResolveGridEdgeData(position, desiredDirection, out Vector3 gridNormal, out float gridDistance);
        return hasGridEdge
            ? $"gridEdgeNormal={gridNormal} gridEdgeDist={gridDistance:F3}"
            : "gridEdge=none";
    }

    private static string BuildNavEdgeDiagnostic(Vector3 position, int agentTypeId)
    {
        if (_world == null)
            return "navEdge=world-null";

        bool hasNavEdge = TryResolveNavEdgeData(position, agentTypeId, out Vector3 navNormal, out float navDistance);
        return hasNavEdge
            ? $"navEdgeNormal={navNormal} navEdgeDist={navDistance:F3}"
            : "navEdge=none";
    }

    private static string BuildExecutorRayDiagnostic(Vector3 position, Vector3 desiredHorizontalDisplacement, Vector3 inputVelocity, int agentTypeId)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
            return "executorRay=zero-displacement";

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = NavMesh.AllAreas
        };

        float sampleRadius = _world != null ? Mathf.Max(0.2f, _world.CellSize * 0.75f) : 0.5f;
        if (!NavMesh.SamplePosition(position, out NavMeshHit startHit, sampleRadius, filter))
            return $"executorRay=start-miss sampleRadius={sampleRadius:F3}";

        Vector3 rayEnd = new Vector3(
            startHit.position.x + desiredHorizontalDisplacement.x,
            startHit.position.y,
            startHit.position.z + desiredHorizontalDisplacement.z);

        bool blocked = NavMesh.Raycast(startHit.position, rayEnd, out NavMeshHit rayHit, filter);
        if (!blocked)
            return "executorRay=clear";

        Vector3 normal = rayHit.normal;
        normal.y = 0f;
        if (normal.sqrMagnitude > 0.0001f)
            normal.Normalize();

        float intoBoundary = normal.sqrMagnitude > 0.0001f ? Vector3.Dot(inputVelocity, -normal) : 0f;
        return $"executorRay=blocked hit={rayHit.position} normal={rayHit.normal} intoBoundary={intoBoundary:F3}";
    }

    private static void LogMissingConstraintAgentDiagnostic(
        IEntityContext self,
        int expectedAgentId,
        Vector3 currentPosition,
        Vector3 desiredHorizontalDisplacement,
        Vector3 inputVelocity,
        string executorReason)
    {
        string candidates = BuildConstraintAgentCandidateDiagnostic(self, currentPosition, 6);
        string executorRay = BuildExecutorRayDiagnostic(
            currentPosition,
            desiredHorizontalDisplacement,
            inputVelocity,
            self is MAEntity ma ? ma.navAgentTypeID : 0);

        Debug.LogWarning(
            $"[FlowConstraintDiagMissingAgent] reason={executorReason} key={self.CharacterKey} expectedId={expectedAgentId} " +
            $"selfType={self.GetType().Name} side={self.Side} pos={currentPosition} selfPos={self.Position} " +
            $"agentType={(self is MAEntity maEntity ? maEntity.navAgentTypeID.ToString() : "unknown")} " +
            $"inputVelocity={inputVelocity} desiredDisp={desiredHorizontalDisplacement} {executorRay} " +
            $"agents={Agents.Count} candidates={candidates}");
    }

    private static string BuildConstraintAgentCandidateDiagnostic(IEntityContext self, Vector3 currentPosition, int maxCandidates)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append('[');
        int appended = 0;
        foreach (AgentRuntimeData candidate in Agents.Values)
        {
            bool sameKey = !string.IsNullOrEmpty(self.CharacterKey) && candidate.CharacterKey == self.CharacterKey;
            float distance = Vector3.Distance(candidate.Position, currentPosition);
            if (!sameKey && distance > 1.5f)
                continue;

            if (appended > 0)
                builder.Append(" | ");

            builder.Append("id=");
            builder.Append(candidate.Id);
            builder.Append(" key=");
            builder.Append(candidate.CharacterKey);
            builder.Append(" dist=");
            builder.Append(distance.ToString("F3"));
            builder.Append(" pos=");
            builder.Append(candidate.Position);
            builder.Append(" agentType=");
            builder.Append(candidate.AgentTypeId);
            builder.Append(" source=");
            builder.Append(candidate.RegistrationSource);
            builder.Append(" synthetic=");
            builder.Append(candidate.IsSyntheticRegistration);
            builder.Append(" intent=");
            builder.Append(candidate.HasNavigationIntent);
            builder.Append(" desired=");
            builder.Append(candidate.NavState.DesiredVelocity);
            builder.Append(" resolved=");
            builder.Append(candidate.NavState.ResolvedVelocity);

            appended++;
            if (appended >= maxCandidates)
                break;
        }

        if (appended == 0)
            builder.Append("none");
        builder.Append(']');
        return builder.ToString();
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
#if UNITY_EDITOR
        if (TestAgentTypeRadii.TryGetValue(agentTypeId, out float testRadius))
            return testRadius;
#endif

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
