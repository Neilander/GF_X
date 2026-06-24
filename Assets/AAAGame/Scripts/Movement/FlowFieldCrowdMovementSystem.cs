using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAAGame.MiniMap.FOG3;
using UnityEngine;
using Debug = UnityEngine.Debug;

public enum FlowFieldAgentState
{
    Idle,
    Follow,
    Combat
}

public static class FlowFieldCrowdMovementSystem
{
    private const int AnyAgentTypeId = int.MinValue + 1;
    private const float PortalGraphMergePriorityBias = 0.35f;
    private const float MaxAgentAvoidBackwardSpeedRatio = 0.35f;
    private const float MaxAgentAvoidLateralSpeedRatio = 0.75f;
    private const float IdleOverlapRecoverySpeed = 0.65f;
    private const float EdgeRecoveryMinSpeedRatio = 0.28f;
    private const float WalkableSteeringCandidateMinSpeedRatio = 0.25f;
    private const int NavigationGoalCandidateCount = 16;
    private const int NavigationGoalRingCount = 3;
    private const float NavigationGoalOccupancyPadding = 0.35f;
    private const int CombatTargetSlotCacheFrameLifetime = 120;
    private const float IntegrationSignificantImprovement = 0.05f;
    private const int BudgetTimeCheckInterval = 16;
    private const byte FlowDirectionMask = 0x0F;
    private const byte FlowLineOfSightFlag = 1 << 4;
    private const byte FlowWaveFrontBlockedFlag = 1 << 5;
    private const byte FlowPathableFlag = 1 << 6;
    private const byte FlowReachableFlag = 1 << 7;
    private const ushort QuantizedIntegrationInfinity = ushort.MaxValue;
    private const ushort QuantizedIntegrationMaxFinite = ushort.MaxValue - 1;
    private static readonly long FlowPerfLogThresholdTicks = Stopwatch.Frequency * 4 / 1000;
    private const float WallCostBlurRadiusCells = 2.25f;
    private const int WallCostAdjacentPenalty = 1;
    private const int WallCostOuterPenalty = 0;
    private const int PortalWindowSplitCostDelta = 8;
    private const int FlowHeavyDiagnosticCooldownFrames = 90;

    private sealed class RuntimeConfig
    {
        public float NavigationCellSize;
        public float NavigationBoundsPadding = 0.6f;
        public int SectorSizeInCells = 12;
        public int PortalNarrowWidthCells = 2;
        public int PortalMaxWindowWidthCells = 6;
        public int FlowTileCacheLimit = 256;
        public float RuntimeRebuildBudgetMilliseconds = 1.5f;
        public bool RequireAuthoredNavigationSource = true;
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
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public bool[] BaseWalkableMask;
        public bool[] WalkableMask;
        public byte[] CostField;
        public byte[][] SectorCostFields;
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
        public bool IsClearCostField;
        public bool IsClearFlowTile;
        public int UniformIslandId;
        public int[] LocalComponentIds;
        public int LocalComponentCount;
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
        public string BuildSource;

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
        public ushort[] QuantizedIntegration;
        public float IntegrationScale;
        public int LastUsedFrame;
    }

    private readonly struct PendingSectorPortalAccess
    {
        public readonly int SectorId;
        public readonly int PortalId;
        public readonly int SectorDirtyVersion;
        public readonly ushort[] QuantizedIntegration;
        public readonly float IntegrationScale;

        public PendingSectorPortalAccess(int sectorId, int portalId, int sectorDirtyVersion, ushort[] quantizedIntegration, float integrationScale)
        {
            SectorId = sectorId;
            PortalId = portalId;
            SectorDirtyVersion = sectorDirtyVersion;
            QuantizedIntegration = quantizedIntegration;
            IntegrationScale = integrationScale;
        }
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
        public int PathDirectionContextHash;
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
        public int LastFlowTileInvariantDiagnosticFrame = -1;
        public int LastFlowWallProbeDiagnosticFrame = -1;
        public int LastPortalRankDiagnosticFrame = -1;
        public int LastRouteAsymmetryDiagnosticFrame = -1;
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
        public FlowFieldAgentState State = FlowFieldAgentState.Idle;
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
        PendingPortal = 2,
        Zero = 3
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
        public readonly Vector3 LineOfSightTargetPosition;
        public readonly int VisibleCandidateCount;
        public readonly int SelectedPairIndex;
        public readonly bool UsedOppositeCenter;
        public readonly string CandidateSummary;

        public PortalTargetResolution(
            Vector3 targetPosition,
            Vector3 lineOfSightTargetPosition,
            int visibleCandidateCount,
            int selectedPairIndex,
            bool usedOppositeCenter,
            string candidateSummary)
        {
            TargetPosition = targetPosition;
            LineOfSightTargetPosition = lineOfSightTargetPosition;
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

    private sealed class IntegrationPayload
    {
        public float[] Values;
        public int ReleasedFrame = -1;

        public IntegrationPayload(int cellCount)
        {
            Values = RentIntegrationArray(cellCount);
        }
    }

    private sealed class IntegrationCostSummary
    {
        public Vector2Int[] Cells;
        public float[] Costs;
    }

    private sealed class FlowTileCacheEntry
    {
        public FlowTileCacheKey Key;
        public int StartX;
        public int StartY;
        public int Width;
        public int Height;
        public IntegrationPayload IntegrationPayload;
        public IntegrationPayload DebugIntegrationPayload;
        public byte[] FlowFieldValues;
        public CachedPortalTarget[] PortalTargets;
        public Vector2Int[] GoalCells;
        public IntegrationCostSummary GoalIntegrationSummary;
        public readonly Dictionary<int, IntegrationCostSummary> PortalSeamIntegrationSummaries = new Dictionary<int, IntegrationCostSummary>();
        public int LastUsedFrame;
        public int LastReferencedFrame;
        public int ActiveReferenceCount;
        public bool IntegrationReleased;
        public bool UsesClearFlowDescriptor;

        public float[] Integration => IntegrationPayload?.Values;

        public int GetLocalIndex(int worldX, int worldY)
        {
            return (worldX - StartX) + (worldY - StartY) * Width;
        }
    }

    private readonly struct FlowTileBuildKey : IEquatable<FlowTileBuildKey>
    {
        public readonly FlowTileCacheKey CacheKey;
        public readonly int SectorPathIndex;
        public readonly int GoalX;
        public readonly int GoalY;
        public readonly int AgentTypeId;

        public FlowTileBuildKey(FlowTileCacheKey cacheKey, int sectorPathIndex, int goalX, int goalY, int agentTypeId)
        {
            CacheKey = cacheKey;
            SectorPathIndex = sectorPathIndex;
            GoalX = goalX;
            GoalY = goalY;
            AgentTypeId = agentTypeId;
        }

        public bool Equals(FlowTileBuildKey other)
        {
            return CacheKey.Equals(other.CacheKey)
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
        public MinHeap LineOfSightOpenSet;
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

    private sealed class SharedGoalFieldBuildJob
    {
        public SharedGoalFieldKey Key;
        public int GoalSectorId;
        public int GoalX;
        public int GoalY;
        public int AgentTypeId;
        public SharedGoalFieldBuildStage Stage;
        public SectorData GoalSector;
        public float[] GoalIntegration;
        public MinHeap GoalIntegrationOpenSet;
        public SharedGoalField Field;
        public MinHeap PortalOpenSet;
    }

    private enum SharedGoalFieldBuildStage
    {
        GoalIntegration = 0,
        PortalGraph = 1,
        Commit = 2,
        Complete = 3
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
        public readonly int RawGoalCellIndex;

        public MovingTargetAnchorKey(int targetId, int rawGoalCellIndex)
        {
            TargetId = targetId;
            RawGoalCellIndex = rawGoalCellIndex;
        }

        public bool Equals(MovingTargetAnchorKey other)
        {
            return TargetId == other.TargetId
                   && RawGoalCellIndex == other.RawGoalCellIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is MovingTargetAnchorKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (TargetId * 397) ^ RawGoalCellIndex;
            }
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

    private readonly struct CombatTargetSlotKey : IEquatable<CombatTargetSlotKey>
    {
        public readonly int WorldVersion;
        public readonly int AgentTypeId;
        public readonly int TargetId;
        public readonly int TargetCellIndex;
        public readonly int TargetXBand;
        public readonly int TargetYBand;
        public readonly int StandOffBand;
        public readonly int RingSpacingBand;
        public readonly int RingCount;
        public readonly int CandidateCount;

        public CombatTargetSlotKey(
            int worldVersion,
            int agentTypeId,
            int targetId,
            int targetCellIndex,
            int targetXBand,
            int targetYBand,
            int standOffBand,
            int ringSpacingBand,
            int ringCount,
            int candidateCount)
        {
            WorldVersion = worldVersion;
            AgentTypeId = agentTypeId;
            TargetId = targetId;
            TargetCellIndex = targetCellIndex;
            TargetXBand = targetXBand;
            TargetYBand = targetYBand;
            StandOffBand = standOffBand;
            RingSpacingBand = ringSpacingBand;
            RingCount = ringCount;
            CandidateCount = candidateCount;
        }

        public bool Equals(CombatTargetSlotKey other)
        {
            return WorldVersion == other.WorldVersion
                   && AgentTypeId == other.AgentTypeId
                   && TargetId == other.TargetId
                   && TargetCellIndex == other.TargetCellIndex
                   && TargetXBand == other.TargetXBand
                   && TargetYBand == other.TargetYBand
                   && StandOffBand == other.StandOffBand
                   && RingSpacingBand == other.RingSpacingBand
                   && RingCount == other.RingCount
                   && CandidateCount == other.CandidateCount;
        }

        public override bool Equals(object obj)
        {
            return obj is CombatTargetSlotKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ TargetId;
                hash = (hash * 397) ^ TargetCellIndex;
                hash = (hash * 397) ^ TargetXBand;
                hash = (hash * 397) ^ TargetYBand;
                hash = (hash * 397) ^ StandOffBand;
                hash = (hash * 397) ^ RingSpacingBand;
                hash = (hash * 397) ^ RingCount;
                hash = (hash * 397) ^ CandidateCount;
                return hash;
            }
        }
    }

    private sealed class CombatTargetSlotEntry
    {
        public Vector3 TargetPoint;
        public Vector3[] Points;
        public int[] CellX;
        public int[] CellY;
        public int[] IslandIds;
        public int LastUsedFrame;
        public string BuildSummary;
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
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public byte[] Costs;
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
    private static readonly LinkedList<FlowTileBuildJob> FlowTileBuildQueue = new LinkedList<FlowTileBuildJob>();
    private static readonly HashSet<FlowTileCacheKey> PendingFlowTileBuildJobs = new HashSet<FlowTileCacheKey>();
    private static readonly Dictionary<int, Stack<float[]>> IntegrationArrayPool = new Dictionary<int, Stack<float[]>>();
    private static readonly Dictionary<SectorPathCacheKey, SectorPathCacheEntry> SectorPathCache = new Dictionary<SectorPathCacheKey, SectorPathCacheEntry>();
    private static readonly Dictionary<SectorPortalAccessKey, SectorPortalAccessEntry> SectorPortalAccessCache = new Dictionary<SectorPortalAccessKey, SectorPortalAccessEntry>();
    private static readonly Dictionary<SharedGoalFieldKey, SharedGoalField> SharedGoalFields = new Dictionary<SharedGoalFieldKey, SharedGoalField>();
    private static readonly LinkedList<SharedGoalFieldBuildJob> SharedGoalFieldBuildQueue = new LinkedList<SharedGoalFieldBuildJob>();
    private static readonly HashSet<SharedGoalFieldKey> PendingSharedGoalFieldBuildJobs = new HashSet<SharedGoalFieldKey>();
    private static readonly Dictionary<CombatTargetSlotKey, CombatTargetSlotEntry> CombatTargetSlotCache = new Dictionary<CombatTargetSlotKey, CombatTargetSlotEntry>();
    private static readonly Dictionary<int, BottleneckRuntimeState> Bottlenecks = new Dictionary<int, BottleneckRuntimeState>();
    private static readonly Dictionary<int, CorridorBottleneckDescriptor> CorridorBottlenecks = new Dictionary<int, CorridorBottleneckDescriptor>();

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

    private static byte EncodeFlowDirectionIndex(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return 0;

        Vector2 normalized = direction.normalized;
        float bestDot = float.NegativeInfinity;
        int bestIndex = 0;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            Vector2 candidate = new Vector2(NeighborOffsetX[i], NeighborOffsetY[i]).normalized;
            float dot = Vector2.Dot(normalized, candidate);
            if (dot <= bestDot)
                continue;

            bestDot = dot;
            bestIndex = i + 1;
        }

        return (byte)bestIndex;
    }

    private static Vector2 DecodeFlowDirectionIndex(byte directionIndex)
    {
        if (directionIndex == 0)
            return Vector2.zero;

        int offsetIndex = directionIndex - 1;
        if (offsetIndex < 0 || offsetIndex >= NeighborOffsetX.Length)
            throw new InvalidOperationException($"DecodeFlowDirectionIndex failed: invalid directionIndex={directionIndex}.");

        return new Vector2(NeighborOffsetX[offsetIndex], NeighborOffsetY[offsetIndex]).normalized;
    }

    private static void ValidateFlowFieldCell(FlowTileCacheEntry tile, int localIndex)
    {
        if (tile == null)
            throw new InvalidOperationException("ValidateFlowFieldCell failed: tile is null.");
        if (tile.FlowFieldValues == null || tile.FlowFieldValues.Length != tile.Width * tile.Height)
            throw new InvalidOperationException($"ValidateFlowFieldCell failed: invalid flow field values key={FormatTileKey(tile.Key)}.");
        if (localIndex < 0 || localIndex >= tile.FlowFieldValues.Length)
            throw new InvalidOperationException($"ValidateFlowFieldCell failed: localIndex out of range index={localIndex} key={FormatTileKey(tile.Key)}.");
    }

    private static byte GetFlowDirectionIndex(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (byte)(tile.FlowFieldValues[localIndex] & FlowDirectionMask);
    }

    private static Vector2 GetFlowDirection(FlowTileCacheEntry tile, int localIndex)
    {
        return DecodeFlowDirectionIndex(GetFlowDirectionIndex(tile, localIndex));
    }

    private static Vector2 ResolveRuntimeFlowDirection(FlowTileCacheEntry tile, int worldX, int worldY, int localIndex)
    {
        if (!IsFlowPathable(tile, localIndex))
            return Vector2.zero;
        if (!IsFlowReachable(tile, localIndex))
            return Vector2.zero;

        Vector2 storedDirection = GetFlowDirection(tile, localIndex);
        if (storedDirection.sqrMagnitude > 0.0001f)
            return storedDirection;
        if (tile.UsesClearFlowDescriptor)
            return ResolveClearFlowDescriptorDirection(tile, worldX, worldY);
        if (_world == null || !TryGetSectorForCell(_world, worldX, worldY, out SectorData sector) || !sector.IsClearFlowTile)
            return Vector2.zero;

        return ResolveFlowDirectionFromIntegration(tile, worldX, worldY);
    }

    private static bool IsRuntimeLineOfSightCell(FlowTileCacheEntry tile, int localIndex)
    {
        return HasFlowLineOfSight(tile, localIndex)
               && IsFlowPathable(tile, localIndex)
               && IsFlowReachable(tile, localIndex);
    }

    private static Vector2 ResolveClearFlowDescriptorDirection(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        if (tile?.GoalIntegrationSummary?.Cells == null || tile.GoalIntegrationSummary.Costs == null)
            throw new InvalidOperationException($"ResolveClearFlowDescriptorDirection failed: missing goal summary key={FormatTileKey(tile.Key)}.");
        if (tile.GoalIntegrationSummary.Cells.Length != tile.GoalIntegrationSummary.Costs.Length)
            throw new InvalidOperationException($"ResolveClearFlowDescriptorDirection failed: goal summary length mismatch key={FormatTileKey(tile.Key)}.");

        Vector2 current = new Vector2(worldX, worldY);
        int bestIndex = -1;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < tile.GoalIntegrationSummary.Cells.Length; i++)
        {
            float seedCost = tile.GoalIntegrationSummary.Costs[i];
            if (float.IsPositiveInfinity(seedCost))
                continue;

            Vector2Int seed = tile.GoalIntegrationSummary.Cells[i];
            float distance = Vector2.Distance(current, new Vector2(seed.x, seed.y));
            float score = seedCost + distance;
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex < 0)
            return Vector2.zero;

        Vector2Int bestCell = tile.GoalIntegrationSummary.Cells[bestIndex];
        Vector2 direction = new Vector2(bestCell.x - worldX, bestCell.y - worldY);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
    }

    private static float[] RequireTileIntegration(FlowTileCacheEntry tile, string caller)
    {
        if (tile == null)
            throw new InvalidOperationException($"{caller} failed: tile is null.");
        if (tile.IntegrationPayload?.Values == null)
            throw new InvalidOperationException($"{caller} failed: integration payload has been released key={FormatTileKey(tile.Key)}.");
        if (tile.IntegrationPayload.Values.Length != tile.Width * tile.Height)
            throw new InvalidOperationException($"{caller} failed: invalid integration payload length={tile.IntegrationPayload.Values.Length} key={FormatTileKey(tile.Key)}.");

        return tile.IntegrationPayload.Values;
    }

    private static float[] RentIntegrationArray(int length)
    {
        if (length <= 0)
            throw new InvalidOperationException($"RentIntegrationArray failed: invalid length={length}.");

        if (IntegrationArrayPool.TryGetValue(length, out Stack<float[]> stack) && stack.Count > 0)
            return stack.Pop();

        return new float[length];
    }

    private static void ReturnIntegrationArray(float[] array)
    {
        if (array == null)
            return;
        if (array.Length <= 0)
            throw new InvalidOperationException("ReturnIntegrationArray failed: invalid zero-length array.");

        if (!IntegrationArrayPool.TryGetValue(array.Length, out Stack<float[]> stack))
        {
            stack = new Stack<float[]>();
            IntegrationArrayPool.Add(array.Length, stack);
        }

        if (stack.Count < 16)
            stack.Push(array);
    }

    private static void ReturnTileIntegrationPayload(FlowTileCacheEntry tile)
    {
        if (tile?.IntegrationPayload?.Values == null)
            return;

        float[] values = tile.IntegrationPayload.Values;
        tile.IntegrationPayload.Values = null;
        tile.IntegrationPayload.ReleasedFrame = GetFrameCount();
        tile.IntegrationPayload = null;
        tile.IntegrationReleased = true;
        ReturnIntegrationArray(values);
    }

    private static void ReturnTileDebugIntegrationPayload(FlowTileCacheEntry tile)
    {
        if (tile?.DebugIntegrationPayload?.Values == null)
            return;

        float[] values = tile.DebugIntegrationPayload.Values;
        tile.DebugIntegrationPayload.Values = null;
        tile.DebugIntegrationPayload = null;
        ReturnIntegrationArray(values);
    }

    private static void ReleaseTilePayloads(FlowTileCacheEntry tile)
    {
        ReturnTileIntegrationPayload(tile);
        ReturnTileDebugIntegrationPayload(tile);
    }

    private static void ClearFlowTileCache()
    {
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
            ReleaseTilePayloads(tile);

        FlowTileCache.Clear();
    }

    private static void RemoveFlowTileCacheEntry(FlowTileCacheKey key)
    {
        if (FlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile))
            ReleaseTilePayloads(tile);

        FlowTileCache.Remove(key);
    }

    private static void ReturnPendingFlowTileBuildIntegrations()
    {
        LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First;
        while (node != null)
        {
            ReturnTileIntegrationPayload(node.Value?.Tile);
            node = node.Next;
        }
    }

    private static void ReturnSharedGoalFieldBuildIntegration(SharedGoalFieldBuildJob job)
    {
        if (job?.GoalIntegration == null)
            return;

        float[] integration = job.GoalIntegration;
        job.GoalIntegration = null;
        ReturnIntegrationArray(integration);
    }

    private static void ReturnPendingSharedGoalFieldBuildIntegrations()
    {
        LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First;
        while (node != null)
        {
            ReturnSharedGoalFieldBuildIntegration(node.Value);
            node = node.Next;
        }
    }

    private static void ReturnWorldBuildPortalTransitionIntegration(WorldBuildJob job)
    {
        if (job?.PortalTransitionIntegration == null)
            return;

        ReturnIntegrationArray(job.PortalTransitionIntegration);
        job.PortalTransitionIntegration = null;
        job.PortalTransitionOpenSet = null;
        job.PortalTransitionIntegrationActive = false;
    }

    private static void ReturnRuntimeDirtyPortalTransitionIntegration(RuntimeDirtyRebuildJob job)
    {
        if (job?.PortalTransitionIntegration == null)
            return;

        ReturnIntegrationArray(job.PortalTransitionIntegration);
        job.PortalTransitionIntegration = null;
        job.PortalTransitionOpenSet = null;
        job.PortalTransitionIntegrationActive = false;
    }

    private static bool TryGetIntegrationSummaryCost(IntegrationCostSummary summary, int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        if (summary?.Cells == null || summary.Costs == null || summary.Cells.Length != summary.Costs.Length)
            return false;

        for (int i = 0; i < summary.Cells.Length; i++)
        {
            if (summary.Cells[i].x != worldX || summary.Cells[i].y != worldY)
                continue;

            cost = summary.Costs[i];
            return true;
        }

        return false;
    }

    private static bool TryGetPortalSeamIntegrationCost(FlowTileCacheEntry tile, int portalId, int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        return tile != null
               && tile.PortalSeamIntegrationSummaries.TryGetValue(portalId, out IntegrationCostSummary summary)
               && TryGetIntegrationSummaryCost(summary, worldX, worldY, out cost);
    }

    private static bool TryGetTileIntegrationCost(FlowTileCacheEntry tile, int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        if (tile == null || !IsInsideSector(tile, worldX, worldY))
            return false;
        if (tile.IntegrationPayload?.Values != null)
        {
            cost = tile.IntegrationPayload.Values[tile.GetLocalIndex(worldX, worldY)];
            return true;
        }
        if (TryGetIntegrationSummaryCost(tile.GoalIntegrationSummary, worldX, worldY, out cost))
            return true;

        foreach (IntegrationCostSummary summary in tile.PortalSeamIntegrationSummaries.Values)
        {
            if (TryGetIntegrationSummaryCost(summary, worldX, worldY, out cost))
                return true;
        }

        return false;
    }

    private static bool TryRebuildTileDebugIntegration(FlowTileCacheEntry tile)
    {
        if (_world == null || tile == null)
            return false;
        if (tile.Key.SectorId < 0 || tile.Key.SectorId >= _world.Sectors.Length)
            return false;

        ReturnTileDebugIntegrationPayload(tile);
        IntegrationPayload payload = new IntegrationPayload(tile.Width * tile.Height);
        float[] integration = payload.Values;
        InitializeIntegrationField(integration);
        MinHeap openSet = new MinHeap();
        int seedCount = 0;
        seedCount += SeedDebugIntegrationSummary(tile, integration, openSet, tile.GoalIntegrationSummary);
        foreach (IntegrationCostSummary summary in tile.PortalSeamIntegrationSummaries.Values)
            seedCount += SeedDebugIntegrationSummary(tile, integration, openSet, summary);

        if (seedCount == 0)
        {
            payload.Values = null;
            ReturnIntegrationArray(integration);
            return false;
        }

        SectorData sector = _world.Sectors[tile.Key.SectorId];
        while (openSet.Count > 0)
        {
            QueueNode node = openSet.Pop();
            if (node.Cost > integration[node.Index] + 0.0001f)
                continue;

            int localX = node.Index % tile.Width;
            int localY = node.Index / tile.Width;
            int worldX = tile.StartX + localX;
            int worldY = tile.StartY + localY;
            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextWorldX = worldX + CardinalOffsetX[i];
                int nextWorldY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(tile, nextWorldX, nextWorldY) || !_world.IsWalkable(nextWorldX, nextWorldY))
                    continue;
                if (!CanTraverseNeighborCells(_world, nextWorldX, nextWorldY, worldX, worldY))
                    continue;

                int nextLocalIndex = tile.GetLocalIndex(nextWorldX, nextWorldY);
                float newCost = ResolveEikonalIntegrationCost(_world, sector, integration, nextWorldX, nextWorldY, reverseTraversal: true);
                if (!IsSignificantIntegrationImprovement(newCost, integration[nextLocalIndex]))
                    continue;

                integration[nextLocalIndex] = newCost;
                openSet.Push(nextLocalIndex, newCost);
            }
        }

        tile.DebugIntegrationPayload = payload;
        return true;
    }

    private static int SeedDebugIntegrationSummary(FlowTileCacheEntry tile, float[] integration, MinHeap openSet, IntegrationCostSummary summary)
    {
        if (summary?.Cells == null || summary.Costs == null || summary.Cells.Length != summary.Costs.Length)
            return 0;

        int count = 0;
        for (int i = 0; i < summary.Cells.Length; i++)
        {
            float cost = summary.Costs[i];
            if (float.IsPositiveInfinity(cost) || float.IsNaN(cost))
                continue;

            Vector2Int cell = summary.Cells[i];
            if (!IsInsideSector(tile, cell.x, cell.y))
                continue;

            int localIndex = tile.GetLocalIndex(cell.x, cell.y);
            if (cost >= integration[localIndex])
                continue;

            integration[localIndex] = cost;
            openSet.Push(localIndex, cost);
            count++;
        }

        return count;
    }

    private static float GetTileIntegrationCostForDiagnostics(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return TryGetTileIntegrationCost(tile, worldX, worldY, out float cost)
            ? cost
            : float.PositiveInfinity;
    }

    private static bool TryGetTileIntegrationCostForDiagnostics(
        FlowTileCacheEntry tile,
        int worldX,
        int worldY,
        out float cost,
        out string source)
    {
        cost = float.PositiveInfinity;
        source = "missing";
        if (tile == null || !IsInsideSector(tile, worldX, worldY))
            return false;

        int localIndex = tile.GetLocalIndex(worldX, worldY);
        if (tile.IntegrationPayload?.Values != null)
        {
            cost = tile.IntegrationPayload.Values[localIndex];
            source = "runtime";
            return true;
        }

        if (tile.DebugIntegrationPayload?.Values != null)
        {
            cost = tile.DebugIntegrationPayload.Values[localIndex];
            source = "debug";
            return true;
        }

        if (TryGetIntegrationSummaryCost(tile.GoalIntegrationSummary, worldX, worldY, out cost))
        {
            source = "goal-summary";
            return true;
        }

        foreach (KeyValuePair<int, IntegrationCostSummary> pair in tile.PortalSeamIntegrationSummaries)
        {
            if (!TryGetIntegrationSummaryCost(pair.Value, worldX, worldY, out cost))
                continue;

            source = "portal-seam:" + pair.Key;
            return true;
        }

        return false;
    }

    private static void SetFlowDirectionIndex(FlowTileCacheEntry tile, int localIndex, byte directionIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        if ((directionIndex & ~FlowDirectionMask) != 0)
            throw new InvalidOperationException($"SetFlowDirectionIndex failed: invalid directionIndex={directionIndex}.");

        tile.FlowFieldValues[localIndex] = (byte)((tile.FlowFieldValues[localIndex] & ~FlowDirectionMask) | directionIndex);
    }

    private static bool HasFlowLineOfSight(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (tile.FlowFieldValues[localIndex] & FlowLineOfSightFlag) != 0;
    }

    private static void SetFlowLineOfSight(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        tile.FlowFieldValues[localIndex] |= FlowLineOfSightFlag;
    }

    private static bool IsFlowWaveFrontBlocked(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (tile.FlowFieldValues[localIndex] & FlowWaveFrontBlockedFlag) != 0;
    }

    private static void SetFlowWaveFrontBlocked(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        tile.FlowFieldValues[localIndex] |= FlowWaveFrontBlockedFlag;
    }

    private static bool IsFlowPathable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (tile.FlowFieldValues[localIndex] & FlowPathableFlag) != 0;
    }

    private static void SetFlowPathable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        tile.FlowFieldValues[localIndex] |= FlowPathableFlag;
    }

    private static bool IsFlowReachable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        return (tile.FlowFieldValues[localIndex] & FlowReachableFlag) != 0;
    }

    private static void SetFlowReachable(FlowTileCacheEntry tile, int localIndex)
    {
        ValidateFlowFieldCell(tile, localIndex);
        tile.FlowFieldValues[localIndex] |= FlowReachableFlag;
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
        public bool HasProvidedCellNavAnchors;
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
        public List<PendingSectorPortalAccess> PendingPortalAccessEntries;
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
        public List<PendingSectorPortalAccess> PendingPortalAccessEntries;
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
        public int PortalGraphMergeHits;
        public int TileBuilds;
        public int SynchronousSectorIntegrations;
        public int SharedGoalFieldBuilds;
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
        public int PathPendingSharedGoal;
        public int TilePendingBuild;
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
        CombatTargetSlotCache.Clear();
        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        SectorPathCache.Clear();
        SectorPortalAccessCache.Clear();
        SharedGoalFields.Clear();
        ReturnPendingSharedGoalFieldBuildIntegrations();
        SharedGoalFieldBuildQueue.Clear();
        PendingSharedGoalFieldBuildJobs.Clear();
        Bottlenecks.Clear();
        CorridorBottlenecks.Clear();
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            ReturnRuntimeDirtyPortalTransitionIntegration(state.RuntimeDirtyJob);
            ReturnWorldBuildPortalTransitionIntegration(state.BuildJob);
        }

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
        SetEditorTestNavigationSource(agentTypeId, width, height, cellSize, origin, walkableMask, null);
    }

    public static void SetEditorTestNavigationSource(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, Vector3[] cellNavAnchors)
    {
        SetEditorTestNavigationSource(AnyAgentTypeId, width, height, cellSize, origin, walkableMask, cellNavAnchors);
    }

    public static void SetEditorTestNavigationSource(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, Vector3[] cellNavAnchors)
    {
        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            throw new InvalidOperationException("SetEditorTestNavigationSource failed: explicit agentTypeId is Unknown. Use the overload without agentTypeId for Any test source.");
        if (walkableMask == null)
            throw new InvalidOperationException("SetEditorTestNavigationSource failed: walkableMask is null.");
        if (walkableMask.Length != width * height)
            throw new InvalidOperationException(
                $"SetEditorTestNavigationSource failed: mask length {walkableMask.Length} does not match {width}x{height}.");
        if (cellNavAnchors != null && cellNavAnchors.Length != width * height)
            throw new InvalidOperationException(
                $"SetEditorTestNavigationSource failed: anchor length {cellNavAnchors.Length} does not match {width}x{height}.");

        _testTerrainOverride = new TestTerrainOverride
        {
            AgentTypeId = agentTypeId,
            Width = width,
            Height = height,
            CellSize = cellSize,
            Origin = origin,
            WalkableMask = (bool[])walkableMask.Clone(),
            CellNavAnchors = cellNavAnchors != null ? (Vector3[])cellNavAnchors.Clone() : null
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

    public static void SetAuthoredNavigationSource(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask)
    {
        SetAuthoredNavigationSource(AnyAgentTypeId, width, height, cellSize, origin, walkableMask, null);
    }

    public static void SetAuthoredNavigationSource(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, Vector3[] cellNavAnchors = null)
    {
        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            throw new InvalidOperationException("SetAuthoredNavigationSource failed: explicit agentTypeId is Unknown.");
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"SetAuthoredNavigationSource failed: invalid size {width}x{height}.");
        if (cellSize <= 0.0001f)
            throw new InvalidOperationException($"SetAuthoredNavigationSource failed: invalid cellSize={cellSize:F4}.");
        if (walkableMask == null)
            throw new InvalidOperationException("SetAuthoredNavigationSource failed: walkableMask is null.");
        if (walkableMask.Length != width * height)
            throw new InvalidOperationException(
                $"SetAuthoredNavigationSource failed: mask length {walkableMask.Length} does not match {width}x{height}.");
        if (cellNavAnchors != null && cellNavAnchors.Length != width * height)
            throw new InvalidOperationException(
                $"SetAuthoredNavigationSource failed: anchor length {cellNavAnchors.Length} does not match {width}x{height}.");

        _testTerrainOverride = new TestTerrainOverride
        {
            AgentTypeId = agentTypeId,
            Width = width,
            Height = height,
            CellSize = cellSize,
            Origin = origin,
            WalkableMask = (bool[])walkableMask.Clone(),
            CellNavAnchors = cellNavAnchors != null ? (Vector3[])cellNavAnchors.Clone() : null
        };
        MarkWorldDirty("authored-navigation-source");
    }

    public static void ClearAuthoredNavigationSource()
    {
        _testTerrainOverride = null;
        MarkWorldDirty("authored-navigation-source-cleared");
    }

    public static bool HasAuthoredNavigationSource()
    {
        return _testTerrainOverride != null;
    }

    public static void SetConfig(GroupMoveConfig config)
    {
        if (config == null)
            return;

        bool requiresRebuild = Config.SectorSizeInCells != config.SectorSizeInCells
                               || Config.PortalNarrowWidthCells != config.PortalNarrowWidthCells
                               || Config.PortalMaxWindowWidthCells != config.PortalMaxWindowWidthCells
                               || !Mathf.Approximately(Config.NavigationCellSize, config.NavigationCellSize)
                               || !Mathf.Approximately(Config.NavigationBoundsPadding, config.NavigationBoundsPadding);

        Config.NavigationCellSize = Mathf.Max(0f, config.NavigationCellSize);
        Config.NavigationBoundsPadding = Mathf.Max(0f, config.NavigationBoundsPadding);
        Config.SectorSizeInCells = Mathf.Max(4, config.SectorSizeInCells);
        Config.PortalNarrowWidthCells = Mathf.Max(1, config.PortalNarrowWidthCells);
        Config.PortalMaxWindowWidthCells = Mathf.Max(2, config.PortalMaxWindowWidthCells);
        Config.FlowTileCacheLimit = Mathf.Max(16, config.FlowTileCacheLimit);
        Config.RuntimeRebuildBudgetMilliseconds = Mathf.Max(0.05f, config.RuntimeRebuildBudgetMilliseconds);
        Config.RequireAuthoredNavigationSource = true;
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
            ReturnRuntimeDirtyPortalTransitionIntegration(state.RuntimeDirtyJob);
            ReturnWorldBuildPortalTransitionIntegration(state.BuildJob);
            state.RuntimeDirtyJob = null;
            state.BuildJob = null;
        }

        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        SectorPathCache.Clear();
        SectorPortalAccessCache.Clear();
        SharedGoalFields.Clear();
        ReturnPendingSharedGoalFieldBuildIntegrations();
        SharedGoalFieldBuildQueue.Clear();
        PendingSharedGoalFieldBuildJobs.Clear();
        MovingTargetAnchors.Clear();
        CombatTargetSlotCache.Clear();
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
            if (!TryEnsureWorldBuilt(agentTypeId, allowSynchronousBuild: true))
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

    public static bool HasActiveNavigationAgents()
    {
        int frame = GetFrameCount();
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (agent == null || agent.IgnoreAgentCollision)
                continue;
            if (agent.HasNavigationIntent)
                return true;
            if (frame - agent.LastAvoidanceActiveFrame <= 2)
                return true;
        }

        return false;
    }

    private static List<int> CollectNavigationWorldAgentTypes()
    {
        List<int> agentTypeIds = new List<int>(8);
        HashSet<int> seen = new HashSet<int>();

#if UNITY_EDITOR
        if (_testTerrainOverride != null)
        {
            if (_testTerrainOverride.AgentTypeId != AnyAgentTypeId)
            {
                AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(_testTerrainOverride.AgentTypeId));
                return agentTypeIds;
            }

            AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(0));
            foreach (AgentRuntimeData agent in Agents.Values)
                AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agent.AgentTypeId));

            return agentTypeIds;
        }
#endif

        AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(0));
        foreach (AgentRuntimeData agent in Agents.Values)
            AddNavigationWorldAgentType(agentTypeIds, seen, ResolvePreferredAgentTypeId(agent.AgentTypeId));

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
                ReturnWorldBuildPortalTransitionIntegration(state.BuildJob);
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

        EnqueueSharedGoalFieldsForActiveAgents();
        EnqueueFlowTileBuildsForActiveAgents();
        RefreshFlowTileReferenceCounts();
        long budgetTicks = Math.Max(1L, (long)(Stopwatch.Frequency * Config.RuntimeRebuildBudgetMilliseconds / 1000.0));
        long deadlineTicks = Stopwatch.GetTimestamp() + budgetTicks;
        ProcessFlowTileBuildQueue(deadlineTicks, forceComplete: false, requiredKey: null);
        if (Stopwatch.GetTimestamp() >= deadlineTicks)
            return;

        ProcessSharedGoalFieldBuildQueue(deadlineTicks, forceComplete: false, requiredKey: null);
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

    private static void EnqueueSharedGoalFieldsForActiveAgents()
    {
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            if (agent.NavState.StableGoalX < 0 || agent.NavState.StableGoalY < 0)
                continue;
            if (agent.NavState.CurrentSectorId < 0)
                continue;
            if (!_world.TryGetSectorId(agent.NavState.StableGoalX, agent.NavState.StableGoalY, out int goalSectorId))
                continue;
            if (goalSectorId == agent.NavState.CurrentSectorId)
                continue;

            EnqueueSharedGoalFieldBuild(goalSectorId, agent.NavState.StableGoalX, agent.NavState.StableGoalY, ResolvePreferredAgentTypeId(agent.AgentTypeId));
        }
    }

    private static void ProcessSharedGoalFieldBuildQueue(long deadlineTicks, bool forceComplete, SharedGoalFieldKey? requiredKey)
    {
        int guard = 0;
        while (SharedGoalFieldBuildQueue.Count > 0)
        {
            SharedGoalFieldBuildJob job = SharedGoalFieldBuildQueue.First.Value;
            SharedGoalFieldBuildQueue.RemoveFirst();
            PendingSharedGoalFieldBuildJobs.Remove(job.Key);

            if (SharedGoalFields.ContainsKey(job.Key))
            {
                ReturnSharedGoalFieldBuildIntegration(job);
                if (requiredKey.HasValue && SharedGoalFields.ContainsKey(requiredKey.Value))
                    return;
                continue;
            }

            if (IsSharedGoalFieldBuildJobStale(job))
            {
                ReturnSharedGoalFieldBuildIntegration(job);
                if (requiredKey.HasValue && SharedGoalFields.ContainsKey(requiredKey.Value))
                    return;
                continue;
            }

            if (!AdvanceSharedGoalFieldBuildJob(job, deadlineTicks, forceComplete))
            {
                SharedGoalFieldBuildQueue.AddFirst(job);
                PendingSharedGoalFieldBuildJobs.Add(job.Key);
                return;
            }

            if (requiredKey.HasValue && SharedGoalFields.ContainsKey(requiredKey.Value))
                return;
            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;

            guard++;
            if (guard > Config.FlowTileCacheLimit * 4 + 1024)
                throw new InvalidOperationException("ProcessSharedGoalFieldBuildQueue failed: queue processing exceeded guard.");
        }
    }

    private static bool IsSharedGoalFieldBuildJobStale(SharedGoalFieldBuildJob job)
    {
        if (job == null || _world == null)
            return true;
        if (job.GoalSectorId < 0 || job.GoalSectorId >= _world.Sectors.Length)
            return true;

        SharedGoalFieldKey expectedKey = CreateSharedGoalFieldKey(job.GoalSectorId, job.GoalX, job.GoalY, job.AgentTypeId);
        return !expectedKey.Equals(job.Key);
    }

    private static void ProcessFlowTileBuildQueue(long deadlineTicks, bool forceComplete, FlowTileCacheKey? requiredKey)
    {
        int guard = 0;
        while (FlowTileBuildQueue.Count > 0)
        {
            FlowTileBuildJob job = FlowTileBuildQueue.First.Value;
            FlowTileBuildQueue.RemoveFirst();
            PendingFlowTileBuildJobs.Remove(job.BuildKey.CacheKey);

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
                PendingFlowTileBuildJobs.Add(job.BuildKey.CacheKey);
                if (!forceComplete)
                    return;
                if (requiredKey.HasValue && FlowTileCache.ContainsKey(requiredKey.Value))
                    return;
                guard++;
                if (guard > Config.FlowTileCacheLimit * 8 + 1024)
                    throw new InvalidOperationException("ProcessFlowTileBuildQueue failed: force-complete dependency processing exceeded guard.");
                continue;
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

            if (job.Stage == FlowTileBuildStage.Complete)
                break;

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
            IntegrationPayload = new IntegrationPayload(job.Sector.Width * job.Sector.Height),
            FlowFieldValues = new byte[job.Sector.Width * job.Sector.Height],
            PortalTargets = key.GoalKind == TileGoalKind.Portal ? new CachedPortalTarget[job.Sector.Width * job.Sector.Height] : null,
            GoalCells = goalCells,
            UsesClearFlowDescriptor = job.Sector.IsClearFlowTile
        };
        InitializeIntegrationField(job.Tile.Integration);
        job.LineOfSightOpenSet = new MinHeap();
        SeedFinalGoalLineOfSightPass(
            job.Tile,
            job.HandleSnapshot,
            job.BuildKey.SectorPathIndex,
            job.BuildKey.GoalX,
            job.BuildKey.GoalY,
            job.BuildKey.AgentTypeId,
            job.Seeds,
            job.LineOfSightOpenSet,
            job.DownstreamTile);
        job.Stage = FlowTileBuildStage.LineOfSight;
        return true;
    }

    private static bool AdvanceFlowTileLineOfSight(FlowTileBuildJob job, long deadlineTicks, bool forceComplete)
    {
        int budgetWork = 0;
        while (job.LineOfSightOpenSet.Count > 0)
        {
            QueueNode node = job.LineOfSightOpenSet.Pop();
            int localIndex = node.Index;
            if (node.Cost > job.Tile.Integration[localIndex] + 0.001f)
                continue;

            int worldX = job.Tile.StartX + localIndex % job.Tile.Width;
            int worldY = job.Tile.StartY + localIndex / job.Tile.Width;
            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextX = worldX + CardinalOffsetX[i];
                int nextY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(job.Tile, nextX, nextY))
                    continue;

                int nextIndex = job.Tile.GetLocalIndex(nextX, nextY);
                if (IsFlowWaveFrontBlocked(job.Tile, nextIndex))
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
                float newCost = job.Tile.Integration[localIndex] + 1f;
                if (!IsSignificantIntegrationImprovement(newCost, job.Tile.Integration[nextIndex]))
                    continue;
                if (!HasStrictLineOfSightToAnyTileGoal(job.Tile, nextX, nextY))
                    continue;

                SetFlowLineOfSight(job.Tile, nextIndex);
                job.Tile.Integration[nextIndex] = newCost;
                job.LineOfSightOpenSet.Push(nextIndex, newCost);
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, ++budgetWork))
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
                && IsFlowWaveFrontBlocked(job.Tile, i))
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
            if (seedCost > job.Tile.Integration[localIndex] + 0.0001f)
                continue;

            if (seedCost < job.Tile.Integration[localIndex])
                job.Tile.Integration[localIndex] = seedCost;
            job.IntegrationOpenSet.Push(localIndex, seedCost);
        }
    }

    private static bool AdvanceFlowTileIntegration(FlowTileBuildJob job, long deadlineTicks, bool forceComplete)
    {
        int budgetWork = 0;
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
                if (HasFlowLineOfSight(job.Tile, nextLocalIndex))
                    continue;

                float newCost = ResolveEikonalIntegrationCost(_world, job.Sector, job.Tile.Integration, nextWorldX, nextWorldY, reverseTraversal: true);
                if (!IsSignificantIntegrationImprovement(newCost, job.Tile.Integration[nextLocalIndex]))
                    continue;

                job.Tile.Integration[nextLocalIndex] = newCost;
                job.IntegrationOpenSet.Push(nextLocalIndex, newCost);
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, ++budgetWork))
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
            bool pathable = _world.IsWalkable(worldX, worldY);
            if (pathable)
                SetFlowPathable(job.Tile, localIndex);
            if (!float.IsPositiveInfinity(job.Tile.Integration[localIndex]))
                SetFlowReachable(job.Tile, localIndex);

            bool useDescriptorDirection = job.Tile.UsesClearFlowDescriptor && !IsTileGoalCell(job.Tile, worldX, worldY);
            bool reachable = !float.IsPositiveInfinity(job.Tile.Integration[localIndex]);
            if (pathable
                && reachable
                && !HasFlowLineOfSight(job.Tile, localIndex)
                && !useDescriptorDirection
                && !IsTileGoalCell(job.Tile, worldX, worldY))
            {
                Vector2 integrationDirection = ResolveFlowDirectionFromIntegration(job.Tile, worldX, worldY);
                if (integrationDirection.sqrMagnitude > 0.0001f || GetFlowDirectionIndex(job.Tile, localIndex) == 0)
                    SetFlowDirectionIndex(job.Tile, localIndex, EncodeFlowDirectionIndex(integrationDirection));
            }
            else
            {
                SetFlowDirectionIndex(job.Tile, localIndex, 0);
            }
            if (job.Tile.PortalTargets != null)
                job.Tile.PortalTargets[localIndex] = ResolveCachedPortalTarget(job.Tile, worldX, worldY);

            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.CellCursor))
                return false;
        }

        job.PortalHandoffCursor = 0;
        job.Stage = FlowTileBuildStage.PortalHandoff;
        return true;
    }

    private static bool IsTileGoalCell(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        if (tile?.GoalCells == null)
            return false;

        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            if (tile.GoalCells[i].x == worldX && tile.GoalCells[i].y == worldY)
                return true;
        }

        return false;
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
                    SetFlowDirectionIndex(job.Tile, job.Tile.GetLocalIndex(currentCell.x, currentCell.y), EncodeFlowDirectionIndex(new Vector2(direction.x, direction.z)));
                }
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.PortalHandoffCursor))
                return false;
        }

        job.Stage = FlowTileBuildStage.Commit;
        return true;
    }

    private static void CommitFlowTileBuildJob(FlowTileBuildJob job)
    {
        FinalizeFlowTileIntegrationForRuntime(job.Tile);
        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowTileBuild] key={FormatTileKey(job.Tile.Key)} sectorPathIndex={job.BuildKey.SectorPathIndex} sectorRect=({job.Tile.StartX},{job.Tile.StartY},{job.Tile.Width},{job.Tile.Height}) " +
                $"goalCells={FormatGoalCells(job.Tile.GoalCells)} seeds={FormatSeeds(job.Seeds)} tileSummary={DescribeTileIntegrationSummary(job.Tile)}");
        }

        FlowTileCache[job.BuildKey.CacheKey] = job.Tile;
        job.Tile.LastUsedFrame = GetFrameCount();
        RefreshFlowTileReferenceCounts();
        TrimTileCache();
        job.Stage = FlowTileBuildStage.Complete;
    }

    private static void FinalizeFlowTileIntegrationForRuntime(FlowTileCacheEntry tile)
    {
        if (tile == null)
            throw new InvalidOperationException("FinalizeFlowTileIntegrationForRuntime failed: tile is null.");

        float[] integration = RequireTileIntegration(tile, nameof(FinalizeFlowTileIntegrationForRuntime));
        ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease(tile, integration);
        tile.GoalIntegrationSummary = BuildIntegrationCostSummary(tile, tile.GoalCells, integration);
        BuildPortalSeamIntegrationSummaries(tile, integration);
        if (!tile.UsesClearFlowDescriptor)
            EnsureStoredFlowDirectionsBeforeIntegrationRelease(tile, integration);
        LogStoredFlowIntegrationInvariantDiagnostics(tile, integration);
        ReturnTileIntegrationPayload(tile);
    }

    private static void ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease(FlowTileCacheEntry tile, float[] integration)
    {
        if (integration == null || integration.Length != tile.Width * tile.Height)
            throw new InvalidOperationException($"ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease failed: invalid integration key={FormatTileKey(tile.Key)}.");

        for (int localIndex = 0; localIndex < tile.Width * tile.Height; localIndex++)
        {
            bool reachable = !float.IsPositiveInfinity(integration[localIndex]);
            bool hasReachableFlag = IsFlowReachable(tile, localIndex);
            if (reachable != hasReachableFlag)
            {
                int worldX = tile.StartX + localIndex % tile.Width;
                int worldY = tile.StartY + localIndex / tile.Width;
                throw new InvalidOperationException(
                    $"ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease failed: reachable flag mismatch key={FormatTileKey(tile.Key)} " +
                    $"cell=({worldX},{worldY}) cost={(reachable ? integration[localIndex].ToString("F3") : "INF")} flag={hasReachableFlag}.");
            }

            if (HasFlowLineOfSight(tile, localIndex) && !reachable)
            {
                int worldX = tile.StartX + localIndex % tile.Width;
                int worldY = tile.StartY + localIndex / tile.Width;
                throw new InvalidOperationException(
                    $"ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease failed: LOS cell is unreachable key={FormatTileKey(tile.Key)} cell=({worldX},{worldY}).");
            }

            if (HasFlowLineOfSight(tile, localIndex))
            {
                int worldX = tile.StartX + localIndex % tile.Width;
                int worldY = tile.StartY + localIndex / tile.Width;
                if (!HasStrictLineOfSightToAnyTileGoal(tile, worldX, worldY))
                {
                    throw new InvalidOperationException(
                        $"ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease failed: LOS cell cannot see tile goal key={FormatTileKey(tile.Key)} " +
                        $"cell=({worldX},{worldY}) goalCells={FormatGoalCells(tile.GoalCells)}.");
                }
            }

            if (GetFlowDirectionIndex(tile, localIndex) != 0 && !reachable)
            {
                int worldX = tile.StartX + localIndex % tile.Width;
                int worldY = tile.StartY + localIndex / tile.Width;
                throw new InvalidOperationException(
                    $"ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease failed: flow direction cell is unreachable key={FormatTileKey(tile.Key)} cell=({worldX},{worldY}).");
            }

            int cellX = tile.StartX + localIndex % tile.Width;
            int cellY = tile.StartY + localIndex / tile.Width;
            bool needsRuntimeDirection = reachable
                                         && IsFlowPathable(tile, localIndex)
                                         && !HasFlowLineOfSight(tile, localIndex)
                                         && !IsTileGoalCell(tile, cellX, cellY)
                                         && !tile.UsesClearFlowDescriptor;
            if (needsRuntimeDirection
                && GetFlowDirectionIndex(tile, localIndex) == 0
                && ResolveFlowDirectionFromIntegration(tile, cellX, cellY).sqrMagnitude > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"ValidateFlowTileRuntimeFlagsBeforeIntegrationRelease failed: reachable non-LOS cell has zero flow key={FormatTileKey(tile.Key)} " +
                    $"cell=({cellX},{cellY}) waveBlocked={IsFlowWaveFrontBlocked(tile, localIndex)} cost={integration[localIndex]:F3}.");
            }
        }
    }

    private static void EnsureStoredFlowDirectionsBeforeIntegrationRelease(FlowTileCacheEntry tile, float[] integration)
    {
        if (!RequiresStoredFlowDirectionBackfill(tile))
            return;

        for (int localIndex = 0; localIndex < tile.Width * tile.Height; localIndex++)
        {
            if (!IsFlowPathable(tile, localIndex)
                || !IsFlowReachable(tile, localIndex)
                || HasFlowLineOfSight(tile, localIndex)
                || GetFlowDirectionIndex(tile, localIndex) != 0)
            {
                continue;
            }

            int worldX = tile.StartX + localIndex % tile.Width;
            int worldY = tile.StartY + localIndex / tile.Width;
            if (IsTileGoalCell(tile, worldX, worldY))
                continue;

            Vector2 direction = ResolveFlowDirectionFromIntegration(tile, worldX, worldY);
            if (direction.sqrMagnitude > 0.0001f)
                SetFlowDirectionIndex(tile, localIndex, EncodeFlowDirectionIndex(direction));
        }
    }

    private static bool RequiresStoredFlowDirectionBackfill(FlowTileCacheEntry tile)
    {
        for (int localIndex = 0; localIndex < tile.Width * tile.Height; localIndex++)
        {
            if (!IsFlowPathable(tile, localIndex)
                || !IsFlowReachable(tile, localIndex))
            {
                continue;
            }

            int worldX = tile.StartX + localIndex % tile.Width;
            int worldY = tile.StartY + localIndex / tile.Width;
            if (!IsTileGoalCell(tile, worldX, worldY) && GetFlowDirectionIndex(tile, localIndex) == 0)
                return true;
        }

        return false;
    }

    private static void LogStoredFlowIntegrationInvariantDiagnostics(FlowTileCacheEntry tile, float[] integration)
    {
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;
        if (tile == null)
            throw new InvalidOperationException("LogStoredFlowIntegrationInvariantDiagnostics failed: tile is null.");
        if (integration == null || integration.Length != tile.Width * tile.Height)
            throw new InvalidOperationException($"LogStoredFlowIntegrationInvariantDiagnostics failed: invalid integration key={FormatTileKey(tile.Key)}.");

        const int maxLogsPerTile = 4;
        int logged = 0;
        for (int localIndex = 0; localIndex < tile.Width * tile.Height; localIndex++)
        {
            if (!IsFlowPathable(tile, localIndex)
                || !IsFlowReachable(tile, localIndex)
                || IsTileGoalCell(tile, tile.StartX + localIndex % tile.Width, tile.StartY + localIndex / tile.Width))
            {
                continue;
            }

            byte directionIndex = GetFlowDirectionIndex(tile, localIndex);
            if (directionIndex == 0)
                continue;

            float currentCost = integration[localIndex];
            if (float.IsPositiveInfinity(currentCost))
                continue;

            int worldX = tile.StartX + localIndex % tile.Width;
            int worldY = tile.StartY + localIndex / tile.Width;
            Vector2 storedDirection = DecodeFlowDirectionIndex(directionIndex);
            int nextX = worldX + Math.Sign(storedDirection.x);
            int nextY = worldY + Math.Sign(storedDirection.y);
            float nextCost = float.PositiveInfinity;
            bool nextInside = IsInsideSector(tile, nextX, nextY);
            bool nextWalkable = nextInside && _world.IsWalkable(nextX, nextY);
            bool nextTraversable = nextWalkable && CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY);
            if (nextTraversable)
                nextCost = integration[tile.GetLocalIndex(nextX, nextY)];

            Vector2 bestDirection = ResolveFlowDirectionFromIntegration(tile, worldX, worldY);
            int bestX = worldX + Math.Sign(bestDirection.x);
            int bestY = worldY + Math.Sign(bestDirection.y);
            float bestCost = currentCost;
            if (bestDirection.sqrMagnitude > 0.0001f && IsInsideSector(tile, bestX, bestY))
                bestCost = integration[tile.GetLocalIndex(bestX, bestY)];

            bool storedDescends = nextTraversable && nextCost < currentCost - 0.0001f;
            bool bestDiffers = bestDirection.sqrMagnitude > 0.0001f
                               && (bestX != nextX || bestY != nextY)
                               && bestCost < nextCost - 0.0001f;
            if (storedDescends && !bestDiffers)
                continue;

            int finalGoalX = 0;
            int finalGoalY = 0;
            TryDecodeExactFinalGoalIndex(tile.Key.FinalGoalIndex, out finalGoalX, out finalGoalY);
            Debug.LogWarning(
                $"[FlowStoredDirInvariantDiag] key={FormatTileKey(tile.Key)} cell=({worldX},{worldY}) cost={FormatDiagnosticCost(currentCost)} " +
                $"stored={storedDirection} next=({nextX},{nextY}) nextInside={nextInside} nextWalk={nextWalkable} nextTrav={nextTraversable} nextCost={FormatDiagnosticCost(nextCost)} " +
                $"best={bestDirection} bestCell=({bestX},{bestY}) bestCost={FormatDiagnosticCost(bestCost)} los={HasFlowLineOfSight(tile, localIndex)} waveBlocked={IsFlowWaveFrontBlocked(tile, localIndex)} " +
                $"goalCells={FormatGoalCells(tile.GoalCells)} portalTarget={(tile.PortalTargets != null ? BuildPortalTargetSelectionDiagnostics(tile, worldX, worldY, finalGoalX, finalGoalY) : "none")}");

            logged++;
            if (logged >= maxLogsPerTile)
                return;
        }
    }

    private static IntegrationCostSummary BuildIntegrationCostSummary(FlowTileCacheEntry tile, Vector2Int[] cells, float[] integration)
    {
        if (cells == null || cells.Length == 0)
            return new IntegrationCostSummary { Cells = Array.Empty<Vector2Int>(), Costs = Array.Empty<float>() };

        IntegrationCostSummary summary = new IntegrationCostSummary
        {
            Cells = new Vector2Int[cells.Length],
            Costs = new float[cells.Length]
        };

        for (int i = 0; i < cells.Length; i++)
        {
            Vector2Int cell = cells[i];
            summary.Cells[i] = cell;
            summary.Costs[i] = IsInsideSector(tile, cell.x, cell.y)
                ? integration[tile.GetLocalIndex(cell.x, cell.y)]
                : float.PositiveInfinity;
        }

        return summary;
    }

    private static void BuildPortalSeamIntegrationSummaries(FlowTileCacheEntry tile, float[] integration)
    {
        if (_world == null || tile.Key.SectorId < 0 || tile.Key.SectorId >= _world.Sectors.Length)
            throw new InvalidOperationException($"BuildPortalSeamIntegrationSummaries failed: invalid world or sector key={FormatTileKey(tile.Key)}.");

        SectorData sector = _world.Sectors[tile.Key.SectorId];
        tile.PortalSeamIntegrationSummaries.Clear();
        for (int i = 0; i < sector.PortalIds.Count; i++)
        {
            int portalId = sector.PortalIds[i];
            PortalData portal = GetPortalById(_world, portalId);
            Vector2Int[] cells = GetPortalCellsForSector(portal, sector.SectorId);
            tile.PortalSeamIntegrationSummaries[portalId] = BuildIntegrationCostSummary(tile, cells, integration);
        }
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
        agent.AgentTypeId = ResolveExplicitAgentTypeId(entity, nameof(RegisterAgent));
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
        agent.AgentTypeId = ResolveExplicitAgentTypeId(entity, nameof(UpdateAgent));
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

    public static void SetAgentState(int agentId, FlowFieldAgentState state)
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

    public static bool TryGetEditorTestLastSteeringSource(int agentId, out int source, out bool hasLineOfSight)
    {
        source = (int)DesiredDirectionSource.Zero;
        hasLineOfSight = false;
        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent))
            return false;

        source = (int)agent.NavState.LastSteeringDesiredSource;
        hasLineOfSight = agent.NavState.LastSteeringHasLineOfSight;
        return agent.NavState.LastSteeringFrame >= 0;
    }

    public static int GetEditorTestFlowTileCacheCount()
    {
        return FlowTileCache.Count;
    }

    public static int GetEditorTestReleasedIntegrationTileCount()
    {
        int count = 0;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (tile.IntegrationReleased && tile.IntegrationPayload?.Values == null)
                count++;
        }

        return count;
    }

    public static int GetEditorTestDebugIntegrationPayloadTileCount()
    {
        int count = 0;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (tile.DebugIntegrationPayload?.Values != null)
                count++;
        }

        return count;
    }

    public static bool TryRebuildEditorDebugTileIntegration(int worldX, int worldY)
    {
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY))
                continue;

            return TryRebuildTileDebugIntegration(tile);
        }

        return false;
    }

    public static bool TryGetEditorTestDebugIntegrationCost(int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.DebugIntegrationPayload?.Values == null)
                continue;

            cost = tile.DebugIntegrationPayload.Values[tile.GetLocalIndex(worldX, worldY)];
            return true;
        }

        return false;
    }

    public static int GetEditorTestIntegrationArrayPoolCount()
    {
        int count = 0;
        foreach (Stack<float[]> stack in IntegrationArrayPool.Values)
            count += stack.Count;

        return count;
    }

    public static void ClearEditorTestFlowTileCache()
    {
        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
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

    public static bool HasEditorTestPendingRuntimeDirty()
    {
        foreach (WorldRuntimeState state in WorldStates.Values)
        {
            if (state == null)
                continue;
            if (state.RuntimeDirtyJob != null || state.DirtyRuntimeObstacleSectors.Count > 0)
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

    public static int GetEditorTestFrameSynchronousSectorIntegrationCount()
    {
        return _perf.SynchronousSectorIntegrations;
    }

    public static int GetEditorTestFrameSharedGoalFieldBuildCount()
    {
        return _perf.SharedGoalFieldBuilds;
    }

    public static int GetEditorTestFramePortalGraphMergeHitCount()
    {
        return _perf.PortalGraphMergeHits;
    }

    public static bool TryGetEditorTestPathPortalIds(int entityId, out int[] portalIds)
    {
        portalIds = null;
        if (!Agents.TryGetValue(entityId, out AgentRuntimeData agent))
            return false;

        PathHandle handle = agent.NavState.PathHandle;
        if (handle?.PortalIds == null)
            return false;

        portalIds = (int[])handle.PortalIds.Clone();
        return true;
    }

    public static int GetEditorTestPendingFlowTileBuildCount()
    {
        return PendingFlowTileBuildJobs.Count;
    }

    public static int GetEditorTestDuplicatePendingFlowTileBuildKeyCount()
    {
        HashSet<FlowTileCacheKey> seen = new HashSet<FlowTileCacheKey>();
        int duplicateCount = 0;
        LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First;
        while (node != null)
        {
            FlowTileCacheKey key = node.Value.BuildKey.CacheKey;
            if (!seen.Add(key))
                duplicateCount++;

            node = node.Next;
        }

        return duplicateCount;
    }

    public static int GetEditorTestPendingSharedGoalFieldBuildCount()
    {
        return PendingSharedGoalFieldBuildJobs.Count;
    }

    public static int GetEditorTestSectorPortalAccessCacheCount()
    {
        return SectorPortalAccessCache.Count;
    }

    public static bool HasEditorTestCommittedFullCostField()
    {
        return _world?.CostField != null;
    }

    public static int GetEditorTestCommittedSectorCostChunkCount()
    {
        if (_world?.SectorCostFields == null)
            return 0;

        int count = 0;
        for (int i = 0; i < _world.SectorCostFields.Length; i++)
        {
            if (_world.SectorCostFields[i] != null)
                count++;
        }

        return count;
    }

    public static int GetEditorTestQuantizedSectorPortalAccessCacheCount()
    {
        int count = 0;
        foreach (SectorPortalAccessEntry entry in SectorPortalAccessCache.Values)
        {
            if (entry?.QuantizedIntegration != null && entry.IntegrationScale > 0f)
                count++;
        }

        return count;
    }

    public static bool TryGetEditorTestFirstPortalForSector(int sectorId, out int portalId, out int oppositeSectorId)
    {
        portalId = -1;
        oppositeSectorId = -1;
        if (_world == null || sectorId < 0 || sectorId >= _world.Sectors.Length)
            return false;

        SectorData sector = _world.Sectors[sectorId];
        if (sector.PortalIds.Count == 0)
            return false;

        portalId = sector.PortalIds[0];
        if (!TryGetPortalById(_world, portalId, out PortalData portal))
            return false;

        oppositeSectorId = GetOppositeSectorId(portal, sectorId);
        return oppositeSectorId >= 0;
    }

    public static bool TryGetEditorTestSectorPortalAccessCost(int sectorId, int portalId, int worldX, int worldY, out float cost)
    {
        cost = float.PositiveInfinity;
        if (_world == null || sectorId < 0 || sectorId >= _world.Sectors.Length)
            return false;

        SectorData sector = _world.Sectors[sectorId];
        if (!IsInsideSector(sector, worldX, worldY))
            return false;

        SectorPortalAccessEntry entry = GetPrebuiltSectorPortalAccess(sector, sectorId, portalId);
        cost = DecodePortalAccessIntegrationCost(entry, GetSectorLocalIndex(sector, worldX, worldY));
        return true;
    }

    public static bool TryGetEditorTestCachedTileCellLineOfSightState(int worldX, int worldY, out bool hasLineOfSight, out bool waveFrontBlocked)
    {
        return TryGetEditorTestCachedTileCellFlags(worldX, worldY, out hasLineOfSight, out waveFrontBlocked, out _);
    }

    public static bool TryGetEditorTestCachedTileCellFlags(int worldX, int worldY, out bool hasLineOfSight, out bool waveFrontBlocked, out bool pathable)
    {
        hasLineOfSight = false;
        waveFrontBlocked = false;
        pathable = false;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY))
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            hasLineOfSight = HasFlowLineOfSight(tile, localIndex);
            waveFrontBlocked = IsFlowWaveFrontBlocked(tile, localIndex);
            pathable = IsFlowPathable(tile, localIndex);
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestCachedTileCellFlowDirection(int worldX, int worldY, out Vector2 direction)
    {
        direction = Vector2.zero;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowFieldValues == null)
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            direction = ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex);
            return true;
        }

        return false;
    }

    public static bool TryGetEditorTestCachedTileCellStoredFlowDirection(int worldX, int worldY, out Vector2 direction)
    {
        direction = Vector2.zero;
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowFieldValues == null)
                continue;

            direction = GetFlowDirection(tile, tile.GetLocalIndex(worldX, worldY));
            return true;
        }

        return false;
    }

    public static string GetEditorTestCachedTileCellDiagnostic(int worldX, int worldY)
    {
        if (_world == null)
            return "world=null";

        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.FlowFieldValues == null)
                continue;

            int localIndex = tile.GetLocalIndex(worldX, worldY);
            byte cost = TryGetCostFieldValue(_world, worldX, worldY, out byte resolvedCost) ? resolvedCost : (byte)0;
            TryGetSectorForCell(_world, worldX, worldY, out SectorData sector);
            float integration = GetTileIntegrationCostForDiagnostics(tile, worldX, worldY);
            string integrationText = float.IsPositiveInfinity(integration) ? "INF/released" : integration.ToString("F3");
            return $"cell=({worldX},{worldY}) cost={cost} integration={integrationText} " +
                   $"pathable={IsFlowPathable(tile, localIndex)} los={HasFlowLineOfSight(tile, localIndex)} waveBlocked={IsFlowWaveFrontBlocked(tile, localIndex)} " +
                   $"stored={GetFlowDirection(tile, localIndex)} runtime={ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex)} " +
                   $"sectorClearCost={(sector != null && sector.IsClearCostField)} sectorClearFlow={(sector != null && sector.IsClearFlowTile)} " +
                   $"tile={FormatTileKey(tile.Key)}";
        }

        return $"cell=({worldX},{worldY}) tile=missing";
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

    public static bool TryGetEditorTestResolvedPortalTargets(int worldX, int worldY, out Vector3 handoffTarget, out Vector3 lineOfSightTarget)
    {
        handoffTarget = Vector3.zero;
        lineOfSightTarget = Vector3.zero;
        if (_world == null)
            return false;

        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
        {
            if (!IsInsideSector(tile, worldX, worldY) || tile.Key.GoalKind != TileGoalKind.Portal || tile.PortalTargets == null)
                continue;

            PortalData portal = GetPortalById(_world, tile.Key.GoalId);
            Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
            CachedPortalTarget cached = tile.PortalTargets[tile.GetLocalIndex(worldX, worldY)];
            handoffTarget = cached.TargetPosition;
            lineOfSightTarget = ResolvePortalLineOfSightTarget(cached, currentSideCells);
            return true;
        }

        return false;
    }

    public static int GetEditorTestSharedGoalFieldCacheCount()
    {
        return SharedGoalFields.Count;
    }

    public static string GetEditorTestStartPortalChoiceDiagnostics(int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY, int agentTypeId)
    {
        if (_world == null)
            return "portalChoice=world-null";

        return BuildStartPortalChoiceDiagnostics(
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            agentTypeId,
            _world.GridToWorldCenter(startX, startY),
            _world.GridToWorldCenter(goalX, goalY));
    }

    public static bool TryGetEditorTestPathRebuildDecision(
        int agentId,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out bool shouldRebuild,
        out string reason,
        out string diagnostics)
    {
        shouldRebuild = false;
        reason = string.Empty;
        diagnostics = "unavailable";
        if (_world == null)
        {
            diagnostics = "world-null";
            return false;
        }

        if (!Agents.TryGetValue(agentId, out AgentRuntimeData agent) || agent?.NavState.PathHandle == null)
        {
            diagnostics = "handle-null";
            return false;
        }

        PathHandle handle = agent.NavState.PathHandle;
        shouldRebuild = ShouldRebuildPathHandleForCurrentStart(
            handle,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            out reason);
        diagnostics = BuildPathRepathDecisionDiagnostics(handle, startSectorId, goalSectorId, startX, startY, goalX, goalY);
        return true;
    }

    public static bool TryGetEditorTestCostFieldValue(int worldX, int worldY, out byte cost)
    {
        cost = 0;
        if (_world == null || !_world.WorldToGrid(_world.GridToWorldCenter(worldX, worldY), out _, out _))
            return false;
        if (worldX < 0 || worldX >= _world.Width || worldY < 0 || worldY >= _world.Height)
            return false;
        if (!TryGetCostFieldValue(_world, worldX, worldY, out cost))
            return false;

        return true;
    }

    public static bool TryGetEditorTestSectorClearCostState(int worldX, int worldY, out bool isClearCostField)
    {
        isClearCostField = false;
        if (_world == null || !TryGetSectorForCell(_world, worldX, worldY, out SectorData sector))
            return false;

        isClearCostField = sector.IsClearCostField;
        return true;
    }

    public static bool TryGetEditorTestSectorClearFlowTileState(int worldX, int worldY, out bool isClearFlowTile)
    {
        isClearFlowTile = false;
        if (_world == null || !TryGetSectorForCell(_world, worldX, worldY, out SectorData sector))
            return false;

        isClearFlowTile = sector.IsClearFlowTile;
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

    public static bool TryGetEditorTestSectorUniformIslandId(int worldX, int worldY, out int uniformIslandId)
    {
        uniformIslandId = -1;
        if (_world == null || !TryGetSectorForCell(_world, worldX, worldY, out SectorData sector))
            return false;

        uniformIslandId = sector.UniformIslandId;
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

    public static void RegisterGridCostStamp(int stampId, Vector3 origin, float cellSize, int width, int height, byte[] costs)
    {
        RegisterGridCostStamp(stampId, AnyAgentTypeId, origin, cellSize, width, height, costs);
    }

    public static void RegisterGridCostStamp(int stampId, int agentTypeId, Vector3 origin, float cellSize, int width, int height, byte[] costs)
    {
        if (cellSize <= 0.0001f)
            throw new InvalidOperationException($"RegisterGridCostStamp failed: cellSize must be positive, cellSize={cellSize}.");
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"RegisterGridCostStamp failed: invalid size {width}x{height}.");
        if (costs == null)
            throw new InvalidOperationException("RegisterGridCostStamp failed: costs is null.");
        if (costs.Length != width * height)
            throw new InvalidOperationException($"RegisterGridCostStamp failed: costs length {costs.Length} does not match {width}x{height}.");

        for (int i = 0; i < costs.Length; i++)
        {
            byte cost = costs[i];
            if (cost == 0 || cost >= 255)
                throw new InvalidOperationException($"RegisterGridCostStamp failed: cost must be in 1..254 at index={i}, cost={cost}.");
        }

        Vector3 size = new Vector3(width * cellSize, 0f, height * cellSize);
        Bounds bounds = new Bounds(origin + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f), size);
        CostStamps[stampId] = new CostStamp
        {
            Id = stampId,
            AgentTypeId = agentTypeId,
            Bounds = bounds,
            Cost = 1,
            Width = width,
            Height = height,
            CellSize = cellSize,
            Origin = origin,
            Costs = (byte[])costs.Clone()
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

    public static bool TryConstrainNavigationDisplacement(
        Vector3 position,
        Vector3 desiredDisplacement,
        int agentTypeId,
        out Vector3 constrainedDisplacement)
    {
        constrainedDisplacement = Vector3.zero;
        Vector3 horizontal = desiredDisplacement;
        horizontal.y = 0f;
        if (horizontal.sqrMagnitude <= 0.000001f)
        {
            constrainedDisplacement = desiredDisplacement;
            return true;
        }

        if (!TryGetNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
            return false;

        if (IsNavigationSegmentWalkable(world, position, horizontal))
        {
            constrainedDisplacement = desiredDisplacement;
            return true;
        }

        float desiredDistance = horizontal.magnitude;
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        TrySelectNavigationDisplacementCandidate(world, position, new Vector3(horizontal.x, 0f, 0f), horizontal, desiredDistance, ref best, ref bestScore);
        TrySelectNavigationDisplacementCandidate(world, position, new Vector3(0f, 0f, horizontal.z), horizontal, desiredDistance, ref best, ref bestScore);

        Vector3 tangentA = new Vector3(-horizontal.z, 0f, horizontal.x);
        Vector3 tangentB = -tangentA;
        TrySelectNavigationDisplacementCandidate(world, position, tangentA, horizontal, desiredDistance, ref best, ref bestScore);
        TrySelectNavigationDisplacementCandidate(world, position, tangentB, horizontal, desiredDistance, ref best, ref bestScore);

        Vector3 limited = FindLongestWalkablePrefix(world, position, horizontal);
        TrySelectNavigationDisplacementCandidate(world, position, limited, horizontal, limited.magnitude, ref best, ref bestScore, preserveDistance: false);

        if (bestScore <= float.NegativeInfinity * 0.5f)
            return false;

        constrainedDisplacement = new Vector3(best.x, desiredDisplacement.y, best.z);
        return true;
    }

    public static bool TryResolveLegalNavigationPoint(
        Vector3 candidate,
        int agentTypeId,
        float maxSnapDistance,
        float edgeClearance,
        out Vector3 legalPoint)
    {
        legalPoint = Vector3.zero;
        if (!TryGetNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
            return false;

        maxSnapDistance = Mathf.Max(0f, maxSnapDistance);
        edgeClearance = Mathf.Max(0f, edgeClearance);
        if (world.WorldToGrid(candidate, out int cellX, out int cellY)
            && world.IsWalkable(cellX, cellY)
            && IsNavigationCellClear(world, cellX, cellY, edgeClearance))
        {
            legalPoint = new Vector3(candidate.x, world.GridToWorldCenter(cellX, cellY).y, candidate.z);
            return true;
        }

        int searchRadius = Mathf.CeilToInt(maxSnapDistance / Mathf.Max(world.CellSize, 0.001f)) + 1;
        float maxDistanceSq = maxSnapDistance * maxSnapDistance;
        bool found = false;
        float bestDistanceSq = float.PositiveInfinity;
        int bestX = 0;
        int bestY = 0;

        int startX = cellX;
        int startY = cellY;
        if (startX < 0 || startX >= world.Width || startY < 0 || startY >= world.Height)
        {
            startX = Mathf.Clamp(Mathf.FloorToInt((candidate.x - world.Origin.x) / world.CellSize), 0, world.Width - 1);
            startY = Mathf.Clamp(Mathf.FloorToInt((candidate.z - world.Origin.z) / world.CellSize), 0, world.Height - 1);
        }

        for (int y = startY - searchRadius; y <= startY + searchRadius; y++)
        {
            for (int x = startX - searchRadius; x <= startX + searchRadius; x++)
            {
                if (!world.IsWalkable(x, y) || !IsNavigationCellClear(world, x, y, edgeClearance))
                    continue;

                Vector3 center = world.GridToWorldCenter(x, y);
                float dx = center.x - candidate.x;
                float dz = center.z - candidate.z;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq > maxDistanceSq || distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                bestX = x;
                bestY = y;
                found = true;
            }
        }

        if (!found)
            return false;

        legalPoint = world.GridToWorldCenter(bestX, bestY);
        return true;
    }

    public static bool TryEstimateNavigationDistance(
        Vector3 from,
        Vector3 to,
        int agentTypeId,
        out float distance)
    {
        distance = 0f;
        if (!TryGetNavigationQueryWorld(agentTypeId, allowSynchronousBuild: true, out NavigationWorld world))
            return false;
        if (!world.WorldToGrid(from, out int startX, out int startY) || !world.WorldToGrid(to, out int goalX, out int goalY))
            return false;
        if (!world.IsWalkable(startX, startY) || !world.IsWalkable(goalX, goalY))
            return false;
        if (startX == goalX && startY == goalY)
            return true;

        return TryEstimateGridPathDistance(world, startX, startY, goalX, goalY, out distance);
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

        if (HasPendingRuntimeDirty(_activeWorldState))
        {
            failureReason = $"runtime dirty pending agentType={agent.AgentTypeId}";
            return false;
        }

        NavigationWorld queryWorld = _activeWorldState.World;
        if (!queryWorld.WorldToGrid(self.Position, out int startX, out int startY))
        {
            failureReason = $"start not on grid pos={self.Position}";
            return false;
        }

        if (!queryWorld.IsWalkable(startX, startY)
            && !TryResolveNearbyStartWalkable(queryWorld, self.Position, startX, startY, out startX, out startY))
        {
            failureReason = $"start blocked and no nearby walkable pos={self.Position}";
            return false;
        }

        int startIsland = ResolveIslandIdForDiagnostics(queryWorld, startX, startY);
        if (startIsland <= 0)
        {
            failureReason = $"start island invalid start=({startX},{startY}) island={startIsland}";
            return false;
        }

        if (!queryWorld.WorldToGrid(desiredGoal, out int goalX, out int goalY))
        {
            failureReason = $"goal not on grid goal={desiredGoal}";
            return false;
        }

        int maxRadiusCells = Mathf.Max(1, Mathf.CeilToInt(searchRadius / Mathf.Max(queryWorld.CellSize, 0.001f)));
        if (TryFindNearestWalkableInIslandByWorldDistance(
                queryWorld,
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
            reachableGoal = queryWorld.GridToWorldCenter(resultX, resultY);
            LogReachableGoalResolution(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, resultX, resultY, distance, maxRadiusCells, fullIsland: false);
            LogReachableGoalTargetSnapDiagnostics(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, resultX, resultY, distance, fullIsland: false);
            return true;
        }

        bool fullIslandFound = TryFindNearestWalkableInIslandByWorldDistance(
            queryWorld,
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
            queryWorld,
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
            reachableGoal = queryWorld.GridToWorldCenter(fullIslandX, fullIslandY);
            LogReachableGoalResolution(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, fullIslandX, fullIslandY, fullIslandDistance, maxRadiusCells, fullIsland: true);
            LogReachableGoalTargetSnapDiagnostics(queryWorld, self, desiredGoal, startX, startY, goalX, goalY, fullIslandX, fullIslandY, fullIslandDistance, fullIsland: true);
            return true;
        }

        failureReason =
            $"no reachable goal in start island desired={desiredGoal} desiredCell=({goalX},{goalY}) start=({startX},{startY}) startIsland={startIsland} radiusCells={maxRadiusCells}";
        return false;
    }

    public static bool TryResolveCombatApproachPoint(
        IEntityContext self,
        IEntityContext target,
        Vector3 targetPoint,
        float standOff,
        float ringSpacing,
        int ringCount,
        int candidateCount,
        float requiredClearance,
        out Vector3 approachPoint,
        out string failureReason)
    {
        approachPoint = targetPoint;
        failureReason = string.Empty;
        if (self == null)
            throw new InvalidOperationException("TryResolveCombatApproachPoint failed: self is null.");
        if (target == null)
            throw new InvalidOperationException("TryResolveCombatApproachPoint failed: target is null.");

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
            agent.AgentTypeId = self is MAEntity maEntity ? maEntity.navAgentTypeID : agent.AgentTypeId;
        }

        if (!TryEnsureWorldBuilt(agent.AgentTypeId))
        {
            failureReason = $"world unavailable agentType={agent.AgentTypeId}";
            return false;
        }

        if (HasPendingRuntimeDirty(_activeWorldState))
        {
            failureReason = $"runtime dirty pending agentType={agent.AgentTypeId}";
            return false;
        }

        if (!TryResolveStartCellForReachability(self, out int startX, out int startY, out int startIsland))
        {
            failureReason = $"start reachability failed pos={self.Position}";
            return false;
        }

        if (!_world.WorldToGrid(targetPoint, out int targetX, out int targetY))
        {
            failureReason = $"target point outside grid targetPoint={targetPoint}";
            return false;
        }

        CombatTargetSlotKey key = CreateCombatTargetSlotKey(
            target,
            agent.AgentTypeId,
            targetPoint,
            targetX,
            targetY,
            standOff,
            ringSpacing,
            ringCount,
            candidateCount);
        CombatTargetSlotEntry entry = GetOrBuildCombatTargetSlotEntry(key, targetPoint, standOff, ringSpacing, ringCount, candidateCount, targetX, targetY);
        if (entry == null || entry.Points == null || entry.Points.Length == 0)
        {
            failureReason = $"no combat approach slots target={target.CharacterKey} targetPoint={targetPoint}";
            return false;
        }

        Vector3 toTargetFromSelf = self.Position - targetPoint;
        toTargetFromSelf.y = 0f;
        if (toTargetFromSelf.sqrMagnitude <= 0.0001f)
            toTargetFromSelf = Vector3.forward;
        toTargetFromSelf.Normalize();

        int ignoredTargetId = ResolveAgentId(target);
        float bestScore = float.PositiveInfinity;
        int bestIndex = -1;
        bool bestOccupied = false;
        for (int i = 0; i < entry.Points.Length; i++)
        {
            if (entry.IslandIds[i] != startIsland)
                continue;

            Vector3 candidate = entry.Points[i];
            bool occupied = IsNavigationGoalOccupiedByOther(selfId, ignoredTargetId, candidate, requiredClearance, includeReservations: true, out _);
            Vector3 candidateDirection = candidate - targetPoint;
            candidateDirection.y = 0f;
            float anglePenalty = 0f;
            if (candidateDirection.sqrMagnitude > 0.0001f)
            {
                candidateDirection.Normalize();
                anglePenalty = Vector3.Angle(candidateDirection, toTargetFromSelf) * 0.015f;
            }

            float distanceToSelf = HorizontalDistanceXZ(self.Position, candidate);
            float distanceToTargetError = Mathf.Abs(HorizontalDistanceXZ(candidate, targetPoint) - standOff);
            float score = distanceToSelf + distanceToTargetError * 3f + anglePenalty;
            if (occupied)
                score += 1000f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
            bestOccupied = occupied;
        }

        if (bestIndex < 0)
        {
            failureReason =
                $"no combat approach slot in start island target={target.CharacterKey} start=({startX},{startY}) startIsland={startIsland} " +
                $"targetPoint={targetPoint} slots={entry.Points.Length} build={entry.BuildSummary}";
            return false;
        }

        approachPoint = entry.Points[bestIndex];
        RegisterNavigationGoalReservation(selfId, approachPoint, requiredClearance);
        if (GameDebugSettings.IsEnabled(DebugCategory.Move)
            && GameDebugSettings.ShouldLogMovementForCharacter(self.CharacterKey))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowCombatSlot] self={self.CharacterKey} target={target.CharacterKey} targetPoint={targetPoint} " +
                $"approach={approachPoint} slotIndex={bestIndex} occupied={bestOccupied} score={bestScore:F3} " +
                $"start=({startX},{startY}) startIsland={startIsland} keyTargetCell={key.TargetCellIndex} slots={entry.Points.Length}");
        }

        return true;
    }

    public static bool TryPrepareSharedGoalRequest(Vector3 goalPosition, IReadOnlyList<IEntityContext> sources, out string failureReason)
    {
        failureReason = string.Empty;
        if (sources == null)
            throw new InvalidOperationException("TryPrepareSharedGoalRequest failed: sources is null.");
        if (sources.Count == 0)
        {
            failureReason = "sources is empty";
            return false;
        }

        int preparedCount = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            IEntityContext source = sources[i];
            if (source == null)
            {
                failureReason = $"source[{i}] is null";
                return false;
            }

            int sourceId = ResolveAgentId(source);
            if (!Agents.TryGetValue(sourceId, out AgentRuntimeData agent))
            {
                RegisterSyntheticAgent(source);
                agent = Agents[sourceId];
            }
            else
            {
                agent.Position = source.Position;
                agent.Radius = ResolveCollisionRadius(source);
                agent.AgentTypeId = source is MAEntity maEntity ? maEntity.navAgentTypeID : agent.AgentTypeId;
            }

            if (!TryEnsureWorldBuilt(agent.AgentTypeId))
            {
                failureReason = $"world unavailable source={source.CharacterKey} agentType={agent.AgentTypeId}";
                return false;
            }

            if (HasPendingRuntimeDirty(_activeWorldState))
            {
                failureReason = $"runtime dirty pending source={source.CharacterKey} agentType={agent.AgentTypeId}";
                return false;
            }

            if (!_world.WorldToGrid(goalPosition, out int rawGoalX, out int rawGoalY))
            {
                failureReason = $"goal outside grid goal={goalPosition}";
                return false;
            }

            if (!TryResolveStartCellForReachability(source, out int startX, out int startY, out int startIsland))
            {
                failureReason = $"start reachability failed source={source.CharacterKey} pos={source.Position}";
                return false;
            }

            if (!TryResolveReachableGoalCell(
                    source,
                    goalPosition,
                    rawGoalX,
                    rawGoalY,
                    startX,
                    startY,
                    startIsland,
                    out int goalX,
                    out int goalY,
                    out _,
                    out int goalSectorId))
            {
                failureReason = $"goal reachability failed source={source.CharacterKey} goal={goalPosition}";
                return false;
            }

            if (!_world.TryGetSectorId(startX, startY, out int startSectorId))
            {
                failureReason = $"start sector failed source={source.CharacterKey} start=({startX},{startY})";
                return false;
            }

            agent.NavState.CurrentCell = new Vector2Int(startX, startY);
            agent.NavState.CurrentSectorId = startSectorId;
            if (!EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY))
            {
                failureReason = $"path handle failed source={source.CharacterKey} startSector={startSectorId} goalSector={goalSectorId}";
                return false;
            }

            EnqueueSharedGoalFieldBuild(goalSectorId, goalX, goalY, ResolvePreferredAgentTypeId(agent.AgentTypeId));
            EnqueueFlowTileBuildChain(agent.NavState.PathHandle, agent.NavState.PathHandle.CurrentSectorIndex, goalX, goalY, agent.AgentTypeId);
            preparedCount++;
        }

        return preparedCount == sources.Count;
    }

    private static CombatTargetSlotKey CreateCombatTargetSlotKey(
        IEntityContext target,
        int agentTypeId,
        Vector3 targetPoint,
        int targetX,
        int targetY,
        float standOff,
        float ringSpacing,
        int ringCount,
        int candidateCount)
    {
        int targetId = ResolveAgentId(target);
        float quantize = Mathf.Max(0.1f, _world.CellSize * 0.25f);
        return new CombatTargetSlotKey(
            _world.Version,
            ResolvePreferredAgentTypeId(agentTypeId),
            targetId,
            _world.GetIndex(targetX, targetY),
            Mathf.RoundToInt(targetPoint.x / quantize),
            Mathf.RoundToInt(targetPoint.z / quantize),
            Mathf.RoundToInt(standOff / Mathf.Max(0.001f, _world.CellSize) * 16f),
            Mathf.RoundToInt(ringSpacing / Mathf.Max(0.001f, _world.CellSize) * 16f),
            Mathf.Max(1, ringCount),
            Mathf.Max(4, candidateCount));
    }

    private static CombatTargetSlotEntry GetOrBuildCombatTargetSlotEntry(
        CombatTargetSlotKey key,
        Vector3 targetPoint,
        float standOff,
        float ringSpacing,
        int ringCount,
        int candidateCount,
        int targetX,
        int targetY)
    {
        if (CombatTargetSlotCache.TryGetValue(key, out CombatTargetSlotEntry cached)
            && cached?.Points != null)
        {
            cached.LastUsedFrame = GetFrameCount();
            return cached;
        }

        List<Vector3> points = new List<Vector3>(Mathf.Max(4, ringCount * candidateCount));
        List<int> cellX = new List<int>(points.Capacity);
        List<int> cellY = new List<int>(points.Capacity);
        List<int> islandIds = new List<int>(points.Capacity);
        int rejectedOutside = 0;
        int rejectedBlocked = 0;
        int rejectedNoLos = 0;
        bool targetLineCellValid = _world.IsWalkable(targetX, targetY);

        for (int ring = 0; ring < Mathf.Max(1, ringCount); ring++)
        {
            float radius = Mathf.Max(0.05f, standOff + ring * Mathf.Max(0.05f, ringSpacing));
            for (int i = 0; i < Mathf.Max(4, candidateCount); i++)
            {
                float angle = i * 360f / Mathf.Max(4, candidateCount);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                Vector3 candidate = targetPoint + direction * radius;
                if (!_world.WorldToGrid(candidate, out int x, out int y))
                {
                    rejectedOutside++;
                    continue;
                }

                if (!_world.IsWalkable(x, y))
                {
                    rejectedBlocked++;
                    continue;
                }

                int islandId = ResolveIslandIdForDiagnostics(_world, x, y);
                if (islandId <= 0)
                {
                    rejectedBlocked++;
                    continue;
                }

                if (targetLineCellValid
                    && ResolveIslandIdForDiagnostics(_world, targetX, targetY) == islandId
                    && !HasSoftCostTolerantGridLineOfSight(_world, x, y, targetX, targetY, maxAllowedCost: 15))
                {
                    rejectedNoLos++;
                    continue;
                }

                Vector3 worldPoint = _world.GridToWorldCenter(x, y);
                bool duplicate = false;
                for (int existing = 0; existing < cellX.Count; existing++)
                {
                    if (cellX[existing] == x && cellY[existing] == y)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                    continue;

                points.Add(worldPoint);
                cellX.Add(x);
                cellY.Add(y);
                islandIds.Add(islandId);
            }
        }

        CombatTargetSlotEntry entry = new CombatTargetSlotEntry
        {
            TargetPoint = targetPoint,
            Points = points.ToArray(),
            CellX = cellX.ToArray(),
            CellY = cellY.ToArray(),
            IslandIds = islandIds.ToArray(),
            LastUsedFrame = GetFrameCount(),
            BuildSummary = $"built={points.Count} rejectedOutside={rejectedOutside} rejectedBlocked={rejectedBlocked} rejectedNoLos={rejectedNoLos}"
        };
        CombatTargetSlotCache[key] = entry;
        return entry;
    }

    private static void LogReachableGoalTargetSnapDiagnostics(
        NavigationWorld world,
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
        if (world == null || self == null)
            return;

        IEntityContext target = self.TargetComp?.CurrentTarget;
        if (target == null)
            return;

        int desiredIsland = ResolveIslandIdForDiagnostics(world, goalX, goalY);
        bool targetInGrid = world.WorldToGrid(target.Position, out int targetX, out int targetY);
        int targetIsland = targetInGrid ? ResolveIslandIdForDiagnostics(world, targetX, targetY) : -1;
        if (desiredIsland == targetIsland && distance <= Mathf.Max(0.35f, world.CellSize * 1.5f))
            return;

        Vector3 resultWorld = world.GridToWorldCenter(resultX, resultY);
        Debug.LogWarning(
            $"[FlowReachableTargetSnapDiag] self={self.CharacterKey} target={target.CharacterKey} fullIsland={fullIsland} " +
            $"selfPos={self.Position} targetPos={target.Position} desired={desiredGoal} desiredCell=({goalX},{goalY}) desiredIsland={desiredIsland} " +
            $"targetCell={(targetInGrid ? $"({targetX},{targetY})" : "out")} targetIsland={targetIsland} result=({resultX},{resultY}) resultWorld={resultWorld} " +
            $"resultIsland={ResolveIslandIdForDiagnostics(world, resultX, resultY)} snapDistance={distance:F3} start=({startX},{startY}) " +
            $"startIsland={ResolveIslandIdForDiagnostics(world, startX, startY)} worldVersion={world.Version}");
    }

    private static void LogReachableGoalResolution(
        NavigationWorld world,
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
            || world == null)
        {
            return;
        }

        int startIsland = ResolveIslandIdForDiagnostics(world, startX, startY);
        int rawGoalIsland = ResolveIslandIdForDiagnostics(world, rawGoalX, rawGoalY);
        int resolvedIsland = ResolveIslandIdForDiagnostics(world, resolvedX, resolvedY);
        bool changed = rawGoalX != resolvedX || rawGoalY != resolvedY;
        if (!changed && !fullIsland)
            return;

        Vector3 resolvedWorld = world.GridToWorldCenter(resolvedX, resolvedY);
        GameDebugSettings.Log(DebugCategory.Move,
            $"[FlowReachableGoalResolve] key={self.CharacterKey} desired={desiredGoal} start=({startX},{startY}) startIsland={startIsland} " +
            $"raw=({rawGoalX},{rawGoalY}) rawIsland={rawGoalIsland} resolved=({resolvedX},{resolvedY}) resolvedIsland={resolvedIsland} " +
            $"resolvedWorld={resolvedWorld} distance={distance:F3} radiusCells={radiusCells} fullIsland={fullIsland} " +
            $"rawGrid={FormatGridSampleDiagnostics(desiredGoal)} resolvedGrid={FormatGridSampleDiagnostics(resolvedWorld)}");
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

        bool hasMovingTarget = self?.TargetComp?.CurrentTarget != null
                               && !ReferenceEquals(self.TargetComp.CurrentTarget, self);
        Vector3 occupiedGoalPosition = ResolveNavigationGoalOccupancy(self, agent, goalPosition);
        Vector3 navigationGoalPosition = hasMovingTarget ? goalPosition : occupiedGoalPosition;

        sectionStartTicks = Stopwatch.GetTimestamp();
        if (!_world.WorldToGrid(self.Position, out int startX, out int startY))
        {
            return FailNoFallback(self, $"start not on grid pos={self.Position}", occupiedGoalPosition, out velocity);
        }

        if (!_world.IsWalkable(startX, startY)
            && !TryResolveNearbyStartWalkable(_world, self.Position, startX, startY, out startX, out startY))
        {
            return FailNoFallback(self, $"start blocked and no nearby walkable originalPos={self.Position} | {BuildStartCellDiagnostics(self, self.Position)}", occupiedGoalPosition, out velocity);
        }

        if (!TryResolveStableGoalCell(agent, self, navigationGoalPosition, out int goalX, out int goalY, out Vector3 stableGoalPosition))
        {
            return FailNoFallback(self, BuildGoalResolutionFailure(self, navigationGoalPosition), occupiedGoalPosition, out velocity);
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
        FlowTileCacheEntry tile = null;
        TileGoalKind goalKind = TileGoalKind.FinalGoal;
        int downstreamPortalId = -1;
        if (!TryBuildOrGetTileWithStrictRepath(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY, out tile, out goalKind, out downstreamPortalId, out string tileFailureReason))
        {
            return FailNoFallback(self, tileFailureReason, occupiedGoalPosition, out velocity);
        }
        _perf.TileTicks += Stopwatch.GetTimestamp() - sectionStartTicks;

        if (tile != null && !IsCellReachableInTile(tile, startX, startY))
        {
            _perf.TileReachabilityFailures++;
            LogTileReachabilityFailure(agent, tile, startSectorId, goalSectorId, startX, startY, goalX, goalY, goalKind, downstreamPortalId);
            agent.NavState.PathHandle = null;
            sectionStartTicks = Stopwatch.GetTimestamp();
            bool rebuiltPathHandle = EnsurePathHandle(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY);
            if (!rebuiltPathHandle)
            {
                return FailNoFallback(self, $"tile reachability failed and path handle rebuild failed start=({startX},{startY}) sector={startSectorId}", occupiedGoalPosition, out velocity);
            }

            if (!TryBuildOrGetTileWithStrictRepath(agent, startSectorId, goalSectorId, startX, startY, goalX, goalY, out tile, out goalKind, out downstreamPortalId, out tileFailureReason))
                return FailNoFallback(self, $"tile reachability failed; rebuilt handle but tile unavailable: {tileFailureReason}", occupiedGoalPosition, out velocity);

            _perf.TileTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
        }

        sectionStartTicks = Stopwatch.GetTimestamp();
        DesiredDirectionResolution desiredResolution;
        if (tile == null)
        {
            string pendingPortalFailure = string.Empty;
            if (goalKind != TileGoalKind.Portal
                || !TryResolvePendingPortalDirection(agent, startSectorId, startX, startY, stableGoalPosition, out desiredResolution, out pendingPortalFailure))
            {
                return FailNoFallback(
                    self,
                    $"pending portal direction failed goalKind={goalKind} portal={downstreamPortalId} reason={pendingPortalFailure} tileDiag=tile-pending",
                    occupiedGoalPosition,
                    out velocity);
            }
        }
        else
        {
            desiredResolution = ResolveDesiredDirection(self.CharacterKey, tile, startX, startY, stableGoalPosition, goalKind);
        }
        int pathDirectionContextHash = ComputePathDirectionContextHash(agent, tile, goalX, goalY, goalKind, downstreamPortalId);
        if (desiredResolution.Source == DesiredDirectionSource.Zero
            && goalKind == TileGoalKind.FinalGoal
            && startX == goalX
            && startY == goalY)
        {
            Vector3 toGoalWithinCell = occupiedGoalPosition - self.Position;
            toGoalWithinCell.y = 0f;
            if (toGoalWithinCell.sqrMagnitude > 0.0001f)
            {
                desiredResolution = new DesiredDirectionResolution(
                    toGoalWithinCell.normalized,
                    DesiredDirectionSource.LineOfSight,
                    occupiedGoalPosition,
                    Vector2.zero,
                    true,
                    0f);
            }
        }
        desiredResolution = ApplyPathDirectionBlend(agent, desiredResolution, startX, startY, pathDirectionContextHash);
        Vector3 desiredDirection = desiredResolution.Direction;
        LogFlowTileInvariantDiagnostic(
            self,
            agent,
            startX,
            startY,
            goalX,
            goalY,
            startSectorId,
            goalSectorId,
            goalKind,
            downstreamPortalId,
            tile,
            desiredResolution,
            desiredDirection,
            stableGoalPosition,
            preferredAgentTypeId);
        LogFlowWallProbeDiagnostic(
            self,
            agent,
            startX,
            startY,
            goalX,
            goalY,
            startSectorId,
            goalSectorId,
            goalKind,
            downstreamPortalId,
            tile,
            desiredResolution,
            desiredDirection,
            stableGoalPosition);
        if (desiredResolution.Source == DesiredDirectionSource.Zero
            && !(goalKind == TileGoalKind.FinalGoal && startX == goalX && startY == goalY))
        {
            return FailNoFallback(
                self,
                $"tile produced zero flow start=({startX},{startY}) sector={startSectorId} goalKind={goalKind} portal={downstreamPortalId} tileDiag={(tile != null ? BuildCurrentTileSteeringDiagnostics(tile, startX, startY) : "tile=pending")}",
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
                $"currentFlow={agent.NavState.CurrentFlowDirection} tileDiag={(tile != null ? BuildCurrentTileSteeringDiagnostics(tile, startX, startY) : "tile=pending")} " +
                $"gridPathDiag={BuildGridPathDiagnostics(self.Position, stableGoalPosition, preferredAgentTypeId)}");
        }

        BottleneckDecision bottleneckDecision = FreeMoveDecision;
        sectionStartTicks = Stopwatch.GetTimestamp();
        if (TryResolveActiveBottleneck(agent, tile, goalKind, downstreamPortalId, startSectorId, startX, startY, desiredDirection, out CorridorBottleneckDescriptor bottleneck))
            bottleneckDecision = EvaluateBottleneck(agent, bottleneck);
        _perf.BottleneckTicks += Stopwatch.GetTimestamp() - sectionStartTicks;

        sectionStartTicks = Stopwatch.GetTimestamp();
        velocity = ResolveCrowdSteering(self, agent, occupiedGoalPosition, desiredVelocity, desiredResolution, tile, bottleneckDecision);
        _perf.SteeringTicks += Stopwatch.GetTimestamp() - sectionStartTicks;
        UpdateResolvedVelocity(selfId, occupiedGoalPosition, desiredVelocity, velocity);

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
        string cell = BuildAgentCellDiagnostic(agent, currentPosition);
        string executorGrid = BuildExecutorGridSegmentDiagnostic(currentPosition, desiredHorizontalDisplacement, inputVelocity);
        string lineOfSight = nav.LastSteeringDesiredSource == DesiredDirectionSource.LineOfSight
            ? BuildLineOfSightFailureDiagnostic(currentPosition, nav.LastSteeringTileTarget, agent.AgentTypeId)
            : string.Empty;

        Debug.LogWarning(
            $"[FlowConstraintDiag] reason={executorReason} key={agent.CharacterKey} id={agent.Id} frame={frameCount} " +
            $"pos={currentPosition} agentPos={agent.Position} radius={agent.Radius:F3} agentType={agent.AgentTypeId} mode={nav.LastMovementMode} " +
            $"inputVelocity={inputVelocity} desiredDisp={desiredHorizontalDisplacement} {executorGrid} " +
            $"steerFrame={nav.LastSteeringFrame} goal={nav.LastSteeringGoal} desiredSrc={nav.LastSteeringDesiredSource} " +
            $"desiredDir={nav.LastSteeringDesiredDirection} desiredVel={nav.LastSteeringDesiredVelocity} flow={nav.LastSteeringFlow} " +
            $"los={nav.LastSteeringHasLineOfSight} tileTarget={nav.LastSteeringTileTarget} integration={nav.LastSteeringIntegration:F3} " +
            $"baseVel={nav.LastSteeringBaseVelocity} agentAvoid={nav.LastSteeringAgentAvoidance} " +
            $"clampedAgentAvoid={nav.LastSteeringClampedAgentAvoidance} boundaryAvoid={nav.LastSteeringBoundaryAvoidance} " +
            $"laneVel={nav.LastSteeringLaneVelocity} resultPreClamp={nav.LastSteeringResultPreClamp} result={nav.LastSteeringResult} " +
            $"steerEdgeNormal={nav.LastSteeringEdgeNormal} steerEdgeDist={nav.LastSteeringEdgeDistance:F3} maxSpeed={nav.LastSteeringMaxSpeed:F3} " +
            $"{cell} {gridEdge} {lineOfSight}");
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
        return $"[FlowLineOfSightDiag] from=({fromX},{fromY}) target=({targetX},{targetY}) " +
               $"fromPos={fromPosition} targetPos={targetPosition} gridLos={gridLos} " +
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
        int cost = TryGetCostFieldValue(world, x, y, out byte resolvedCost) ? resolvedCost : -1;
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

    private static bool IsBudgetExpired(long deadlineTicks, int workCursor)
    {
        return (workCursor & (BudgetTimeCheckInterval - 1)) == 0 && Stopwatch.GetTimestamp() >= deadlineTicks;
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
        TrimCombatTargetSlotCache();
        _perf = new FlowPerfAccumulator { Frame = frame };
    }

    private static void FlushPerfIfNeeded()
    {
        if (!_perfInitialized || _perf.Calls <= 0)
            return;

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
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
        if (diagnosticTicks < FlowPerfLogThresholdTicks && !shouldAlwaysLog)
            return;

        Debug.Log(
            $"[FlowPerf] frame={_perf.Frame} calls={_perf.Calls} worldBuilds={_perf.WorldBuilds} pathBuilds={_perf.PathBuilds} tileBuilds={_perf.TileBuilds} " +
            $"total={TicksToMs(_perf.TotalTicks):F3}ms world={TicksToMs(_perf.WorldTicks):F3}ms cells={TicksToMs(_perf.ResolveCellsTicks):F3}ms " +
            $"path={TicksToMs(_perf.PathTicks):F3}ms advance={TicksToMs(_perf.AdvanceTicks):F3}ms tile={TicksToMs(_perf.TileTicks):F3}ms " +
            $"desired={TicksToMs(_perf.DesiredTicks):F3}ms bottleneck={TicksToMs(_perf.BottleneckTicks):F3}ms steering={TicksToMs(_perf.SteeringTicks):F3}ms " +
            $"neighborCollect={TicksToMs(_perf.NeighborCollectTicks):F3}ms neighborAvoid={TicksToMs(_perf.NeighborAvoidTicks):F3}ms " +
            $"boundary={TicksToMs(_perf.BoundaryTicks):F3}ms agentUpdate={TicksToMs(_perf.AgentUpdateTicks):F3}ms neighborChecks={_perf.NeighborChecks} " +
            $"sectorPath(searches={_perf.SectorPathSearches},cacheHits={_perf.SectorPathCacheHits}) " +
            $"portalGraphMergeHits={_perf.PortalGraphMergeHits} " +
            $"tileCache(hits={_perf.TileCacheHits},misses={_perf.TileCacheMisses}) " +
            $"pending(sharedGoal={_perf.PathPendingSharedGoal},tile={_perf.TilePendingBuild}) " +
            $"stableGoal(raw={_perf.StableGoalRaw},reuse={_perf.StableGoalReuse},initial={_perf.StableGoalRefreshInitial},cell={_perf.StableGoalRefreshCellDelta}) " +
            $"pathReasons(noHandle={_perf.PathBuildNoHandle},worldMismatch={_perf.PathBuildWorldMismatch},goalSectorMismatch={_perf.PathBuildGoalSectorMismatch},goalCellMismatch={_perf.PathBuildGoalCellMismatch},invalid={_perf.PathBuildInvalidHandle}) " +
            $"advanceFail={_perf.PathAdvanceFailures} tileReachFail={_perf.TileReachabilityFailures} runtimeDirty(apply={_perf.RuntimeDirtyApplications},sectors={_perf.RuntimeDirtySectorCount})");
    }

    private static void LogAgentOverlapDiagnostics(int frame)
    {
        if (Agents.Count < 2)
            return;
        if (_world == null)
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

            List<AgentRuntimeData> nearbyAgents = CollectNearbyDynamicNeighbors(
                self.Position,
                self.Id,
                Mathf.Max(self.Radius * 4f, _world.CellSize * 2f));
            for (int nearbyIndex = 0; nearbyIndex < nearbyAgents.Count; nearbyIndex++)
            {
                AgentRuntimeData other = nearbyAgents[nearbyIndex];
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
        float[] integration = tile?.IntegrationPayload?.Values ?? tile?.DebugIntegrationPayload?.Values;
        if (integration == null)
            return;

        float maxReachableCost = 0f;
        for (int i = 0; i < integration.Length; i++)
        {
            float cost = integration[i];
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
                float cost = integration[localIndex];
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
        if (tile?.FlowFieldValues == null)
            return;

        for (int worldY = tile.StartY; worldY < tile.StartY + tile.Height; worldY++)
        {
            for (int worldX = tile.StartX; worldX < tile.StartX + tile.Width; worldX++)
            {
                if (!_world.IsWalkable(worldX, worldY))
                    continue;

                int localIndex = tile.GetLocalIndex(worldX, worldY);
                Vector2 flow2 = ResolveRuntimeFlowDirection(tile, worldX, worldY, localIndex);
                if (flow2.sqrMagnitude <= 0.0001f)
                    continue;

                Vector3 center = _world.GridToWorldCenter(worldX, worldY) + Vector3.up * 0.035f;
                Vector3 dir = new Vector3(flow2.x, 0f, flow2.y).normalized;
                Gizmos.color = HasFlowLineOfSight(tile, localIndex)
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

        if (agent.State == FlowFieldAgentState.Combat)
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

    private static List<AgentRuntimeData> CollectNearbyDynamicNeighbors(Vector3 center, int excludedAgentId, float radius)
    {
        if (_world == null)
            throw new InvalidOperationException("CollectNearbyDynamicNeighbors failed: world is null.");

        NearbyAgentScratch.Clear();
        EnsureAgentSpatialBuckets();

        ResolveSpatialBucketCell(center, out int centerCellX, out int centerCellY);
        int searchRadiusInCells = Mathf.Max(1, Mathf.CeilToInt(radius / Mathf.Max(_world.CellSize, 0.001f)));
        float radiusSq = radius * radius;
        for (int y = centerCellY - searchRadiusInCells; y <= centerCellY + searchRadiusInCells; y++)
        {
            for (int x = centerCellX - searchRadiusInCells; x <= centerCellX + searchRadiusInCells; x++)
            {
                int bucketKey = BuildSpatialBucketKey(x, y);
                if (!AgentSpatialBuckets.TryGetValue(bucketKey, out List<AgentRuntimeData> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    AgentRuntimeData other = bucket[i];
                    if (other.Id == excludedAgentId)
                        continue;

                    Vector3 delta = other.Position - center;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > radiusSq)
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

    private static bool TryEnsureWorldBuilt(int preferredAgentTypeId, bool allowSynchronousBuild = false)
    {
        int agentTypeId = ResolvePreferredAgentTypeId(preferredAgentTypeId);
        WorldRuntimeState state = GetOrCreateWorldState(agentTypeId);
        _activeWorldState = state;
        _world = state.World;

        if (!state.IsDirty && state.World != null)
        {
            _world = state.World;
            return true;
        }

        if (!EnsureWorldBuildJob(state, agentTypeId))
            return false;

        if (!allowSynchronousBuild && !CanSynchronouslyBuildWorldForEditorTest(agentTypeId))
            return false;

        ProcessWorldBuildJob(state, long.MaxValue, forceComplete: true);
        if (state.IsDirty || state.World == null || state.BuildJob != null)
            throw new InvalidOperationException($"TryEnsureWorldBuilt failed: world build did not complete agentType={agentTypeId}.");

        _world = state.World;
        return true;
    }

    private static bool HasPendingRuntimeDirty(WorldRuntimeState state)
    {
        return state != null
               && (state.RuntimeDirtyJob != null || state.DirtyRuntimeObstacleSectors.Count > 0);
    }

    private static NavigationWorld ResolveReachabilityQueryWorld(WorldRuntimeState state)
    {
        if (state == null)
            throw new InvalidOperationException("ResolveReachabilityQueryWorld failed: state is null.");
        if (state.World == null)
            throw new InvalidOperationException("ResolveReachabilityQueryWorld failed: committed world is null.");

        if (state.RuntimeDirtyJob != null)
            return BuildRuntimeDirtyPreviewWorld(state.RuntimeDirtyJob);

        if (state.DirtyRuntimeObstacleSectors.Count == 0)
            return state.World;

        return BuildRuntimeDirtyPreviewWorld(state, state.DirtyRuntimeObstacleSectors);
    }

    private static NavigationWorld BuildRuntimeDirtyPreviewWorld(RuntimeDirtyRebuildJob sourceJob)
    {
        if (sourceJob == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: source job is null.");
        if (sourceJob.TargetWorld == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: source target world is null.");
        if (sourceJob.DirtySectors == null || sourceJob.DirtySectors.Count == 0)
            return sourceJob.TargetWorld;

        return BuildRuntimeDirtyPreviewWorld(
            sourceJob.TargetWorld,
            sourceJob.DirtySectors,
            sourceJob.CostDirtySectors,
            sourceJob.CircleObstacles,
            sourceJob.BoxObstacles,
            sourceJob.CostStamps);
    }

    private static NavigationWorld BuildRuntimeDirtyPreviewWorld(WorldRuntimeState state, HashSet<int> dirtySectors)
    {
        if (state == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: state is null.");
        if (state.World == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: state world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return state.World;

        HashSet<int> costDirtySectors = ExpandDirtySectorsByCellRadius(
            state.World,
            dirtySectors,
            Mathf.CeilToInt(ResolveWallCostBlurRadiusCells(state.World)));
        return BuildRuntimeDirtyPreviewWorld(
            state.World,
            dirtySectors,
            costDirtySectors,
            CircleObstacles.Values,
            BoxObstacles.Values,
            CostStamps.Values);
    }

    private static NavigationWorld BuildRuntimeDirtyPreviewWorld(
        NavigationWorld targetWorld,
        HashSet<int> dirtySectors,
        HashSet<int> costDirtySectors,
        IEnumerable<CircleObstacle> circleObstacles,
        IEnumerable<BoxObstacle> boxObstacles,
        IReadOnlyCollection<CostStamp> costStamps)
    {
        if (targetWorld == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: target world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return targetWorld;
        if (costDirtySectors == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: cost dirty sectors are null.");
        if (circleObstacles == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: circle obstacles are null.");
        if (boxObstacles == null)
            throw new InvalidOperationException("BuildRuntimeDirtyPreviewWorld failed: box obstacles are null.");

        NavigationWorld preview = CloneNavigationWorldForRuntimeDirty(targetWorld);
        foreach (int sectorId in dirtySectors)
        {
            ResetSectorWalkableFromBase(preview, preview.Sectors[sectorId]);
            preview.Sectors[sectorId].DirtyVersion++;
        }

        foreach (CircleObstacle circle in circleObstacles)
            BlockCellsByCircleInSectors(preview, dirtySectors, circle.Position, circle.Radius);
        foreach (BoxObstacle box in boxObstacles)
            BlockCellsByBoundsInSectors(preview, dirtySectors, new Bounds(box.Center, box.HalfExtents * 2f));

        RebuildNeighborTraversalMaskForSectors(preview, dirtySectors);
        SymmetrizeNeighborTraversalMaskForSectors(preview, dirtySectors);
        foreach (int sectorId in costDirtySectors)
            RebuildCostFieldForSector(preview, sectorId, costStamps);

        RebuildIslandFieldImmediate(preview);
        return preview;
    }

    private static void RebuildIslandFieldImmediate(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildIslandFieldImmediate failed: world is null.");

        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            world.IslandIds = new int[world.Width * world.Height];

        Array.Clear(world.IslandIds, 0, world.IslandIds.Length);
        world.IslandCount = 0;
        world.MainIslandId = 0;
        world.MainIslandSize = 0;
        Queue<int> openQueue = new Queue<int>(256);
        int scanIndex = 0;
        int currentId = 0;
        int currentSize = 0;
        int mainId = 0;
        int mainSize = 0;
        bool bfsActive = false;
        AdvanceIslandFieldBuild(
            world,
            openQueue,
            ref scanIndex,
            ref currentId,
            ref currentSize,
            ref mainId,
            ref mainSize,
            ref bfsActive,
            long.MaxValue,
            forceComplete: true,
            out bool complete);
        if (!complete)
            throw new InvalidOperationException("RebuildIslandFieldImmediate failed: island build did not complete.");

        RebuildAllSectorLocalComponents(world);
    }

    private static bool CanSynchronouslyBuildWorldForEditorTest(int agentTypeId)
    {
#if UNITY_EDITOR
        return _testTerrainOverride != null
               && (_testTerrainOverride.AgentTypeId == AnyAgentTypeId || _testTerrainOverride.AgentTypeId == agentTypeId);
#else
        return false;
#endif
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
                        ReturnWorldBuildPortalTransitionIntegration(job);
                        state.BuildJob = null;
                        return;
                    }
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

            if (job.Stage == WorldBuildStage.Complete)
                break;

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        ReturnWorldBuildPortalTransitionIntegration(job);
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
            job.HasProvidedCellNavAnchors = job.CellNavAnchors != null;
            job.Stage = WorldBuildStage.CreateWorldShell;
            return true;
        }

        throw new InvalidOperationException(
            "InitializeWorldBuildJob failed: FlowNavigationGridSource has not applied a FlowNavigationGridAsset. " +
            "Runtime navigation is strict authored-grid only and will not fall back to any legacy navigation source.");
    }

    private static void CreateWorldBuildShell(WorldBuildJob job)
    {
        bool[] runtimeWalkableMask = (bool[])job.BaseWalkableMask.Clone();
        job.WorkingWorld = new NavigationWorld
        {
            AgentTypeId = job.AgentTypeId,
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
                if (!forceComplete && IsBudgetExpired(deadlineTicks, job.ObstacleCursor))
                    return;
            }

            job.ApplyingCircleObstacles = false;
            job.ObstacleCursor = 0;
        }

        while (job.ObstacleCursor < job.BoxObstacles.Count)
        {
            BoxObstacle box = job.BoxObstacles[job.ObstacleCursor++];
            BlockCellsByBounds(world.WalkableMask, world.Width, world.Height, world.CellSize, world.Origin, new Bounds(box.Center, box.HalfExtents * 2f));
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.ObstacleCursor))
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
                    LocalComponentIds = new int[sectorWidth * sectorHeight],
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
        if (job.HasProvidedCellNavAnchors)
        {
            job.CellCursor = 0;
            job.Stage = WorldBuildStage.NeighborMask;
            return;
        }

        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            if (!world.BaseWalkableMask[index])
                continue;

            int x = index % world.Width;
            int y = index / world.Width;
            world.CellNavAnchors[index] = world.GridToWorldCenter(x, y);
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.CellCursor))
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

        int cellCount = world.Width * world.Height;
        while (job.CellCursor < cellCount)
        {
            int index = job.CellCursor++;
            RebuildNeighborTraversalMaskCell(world, index % world.Width, index / world.Width);
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.CellCursor))
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
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.CellCursor))
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
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.SectorCursor))
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

        RebuildAllSectorLocalComponents(world);
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
                        if (!forceComplete && IsBudgetExpired(deadlineTicks, job.PortalAddCursor))
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

            if (sector.PortalIds.Count == 0)
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
                AddPendingSectorPortalAccess(job.PendingPortalAccessEntries ??= new List<PendingSectorPortalAccess>(), sector, fromPortalId, integration);
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
                ReturnWorldBuildPortalTransitionIntegration(job);
                if (!forceComplete && IsBudgetExpired(deadlineTicks, job.PortalTransitionFromCursor))
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
        job.PortalTransitionIntegration = RentIntegrationArray(sector.Width * sector.Height);
        InitializeIntegrationField(job.PortalTransitionIntegration);
        job.PortalTransitionOpenSet = new MinHeap();
        SeedPortalTransitionIntegration(sector, fromCells, job.PortalTransitionIntegration, job.PortalTransitionOpenSet);
        job.PortalTransitionIntegrationActive = true;
    }

private static void CommitWorldBuildJob(WorldRuntimeState state, WorldBuildJob job)
    {
        if (job.WorkingWorld == null)
            throw new InvalidOperationException("CommitWorldBuildJob failed: working world is null.");

        int previousWorldVersion = state.World != null ? state.World.Version : 0;
        _perf.WorldBuilds++;
        state.World = job.WorkingWorld;
        state.World.Version = _nextWorldVersion++;
        LogIslandFieldDiagnostics(state.World, "world-build");
        state.IsDirty = false;
        state.DirtyRuntimeObstacleSectors.Clear();
        state.RuntimeDirtyJob = null;
        ClearFlowTileCache();
        ReturnPendingFlowTileBuildIntegrations();
        FlowTileBuildQueue.Clear();
        PendingFlowTileBuildJobs.Clear();
        SectorPathCache.Clear();
        RemoveSectorPortalAccessCacheEntriesForWorldVersion(previousWorldVersion);
        SharedGoalFields.Clear();
        ReturnPendingSharedGoalFieldBuildIntegrations();
        SharedGoalFieldBuildQueue.Clear();
        PendingSharedGoalFieldBuildJobs.Clear();
        CommitPendingSectorPortalAccessEntries(state.World, job.PendingPortalAccessEntries);
        ValidateAllSectorPortalAccessCoverage(state.World, "world-build-commit");
        FinalizeWorldCostStorage(state.World);
        CombatTargetSlotCache.Clear();
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

    private static string BuildCellProbeDiagnostics(NavigationWorld world, int x, int y)
    {
        if (world == null)
            return "centerProbe=world-null";
        if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return $"centerProbe={{cell=({x},{y}) inGrid=False}}";

        int index = world.GetIndex(x, y);
        return $"centerProbe={{cell=({x},{y}) center={world.GridToWorldCenter(x, y)} walk={world.WalkableMask[index]} base={world.BaseWalkableMask[index]} island={ResolveIslandIdForDiagnostics(world, x, y)} mask=0x{(world.NeighborTraversalMask != null && world.NeighborTraversalMask.Length == world.Width * world.Height ? world.NeighborTraversalMask[index].ToString("X2") : "NA")}}}";
    }

    private static string BuildWalkableNeighborhoodDiagnostics(NavigationWorld world, int centerX, int centerY, int radius)
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

    private static void InvalidateRuntimeDirtyJob(WorldRuntimeState state)
    {
        if (state == null)
            throw new InvalidOperationException("InvalidateRuntimeDirtyJob failed: state is null.");

        if (state.RuntimeDirtyJob != null)
        {
            foreach (int sectorId in state.RuntimeDirtyJob.DirtySectors)
                state.DirtyRuntimeObstacleSectors.Add(sectorId);
            foreach (int sectorId in state.RuntimeDirtyJob.CostDirtySectors)
                state.DirtyRuntimeObstacleSectors.Add(sectorId);
            ReturnRuntimeDirtyPortalTransitionIntegration(state.RuntimeDirtyJob);
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

            if (job.Stage == RuntimeDirtyRebuildStage.Complete)
                break;

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return;
        }

        ReturnRuntimeDirtyPortalTransitionIntegration(job);
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
        while (job.SectorCursor < job.DirtySectorIds.Count)
        {
            SectorData sector = job.WorkingWorld.Sectors[job.DirtySectorIds[job.SectorCursor++]];
            for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
            {
                for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
                    RebuildNeighborTraversalMaskCell(job.WorkingWorld, x, y);
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

        RebuildAllSectorLocalComponents(world);
        LogIslandFieldDiagnostics(world, "runtime-dirty");
        job.Stage = RuntimeDirtyRebuildStage.PortalGraph;
    }

    private static void RebuildAllSectorLocalComponents(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildAllSectorLocalComponents failed: world is null.");
        if (world.Sectors == null)
            throw new InvalidOperationException("RebuildAllSectorLocalComponents failed: sectors are null.");

        for (int i = 0; i < world.Sectors.Length; i++)
        {
            RebuildSectorIslandMetadata(world, world.Sectors[i]);
            RebuildSectorLocalComponents(world, world.Sectors[i]);
        }
    }

    private static void RebuildSectorIslandMetadata(NavigationWorld world, SectorData sector)
    {
        if (world == null)
            throw new InvalidOperationException("RebuildSectorIslandMetadata failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("RebuildSectorIslandMetadata failed: sector is null.");
        if (world.IslandIds == null || world.IslandIds.Length != world.Width * world.Height)
            throw new InvalidOperationException("RebuildSectorIslandMetadata failed: island field is missing or invalid.");

        int uniformIslandId = 0;
        bool mixed = false;
        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            int rowStart = y * world.Width;
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = rowStart + x;
                if (!world.WalkableMask[index])
                    continue;

                int islandId = world.IslandIds[index];
                if (islandId <= 0)
                    throw new InvalidOperationException($"RebuildSectorIslandMetadata failed: walkable cell ({x},{y}) has invalid island {islandId}.");

                if (uniformIslandId == 0)
                {
                    uniformIslandId = islandId;
                    continue;
                }

                if (uniformIslandId == islandId)
                    continue;

                mixed = true;
                break;
            }

            if (mixed)
                break;
        }

        sector.UniformIslandId = mixed ? -1 : uniformIslandId;
    }

    private static void RebuildSectorLocalComponents(NavigationWorld world, SectorData sector)
    {
        if (sector == null)
            throw new InvalidOperationException("RebuildSectorLocalComponents failed: sector is null.");

        int cellCount = sector.Width * sector.Height;
        if (sector.LocalComponentIds == null || sector.LocalComponentIds.Length != cellCount)
            sector.LocalComponentIds = new int[cellCount];
        else
            Array.Clear(sector.LocalComponentIds, 0, sector.LocalComponentIds.Length);

        int componentId = 0;
        Queue<int> open = new Queue<int>(Mathf.Min(256, Mathf.Max(1, cellCount)));
        for (int localIndex = 0; localIndex < cellCount; localIndex++)
        {
            if (sector.LocalComponentIds[localIndex] != 0)
                continue;

            int worldX = sector.StartX + localIndex % sector.Width;
            int worldY = sector.StartY + localIndex / sector.Width;
            if (!world.IsWalkable(worldX, worldY))
                continue;

            componentId++;
            sector.LocalComponentIds[localIndex] = componentId;
            open.Enqueue(localIndex);
            while (open.Count > 0)
            {
                int currentLocal = open.Dequeue();
                int currentX = sector.StartX + currentLocal % sector.Width;
                int currentY = sector.StartY + currentLocal / sector.Width;
                byte traversalMask = world.NeighborTraversalMask[world.GetIndex(currentX, currentY)];
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    if ((traversalMask & (1 << i)) == 0)
                        continue;

                    int nextX = currentX + NeighborOffsetX[i];
                    int nextY = currentY + NeighborOffsetY[i];
                    if (!IsInsideSector(sector, nextX, nextY) || !world.IsWalkable(nextX, nextY))
                        continue;

                    int nextLocal = GetSectorLocalIndex(sector, nextX, nextY);
                    if (sector.LocalComponentIds[nextLocal] != 0)
                        continue;

                    sector.LocalComponentIds[nextLocal] = componentId;
                    open.Enqueue(nextLocal);
                }
            }
        }

        sector.LocalComponentCount = componentId;
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
                int budgetWork = 0;
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

                    if (!forceComplete && IsBudgetExpired(deadlineTicks, ++budgetWork))
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
            foreach (int sectorId in job.CostDirtySectors)
                job.PortalTransitionDirtySectors.Add(sectorId);

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

            if (sector.PortalIds.Count == 0)
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
                AddPendingSectorPortalAccess(job.PendingPortalAccessEntries ??= new List<PendingSectorPortalAccess>(), sector, fromPortalId, integration);
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
                ReturnRuntimeDirtyPortalTransitionIntegration(job);
                if (!forceComplete && IsBudgetExpired(deadlineTicks, job.PortalTransitionFromCursor))
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
        job.PortalTransitionIntegration = RentIntegrationArray(sector.Width * sector.Height);
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
                if (!IsSignificantIntegrationImprovement(newCost, integration[nextLocalIndex]))
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

        InvalidateCachesForDirtySectors(target, job.CostDirtySectors);
        CommitPendingSectorPortalAccessEntries(target, job.PendingPortalAccessEntries);
        ValidateSectorPortalAccessCoverage(target, job.CostDirtySectors, "runtime-dirty-commit");
        FinalizeWorldCostStorage(target);
        MovingTargetAnchors.Clear();
        CombatTargetSlotCache.Clear();
        Bottlenecks.Clear();
        CorridorBottlenecks.Clear();
        NavigationGoalOccupancyBuckets.Clear();
        _lastNavigationGoalOccupancyBucketFrame = -1;
        _lastNavigationGoalOccupancyBucketWorldVersion = -1;
        foreach (KeyValuePair<int, AgentRuntimeData> pair in Agents)
        {
            if (PathTouchesAnySector(pair.Value.NavState.PathHandle, job.CostDirtySectors)
                || PathReferencesMissingPortal(target, pair.Value.NavState.PathHandle))
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
            Width = source.Width,
            Height = source.Height,
            CellSize = source.CellSize,
            Origin = source.Origin,
            BaseWalkableMask = source.BaseWalkableMask,
            WalkableMask = (bool[])source.WalkableMask.Clone(),
            CostField = MaterializeMutableCostField(source),
            SectorCostFields = null,
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
                DirtyVersion = sector.DirtyVersion,
                IsClearCostField = sector.IsClearCostField,
                IsClearFlowTile = sector.IsClearFlowTile,
                UniformIslandId = sector.UniformIslandId,
                LocalComponentIds = sector.LocalComponentIds != null ? (int[])sector.LocalComponentIds.Clone() : new int[sector.Width * sector.Height],
                LocalComponentCount = sector.LocalComponentCount
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

        foreach (int sectorId in sectorIds)
        {
            SectorData sector = world.Sectors[sectorId];
            for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
            {
                for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
                    RebuildNeighborTraversalMaskCell(world, x, y);
            }
        }
    }

    private static void RebuildNeighborTraversalMaskCell(NavigationWorld world, int x, int y)
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

    private static void InvalidateCachesForDirtySectors(NavigationWorld world, HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("InvalidateCachesForDirtySectors failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return;

        List<FlowTileCacheKey> tileKeysToRemove = null;
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            if (pair.Key.WorldVersion != world.Version || !dirtySectors.Contains(pair.Key.SectorId))
                continue;

            tileKeysToRemove ??= new List<FlowTileCacheKey>();
            tileKeysToRemove.Add(pair.Key);
        }

        if (tileKeysToRemove != null)
        {
            for (int i = 0; i < tileKeysToRemove.Count; i++)
                RemoveFlowTileCacheEntry(tileKeysToRemove[i]);
        }

        List<SectorPathCacheKey> pathKeysToRemove = null;
        foreach (KeyValuePair<SectorPathCacheKey, SectorPathCacheEntry> pair in SectorPathCache)
        {
            if (pair.Key.WorldVersion != world.Version)
                continue;

            if (!dirtySectors.Contains(pair.Key.StartSectorId)
                && !dirtySectors.Contains(pair.Key.GoalSectorId)
                && !SectorPathTouchesAnySector(pair.Value, dirtySectors)
                && !SectorPathReferencesMissingPortal(world, pair.Value))
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
            if (pair.Key.WorldVersion != world.Version)
                continue;

            if (!dirtySectors.Contains(pair.Key.SectorId)
                && TryGetPortalById(world, pair.Key.PortalId, out _))
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
            if (pair.Key.WorldVersion != world.Version)
                continue;

            if (!dirtySectors.Contains(pair.Key.GoalSectorId)
                && !SharedGoalFieldTouchesAnySector(pair.Value, dirtySectors)
                && !SharedGoalFieldReferencesMissingPortal(world, pair.Value))
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

        InvalidatePendingBuildJobsForDirtySectors(world, dirtySectors);
    }

    private static void InvalidatePendingBuildJobsForDirtySectors(NavigationWorld world, HashSet<int> dirtySectors)
    {
        if (world == null)
            throw new InvalidOperationException("InvalidatePendingBuildJobsForDirtySectors failed: world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return;

        InvalidatePendingFlowTileBuildJobsForDirtySectors(world, dirtySectors);
        InvalidatePendingSharedGoalFieldBuildJobsForDirtySectors(world, dirtySectors);
    }

    private static void InvalidatePendingFlowTileBuildJobsForDirtySectors(NavigationWorld world, HashSet<int> dirtySectors)
    {
        LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First;
        while (node != null)
        {
            LinkedListNode<FlowTileBuildJob> next = node.Next;
            FlowTileBuildJob job = node.Value;
            if (job != null
                && job.BuildKey.CacheKey.WorldVersion == world.Version
                && (FlowTileBuildJobTouchesAnySector(job, dirtySectors) || FlowTileBuildJobReferencesMissingPortal(world, job)))
            {
                ReturnTileIntegrationPayload(job.Tile);
                FlowTileBuildQueue.Remove(node);
                PendingFlowTileBuildJobs.Remove(job.BuildKey.CacheKey);
            }

            node = next;
        }
    }

    private static bool FlowTileBuildJobTouchesAnySector(FlowTileBuildJob job, HashSet<int> dirtySectors)
    {
        if (job == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        if (dirtySectors.Contains(job.BuildKey.CacheKey.SectorId))
            return true;

        return PathTouchesAnySector(job.HandleSnapshot, dirtySectors);
    }

    private static bool FlowTileBuildJobReferencesMissingPortal(NavigationWorld world, FlowTileBuildJob job)
    {
        if (world == null)
            throw new InvalidOperationException("FlowTileBuildJobReferencesMissingPortal failed: world is null.");
        if (job == null)
            return false;

        FlowTileCacheKey key = job.BuildKey.CacheKey;
        if (key.GoalKind == TileGoalKind.Portal && !TryGetPortalById(world, key.GoalId, out _))
            return true;

        return PathReferencesMissingPortal(world, job.HandleSnapshot);
    }

    private static void InvalidatePendingSharedGoalFieldBuildJobsForDirtySectors(NavigationWorld world, HashSet<int> dirtySectors)
    {
        LinkedListNode<SharedGoalFieldBuildJob> node = SharedGoalFieldBuildQueue.First;
        while (node != null)
        {
            LinkedListNode<SharedGoalFieldBuildJob> next = node.Next;
            SharedGoalFieldBuildJob job = node.Value;
            if (job != null
                && job.Key.WorldVersion == world.Version
                && (SharedGoalFieldBuildJobTouchesAnySector(job, dirtySectors) || SharedGoalFieldBuildJobReferencesMissingPortal(world, job)))
            {
                ReturnSharedGoalFieldBuildIntegration(job);
                SharedGoalFieldBuildQueue.Remove(node);
                PendingSharedGoalFieldBuildJobs.Remove(job.Key);
            }

            node = next;
        }
    }

    private static bool SharedGoalFieldBuildJobTouchesAnySector(SharedGoalFieldBuildJob job, HashSet<int> dirtySectors)
    {
        if (job == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        if (dirtySectors.Contains(job.GoalSectorId))
            return true;

        return job.Field != null && SharedGoalFieldTouchesAnySector(job.Field, dirtySectors);
    }

    private static bool SharedGoalFieldBuildJobReferencesMissingPortal(NavigationWorld world, SharedGoalFieldBuildJob job)
    {
        return job?.Field != null && SharedGoalFieldReferencesMissingPortal(world, job.Field);
    }

    private static bool SharedGoalFieldTouchesAnySector(SharedGoalField field, HashSet<int> dirtySectors)
    {
        if (field == null || dirtySectors == null || dirtySectors.Count == 0)
            return false;

        foreach (int node in field.NodeCosts.Keys)
        {
            DecodePortalNode(node, out int sectorId, out _);
            if (dirtySectors.Contains(sectorId))
                return true;
        }

        foreach (KeyValuePair<int, int> pair in field.NextNodeTowardGoal)
        {
            DecodePortalNode(pair.Key, out int fromSectorId, out _);
            DecodePortalNode(pair.Value, out int toSectorId, out _);
            if (dirtySectors.Contains(fromSectorId) || dirtySectors.Contains(toSectorId))
                return true;
        }

        return false;
    }

    private static bool SharedGoalFieldReferencesMissingPortal(NavigationWorld world, SharedGoalField field)
    {
        if (world == null)
            throw new InvalidOperationException("SharedGoalFieldReferencesMissingPortal failed: world is null.");
        if (field == null)
            return false;

        foreach (int node in field.NodeCosts.Keys)
        {
            DecodePortalNode(node, out _, out int portalId);
            if (!TryGetPortalById(world, portalId, out _))
                return true;
        }

        foreach (KeyValuePair<int, int> pair in field.NextNodeTowardGoal)
        {
            DecodePortalNode(pair.Key, out _, out int fromPortalId);
            DecodePortalNode(pair.Value, out _, out int toPortalId);
            if (!TryGetPortalById(world, fromPortalId, out _) || !TryGetPortalById(world, toPortalId, out _))
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

    private static bool SectorPathReferencesMissingPortal(NavigationWorld world, SectorPathCacheEntry entry)
    {
        if (world == null)
            throw new InvalidOperationException("SectorPathReferencesMissingPortal failed: world is null.");
        if (entry == null || entry.PortalIds == null)
            return false;

        for (int i = 0; i < entry.PortalIds.Length; i++)
        {
            if (!TryGetPortalById(world, entry.PortalIds[i], out _))
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
        return PathReferencesMissingPortal(_world, handle);
    }

    private static bool PathReferencesMissingPortal(NavigationWorld world, PathHandle handle)
    {
        if (world == null)
            throw new InvalidOperationException("PathReferencesMissingPortal failed: world is null.");
        if (handle == null || handle.PortalIds == null)
            return false;

        for (int i = 0; i < handle.PortalIds.Length; i++)
        {
            if (!TryGetPortalById(world, handle.PortalIds[i], out _))
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
        int maxSegmentWidth = ResolvePortalMaxWindowWidthCells();
        int segmentStart = 0;
        for (int i = 1; i <= cellsA.Count; i++)
        {
            bool reachedEnd = i == cellsA.Count;
            bool reachedMaxWidth = i - segmentStart >= maxSegmentWidth;
            bool costSplit = !reachedEnd && ShouldSplitPortalRunBeforeIndex(world, cellsA, cellsB, i);
            if (!reachedEnd && !reachedMaxWidth && !costSplit)
                continue;

            AddPortalSegment(world, portals, sectorAId, sectorBId, cellsA, cellsB, segmentStart, i - segmentStart, isVerticalBoundary, runIsNarrow);
            segmentStart = i;
        }
    }

    private static int ResolvePortalMaxWindowWidthCells()
    {
        return Mathf.Max(Config.PortalNarrowWidthCells, Config.PortalMaxWindowWidthCells);
    }

    private static bool ShouldSplitPortalRunBeforeIndex(NavigationWorld world, List<Vector2Int> cellsA, List<Vector2Int> cellsB, int index)
    {
        if (index <= 0 || index >= cellsA.Count || index >= cellsB.Count)
            return false;

        int previousCost = ResolvePortalPairCost(world, cellsA[index - 1], cellsB[index - 1]);
        int currentCost = ResolvePortalPairCost(world, cellsA[index], cellsB[index]);
        return Mathf.Abs(currentCost - previousCost) >= PortalWindowSplitCostDelta;
    }

    private static int ResolvePortalPairCost(NavigationWorld world, Vector2Int cellA, Vector2Int cellB)
    {
        int costA = GetCostFieldValueStrict(world, cellA.x, cellA.y);
        int costB = GetCostFieldValueStrict(world, cellB.x, cellB.y);
        if (costA >= 255 || costB >= 255)
            return 255;

        return Mathf.Max(1, (costA + costB) / 2);
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

        return cellsA.Count <= Config.PortalNarrowWidthCells;
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
        _perf.SynchronousSectorIntegrations++;
        if (seeds == null || seeds.Length == 0)
            throw new InvalidOperationException($"BuildSectorIntegrationField failed: sector {sector.SectorId} has no seeds.");

        MinHeap openSet = new MinHeap();
        float[] integration = new float[sector.Width * sector.Height];
        InitializeIntegrationField(integration);

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
            openSet.Push(localIndex, seedCost);
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

                if (!CanTraverseNeighborCells(
                        world,
                        reverseTraversal ? nextWorldX : worldX,
                        reverseTraversal ? nextWorldY : worldY,
                        reverseTraversal ? worldX : nextWorldX,
                        reverseTraversal ? worldY : nextWorldY))
                    continue;

                int nextLocalIndex = GetSectorLocalIndex(sector, nextWorldX, nextWorldY);
                float newCost = ResolveEikonalIntegrationCost(world, sector, integration, nextWorldX, nextWorldY, reverseTraversal);
                if (!IsSignificantIntegrationImprovement(newCost, integration[nextLocalIndex]))
                    continue;

                integration[nextLocalIndex] = newCost;
                openSet.Push(nextLocalIndex, newCost);
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

    private static bool IsSignificantIntegrationImprovement(float newCost, float existingCost)
    {
        if (float.IsPositiveInfinity(newCost))
            return false;
        if (float.IsPositiveInfinity(existingCost))
            return true;

        return newCost < existingCost - IntegrationSignificantImprovement;
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
        int cost = GetCostFieldValueStrict(world, worldX, worldY);
        if (cost >= 255)
            return float.PositiveInfinity;

        if (cost <= 1)
            return 1f;

        return Mathf.Max(1f, cost);
    }

    private static int GetCostFieldValueStrict(NavigationWorld world, int worldX, int worldY)
    {
        if (!TryGetCostFieldValue(world, worldX, worldY, out byte cost))
            throw new InvalidOperationException($"GetCostFieldValueStrict failed: invalid cost field read cell=({worldX},{worldY}).");

        return cost;
    }

    private static bool TryGetCostFieldValue(NavigationWorld world, int worldX, int worldY, out byte cost)
    {
        cost = 0;
        if (world == null)
            return false;
        if (worldX < 0 || worldX >= world.Width || worldY < 0 || worldY >= world.Height)
            return false;
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            return false;

        int index = world.GetIndex(worldX, worldY);
        if (TryGetSectorForCell(world, worldX, worldY, out SectorData sector) && sector.IsClearCostField)
        {
            cost = world.WalkableMask[index] ? (byte)1 : byte.MaxValue;
            return true;
        }

        if (sector != null && world.SectorCostFields != null)
        {
            if (sector.SectorId < 0 || sector.SectorId >= world.SectorCostFields.Length)
                return false;

            byte[] sectorCost = world.SectorCostFields[sector.SectorId];
            if (sectorCost == null || sectorCost.Length != sector.Width * sector.Height)
                return false;

            cost = sectorCost[GetSectorLocalIndex(sector, worldX, worldY)];
            return true;
        }

        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            return false;

        cost = world.CostField[index];
        return true;
    }

    private static void FinalizeWorldCostStorage(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("FinalizeWorldCostStorage failed: world is null.");
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            throw new InvalidOperationException("FinalizeWorldCostStorage failed: mutable cost field is missing.");
        if (world.Sectors == null || world.Sectors.Length != world.SectorCountX * world.SectorCountY)
            throw new InvalidOperationException("FinalizeWorldCostStorage failed: sectors are missing.");

        byte[][] sectorCostFields = new byte[world.Sectors.Length][];
        for (int i = 0; i < world.Sectors.Length; i++)
        {
            SectorData sector = world.Sectors[i];
            if (sector.IsClearCostField)
                continue;

            byte[] chunk = new byte[sector.Width * sector.Height];
            for (int y = 0; y < sector.Height; y++)
            {
                for (int x = 0; x < sector.Width; x++)
                {
                    int worldX = sector.StartX + x;
                    int worldY = sector.StartY + y;
                    chunk[x + y * sector.Width] = world.CostField[world.GetIndex(worldX, worldY)];
                }
            }

            sectorCostFields[sector.SectorId] = chunk;
        }

        world.SectorCostFields = sectorCostFields;
        world.CostField = null;
    }

    private static byte[] MaterializeMutableCostField(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("MaterializeMutableCostField failed: world is null.");
        if (world.WalkableMask == null || world.WalkableMask.Length != world.Width * world.Height)
            throw new InvalidOperationException("MaterializeMutableCostField failed: walkable mask is missing.");
        if (world.Sectors == null || world.Sectors.Length != world.SectorCountX * world.SectorCountY)
            throw new InvalidOperationException("MaterializeMutableCostField failed: sectors are missing.");

        byte[] costField = new byte[world.Width * world.Height];
        for (int i = 0; i < costField.Length; i++)
            costField[i] = world.WalkableMask[i] ? (byte)1 : byte.MaxValue;

        for (int i = 0; i < world.Sectors.Length; i++)
        {
            SectorData sector = world.Sectors[i];
            byte[] chunk = world.SectorCostFields != null && sector.SectorId >= 0 && sector.SectorId < world.SectorCostFields.Length
                ? world.SectorCostFields[sector.SectorId]
                : null;
            if (chunk == null)
            {
                if (!sector.IsClearCostField)
                    throw new InvalidOperationException($"MaterializeMutableCostField failed: missing non-clear cost chunk sector={sector.SectorId}.");
                continue;
            }
            if (chunk.Length != sector.Width * sector.Height)
                throw new InvalidOperationException($"MaterializeMutableCostField failed: invalid cost chunk sector={sector.SectorId} length={chunk.Length}.");

            for (int y = 0; y < sector.Height; y++)
            {
                for (int x = 0; x < sector.Width; x++)
                {
                    int worldX = sector.StartX + x;
                    int worldY = sector.StartY + y;
                    costField[world.GetIndex(worldX, worldY)] = chunk[x + y * sector.Width];
                }
            }
        }

        return costField;
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

        SectorPortalAccessEntry entry = GetPrebuiltSectorPortalAccess(sector, sectorId, portalId);
        if (entry == null || entry.QuantizedIntegration == null)
            return float.PositiveInfinity;

        return DecodePortalAccessIntegrationCost(entry, GetSectorLocalIndex(sector, worldX, worldY));
    }

    private static float DecodePortalAccessIntegrationCost(SectorPortalAccessEntry entry, int localIndex)
    {
        if (entry == null)
            throw new InvalidOperationException("DecodePortalAccessIntegrationCost failed: entry is null.");
        if (entry.QuantizedIntegration == null)
            throw new InvalidOperationException("DecodePortalAccessIntegrationCost failed: quantized integration is null.");
        if (localIndex < 0 || localIndex >= entry.QuantizedIntegration.Length)
            throw new InvalidOperationException($"DecodePortalAccessIntegrationCost failed: local index out of range index={localIndex} length={entry.QuantizedIntegration.Length}.");
        if (entry.IntegrationScale <= 0f || float.IsNaN(entry.IntegrationScale) || float.IsInfinity(entry.IntegrationScale))
            throw new InvalidOperationException($"DecodePortalAccessIntegrationCost failed: invalid integration scale={entry.IntegrationScale}.");

        ushort value = entry.QuantizedIntegration[localIndex];
        return value == QuantizedIntegrationInfinity ? float.PositiveInfinity : value * entry.IntegrationScale;
    }

    private static ushort[] QuantizePortalAccessIntegration(SectorData sector, float[] integration, out float scale)
    {
        if (sector == null)
            throw new InvalidOperationException("QuantizePortalAccessIntegration failed: sector is null.");
        if (integration == null || integration.Length != sector.Width * sector.Height)
            throw new InvalidOperationException($"QuantizePortalAccessIntegration failed: invalid integration sector={sector.SectorId}.");

        float maxFinite = 0f;
        for (int i = 0; i < integration.Length; i++)
        {
            float cost = integration[i];
            if (float.IsPositiveInfinity(cost))
                continue;
            if (float.IsNaN(cost) || cost < 0f)
                throw new InvalidOperationException($"QuantizePortalAccessIntegration failed: invalid cost sector={sector.SectorId} index={i} cost={cost}.");
            if (cost > maxFinite)
                maxFinite = cost;
        }

        scale = maxFinite > 0f ? maxFinite / QuantizedIntegrationMaxFinite : 1f;
        ushort[] quantized = new ushort[integration.Length];
        for (int i = 0; i < integration.Length; i++)
        {
            float cost = integration[i];
            if (float.IsPositiveInfinity(cost))
            {
                quantized[i] = QuantizedIntegrationInfinity;
                continue;
            }

            int encoded = Mathf.RoundToInt(cost / scale);
            quantized[i] = (ushort)Mathf.Clamp(encoded, 0, QuantizedIntegrationMaxFinite);
        }

        return quantized;
    }

private static SectorPortalAccessEntry GetPrebuiltSectorPortalAccess(SectorData sector, int sectorId, int portalId)
    {
        SectorPortalAccessKey key = new SectorPortalAccessKey(
            _world.Version,
            sectorId,
            portalId,
            sector.DirtyVersion);
        if (SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry cached)
            && cached?.QuantizedIntegration != null)
        {
            cached.LastUsedFrame = GetFrameCount();
            return cached;
        }

        throw new InvalidOperationException(
            $"GetPrebuiltSectorPortalAccess failed: missing prebuilt portal access field world={_world.Version} sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}. " +
            $"{BuildMissingPortalAccessDiagnostics(_world, sectorId, portalId, sector.DirtyVersion)} " +
            "Portal access integration must be produced by world/runtime portal transition jobs, not synchronously in query/tile build.");
    }

    private static void AddPendingSectorPortalAccess(List<PendingSectorPortalAccess> pendingEntries, SectorData sector, int portalId, float[] integration)
    {
        if (pendingEntries == null)
            throw new InvalidOperationException("AddPendingSectorPortalAccess failed: pendingEntries is null.");
        if (sector == null)
            throw new InvalidOperationException("AddPendingSectorPortalAccess failed: sector is null.");
        if (integration == null || integration.Length != sector.Width * sector.Height)
            throw new InvalidOperationException($"AddPendingSectorPortalAccess failed: invalid integration sector={sector.SectorId} portal={portalId}.");

        ushort[] quantized = QuantizePortalAccessIntegration(sector, integration, out float scale);
        pendingEntries.Add(new PendingSectorPortalAccess(sector.SectorId, portalId, sector.DirtyVersion, quantized, scale));
    }

    private static void CommitPendingSectorPortalAccessEntries(NavigationWorld world, List<PendingSectorPortalAccess> pendingEntries)
    {
        if (pendingEntries == null || pendingEntries.Count == 0)
            return;
        if (world == null)
            throw new InvalidOperationException("CommitPendingSectorPortalAccessEntries failed: world is null.");

        for (int i = 0; i < pendingEntries.Count; i++)
        {
            PendingSectorPortalAccess pending = pendingEntries[i];
            SectorPortalAccessKey key = new SectorPortalAccessKey(
                world.Version,
                pending.SectorId,
                pending.PortalId,
                pending.SectorDirtyVersion);
            SectorPortalAccessCache[key] = new SectorPortalAccessEntry
            {
                QuantizedIntegration = pending.QuantizedIntegration,
                IntegrationScale = pending.IntegrationScale,
                LastUsedFrame = GetFrameCount()
            };
        }

        TrimSectorPortalAccessCache();
    }

    private static void ValidateSectorPortalAccessCoverage(NavigationWorld world, HashSet<int> sectorIds, string stage)
    {
        if (world == null)
            throw new InvalidOperationException("ValidateSectorPortalAccessCoverage failed: world is null.");
        if (sectorIds == null || sectorIds.Count == 0)
            return;

        foreach (int sectorId in sectorIds)
        {
            if (sectorId < 0 || sectorId >= world.Sectors.Length)
                throw new InvalidOperationException($"ValidateSectorPortalAccessCoverage failed: sector out of range sector={sectorId} stage={stage}.");

            SectorData sector = world.Sectors[sectorId];
            for (int i = 0; i < sector.PortalIds.Count; i++)
            {
                int portalId = sector.PortalIds[i];
                SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sectorId, portalId, sector.DirtyVersion);
                if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry) || entry?.QuantizedIntegration == null)
                {
                    throw new InvalidOperationException(
                        $"ValidateSectorPortalAccessCoverage failed: missing portal access stage={stage} world={world.Version} sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}.");
                }
            }
        }
    }

private static void ValidateAllSectorPortalAccessCoverage(NavigationWorld world, string stage)
    {
        if (world == null)
            throw new InvalidOperationException("ValidateAllSectorPortalAccessCoverage failed: world is null.");
        if (world.Sectors == null)
            throw new InvalidOperationException($"ValidateAllSectorPortalAccessCoverage failed: sectors are null stage={stage}.");

        for (int sectorId = 0; sectorId < world.Sectors.Length; sectorId++)
        {
            SectorData sector = world.Sectors[sectorId];
            if (sector == null)
                throw new InvalidOperationException($"ValidateAllSectorPortalAccessCoverage failed: sector is null stage={stage} sector={sectorId}.");

            for (int i = 0; i < sector.PortalIds.Count; i++)
            {
                int portalId = sector.PortalIds[i];
                SectorPortalAccessKey key = new SectorPortalAccessKey(world.Version, sectorId, portalId, sector.DirtyVersion);
                if (!SectorPortalAccessCache.TryGetValue(key, out SectorPortalAccessEntry entry) || entry?.QuantizedIntegration == null)
                {
                    throw new InvalidOperationException(
                        $"ValidateAllSectorPortalAccessCoverage failed: missing portal access stage={stage} world={world.Version} sector={sectorId} portal={portalId} dirty={sector.DirtyVersion}. " +
                        BuildMissingPortalAccessDiagnostics(world, sectorId, portalId, sector.DirtyVersion));
                }
            }
        }
    }

    private static void RemoveSectorPortalAccessCacheEntriesForWorldVersion(int worldVersion)
    {
        if (worldVersion <= 0 || SectorPortalAccessCache.Count == 0)
            return;

        List<SectorPortalAccessKey> keysToRemove = null;
        foreach (SectorPortalAccessKey key in SectorPortalAccessCache.Keys)
        {
            if (key.WorldVersion != worldVersion)
                continue;

            keysToRemove ??= new List<SectorPortalAccessKey>();
            keysToRemove.Add(key);
        }

        if (keysToRemove == null)
            return;

        for (int i = 0; i < keysToRemove.Count; i++)
            SectorPortalAccessCache.Remove(keysToRemove[i]);
    }

    private static string BuildMissingPortalAccessDiagnostics(NavigationWorld world, int sectorId, int portalId, int dirtyVersion)
    {
        if (world == null)
            return "diag=world-null";

        int sameWorldSectorEntries = 0;
        int sameWorldEntries = 0;
        int samePortalAnyWorldEntries = 0;
        string sampleSameWorldSector = "none";
        string sampleSamePortal = "none";
        foreach (SectorPortalAccessKey existingKey in SectorPortalAccessCache.Keys)
        {
            if (existingKey.WorldVersion == world.Version)
            {
                sameWorldEntries++;
                if (existingKey.SectorId == sectorId)
                {
                    sameWorldSectorEntries++;
                    if (sampleSameWorldSector == "none")
                        sampleSameWorldSector = $"world={existingKey.WorldVersion},sector={existingKey.SectorId},portal={existingKey.PortalId},dirty={existingKey.SectorDirtyVersion}";
                }
            }

            if (existingKey.PortalId == portalId)
            {
                samePortalAnyWorldEntries++;
                if (sampleSamePortal == "none")
                    sampleSamePortal = $"world={existingKey.WorldVersion},sector={existingKey.SectorId},portal={existingKey.PortalId},dirty={existingKey.SectorDirtyVersion}";
            }
        }

        string sectorInfo = "sector=out-of-range";
        if (world.Sectors != null && sectorId >= 0 && sectorId < world.Sectors.Length)
        {
            SectorData sector = world.Sectors[sectorId];
            sectorInfo = sector == null
                ? "sector=null"
                : $"sectorPortalCount={sector.PortalIds.Count} sectorTransitionCount={sector.PortalTransitions.Count} sectorDirty={sector.DirtyVersion} sectorBounds=({sector.StartX},{sector.StartY},{sector.Width},{sector.Height}) sectorPortals=[{string.Join(",", sector.PortalIds)}]";
        }

        string portalInfo = "portal=missing";
        if (TryGetPortalById(world, portalId, out PortalData portal))
        {
            portalInfo = $"portalSectors=({portal.SectorAId},{portal.SectorBId}) portalWidth={portal.WidthCells} portalNarrow={portal.IsNarrow} portalVertical={portal.IsVerticalBoundary} cellsA={FormatPortalCellsForDiagnostics(portal.CellsA, 4)} cellsB={FormatPortalCellsForDiagnostics(portal.CellsB, 4)}";
        }

        string states = BuildWorldStateDiagnosticsForPortalAccess();
        return $"diag=cacheCount={SectorPortalAccessCache.Count} sameWorldEntries={sameWorldEntries} sameWorldSectorEntries={sameWorldSectorEntries} samePortalAnyWorldEntries={samePortalAnyWorldEntries} sampleSameWorldSector={sampleSameWorldSector} sampleSamePortal={sampleSamePortal} worldAgentType={world.AgentTypeId} worldDirtyExpected={dirtyVersion} sectors={(world.Sectors != null ? world.Sectors.Length : -1)} portals={(world.Portals != null ? world.Portals.Length : -1)} {sectorInfo} {portalInfo} {states}";
    }

    private static string FormatPortalCellsForDiagnostics(Vector2Int[] cells, int maxCells)
    {
        if (cells == null)
            return "null";
        if (cells.Length == 0)
            return "empty";

        int count = Mathf.Min(cells.Length, Mathf.Max(1, maxCells));
        System.Text.StringBuilder builder = new System.Text.StringBuilder(96);
        builder.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(';');

            builder.Append(cells[i].x);
            builder.Append(',');
            builder.Append(cells[i].y);
        }

        if (cells.Length > count)
        {
            builder.Append(";...");
            builder.Append(cells.Length);
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildWorldStateDiagnosticsForPortalAccess()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        builder.Append("worldStates=[");
        int appended = 0;
        foreach (KeyValuePair<int, WorldRuntimeState> pair in WorldStates)
        {
            if (appended > 0)
                builder.Append(" | ");

            WorldRuntimeState state = pair.Value;
            builder.Append("agentType=");
            builder.Append(pair.Key);
            builder.Append(",world=");
            builder.Append(state?.World != null ? state.World.Version.ToString() : "null");
            builder.Append(",dirty=");
            builder.Append(state != null && state.IsDirty);
            builder.Append(",build=");
            builder.Append(state?.BuildJob != null ? state.BuildJob.Stage.ToString() : "null");
            builder.Append(",runtime=");
            builder.Append(state?.RuntimeDirtyJob != null ? state.RuntimeDirtyJob.Stage.ToString() : "null");
            appended++;
            if (appended >= 8)
                break;
        }

        if (appended == 0)
            builder.Append("none");
        builder.Append(']');
        return builder.ToString();
    }


    private static void TrimSectorPortalAccessCache()
    {
        // Sector portal access fields are required graph data for strict path building.
        // Dirty-sector invalidation removes stale entries; LRU eviction would create missing-field failures.
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
        return IsFlowReachable(tile, localIndex);
    }

    private static bool AreCellsConnectedInsideSector(SectorData sector, int startX, int startY, int goalX, int goalY)
    {
        if (sector == null)
            throw new InvalidOperationException("AreCellsConnectedInsideSector failed: sector is null.");
        if (!IsInsideSector(sector, startX, startY) || !IsInsideSector(sector, goalX, goalY))
            return false;
        if (sector.LocalComponentIds == null || sector.LocalComponentIds.Length != sector.Width * sector.Height)
            throw new InvalidOperationException($"AreCellsConnectedInsideSector failed: local components missing sector={sector.SectorId}.");

        int startComponent = sector.LocalComponentIds[GetSectorLocalIndex(sector, startX, startY)];
        int goalComponent = sector.LocalComponentIds[GetSectorLocalIndex(sector, goalX, goalY)];
        return startComponent > 0 && startComponent == goalComponent;
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
            float cost = GetTileIntegrationCostForDiagnostics(tile, startX, startY);
            startCost = float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3");
            startFlow = ResolveRuntimeFlowDirection(tile, startX, startY, localIndex).ToString();
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
                int oldGoalX = handle.GoalX;
                int oldGoalY = handle.GoalY;
                _perf.PathBuildGoalCellMismatch++;
                if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowPathGoalCellUpdate] agent={agent.CharacterKey} action=rebuild startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) oldGoal=({oldGoalX},{oldGoalY}) newGoal=({goalX},{goalY}) " +
                        $"source={handle.BuildSource ?? "unknown"} decision={BuildPathRepathDecisionDiagnostics(handle, startSectorId, goalSectorId, startX, startY, goalX, goalY)} " +
                        $"handle={FormatPathHandle(handle)}");
                }

                PathHandle previousHandle = handle;
                handle = BuildPathHandle(startSectorId, goalSectorId, startX, startY, goalX, goalY);
                agent.NavState.PathHandle = handle;
                if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowPathRebuild] agent={agent.CharacterKey} reason=goalCellMismatch startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) goal=({goalX},{goalY}) oldHandle={FormatPathHandle(previousHandle)} result={(handle != null ? "ok" : "null")} newHandle={FormatPathHandle(handle)}");
                }

                return handle != null;
            }

            bool shouldRebuild = ShouldRebuildPathHandleForCurrentStart(
                    handle,
                    startSectorId,
                    goalSectorId,
                    startX,
                    startY,
                    goalX,
                    goalY,
                    out string currentStartReason);
            if (GameDebugSettings.IsEnabled(DebugCategory.Move)
                && (!string.IsNullOrEmpty(currentStartReason) || shouldRebuild))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowPathRepathDecision] agent={agent.CharacterKey} rebuild={shouldRebuild} reason={currentStartReason} " +
                    $"startSector={startSectorId} goalSector={goalSectorId} start=({startX},{startY}) goal=({goalX},{goalY}) " +
                    $"source={handle.BuildSource ?? "unknown"} decision={BuildPathRepathDecisionDiagnostics(handle, startSectorId, goalSectorId, startX, startY, goalX, goalY)} " +
                    $"handle={FormatPathHandle(handle)}");
            }

            if (shouldRebuild)
            {
                PathHandle previousHandle = handle;
                IncrementPathHandleRebuildReason("startPortalMismatch");
                handle = BuildPathHandle(startSectorId, goalSectorId, startX, startY, goalX, goalY);
                agent.NavState.PathHandle = handle;
                if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                {
                    GameDebugSettings.Log(DebugCategory.Move,
                        $"[FlowPathRebuild] agent={agent.CharacterKey} reason={currentStartReason} startSector={startSectorId} goalSector={goalSectorId} " +
                        $"start=({startX},{startY}) goal=({goalX},{goalY}) oldHandle={FormatPathHandle(previousHandle)} result={(handle != null ? "ok" : "null")} newHandle={FormatPathHandle(handle)}");
                }

                return handle != null;
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

    private static bool ShouldRebuildPathHandleForCurrentStart(
        PathHandle handle,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out string reason)
    {
        reason = string.Empty;
        if (handle == null
            || handle.PortalIds == null
            || handle.PortalIds.Length == 0
            || handle.SectorIds == null
            || handle.SectorIds.Length == 0)
        {
            return false;
        }

        int sectorIndex = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (sectorIndex < 0 || sectorIndex >= handle.PortalIds.Length)
            return false;

        int selectedPortalId = handle.PortalIds[sectorIndex];
        if (!TryResolveBestStartPortalForCurrentCell(
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                selectedPortalId,
                out int bestPortalId,
                out float selectedCost,
                out float bestCost))
        {
            return false;
        }

        if (selectedPortalId == bestPortalId || selectedCost <= bestCost + IntegrationSignificantImprovement)
            return false;

        reason =
            $"startPortalMismatch selected={selectedPortalId}:{FormatDiagnosticCost(selectedCost)} best={bestPortalId}:{FormatDiagnosticCost(bestCost)} sectorIndex={sectorIndex}";
        return true;
    }

    private static bool TryResolveBestStartPortalForCurrentCell(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int selectedPortalId,
        out int bestPortalId,
        out float selectedCost,
        out float bestCost)
    {
        bestPortalId = -1;
        selectedCost = float.PositiveInfinity;
        bestCost = float.PositiveInfinity;
        if (_world == null || startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return false;

        int sharedAgentTypeId = ResolvePreferredAgentTypeId(_world.AgentTypeId);
        if (!TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, sharedAgentTypeId, out SharedGoalField sharedField))
            return false;

        SectorData startSector = _world.Sectors[startSectorId];
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            if (!sharedField.NodeCosts.TryGetValue(startNode, out float downstreamCost))
                continue;

            float accessCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            if (float.IsPositiveInfinity(accessCost))
                continue;

            float totalCost = accessCost + downstreamCost;
            if (portalId == selectedPortalId)
                selectedCost = totalCost;
            if (totalCost >= bestCost)
                continue;

            bestCost = totalCost;
            bestPortalId = portalId;
        }

        return bestPortalId >= 0;
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

    private static bool TryGetCachedSharedGoalField(int goalSectorId, int goalX, int goalY, int agentTypeId, out SharedGoalField field)
    {
        SharedGoalFieldKey key = CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, agentTypeId);
        if (SharedGoalFields.TryGetValue(key, out field)
            && !SharedGoalFieldReferencesMissingPortal(_world, field))
        {
            field.LastUsedFrame = GetFrameCount();
            return true;
        }

        field = null;
        return false;
    }

    private static SharedGoalFieldKey CreateSharedGoalFieldKey(int goalSectorId, int goalX, int goalY, int agentTypeId)
    {
        SectorData goalSector = _world.Sectors[goalSectorId];
        return new SharedGoalFieldKey(
            _world.Version,
            agentTypeId,
            goalSectorId,
            _world.GetIndex(goalX, goalY),
            goalSector.DirtyVersion);
    }

    private static void EnqueueSharedGoalFieldBuild(int goalSectorId, int goalX, int goalY, int agentTypeId)
    {
        SharedGoalFieldKey key = CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, agentTypeId);
        if (SharedGoalFields.ContainsKey(key))
            return;
        if (!PendingSharedGoalFieldBuildJobs.Add(key))
            return;

        _perf.PathPendingSharedGoal++;
        SharedGoalFieldBuildQueue.AddLast(new SharedGoalFieldBuildJob
        {
            Key = key,
            GoalSectorId = goalSectorId,
            GoalX = goalX,
            GoalY = goalY,
            AgentTypeId = agentTypeId
        });
    }

    private static bool AdvanceSharedGoalFieldBuildJob(SharedGoalFieldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        while (job.Stage != SharedGoalFieldBuildStage.Complete)
        {
            switch (job.Stage)
            {
                case SharedGoalFieldBuildStage.GoalIntegration:
                    if (!AdvanceSharedGoalFieldGoalIntegration(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                case SharedGoalFieldBuildStage.PortalGraph:
                    if (!AdvanceSharedGoalFieldPortalGraph(job, deadlineTicks, forceComplete))
                        return false;
                    break;
                case SharedGoalFieldBuildStage.Commit:
                    CommitSharedGoalFieldBuildJob(job);
                    break;
                default:
                    throw new InvalidOperationException($"AdvanceSharedGoalFieldBuildJob failed: unknown stage {job.Stage}.");
            }

            if (job.Stage == SharedGoalFieldBuildStage.Complete)
                break;

            if (!forceComplete && Stopwatch.GetTimestamp() >= deadlineTicks)
                return false;
        }

        return true;
    }

    private static bool AdvanceSharedGoalFieldGoalIntegration(SharedGoalFieldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        if (job.GoalSector == null)
        {
            job.GoalSector = _world.Sectors[job.GoalSectorId];
            job.GoalIntegration = RentIntegrationArray(job.GoalSector.Width * job.GoalSector.Height);
            InitializeIntegrationField(job.GoalIntegration);
            job.GoalIntegrationOpenSet = new MinHeap();
            int localIndex = GetSectorLocalIndex(job.GoalSector, job.GoalX, job.GoalY);
            job.GoalIntegration[localIndex] = 0f;
            job.GoalIntegrationOpenSet.Push(localIndex, 0f);
            _perf.SynchronousSectorIntegrations++;
        }

        int budgetWork = 0;
        while (job.GoalIntegrationOpenSet.Count > 0)
        {
            QueueNode node = job.GoalIntegrationOpenSet.Pop();
            if (node.Cost > job.GoalIntegration[node.Index] + 0.0001f)
                continue;

            int localX = node.Index % job.GoalSector.Width;
            int localY = node.Index / job.GoalSector.Width;
            int worldX = job.GoalSector.StartX + localX;
            int worldY = job.GoalSector.StartY + localY;

            for (int i = 0; i < CardinalOffsetX.Length; i++)
            {
                int nextWorldX = worldX + CardinalOffsetX[i];
                int nextWorldY = worldY + CardinalOffsetY[i];
                if (!IsInsideSector(job.GoalSector, nextWorldX, nextWorldY) || !_world.IsWalkable(nextWorldX, nextWorldY))
                    continue;
                if (!CanTraverseNeighborCells(_world, nextWorldX, nextWorldY, worldX, worldY))
                    continue;

                int nextLocalIndex = GetSectorLocalIndex(job.GoalSector, nextWorldX, nextWorldY);
                float newCost = ResolveEikonalIntegrationCost(_world, job.GoalSector, job.GoalIntegration, nextWorldX, nextWorldY, reverseTraversal: true);
                if (!IsSignificantIntegrationImprovement(newCost, job.GoalIntegration[nextLocalIndex]))
                    continue;

                job.GoalIntegration[nextLocalIndex] = newCost;
                job.GoalIntegrationOpenSet.Push(nextLocalIndex, newCost);
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, ++budgetWork))
                return false;
        }

        job.Field = BeginSharedGoalField(job.Key, job.GoalSectorId, job.GoalX, job.GoalY, job.GoalIntegration, out job.PortalOpenSet);
        ReturnSharedGoalFieldBuildIntegration(job);
        if (job.Field == null)
        {
            job.Stage = SharedGoalFieldBuildStage.Complete;
            return true;
        }

        job.Stage = SharedGoalFieldBuildStage.PortalGraph;
        return true;
    }

    private static bool AdvanceSharedGoalFieldPortalGraph(SharedGoalFieldBuildJob job, long deadlineTicks, bool forceComplete)
    {
        int budgetWork = 0;
        while (job.PortalOpenSet.Count > 0)
        {
            QueueNode node = job.PortalOpenSet.Pop();
            int currentNode = node.Index;
            if (!job.Field.NodeCosts.TryGetValue(currentNode, out float currentCost) || node.Cost > currentCost + 0.001f)
                continue;

            DecodePortalNode(currentNode, out int currentSectorId, out int currentPortalId);
            PortalData currentPortal = GetPortalById(_world, currentPortalId);
            int oppositeSectorId = GetOppositeSectorId(currentPortal, currentSectorId);
            int oppositeNode = EncodePortalNode(oppositeSectorId, currentPortalId);
            AddSharedGoalReverseEdge(job.Field, job.PortalOpenSet, oppositeNode, currentNode, currentCost + 1f);

            SectorData sector = _world.Sectors[currentSectorId];
            for (int i = 0; i < sector.PortalTransitions.Count; i++)
            {
                PortalTransition transition = sector.PortalTransitions[i];
                if (transition.ToPortalId != currentPortalId)
                    continue;

                int predecessorNode = EncodePortalNode(currentSectorId, transition.FromPortalId);
                AddSharedGoalReverseEdge(job.Field, job.PortalOpenSet, predecessorNode, currentNode, currentCost + transition.Cost);
            }

            if (!forceComplete && IsBudgetExpired(deadlineTicks, ++budgetWork))
                return false;
        }

        job.Stage = SharedGoalFieldBuildStage.Commit;
        return true;
    }

    private static void CommitSharedGoalFieldBuildJob(SharedGoalFieldBuildJob job)
    {
        if (job.Field != null)
        {
            SharedGoalFields[job.Key] = job.Field;
            _perf.SharedGoalFieldBuilds++;
            TrimSharedGoalFields();
        }

        job.Stage = SharedGoalFieldBuildStage.Complete;
    }

    private static SharedGoalField BeginSharedGoalField(SharedGoalFieldKey key, int goalSectorId, int goalX, int goalY, float[] goalIntegration, out MinHeap openSet)
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

        openSet = new MinHeap();
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
            openSet.Push(goalNode, goalCost);
        }

        if (openSet.Count == 0)
            return null;

        return field;
    }

    private static void AddSharedGoalReverseEdge(SharedGoalField field, MinHeap openSet, int predecessorNode, int nextNodeTowardGoal, float cost)
    {
        if (field.NodeCosts.TryGetValue(predecessorNode, out float existingCost) && cost >= existingCost)
            return;

        field.NodeCosts[predecessorNode] = cost;
        field.NextNodeTowardGoal[predecessorNode] = nextNodeTowardGoal;
        openSet.Push(predecessorNode, cost);
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
            case "startPortalMismatch":
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
            if (AreCellsConnectedInsideSector(startSector, startX, startY, goalX, goalY))
            {
                PathHandle sameSectorHandle = new PathHandle
                    {
                        HandleId = _nextPathHandleId++,
                        WorldVersion = _world.Version,
                        GoalX = goalX,
                        GoalY = goalY,
                        SectorIds = new[] { startSectorId },
                        PortalIds = Array.Empty<int>(),
                        CurrentSectorIndex = 0,
                        BuildSource = "sameSector"
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

        int sharedAgentTypeId = ResolvePreferredAgentTypeId(_world.AgentTypeId);
        EnqueueSharedGoalFieldBuild(goalSectorId, goalX, goalY, sharedAgentTypeId);
        if (TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, sharedAgentTypeId, out SharedGoalField sharedField)
            && TryBuildPathHandleFromSharedGoalField(
                sectorPathKey,
                sharedField,
                startSector,
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                out PathHandle sharedHandle))
        {
            return sharedHandle;
        }

        _perf.SectorPathSearches++;
        if (TryBuildPathHandleWithPortalGraphAStar(
            sectorPathKey,
            startSector,
            goalSector,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            out PathHandle graphHandle))
        {
            return graphHandle;
        }

        return null;
    }

    private static bool TryBuildPathHandleFromSharedGoalField(
        SectorPathCacheKey sectorPathKey,
        SharedGoalField sharedField,
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
        if (sharedField == null)
            return false;

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
            return false;

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
                    $"[FlowPathHandleBuild] result=null source=sharedGoal startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) bestStartNode={bestStartNode} bestStartCost={bestStartCost:F3} endSector={endSectorId} " +
                    $"sharedNodes={sharedField.NodeCosts.Count} sectorIdsPartial=[{string.Join(",", sectorIds)}] portalIdsPartial=[{string.Join(",", portalIds)}]");
            }
            return false;
        }

        handle = CreateAndCachePathHandle(sectorPathKey, sectorIds, portalIds, goalX, goalY);
        handle.BuildSource = "sharedGoal";
        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathHandleBuild] result=ok source=sharedGoal startSector={startSectorId} goalSector={goalSectorId} " +
                $"start=({startX},{startY}) goal=({goalX},{goalY}) handle={FormatPathHandle(handle)}");
        }

        return true;
    }

    private static bool TryBuildPathHandleWithPortalGraphAStar(
        SectorPathCacheKey sectorPathKey,
        SectorData startSector,
        SectorData goalSector,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        handle = null;
        MinHeap openSet = new MinHeap();
        Dictionary<int, float> nodeCosts = new Dictionary<int, float>(256);
        Dictionary<int, int> cameFrom = new Dictionary<int, int>(256);
        Dictionary<int, int> startPortalByNode = new Dictionary<int, int>(64);
        HashSet<int> goalNodes = new HashSet<int>();
        Dictionary<int, float> goalAccessCosts = new Dictionary<int, float>(64);
        Dictionary<int, ExistingPathMergePoint> mergeNodes = BuildExistingPathMergeNodes(startSectorId, goalSectorId);

        for (int i = 0; i < goalSector.PortalIds.Count; i++)
        {
            int portalId = goalSector.PortalIds[i];
            float goalAccessCost = ResolvePortalAccessCost(goalSector, goalSectorId, portalId, goalX, goalY);
            if (float.IsPositiveInfinity(goalAccessCost))
                continue;

            int goalNode = EncodePortalNode(goalSectorId, portalId);
            goalNodes.Add(goalNode);
            goalAccessCosts[goalNode] = goalAccessCost;
        }

        if (goalNodes.Count == 0)
            return false;

        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            float startCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            if (float.IsPositiveInfinity(startCost))
                continue;

            int startNode = EncodePortalNode(startSectorId, portalId);
            float priority = startCost + ResolvePortalGraphHeuristic(startNode, goalX, goalY);
            nodeCosts[startNode] = startCost;
            startPortalByNode[startNode] = portalId;
            openSet.Push(startNode, priority);
        }

        int bestGoalNode = int.MinValue;
        float bestGoalCost = float.PositiveInfinity;
        int guard = 0;
        while (openSet.Count > 0)
        {
            QueueNode node = openSet.Pop();
            int currentNode = node.Index;
            if (!nodeCosts.TryGetValue(currentNode, out float currentCost))
                continue;
            if (node.Cost > currentCost + ResolvePortalGraphHeuristic(currentNode, goalX, goalY) + 0.001f)
                continue;

            bool canFinishAtGoalNode = goalNodes.Contains(currentNode)
                                       && (startSectorId != goalSectorId || cameFrom.ContainsKey(currentNode));
            if (canFinishAtGoalNode)
            {
                float goalAccessCost = goalAccessCosts.TryGetValue(currentNode, out float foundGoalAccess)
                    ? foundGoalAccess
                    : 0f;
                float totalGoalCost = currentCost + goalAccessCost;
                if (totalGoalCost < bestGoalCost)
                {
                    bestGoalNode = currentNode;
                    bestGoalCost = totalGoalCost;
                }
            }

            if (currentCost >= bestGoalCost)
                continue;

            DecodePortalNode(currentNode, out int currentSectorId, out int currentPortalId);
            PortalData currentPortal = GetPortalById(_world, currentPortalId);
            int oppositeSectorId = GetOppositeSectorId(currentPortal, currentSectorId);
            int oppositeNode = EncodePortalNode(oppositeSectorId, currentPortalId);
            TryRelaxPortalGraphEdge(openSet, nodeCosts, cameFrom, mergeNodes, currentNode, oppositeNode, currentCost + 1f, goalX, goalY);

            SectorData sector = _world.Sectors[currentSectorId];
            for (int i = 0; i < sector.PortalTransitions.Count; i++)
            {
                PortalTransition transition = sector.PortalTransitions[i];
                if (transition.FromPortalId != currentPortalId)
                    continue;

                int nextNode = EncodePortalNode(currentSectorId, transition.ToPortalId);
                TryRelaxPortalGraphEdge(openSet, nodeCosts, cameFrom, mergeNodes, currentNode, nextNode, currentCost + transition.Cost, goalX, goalY);
            }

            guard++;
            if (guard > _world.Sectors.Length * Mathf.Max(1, _world.PortalsById.Count + 1))
                throw new InvalidOperationException("TryBuildPathHandleWithPortalGraphAStar failed: graph search exceeded guard.");
        }

        if (bestGoalNode == int.MinValue)
            return false;

        List<int> reversedNodes = new List<int>(16);
        int cursor = bestGoalNode;
        reversedNodes.Add(cursor);
        guard = 0;
        while (cameFrom.TryGetValue(cursor, out int previousNode))
        {
            cursor = previousNode;
            reversedNodes.Add(cursor);
            guard++;
            if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("TryBuildPathHandleWithPortalGraphAStar failed: reconstruction exceeded guard.");
        }

        reversedNodes.Reverse();
        if (!TryConvertPortalNodesToPath(reversedNodes, startSectorId, goalSectorId, out List<int> sectorIds, out List<int> portalIds))
            return false;

        handle = CreateAndCachePathHandle(sectorPathKey, sectorIds, portalIds, goalX, goalY);
        handle.BuildSource = "portalGraph";
        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            int startPortalId = startPortalByNode.TryGetValue(reversedNodes[0], out int foundStartPortal) ? foundStartPortal : -1;
            GameDebugSettings.Log(DebugCategory.Move,
                $"[FlowPathHandleBuild] result=ok source=portalGraph startSector={startSectorId} goalSector={goalSectorId} " +
                $"start=({startX},{startY}) goal=({goalX},{goalY}) startPortal={startPortalId} cost={bestGoalCost:F3} handle={FormatPathHandle(handle)}");
        }

        return true;
    }

    private readonly struct ExistingPathMergePoint
    {
        public readonly int[] SectorIds;
        public readonly int[] PortalIds;
        public readonly int SectorIndex;

        public ExistingPathMergePoint(int[] sectorIds, int[] portalIds, int sectorIndex)
        {
            SectorIds = sectorIds;
            PortalIds = portalIds;
            SectorIndex = sectorIndex;
        }
    }

    private static Dictionary<int, ExistingPathMergePoint> BuildExistingPathMergeNodes(int startSectorId, int goalSectorId)
    {
        Dictionary<int, ExistingPathMergePoint> mergeNodes = null;
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null
                || handle.WorldVersion != _world.Version
                || handle.SectorIds == null
                || handle.PortalIds == null
                || handle.SectorIds.Length <= 1
                || handle.SectorIds[handle.SectorIds.Length - 1] != goalSectorId
                || PathReferencesMissingPortal(handle))
            {
                continue;
            }

            for (int i = Mathf.Max(0, handle.CurrentSectorIndex); i < handle.PortalIds.Length; i++)
            {
                int sectorId = handle.SectorIds[i];
                if (sectorId == startSectorId)
                    continue;

                int node = EncodePortalNode(sectorId, handle.PortalIds[i]);
                mergeNodes ??= new Dictionary<int, ExistingPathMergePoint>(32);
                if (!mergeNodes.ContainsKey(node))
                {
                    mergeNodes[node] = new ExistingPathMergePoint(
                        (int[])handle.SectorIds.Clone(),
                        (int[])handle.PortalIds.Clone(),
                        i);
                }
            }
        }

        return mergeNodes ?? EmptyMergeNodes;
    }

    private static readonly Dictionary<int, ExistingPathMergePoint> EmptyMergeNodes = new Dictionary<int, ExistingPathMergePoint>(0);

    private static bool TryConvertPortalNodesToMergedPath(
        int mergeNode,
        Dictionary<int, int> cameFrom,
        ExistingPathMergePoint mergePoint,
        int startSectorId,
        int goalSectorId,
        out List<int> sectorIds,
        out List<int> portalIds)
    {
        sectorIds = null;
        portalIds = null;
        List<int> prefixNodes = new List<int>(16);
        int cursor = mergeNode;
        prefixNodes.Add(cursor);
        int guard = 0;
        while (cameFrom.TryGetValue(cursor, out int previousNode))
        {
            cursor = previousNode;
            prefixNodes.Add(cursor);
            guard++;
            if (guard > _world.Sectors.Length + _world.PortalsById.Count)
                throw new InvalidOperationException("TryConvertPortalNodesToMergedPath failed: prefix reconstruction exceeded guard.");
        }

        prefixNodes.Reverse();
        if (!TryConvertPortalNodesToPathPrefix(prefixNodes, startSectorId, out sectorIds, out portalIds))
            return false;

        int mergeSector = mergePoint.SectorIds[mergePoint.SectorIndex];
        if (sectorIds[sectorIds.Count - 1] != mergeSector)
            return false;

        for (int i = mergePoint.SectorIndex; i < mergePoint.PortalIds.Length; i++)
        {
            portalIds.Add(mergePoint.PortalIds[i]);
            sectorIds.Add(mergePoint.SectorIds[i + 1]);
        }

        return portalIds.Count > 0 && sectorIds[sectorIds.Count - 1] == goalSectorId;
    }

    private static bool TryConvertPortalNodesToPathPrefix(List<int> nodes, int startSectorId, out List<int> sectorIds, out List<int> portalIds)
    {
        sectorIds = new List<int>(8) { startSectorId };
        portalIds = new List<int>(8);
        if (nodes == null || nodes.Count == 0)
            return false;

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            DecodePortalNode(nodes[i], out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nodes[i + 1], out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId)
            {
                portalIds.Add(fromPortalId);
                sectorIds.Add(toSectorId);
            }
        }

        return sectorIds.Count > 0;
    }

    private static void TryRelaxPortalGraphEdge(
        MinHeap openSet,
        Dictionary<int, float> nodeCosts,
        Dictionary<int, int> cameFrom,
        Dictionary<int, ExistingPathMergePoint> mergeNodes,
        int fromNode,
        int toNode,
        float newCost,
        int goalX,
        int goalY)
    {
        if (nodeCosts.TryGetValue(toNode, out float existingCost) && newCost >= existingCost)
            return;

        nodeCosts[toNode] = newCost;
        cameFrom[toNode] = fromNode;
        float priority = newCost + ResolvePortalGraphHeuristic(toNode, goalX, goalY);
        if (mergeNodes != null && mergeNodes.ContainsKey(toNode))
            priority -= PortalGraphMergePriorityBias;
        openSet.Push(toNode, priority);
    }

    private static float ResolvePortalGraphHeuristic(int node, int goalX, int goalY)
    {
        DecodePortalNode(node, out _, out int portalId);
        PortalData portal = GetPortalById(_world, portalId);
        Vector3 goalWorld = _world.GridToWorldCenter(goalX, goalY);
        return HorizontalDistanceXZ(portal.WorldCenter, goalWorld) / Mathf.Max(_world.CellSize, 0.001f);
    }

    private static bool TryConvertPortalNodesToPath(List<int> nodes, int startSectorId, int goalSectorId, out List<int> sectorIds, out List<int> portalIds)
    {
        sectorIds = new List<int>(8) { startSectorId };
        portalIds = new List<int>(8);
        if (nodes == null || nodes.Count == 0)
            return false;

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            DecodePortalNode(nodes[i], out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nodes[i + 1], out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && fromSectorId != toSectorId)
            {
                portalIds.Add(fromPortalId);
                sectorIds.Add(toSectorId);
            }
        }

        return portalIds.Count > 0 && sectorIds[sectorIds.Count - 1] == goalSectorId;
    }

    private static PathHandle CreateAndCachePathHandle(SectorPathCacheKey sectorPathKey, List<int> sectorIds, List<int> portalIds, int goalX, int goalY)
    {
        if (sectorIds == null || sectorIds.Count == 0 || portalIds == null)
            throw new InvalidOperationException("CreateAndCachePathHandle failed: invalid path lists.");

        PathHandle handle = new PathHandle
        {
            HandleId = _nextPathHandleId++,
            WorldVersion = _world.Version,
            GoalX = goalX,
            GoalY = goalY,
            SectorIds = sectorIds.ToArray(),
            PortalIds = portalIds.ToArray(),
            CurrentSectorIndex = 0,
            BuildSource = "created"
        };
        SectorPathCache[sectorPathKey] = new SectorPathCacheEntry
        {
            SectorIds = (int[])handle.SectorIds.Clone(),
            PortalIds = (int[])handle.PortalIds.Clone(),
            LastUsedFrame = GetFrameCount()
        };
        TrimSectorPathCache();
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

        if (TryResolveBestStartPortalForCurrentCell(
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                firstPortalId,
                out int bestPortalId,
                out float selectedCost,
                out float bestCost)
            && firstPortalId != bestPortalId
            && selectedCost > bestCost + IntegrationSignificantImprovement)
        {
            SectorPathCache.Remove(key);
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                GameDebugSettings.Log(DebugCategory.Move,
                    $"[FlowPathCacheReject] reason=startPortalMismatch startSector={startSectorId} goalSector={goalSectorId} " +
                    $"start=({startX},{startY}) goal=({goalX},{goalY}) selected={firstPortalId}:{FormatDiagnosticCost(selectedCost)} " +
                    $"best={bestPortalId}:{FormatDiagnosticCost(bestCost)}");
            }

            return false;
        }

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
            CurrentSectorIndex = 0,
            BuildSource = "sectorPathCache"
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

    private static int ResolveCurrentDownstreamPortalId(PathHandle handle)
    {
        if (handle == null || handle.PortalIds == null || handle.SectorIds == null)
            return -1;

        int index = Mathf.Clamp(handle.CurrentSectorIndex, 0, Mathf.Max(0, handle.SectorIds.Length - 1));
        return index >= 0 && index < handle.PortalIds.Length ? handle.PortalIds[index] : -1;
    }

    private static bool TryResolvePendingPortalDirection(
        AgentRuntimeData agent,
        int currentSectorId,
        int currentX,
        int currentY,
        Vector3 finalGoalPosition,
        out DesiredDirectionResolution resolution,
        out string failureReason)
    {
        resolution = default;
        failureReason = string.Empty;
        PathHandle handle = agent?.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
        {
            failureReason = $"invalid handle={FormatPathHandle(handle)}";
            return false;
        }

        int finalGoalX = handle.GoalX;
        int finalGoalY = handle.GoalY;
        if (TryResolveGoalCell(_world, finalGoalPosition, out int resolvedFinalGoalX, out int resolvedFinalGoalY))
        {
            finalGoalX = resolvedFinalGoalX;
            finalGoalY = resolvedFinalGoalY;
        }

        int currentIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        if (currentIndex >= handle.SectorIds.Length - 1)
        {
            Vector3 toFinalGoal = finalGoalPosition - _world.GridToWorldCenter(currentX, currentY);
            toFinalGoal.y = 0f;
            if (toFinalGoal.sqrMagnitude <= 0.0001f)
            {
                resolution = new DesiredDirectionResolution(
                    Vector3.zero,
                    DesiredDirectionSource.PendingPortal,
                    finalGoalPosition,
                    Vector2.zero,
                    false,
                    0f);
                return true;
            }

            resolution = new DesiredDirectionResolution(
                toFinalGoal.normalized,
                DesiredDirectionSource.PendingPortal,
                finalGoalPosition,
                Vector2.zero,
                false,
                float.PositiveInfinity);
            return true;
        }

        int portalId = ResolveCurrentDownstreamPortalId(handle);
        if (portalId < 0 || !TryGetPortalById(_world, portalId, out PortalData portal))
        {
            failureReason = $"invalid downstream portal portal={portalId} currentIndex={currentIndex} handle={FormatPathHandle(handle)}";
            return false;
        }

        if (!TryResolvePendingPortalAccessDirection(currentSectorId, portal, currentX, currentY, finalGoalX, finalGoalY, out Vector3 direction, out Vector3 target))
        {
            failureReason = $"portal access direction unavailable portal={portalId} current=({currentX},{currentY}) finalGoal=({finalGoalX},{finalGoalY}) handle={FormatPathHandle(handle)}";
            return false;
        }

        resolution = new DesiredDirectionResolution(
            direction,
            DesiredDirectionSource.PendingPortal,
            target,
            new Vector2(direction.x, direction.z),
            false,
            float.PositiveInfinity);
        return true;
    }

    private static bool TryResolvePendingPortalAccessDirection(
        int sectorId,
        PortalData portal,
        int currentX,
        int currentY,
        int finalGoalX,
        int finalGoalY,
        out Vector3 direction,
        out Vector3 target)
    {
        direction = Vector3.zero;
        target = Vector3.zero;
        SectorData sector = _world.Sectors[sectorId];
        SectorPortalAccessEntry entry = GetPrebuiltSectorPortalAccess(sector, sectorId, portal.PortalId);
        int localIndex = GetSectorLocalIndex(sector, currentX, currentY);
        float currentCost = DecodePortalAccessIntegrationCost(entry, localIndex);
        if (float.IsPositiveInfinity(currentCost))
            return false;

        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, sectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, sectorId));
        if (currentSideCells.Length != oppositeSideCells.Length)
            throw new InvalidOperationException(
                $"TryResolvePendingPortalAccessDirection failed: portal {portal.PortalId} side cell count mismatch current={currentSideCells.Length} opposite={oppositeSideCells.Length}");

        int selectedPairIndex = ResolveReachablePortalPairIndexForFinalGoal(portal, sector, entry, currentSideCells, oppositeSideCells, finalGoalX, finalGoalY);
        if (selectedPairIndex < 0)
            return false;

        Vector2Int selectedCurrentCell = currentSideCells[selectedPairIndex];
        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);

        for (int i = 0; i < currentSideCells.Length; i++)
        {
            Vector2Int currentCell = currentSideCells[i];
            if (currentCell.x != currentX || currentCell.y != currentY)
                continue;

            Vector2Int oppositeCell = oppositeSideCells[i];
            if (!_world.IsWalkable(oppositeCell.x, oppositeCell.y)
                || !CanTraverseNeighborCells(_world, currentX, currentY, oppositeCell.x, oppositeCell.y))
            {
                return false;
            }

            target = _world.GridToWorldCenter(oppositeCell.x, oppositeCell.y);
            Vector3 toOpposite = target - currentCenter;
            toOpposite.y = 0f;
            if (toOpposite.sqrMagnitude <= 0.0001f)
                return false;

            direction = toOpposite.normalized;
            return true;
        }

        if (HasGridLineOfSight(_world, currentX, currentY, selectedCurrentCell.x, selectedCurrentCell.y))
        {
            target = _world.GridToWorldCenter(selectedCurrentCell.x, selectedCurrentCell.y);
            Vector3 toSelectedPortalLane = target - currentCenter;
            toSelectedPortalLane.y = 0f;
            if (toSelectedPortalLane.sqrMagnitude > 0.0001f)
            {
                direction = toSelectedPortalLane.normalized;
                return true;
            }
        }

        float bestCost = currentCost;
        float bestLaneDistanceSq = float.MaxValue;
        Vector2 bestDirection = Vector2.zero;
        int bestStepDistanceSq = int.MaxValue;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = currentX + NeighborOffsetX[i];
            int nextY = currentY + NeighborOffsetY[i];
            if (!IsInsideSector(sector, nextX, nextY) || !_world.IsWalkable(nextX, nextY))
                continue;
            if (!CanTraverseNeighborCells(_world, currentX, currentY, nextX, nextY))
                continue;

            float candidate = DecodePortalAccessIntegrationCost(entry, GetSectorLocalIndex(sector, nextX, nextY));
            if (float.IsPositiveInfinity(candidate) || candidate >= currentCost - 0.001f)
                continue;

            float laneDistanceSq = SquaredCellDistance(nextX, nextY, selectedCurrentCell.x, selectedCurrentCell.y);
            int stepDistanceSq = NeighborOffsetX[i] * NeighborOffsetX[i] + NeighborOffsetY[i] * NeighborOffsetY[i];
            bool betterCost = candidate < bestCost - 0.001f;
            bool sameCostBetterLane = Mathf.Abs(candidate - bestCost) <= 0.001f && laneDistanceSq < bestLaneDistanceSq - 0.001f;
            bool sameCostSameLaneShorterStep = Mathf.Abs(candidate - bestCost) <= 0.001f
                                              && Mathf.Abs(laneDistanceSq - bestLaneDistanceSq) <= 0.001f
                                              && stepDistanceSq < bestStepDistanceSq;
            if (!betterCost && !sameCostBetterLane && !sameCostSameLaneShorterStep)
                continue;

            bestCost = candidate;
            bestLaneDistanceSq = laneDistanceSq;
            bestStepDistanceSq = stepDistanceSq;
            bestDirection = new Vector2(nextX - currentX, nextY - currentY).normalized;
        }

        if (bestDirection.sqrMagnitude <= 0.0001f)
            return false;

        direction = new Vector3(bestDirection.x, 0f, bestDirection.y);
        target = _world.GridToWorldCenter(selectedCurrentCell.x, selectedCurrentCell.y);
        return true;
    }

    private static Vector3 ResolvePortalApproachTarget(PortalData portal, int sectorId, int currentX, int currentY)
    {
        if (portal == null)
            throw new InvalidOperationException("ResolvePortalApproachTarget failed: portal is null.");
        if (_world == null)
            throw new InvalidOperationException("ResolvePortalApproachTarget failed: world is null.");

        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, sectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, sectorId));
        if (currentSideCells.Length != oppositeSideCells.Length)
            throw new InvalidOperationException(
                $"ResolvePortalApproachTarget failed: portal {portal.PortalId} side cell count mismatch current={currentSideCells.Length} opposite={oppositeSideCells.Length}");

        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);
        float bestDistanceSq = float.MaxValue;
        Vector3 bestTarget = Vector3.zero;
        bool found = false;
        for (int i = 0; i < currentSideCells.Length; i++)
        {
            Vector2Int currentCell = currentSideCells[i];
            if (!HasGridLineOfSight(_world, currentX, currentY, currentCell.x, currentCell.y))
                continue;

            Vector2Int oppositeCell = oppositeSideCells[i];
            Vector3 candidate = _world.GridToWorldCenter(oppositeCell.x, oppositeCell.y);
            float distanceSq = (candidate - currentCenter).sqrMagnitude;
            if (found && distanceSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distanceSq;
            bestTarget = candidate;
            found = true;
        }

        return found ? bestTarget : ResolveCellGroupCenter(oppositeSideCells);
    }

    private static int ResolvePortalPairIndexForFinalGoal(PortalData portal, int sectorId, int finalGoalX, int finalGoalY)
    {
        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, sectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, sectorId));
        if (currentSideCells.Length == 0 || currentSideCells.Length != oppositeSideCells.Length)
            throw new InvalidOperationException(
                $"ResolvePortalPairIndexForFinalGoal failed: portal {portal.PortalId} side cell count mismatch current={currentSideCells.Length} opposite={oppositeSideCells.Length}");

        int bestIndex = 0;
        float bestLaneDistanceSq = float.MaxValue;
        float bestGoalDistanceSq = float.MaxValue;
        for (int i = 0; i < oppositeSideCells.Length; i++)
        {
            Vector2Int oppositeCell = oppositeSideCells[i];
            float laneDistanceSq = ResolvePortalLaneDistanceSq(portal, oppositeCell, finalGoalX, finalGoalY);
            float goalDistanceSq = SquaredCellDistance(oppositeCell.x, oppositeCell.y, finalGoalX, finalGoalY);
            if (laneDistanceSq > bestLaneDistanceSq + 0.001f)
                continue;
            if (Mathf.Abs(laneDistanceSq - bestLaneDistanceSq) <= 0.001f && goalDistanceSq >= bestGoalDistanceSq)
                continue;

            bestIndex = i;
            bestLaneDistanceSq = laneDistanceSq;
            bestGoalDistanceSq = goalDistanceSq;
        }

        return bestIndex;
    }

    private static int ResolveReachablePortalPairIndexForFinalGoal(
        PortalData portal,
        SectorData sector,
        SectorPortalAccessEntry entry,
        Vector2Int[] currentSideCells,
        Vector2Int[] oppositeSideCells,
        int finalGoalX,
        int finalGoalY)
    {
        int bestIndex = -1;
        float bestLaneDistanceSq = float.MaxValue;
        float bestGoalDistanceSq = float.MaxValue;
        for (int i = 0; i < currentSideCells.Length; i++)
        {
            Vector2Int currentCell = currentSideCells[i];
            if (!IsInsideSector(sector, currentCell.x, currentCell.y))
                continue;

            float portalCost = DecodePortalAccessIntegrationCost(entry, GetSectorLocalIndex(sector, currentCell.x, currentCell.y));
            if (float.IsPositiveInfinity(portalCost))
                continue;

            Vector2Int oppositeCell = oppositeSideCells[i];
            float laneDistanceSq = ResolvePortalLaneDistanceSq(portal, oppositeCell, finalGoalX, finalGoalY);
            float goalDistanceSq = SquaredCellDistance(oppositeCell.x, oppositeCell.y, finalGoalX, finalGoalY);
            if (laneDistanceSq > bestLaneDistanceSq + 0.001f)
                continue;
            if (Mathf.Abs(laneDistanceSq - bestLaneDistanceSq) <= 0.001f && goalDistanceSq >= bestGoalDistanceSq)
                continue;

            bestIndex = i;
            bestLaneDistanceSq = laneDistanceSq;
            bestGoalDistanceSq = goalDistanceSq;
        }

        return bestIndex;
    }


    private static bool TryDecodeExactFinalGoalIndex(int finalGoalIndex, out int finalGoalX, out int finalGoalY)
    {
        finalGoalX = -1;
        finalGoalY = -1;
        if (_world == null || finalGoalIndex < 0)
            return false;

        finalGoalX = finalGoalIndex % _world.Width;
        finalGoalY = finalGoalIndex / _world.Width;
        return finalGoalX >= 0 && finalGoalX < _world.Width && finalGoalY >= 0 && finalGoalY < _world.Height;
    }

    private static float ResolvePortalLaneDistanceSq(PortalData portal, Vector2Int portalCell, int finalGoalX, int finalGoalY)
    {
        int portalAxis = portal.IsVerticalBoundary ? portalCell.y : portalCell.x;
        int finalAxis = portal.IsVerticalBoundary ? finalGoalY : finalGoalX;
        int delta = portalAxis - finalAxis;
        return delta * delta;
    }

    private static float SquaredCellDistance(int ax, int ay, int bx, int by)
    {
        int dx = ax - bx;
        int dy = ay - by;
        return dx * dx + dy * dy;
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
            agent.NavState.CurrentTileKeyHash = key.GetHashCode();
            return true;
        }

        _perf.TileCacheMisses++;
        EnqueueFlowTileBuildChain(handle, sectorPathIndex, goalX, goalY, agent.AgentTypeId);
        if (goalKind == TileGoalKind.FinalGoal && sectorPathIndex == handle.SectorIds.Length - 1)
            ProcessFlowTileBuildQueue(long.MaxValue, forceComplete: true, requiredKey: key);
        if (FlowTileCache.TryGetValue(key, out tile))
        {
            _perf.TileCacheHits++;
            tile.LastUsedFrame = GetFrameCount();
            agent.NavState.CurrentTileKeyHash = key.GetHashCode();
            return true;
        }

        _perf.TilePendingBuild++;
        return false;
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
            bool builtOrCached = TryBuildOrGetTile(agent, goalX, goalY, out tile, out goalKind, out downstreamPortalId);
            if (!builtOrCached && IsCurrentPathSegmentPendingPortal(agent, out int pendingPortalId))
            {
                goalKind = TileGoalKind.Portal;
                downstreamPortalId = pendingPortalId;
                return true;
            }
            if (!builtOrCached)
                failureReason = BuildTileBuildPendingFailure(agent, startSectorId, goalSectorId, goalX, goalY);
            return builtOrCached;
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
                {
                    if (IsCurrentPathSegmentPendingPortal(agent, out int pendingPortalId))
                    {
                        goalKind = TileGoalKind.Portal;
                        downstreamPortalId = pendingPortalId;
                        return true;
                    }
                    failureReason = $"tile build failed after strict repath sector={startSectorId} goal=({goalX},{goalY}); {BuildTileBuildPendingFailure(agent, startSectorId, goalSectorId, goalX, goalY)}";
                }
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

    private static bool IsCurrentPathSegmentPendingPortal(AgentRuntimeData agent, out int portalId)
    {
        portalId = -1;
        PathHandle handle = agent?.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.PortalIds == null)
            return false;

        int currentIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, Mathf.Max(0, handle.SectorIds.Length - 1));
        if (currentIndex < 0 || currentIndex >= handle.SectorIds.Length - 1)
            return false;
        if (currentIndex >= handle.PortalIds.Length)
            return false;

        portalId = handle.PortalIds[currentIndex];
        return portalId >= 0;
    }

    private static string BuildTileBuildPendingFailure(AgentRuntimeData agent, int startSectorId, int goalSectorId, int goalX, int goalY)
    {
        PathHandle handle = agent?.NavState.PathHandle;
        if (handle == null || handle.SectorIds == null || handle.SectorIds.Length == 0)
            return $"tile pending unavailable: invalid handle={FormatPathHandle(handle)}";

        int sectorPathIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
        FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
            handle,
            sectorPathIndex,
            goalX,
            goalY,
            agent.AgentTypeId,
            out TileGoalKind goalKind,
            out int downstreamPortalId);
        bool cached = FlowTileCache.ContainsKey(key);
        bool pending = PendingFlowTileBuildJobs.Contains(key);
        bool pathMissingPortal = PathReferencesMissingPortal(handle);
        return $"tile pending startSector={startSectorId} goalSector={goalSectorId} sectorPathIndex={sectorPathIndex} " +
               $"goalKind={goalKind} downstreamPortal={downstreamPortalId} key={FormatTileKey(key)} cached={cached} pending={pending} " +
               $"pendingJobs={FlowTileBuildQueue.Count} pendingDetails={BuildPendingFlowTileJobDiagnostics()} " +
               $"cacheCount={FlowTileCache.Count} pathMissingPortal={pathMissingPortal} handle={FormatPathHandle(handle)}";
    }

    private static string BuildPendingFlowTileJobDiagnostics()
    {
        if (FlowTileBuildQueue.Count == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append('[');
        int count = 0;
        for (LinkedListNode<FlowTileBuildJob> node = FlowTileBuildQueue.First; node != null && count < 8; node = node.Next, count++)
        {
            if (count > 0)
                builder.Append(" | ");

            FlowTileBuildJob job = node.Value;
            builder.Append("{key=").Append(FormatTileKey(job.BuildKey.CacheKey))
                .Append(",stage=").Append(job.Stage)
                .Append(",waiting=").Append(job.WaitingForDependency)
                .Append(",sectorPathIndex=").Append(job.BuildKey.SectorPathIndex)
                .Append('}');
        }

        if (FlowTileBuildQueue.Count > count)
            builder.Append(" | ...");
        builder.Append(']');
        return builder.ToString();
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

        FlowTileBuildKey buildKey = new FlowTileBuildKey(key, sectorPathIndex, goalX, goalY, agentTypeId);
        if (!PendingFlowTileBuildJobs.Add(key))
            return;

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
            CurrentSectorIndex = handle.CurrentSectorIndex,
            BuildSource = handle.BuildSource
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
            float downstreamCost;
            if (downstreamIsFinalSector)
            {
                if (!TryGetPortalSeamIntegrationCost(downstreamTile, portal.PortalId, downstreamCell.x, downstreamCell.y, out downstreamCost))
                {
                    throw new InvalidOperationException(
                        $"BuildIntegrationSeedsForTile failed: downstream seam summary missing portal={portal.PortalId} downstreamCell={downstreamCell} " +
                        $"downstreamKey={FormatTileKey(downstreamTile.Key)} upstreamKey={FormatTileKey(key)}.");
                }
            }
            else
            {
                downstreamCost = ResolvePortalAccessCost(downstreamSector, downstreamSectorId, nextPortalId, downstreamCell.x, downstreamCell.y);
            }
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
        float[] integration = RequireTileIntegration(tile, nameof(ResolveFlowDirectionFromIntegration));
        float currentCost = integration[localIndex];
        return ResolveLowestNeighborFlowDirection(tile, integration, worldX, worldY, currentCost);
    }

    private static Vector2 ResolveLowestNeighborFlowDirection(FlowTileCacheEntry tile, float[] integration, int worldX, int worldY, float currentCost)
    {
        float bestCost = currentCost;
        Vector2 bestDirection = Vector2.zero;
        bool currentReachable = !float.IsPositiveInfinity(currentCost);
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextWorldX = worldX + NeighborOffsetX[i];
            int nextWorldY = worldY + NeighborOffsetY[i];
            if (!IsInsideSector(tile, nextWorldX, nextWorldY) || !_world.IsWalkable(nextWorldX, nextWorldY))
                continue;

            if (!CanTraverseNeighborCells(_world, worldX, worldY, nextWorldX, nextWorldY))
                continue;

            float candidate = integration[tile.GetLocalIndex(nextWorldX, nextWorldY)];
            if (float.IsPositiveInfinity(candidate))
                continue;
            if (currentReachable && candidate >= bestCost)
                continue;
            if (!currentReachable && candidate >= bestCost)
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
        MinHeap openSet,
        FlowTileCacheEntry downstreamTile)
    {
        if (tile.Key.GoalKind == TileGoalKind.FinalGoal)
        {
            if (seeds == null || seeds.Length == 0)
                throw new InvalidOperationException("SeedFinalGoalLineOfSightPass failed: final goal seeds are missing.");

            for (int i = 0; i < seeds.Length; i++)
                TrySeedLineOfSightCell(tile, seeds[i].Cell, seeds[i].Cost, openSet, requireClearCost: false);
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
            if (IsFlowWaveFrontBlocked(downstreamTile, downstreamIndex))
            {
                Vector2Int currentCell = currentCells[i];
                if (IsInsideSector(tile, currentCell.x, currentCell.y))
                {
                    float carriedSeedCost = ResolveIntegrationSeedCost(seeds, currentCell);
                    MarkCarriedWaveFrontBlockedLine(tile, goalX, goalY, currentCell.x, currentCell.y, carriedSeedCost);
                }
            }

            if (!HasFlowLineOfSight(downstreamTile, downstreamIndex))
                continue;

            float seedCost = ResolveIntegrationSeedCost(seeds, currentCells[i]);
            TrySeedLineOfSightCell(tile, currentCells[i], seedCost, openSet, requireClearCost: false);
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

    private static void TrySeedLineOfSightCell(FlowTileCacheEntry tile, Vector2Int cell, float cost, MinHeap openSet, bool requireClearCost = true)
    {
        if (float.IsNaN(cost) || float.IsPositiveInfinity(cost))
            throw new InvalidOperationException($"TrySeedLineOfSightCell failed: invalid seed cost={cost} key={FormatTileKey(tile?.Key ?? default)} cell=({cell.x},{cell.y}).");
        if (!IsInsideSector(tile, cell.x, cell.y) || !_world.IsWalkable(cell.x, cell.y))
            return;
        if (requireClearCost && !IsClearLosCost(_world, cell.x, cell.y))
            return;

        int localIndex = tile.GetLocalIndex(cell.x, cell.y);
        if (!IsSignificantIntegrationImprovement(cost, tile.Integration[localIndex]))
            return;

        SetFlowLineOfSight(tile, localIndex);
        tile.Integration[localIndex] = cost;
        openSet.Push(localIndex, cost);
    }

    private static bool IsClearLosCost(NavigationWorld world, int worldX, int worldY)
    {
        return GetCostFieldValueStrict(world, worldX, worldY) <= 1;
    }

    private static bool HasStrictLineOfSightToAnyTileGoal(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        if (_world == null)
            throw new InvalidOperationException("HasStrictLineOfSightToAnyTileGoal failed: world is null.");
        if (tile?.GoalCells == null || tile.GoalCells.Length == 0)
            throw new InvalidOperationException($"HasStrictLineOfSightToAnyTileGoal failed: missing goal cells key={FormatTileKey(tile?.Key ?? default)}.");

        bool allowTargetSoftCost = tile.Key.GoalKind == TileGoalKind.FinalGoal;
        for (int i = 0; i < tile.GoalCells.Length; i++)
        {
            Vector2Int goalCell = tile.GoalCells[i];
            if (goalCell.x == worldX && goalCell.y == worldY)
                return true;
            if (HasGridLineOfSight(_world, worldX, worldY, goalCell.x, goalCell.y, allowTargetSoftCost))
                return true;
        }

        return false;
    }

    private static void MarkWaveFrontBlockedFromLosCorner(FlowTileCacheEntry tile, int goalX, int goalY, int visibleX, int visibleY, int blockedX, int blockedY)
    {
        if (!IsLineOfSightCorner(tile, visibleX, visibleY, blockedX, blockedY))
            return;

        if (!TryResolveLosBlockedRay(goalX, goalY, visibleX, visibleY, blockedX, blockedY, out Vector2 origin, out Vector2 direction))
            return;

        int visibleIndex = tile.GetLocalIndex(visibleX, visibleY);
        float visibleCost = tile.Integration[visibleIndex];
        if (float.IsPositiveInfinity(visibleCost))
            return;

        MarkWaveFrontBlockedRay(tile, origin, direction, blockedX, blockedY, visibleX, visibleY, visibleCost);
    }

    private static void MarkCarriedWaveFrontBlockedLine(FlowTileCacheEntry tile, int goalX, int goalY, int portalX, int portalY, float startCost)
    {
        if (!TryResolveCarriedLosBlockedRay(goalX, goalY, portalX, portalY, out Vector2 origin, out Vector2 direction))
            return;

        MarkWaveFrontBlockedRay(tile, origin, direction, portalX, portalY, portalX, portalY, startCost);
    }

    private static bool TryResolveLosBlockedRay(
        int goalX,
        int goalY,
        int visibleX,
        int visibleY,
        int blockedX,
        int blockedY,
        out Vector2 origin,
        out Vector2 direction)
    {
        Vector2 goalCenter = new Vector2(goalX + 0.5f, goalY + 0.5f);
        Vector2 blockedCenter = new Vector2(blockedX + 0.5f, blockedY + 0.5f);
        direction = blockedCenter - goalCenter;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = new Vector2(blockedX - visibleX, blockedY - visibleY);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            origin = blockedCenter;
            return false;
        }

        origin = ResolveCellOuterEdgePoint(blockedX, blockedY, direction);
        direction = (origin - goalCenter);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = blockedCenter - goalCenter;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return true;
    }

    private static Vector2 ResolveCellOuterEdgePoint(int cellX, int cellY, Vector2 awayFromGoal)
    {
        const float epsilon = 0.0001f;
        float x = cellX + 0.5f;
        float y = cellY + 0.5f;
        if (awayFromGoal.x > epsilon)
            x = cellX + 1f;
        else if (awayFromGoal.x < -epsilon)
            x = cellX;

        if (awayFromGoal.y > epsilon)
            y = cellY + 1f;
        else if (awayFromGoal.y < -epsilon)
            y = cellY;

        return new Vector2(x, y);
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
        bool hardBlockedCell = !IsInsideSector(tile, blockedX, blockedY) || !_world.IsWalkable(blockedX, blockedY);

        bool blockedSideA = hardBlockedCell
            ? IsLosHardBlockedForCorner(tile, blockedX + perpendicularAX, blockedY + perpendicularAY)
            : IsLosBlockedForCorner(tile, blockedX + perpendicularAX, blockedY + perpendicularAY);
        bool blockedSideB = hardBlockedCell
            ? IsLosHardBlockedForCorner(tile, blockedX + perpendicularBX, blockedY + perpendicularBY)
            : IsLosBlockedForCorner(tile, blockedX + perpendicularBX, blockedY + perpendicularBY);
        if (blockedSideA != blockedSideB)
            return true;

        bool visibleSideA = hardBlockedCell
            ? IsLosHardBlockedForCorner(tile, visibleX + perpendicularAX, visibleY + perpendicularAY)
            : IsLosBlockedForCorner(tile, visibleX + perpendicularAX, visibleY + perpendicularAY);
        bool visibleSideB = hardBlockedCell
            ? IsLosHardBlockedForCorner(tile, visibleX + perpendicularBX, visibleY + perpendicularBY)
            : IsLosBlockedForCorner(tile, visibleX + perpendicularBX, visibleY + perpendicularBY);
        return visibleSideA != visibleSideB;
    }

    private static bool IsLosHardBlockedForCorner(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return !IsInsideSector(tile, worldX, worldY)
               || !_world.IsWalkable(worldX, worldY);
    }

    private static bool IsLosBlockedForCorner(FlowTileCacheEntry tile, int worldX, int worldY)
    {
        return !IsInsideSector(tile, worldX, worldY)
               || !_world.IsWalkable(worldX, worldY)
               || !IsClearLosCost(_world, worldX, worldY);
    }

    private static bool TryResolveCarriedLosBlockedRay(
        int goalX,
        int goalY,
        int startX,
        int startY,
        out Vector2 origin,
        out Vector2 direction)
    {
        Vector2 goalCenter = new Vector2(goalX + 0.5f, goalY + 0.5f);
        Vector2 startCenter = new Vector2(startX + 0.5f, startY + 0.5f);
        direction = startCenter - goalCenter;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            origin = startCenter;
            return false;
        }

        origin = ResolveCellOuterEdgePoint(startX, startY, direction);
        direction = origin - goalCenter;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = startCenter - goalCenter;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return true;
    }

    private static void MarkWaveFrontBlockedRay(
        FlowTileCacheEntry tile,
        Vector2 origin,
        Vector2 direction,
        int blockedX,
        int blockedY,
        int visibleX,
        int visibleY,
        float visibleCost)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        int x = Mathf.Clamp(blockedX, tile.StartX, tile.StartX + tile.Width - 1);
        int y = Mathf.Clamp(blockedY, tile.StartY, tile.StartY + tile.Height - 1);
        int stepX = direction.x > 0f ? 1 : direction.x < 0f ? -1 : 0;
        int stepY = direction.y > 0f ? 1 : direction.y < 0f ? -1 : 0;
        float tMaxX = stepX == 0 ? float.PositiveInfinity : ResolveRayGridBoundaryT(origin.x, direction.x, stepX > 0 ? x + 1f : x);
        float tMaxY = stepY == 0 ? float.PositiveInfinity : ResolveRayGridBoundaryT(origin.y, direction.y, stepY > 0 ? y + 1f : y);
        float tDeltaX = stepX == 0 ? float.PositiveInfinity : Mathf.Abs(1f / direction.x);
        float tDeltaY = stepY == 0 ? float.PositiveInfinity : Mathf.Abs(1f / direction.y);
        int guard = tile.Width * tile.Height * 2 + 8;
        bool seededBoundary = false;

        while (guard-- > 0 && IsInsideSector(tile, x, y))
        {
            int localIndex = tile.GetLocalIndex(x, y);
            SetFlowWaveFrontBlocked(tile, localIndex);
            bool sameAsVisible = x == visibleX && y == visibleY;
            bool canSeedBoundary = !seededBoundary
                                   && _world.IsWalkable(x, y)
                                   && (sameAsVisible || CanTraverseNeighborCells(_world, x, y, visibleX, visibleY));
            if (canSeedBoundary)
            {
                float cost = sameAsVisible
                    ? visibleCost
                    : visibleCost + ResolveCellIntegrationCost(_world, x, y);
                if (cost < tile.Integration[localIndex])
                    tile.Integration[localIndex] = cost;
                if (!sameAsVisible)
                    SetFlowDirectionIndex(tile, localIndex, EncodeFlowDirectionIndex(new Vector2(visibleX - x, visibleY - y)));
                seededBoundary = true;
            }

            if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY < tMaxX)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                x += stepX;
                y += stepY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
        }

        if (guard <= 0)
            throw new InvalidOperationException($"MarkWaveFrontBlockedRay failed: ray iteration exceeded guard sector={tile.Key.SectorId} origin={origin} direction={direction}.");
    }

    private static float ResolveRayGridBoundaryT(float origin, float direction, float boundary)
    {
        if (Mathf.Abs(direction) <= 0.0001f)
            return float.PositiveInfinity;

        float t = (boundary - origin) / direction;
        return t < 0f ? 0f : t;
    }

    private static float HorizontalDistanceCells(int fromX, int fromY, int toX, int toY)
    {
        int dx = Mathf.Abs(toX - fromX);
        int dy = Mathf.Abs(toY - fromY);
        int diagonal = Mathf.Min(dx, dy);
        int cardinal = Mathf.Max(dx, dy) - diagonal;
        return diagonal * 1.4142135f + cardinal;
    }

    private static void TrimTileCache()
    {
        if (FlowTileCache.Count <= Config.FlowTileCacheLimit)
            return;

        RefreshFlowTileReferenceCounts();
        int threshold = GetFrameCount() - 120;
        List<FlowTileCacheKey> expiredKeys = new List<FlowTileCacheKey>();
        foreach (KeyValuePair<FlowTileCacheKey, FlowTileCacheEntry> pair in FlowTileCache)
        {
            if (pair.Value.ActiveReferenceCount > 0)
                continue;
            if (pair.Value.LastUsedFrame < threshold && pair.Value.LastReferencedFrame < threshold)
                expiredKeys.Add(pair.Key);
        }

        for (int i = 0; i < expiredKeys.Count; i++)
            RemoveFlowTileCacheEntry(expiredKeys[i]);

        if (FlowTileCache.Count <= Config.FlowTileCacheLimit)
            return;

        List<FlowTileCacheKey> keys = new List<FlowTileCacheKey>(FlowTileCache.Keys);
        keys.Sort((a, b) => ResolveFlowTileRetainFrame(FlowTileCache[a]).CompareTo(ResolveFlowTileRetainFrame(FlowTileCache[b])));
        int removeCount = FlowTileCache.Count - Config.FlowTileCacheLimit;
        for (int i = 0; i < keys.Count && removeCount > 0; i++)
        {
            if (FlowTileCache[keys[i]].ActiveReferenceCount > 0)
                continue;

            RemoveFlowTileCacheEntry(keys[i]);
            removeCount--;
        }
    }

    private static void RefreshFlowTileReferenceCounts()
    {
        foreach (FlowTileCacheEntry tile in FlowTileCache.Values)
            tile.ActiveReferenceCount = 0;

        int frame = GetFrameCount();
        foreach (AgentRuntimeData agent in Agents.Values)
        {
            PathHandle handle = agent.NavState.PathHandle;
            if (handle == null
                || handle.WorldVersion != _world.Version
                || handle.SectorIds == null
                || handle.SectorIds.Length == 0
                || PathReferencesMissingPortal(handle))
            {
                continue;
            }

            int startIndex = Mathf.Clamp(handle.CurrentSectorIndex, 0, handle.SectorIds.Length - 1);
            for (int i = startIndex; i < handle.SectorIds.Length; i++)
            {
                FlowTileCacheKey key = CreateTileCacheKeyForPathSegment(
                    handle,
                    i,
                    handle.GoalX,
                    handle.GoalY,
                    agent.AgentTypeId,
                    out _,
                    out _);
                if (!FlowTileCache.TryGetValue(key, out FlowTileCacheEntry tile))
                    continue;

                tile.ActiveReferenceCount++;
                tile.LastReferencedFrame = frame;
            }
        }
    }

    private static int ResolveFlowTileRetainFrame(FlowTileCacheEntry tile)
    {
        if (tile == null)
            return int.MinValue;

        return Mathf.Max(tile.LastUsedFrame, tile.LastReferencedFrame);
    }

    private static DesiredDirectionResolution ResolveDesiredDirection(string characterKey, FlowTileCacheEntry tile, int currentX, int currentY, Vector3 goalPosition, TileGoalKind goalKind)
    {
        int localIndex = tile.GetLocalIndex(currentX, currentY);
        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);
        bool logPortalDiagnostics = ShouldLogPortalDiagnostics(characterKey, goalKind);
        PortalTargetResolution portalTarget = goalKind == TileGoalKind.FinalGoal || goalKind == TileGoalKind.Portal
            ? ResolveTileTargetPosition(characterKey, tile, currentX, currentY, goalPosition, goalKind)
            : new PortalTargetResolution(Vector3.zero, Vector3.zero, 0, -1, false, string.Empty);
        Vector3 tileTargetPosition = portalTarget.TargetPosition;
        Vector3 lineOfSightTargetPosition = goalKind == TileGoalKind.Portal ? portalTarget.LineOfSightTargetPosition : goalPosition;
        Vector3 toLineOfSightTarget = lineOfSightTargetPosition - currentCenter;
        toLineOfSightTarget.y = 0f;
        float integration = GetTileIntegrationCostForDiagnostics(tile, currentX, currentY);
        bool hasLineOfSight = IsRuntimeLineOfSightCell(tile, localIndex)
                              && _world.WorldToGrid(lineOfSightTargetPosition, out int lineOfSightTargetX, out int lineOfSightTargetY)
                              && HasGridLineOfSight(
                                  _world,
                                  currentX,
                                  currentY,
                                  lineOfSightTargetX,
                                  lineOfSightTargetY,
                                  allowTargetSoftCost: goalKind == TileGoalKind.FinalGoal);
        Vector2 flow = ResolveRuntimeFlowDirection(tile, currentX, currentY, localIndex);
        Vector3 flowDirection = new Vector3(flow.x, 0f, flow.y);
        Vector3 lineOfSightDirection = toLineOfSightTarget.sqrMagnitude > 0.0001f ? toLineOfSightTarget.normalized : Vector3.zero;
        DesiredDirectionResolution resolution;

        if (hasLineOfSight && toLineOfSightTarget.sqrMagnitude > 0.0001f)
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

    private static DesiredDirectionResolution ApplyPathDirectionBlend(AgentRuntimeData agent, DesiredDirectionResolution resolution, int currentX, int currentY, int contextHash)
    {
        if (agent == null)
            throw new InvalidOperationException("ApplyPathDirectionBlend failed: agent is null.");

        AgentNavState navState = agent.NavState;
        if ((resolution.Source != DesiredDirectionSource.FlowField && resolution.Source != DesiredDirectionSource.PendingPortal)
            || resolution.Direction.sqrMagnitude <= 0.0001f
            || Config.PathDirectionBlend >= 0.999f)
        {
            navState.PathDirection = resolution.Direction;
            navState.PathDirectionCell = new Vector2Int(currentX, currentY);
            navState.PathDirectionContextHash = contextHash;
            return resolution;
        }

        Vector2Int cell = new Vector2Int(currentX, currentY);
        Vector3 rawDirection = resolution.Direction.normalized;
        if (navState.PathDirectionContextHash != contextHash
            || navState.PathDirection.sqrMagnitude <= 0.0001f
            || Vector3.Dot(navState.PathDirection, rawDirection) < -0.05f)
        {
            navState.PathDirection = rawDirection;
            navState.PathDirectionCell = cell;
            navState.PathDirectionContextHash = contextHash;
            return resolution;
        }

        if (navState.PathDirectionCell == cell)
        {
            return new DesiredDirectionResolution(
                navState.PathDirection.normalized,
                resolution.Source,
                resolution.TileTargetPosition,
                resolution.Flow,
                resolution.HasLineOfSight,
                resolution.Integration);
        }

        float newDirectionWeight = Mathf.Clamp01(Config.PathDirectionBlend);
        Vector3 blendedDirection = Vector3.Lerp(navState.PathDirection.normalized, rawDirection, newDirectionWeight);
        if (blendedDirection.sqrMagnitude <= 0.0001f)
            blendedDirection = rawDirection;

        blendedDirection.Normalize();
        navState.PathDirection = blendedDirection;
        navState.PathDirectionCell = cell;
        navState.PathDirectionContextHash = contextHash;
        return new DesiredDirectionResolution(
            blendedDirection,
            resolution.Source,
            resolution.TileTargetPosition,
            resolution.Flow,
            resolution.HasLineOfSight,
            resolution.Integration);
    }

    private static int ComputePathDirectionContextHash(
        AgentRuntimeData agent,
        FlowTileCacheEntry tile,
        int goalX,
        int goalY,
        TileGoalKind goalKind,
        int downstreamPortalId)
    {
        if (agent == null)
            throw new InvalidOperationException("ComputePathDirectionContextHash failed: agent is null.");

        unchecked
        {
            int hash = 17;
            hash = hash * 397 ^ (agent.NavState.PathHandle?.HandleId ?? 0);
            hash = hash * 397 ^ goalX;
            hash = hash * 397 ^ goalY;
            return hash;
        }
    }

    private static PortalTargetResolution ResolveTileTargetPosition(string characterKey, FlowTileCacheEntry tile, int currentX, int currentY, Vector3 goalPosition, TileGoalKind goalKind)
    {
        if (goalKind == TileGoalKind.FinalGoal)
            return new PortalTargetResolution(goalPosition, goalPosition, 0, -1, false, string.Empty);

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
        Vector3 lineOfSightTarget = ResolvePortalLineOfSightTarget(cached, currentSideCells);
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
                $"selection={(cached.VisibleCandidateCount > 0 ? "VisibleLane" : "IntegrationLane")} selectedPair={cached.SelectedPairIndex} visibleCount={cached.VisibleCandidateCount} " +
                $"target={cached.TargetPosition} candidates={candidateSummary}");
            }

            return new PortalTargetResolution(cached.TargetPosition, lineOfSightTarget, cached.VisibleCandidateCount, cached.SelectedPairIndex, false, candidateSummary);
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

        return new PortalTargetResolution(cached.TargetPosition, lineOfSightTarget, cached.VisibleCandidateCount, cached.SelectedPairIndex, true, candidateSummary);
    }

    private static Vector3 ResolvePortalLineOfSightTarget(CachedPortalTarget cached, Vector2Int[] currentSideCells)
    {
        if (currentSideCells == null || currentSideCells.Length == 0)
            throw new InvalidOperationException("ResolvePortalLineOfSightTarget failed: currentSideCells are empty.");
        if (cached.SelectedPairIndex >= 0 && cached.SelectedPairIndex < currentSideCells.Length)
        {
            Vector2Int selectedCell = currentSideCells[cached.SelectedPairIndex];
            return _world.GridToWorldCenter(selectedCell.x, selectedCell.y);
        }

        return ResolveCellGroupCenter(currentSideCells);
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

        bool hasExactFinalGoal = TryDecodeExactFinalGoalIndex(tile.Key.FinalGoalIndex, out int finalGoalX, out int finalGoalY);
        Vector3 currentCenter = _world.GridToWorldCenter(currentX, currentY);
        float[] integration = RequireTileIntegration(tile, nameof(ResolveCachedPortalTarget));
        float bestGoalCost = float.PositiveInfinity;
        float bestLaneDistanceSq = float.MaxValue;
        float bestDistanceSq = float.MaxValue;
        Vector3 bestTarget = Vector3.zero;
        int visibleCandidateCount = 0;
        int selectedPairIndex = -1;
        for (int i = 0; i < currentSideCells.Length; i++)
        {
            Vector2Int goalCell = currentSideCells[i];
            if (!HasGridLineOfSight(_world, currentX, currentY, goalCell.x, goalCell.y))
                continue;

            float goalCost = integration[tile.GetLocalIndex(goalCell.x, goalCell.y)];
            if (float.IsPositiveInfinity(goalCost))
                continue;

            Vector2Int oppositeCell = oppositeSideCells[i];
            Vector3 candidate = _world.GridToWorldCenter(oppositeCell.x, oppositeCell.y);
            float distanceSq = (candidate - currentCenter).sqrMagnitude;
            float laneDistanceSq = hasExactFinalGoal
                ? ResolvePortalLaneDistanceSq(portal, oppositeCell, finalGoalX, finalGoalY)
                : 0f;
            visibleCandidateCount++;
            bool betterCost = goalCost < bestGoalCost - 0.001f;
            bool sameCostBetterLane = Mathf.Abs(goalCost - bestGoalCost) <= 0.001f && laneDistanceSq < bestLaneDistanceSq - 0.001f;
            bool sameCostSameLaneCloser = Mathf.Abs(goalCost - bestGoalCost) <= 0.001f
                                          && Mathf.Abs(laneDistanceSq - bestLaneDistanceSq) <= 0.001f
                                          && distanceSq < bestDistanceSq;
            if (!betterCost && !sameCostBetterLane && !sameCostSameLaneCloser)
                continue;

            bestGoalCost = goalCost;
            bestLaneDistanceSq = laneDistanceSq;
            bestDistanceSq = distanceSq;
            bestTarget = candidate;
            selectedPairIndex = i;
        }

        if (selectedPairIndex >= 0)
            return new CachedPortalTarget(bestTarget, visibleCandidateCount, selectedPairIndex, false);

        if (visibleCandidateCount > 0
            && TryResolvePortalPairIndexFromIntegration(tile, currentX, currentY, currentSideCells, integration, out int integrationPairIndex))
        {
            Vector2Int oppositeCell = oppositeSideCells[integrationPairIndex];
            return new CachedPortalTarget(_world.GridToWorldCenter(oppositeCell.x, oppositeCell.y), 0, integrationPairIndex, false);
        }

        Vector3 oppositePortalCenter = ResolveCellGroupCenter(oppositeSideCells);
        if (visibleCandidateCount == 0)
            return new CachedPortalTarget(oppositePortalCenter, 0, -1, true);

        return new CachedPortalTarget(oppositePortalCenter, 0, -1, true);
    }

    private static bool TryResolvePortalPairIndexFromIntegration(
        FlowTileCacheEntry tile,
        int currentX,
        int currentY,
        Vector2Int[] currentSideCells,
        float[] integration,
        out int selectedPairIndex)
    {
        selectedPairIndex = -1;
        if (tile == null || currentSideCells == null || currentSideCells.Length == 0 || integration == null)
            return false;
        if (!IsInsideSector(tile, currentX, currentY))
            return false;

        int x = currentX;
        int y = currentY;
        int guard = tile.Width * tile.Height + 1;
        while (guard-- > 0)
        {
            int pairIndex = IndexOfPortalCell(currentSideCells, x, y);
            if (pairIndex >= 0)
            {
                selectedPairIndex = pairIndex;
                return true;
            }

            int localIndex = tile.GetLocalIndex(x, y);
            float currentCost = integration[localIndex];
            if (float.IsPositiveInfinity(currentCost))
                return false;

            float bestCost = currentCost;
            float bestPortalDistanceSq = float.MaxValue;
            int bestX = x;
            int bestY = y;
            bool foundStep = false;
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = x + NeighborOffsetX[i];
                int nextY = y + NeighborOffsetY[i];
                if (!IsInsideSector(tile, nextX, nextY) || !_world.IsWalkable(nextX, nextY))
                    continue;
                if (!CanTraverseNeighborCells(_world, x, y, nextX, nextY))
                    continue;

                float candidateCost = integration[tile.GetLocalIndex(nextX, nextY)];
                if (float.IsPositiveInfinity(candidateCost) || candidateCost > bestCost - 0.001f)
                    continue;

                float portalDistanceSq = ResolveNearestPortalCellDistanceSq(currentSideCells, nextX, nextY);
                bool betterCost = candidateCost < bestCost - 0.001f;
                bool sameCostCloserToPortal = Mathf.Abs(candidateCost - bestCost) <= 0.001f
                                              && portalDistanceSq < bestPortalDistanceSq - 0.001f;
                if (!betterCost && !sameCostCloserToPortal)
                    continue;

                bestCost = candidateCost;
                bestPortalDistanceSq = portalDistanceSq;
                bestX = nextX;
                bestY = nextY;
                foundStep = true;
            }

            if (!foundStep)
                return false;

            x = bestX;
            y = bestY;
        }

        return false;
    }

    private static int IndexOfPortalCell(Vector2Int[] cells, int worldX, int worldY)
    {
        if (cells == null)
            return -1;

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].x == worldX && cells[i].y == worldY)
                return i;
        }

        return -1;
    }

    private static float ResolveNearestPortalCellDistanceSq(Vector2Int[] cells, int worldX, int worldY)
    {
        if (cells == null || cells.Length == 0)
            return float.PositiveInfinity;

        float best = float.PositiveInfinity;
        for (int i = 0; i < cells.Length; i++)
        {
            float distanceSq = SquaredCellDistance(worldX, worldY, cells[i].x, cells[i].y);
            if (distanceSq < best)
                best = distanceSq;
        }

        return best;
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
            float goalCost = GetTileIntegrationCostForDiagnostics(tile, goalCell.x, goalCell.y);
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
        TryGetTileIntegrationCostForDiagnostics(tile, currentX, currentY, out float currentCost, out string currentCostSource);
        Vector2 currentFlow = ResolveRuntimeFlowDirection(tile, currentX, currentY, localIndex);
        Vector2 storedFlow = GetFlowDirection(tile, localIndex);
        bool currentLos = HasFlowLineOfSight(tile, localIndex);
        bool currentWaveBlocked = IsFlowWaveFrontBlocked(tile, localIndex);
        bool currentPathable = IsFlowPathable(tile, localIndex);
        bool currentReachable = IsFlowReachable(tile, localIndex);
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
            TryGetTileIntegrationCostForDiagnostics(tile, nextX, nextY, out float nextCost, out string nextCostSource);
            Vector2 nextFlow = ResolveRuntimeFlowDirection(tile, nextX, nextY, nextIndex);
            Vector2 nextStoredFlow = GetFlowDirection(tile, nextIndex);
            if (neighborCount > 0)
                neighborBuilder.Append(" | ");
            neighborBuilder.Append("(").Append(nextX).Append(',').Append(nextY).Append("){cost=")
                .Append(float.IsPositiveInfinity(nextCost) ? "INF" : nextCost.ToString("F3"))
                .Append(",costSource=").Append(nextCostSource)
                .Append(",stored=").Append(nextStoredFlow)
                .Append(",runtime=").Append(nextFlow)
                .Append(",los=").Append(HasFlowLineOfSight(tile, nextIndex))
                .Append(",blocked=").Append(IsFlowWaveFrontBlocked(tile, nextIndex))
                .Append(",pathable=").Append(IsFlowPathable(tile, nextIndex))
                .Append(",reachable=").Append(IsFlowReachable(tile, nextIndex))
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

        return $"tileKey={FormatTileKey(tile.Key)} cell=({currentX},{currentY}) cost={(float.IsPositiveInfinity(currentCost) ? "INF" : currentCost.ToString("F3"))} costSource={currentCostSource} " +
               $"flow={currentFlow} stored={storedFlow} los={currentLos} waveBlocked={currentWaveBlocked} pathable={currentPathable} reachable={currentReachable} " +
               $"{goalCellState} " +
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

    private static string BuildGridPathDiagnostics(Vector3 fromPosition, Vector3 toPosition, int agentTypeId)
    {
        if (_world == null)
            return "flowPath=world-null";

        bool fromInGrid = _world.WorldToGrid(fromPosition, out int fromX, out int fromY);
        bool toInGrid = _world.WorldToGrid(toPosition, out int toX, out int toY);
        bool fromWalkable = fromInGrid && _world.IsWalkable(fromX, fromY);
        bool toWalkable = toInGrid && _world.IsWalkable(toX, toY);
        int fromIsland = fromInGrid ? ResolveIslandIdForDiagnostics(_world, fromX, fromY) : -1;
        int toIsland = toInGrid ? ResolveIslandIdForDiagnostics(_world, toX, toY) : -1;
        float distance = ResolveGridPathLengthOrInfinity(fromPosition, toPosition, agentTypeId);
        return $"flowPath={{fromCell={(fromInGrid ? $"({fromX},{fromY})" : "out")},toCell={(toInGrid ? $"({toX},{toY})" : "out")},fromWalk={fromWalkable},toWalk={toWalkable},fromIsland={fromIsland},toIsland={toIsland},sameIsland={fromIsland > 0 && fromIsland == toIsland},gridDistance={FormatDiagnosticCost(distance)}}}";
    }

    private static void LogFlowTileInvariantDiagnostic(
        IEntityContext self,
        AgentRuntimeData agent,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int startSectorId,
        int goalSectorId,
        TileGoalKind goalKind,
        int downstreamPortalId,
        FlowTileCacheEntry tile,
        DesiredDirectionResolution desiredResolution,
        Vector3 desiredDirection,
        Vector3 stableGoalPosition,
        int agentTypeId)
    {
        if (self == null || agent == null || _world == null || tile == null)
            return;
        if (!IsInsideSector(tile, startX, startY))
            return;

        int localIndex = tile.GetLocalIndex(startX, startY);
        bool reachable = IsFlowReachable(tile, localIndex);
        bool pathable = IsFlowPathable(tile, localIndex);
        bool los = HasFlowLineOfSight(tile, localIndex);
        bool isGoalCell = IsTileGoalCell(tile, startX, startY);
        Vector2 runtimeFlow = ResolveRuntimeFlowDirection(tile, startX, startY, localIndex);
        Vector2 storedFlow = GetFlowDirection(tile, localIndex);
        TryGetTileIntegrationCostForDiagnostics(tile, startX, startY, out float diagnosticCost, out string diagnosticCostSource);

        bool debugMove = GameDebugSettings.IsEnabled(DebugCategory.Move);
        bool missingReachableCost = debugMove
                                    && reachable
                                    && (diagnosticCostSource == "missing" || float.IsPositiveInfinity(diagnosticCost));
        bool zeroReachableFlow = reachable
                                 && pathable
                                 && !los
                                 && !isGoalCell
                                 && !tile.UsesClearFlowDescriptor
                                 && runtimeFlow.sqrMagnitude <= 0.0001f;
        bool flowNextBlocked = false;
        int nextX = startX;
        int nextY = startY;
        if (runtimeFlow.sqrMagnitude > 0.0001f)
        {
            nextX += runtimeFlow.x > 0.35f ? 1 : runtimeFlow.x < -0.35f ? -1 : 0;
            nextY += runtimeFlow.y > 0.35f ? 1 : runtimeFlow.y < -0.35f ? -1 : 0;
            flowNextBlocked = nextX == startX && nextY == startY
                              || !_world.IsWalkable(nextX, nextY)
                              || !CanTraverseNeighborCells(_world, startX, startY, nextX, nextY);
        }

        bool zeroSource = desiredResolution.Source == DesiredDirectionSource.Zero
                          && !(goalKind == TileGoalKind.FinalGoal && startX == goalX && startY == goalY);
        if (!missingReachableCost && !zeroReachableFlow && !flowNextBlocked && !zeroSource)
            return;

        int frame = GetFrameCount();
        if (agent.NavState.LastFlowTileInvariantDiagnosticFrame >= 0
            && frame - agent.NavState.LastFlowTileInvariantDiagnosticFrame < FlowHeavyDiagnosticCooldownFrames)
        {
            return;
        }

        agent.NavState.LastFlowTileInvariantDiagnosticFrame = frame;
        string trigger = $"missingReachableCost={missingReachableCost},zeroReachableFlow={zeroReachableFlow},flowNextBlocked={flowNextBlocked},zeroSource={zeroSource}";
        Debug.LogWarning(
            $"[FlowTileInvariantDiag] frame={frame} trigger={trigger} key={self.CharacterKey} id={agent.Id} pos={self.Position} " +
            $"start=({startX},{startY}) next=({nextX},{nextY}) goal=({goalX},{goalY}) sector={startSectorId}->{goalSectorId} " +
            $"goalKind={goalKind} portal={downstreamPortalId} handle={FormatPathHandle(agent.NavState.PathHandle)} " +
            $"desiredSrc={desiredResolution.Source} desiredDir={desiredDirection} flow={runtimeFlow} stored={storedFlow} " +
            $"cost={(float.IsPositiveInfinity(diagnosticCost) ? "INF" : diagnosticCost.ToString("F3"))} costSource={diagnosticCostSource} " +
            $"reachable={reachable} pathable={pathable} los={los} waveBlocked={IsFlowWaveFrontBlocked(tile, localIndex)} isGoalCell={isGoalCell} " +
            $"tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
            $"flowCostDiag={BuildFlowDirectionCostDiagnostics(tile, startX, startY)} " +
            $"portalTargetDiag={BuildPortalTargetSelectionDiagnostics(tile, startX, startY, goalX, goalY)} " +
            $"flowProbe={BuildFlowDirectionProbeDiagnostics(self.Position, startX, startY, desiredDirection, runtimeFlow, agentTypeId)} " +
            $"{BuildStartPortalRankDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY, agentTypeId, self.Position, stableGoalPosition, agent.NavState.PathHandle)}");
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
        if (!GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return;

        int frame = GetFrameCount();
        if (agent.NavState.LastFlowExecutionMismatchDiagnosticFrame >= 0
            && frame - agent.NavState.LastFlowExecutionMismatchDiagnosticFrame < FlowHeavyDiagnosticCooldownFrames)
        {
            return;
        }

        if (!_world.WorldToGrid(self.Position, out int fromX, out int fromY))
            return;

        bool tileTargetInGrid = _world.WorldToGrid(desiredResolution.TileTargetPosition, out int tileTargetX, out int tileTargetY);
        bool goalInGrid = _world.WorldToGrid(stableGoalPosition, out int stableGoalX, out int stableGoalY);
        bool tileTargetBlocked = tileTargetInGrid && !HasGridLineOfSight(_world, fromX, fromY, tileTargetX, tileTargetY);
        bool goalBlocked = goalInGrid && !HasGridLineOfSight(_world, fromX, fromY, stableGoalX, stableGoalY);
        float tileTargetDistance = HorizontalDistanceXZ(self.Position, desiredResolution.TileTargetPosition);
        float goalDistance = HorizontalDistanceXZ(self.Position, stableGoalPosition);

        Vector3 toStableGoal = stableGoalPosition - self.Position;
        toStableGoal.y = 0f;
        float desiredDotGoal = 0f;
        if (toStableGoal.sqrMagnitude > 0.0001f)
        {
            toStableGoal.Normalize();
            desiredDotGoal = Vector3.Dot(desiredDirection.normalized, toStableGoal);
        }

        bool gridGoalLineBlocked = !HasGridLineOfSight(_world, startX, startY, goalX, goalY);
        bool suspiciousTileTarget = tileTargetBlocked
                                    && tileTargetDistance > Mathf.Max(_world.CellSize * 1.5f, agent.Radius * 3f);
        bool suspiciousLos = desiredResolution.Source == DesiredDirectionSource.LineOfSight
                             && goalBlocked
                             && goalDistance > Mathf.Max(_world.CellSize * 4f, agent.Radius * 6f);
        bool suspiciousDirectGoal = gridGoalLineBlocked
                                    && desiredDotGoal > 0.65f
                                    && goalDistance > Mathf.Max(_world.CellSize * 4f, agent.Radius * 6f);
        if (!suspiciousTileTarget && !suspiciousLos && !suspiciousDirectGoal)
            return;

        agent.NavState.LastFlowExecutionMismatchDiagnosticFrame = frame;
        string trigger = $"tileTarget={suspiciousTileTarget},los={suspiciousLos},directGoal={suspiciousDirectGoal}";
        string goalGridTrace = suspiciousDirectGoal
            ? BuildLineOfSightCellTrace(_world, startX, startY, goalX, goalY)
            : "not-triggered";
        string tileTargetGridTrace = tileTargetInGrid
            ? BuildLineOfSightCellTrace(_world, fromX, fromY, tileTargetX, tileTargetY)
            : "tile-target-out";
        string startPortalChoiceDiagnostics = BuildStartPortalChoiceDiagnostics(
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            agentTypeId,
            self.Position,
            stableGoalPosition,
            agent.NavState.PathHandle);
        string portalRankDiagnostics = BuildStartPortalRankDiagnostics(
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            agentTypeId,
            self.Position,
            stableGoalPosition,
            agent.NavState.PathHandle);
        string losDecisionDiagnostics = BuildFlowLineOfSightDecisionDiagnostics(
            self.Position,
            stableGoalPosition,
            desiredResolution.TileTargetPosition,
            startX,
            startY,
            goalX,
            goalY,
            agentTypeId,
            tile);

        Debug.LogWarning(
            $"[FlowBridgeDiag] forced=True trigger={trigger} frame={frame} key={self.CharacterKey} id={agent.Id} pos={self.Position} " +
            $"rawGoal={rawGoalPosition} occupiedGoal={occupiedGoalPosition} stableGoal={stableGoalPosition} " +
            $"start=({startX},{startY}) goal=({goalX},{goalY}) sector={startSectorId}->{goalSectorId} " +
            $"handle={FormatPathHandle(agent.NavState.PathHandle)} goalKind={goalKind} portal={downstreamPortalId} " +
            $"desiredSrc={desiredResolution.Source} hasLOS={desiredResolution.HasLineOfSight} desiredDir={desiredDirection} desiredVel={desiredVelocity} " +
            $"desiredDotGoal={desiredDotGoal:F3} flow={desiredResolution.Flow} " +
            $"tileTarget={desiredResolution.TileTargetPosition} tileTargetDistance={tileTargetDistance:F3} integration={desiredResolution.Integration:F3} " +
            $"gridGoalBlocked={gridGoalLineBlocked} gridGoalTrace={goalGridTrace} tileTargetBlocked={tileTargetBlocked} tileTargetTrace={tileTargetGridTrace} " +
            $"tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
            $"flowCostDiag={BuildFlowDirectionCostDiagnostics(tile, startX, startY)} " +
            $"portalTargetDiag={BuildPortalTargetSelectionDiagnostics(tile, startX, startY, goalX, goalY)} " +
            $"{losDecisionDiagnostics} " +
            $"{portalRankDiagnostics} " +
            $"gridPathDiag={BuildGridPathDiagnostics(self.Position, stableGoalPosition, agentTypeId)}");

        if (goalKind == TileGoalKind.Portal && tile != null)
        {
            Debug.LogWarning(
                $"[FlowPortalTarget] forced=True trigger={trigger} key={self.CharacterKey} sector={tile.Key.SectorId} goalId={tile.Key.GoalId} " +
                $"currentCell=({startX},{startY}) currentCenter={_world.GridToWorldCenter(startX, startY)} " +
                $"target={desiredResolution.TileTargetPosition} source={desiredResolution.Source} hasLOS={desiredResolution.HasLineOfSight} " +
                $"flow={desiredResolution.Flow} tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
                $"flowCostDiag={BuildFlowDirectionCostDiagnostics(tile, startX, startY)} " +
                $"portalTargetDiag={BuildPortalTargetSelectionDiagnostics(tile, startX, startY, goalX, goalY)}");
        }

        Debug.LogWarning(
            $"[FlowExecutionMismatchDiag] frame={frame} trigger={trigger} key={self.CharacterKey} id={agent.Id} pos={self.Position} " +
            $"rawGoal={rawGoalPosition} occupiedGoal={occupiedGoalPosition} stableGoal={stableGoalPosition} " +
            $"start=({startX},{startY}) goal=({goalX},{goalY}) sector={startSectorId}->{goalSectorId} " +
            $"goalKind={goalKind} portal={downstreamPortalId} handle={FormatPathHandle(agent.NavState.PathHandle)} " +
            $"desiredSrc={desiredResolution.Source} desiredDir={desiredDirection} desiredVel={desiredVelocity} " +
            $"flow={desiredResolution.Flow} los={desiredResolution.HasLineOfSight} integration={desiredResolution.Integration:F3} " +
            $"tileTarget={desiredResolution.TileTargetPosition} tileTargetDistance={tileTargetDistance:F3} " +
            $"tileTargetInGrid={tileTargetInGrid} tileTargetCell={(tileTargetInGrid ? $"({tileTargetX},{tileTargetY})" : "out")} tileTargetBlocked={tileTargetBlocked} tileTargetTrace={tileTargetGridTrace} " +
            $"goalInGrid={goalInGrid} stableGoalCell={(goalInGrid ? $"({stableGoalX},{stableGoalY})" : "out")} goalBlocked={goalBlocked} " +
            $"gridGoalBlocked={gridGoalLineBlocked} desiredDotGoal={desiredDotGoal:F3} " +
            $"tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
            $"flowCostDiag={BuildFlowDirectionCostDiagnostics(tile, startX, startY)} " +
            $"portalTargetDiag={BuildPortalTargetSelectionDiagnostics(tile, startX, startY, goalX, goalY)} " +
            $"{losDecisionDiagnostics} " +
            $"{portalRankDiagnostics} " +
            $"{startPortalChoiceDiagnostics} " +
            $"gridPathDiag={BuildGridPathDiagnostics(self.Position, stableGoalPosition, agentTypeId)}");
        Debug.LogWarning(startPortalChoiceDiagnostics);
        Debug.LogWarning(portalRankDiagnostics);
    }

    private static void LogFlowWallProbeDiagnostic(
        IEntityContext self,
        AgentRuntimeData agent,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int startSectorId,
        int goalSectorId,
        TileGoalKind goalKind,
        int downstreamPortalId,
        FlowTileCacheEntry tile,
        DesiredDirectionResolution desiredResolution,
        Vector3 desiredDirection,
        Vector3 stableGoalPosition)
    {
        if (self == null || agent == null || _world == null)
            return;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TryResolveForwardBlockedProbe(startX, startY, desiredDirection, out string forwardProbe, out int blockedX, out int blockedY))
            return;

        int frame = GetFrameCount();
        if (agent.NavState.LastFlowWallProbeDiagnosticFrame >= 0
            && frame - agent.NavState.LastFlowWallProbeDiagnosticFrame < FlowHeavyDiagnosticCooldownFrames)
        {
            return;
        }

        agent.NavState.LastFlowWallProbeDiagnosticFrame = frame;
        string goalTrace = _world.WorldToGrid(stableGoalPosition, out int stableGoalX, out int stableGoalY)
            ? BuildLineOfSightCellTrace(_world, startX, startY, stableGoalX, stableGoalY)
            : "stableGoal-out";
        string tileTargetTrace = _world.WorldToGrid(desiredResolution.TileTargetPosition, out int tileTargetX, out int tileTargetY)
            ? BuildLineOfSightCellTrace(_world, startX, startY, tileTargetX, tileTargetY)
            : "tileTarget-out";

        Debug.LogWarning(
            $"[FlowWallProbeDiag] frame={frame} key={self.CharacterKey} id={agent.Id} pos={self.Position} " +
            $"start=({startX},{startY}) goal=({goalX},{goalY}) stableGoal={stableGoalPosition} sector={startSectorId}->{goalSectorId} " +
            $"goalKind={goalKind} portal={downstreamPortalId} handle={FormatPathHandle(agent.NavState.PathHandle)} " +
            $"desiredSrc={desiredResolution.Source} desiredDir={desiredDirection} flow={desiredResolution.Flow} hasLOS={desiredResolution.HasLineOfSight} " +
            $"tileTarget={desiredResolution.TileTargetPosition} integration={desiredResolution.Integration:F3} blocked=({blockedX},{blockedY}) " +
            $"forwardProbe={forwardProbe} tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
            $"flowPathTrace={BuildRuntimeFlowPathTrace(tile, startX, startY, maxSteps: 14)} " +
            $"pathCompare={BuildFlowPathComparisonDiagnostics(tile, startSectorId, goalSectorId, startX, startY, goalX, goalY, downstreamPortalId, agent.NavState.PathHandle)} " +
            $"goalTrace={goalTrace} tileTargetTrace={tileTargetTrace} " +
            $"{BuildStartPortalRankDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY, agent.AgentTypeId, self.Position, stableGoalPosition, agent.NavState.PathHandle)}");
    }

    private static string BuildRuntimeFlowPathTrace(FlowTileCacheEntry tile, int startX, int startY, int maxSteps)
    {
        if (_world == null)
            return "{world=null}";
        if (tile == null)
            return "{tile=null}";
        if (!IsInsideSector(tile, startX, startY))
            return "{start=outside}";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(1536);
        builder.Append('[');
        int x = startX;
        int y = startY;
        int previousX = x;
        int previousY = y;
        for (int step = 0; step <= maxSteps; step++)
        {
            if (step > 0)
                builder.Append(" -> ");

            if (!IsInsideSector(tile, x, y))
            {
                builder.Append('(').Append(x).Append(',').Append(y).Append("){outside-sector}");
                break;
            }

            int localIndex = tile.GetLocalIndex(x, y);
            bool walkable = _world.IsWalkable(x, y);
            bool traversable = step == 0 || CanTraverseNeighborCells(_world, previousX, previousY, x, y);
            TryGetTileIntegrationCostForDiagnostics(tile, x, y, out float cost, out string costSource);
            Vector2 flow = ResolveRuntimeFlowDirection(tile, x, y, localIndex);
            Vector2 storedFlow = GetFlowDirection(tile, localIndex);
            builder.Append('(').Append(x).Append(',').Append(y).Append("){cost=")
                .Append(FormatDiagnosticCost(cost))
                .Append(",source=").Append(costSource)
                .Append(",walk=").Append(walkable)
                .Append(",trav=").Append(traversable)
                .Append(",los=").Append(HasFlowLineOfSight(tile, localIndex))
                .Append(",blocked=").Append(IsFlowWaveFrontBlocked(tile, localIndex))
                .Append(",stored=").Append(storedFlow)
                .Append(",flow=").Append(flow)
                .Append('}');

            if (!walkable || !traversable || flow.sqrMagnitude <= 0.0001f)
                break;

            int stepX = flow.x > 0.35f ? 1 : flow.x < -0.35f ? -1 : 0;
            int stepY = flow.y > 0.35f ? 1 : flow.y < -0.35f ? -1 : 0;
            if (stepX == 0 && stepY == 0)
                break;

            previousX = x;
            previousY = y;
            x += stepX;
            y += stepY;
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildFlowPathComparisonDiagnostics(
        FlowTileCacheEntry tile,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int downstreamPortalId,
        PathHandle handle)
    {
        if (_world == null)
            return "{world=null}";

        const int maxTraceSteps = 32;
        string runtimeTrace = BuildRuntimeFlowCellTrace(tile, startX, startY, maxTraceSteps);
        string globalGoalTrace = BuildGridShortestPathTraceToCell(startX, startY, goalX, goalY, maxTraceSteps);
        string selectedPortalTrace = BuildSelectedPortalGridTrace(startSectorId, startX, startY, downstreamPortalId, maxTraceSteps);
        string handleTrace = BuildHandlePortalRouteDiagnostics(handle, startSectorId, startX, startY, goalX, goalY, maxTraceSteps);

        return "{runtime=" + runtimeTrace
               + ",globalGoal=" + globalGoalTrace
               + ",selectedPortal=" + selectedPortalTrace
               + ",handleRoute=" + handleTrace
               + ",sameSector=" + (startSectorId == goalSectorId)
               + "}";
    }

    private static string BuildRuntimeFlowCellTrace(FlowTileCacheEntry tile, int startX, int startY, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (tile == null)
            return "tile-null";
        if (!IsInsideSector(tile, startX, startY))
            return "start-outside-tile";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(maxSteps * 12);
        int x = startX;
        int y = startY;
        int previousX = x;
        int previousY = y;
        for (int step = 0; step <= maxSteps; step++)
        {
            if (step > 0)
                builder.Append("->");

            if (!IsInsideSector(tile, x, y))
            {
                builder.Append('(').Append(x).Append(',').Append(y).Append(":outside)");
                break;
            }

            int localIndex = tile.GetLocalIndex(x, y);
            bool walkable = _world.IsWalkable(x, y);
            bool traversable = step == 0 || CanTraverseNeighborCells(_world, previousX, previousY, x, y);
            Vector2 flow = ResolveRuntimeFlowDirection(tile, x, y, localIndex);
            builder.Append('(').Append(x).Append(',').Append(y).Append(')');

            if (!walkable)
            {
                builder.Append(":blocked");
                break;
            }

            if (!traversable)
            {
                builder.Append(":unlinked");
                break;
            }

            int stepX = flow.x > 0.35f ? 1 : flow.x < -0.35f ? -1 : 0;
            int stepY = flow.y > 0.35f ? 1 : flow.y < -0.35f ? -1 : 0;
            if (stepX == 0 && stepY == 0)
            {
                builder.Append(":zero");
                break;
            }

            previousX = x;
            previousY = y;
            x += stepX;
            y += stepY;
        }

        return builder.ToString();
    }

    private static string BuildSelectedPortalGridTrace(int startSectorId, int startX, int startY, int portalId, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (portalId < 0)
            return "none";
        if (!TryGetPortalById(_world, portalId, out PortalData portal))
            return "portal-missing:" + portalId;

        Vector2Int[] currentCells = GetPortalCellsForSector(portal, startSectorId);
        return "portal=" + FormatPortal(portal)
               + ",cells=" + FormatGoalCells(currentCells)
               + ",path=" + BuildGridShortestPathTraceToCells(startX, startY, currentCells, maxSteps);
    }

    private static string BuildHandlePortalRouteDiagnostics(PathHandle handle, int startSectorId, int startX, int startY, int goalX, int goalY, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (handle == null || handle.SectorIds == null || handle.PortalIds == null)
            return "handle-null";

        int sectorIndex = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (sectorIndex < 0)
            return "start-sector-not-in-handle:" + FormatPathHandle(handle);

        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append(FormatPathHandle(handle));
        int cursorX = -1;
        int cursorY = -1;
        for (int i = sectorIndex; i < handle.PortalIds.Length; i++)
        {
            int sectorId = handle.SectorIds[i];
            int portalId = handle.PortalIds[i];
            if (!TryGetPortalById(_world, portalId, out PortalData portal))
            {
                builder.Append("|missingPortal=").Append(portalId);
                continue;
            }

            Vector2Int[] currentCells = GetPortalCellsForSector(portal, sectorId);
            Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, handle.SectorIds[i + 1]);
            builder.Append("|seg").Append(i)
                .Append("{sector=").Append(sectorId)
                .Append(",portal=").Append(portalId)
                .Append(",cur=").Append(FormatGoalCells(currentCells))
                .Append(",opp=").Append(FormatGoalCells(oppositeCells));
            if (i == sectorIndex)
                builder.Append(",fromStart=").Append(BuildGridShortestPathTraceToCells(startX, startY, currentCells, maxSteps));
            if (oppositeCells != null && oppositeCells.Length > 0)
            {
                Vector2Int opposite = oppositeCells[oppositeCells.Length / 2];
                cursorX = opposite.x;
                cursorY = opposite.y;
            }
            builder.Append('}');
        }

        if (cursorX >= 0)
            builder.Append("|lastOppToGoal=").Append(BuildGridShortestPathTraceToCell(cursorX, cursorY, goalX, goalY, maxSteps));
        return builder.ToString();
    }

    private static string BuildGridShortestPathTraceToCell(int startX, int startY, int goalX, int goalY, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (goalX < 0 || goalX >= _world.Width || goalY < 0 || goalY >= _world.Height || !_world.IsWalkable(goalX, goalY))
            return "goal-invalid";

        bool[] targets = new bool[_world.Width * _world.Height];
        targets[_world.GetIndex(goalX, goalY)] = true;
        return BuildGridShortestPathTraceToTargets(startX, startY, targets, maxSteps);
    }

    private static string BuildGridShortestPathTraceToCells(int startX, int startY, Vector2Int[] targetCells, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        if (targetCells == null || targetCells.Length == 0)
            return "no-targets";

        bool[] targets = new bool[_world.Width * _world.Height];
        bool hasTarget = false;
        for (int i = 0; i < targetCells.Length; i++)
        {
            Vector2Int cell = targetCells[i];
            if (cell.x < 0 || cell.x >= _world.Width || cell.y < 0 || cell.y >= _world.Height || !_world.IsWalkable(cell.x, cell.y))
                continue;

            targets[_world.GetIndex(cell.x, cell.y)] = true;
            hasTarget = true;
        }

        return hasTarget ? BuildGridShortestPathTraceToTargets(startX, startY, targets, maxSteps) : "no-walkable-targets";
    }

    private static string BuildGridShortestPathTraceToTargets(int startX, int startY, bool[] targets, int maxSteps)
    {
        if (_world == null)
            return "world-null";
        int cellCount = _world.Width * _world.Height;
        if (targets == null || targets.Length != cellCount)
            return "invalid-targets";
        if (startX < 0 || startX >= _world.Width || startY < 0 || startY >= _world.Height || !_world.IsWalkable(startX, startY))
            return "start-invalid";

        int startIndex = _world.GetIndex(startX, startY);
        if (targets[startIndex])
            return "(" + startX + "," + startY + "):target";

        float[] costs = RentIntegrationArray(cellCount);
        int[] cameFrom = new int[cellCount];
        for (int i = 0; i < cameFrom.Length; i++)
            cameFrom[i] = -1;

        InitializeIntegrationField(costs);
        MinHeap openSet = new MinHeap();
        costs[startIndex] = 0f;
        openSet.Push(startIndex, 0f);
        int reachedIndex = -1;
        int guard = cellCount * 4;
        try
        {
            while (openSet.Count > 0 && guard-- > 0)
            {
                QueueNode node = openSet.Pop();
                if (node.Cost > costs[node.Index] + 0.001f)
                    continue;
                if (targets[node.Index])
                {
                    reachedIndex = node.Index;
                    break;
                }

                int worldX = node.Index % _world.Width;
                int worldY = node.Index / _world.Width;
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int nextX = worldX + NeighborOffsetX[i];
                    int nextY = worldY + NeighborOffsetY[i];
                    if (nextX < 0 || nextX >= _world.Width || nextY < 0 || nextY >= _world.Height)
                        continue;
                    if (!_world.IsWalkable(nextX, nextY) || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
                        continue;

                    int nextIndex = _world.GetIndex(nextX, nextY);
                    float stepDistance = Mathf.Abs(NeighborOffsetX[i]) + Mathf.Abs(NeighborOffsetY[i]) == 2 ? 1.4142135f : 1f;
                    float stepCost = Mathf.Max(ResolveCellIntegrationCost(_world, nextX, nextY), 0.001f) * stepDistance;
                    float newCost = node.Cost + stepCost;
                    if (!IsSignificantIntegrationImprovement(newCost, costs[nextIndex]))
                        continue;

                    costs[nextIndex] = newCost;
                    cameFrom[nextIndex] = node.Index;
                    openSet.Push(nextIndex, newCost);
                }
            }

            if (reachedIndex < 0)
                return guard <= 0 ? "guard-exceeded" : "unreachable";

            return FormatReconstructedGridPath(cameFrom, startIndex, reachedIndex, maxSteps, costs[reachedIndex]);
        }
        finally
        {
            ReturnIntegrationArray(costs);
        }
    }

    private static string FormatReconstructedGridPath(int[] cameFrom, int startIndex, int reachedIndex, int maxSteps, float totalCost)
    {
        if (_world == null || cameFrom == null)
            return "invalid-reconstruct";

        List<int> reversed = new List<int>(Mathf.Max(8, maxSteps + 1));
        int cursor = reachedIndex;
        int guard = cameFrom.Length + 1;
        while (cursor >= 0 && guard-- > 0)
        {
            reversed.Add(cursor);
            if (cursor == startIndex)
                break;
            cursor = cameFrom[cursor];
        }

        if (reversed[reversed.Count - 1] != startIndex)
            return "reconstruct-failed";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(maxSteps * 12 + 64);
        builder.Append("cost=").Append(FormatDiagnosticCost(totalCost)).Append(",path=");
        int emitted = 0;
        for (int i = reversed.Count - 1; i >= 0 && emitted <= maxSteps; i--, emitted++)
        {
            if (emitted > 0)
                builder.Append("->");
            int index = reversed[i];
            builder.Append('(').Append(index % _world.Width).Append(',').Append(index / _world.Width).Append(')');
        }

        if (emitted < reversed.Count)
            builder.Append("->...");
        return builder.ToString();
    }

    private static bool TryResolveForwardBlockedProbe(int startX, int startY, Vector3 desiredDirection, out string probe, out int blockedX, out int blockedY)
    {
        blockedX = startX;
        blockedY = startY;
        probe = "none";
        if (_world == null)
            return false;

        Vector3 direction = desiredDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;
        direction.Normalize();

        int stepX = direction.x > 0.35f ? 1 : direction.x < -0.35f ? -1 : 0;
        int stepY = direction.z > 0.35f ? 1 : direction.z < -0.35f ? -1 : 0;
        if (stepX == 0 && stepY == 0)
            return false;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        int previousX = startX;
        int previousY = startY;
        bool foundBlocked = false;
        for (int step = 1; step <= 5; step++)
        {
            int x = startX + stepX * step;
            int y = startY + stepY * step;
            if (step > 1)
                builder.Append(" -> ");

            AppendForwardProbeCell(builder, previousX, previousY, x, y);
            bool blocked = x < 0
                           || x >= _world.Width
                           || y < 0
                           || y >= _world.Height
                           || !_world.IsWalkable(x, y)
                           || !CanTraverseNeighborCells(_world, previousX, previousY, x, y);
            if (blocked)
            {
                blockedX = x;
                blockedY = y;
                foundBlocked = true;
                break;
            }

            previousX = x;
            previousY = y;
        }

        probe = builder.ToString();
        return foundBlocked;
    }

    private static void AppendForwardProbeCell(System.Text.StringBuilder builder, int previousX, int previousY, int x, int y)
    {
        builder.Append('(').Append(x).Append(',').Append(y).Append("){");
        if (_world == null || x < 0 || x >= _world.Width || y < 0 || y >= _world.Height)
        {
            builder.Append("out}");
            return;
        }

        int index = _world.GetIndex(x, y);
        bool walkable = _world.IsWalkable(x, y);
        bool baseWalkable = _world.BaseWalkableMask != null
                            && _world.BaseWalkableMask.Length == _world.Width * _world.Height
                            && _world.BaseWalkableMask[index];
        byte cost = 0;
        TryGetCostFieldValue(_world, x, y, out cost);
        int island = _world.IslandIds != null && _world.IslandIds.Length == _world.Width * _world.Height ? _world.IslandIds[index] : -1;
        string mask = _world.NeighborTraversalMask != null && _world.NeighborTraversalMask.Length == _world.Width * _world.Height
            ? _world.NeighborTraversalMask[index].ToString("X2")
            : "null";
        bool link = previousX == x && previousY == y || CanTraverseNeighborCells(_world, previousX, previousY, x, y);
        builder.Append("walk=").Append(walkable)
            .Append(",base=").Append(baseWalkable)
            .Append(",cost=").Append(cost)
            .Append(",island=").Append(island)
            .Append(",mask=0x").Append(mask)
            .Append(",link=").Append(link)
            .Append('}');
    }

    private static string BuildFlowDirectionCostDiagnostics(FlowTileCacheEntry tile, int currentX, int currentY)
    {
        if (_world == null || tile == null)
            return "{worldOrTile=null}";
        if (!IsInsideSector(tile, currentX, currentY))
            return "{cell=outside}";

        bool rebuiltDebug = false;
        if (tile.DebugIntegrationPayload?.Values == null && tile.IntegrationPayload?.Values == null)
            rebuiltDebug = TryRebuildTileDebugIntegration(tile);

        TryGetTileIntegrationCostForDiagnostics(tile, currentX, currentY, out float currentCost, out string currentCostSource);
        int localIndex = tile.GetLocalIndex(currentX, currentY);
        Vector2 runtimeFlow = ResolveRuntimeFlowDirection(tile, currentX, currentY, localIndex);
        int flowStepX = runtimeFlow.x > 0.35f ? 1 : runtimeFlow.x < -0.35f ? -1 : 0;
        int flowStepY = runtimeFlow.y > 0.35f ? 1 : runtimeFlow.y < -0.35f ? -1 : 0;
        int flowNextX = currentX + flowStepX;
        int flowNextY = currentY + flowStepY;
        bool flowNextInside = flowStepX != 0 || flowStepY != 0;
        flowNextInside = flowNextInside && IsInsideSector(tile, flowNextX, flowNextY);
        bool flowNextWalkable = flowNextInside && _world.IsWalkable(flowNextX, flowNextY);
        bool flowNextTraversable = flowNextWalkable && CanTraverseNeighborCells(_world, currentX, currentY, flowNextX, flowNextY);
        float flowNextCost = float.PositiveInfinity;
        string flowNextCostSource = "missing";
        if (flowNextInside)
            TryGetTileIntegrationCostForDiagnostics(tile, flowNextX, flowNextY, out flowNextCost, out flowNextCostSource);

        int bestX = currentX;
        int bestY = currentY;
        float bestCost = currentCost;
        string bestSource = currentCostSource;
        bool foundBetterNeighbor = false;
        System.Text.StringBuilder neighbors = new System.Text.StringBuilder(768);
        neighbors.Append('[');
        int neighborCount = 0;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = currentX + NeighborOffsetX[i];
            int nextY = currentY + NeighborOffsetY[i];
            if (!IsInsideSector(tile, nextX, nextY))
                continue;

            bool walkable = _world.IsWalkable(nextX, nextY);
            bool traversable = walkable && CanTraverseNeighborCells(_world, currentX, currentY, nextX, nextY);
            TryGetTileIntegrationCostForDiagnostics(tile, nextX, nextY, out float nextCost, out string nextSource);
            if (neighborCount > 0)
                neighbors.Append(" | ");
            neighbors.Append('(').Append(nextX).Append(',').Append(nextY).Append("){cost=")
                .Append(FormatDiagnosticCost(nextCost))
                .Append(",source=").Append(nextSource)
                .Append(",walk=").Append(walkable)
                .Append(",trav=").Append(traversable)
                .Append(",flow=").Append(ResolveRuntimeFlowDirection(tile, nextX, nextY, tile.GetLocalIndex(nextX, nextY)))
                .Append('}');
            neighborCount++;

            if (!traversable || float.IsPositiveInfinity(nextCost))
                continue;
            if (!foundBetterNeighbor || nextCost < bestCost - 0.001f)
            {
                foundBetterNeighbor = true;
                bestX = nextX;
                bestY = nextY;
                bestCost = nextCost;
                bestSource = nextSource;
            }
        }

        if (neighborCount == 0)
            neighbors.Append("none");
        neighbors.Append(']');

        bool flowDropsCost = flowNextTraversable
                             && !float.IsPositiveInfinity(currentCost)
                             && flowNextCost < currentCost - 0.001f;
        bool flowMatchesBest = foundBetterNeighbor && flowNextX == bestX && flowNextY == bestY;
        return "{rebuiltDebug=" + rebuiltDebug
               + ",currentCost=" + FormatDiagnosticCost(currentCost)
               + ",currentSource=" + currentCostSource
               + ",runtimeFlow=" + runtimeFlow
               + ",flowNext=(" + flowNextX + "," + flowNextY + ")"
               + ",flowNextInside=" + flowNextInside
               + ",flowNextWalkable=" + flowNextWalkable
               + ",flowNextTraversable=" + flowNextTraversable
               + ",flowNextCost=" + FormatDiagnosticCost(flowNextCost)
               + ",flowNextSource=" + flowNextCostSource
               + ",flowDropsCost=" + flowDropsCost
               + ",best=(" + bestX + "," + bestY + ")"
               + ",bestCost=" + FormatDiagnosticCost(bestCost)
               + ",bestSource=" + bestSource
               + ",flowMatchesBest=" + flowMatchesBest
               + ",neighbors=" + neighbors
               + "}";
    }

    private static string BuildPortalTargetSelectionDiagnostics(FlowTileCacheEntry tile, int currentX, int currentY, int finalGoalX, int finalGoalY)
    {
        if (_world == null || tile == null)
            return "{worldOrTile=null}";
        if (tile.Key.GoalKind != TileGoalKind.Portal)
            return "{goalKind=" + tile.Key.GoalKind + "}";
        if (!IsInsideSector(tile, currentX, currentY))
            return "{cell=outside}";

        if (tile.DebugIntegrationPayload?.Values == null && tile.IntegrationPayload?.Values == null)
            TryRebuildTileDebugIntegration(tile);

        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, tile.Key.SectorId));
        CachedPortalTarget cached = tile.PortalTargets != null && tile.PortalTargets.Length == tile.Width * tile.Height
            ? tile.PortalTargets[tile.GetLocalIndex(currentX, currentY)]
            : default;
        System.Text.StringBuilder builder = new System.Text.StringBuilder(1536);
        builder.Append("{portal=").Append(FormatPortal(portal))
            .Append(",cachedUsedOpposite=").Append(cached.UsedOppositeCenter)
            .Append(",cachedPair=").Append(cached.SelectedPairIndex)
            .Append(",cachedVisible=").Append(cached.VisibleCandidateCount)
            .Append(",candidates=[");

        int count = Mathf.Min(currentSideCells.Length, oppositeSideCells.Length);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(" | ");
            Vector2Int currentCell = currentSideCells[i];
            Vector2Int oppositeCell = oppositeSideCells[i];
            bool currentInside = IsInsideSector(tile, currentCell.x, currentCell.y);
            bool currentWalkable = currentInside && _world.IsWalkable(currentCell.x, currentCell.y);
            bool oppositeWalkable = _world.IsWalkable(oppositeCell.x, oppositeCell.y);
            bool portalTraversable = currentWalkable
                                     && oppositeWalkable
                                     && CanTraverseNeighborCells(_world, currentCell.x, currentCell.y, oppositeCell.x, oppositeCell.y);
            bool startLos = currentWalkable && HasGridLineOfSight(_world, currentX, currentY, currentCell.x, currentCell.y);
            bool oppositeGoalLos = oppositeWalkable && HasGridLineOfSight(_world, oppositeCell.x, oppositeCell.y, finalGoalX, finalGoalY);
            TryGetTileIntegrationCostForDiagnostics(tile, currentCell.x, currentCell.y, out float currentCost, out string costSource);
            float gridFromStart = ResolveGridPathCostOrInfinity(currentX, currentY, currentCell.x, currentCell.y);
            float gridToGoal = ResolveGridPathCostOrInfinity(oppositeCell.x, oppositeCell.y, finalGoalX, finalGoalY);
            builder.Append("i=").Append(i)
                .Append("{cur=(").Append(currentCell.x).Append(',').Append(currentCell.y).Append(')')
                .Append(",opp=(").Append(oppositeCell.x).Append(',').Append(oppositeCell.y).Append(')')
                .Append(",selected=").Append(!cached.UsedOppositeCenter && cached.SelectedPairIndex == i)
                .Append(",curWalk=").Append(currentWalkable)
                .Append(",oppWalk=").Append(oppositeWalkable)
                .Append(",portalTrav=").Append(portalTraversable)
                .Append(",startLos=").Append(startLos)
                .Append(",oppGoalLos=").Append(oppositeGoalLos)
                .Append(",curCost=").Append(FormatDiagnosticCost(currentCost))
                .Append(",costSource=").Append(costSource)
                .Append(",gridStart=").Append(FormatDiagnosticCost(gridFromStart))
                .Append(",gridGoal=").Append(FormatDiagnosticCost(gridToGoal))
                .Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string BuildFlowDirectionProbeDiagnostics(
        Vector3 startPosition,
        int startX,
        int startY,
        Vector3 desiredDirection,
        Vector2 runtimeFlow,
        int agentTypeId)
    {
        if (_world == null)
            return "{world=null}";

        Vector3 probeDirection = desiredDirection;
        probeDirection.y = 0f;
        if (probeDirection.sqrMagnitude <= 0.0001f && runtimeFlow.sqrMagnitude > 0.0001f)
            probeDirection = new Vector3(runtimeFlow.x, 0f, runtimeFlow.y);
        if (probeDirection.sqrMagnitude <= 0.0001f)
            return "{direction=zero}";

        probeDirection.Normalize();
        float probeDistance = Mathf.Max(_world.CellSize * 8f, 4f);
        Vector3 endPosition = startPosition + probeDirection * probeDistance;
        bool endInGrid = _world.WorldToGrid(endPosition, out int endX, out int endY);

        System.Text.StringBuilder builder = new System.Text.StringBuilder(1536);
        builder.Append("{dir=").Append(probeDirection)
            .Append(",end=").Append(endPosition)
            .Append(",endInGrid=").Append(endInGrid)
            .Append(",segment=");
        if (endInGrid)
            builder.Append(BuildLineDecisionSegmentDiagnostics(startPosition, endPosition, startX, startY, endX, endY, agentTypeId));
        else
            builder.Append("end-out");

        builder.Append(",flowSteps=[");
        int stepX = runtimeFlow.x > 0.35f ? 1 : runtimeFlow.x < -0.35f ? -1 : 0;
        int stepY = runtimeFlow.y > 0.35f ? 1 : runtimeFlow.y < -0.35f ? -1 : 0;
        if (stepX == 0 && stepY == 0)
        {
            builder.Append("none");
        }
        else
        {
            int previousX = startX;
            int previousY = startY;
            for (int step = 1; step <= 6; step++)
            {
                if (step > 1)
                    builder.Append(" -> ");
                int x = startX + stepX * step;
                int y = startY + stepY * step;
                AppendLineDecisionCell(builder, _world, x, y, previousX, previousY, first: false);
                previousX = x;
                previousY = y;
                if (x < 0 || x >= _world.Width || y < 0 || y >= _world.Height || !_world.IsWalkable(x, y))
                    break;
            }
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string BuildStartPortalRankDiagnostics(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int agentTypeId,
        Vector3 startPosition,
        Vector3 stableGoalPosition,
        PathHandle handle = null)
    {
        if (_world == null)
            return "[FlowPortalRankDiag world=null]";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return $"[FlowPortalRankDiag invalid-start-sector startSector={startSectorId}]";
        if (!TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, ResolvePreferredAgentTypeId(agentTypeId), out SharedGoalField sharedField))
            return "[FlowPortalRankDiag shared-field-not-cached]";

        SectorData startSector = _world.Sectors[startSectorId];
        int selectedPortalId = ResolveSelectedPortalIdForDiagnostics(handle, startSectorId);
        int bestFlowPortalId = -1;
        float bestFlowTotal = float.PositiveInfinity;
        int bestGridDistancePortalId = -1;
        float bestGridDistanceTotal = float.PositiveInfinity;
        List<string> candidates = new List<string>(startSector.PortalIds.Count);

        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            bool hasDownstream = sharedField.NodeCosts.TryGetValue(startNode, out float downstreamCost);
            float accessCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            float flowTotal = hasDownstream && !float.IsPositiveInfinity(accessCost)
                ? accessCost + downstreamCost
                : float.PositiveInfinity;
            if (flowTotal < bestFlowTotal)
            {
                bestFlowTotal = flowTotal;
                bestFlowPortalId = portalId;
            }

            float gridDistanceTotal = float.PositiveInfinity;
            float gridAccess = float.PositiveInfinity;
            float gridDownstream = float.PositiveInfinity;
            float gridTotal = float.PositiveInfinity;
            if (TryGetPortalById(_world, portalId, out PortalData portal))
            {
                Vector2Int[] currentCells = GetPortalCellsForSector(portal, startSectorId);
                Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, startSectorId));
                Vector3 currentCenter = ResolveCellGroupCenter(currentCells);
                Vector3 oppositeCenter = ResolveCellGroupCenter(oppositeCells);
                float currentGridDistance = ResolveGridPathLengthOrInfinity(startPosition, currentCenter, agentTypeId)
                                   + ResolveGridPathLengthOrInfinity(currentCenter, stableGoalPosition, agentTypeId);
                float oppositeGridDistance = ResolveGridPathLengthOrInfinity(startPosition, oppositeCenter, agentTypeId)
                                    + ResolveGridPathLengthOrInfinity(oppositeCenter, stableGoalPosition, agentTypeId);
                gridDistanceTotal = Mathf.Min(currentGridDistance, oppositeGridDistance);
                gridAccess = ResolveGridPathCostToAnyOrInfinity(startX, startY, currentCells);
                gridDownstream = ResolveGridPathCostFromAnyToCellOrInfinity(oppositeCells, goalX, goalY);
                bool hasTraversablePortalPair = false;
                int pairCount = Mathf.Min(currentCells.Length, oppositeCells.Length);
                for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
                {
                    Vector2Int currentCell = currentCells[pairIndex];
                    Vector2Int oppositeCell = oppositeCells[pairIndex];
                    if (CanTraverseNeighborCells(_world, currentCell.x, currentCell.y, oppositeCell.x, oppositeCell.y))
                    {
                        hasTraversablePortalPair = true;
                        break;
                    }
                }

                if (hasTraversablePortalPair
                    && !float.IsPositiveInfinity(gridAccess)
                    && !float.IsPositiveInfinity(gridDownstream))
                {
                    gridTotal = gridAccess + 1f + gridDownstream;
                }
            }

            if (gridDistanceTotal < bestGridDistanceTotal)
            {
                bestGridDistanceTotal = gridDistanceTotal;
                bestGridDistancePortalId = portalId;
            }

            candidates.Add(
                "{id=" + portalId
                + ",selected=" + (portalId == selectedPortalId)
                + ",flowAccess=" + FormatDiagnosticCost(accessCost)
                + ",flowDownstream=" + (hasDownstream ? FormatDiagnosticCost(downstreamCost) : "missing")
                + ",flowTotal=" + FormatDiagnosticCost(flowTotal)
                + ",gridDistanceTotal=" + FormatDiagnosticCost(gridDistanceTotal)
                + ",gridAccess=" + FormatDiagnosticCost(gridAccess)
                + ",gridDownstream=" + FormatDiagnosticCost(gridDownstream)
                + ",gridTotal=" + FormatDiagnosticCost(gridTotal)
                + "}");
        }

        return "[FlowPortalRankDiag startSector=" + startSectorId
               + " goalSector=" + goalSectorId
               + " start=(" + startX + "," + startY + ")"
               + " goal=(" + goalX + "," + goalY + ")"
               + " selected=" + selectedPortalId
               + " bestFlow=" + bestFlowPortalId + ":" + FormatDiagnosticCost(bestFlowTotal)
               + " bestGridDistance=" + bestGridDistancePortalId + ":" + FormatDiagnosticCost(bestGridDistanceTotal)
               + " handle=" + FormatPathHandle(handle)
               + " candidates=" + string.Join(" | ", candidates)
               + "]";
    }

    private static float ResolveGridPathCostToAnyOrInfinity(int startX, int startY, Vector2Int[] goalCells)
    {
        if (_world == null || goalCells == null || goalCells.Length == 0)
            return float.PositiveInfinity;
        if (startX < 0 || startX >= _world.Width || startY < 0 || startY >= _world.Height || !_world.IsWalkable(startX, startY))
            return float.PositiveInfinity;

        bool[] targets = new bool[_world.Width * _world.Height];
        bool hasTarget = false;
        for (int i = 0; i < goalCells.Length; i++)
        {
            Vector2Int cell = goalCells[i];
            if (cell.x < 0 || cell.x >= _world.Width || cell.y < 0 || cell.y >= _world.Height || !_world.IsWalkable(cell.x, cell.y))
                continue;

            targets[_world.GetIndex(cell.x, cell.y)] = true;
            hasTarget = true;
        }

        if (!hasTarget)
            return float.PositiveInfinity;

        return ResolveGridPathCostToTargetsOrInfinity(startX, startY, targets);
    }

    private static float ResolveGridPathCostFromAnyToCellOrInfinity(Vector2Int[] startCells, int goalX, int goalY)
    {
        if (_world == null || startCells == null || startCells.Length == 0)
            return float.PositiveInfinity;

        float best = float.PositiveInfinity;
        for (int i = 0; i < startCells.Length; i++)
        {
            Vector2Int start = startCells[i];
            float cost = ResolveGridPathCostOrInfinity(start.x, start.y, goalX, goalY);
            if (cost < best)
                best = cost;
        }

        return best;
    }

    private static float ResolveGridPathCostOrInfinity(int startX, int startY, int goalX, int goalY)
    {
        if (_world == null)
            return float.PositiveInfinity;
        if (goalX < 0 || goalX >= _world.Width || goalY < 0 || goalY >= _world.Height || !_world.IsWalkable(goalX, goalY))
            return float.PositiveInfinity;

        bool[] targets = new bool[_world.Width * _world.Height];
        targets[_world.GetIndex(goalX, goalY)] = true;
        return ResolveGridPathCostToTargetsOrInfinity(startX, startY, targets);
    }

    private static float ResolveGridPathCostToTargetsOrInfinity(int startX, int startY, bool[] targets)
    {
        if (_world == null || targets == null || targets.Length != _world.Width * _world.Height)
            return float.PositiveInfinity;
        if (startX < 0 || startX >= _world.Width || startY < 0 || startY >= _world.Height || !_world.IsWalkable(startX, startY))
            return float.PositiveInfinity;

        int startIndex = _world.GetIndex(startX, startY);
        if (targets[startIndex])
            return 0f;

        float[] costs = RentIntegrationArray(_world.Width * _world.Height);
        InitializeIntegrationField(costs);
        MinHeap openSet = new MinHeap();
        costs[startIndex] = 0f;
        openSet.Push(startIndex, 0f);
        int guard = _world.Width * _world.Height * 4;
        try
        {
            while (openSet.Count > 0 && guard-- > 0)
            {
                QueueNode node = openSet.Pop();
                if (node.Cost > costs[node.Index] + 0.001f)
                    continue;
                if (targets[node.Index])
                    return node.Cost;

                int worldX = node.Index % _world.Width;
                int worldY = node.Index / _world.Width;
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int nextX = worldX + NeighborOffsetX[i];
                    int nextY = worldY + NeighborOffsetY[i];
                    if (nextX < 0 || nextX >= _world.Width || nextY < 0 || nextY >= _world.Height)
                        continue;
                    if (!_world.IsWalkable(nextX, nextY) || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
                        continue;

                    int nextIndex = _world.GetIndex(nextX, nextY);
                    float stepDistance = Mathf.Abs(NeighborOffsetX[i]) + Mathf.Abs(NeighborOffsetY[i]) == 2 ? 1.4142135f : 1f;
                    float stepCost = Mathf.Max(ResolveCellIntegrationCost(_world, nextX, nextY), 0.001f) * stepDistance;
                    float newCost = node.Cost + stepCost;
                    if (!IsSignificantIntegrationImprovement(newCost, costs[nextIndex]))
                        continue;

                    costs[nextIndex] = newCost;
                    openSet.Push(nextIndex, newCost);
                }
            }
        }
        finally
        {
            ReturnIntegrationArray(costs);
        }

        return float.PositiveInfinity;
    }

    private static float ResolveGridPathLengthOrInfinity(Vector3 fromPosition, Vector3 toPosition, int agentTypeId)
    {
        if (_world == null)
            return float.PositiveInfinity;
        return TryEstimateNavigationDistance(fromPosition, toPosition, agentTypeId >= 0 ? agentTypeId : _world.AgentTypeId, out float distance)
            ? distance
            : float.PositiveInfinity;
    }

    private static string FormatDiagnosticCost(float cost)
    {
        return float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3");
    }

    private static string BuildPathRepathDecisionDiagnostics(PathHandle handle, int startSectorId, int goalSectorId, int startX, int startY, int goalX, int goalY)
    {
        if (_world == null)
            return "world=null";
        if (handle == null)
            return "handle=null";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return "invalid-start-sector";
        if (handle.PortalIds == null || handle.PortalIds.Length == 0)
            return "no-portals";

        int sectorIndex = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (sectorIndex < 0)
            return "start-sector-not-in-handle";
        if (sectorIndex >= handle.PortalIds.Length)
            return "final-sector";

        int selectedPortalId = handle.PortalIds[sectorIndex];
        int sharedAgentTypeId = ResolvePreferredAgentTypeId(_world.AgentTypeId);
        bool sharedCached = TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, sharedAgentTypeId, out SharedGoalField sharedField);
        bool pending = PendingSharedGoalFieldBuildJobs.Contains(CreateSharedGoalFieldKey(goalSectorId, goalX, goalY, sharedAgentTypeId));
        if (!sharedCached)
        {
            return "selected=" + selectedPortalId
                   + " sharedCached=False pending=" + pending
                   + " queue=" + SharedGoalFieldBuildQueue.Count;
        }

        SectorData startSector = _world.Sectors[startSectorId];
        int bestPortalId = -1;
        float selectedCost = float.PositiveInfinity;
        float bestCost = float.PositiveInfinity;
        System.Text.StringBuilder candidates = new System.Text.StringBuilder(256);
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int startNode = EncodePortalNode(startSectorId, portalId);
            bool hasDownstream = sharedField.NodeCosts.TryGetValue(startNode, out float downstreamCost);
            float accessCost = ResolvePortalAccessCost(startSector, startSectorId, portalId, startX, startY);
            float totalCost = hasDownstream && !float.IsPositiveInfinity(accessCost)
                ? accessCost + downstreamCost
                : float.PositiveInfinity;
            if (portalId == selectedPortalId)
                selectedCost = totalCost;
            if (totalCost < bestCost)
            {
                bestCost = totalCost;
                bestPortalId = portalId;
            }

            if (i > 0)
                candidates.Append(" | ");
            candidates.Append(portalId)
                .Append(":a=").Append(FormatDiagnosticCost(accessCost))
                .Append(",d=").Append(hasDownstream ? FormatDiagnosticCost(downstreamCost) : "missing")
                .Append(",t=").Append(FormatDiagnosticCost(totalCost));
        }

        bool shouldRebuild = selectedPortalId != bestPortalId
                             && selectedCost > bestCost + IntegrationSignificantImprovement;
        return "selected=" + selectedPortalId + ":" + FormatDiagnosticCost(selectedCost)
               + " best=" + bestPortalId + ":" + FormatDiagnosticCost(bestCost)
               + " threshold=" + IntegrationSignificantImprovement.ToString("F3")
               + " shouldRebuild=" + shouldRebuild
               + " sharedNodes=" + sharedField.NodeCosts.Count
               + " candidates=[" + candidates + "]";
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
        Vector3 stableGoalPosition,
        PathHandle handle = null)
    {
        if (_world == null)
            return "portalChoice=world-null";
        if (startSectorId < 0 || startSectorId >= _world.Sectors.Length)
            return $"portalChoice=invalid-start-sector startSector={startSectorId}";

        if (!TryGetCachedSharedGoalField(goalSectorId, goalX, goalY, ResolvePreferredAgentTypeId(agentTypeId), out SharedGoalField sharedField))
            return "portalChoice=shared-field-not-cached";

        SectorData startSector = _world.Sectors[startSectorId];
        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        int selectedPortalId = ResolveSelectedPortalIdForDiagnostics(handle, startSectorId);
        builder.Append("[FlowStartPortalChoiceDiag startSector=").Append(startSectorId)
            .Append(" goalSector=").Append(goalSectorId)
            .Append(" start=(").Append(startX).Append(',').Append(startY).Append(')')
            .Append(" goal=(").Append(goalX).Append(',').Append(goalY).Append(')')
            .Append(" selectedPortal=").Append(selectedPortalId)
            .Append(" handle=").Append(FormatPathHandle(handle))
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
                .Append(",selected=").Append(portalId == selectedPortalId)
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

    private static int ResolveSelectedPortalIdForDiagnostics(PathHandle handle, int startSectorId)
    {
        if (handle == null || handle.SectorIds == null || handle.PortalIds == null)
            return -1;

        int index = FindSectorIndex(handle, startSectorId, Mathf.Max(0, handle.CurrentSectorIndex));
        if (index < 0 || index >= handle.PortalIds.Length)
            return -1;

        return handle.PortalIds[index];
    }

    private static string BuildPortalCandidateNavDiagnostics(Vector3 startPosition, Vector3 stableGoalPosition, PortalData portal, int sectorId, int agentTypeId)
    {
        Vector2Int[] currentCells = GetPortalCellsForSector(portal, sectorId);
        Vector2Int[] oppositeCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, sectorId));
        Vector3 currentCenter = ResolveCellGroupCenter(currentCells);
        Vector3 oppositeCenter = ResolveCellGroupCenter(oppositeCells);
        string toCurrent = BuildGridPathDiagnostics(startPosition, currentCenter, agentTypeId);
        string toOpposite = BuildGridPathDiagnostics(startPosition, oppositeCenter, agentTypeId);
        string currentToGoal = BuildGridPathDiagnostics(currentCenter, stableGoalPosition, agentTypeId);
        string oppositeToGoal = BuildGridPathDiagnostics(oppositeCenter, stableGoalPosition, agentTypeId);
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

    private static string BuildFlowLineOfSightDecisionDiagnostics(
        Vector3 startPosition,
        Vector3 stableGoalPosition,
        Vector3 tileTargetPosition,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int agentTypeId,
        FlowTileCacheEntry tile)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(2048);
        builder.Append("[FlowLosDecision goal=")
            .Append(BuildLineDecisionSegmentDiagnostics(startPosition, stableGoalPosition, startX, startY, goalX, goalY, agentTypeId))
            .Append(" tileTarget=");

        if (_world != null && _world.WorldToGrid(tileTargetPosition, out int tileTargetX, out int tileTargetY))
        {
            builder.Append(BuildLineDecisionSegmentDiagnostics(startPosition, tileTargetPosition, startX, startY, tileTargetX, tileTargetY, agentTypeId));
        }
        else
        {
            builder.Append("{targetInGrid=False targetPos=").Append(tileTargetPosition).Append('}');
        }

        if (tile != null && tile.Key.GoalKind == TileGoalKind.Portal)
        {
            PortalData portal = GetPortalById(_world, tile.Key.GoalId);
            Vector2Int[] cells = GetPortalCellsForSector(portal, tile.Key.SectorId);
            builder.Append(" portalCells=[");
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                    builder.Append(" | ");
                Vector2Int cell = cells[i];
                builder.Append('i').Append(i).Append('=')
                    .Append(BuildLineDecisionSegmentDiagnostics(startPosition, _world.GridToWorldCenter(cell.x, cell.y), startX, startY, cell.x, cell.y, agentTypeId));
            }
            builder.Append(']');
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildLineDecisionSegmentDiagnostics(
        Vector3 fromPosition,
        Vector3 toPosition,
        int fromX,
        int fromY,
        int toX,
        int toY,
        int agentTypeId)
    {
        if (_world == null)
            return "{world=null}";

        bool toInBounds = toX >= 0 && toX < _world.Width && toY >= 0 && toY < _world.Height;
        bool strictGridLos = toInBounds && HasGridLineOfSight(_world, fromX, fromY, toX, toY);
        bool pendingGridLos = toInBounds && HasPendingDirectLineOfSight(_world, fromX, fromY, toX, toY);
        bool softGridLos = toInBounds && HasSoftCostTolerantGridLineOfSight(_world, fromX, fromY, toX, toY, maxAllowedCost: 15);
        return "{from=(" + fromX + "," + fromY + ")"
               + ",to=(" + toX + "," + toY + ")"
               + ",toInBounds=" + toInBounds
               + ",strictGridLos=" + strictGridLos
               + ",pendingGridLos=" + pendingGridLos
               + ",softGridLos=" + softGridLos
               + ",trace=" + BuildLineOfSightDecisionCellTrace(_world, fromX, fromY, toX, toY)
               + ",registeredObstacles=" + BuildRegisteredObstacleSegmentDiagnostics(fromPosition, toPosition)
               + ",physics=" + BuildPhysicsSegmentDiagnostics(fromPosition, toPosition, agentTypeId)
               + "}";
    }

    private static string BuildLineOfSightDecisionCellTrace(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        if (world == null)
            return "world-null";
        if (x0 < 0 || x0 >= world.Width || y0 < 0 || y0 >= world.Height)
            return "start-out";
        if (x1 < 0 || x1 >= world.Width || y1 < 0 || y1 >= world.Height)
            return "target-out";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(768);
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
        bool first = true;
        int count = 0;
        const int maxCells = 96;

        while (true)
        {
            if (count > 0)
                builder.Append(" -> ");

            AppendLineDecisionCell(builder, world, cx, cy, previousX, previousY, first);
            count++;

            if (cx == x1 && cy == y1)
                break;
            if (count >= maxCells)
            {
                builder.Append(" -> truncated");
                break;
            }

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

        builder.Append("]");
        return builder.ToString();
    }

    private static void AppendLineDecisionCell(System.Text.StringBuilder builder, NavigationWorld world, int x, int y, int previousX, int previousY, bool first)
    {
        bool inBounds = x >= 0 && x < world.Width && y >= 0 && y < world.Height;
        builder.Append("(").Append(x).Append(',').Append(y).Append(")");
        if (!inBounds)
        {
            builder.Append("{out}");
            return;
        }

        int index = world.GetIndex(x, y);
        bool walkable = world.WalkableMask != null && world.WalkableMask.Length == world.Width * world.Height && world.WalkableMask[index];
        bool baseWalkable = world.BaseWalkableMask != null && world.BaseWalkableMask.Length == world.Width * world.Height && world.BaseWalkableMask[index];
        int cost = TryGetCostFieldValue(world, x, y, out byte resolvedCost) ? resolvedCost : -1;
        int island = world.IslandIds != null && world.IslandIds.Length == world.Width * world.Height ? world.IslandIds[index] : -1;
        bool link = first || CanTraverseNeighborCells(world, previousX, previousY, x, y);
        bool softBoundary = walkable && cost > 1 && IsBoundaryOnlySoftCostCell(world, x, y);
        bool costStamp = walkable && IsCellAffectedByCostStamp(world, x, y);
        builder.Append("{walk=").Append(walkable)
            .Append(",base=").Append(baseWalkable)
            .Append(",cost=").Append(cost)
            .Append(",softBoundary=").Append(softBoundary)
            .Append(",costStamp=").Append(costStamp)
            .Append(",island=").Append(island)
            .Append(",link=").Append(link)
            .Append("}");
    }

    private static string BuildRegisteredObstacleSegmentDiagnostics(Vector3 fromPosition, Vector3 toPosition)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("[boxes=");
        int count = 0;
        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            Vector3 half = box.HalfExtents;
            if (!TryMeasureSegmentBoxDistance(fromPosition, toPosition, box.Center, half, out float distance, out Vector3 closestPoint))
                continue;
            float inflatedDistance = distance - Mathf.Max(0f, half.x > half.z ? half.x : half.z);
            if (distance > Mathf.Max(_world.CellSize * 4f, 2f))
                continue;

            if (count > 0)
                builder.Append(" | ");
            builder.Append("{id=").Append(box.Id)
                .Append(",center=").Append(box.Center)
                .Append(",half=").Append(box.HalfExtents)
                .Append(",dist=").Append(distance.ToString("F3"))
                .Append(",inflatedDist=").Append(inflatedDistance.ToString("F3"))
                .Append(",closest=").Append(closestPoint)
                .Append(",grid=").Append(FormatBoundsGridRange(new Bounds(box.Center, box.HalfExtents * 2f)))
                .Append("}");
            count++;
            if (count >= 8)
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
            if (!TryMeasureSegmentCircleDistance(fromPosition, toPosition, circle.Position, circle.Radius, out float distance, out Vector3 closestPoint))
                continue;
            if (distance > Mathf.Max(_world.CellSize * 4f, 2f))
                continue;

            if (count > 0)
                builder.Append(" | ");
            builder.Append("{id=").Append(circle.Id)
                .Append(",center=").Append(circle.Position)
                .Append(",radius=").Append(circle.Radius.ToString("F3"))
                .Append(",dist=").Append(distance.ToString("F3"))
                .Append(",closest=").Append(closestPoint)
                .Append("}");
            count++;
            if (count >= 8)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");
        builder.Append(" nearestBoxes=");
        AppendNearestRegisteredBoxes(builder, fromPosition, toPosition);
        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendNearestRegisteredBoxes(System.Text.StringBuilder builder, Vector3 fromPosition, Vector3 toPosition)
    {
        const int maxNearest = 5;
        int[] nearestIds = new int[maxNearest];
        float[] nearestDistances = new float[maxNearest];
        Vector3[] nearestCenters = new Vector3[maxNearest];
        Vector3[] nearestHalfExtents = new Vector3[maxNearest];
        string[] nearestGrids = new string[maxNearest];
        for (int i = 0; i < maxNearest; i++)
        {
            nearestIds[i] = 0;
            nearestDistances[i] = float.PositiveInfinity;
            nearestCenters[i] = Vector3.zero;
            nearestHalfExtents[i] = Vector3.zero;
            nearestGrids[i] = "none";
        }

        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            if (!TryMeasureSegmentBoxDistance(fromPosition, toPosition, box.Center, box.HalfExtents, out float distance, out _))
                continue;

            for (int i = 0; i < maxNearest; i++)
            {
                if (distance >= nearestDistances[i])
                    continue;

                for (int j = maxNearest - 1; j > i; j--)
                {
                    nearestIds[j] = nearestIds[j - 1];
                    nearestDistances[j] = nearestDistances[j - 1];
                    nearestCenters[j] = nearestCenters[j - 1];
                    nearestHalfExtents[j] = nearestHalfExtents[j - 1];
                    nearestGrids[j] = nearestGrids[j - 1];
                }

                nearestIds[i] = box.Id;
                nearestDistances[i] = distance;
                nearestCenters[i] = box.Center;
                nearestHalfExtents[i] = box.HalfExtents;
                nearestGrids[i] = FormatBoundsGridRange(new Bounds(box.Center, box.HalfExtents * 2f));
                break;
            }
        }

        if (float.IsPositiveInfinity(nearestDistances[0]))
        {
            builder.Append("none");
            return;
        }

        for (int i = 0; i < maxNearest; i++)
        {
            if (float.IsPositiveInfinity(nearestDistances[i]))
                break;
            if (i > 0)
                builder.Append(" | ");
            builder.Append("{id=").Append(nearestIds[i])
                .Append(",center=").Append(nearestCenters[i])
                .Append(",half=").Append(nearestHalfExtents[i])
                .Append(",dist=").Append(nearestDistances[i].ToString("F3"))
                .Append(",grid=").Append(nearestGrids[i])
                .Append("}");
        }
    }

    private static bool TryMeasureSegmentBoxDistance(Vector3 segmentStart, Vector3 segmentEnd, Vector3 center, Vector3 halfExtents, out float distance, out Vector3 closestPoint)
    {
        Vector3 segment = segmentEnd - segmentStart;
        segment.y = 0f;
        float lengthSq = segment.sqrMagnitude;
        if (lengthSq <= 0.0001f)
        {
            distance = float.PositiveInfinity;
            closestPoint = segmentStart;
            return false;
        }

        Vector3 centerOffset = center - segmentStart;
        centerOffset.y = 0f;
        float t = Mathf.Clamp01(Vector3.Dot(centerOffset, segment) / lengthSq);
        closestPoint = segmentStart + segment * t;
        Vector3 closestOnBox = new Vector3(
            Mathf.Clamp(closestPoint.x, center.x - halfExtents.x, center.x + halfExtents.x),
            closestPoint.y,
            Mathf.Clamp(closestPoint.z, center.z - halfExtents.z, center.z + halfExtents.z));
        Vector3 delta = closestPoint - closestOnBox;
        delta.y = 0f;
        distance = delta.magnitude;
        return true;
    }

    private static bool TryMeasureSegmentCircleDistance(Vector3 segmentStart, Vector3 segmentEnd, Vector3 center, float radius, out float distance, out Vector3 closestPoint)
    {
        Vector3 segment = segmentEnd - segmentStart;
        segment.y = 0f;
        float lengthSq = segment.sqrMagnitude;
        if (lengthSq <= 0.0001f)
        {
            distance = float.PositiveInfinity;
            closestPoint = segmentStart;
            return false;
        }

        Vector3 centerOffset = center - segmentStart;
        centerOffset.y = 0f;
        float t = Mathf.Clamp01(Vector3.Dot(centerOffset, segment) / lengthSq);
        closestPoint = segmentStart + segment * t;
        Vector3 delta = closestPoint - center;
        delta.y = 0f;
        distance = Mathf.Max(0f, delta.magnitude - radius);
        return true;
    }

    private static string BuildPhysicsSegmentDiagnostics(Vector3 fromPosition, Vector3 toPosition, int agentTypeId)
    {
        Vector3 start = fromPosition + Vector3.up * 0.5f;
        Vector3 end = toPosition + Vector3.up * 0.5f;
        Vector3 delta = end - start;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return "{empty}";

        Vector3 direction = delta / distance;
        RaycastHit[] hits = Physics.RaycastAll(start, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        System.Text.StringBuilder builder = new System.Text.StringBuilder(512);
        builder.Append("{agentType=").Append(agentTypeId).Append(",hits=");
        int count = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null)
                continue;
            if (count > 0)
                builder.Append(" | ");
            builder.Append("{name=").Append(collider.name)
                .Append(",layer=").Append(LayerMask.LayerToName(collider.gameObject.layer))
                .Append(",dist=").Append(hits[i].distance.ToString("F3"))
                .Append(",point=").Append(hits[i].point)
                .Append(",normal=").Append(hits[i].normal)
                .Append("}");
            count++;
            if (count >= 8)
            {
                builder.Append(" | truncated");
                break;
            }
        }

        if (count == 0)
            builder.Append("none");
        builder.Append('}');
        return builder.ToString();
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

            float cost = GetTileIntegrationCostForDiagnostics(tile, cell.x, cell.y);
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

        SectorPortalAccessEntry entry = GetPrebuiltSectorPortalAccess(sector, sectorId, portalId);
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

            float cost = DecodePortalAccessIntegrationCost(entry, GetSectorLocalIndex(sector, cell.x, cell.y));
            builder.Append(float.IsPositiveInfinity(cost) ? "INF" : cost.ToString("F3"));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string DescribeTileIntegrationSummary(FlowTileCacheEntry tile)
    {
        if (tile == null)
            return "tile=null";
        if (tile.Integration == null || tile.Integration.Length == 0)
            return tile.IntegrationReleased
                ? $"released reachableFlags={CountReachableFlowCells(tile)} goalSummary={FormatIntegrationCosts(tile, tile.GoalCells)} seamSummaries={tile.PortalSeamIntegrationSummaries.Count}"
                : "empty";

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

    private static int CountReachableFlowCells(FlowTileCacheEntry tile)
    {
        if (tile?.FlowFieldValues == null)
            return 0;

        int count = 0;
        for (int i = 0; i < tile.FlowFieldValues.Length; i++)
        {
            if (IsFlowReachable(tile, i))
                count++;
        }

        return count;
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
        Vector3 laneBias = shouldApplyLaneBias ? ResolveStableLaneBias(agent.Position, descriptor.CorridorAxis) : Vector3.zero;
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
        if (distanceToAnchor <= ResolveBottleneckOccupiedRadius(descriptor))
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

    private static float ResolveBottleneckOccupiedRadius(CorridorBottleneckDescriptor descriptor)
    {
        float criticalRadius = Mathf.Max(_world.CellSize * 1.25f, Config.BottleneckInfluenceDistance);
        if (descriptor.IsLongCorridor)
            return Mathf.Min(descriptor.InfluenceRadius * 0.85f, criticalRadius);

        return Mathf.Min(descriptor.InfluenceRadius * 0.85f, criticalRadius * 1.35f);
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
            {
                if (TryResolveImmediateDownstreamNarrowBottleneck(
                        agent.NavState.PathHandle,
                        downstreamPortalId,
                        currentSectorId,
                        currentPortal,
                        out descriptor))
                {
                    return true;
                }

                return false;
            }

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

    private static bool TryResolveImmediateDownstreamNarrowBottleneck(
        PathHandle handle,
        int currentPortalId,
        int currentSectorId,
        PortalData currentPortal,
        out CorridorBottleneckDescriptor descriptor)
    {
        descriptor = default;
        if (handle == null || handle.PortalIds == null || handle.SectorIds == null)
            return false;

        int currentPortalIndex = -1;
        for (int i = 0; i < handle.PortalIds.Length; i++)
        {
            if (handle.PortalIds[i] == currentPortalId)
            {
                currentPortalIndex = i;
                break;
            }
        }

        int nextPortalIndex = currentPortalIndex + 1;
        if (currentPortalIndex < 0 || nextPortalIndex >= handle.PortalIds.Length || nextPortalIndex + 1 >= handle.SectorIds.Length)
            return false;

        int nextPortalId = handle.PortalIds[nextPortalIndex];
        if (!TryGetPortalById(_world, nextPortalId, out PortalData nextPortal) || !nextPortal.IsNarrow)
            return false;

        int bridgeSectorId = handle.SectorIds[nextPortalIndex];
        if (currentPortal.SectorAId != currentSectorId && currentPortal.SectorBId != currentSectorId)
            return false;
        if (currentPortal.SectorAId != bridgeSectorId && currentPortal.SectorBId != bridgeSectorId)
            return false;
        if (nextPortal.SectorAId != bridgeSectorId && nextPortal.SectorBId != bridgeSectorId)
            return false;

        float portalDistance = Vector3.Distance(currentPortal.WorldCenter, nextPortal.WorldCenter);
        float immediateDistance = _world.CellSize * _world.SectorSizeInCells + Config.BottleneckInfluenceDistance;
        if (portalDistance > immediateDistance)
            return false;

        int nextSectorId = handle.SectorIds[nextPortalIndex + 1];
        int travelDirection;
        if (bridgeSectorId == nextPortal.SectorAId && nextSectorId == nextPortal.SectorBId)
            travelDirection = 1;
        else if (bridgeSectorId == nextPortal.SectorBId && nextSectorId == nextPortal.SectorAId)
            travelDirection = -1;
        else
            return false;

        Vector3 corridorAxis = nextPortal.IsVerticalBoundary
            ? new Vector3(travelDirection >= 0 ? 1f : -1f, 0f, 0f)
            : new Vector3(0f, 0f, travelDirection >= 0 ? 1f : -1f);
        float influenceRadius = Mathf.Max(
            Config.BottleneckInfluenceDistance,
            portalDistance + Config.BottleneckInfluenceDistance);
        descriptor = new CorridorBottleneckDescriptor(
            -1 - nextPortalId,
            nextPortalId,
            currentSectorId,
            travelDirection,
            corridorAxis.normalized,
            currentPortal.WorldCenter,
            false,
            false,
            influenceRadius);
        return true;
    }

    private static Vector3 ResolveStableLaneBias(Vector3 position, Vector3 corridorAxis)
    {
        Vector3 sideAxis = Vector3.Cross(Vector3.up, corridorAxis);
        if (sideAxis.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        sideAxis.Normalize();
        if (!HasWalkableLaneSide(position, sideAxis) && !HasWalkableLaneSide(position, -sideAxis))
        {
            int axisMode = ResolveCorridorAxisMode(corridorAxis);
            if (axisMode == 0 || !IsInsideSingleCellBottleneckLane(position, axisMode))
                return Vector3.zero;
        }

        return sideAxis * Config.LaneBiasStrength;
    }

    private static bool IsInsideSingleCellBottleneckLane(Vector3 position, int axisMode)
    {
        if (_world == null)
            throw new InvalidOperationException("IsInsideSingleCellBottleneckLane failed: world is null.");
        if (!_world.WorldToGrid(position, out int cellX, out int cellY) || !_world.IsWalkable(cellX, cellY))
            return false;
        if (!TryMeasureCorridorSpan(cellX, cellY, axisMode, out int laneMin, out int laneMax, out int spanWidth))
            return false;
        if (spanWidth > Config.PortalNarrowWidthCells)
            return false;

        return TryMeasureCorridorRunLength(cellX, cellY, axisMode, laneMin, laneMax, out int forwardRun, out int backwardRun)
               && forwardRun + backwardRun >= 2;
    }

    private static bool HasWalkableLaneSide(Vector3 position, Vector3 sideAxis)
    {
        if (_world == null)
            throw new InvalidOperationException("HasWalkableLaneSide failed: world is null.");
        if (!_world.WorldToGrid(position, out int fromX, out int fromY) || !_world.IsWalkable(fromX, fromY))
            return false;

        int sideX = Mathf.RoundToInt(sideAxis.x);
        int sideY = Mathf.RoundToInt(sideAxis.z);
        if (sideX == 0 && sideY == 0)
            return false;

        int toX = fromX + sideX;
        int toY = fromY + sideY;
        return _world.IsWalkable(toX, toY)
               && CanTraverseNeighborCells(_world, fromX, fromY, toX, toY);
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
            int localIndex = tile.GetLocalIndex(currentX, currentY);
            Vector2 flow = ResolveRuntimeFlowDirection(tile, currentX, currentY, localIndex);
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
        if (tile == null || !HasFlowLineOfSight(tile, tile.GetLocalIndex(currentX, currentY)))
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
            case FlowFieldAgentState.Combat:
                score += 2;
                break;
            case FlowFieldAgentState.Follow:
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
        List<AgentRuntimeData> nearbyAgents = CollectNearbyDynamicNeighbors(self.Position, self.Id, contextRadius);
        for (int i = 0; i < nearbyAgents.Count; i++)
        {
            AgentRuntimeData other = nearbyAgents[i];
            if (other.IgnoreAgentCollision)
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
        List<AgentRuntimeData> nearbyAgents = CollectNearbyDynamicNeighbors(anchor, self.Id, forwardWindow);
        for (int i = 0; i < nearbyAgents.Count; i++)
        {
            AgentRuntimeData other = nearbyAgents[i];
            if (other.IgnoreAgentCollision)
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
        Vector3 runtimeObstacleAvoidance = ResolvePendingRuntimeObstacleAvoidance(
            self,
            desiredDirection,
            desiredVelocity,
            maxSpeed,
            out string runtimeObstacleDebug);

        Vector3 baseVelocity = desiredVelocity * bottleneck.SpeedScale + bottleneck.QueueBias;
        Vector3 avoidance = clampedAgentAvoidance + boundaryAvoidance + runtimeObstacleAvoidance;
        Vector3 laneVelocity = ResolveWalkableLaneVelocity(self.Position, baseVelocity + avoidance * 0.75f, bottleneck.LaneBias * maxSpeed);
        Vector3 result = baseVelocity + avoidance * 0.75f + laneVelocity;
        result = ApplyEdgeRecovery(result, desiredDirection, edgeNormal, edgeDistance, self.Radius, maxSpeed);
        result = ApplyLaneCommitment(self.Position, result, laneVelocity, bottleneck, edgeNormal, edgeDistance, self.Radius, maxSpeed);
        result = ConstrainVelocityToWalkableSteeringStep(self.Position, result, desiredVelocity, maxSpeed);

        Vector3 toGoal = goalPosition - self.Position;
        toGoal.y = 0f;
        if (bottleneck.SpeedScale > 0f && toGoal.sqrMagnitude > 0.0001f && Vector3.Dot(result, toGoal) < 0f)
            result = desiredVelocity * 0.35f + avoidance * 0.25f;

        Vector3 clampedResult = ConstrainVelocityToWalkableSteeringStep(
            self.Position,
            Vector3.ClampMagnitude(result, maxSpeed),
            desiredVelocity,
            maxSpeed);
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
        LogRouteAsymmetryDiagnostic(
            selfContext,
            self,
            goalPosition,
            desiredVelocity,
            desiredResolution,
            tile,
            bottleneck,
            nearbyAgents.Count,
            baseVelocity,
            agentAvoidance,
            clampedAgentAvoidance,
            boundaryAvoidance,
            runtimeObstacleAvoidance,
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
                    $"agentAvoid={agentAvoidance} clampedAgentAvoid={clampedAgentAvoidance} boundaryAvoid={boundaryAvoidance} runtimeObstacleAvoid={runtimeObstacleAvoidance} " +
                    $"combinedAvoid={avoidance} edgeNormal={edgeNormal} edgeDist={edgeDistance:F3} resultPreClamp={result} maxSpeed={maxSpeed:F3}");

            float forwardSpeed = desiredDirection.sqrMagnitude > 0.0001f ? Vector3.Dot(result, desiredDirection) : 0f;
            float lateralSpeed = desiredDirection.sqrMagnitude > 0.0001f
                ? Vector3.ProjectOnPlane(result, desiredDirection).magnitude
                : result.magnitude;
            bool anomalousSteering = agentAvoidance.magnitude > maxSpeed * 1.2f
                                     || boundaryAvoidance.magnitude > maxSpeed * 0.45f
                                     || runtimeObstacleAvoidance.magnitude > maxSpeed * 0.45f
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
                    $"runtimeObstacleAvoidMag={runtimeObstacleAvoidance.magnitude:F3} runtimeObstacle={runtimeObstacleDebug} " +
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

    private static void LogRouteAsymmetryDiagnostic(
        IEntityContext selfContext,
        AgentRuntimeData agent,
        Vector3 goalPosition,
        Vector3 desiredVelocity,
        DesiredDirectionResolution desiredResolution,
        FlowTileCacheEntry tile,
        BottleneckDecision bottleneck,
        int nearbyAgentCount,
        Vector3 baseVelocity,
        Vector3 agentAvoidance,
        Vector3 clampedAgentAvoidance,
        Vector3 boundaryAvoidance,
        Vector3 runtimeObstacleAvoidance,
        Vector3 laneVelocity,
        Vector3 resultPreClamp,
        Vector3 result,
        Vector3 edgeNormal,
        float edgeDistance,
        float maxSpeed)
    {
        if (selfContext == null || agent == null || _world == null)
            return;
        if (maxSpeed <= 0.0001f)
            return;

        Vector3 desiredDirection = desiredVelocity.sqrMagnitude > 0.0001f
            ? desiredVelocity.normalized
            : desiredResolution.Direction;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude > 0.0001f)
            desiredDirection.Normalize();

        float forwardSpeed = desiredDirection.sqrMagnitude > 0.0001f ? Vector3.Dot(result, desiredDirection) : result.magnitude;
        float lateralSpeed = desiredDirection.sqrMagnitude > 0.0001f
            ? Vector3.ProjectOnPlane(result, desiredDirection).magnitude
            : 0f;
        bool nearEdge = edgeDistance < Mathf.Max(agent.Radius * 2.4f, _world.CellSize * 1.35f);
        bool slowNearEdge = nearEdge && forwardSpeed < maxSpeed * 0.75f;
        bool crowded = nearbyAgentCount >= 3 && forwardSpeed < maxSpeed * 0.85f;
        bool bottleneckLimited = bottleneck.SpeedScale < 0.98f || bottleneck.QueueBias.sqrMagnitude > 0.0001f;
        bool lateralDominant = lateralSpeed > maxSpeed * 0.55f && forwardSpeed < maxSpeed * 0.95f;
        bool strongBoundary = boundaryAvoidance.magnitude > maxSpeed * 0.25f;
        if (!slowNearEdge && !crowded && !bottleneckLimited && !lateralDominant && !strongBoundary)
            return;

        int frame = GetFrameCount();
        if (agent.NavState.LastRouteAsymmetryDiagnosticFrame >= 0
            && frame - agent.NavState.LastRouteAsymmetryDiagnosticFrame < FlowHeavyDiagnosticCooldownFrames)
        {
            return;
        }

        agent.NavState.LastRouteAsymmetryDiagnosticFrame = frame;
        Vector2Int currentCell = agent.NavState.CurrentCell;
        int startX = currentCell.x;
        int startY = currentCell.y;
        int startSectorId = agent.NavState.CurrentSectorId;
        bool goalInGrid = _world.WorldToGrid(goalPosition, out int goalX, out int goalY);
        int goalSectorId = -1;
        if (goalInGrid)
            _world.TryGetSectorId(goalX, goalY, out goalSectorId);

        int downstreamPortalId = tile != null && tile.Key.GoalKind == TileGoalKind.Portal ? tile.Key.GoalId : -1;
        string portalRanks = goalInGrid
            ? BuildStartPortalRankDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY, agent.AgentTypeId, selfContext.Position, goalPosition, agent.NavState.PathHandle)
            : "[FlowPortalRankDiag goal-out-of-grid]";
        string routeCompare = goalInGrid
            ? BuildFlowPathComparisonDiagnostics(tile, startSectorId, goalSectorId, startX, startY, goalX, goalY, downstreamPortalId, agent.NavState.PathHandle)
            : "{goal-out-of-grid}";

        Debug.LogWarning(
            $"[FlowRouteAsymmetryDiag] frame={frame} key={selfContext.CharacterKey} id={agent.Id} " +
            $"trigger=slowNearEdge:{slowNearEdge},crowded:{crowded},bottleneck:{bottleneckLimited},lateral:{lateralDominant},boundary:{strongBoundary} " +
            $"pos={selfContext.Position} cell=({startX},{startY}) sector={startSectorId} goalPos={goalPosition} goalCell={(goalInGrid ? $"({goalX},{goalY})" : "out")} goalSector={goalSectorId} " +
            $"goalDeltaXZ=({goalPosition.x - selfContext.Position.x:F3},{goalPosition.z - selfContext.Position.z:F3}) " +
            $"desiredSrc={desiredResolution.Source} desiredDir={desiredDirection} desiredVel={desiredVelocity} tileTarget={desiredResolution.TileTargetPosition} " +
            $"tileTargetDeltaXZ=({desiredResolution.TileTargetPosition.x - selfContext.Position.x:F3},{desiredResolution.TileTargetPosition.z - selfContext.Position.z:F3}) " +
            $"flow={desiredResolution.Flow} integration={desiredResolution.Integration:F3} goalKind={(tile != null ? tile.Key.GoalKind.ToString() : "null")} portal={downstreamPortalId} " +
            $"speedForward={forwardSpeed:F3}/{maxSpeed:F3} lateral={lateralSpeed:F3} result={result} preClamp={resultPreClamp} baseVel={baseVelocity} " +
            $"nearby={nearbyAgentCount} agentAvoid={agentAvoidance} clampedAgentAvoid={clampedAgentAvoidance} boundaryAvoid={boundaryAvoidance} runtimeObstacleAvoid={runtimeObstacleAvoidance} laneVelocity={laneVelocity} " +
            $"edgeNormal={edgeNormal} edgeDist={edgeDistance:F3} bottleneckSpeed={bottleneck.SpeedScale:F3} bottleneckState={bottleneck.OwnerState} queueBias={bottleneck.QueueBias} laneBias={bottleneck.LaneBias} " +
            $"localGeom={BuildLocalRouteGeometryDiagnostics(startX, startY)} tileDiag={BuildCurrentTileSteeringDiagnostics(tile, startX, startY)} " +
            $"flowTrace={BuildRuntimeFlowPathTrace(tile, startX, startY, 16)} pathCompare={routeCompare} {portalRanks}");

        if (TryBuildPortalLaneMismatchDiagnostic(tile, startX, startY, desiredResolution.TileTargetPosition, out string portalLaneMismatch))
        {
            Debug.LogWarning(
                $"[FlowPortalLaneMismatchDiag] frame={frame} key={selfContext.CharacterKey} id={agent.Id} " +
                $"cell=({startX},{startY}) sector={startSectorId} goalCell={(goalInGrid ? $"({goalX},{goalY})" : "out")} " +
                $"{portalLaneMismatch}");
        }
    }

    private static bool TryBuildPortalLaneMismatchDiagnostic(
        FlowTileCacheEntry tile,
        int startX,
        int startY,
        Vector3 tileTargetPosition,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        if (_world == null || tile == null || tile.Key.GoalKind != TileGoalKind.Portal)
            return false;
        if (!IsInsideSector(tile, startX, startY))
            return false;

        PortalData portal = GetPortalById(_world, tile.Key.GoalId);
        Vector2Int[] currentSideCells = GetPortalCellsForSector(portal, tile.Key.SectorId);
        Vector2Int[] oppositeSideCells = GetPortalCellsForSector(portal, GetOppositeSectorId(portal, tile.Key.SectorId));
        if (currentSideCells.Length != oppositeSideCells.Length)
            throw new InvalidOperationException(
                $"TryBuildPortalLaneMismatchDiagnostic failed: portal {portal.PortalId} side cell count mismatch current={currentSideCells.Length} opposite={oppositeSideCells.Length}");

        int targetPairIndex = -1;
        Vector2Int targetCell = new Vector2Int(int.MinValue, int.MinValue);
        if (_world.WorldToGrid(tileTargetPosition, out int targetX, out int targetY))
        {
            targetCell = new Vector2Int(targetX, targetY);
            targetPairIndex = IndexOfPortalCell(oppositeSideCells, targetX, targetY);
        }

        int localIndex = tile.GetLocalIndex(startX, startY);
        CachedPortalTarget cached = tile.PortalTargets != null && tile.PortalTargets.Length == tile.Width * tile.Height
            ? tile.PortalTargets[localIndex]
            : default;

        bool hasFlowPair = TryTraceRuntimeFlowToPortalPair(tile, startX, startY, currentSideCells, out int flowPairIndex, out string flowTrace);
        bool hasGridPair = TryResolveGridPathPortalPairIndex(startX, startY, currentSideCells, out int gridPairIndex, out string gridTrace);
        bool mismatched = cached.UsedOppositeCenter
                          || cached.SelectedPairIndex < 0
                          || (hasFlowPair && targetPairIndex >= 0 && flowPairIndex != targetPairIndex)
                          || (hasGridPair && targetPairIndex >= 0 && gridPairIndex != targetPairIndex);
        if (!mismatched)
            return false;

        diagnostic =
            $"portal={FormatPortal(portal)} target={tileTargetPosition} targetCell=({targetCell.x},{targetCell.y}) " +
            $"cachedPair={cached.SelectedPairIndex} cachedCenter={cached.UsedOppositeCenter} cachedVisible={cached.VisibleCandidateCount} " +
            $"targetPair={targetPairIndex} flowPair={(hasFlowPair ? flowPairIndex.ToString() : "none")} gridPair={(hasGridPair ? gridPairIndex.ToString() : "none")} " +
            $"currentSide={FormatGoalCells(currentSideCells)} oppositeSide={FormatGoalCells(oppositeSideCells)} " +
            $"flowTrace={flowTrace} gridTrace={gridTrace}";
        return true;
    }

    private static bool TryTraceRuntimeFlowToPortalPair(
        FlowTileCacheEntry tile,
        int startX,
        int startY,
        Vector2Int[] currentSideCells,
        out int pairIndex,
        out string trace)
    {
        pairIndex = -1;
        trace = string.Empty;
        if (tile == null || currentSideCells == null || currentSideCells.Length == 0 || !IsInsideSector(tile, startX, startY))
            return false;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(192);
        int x = startX;
        int y = startY;
        int previousX = x;
        int previousY = y;
        int guard = tile.Width * tile.Height + 1;
        while (guard-- > 0)
        {
            if (builder.Length > 0)
                builder.Append("->");
            builder.Append('(').Append(x).Append(',').Append(y).Append(')');

            int foundPair = IndexOfPortalCell(currentSideCells, x, y);
            if (foundPair >= 0)
            {
                pairIndex = foundPair;
                trace = builder.ToString();
                return true;
            }

            if (!IsInsideSector(tile, x, y) || !_world.IsWalkable(x, y))
            {
                trace = builder.Append(":stop").ToString();
                return false;
            }
            if (x != startX || y != startY)
            {
                if (!CanTraverseNeighborCells(_world, previousX, previousY, x, y))
                {
                    trace = builder.Append(":unlinked").ToString();
                    return false;
                }
            }

            int localIndex = tile.GetLocalIndex(x, y);
            Vector2 flow = ResolveRuntimeFlowDirection(tile, x, y, localIndex);
            int stepX = flow.x > 0.35f ? 1 : flow.x < -0.35f ? -1 : 0;
            int stepY = flow.y > 0.35f ? 1 : flow.y < -0.35f ? -1 : 0;
            if (stepX == 0 && stepY == 0)
            {
                trace = builder.Append(":zero").ToString();
                return false;
            }

            previousX = x;
            previousY = y;
            x += stepX;
            y += stepY;
        }

        trace = builder.Append(":guard").ToString();
        return false;
    }

    private static bool TryResolveGridPathPortalPairIndex(
        int startX,
        int startY,
        Vector2Int[] targetCells,
        out int pairIndex,
        out string trace)
    {
        pairIndex = -1;
        trace = string.Empty;
        if (_world == null || targetCells == null || targetCells.Length == 0)
            return false;
        if (startX < 0 || startX >= _world.Width || startY < 0 || startY >= _world.Height || !_world.IsWalkable(startX, startY))
        {
            trace = "start-invalid";
            return false;
        }

        int cellCount = _world.Width * _world.Height;
        bool[] targets = new bool[cellCount];
        for (int i = 0; i < targetCells.Length; i++)
        {
            Vector2Int target = targetCells[i];
            if (target.x < 0 || target.x >= _world.Width || target.y < 0 || target.y >= _world.Height || !_world.IsWalkable(target.x, target.y))
                continue;
            targets[_world.GetIndex(target.x, target.y)] = true;
        }

        int startIndex = _world.GetIndex(startX, startY);
        float[] costs = RentIntegrationArray(cellCount);
        int[] cameFrom = new int[cellCount];
        for (int i = 0; i < cameFrom.Length; i++)
            cameFrom[i] = -1;

        InitializeIntegrationField(costs);
        MinHeap openSet = new MinHeap();
        costs[startIndex] = 0f;
        openSet.Push(startIndex, 0f);
        int reachedIndex = -1;
        int guard = cellCount * 4;
        try
        {
            while (openSet.Count > 0 && guard-- > 0)
            {
                QueueNode node = openSet.Pop();
                if (node.Cost > costs[node.Index] + 0.001f)
                    continue;
                if (targets[node.Index])
                {
                    reachedIndex = node.Index;
                    break;
                }

                int worldX = node.Index % _world.Width;
                int worldY = node.Index / _world.Width;
                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int nextX = worldX + NeighborOffsetX[i];
                    int nextY = worldY + NeighborOffsetY[i];
                    if (nextX < 0 || nextX >= _world.Width || nextY < 0 || nextY >= _world.Height)
                        continue;
                    if (!_world.IsWalkable(nextX, nextY) || !CanTraverseNeighborCells(_world, worldX, worldY, nextX, nextY))
                        continue;

                    int nextIndex = _world.GetIndex(nextX, nextY);
                    float stepDistance = Mathf.Abs(NeighborOffsetX[i]) + Mathf.Abs(NeighborOffsetY[i]) == 2 ? 1.4142135f : 1f;
                    float stepCost = Mathf.Max(ResolveCellIntegrationCost(_world, nextX, nextY), 0.001f) * stepDistance;
                    float newCost = node.Cost + stepCost;
                    if (!IsSignificantIntegrationImprovement(newCost, costs[nextIndex]))
                        continue;

                    costs[nextIndex] = newCost;
                    cameFrom[nextIndex] = node.Index;
                    openSet.Push(nextIndex, newCost);
                }
            }

            if (reachedIndex < 0)
            {
                trace = guard <= 0 ? "guard-exceeded" : "unreachable";
                return false;
            }

            int reachedX = reachedIndex % _world.Width;
            int reachedY = reachedIndex / _world.Width;
            pairIndex = IndexOfPortalCell(targetCells, reachedX, reachedY);
            trace = FormatReconstructedGridPath(cameFrom, startIndex, reachedIndex, 16, costs[reachedIndex]);
            return pairIndex >= 0;
        }
        finally
        {
            ReturnIntegrationArray(costs);
        }
    }

    private static string BuildLocalRouteGeometryDiagnostics(int centerX, int centerY)
    {
        if (_world == null)
            return "world-null";
        if (centerX < 0 || centerX >= _world.Width || centerY < 0 || centerY >= _world.Height)
            return "center-out";

        int negX = CountTraversableRun(centerX, centerY, -1, 0, 8);
        int posX = CountTraversableRun(centerX, centerY, 1, 0, 8);
        int negZ = CountTraversableRun(centerX, centerY, 0, -1, 8);
        int posZ = CountTraversableRun(centerX, centerY, 0, 1, 8);
        int diagNegZNegX = CountTraversableRun(centerX, centerY, -1, -1, 5);
        int diagNegZPosX = CountTraversableRun(centerX, centerY, 1, -1, 5);
        int diagPosZNegX = CountTraversableRun(centerX, centerY, -1, 1, 5);
        int diagPosZPosX = CountTraversableRun(centerX, centerY, 1, 1, 5);

        int index = _world.GetIndex(centerX, centerY);
        return $"walk={_world.IsWalkable(centerX, centerY)} cost={ResolveCellIntegrationCost(_world, centerX, centerY):F3} " +
               $"island={(_world.IslandIds != null && index < _world.IslandIds.Length ? _world.IslandIds[index] : -1)} " +
               $"runX-={negX} runX+={posX} runZ-={negZ} runZ+={posZ} diag(--,{diagNegZNegX}) diag(+-,{diagNegZPosX}) diag(-+,{diagPosZNegX}) diag(++,{diagPosZPosX}) " +
               $"neighbors={BuildNeighborWalkabilityDiagnostics(centerX, centerY)}";
    }

    private static int CountTraversableRun(int startX, int startY, int stepX, int stepY, int maxSteps)
    {
        if (_world == null)
            return 0;

        int previousX = startX;
        int previousY = startY;
        int count = 0;
        for (int step = 1; step <= maxSteps; step++)
        {
            int x = startX + stepX * step;
            int y = startY + stepY * step;
            if (x < 0 || x >= _world.Width || y < 0 || y >= _world.Height)
                break;
            if (!_world.IsWalkable(x, y) || !CanTraverseNeighborCells(_world, previousX, previousY, x, y))
                break;

            count++;
            previousX = x;
            previousY = y;
        }

        return count;
    }

    private static string BuildNeighborWalkabilityDiagnostics(int centerX, int centerY)
    {
        if (_world == null)
            return "world-null";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        builder.Append('[');
        bool first = true;
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int x = centerX + NeighborOffsetX[i];
            int y = centerY + NeighborOffsetY[i];
            if (!first)
                builder.Append('|');
            first = false;
            builder.Append('(').Append(x).Append(',').Append(y).Append(")=");
            if (x < 0 || x >= _world.Width || y < 0 || y >= _world.Height)
            {
                builder.Append("out");
                continue;
            }

            builder.Append(_world.IsWalkable(x, y) ? "W" : "B")
                .Append(CanTraverseNeighborCells(_world, centerX, centerY, x, y) ? "T" : "N")
                .Append(":c").Append(ResolveCellIntegrationCost(_world, x, y).ToString("F2"));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static Vector3 ApplyLaneCommitment(
        Vector3 position,
        Vector3 velocity,
        Vector3 laneVelocity,
        BottleneckDecision bottleneck,
        Vector3 edgeNormal,
        float edgeDistance,
        float radius,
        float maxSpeed)
    {
        if (!bottleneck.EnforceLaneCommitment || maxSpeed <= 0.0001f)
            return velocity;

        Vector3 laneReference = laneVelocity.sqrMagnitude > 0.0001f ? laneVelocity : bottleneck.LaneBias;
        if (laneReference.sqrMagnitude <= 0.0001f)
            return velocity;

        Vector3 laneAxis = laneReference.normalized;
        float currentLaneSpeed = Vector3.Dot(velocity, laneAxis);
        float desiredLaneSpeed = Vector3.Dot(laneReference, laneAxis);
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
        Vector3 corrected = Vector3.ClampMagnitude(velocity + correction, maxSpeed);
        if (IsPredictedStepWalkable(position, corrected))
            return corrected;

        float oppositeLaneSpeed = Vector3.Dot(velocity, laneAxis);
        bool oppositeLane = desiredLaneSpeed >= 0f
            ? oppositeLaneSpeed < 0f
            : oppositeLaneSpeed > 0f;
        if (!oppositeLane)
            return velocity;

        Vector3 withoutOppositeLane = velocity - laneAxis * oppositeLaneSpeed;
        return IsPredictedStepWalkable(position, withoutOppositeLane) ? withoutOppositeLane : velocity;
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
        if ((encounterType == AvoidanceEncounterType.SameLaneFollow || encounterType == AvoidanceEncounterType.Overtaking)
            && desiredDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 lateralAway = Vector3.ProjectOnPlane(awayContribution, desiredDirection);
            Vector3 longitudinalAway = awayContribution - lateralAway;
            bool hasLateralEscape = HasWalkableLateralSpace(self.Position, desiredDirection);
            if (hasLateralEscape)
            {
                float lateralScale = encounterType == AvoidanceEncounterType.Overtaking
                    ? (overlap > 0.05f ? Mathf.Lerp(0.08f, 0.25f, Mathf.Clamp01(overlap)) : 0.03f)
                    : (overlap > 0.05f ? Mathf.Lerp(0.15f, 0.55f, Mathf.Clamp01(overlap)) : 0.08f);
                awayContribution = longitudinalAway + lateralAway * lateralScale;
            }
            else
            {
                awayContribution = longitudinalAway;
                tangentWeight = 0f;
            }
        }
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

    private static bool HasWalkableLateralSpace(Vector3 position, Vector3 desiredDirection)
    {
        if (_world == null)
            throw new InvalidOperationException("HasWalkableLateralSpace failed: world is null.");
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return false;

        Vector3 sideAxis = Mathf.Abs(desiredDirection.x) >= Mathf.Abs(desiredDirection.z)
            ? Vector3.forward
            : Vector3.right;
        return HasWalkableLaneSide(position, sideAxis) || HasWalkableLaneSide(position, -sideAxis);
    }

    private static AvoidanceEncounterType ClassifyAvoidanceEncounter(
        Vector3 desiredDirection,
        Vector3 desiredVelocity,
        Vector3 otherVelocity,
        Vector3 relativePos,
        float overlap,
        bool sameLaneFlow)
    {
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return sameLaneFlow ? AvoidanceEncounterType.SameLaneFollow : AvoidanceEncounterType.Crossing;

        Vector3 lateralOffset = Vector3.ProjectOnPlane(relativePos, desiredDirection);
        bool nearLane = lateralOffset.magnitude <= Mathf.Max(_world.CellSize * 0.75f, 0.45f);
        if (otherVelocity.sqrMagnitude <= 0.0001f)
        {
            if (!nearLane)
                return AvoidanceEncounterType.Crossing;

            float forwardOffset = Vector3.Dot(relativePos, desiredDirection);
            return forwardOffset >= 0f ? AvoidanceEncounterType.SameLaneFollow : AvoidanceEncounterType.Overtaking;
        }

        float headingDot = Vector3.Dot(desiredDirection, otherVelocity.normalized);

        if (headingDot > 0.72f && nearLane)
        {
            float forwardOffset = Vector3.Dot(relativePos, desiredDirection);
            return forwardOffset >= 0f ? AvoidanceEncounterType.SameLaneFollow : AvoidanceEncounterType.Overtaking;
        }

        if (overlap > 0.35f)
            return AvoidanceEncounterType.StaticOverlap;

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

    private static Vector3 ResolvePendingRuntimeObstacleAvoidance(
        AgentRuntimeData self,
        Vector3 desiredDirection,
        Vector3 desiredVelocity,
        float maxSpeed,
        out string debugSummary)
    {
        debugSummary = "none";
        if (self == null)
            throw new InvalidOperationException("ResolvePendingRuntimeObstacleAvoidance failed: self is null.");
        if (_activeWorldState == null || _activeWorldState.World == null)
            return Vector3.zero;
        if (_activeWorldState.RuntimeDirtyJob == null && _activeWorldState.DirtyRuntimeObstacleSectors.Count == 0)
            return Vector3.zero;
        if (desiredDirection.sqrMagnitude <= 0.0001f || desiredVelocity.sqrMagnitude <= 0.0001f || maxSpeed <= 0.0001f)
            return Vector3.zero;

        Vector3 segmentStart = self.Position;
        Vector3 segmentEnd = segmentStart + desiredDirection * Mathf.Max(_activeWorldState.World.CellSize * 2.5f, maxSpeed * Config.CrowdPredictionTime * 2f);
        float bestRisk = 0f;
        Vector3 bestAvoidance = Vector3.zero;
        string bestDebug = "none";

        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            Vector3 halfExtents = box.HalfExtents;
            halfExtents.x += self.Radius;
            halfExtents.z += self.Radius;
            if (!TryResolveSegmentBoxAvoidance(
                    segmentStart,
                    segmentEnd,
                    desiredDirection,
                    maxSpeed,
                    box.Center,
                    halfExtents,
                    out Vector3 contribution,
                    out float risk,
                    out float closestDistance,
                    out Vector3 closestPoint))
            {
                continue;
            }

            if (risk <= bestRisk)
                continue;

            bestRisk = risk;
            bestAvoidance = contribution;
            bestDebug = $"box={box.Id} risk={risk:F3} dist={closestDistance:F3} closest={closestPoint}";
        }

        foreach (CircleObstacle circle in CircleObstacles.Values)
        {
            if (!TryResolveSegmentCircleAvoidance(
                    segmentStart,
                    segmentEnd,
                    desiredDirection,
                    maxSpeed,
                    circle.Position,
                    circle.Radius + self.Radius,
                    out Vector3 contribution,
                    out float risk,
                    out float closestDistance,
                    out Vector3 closestPoint))
            {
                continue;
            }

            if (risk <= bestRisk)
                continue;

            bestRisk = risk;
            bestAvoidance = contribution;
            bestDebug = $"circle={circle.Id} risk={risk:F3} dist={closestDistance:F3} closest={closestPoint}";
        }

        debugSummary = bestDebug;
        return bestAvoidance;
    }

    private static bool TryResolveSegmentBoxAvoidance(
        Vector3 segmentStart,
        Vector3 segmentEnd,
        Vector3 desiredDirection,
        float maxSpeed,
        Vector3 center,
        Vector3 halfExtents,
        out Vector3 contribution,
        out float risk,
        out float closestDistance,
        out Vector3 closestPoint)
    {
        contribution = Vector3.zero;
        risk = 0f;
        closestDistance = float.MaxValue;
        closestPoint = segmentStart;

        Vector3 segment = segmentEnd - segmentStart;
        segment.y = 0f;
        float segmentLengthSq = segment.sqrMagnitude;
        if (segmentLengthSq <= 0.0001f)
            return false;

        Vector3 centerOffset = center - segmentStart;
        centerOffset.y = 0f;
        float t = Mathf.Clamp01(Vector3.Dot(centerOffset, segment) / segmentLengthSq);
        closestPoint = segmentStart + segment * t;
        Vector3 closestOnBox = new Vector3(
            Mathf.Clamp(closestPoint.x, center.x - halfExtents.x, center.x + halfExtents.x),
            closestPoint.y,
            Mathf.Clamp(closestPoint.z, center.z - halfExtents.z, center.z + halfExtents.z));
        Vector3 away = closestPoint - closestOnBox;
        away.y = 0f;
        closestDistance = away.magnitude;

        float influence = Mathf.Max(halfExtents.x, halfExtents.z) + Mathf.Max(0.25f, maxSpeed * Config.CrowdPredictionTime);
        if (closestDistance >= influence)
            return false;

        if (away.sqrMagnitude <= 0.0001f)
        {
            Vector3 local = closestPoint - center;
            local.y = 0f;
            float dx = halfExtents.x - Mathf.Abs(local.x);
            float dz = halfExtents.z - Mathf.Abs(local.z);
            away = dx < dz
                ? new Vector3(Mathf.Sign(local.x == 0f ? desiredDirection.x : local.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? desiredDirection.z : local.z));
            if (away.sqrMagnitude <= 0.0001f)
                away = Vector3.Cross(Vector3.up, desiredDirection);
        }

        away.Normalize();
        Vector3 tangent = Vector3.Cross(Vector3.up, away);
        if (Vector3.Dot(tangent, desiredDirection) < 0f)
            tangent = -tangent;

        risk = Mathf.Clamp01(1f - closestDistance / Mathf.Max(influence, 0.001f));
        contribution = (tangent * (0.9f * risk) - desiredDirection * (0.25f * risk)) * maxSpeed;
        return contribution.sqrMagnitude > 0.0001f;
    }

    private static bool TryResolveSegmentCircleAvoidance(
        Vector3 segmentStart,
        Vector3 segmentEnd,
        Vector3 desiredDirection,
        float maxSpeed,
        Vector3 center,
        float radius,
        out Vector3 contribution,
        out float risk,
        out float closestDistance,
        out Vector3 closestPoint)
    {
        contribution = Vector3.zero;
        risk = 0f;
        closestDistance = float.MaxValue;
        closestPoint = segmentStart;

        Vector3 segment = segmentEnd - segmentStart;
        segment.y = 0f;
        float segmentLengthSq = segment.sqrMagnitude;
        if (segmentLengthSq <= 0.0001f)
            return false;

        Vector3 centerOffset = center - segmentStart;
        centerOffset.y = 0f;
        float t = Mathf.Clamp01(Vector3.Dot(centerOffset, segment) / segmentLengthSq);
        closestPoint = segmentStart + segment * t;
        Vector3 away = closestPoint - center;
        away.y = 0f;
        closestDistance = Mathf.Max(0f, away.magnitude - radius);
        float influence = radius + Mathf.Max(0.25f, maxSpeed * Config.CrowdPredictionTime);
        if (closestDistance >= influence)
            return false;

        if (away.sqrMagnitude <= 0.0001f)
            away = Vector3.Cross(Vector3.up, desiredDirection);
        if (away.sqrMagnitude <= 0.0001f)
            return false;

        away.Normalize();
        Vector3 tangent = Vector3.Cross(Vector3.up, away);
        if (Vector3.Dot(tangent, desiredDirection) < 0f)
            tangent = -tangent;

        risk = Mathf.Clamp01(1f - closestDistance / Mathf.Max(influence, 0.001f));
        contribution = (tangent * (0.85f * risk) - desiredDirection * (0.2f * risk)) * maxSpeed;
        return contribution.sqrMagnitude > 0.0001f;
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

    private static Vector3 ResolveWalkableLaneVelocity(Vector3 position, Vector3 baseVelocity, Vector3 laneVelocity)
    {
        if (laneVelocity.sqrMagnitude <= 0.0001f)
            return laneVelocity;
        if (IsPredictedStepWalkable(position, baseVelocity + laneVelocity))
            return laneVelocity;
        if (IsPredictedStepWalkable(position, baseVelocity))
            return Vector3.zero;

        Vector3 halfLane = laneVelocity * 0.5f;
        return IsPredictedStepWalkable(position, baseVelocity + halfLane) ? halfLane : Vector3.zero;
    }

    private static Vector3 ConstrainVelocityToWalkableSteeringStep(Vector3 position, Vector3 velocity, Vector3 preferredVelocity, float maxSpeed)
    {
        if (velocity.sqrMagnitude <= 0.0001f || maxSpeed <= 0.0001f)
            return velocity;
        if (IsPredictedStepWalkable(position, velocity))
            return velocity;

        Vector3 boundaryLimited = ClampVelocityInsideCurrentWalkableCell(position, velocity);
        if (boundaryLimited.sqrMagnitude > 0.0001f && IsPredictedStepWalkable(position, boundaryLimited))
            return boundaryLimited;

        Vector3 preferredDirection = preferredVelocity;
        preferredDirection.y = 0f;
        if (preferredDirection.sqrMagnitude <= 0.0001f)
            preferredDirection = velocity;
        preferredDirection.y = 0f;
        if (preferredDirection.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        preferredDirection.Normalize();
        Vector3 forward = preferredDirection * Mathf.Min(maxSpeed, Mathf.Max(Vector3.Dot(velocity, preferredDirection), maxSpeed * WalkableSteeringCandidateMinSpeedRatio));
        if (IsPredictedStepWalkable(position, forward))
            return forward;

        Vector3 lateral = Vector3.ProjectOnPlane(velocity, preferredDirection);
        lateral.y = 0f;
        if (lateral.sqrMagnitude > 0.0001f)
        {
            Vector3 projected = Vector3.ClampMagnitude(lateral, maxSpeed);
            if (IsPredictedStepWalkable(position, projected))
                return projected;
        }

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        TrySelectWalkableSteeringCandidate(position, preferredDirection * (maxSpeed * 0.5f), preferredDirection, velocity, ref best, ref bestScore);
        TrySelectWalkableSteeringCandidate(position, preferredDirection * (maxSpeed * 0.25f), preferredDirection, velocity, ref best, ref bestScore);
        TrySelectWalkableSteeringCandidate(position, Vector3.Cross(Vector3.up, preferredDirection) * (maxSpeed * 0.35f), preferredDirection, velocity, ref best, ref bestScore);
        TrySelectWalkableSteeringCandidate(position, Vector3.Cross(preferredDirection, Vector3.up) * (maxSpeed * 0.35f), preferredDirection, velocity, ref best, ref bestScore);

        return best;
    }

    private static Vector3 ClampVelocityInsideCurrentWalkableCell(Vector3 position, Vector3 velocity)
    {
        if (_world == null)
            throw new InvalidOperationException("ClampVelocityInsideCurrentWalkableCell failed: world is null.");
        if (!_world.WorldToGrid(position, out int cellX, out int cellY) || !_world.IsWalkable(cellX, cellY))
            return velocity;

        float dt = ResolveSteeringPredictionDeltaTime();
        if (dt <= 0.0001f)
            return velocity;

        float epsilon = Mathf.Max(0.001f, _world.CellSize * 0.001f);
        float minX = _world.Origin.x + cellX * _world.CellSize + epsilon;
        float maxX = minX + _world.CellSize - epsilon * 2f;
        float minZ = _world.Origin.z + cellY * _world.CellSize + epsilon;
        float maxZ = minZ + _world.CellSize - epsilon * 2f;
        Vector3 predicted = position + velocity * dt;
        predicted.x = Mathf.Clamp(predicted.x, minX, maxX);
        predicted.z = Mathf.Clamp(predicted.z, minZ, maxZ);
        Vector3 clamped = (predicted - position) / dt;
        clamped.y = velocity.y;
        return clamped;
    }

    private static void TrySelectWalkableSteeringCandidate(
        Vector3 position,
        Vector3 candidate,
        Vector3 preferredDirection,
        Vector3 originalVelocity,
        ref Vector3 best,
        ref float bestScore)
    {
        if (candidate.sqrMagnitude <= 0.0001f || !IsPredictedStepWalkable(position, candidate))
            return;

        float score = Vector3.Dot(candidate, preferredDirection) + Vector3.Dot(candidate.normalized, originalVelocity.normalized) * 0.1f;
        if (score <= bestScore)
            return;

        bestScore = score;
        best = candidate;
    }

    private static bool IsPredictedStepWalkable(Vector3 position, Vector3 velocity)
    {
        Vector3 predicted = position + velocity * ResolveSteeringPredictionDeltaTime();
        if (!_world.WorldToGrid(position, out int fromX, out int fromY))
            return false;
        if (!_world.WorldToGrid(predicted, out int toX, out int toY))
            return false;
        if (!_world.IsWalkable(fromX, fromY))
            return false;
        if (!_world.IsWalkable(toX, toY))
            return false;
        if (fromX == toX && fromY == toY)
            return true;

        return CanTraverseNeighborCells(_world, fromX, fromY, toX, toY);
    }

    private static float ResolveSteeringPredictionDeltaTime()
    {
        return _hasTestTimeOverride ? 0.2f : Mathf.Max(0.05f, Time.deltaTime > 0f ? Time.deltaTime : 0.1f);
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

        if (!TryResolveGridEdgeData(position, desiredDirection, out edgeNormal, out edgeDistance))
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
            ? Mathf.Max(0f, -Vector3.Dot(desiredDirection, inwardNormal))
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
        MovingTargetAnchorKey anchorKey = new MovingTargetAnchorKey(targetId, rawGoalCellIndex);
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
            && !TryResolveNearbyStartWalkable(_world, self.Position, startX, startY, out startX, out startY))
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
            $"worldVersion={_world.Version} islandCount={_world.IslandCount} mainIsland={_world.MainIslandId}:{_world.MainIslandSize} {targetDiagnostics} rawGrid={FormatGridSampleDiagnostics(rawGoalPosition)} " +
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

        Vector3 startPosition = _world.GridToWorldCenter(startX, startY);
        string flowPath = BuildGridPathDiagnostics(startPosition, target.Position, _world.AgentTypeId);

        return $"target={{key={target.CharacterKey},pos={target.Position},inGrid={targetInGrid},cell=({targetX},{targetY}),island={targetIsland},main={targetIsMainIsland},rawDist={rawTargetDistance:F3},{flowPath}}}";
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

    private static void TrimCombatTargetSlotCache()
    {
        if (CombatTargetSlotCache.Count == 0)
            return;

        int expireBeforeFrame = GetFrameCount() - CombatTargetSlotCacheFrameLifetime;
        List<CombatTargetSlotKey> expiredKeys = null;
        foreach (KeyValuePair<CombatTargetSlotKey, CombatTargetSlotEntry> pair in CombatTargetSlotCache)
        {
            if (pair.Value != null && pair.Value.LastUsedFrame >= expireBeforeFrame)
                continue;

            expiredKeys ??= new List<CombatTargetSlotKey>();
            expiredKeys.Add(pair.Key);
        }

        if (expiredKeys == null)
            return;

        for (int i = 0; i < expiredKeys.Count; i++)
            CombatTargetSlotCache.Remove(expiredKeys[i]);
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
        float distanceToGoal = HorizontalDistanceXZ(self.Position, goalPosition);
        float blockingActivationDistance = Mathf.Max(requiredDistance * 4f, (_world?.CellSize ?? 1f) * 5f);
        float reservationActivationDistance = Mathf.Max(requiredDistance * 2f, (_world?.CellSize ?? 1f) * 2f);
        bool useBlockingAgents = distanceToGoal <= blockingActivationDistance;
        bool useReservations = distanceToGoal <= reservationActivationDistance;
        if (!useBlockingAgents)
        {
            return goalPosition;
        }

        if (IsNavigationGoalOccupiedByOther(selfId, ignoredTargetId, goalPosition, requiredDistance, useReservations, out int reservingAgentId))
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

                bool occupied = IsNavigationGoalOccupiedByOther(ResolveAgentId(self), targetId, resolvedWorld, requiredDistance, true, out _);
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
        bool includeReservations,
        out int reservingAgentId)
    {
        int frame = GetFrameCount();
        if (_navigationGoalReservationFrame != frame)
        {
            NavigationGoalReservations.Clear();
            _navigationGoalReservationFrame = frame;
        }

        reservingAgentId = 0;
        float reservationDistance = _world != null
            ? Mathf.Min(requiredDistance, _world.CellSize * 0.9f)
            : requiredDistance;
        float reservationDistanceSq = reservationDistance * reservationDistance;
        float bestDistance = float.PositiveInfinity;
        if (includeReservations)
        {
            for (int i = 0; i < NavigationGoalReservations.Count; i++)
            {
                NavigationGoalReservation reservation = NavigationGoalReservations[i];
                if (reservation.SelfId == selfId)
                    continue;

                Vector3 offset = reservation.Point - position;
                offset.y = 0f;
                float distanceSq = offset.sqrMagnitude;
                if (distanceSq >= reservationDistanceSq)
                    continue;

                float distance = Mathf.Sqrt(distanceSq);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                reservingAgentId = reservation.SelfId;
            }
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

        int startIsland = ResolveIslandId(world, startX, startY);
        int goalIsland = ResolveIslandId(world, goalX, goalY);
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
        string startGrid = FormatGridSampleDiagnostics(startWorld);
        string goalGrid = FormatGridSampleDiagnostics(rawGoalPosition);
        string startNeighborhood = BuildIslandNeighborhoodDiagnostics(_world, startX, startY, 2);
        string goalNeighborhood = BuildIslandNeighborhoodDiagnostics(_world, goalX, goalY, 2);

        return $"island mismatch start=({startX},{startY}) island={startIsland} startWorld={startWorld} " +
               $"goal=({goalX},{goalY}) island={goalIsland} goalWorld={goalWorld} rawGoal={rawGoalPosition} " +
               $"worldVersion={_world?.Version ?? -1} agentType={_world?.AgentTypeId ?? int.MinValue} islandCount={_world?.IslandCount ?? -1} " +
               $"runtimeDirtySectors={(_activeWorldState != null ? _activeWorldState.DirtyRuntimeObstacleSectors.Count : -1)} " +
               $"runtimeDirtyReason={_lastRuntimeObstacleDirtyReason} circleObstacles={CircleObstacles.Count} boxObstacles={BoxObstacles.Count}" +
               $"{nearestGoalIsland}{nearestStartIsland}{selfInfo}{targetInfo} startGrid={startGrid} goalGrid={goalGrid} " +
               $"startNeighborhood={startNeighborhood} goalNeighborhood={goalNeighborhood}";
    }

    private static int ResolveIslandIdForDiagnostics(NavigationWorld world, int x, int y)
    {
        return ResolveIslandId(world, x, y);
    }

    private static int ResolveIslandId(NavigationWorld world, int x, int y)
    {
        if (world == null || world.IslandIds == null || x < 0 || x >= world.Width || y < 0 || y >= world.Height)
            return -1;
        if (TryGetSectorForCell(world, x, y, out SectorData sector) && sector.UniformIslandId > 0)
            return sector.UniformIslandId;

        if (world.IslandIds.Length != world.Width * world.Height)
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
        NavigationWorld world,
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
        if (world == null)
            return $"[FlowGoalResolutionIslandDiag] world=null desired={desiredGoal}";

        int goalIsland = ResolveIslandIdForDiagnostics(world, goalX, goalY);
        Vector3 startWorld = world.GridToWorldCenter(startX, startY);
        Vector3 goalWorld = world.GridToWorldCenter(goalX, goalY);
        string fullIslandResult = fullIslandFound
            ? $"fullIslandNearest=({fullIslandX},{fullIslandY}) world={world.GridToWorldCenter(fullIslandX, fullIslandY)} dist={fullIslandDistance:F3}"
            : "fullIslandNearest=none";
        IEntityContext target = self?.TargetComp?.CurrentTarget;
        string selfInfo = self != null
            ? $"self={self.CharacterKey} side={self.Side} pos={self.Position} moveMode={(self.MoveExecutor != null ? self.MoveExecutor.MovementMode.ToString() : "null")}"
            : "self=null";
        string targetInfo = target != null
            ? $"target={target.CharacterKey} side={target.Side} pos={target.Position} moveMode={(target.MoveExecutor != null ? target.MoveExecutor.MovementMode.ToString() : "null")}"
            : "target=null";
        string startNeighborhood = BuildWalkableNeighborhoodDiagnostics(world, startX, startY, 2);
        string goalNeighborhood = BuildWalkableNeighborhoodDiagnostics(world, goalX, goalY, 2);

        return $"[FlowGoalResolutionIslandDiag] local same-island goal resolution failed desired={desiredGoal} " +
               $"start=({startX},{startY}) island={startIsland} world={startWorld} " +
               $"goal=({goalX},{goalY}) island={goalIsland} world={goalWorld} radiusCells={radiusCells} " +
               $"worldVersion={world.Version} agentType={world.AgentTypeId} islandCount={world.IslandCount} " +
               $"runtimeDirtySectors={(_activeWorldState != null ? _activeWorldState.DirtyRuntimeObstacleSectors.Count : -1)} " +
               $"runtimeDirtyReason={_lastRuntimeObstacleDirtyReason} circleObstacles={CircleObstacles.Count} boxObstacles={BoxObstacles.Count} " +
               $"{fullIslandResult} {selfInfo} {targetInfo} desiredGrid={FormatGridSampleDiagnostics(desiredGoal)} " +
               $"startGrid={FormatGridSampleDiagnostics(startWorld)} goalCellGrid={FormatGridSampleDiagnostics(goalWorld)} " +
               $"startNeighborhood={startNeighborhood} startLinks={BuildNeighborLinkDiagnostics(world, startX, startY, 2)} " +
               $"goalNeighborhood={goalNeighborhood} goalLinks={BuildNeighborLinkDiagnostics(world, goalX, goalY, 2)}";
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

                if (ResolveIslandId(world, x, y) != islandId)
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
                if (!world.IsWalkable(x, y) || ResolveIslandId(world, x, y) != islandId)
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

    private static string FormatGridSampleDiagnostics(Vector3 position)
    {
        if (_world == null)
            return "world-null";

        bool inGrid = _world.WorldToGrid(position, out int x, out int y);
        bool walkable = inGrid && _world.IsWalkable(x, y);
        int island = inGrid ? ResolveIslandIdForDiagnostics(_world, x, y) : -1;
        return $"grid={(inGrid ? $"({x},{y})" : "out")} walkable={walkable} island={island}";
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
               $"goalGridSample={FormatGridSampleDiagnostics(goalPosition)} " +
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

    private static bool TryResolveNearbyStartWalkable(NavigationWorld world, Vector3 position, int startX, int startY, out int resultX, out int resultY)
    {
        resultX = startX;
        resultY = startY;
        if (world == null)
            throw new InvalidOperationException("TryResolveNearbyStartWalkable failed: world is null.");

        float maxSnapDistance = Mathf.Max(world.CellSize * 0.45f, 0.25f);
        float bestDistanceSq = maxSnapDistance * maxSnapDistance;
        bool found = false;
        for (int y = startY - 1; y <= startY + 1; y++)
        {
            for (int x = startX - 1; x <= startX + 1; x++)
            {
                if (!world.IsWalkable(x, y))
                    continue;

                float minX = world.Origin.x + x * world.CellSize;
                float maxX = minX + world.CellSize;
                float minZ = world.Origin.z + y * world.CellSize;
                float maxZ = minZ + world.CellSize;
                float dx = position.x < minX ? minX - position.x : position.x > maxX ? position.x - maxX : 0f;
                float dz = position.z < minZ ? minZ - position.z : position.z > maxZ ? position.z - maxZ : 0f;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq > bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                resultX = x;
                resultY = y;
                found = true;
            }
        }

        return found;
    }

    private static bool HasGridLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1)
    {
        return HasGridLineOfSight(world, x0, y0, x1, y1, allowTargetSoftCost: false);
    }

    private static bool HasGridLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1, bool allowTargetSoftCost)
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

            if (GetCostFieldValueStrict(world, cx, cy) > 1
                && !(allowTargetSoftCost && cx == x1 && cy == y1))
            {
                return false;
            }

            if (!first && !CanTraverseNeighborCells(world, previousX, previousY, cx, cy))
                return false;

            if (cx == x1 && cy == y1)
                return true;

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

    private static bool HasPendingDirectLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1)
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

            int cellCost = GetCostFieldValueStrict(world, cx, cy);
            if (cellCost > 1 && !IsBoundaryOnlySoftCostCell(world, cx, cy))
                return false;

            if (!first && !CanTraverseNeighborCells(world, previousX, previousY, cx, cy))
                return false;

            if (cx == x1 && cy == y1)
                return true;

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

    private static bool HasSoftCostTolerantGridLineOfSight(NavigationWorld world, int x0, int y0, int x1, int y1, int maxAllowedCost)
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

            if (GetCostFieldValueStrict(world, cx, cy) > maxAllowedCost)
            {
                return false;
            }

            if (!first && !CanTraverseNeighborCells(world, previousX, previousY, cx, cy))
                return false;

            if (cx == x1 && cy == y1)
                return true;

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
        sector.IsClearCostField = IsSectorCostFieldClear(world, sector);
        sector.IsClearFlowTile = IsSectorClearFlowTile(world, sector);
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

        MinHeap openSet = new MinHeap();
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
                    openSet.Push(index, 0f);
                    continue;
                }

                if (IsInsideBounds(x, y, writeMinX, writeMinY, writeMaxX, writeMaxY))
                    world.CostField[index] = 1;
                if (!TouchesBlockedOrUntraversableNeighbor(world, x, y))
                    continue;

                wallDistance[index] = 1f;
                openSet.Push(index, 1f);
            }
        }

        while (openSet.Count > 0)
        {
            QueueNode node = openSet.Pop();
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
                openSet.Push(nextIndex, newDistance);
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

                int penalty = ResolveSlopeCostPenalty(world, x, y);
                float distance = wallDistance[index];
                if (!float.IsPositiveInfinity(distance) && distance <= wallCostBlurRadiusCells)
                {
                    float t = Mathf.Clamp01((wallCostBlurRadiusCells - distance) / Mathf.Max(0.001f, wallCostBlurRadiusCells - 1f));
                    penalty += Mathf.RoundToInt(Mathf.Lerp(WallCostOuterPenalty, WallCostAdjacentPenalty, t));
                }

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

    private static int ResolveSlopeCostPenalty(NavigationWorld world, int worldX, int worldY)
    {
        return 0;
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

                    if (!TryResolveCostStampCellCost(world, stamp, x, y, out byte stampCost))
                        continue;

                    world.CostField[index] = stampCost;
                }
            }
        }
    }

    private static bool TryResolveCostStampCellCost(NavigationWorld world, CostStamp stamp, int worldX, int worldY, out byte cost)
    {
        cost = stamp.Cost;
        if (stamp.Costs == null)
            return true;
        if (stamp.Width <= 0 || stamp.Height <= 0 || stamp.CellSize <= 0.0001f)
            throw new InvalidOperationException($"TryResolveCostStampCellCost failed: invalid grid stamp id={stamp.Id} size={stamp.Width}x{stamp.Height} cellSize={stamp.CellSize}.");
        if (stamp.Costs.Length != stamp.Width * stamp.Height)
            throw new InvalidOperationException($"TryResolveCostStampCellCost failed: grid stamp costs length {stamp.Costs.Length} does not match {stamp.Width}x{stamp.Height} id={stamp.Id}.");

        Vector3 center = world.GridToWorldCenter(worldX, worldY);
        int stampX = Mathf.FloorToInt((center.x - stamp.Origin.x) / stamp.CellSize);
        int stampY = Mathf.FloorToInt((center.z - stamp.Origin.z) / stamp.CellSize);
        if (stampX < 0 || stampX >= stamp.Width || stampY < 0 || stampY >= stamp.Height)
            return false;

        cost = stamp.Costs[stampX + stampY * stamp.Width];
        return true;
    }

    private static bool IsCellAffectedByCostStamp(NavigationWorld world, int worldX, int worldY)
    {
        if (CostStamps.Count == 0)
            return false;

        foreach (CostStamp stamp in CostStamps.Values)
        {
            if (stamp.AgentTypeId != AnyAgentTypeId && stamp.AgentTypeId != world.AgentTypeId)
                continue;

            int rawMinX = Mathf.FloorToInt((stamp.Bounds.min.x - world.Origin.x) / world.CellSize);
            int rawMaxX = Mathf.FloorToInt((stamp.Bounds.max.x - world.Origin.x) / world.CellSize);
            int rawMinY = Mathf.FloorToInt((stamp.Bounds.min.z - world.Origin.z) / world.CellSize);
            int rawMaxY = Mathf.FloorToInt((stamp.Bounds.max.z - world.Origin.z) / world.CellSize);
            if (worldX < rawMinX || worldX > rawMaxX || worldY < rawMinY || worldY > rawMaxY)
                continue;

            if (!world.WalkableMask[world.GetIndex(worldX, worldY)] || GetCostFieldValueStrict(world, worldX, worldY) >= 255)
                continue;

            if (TryResolveCostStampCellCost(world, stamp, worldX, worldY, out _))
                return true;
        }

        return false;
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

    private static bool HasInternalBlockedOrUntraversableNeighbor(NavigationWorld world, int centerX, int centerY)
    {
        for (int i = 0; i < NeighborOffsetX.Length; i++)
        {
            int nextX = centerX + NeighborOffsetX[i];
            int nextY = centerY + NeighborOffsetY[i];
            if (nextX < 0 || nextX >= world.Width || nextY < 0 || nextY >= world.Height)
                continue;
            if (!world.IsWalkable(nextX, nextY))
                return true;
            if (!CanTraverseNeighborCells(world, centerX, centerY, nextX, nextY))
                return true;
        }

        return false;
    }

    private static bool IsBoundaryOnlySoftCostCell(NavigationWorld world, int centerX, int centerY)
    {
        if (IsCellAffectedByCostStamp(world, centerX, centerY))
            return false;

        int radius = Mathf.CeilToInt(ResolveWallCostBlurRadiusCells(world));
        bool hasOutsideWithinRadius = false;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
                {
                    hasOutsideWithinRadius = true;
                    continue;
                }

                if (!world.IsWalkable(x, y))
                    return false;
            }
        }

        if (!hasOutsideWithinRadius)
            return false;

        return !HasInternalBlockedOrUntraversableNeighbor(world, centerX, centerY);
    }



    private static float ResolveTraversalCost(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        int fromCost = GetCostFieldValueStrict(world, fromX, fromY);
        int toCost = GetCostFieldValueStrict(world, toX, toY);
        if (fromCost >= 255 || toCost >= 255)
            return float.PositiveInfinity;

        return Mathf.Max(1f, (fromCost + toCost) * 0.5f);
    }

    private static bool IsSectorCostFieldClear(NavigationWorld world, SectorData sector)
    {
        if (world == null)
            throw new InvalidOperationException("IsSectorCostFieldClear failed: world is null.");
        if (sector == null)
            throw new InvalidOperationException("IsSectorCostFieldClear failed: sector is null.");
        if (world.CostField == null || world.CostField.Length != world.Width * world.Height)
            return true;

        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            int rowStart = y * world.Width;
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                int index = rowStart + x;
                if (world.WalkableMask[index] && world.CostField[index] != 1)
                    return false;
            }
        }

        return true;
    }

    private static bool IsSectorClearFlowTile(NavigationWorld world, SectorData sector)
    {
        if (!IsSectorCostFieldClear(world, sector))
            return false;
        if (world.NeighborTraversalMask == null || world.NeighborTraversalMask.Length != world.Width * world.Height)
            return false;

        for (int y = sector.StartY; y < sector.StartY + sector.Height; y++)
        {
            for (int x = sector.StartX; x < sector.StartX + sector.Width; x++)
            {
                if (!world.IsWalkable(x, y))
                    return false;

                for (int i = 0; i < NeighborOffsetX.Length; i++)
                {
                    int nextX = x + NeighborOffsetX[i];
                    int nextY = y + NeighborOffsetY[i];
                    if (!IsInsideSector(sector, nextX, nextY))
                        continue;
                    if (!CanTraverseNeighborCells(world, x, y, nextX, nextY))
                        return false;
                }
            }
        }

        return true;
    }

    private static bool TryGetSectorForCell(NavigationWorld world, int worldX, int worldY, out SectorData sector)
    {
        sector = null;
        if (world == null || world.Sectors == null)
            return false;
        if (!world.TryGetSectorId(worldX, worldY, out int sectorId))
            return false;

        sector = world.Sectors[sectorId];
        return sector != null;
    }

    private static bool AreSectorsSameOrAdjacent(NavigationWorld world, int sectorAId, int sectorBId)
    {
        if (world == null)
            throw new InvalidOperationException("AreSectorsSameOrAdjacent failed: world is null.");
        if (world.SectorCountX <= 0)
            throw new InvalidOperationException($"AreSectorsSameOrAdjacent failed: invalid sector grid countX={world.SectorCountX}.");
        if (sectorAId < 0 || sectorAId >= world.Sectors.Length || sectorBId < 0 || sectorBId >= world.Sectors.Length)
            return false;

        int ax = sectorAId % world.SectorCountX;
        int ay = sectorAId / world.SectorCountX;
        int bx = sectorBId % world.SectorCountX;
        int by = sectorBId / world.SectorCountX;
        return Mathf.Abs(ax - bx) <= 1 && Mathf.Abs(ay - by) <= 1;
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

    private static bool TryGetNavigationQueryWorld(int agentTypeId, bool allowSynchronousBuild, out NavigationWorld world)
    {
        world = null;
        try
        {
            if (!TryEnsureWorldBuilt(agentTypeId, allowSynchronousBuild))
                return false;
        }
        catch (InvalidOperationException exception)
        {
            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
                Debug.LogWarning($"[FlowNavigationQuery] world unavailable agentType={agentTypeId}: {exception.Message}");
            return false;
        }

        WorldRuntimeState state = _activeWorldState;
        if (state == null || state.World == null)
            return false;

        world = HasPendingRuntimeDirty(state) ? ResolveReachabilityQueryWorld(state) : state.World;
        return world != null;
    }

    private static bool IsNavigationSegmentWalkable(NavigationWorld world, Vector3 position, Vector3 horizontalDisplacement)
    {
        if (world == null)
            return false;
        if (!world.WorldToGrid(position, out int previousX, out int previousY) || !world.IsWalkable(previousX, previousY))
            return false;

        Vector3 target = position + horizontalDisplacement;
        if (!world.WorldToGrid(target, out int targetX, out int targetY) || !world.IsWalkable(targetX, targetY))
            return false;

        float distance = horizontalDisplacement.magnitude;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(world.CellSize * 0.45f, 0.001f)));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 sample = position + horizontalDisplacement * (i / (float)steps);
            if (!world.WorldToGrid(sample, out int x, out int y) || !world.IsWalkable(x, y))
                return false;

            if (x == previousX && y == previousY)
                continue;

            int dx = Math.Abs(x - previousX);
            int dy = Math.Abs(y - previousY);
            if (dx > 1 || dy > 1)
            {
                if (!IsNavigationCellTraceWalkable(world, previousX, previousY, x, y))
                    return false;
            }
            else if (!CanTraverseNeighborCells(world, previousX, previousY, x, y))
            {
                return false;
            }

            previousX = x;
            previousY = y;
        }

        return true;
    }

    private static bool IsNavigationCellTraceWalkable(NavigationWorld world, int fromX, int fromY, int toX, int toY)
    {
        int currentX = fromX;
        int currentY = fromY;
        int steps = Mathf.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY));
        if (steps <= 0)
            return true;

        for (int i = 1; i <= steps; i++)
        {
            int nextX = Mathf.RoundToInt(Mathf.Lerp(fromX, toX, i / (float)steps));
            int nextY = Mathf.RoundToInt(Mathf.Lerp(fromY, toY, i / (float)steps));
            if (nextX == currentX && nextY == currentY)
                continue;
            if (!CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                return false;

            currentX = nextX;
            currentY = nextY;
        }

        return true;
    }

    private static void TrySelectNavigationDisplacementCandidate(
        NavigationWorld world,
        Vector3 position,
        Vector3 candidate,
        Vector3 desired,
        float desiredDistance,
        ref Vector3 best,
        ref float bestScore,
        bool preserveDistance = true)
    {
        candidate.y = 0f;
        if (candidate.sqrMagnitude <= 0.000001f)
            return;

        if (preserveDistance)
            candidate = candidate.normalized * desiredDistance;

        if (!IsNavigationSegmentWalkable(world, position, candidate))
            return;

        Vector3 desiredDirection = desired.sqrMagnitude > 0.000001f ? desired.normalized : Vector3.zero;
        float score = Vector3.Dot(candidate, desiredDirection) + candidate.magnitude * 0.05f;
        if (score <= bestScore)
            return;

        bestScore = score;
        best = candidate;
    }

    private static Vector3 FindLongestWalkablePrefix(NavigationWorld world, Vector3 position, Vector3 desired)
    {
        Vector3 best = Vector3.zero;
        float low = 0f;
        float high = 1f;
        for (int i = 0; i < 7; i++)
        {
            float mid = (low + high) * 0.5f;
            Vector3 candidate = desired * mid;
            if (IsNavigationSegmentWalkable(world, position, candidate))
            {
                best = candidate;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return best;
    }

    private static bool IsNavigationCellClear(NavigationWorld world, int cellX, int cellY, float edgeClearance)
    {
        if (!world.IsWalkable(cellX, cellY))
            return false;
        if (edgeClearance <= 0.0001f)
            return true;

        float clearanceSq = edgeClearance * edgeClearance;
        Vector3 center = world.GridToWorldCenter(cellX, cellY);
        int radius = Mathf.CeilToInt(edgeClearance / Mathf.Max(world.CellSize, 0.001f)) + 1;
        for (int y = cellY - radius; y <= cellY + radius; y++)
        {
            for (int x = cellX - radius; x <= cellX + radius; x++)
            {
                if (world.IsWalkable(x, y))
                    continue;

                float nearestX = Mathf.Clamp(center.x, world.Origin.x + x * world.CellSize, world.Origin.x + (x + 1) * world.CellSize);
                float nearestZ = Mathf.Clamp(center.z, world.Origin.z + y * world.CellSize, world.Origin.z + (y + 1) * world.CellSize);
                float dx = nearestX - center.x;
                float dz = nearestZ - center.z;
                if (dx * dx + dz * dz < clearanceSq)
                    return false;
            }
        }

        return true;
    }

    private static bool TryEstimateGridPathDistance(NavigationWorld world, int startX, int startY, int goalX, int goalY, out float distance)
    {
        distance = 0f;
        int cellCount = world.Width * world.Height;
        float[] costs = new float[cellCount];
        bool[] closed = new bool[cellCount];
        for (int i = 0; i < costs.Length; i++)
            costs[i] = float.PositiveInfinity;

        List<int> open = new List<int>(128);
        int startIndex = world.GetIndex(startX, startY);
        int goalIndex = world.GetIndex(goalX, goalY);
        costs[startIndex] = 0f;
        open.Add(startIndex);

        while (open.Count > 0)
        {
            int bestOpenSlot = 0;
            float bestPriority = float.PositiveInfinity;
            for (int i = 0; i < open.Count; i++)
            {
                int index = open[i];
                int x = index % world.Width;
                int y = index / world.Width;
                float priority = costs[index] + Vector2.Distance(new Vector2(x, y), new Vector2(goalX, goalY)) * world.CellSize;
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    bestOpenSlot = i;
                }
            }

            int currentIndex = open[bestOpenSlot];
            open[bestOpenSlot] = open[open.Count - 1];
            open.RemoveAt(open.Count - 1);
            if (currentIndex == goalIndex)
            {
                distance = costs[currentIndex];
                return true;
            }

            if (closed[currentIndex])
                continue;

            closed[currentIndex] = true;
            int currentX = currentIndex % world.Width;
            int currentY = currentIndex / world.Width;
            for (int i = 0; i < NeighborOffsetX.Length; i++)
            {
                int nextX = currentX + NeighborOffsetX[i];
                int nextY = currentY + NeighborOffsetY[i];
                if (!CanTraverseNeighborCells(world, currentX, currentY, nextX, nextY))
                    continue;

                int nextIndex = world.GetIndex(nextX, nextY);
                if (closed[nextIndex])
                    continue;

                float stepCost = (NeighborOffsetX[i] != 0 && NeighborOffsetY[i] != 0) ? world.CellSize * 1.41421356f : world.CellSize;
                float nextCost = costs[currentIndex] + stepCost;
                if (nextCost >= costs[nextIndex])
                    continue;

                costs[nextIndex] = nextCost;
                open.Add(nextIndex);
            }
        }

        return false;
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

        string probeDiagnostics = worldInGrid
            ? BuildCellProbeDiagnostics(_world, worldX, worldY)
            : "probes=out";
        string neighborhoodDiagnostics = worldInGrid
            ? BuildWalkableNeighborhoodDiagnostics(_world, worldX, worldY, 2)
            : "neighborhood=out";
        string nearestSearchDiagnostics = worldInGrid
            ? BuildNearestWalkableSearchDiagnostics(_world, worldX, worldY, 6)
            : "nearestSearch=out";

        return $"diag=agentType={_world.AgentTypeId} cellSize={_world.CellSize:F3} origin={_world.Origin} size={_world.Width}x{_world.Height} " +
               $"worldCell={(worldInGrid ? $"({worldX},{worldY})" : "out")} worldWalkable={worldWalkable} " +
               $"baseWalkable={baseWalkable} fogCell={(fogInGrid ? $"({fogX},{fogY})" : "out")} fogWalkable={fogWalkable} " +
               $"fogState={fogState} fogVis={fogVisibility:F2} " +
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

    private static string BuildExecutorGridSegmentDiagnostic(Vector3 position, Vector3 desiredHorizontalDisplacement, Vector3 inputVelocity)
    {
        if (desiredHorizontalDisplacement.sqrMagnitude <= 0.000001f)
            return "executorGrid=zero-displacement";
        if (_world == null)
            return "executorGrid=world-null";

        Vector3 end = position + desiredHorizontalDisplacement;
        bool startInGrid = _world.WorldToGrid(position, out int startX, out int startY);
        bool endInGrid = _world.WorldToGrid(end, out int endX, out int endY);
        bool startWalkable = startInGrid && _world.IsWalkable(startX, startY);
        bool endWalkable = endInGrid && _world.IsWalkable(endX, endY);
        bool clear = startInGrid && endInGrid && HasGridLineOfSight(_world, startX, startY, endX, endY);
        string trace = startInGrid && endInGrid
            ? BuildLineOfSightCellTrace(_world, startX, startY, endX, endY)
            : "trace=out";

        Vector3 edgeNormal = Vector3.zero;
        float edgeDistance = 0f;
        bool hasEdge = TryResolveGridEdgeData(position, desiredHorizontalDisplacement, out edgeNormal, out edgeDistance);
        float intoBoundary = hasEdge ? Vector3.Dot(inputVelocity, -edgeNormal) : 0f;
        return $"executorGrid={{start=({(startInGrid ? startX.ToString() : "out")},{(startInGrid ? startY.ToString() : "out")}) end=({(endInGrid ? endX.ToString() : "out")},{(endInGrid ? endY.ToString() : "out")}) startWalk={startWalkable} endWalk={endWalkable} clear={clear} edgeNormal={edgeNormal} edgeDist={edgeDistance:F3} intoBoundary={intoBoundary:F3} {trace}}}";
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
        string executorGrid = BuildExecutorGridSegmentDiagnostic(
            currentPosition,
            desiredHorizontalDisplacement,
            inputVelocity);

        Debug.LogWarning(
            $"[FlowConstraintDiagMissingAgent] reason={executorReason} key={self.CharacterKey} expectedId={expectedAgentId} " +
            $"selfType={self.GetType().Name} side={self.Side} pos={currentPosition} selfPos={self.Position} " +
            $"agentType={(self is MAEntity maEntity ? maEntity.navAgentTypeID.ToString() : "unknown")} " +
            $"inputVelocity={inputVelocity} desiredDisp={desiredHorizontalDisplacement} {executorGrid} " +
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

    private static int ResolveExplicitAgentTypeId(IEntityContext entity, string caller)
    {
        if (entity == null)
            throw new InvalidOperationException($"{caller} failed: entity is null.");
        if (entity is MAEntity maEntity)
        {
            if (maEntity.navAgentTypeID == MAEntity.UnknownNavAgentTypeId)
            {
                throw new InvalidOperationException(
                    $"{caller} failed: MAEntity '{entity.CharacterKey}' has Unknown navAgentTypeID. Runtime flow worlds require an explicit movement type.");
            }

            return maEntity.navAgentTypeID;
        }

        return 0;
    }

    private static int ResolvePreferredAgentTypeId(int preferredAgentTypeId)
    {
        if (preferredAgentTypeId != MAEntity.UnknownNavAgentTypeId)
            return preferredAgentTypeId;

        throw new InvalidOperationException("ResolvePreferredAgentTypeId failed: preferred agent type is Unknown. Runtime navigation must pass an explicit agent type.");
    }

    private static float ResolveAgentTypeRadius(int agentTypeId)
    {
#if UNITY_EDITOR
        if (TestAgentTypeRadii.TryGetValue(agentTypeId, out float testRadius))
            return testRadius;
#endif

        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            return 0.5f;

        if (agentTypeId == AgentTypeHelper.SmallMovementTypeId)
            return 0.35f;
        if (agentTypeId == AgentTypeHelper.MediumMovementTypeId)
            return 0.5f;
        if (agentTypeId == AgentTypeHelper.LargeMovementTypeId)
            return 0.75f;

        return 0.5f;
    }

    private static float ResolveCollisionRadius(IEntityContext entity)
    {
        float radius = DistanceUnitConverter.ConvertToWorldFloat(entity.GetProperty(CreatureMainProperty.CollisionRadius));
        return radius > 0.0001f ? radius : 0.5f;
    }
}
