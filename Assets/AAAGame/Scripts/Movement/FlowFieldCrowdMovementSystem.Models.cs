using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AAAGame.FlowPath;
using AAAGame.MiniMap.FOG3;
using GameFramework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    public enum NavigationQueryFailureKind
    {
        None = 0,
        PendingRuntimeUpdate = 1,
        Unavailable = 2,
        Unreachable = 3
    }

    private const int AnyAgentTypeId = int.MinValue + 1;
    private const float MaxAgentAvoidBackwardSpeedRatio = 0.35f;
    private const float MaxAgentAvoidForwardSpeedRatio = 0f;
    private const float MaxAgentAvoidLateralSpeedRatio = 0.75f;
    private const float EdgeRecoveryMinSpeedRatio = 0.28f;
    private const float WalkableSteeringCandidateMinSpeedRatio = 0.25f;
    private const int NavigationGoalCandidateCount = 16;
    private const int NavigationGoalRingCount = 3;
    private static readonly Fix64 NavigationGoalOccupancyPadding = Fix64.FromRaw(1434);
    private const int CombatTargetSlotCacheFrameLifetime = 120;
    private const float IntegrationSignificantImprovement = 0.05f;
    private const byte FlowDirectionMask = 0x0F;
    private const byte FlowPathableFlag = 1 << 6;
    private const byte FlowReachableFlag = 1 << 7;
    private static readonly long FlowPerfLogThresholdTicks = Stopwatch.Frequency * 4 / 1000;
    private const int WallCostAdjacentPenalty = 1;
    private const int WallCostOuterPenalty = 0;
    private const int PortalWindowSplitCostDelta = 8;
    private const float PortalBaseCrossingCost = 1f;
    private const long DeterministicPortalCostScale = 4096L;
    private const long DeterministicPortalDiagonalCost = 5793L;
    private const long WallCostBlurRadiusRaw = 9L * DeterministicPortalCostScale / 4L;
    private const long DeterministicPortalCrossingCost = DeterministicPortalCostScale;
    private const int FlowHeavyDiagnosticCooldownFrames = 90;
    private const int FlowSuccessfulMoveDiagnosticCooldownFrames = 90;
    private const int FlowPendingPortalTraceThresholdFrames = 12;
    private const int FlowPendingPortalTraceCooldownFrames = 30;
    private const int FlowTileQueueTraceThresholdFrames = 8;
    private const int FlowTileQueueTraceCooldownFrames = 30;
    private const int MaxSuccessfulMoveDiagnosticsPerFrame = 3;
    private const int MaxHeavySteeringDiagnosticsPerFrame = 1;
    private const int MaxNavigationStuckDiagnosticsPerFrame = 2;
    private const int MaxRuntimeArrayPoolEntriesPerLength = 8;
    private const int RuntimeDirtyPortalTransitionSourceQuota = 128;
    private const float NavigationConstraintMinDirectionDot = 0.18f;
    private const float AgentSpatialBucketMinWorldSize = 0.75f;
    private const float AgentSpatialBucketMaxWorldSize = 2.0f;

    private sealed class RuntimeConfig
    {
        public int SectorWorldSizeMillimeters = 3600;
#if UNITY_EDITOR
        public int EditorTestSectorSizeInCells;
#endif
        public int PortalNarrowWidthCells = 2;
        public int PortalMaxWindowWidthCells = 6;
        public int FlowTileCacheLimit = 256;
        public int WorldBuildOperationQuota = 32768;
        public int RuntimeRebuildOperationQuota = 512;
        public int DeterministicFlowTileCommitQuota = 8;
        public int FlowTileBuildOperationQuota = 2048;
        public int SharedGoalBuildOperationQuota = 2048;
        public int MovingTargetProjectionOperationQuota = 2048;
        public int PathRequestOperationQuota = 512;
        public bool RequireAuthoredNavigationSource = true;
        public bool EnableDeterministicStaticCollisionShadow = true;
        public float StaticCollisionShadowMismatchTolerance = 0.03f;
        public int StaticCollisionShadowLogIntervalTicks = 300;
        public bool DrawNavigationDebug = false;
        public bool DrawFlowFieldDebug;
        public bool StrictNoFallback = true;
    }

    // Cost and traversal inputs are immutable for one committed world version.
    // Tile workers share this backing rather than materializing a sector snapshot per request.
    private sealed class FlowTileSectorInputBacking : IDisposable
    {
        public NativeArray<bool> Walkable;
        public NativeArray<byte> TraversalMask;
        public NativeArray<byte> CellCosts;
        public int ReferenceCount = 1;

        public void Acquire()
        {
            if (ReferenceCount <= 0)
                throw new InvalidOperationException("Cannot acquire a disposed flow tile sector input backing.");
            ReferenceCount = checked(ReferenceCount + 1);
        }

        public void ReleaseOwner()
        {
            Release();
        }

        public void Release()
        {
            if (ReferenceCount <= 0)
                throw new InvalidOperationException("Flow tile sector input backing reference was released twice.");
            ReferenceCount--;
            if (ReferenceCount == 0)
                Dispose();
        }

        public void Dispose()
        {
            if (Walkable.IsCreated) Walkable.Dispose();
            if (TraversalMask.IsCreated) TraversalMask.Dispose();
            if (CellCosts.IsCreated) CellCosts.Dispose();
        }
    }

    private sealed class NavigationWorld
    {
        private const int GridFractionalPlaces = 32;
        private const int GridToFixShift = GridFractionalPlaces - Fix64.FRACTIONAL_PLACES;

        public bool HasDeterministicContentHash;
        public ulong DeterministicContentHash;
        public bool HasImmutableContentHash;
        public ulong ImmutableContentHash;
        public int Version;
        public int AgentTypeId;
        public int Width;
        public int Height;
        public float CellSize;
        public float EncodedCenterClearance;
        public Vector3 Origin;
        public long CellSizeGridRaw;
        public long EncodedCenterClearanceFixedRaw;
        public long AgentRadiusFixedRaw;
        public long OriginXGridRaw;
        public long OriginZGridRaw;
        public bool HasAuthorityGridMetadata;
        public bool[] BaseWalkableMask;
        public byte[] BaseNeighborTraversalMask;
        public FixVector2[] StaticCollisionVertices;
        public int[] StaticCollisionPathStarts;
        public byte[] SourceCostField;
        public bool[] WalkableMask;
        public byte[] CostField;
        public byte[][] SectorCostFields;
        public Vector3[] CellNavAnchors;
        public FixVector2[] CellNavAnchorsFixedXZ;
        public byte[] NeighborTraversalMask;
        public FlowTileSectorInputBacking[] FlowTileSectorInputBackings;
        public int[] IslandIds;
        public int IslandCount;
        public int MainIslandId;
        public int MainIslandSize;
        public int SectorSizeInCells;
        public int SectorCountX;
        public int SectorCountY;
        public SectorData[] Sectors;
        // World-derived accelerator for exact reachable-goal projection. Its bucket
        // contents are immutable after publication; query scratch is consumer-local.
        public GoalProjectionSpatialIndex GoalProjectionSpatialIndex;
        public PortalData[] Portals;
        public FlowPathKernelGraphIndex L0SearchGraphIndex;
        public Dictionary<int, PortalData> PortalsById;
        public Dictionary<PortalSignature, int> PortalIdsBySignature = new Dictionary<PortalSignature, int>();
        public HashSet<int> UsedPortalIds = new HashSet<int>();
        public int NextPortalId;
        public PortalHierarchy Hierarchy;

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
            float height = Origin.y;
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                int index = GetIndex(x, y);
                if (WalkableMask != null
                    && index >= 0
                    && index < WalkableMask.Length
                    && WalkableMask[index]
                    && CellNavAnchors != null
                    && index < CellNavAnchors.Length
                    && IsFinite(CellNavAnchors[index])
                    && CellNavAnchorsFixedXZ != null
                    && index < CellNavAnchorsFixedXZ.Length
                    && IsAnchorInsideCell(CellNavAnchorsFixedXZ[index], x, y))
                {
                    height = CellNavAnchors[index].y;
                }
            }

            FixVector2 center = GridToWorldCenterFixed(x, y);
            return new Vector3((float)center.x, height, (float)center.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                   && !float.IsNaN(value.y)
                   && !float.IsNaN(value.z)
                   && !float.IsInfinity(value.x)
                   && !float.IsInfinity(value.y)
                   && !float.IsInfinity(value.z);
        }

        public void FreezeAuthorityGridMetadata()
        {
            FreezeAuthorityGridMetadata(ResolveAgentTypeRadiusFixed(AgentTypeId).RawValue);
        }

        public void FreezeAuthorityGridMetadata(long agentRadiusFixedRaw)
        {
            CellSizeGridRaw = FloatToGridRaw(CellSize);
            if (CellSizeGridRaw <= 0)
                throw new InvalidOperationException("NavigationWorld authority cell size must be positive.");
            EncodedCenterClearanceFixedRaw = ((Fix64)EncodedCenterClearance).RawValue;
            if (agentRadiusFixedRaw <= 0)
                throw new InvalidOperationException("NavigationWorld authority agent radius must be positive.");
            AgentRadiusFixedRaw = agentRadiusFixedRaw;
            OriginXGridRaw = FloatToGridRaw(Origin.x);
            OriginZGridRaw = FloatToGridRaw(Origin.z);
            HasAuthorityGridMetadata = true;
        }

        public void SetAuthorityGridMetadata(
            long cellSizeGridRaw,
            long encodedCenterClearanceFixedRaw,
            long agentRadiusFixedRaw,
            long originXGridRaw,
            long originZGridRaw)
        {
            if (cellSizeGridRaw <= 0)
                throw new InvalidOperationException("NavigationWorld authority cell size must be positive.");
            if (agentRadiusFixedRaw <= 0)
                throw new InvalidOperationException("NavigationWorld authority agent radius must be positive.");

            CellSizeGridRaw = cellSizeGridRaw;
            EncodedCenterClearanceFixedRaw = encodedCenterClearanceFixedRaw;
            AgentRadiusFixedRaw = agentRadiusFixedRaw;
            OriginXGridRaw = originXGridRaw;
            OriginZGridRaw = originZGridRaw;
            HasAuthorityGridMetadata = true;
        }

        public void CopyAuthorityGridMetadataFrom(NavigationWorld source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            source.ValidateAuthorityGridMetadata("NavigationWorld.CopyAuthorityGridMetadataFrom");
            CellSizeGridRaw = source.CellSizeGridRaw;
            EncodedCenterClearanceFixedRaw = source.EncodedCenterClearanceFixedRaw;
            AgentRadiusFixedRaw = source.AgentRadiusFixedRaw;
            OriginXGridRaw = source.OriginXGridRaw;
            OriginZGridRaw = source.OriginZGridRaw;
            HasAuthorityGridMetadata = true;
        }

        public void ValidateAuthorityGridMetadata(string caller)
        {
            if (!HasAuthorityGridMetadata || CellSizeGridRaw <= 0 || AgentRadiusFixedRaw <= 0)
                throw new InvalidOperationException($"{caller} encountered a world without frozen authority grid metadata.");
        }

        public Fix64 CellSizeFixed
        {
            get
            {
                ValidateAuthorityGridMetadata("NavigationWorld.CellSizeFixed");
                return GridRawToFix64(CellSizeGridRaw);
            }
        }

        public Fix64 EncodedCenterClearanceFixed => Fix64.FromRaw(EncodedCenterClearanceFixedRaw);

        public Fix64 AgentRadiusFixed => Fix64.FromRaw(AgentRadiusFixedRaw);

        public FixVector2 OriginFixed
        {
            get
            {
                ValidateAuthorityGridMetadata("NavigationWorld.OriginFixed");
                return new FixVector2(GridRawToFix64(OriginXGridRaw), GridRawToFix64(OriginZGridRaw));
            }
        }

        private bool IsAnchorInsideCell(FixVector2 anchor, int x, int y)
        {
            GetGridCellBoundsFixed(x, y, out FixVector2 minimum, out FixVector2 maximum);
            Fix64 epsilon = Fix64.Max(Fix64.FromRaw(1), GridRawToFix64(CellSizeGridRaw) * Fix64.FromRaw(5));
            return anchor.x > minimum.x + epsilon
                   && anchor.x < maximum.x - epsilon
                   && anchor.y > minimum.y + epsilon
                   && anchor.y < maximum.y - epsilon;
        }

        public bool WorldToGrid(Vector3 position, out int x, out int y)
        {
            if (!IsFinite(position))
                throw new ArgumentOutOfRangeException(nameof(position), position, "Navigation position must be finite.");

            return WorldToGridFixed(new FixVector2((Fix64)position.x, (Fix64)position.z), out x, out y);
        }

        public bool WorldToGridFixed(FixVector2 position, out int x, out int y)
        {
            ValidateAuthorityGridMetadata("NavigationWorld.WorldToGridFixed");

            long positionXRaw = checked(position.x.RawValue << GridToFixShift);
            long positionYRaw = checked(position.y.RawValue << GridToFixShift);
            x = FloorDivRaw(checked(positionXRaw - OriginXGridRaw), CellSizeGridRaw);
            y = FloorDivRaw(checked(positionYRaw - OriginZGridRaw), CellSizeGridRaw);
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public FixVector2 GridToWorldCenterFixed(int x, int y)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                int index = GetIndex(x, y);
                if (WalkableMask != null
                    && index >= 0
                    && index < WalkableMask.Length
                    && WalkableMask[index]
                    && CellNavAnchorsFixedXZ != null
                    && index < CellNavAnchorsFixedXZ.Length
                    && IsAnchorInsideCell(CellNavAnchorsFixedXZ[index], x, y))
                {
                    return CellNavAnchorsFixedXZ[index];
                }
            }

            ValidateAuthorityGridMetadata("NavigationWorld.GridToWorldCenterFixed");
            return new FixVector2(
                GridRawToFix64(checked(OriginXGridRaw + checked((long)x * CellSizeGridRaw) + CellSizeGridRaw / 2)),
                GridRawToFix64(checked(OriginZGridRaw + checked((long)y * CellSizeGridRaw) + CellSizeGridRaw / 2)));
        }

        public FixVector2 GridToWorldGeometricCenterFixed(int x, int y)
        {
            ValidateAuthorityGridMetadata("NavigationWorld.GridToWorldGeometricCenterFixed");
            return NavigationGridFixedMath.GridCellCenterFixed(
                CellSizeGridRaw,
                OriginXGridRaw,
                OriginZGridRaw,
                x,
                y);
        }

        public void GetGridCellBoundsFixed(int x, int y, out FixVector2 minimum, out FixVector2 maximum)
        {
            ValidateAuthorityGridMetadata("NavigationWorld.GetGridCellBoundsFixed");
            long minimumXRaw = checked(OriginXGridRaw + checked((long)x * CellSizeGridRaw));
            long minimumYRaw = checked(OriginZGridRaw + checked((long)y * CellSizeGridRaw));
            minimum = new FixVector2(GridRawToFix64(minimumXRaw), GridRawToFix64(minimumYRaw));
            maximum = new FixVector2(
                GridRawToFix64(checked(minimumXRaw + CellSizeGridRaw)),
                GridRawToFix64(checked(minimumYRaw + CellSizeGridRaw)));
        }

        internal static long FloatToGridRaw(float value)
        {
            return NavigationGridFixedMath.FloatToGridRaw(value);
        }

        private static Fix64 GridRawToFix64(long value)
        {
            return NavigationGridFixedMath.GridRawToFix64(value);
        }

        public static int FloorDivRaw(long numerator, long positiveDenominator)
        {
            if (positiveDenominator <= 0)
                throw new ArgumentOutOfRangeException(nameof(positiveDenominator));
            long quotient = numerator / positiveDenominator;
            if (numerator < 0 && numerator % positiveDenominator != 0)
                quotient--;
            return checked((int)quotient);
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
        public SectorData()
        {
            PortalIds = new List<int>(8);
            PortalTransitions = new List<PortalTransition>(16);
            IncomingPortalTransitionsByToPortalId = new Dictionary<int, List<PortalTransition>>(8);
            OutgoingPortalTransitionsByFromPortalId = new Dictionary<int, List<PortalTransition>>(8);
        }

        public SectorData(SectorData sharedPortalSource)
        {
            if (sharedPortalSource == null)
                throw new ArgumentNullException(nameof(sharedPortalSource));

            PortalIds = sharedPortalSource.PortalIds;
            PortalTransitions = sharedPortalSource.PortalTransitions;
            IncomingPortalTransitionsByToPortalId = sharedPortalSource.IncomingPortalTransitionsByToPortalId;
            OutgoingPortalTransitionsByFromPortalId = sharedPortalSource.OutgoingPortalTransitionsByFromPortalId;
        }

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
        // Runtime island authority is sector-local components plus this compact
        // component-to-global-island mapping. The full IslandIds array remains
        // only as an export/legacy compatibility cache.
        public int[] LocalComponentIslandIds;
        public int[] LocalComponentSizes;
        public int[] LocalComponentMinCellIndices;
        public bool HasDeterministicContentHash;
        public ulong DeterministicContentHash;
        public readonly List<int> PortalIds;
        public readonly List<PortalTransition> PortalTransitions;
        public readonly Dictionary<int, List<PortalTransition>> IncomingPortalTransitionsByToPortalId;
        public readonly Dictionary<int, List<PortalTransition>> OutgoingPortalTransitionsByFromPortalId;
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
        public long DeterministicCost = long.MaxValue;
    }

    private enum AnalyticPortalAccessRejectReason
    {
        None = 0,
        MultipleLocalComponents = 1,
        NonClearSector = 2
    }

    private struct AnalyticPortalAccessDiagnostics
    {
        public bool IsClearSector;
        public AnalyticPortalAccessRejectReason RejectReason;
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

    private sealed class ImmutableRouteSequence : IEnumerable<int>
    {
        private const ulong HashPrime = 1099511628211UL;
        private const ulong ValueSalt = 0x9E3779B97F4A7C15UL;

        private sealed class Segment
        {
            public Segment(int[] values)
            {
                Values = values ?? throw new ArgumentNullException(nameof(values));
                PrefixHashes = new ulong[values.Length + 1];
                Powers = new ulong[values.Length + 1];
                Powers[0] = 1UL;
                for (int i = 0; i < values.Length; i++)
                {
                    PrefixHashes[i + 1] = unchecked(
                        PrefixHashes[i] * HashPrime + unchecked((uint)values[i]) + ValueSalt);
                    Powers[i + 1] = unchecked(Powers[i] * HashPrime);
                }
            }

            public int[] Values { get; }
            public ulong[] PrefixHashes { get; }
            public ulong[] Powers { get; }

            public ulong GetSliceHash(int offset, int count)
            {
                return unchecked(PrefixHashes[offset + count] - PrefixHashes[offset] * Powers[count]);
            }
        }

        private readonly struct Slice
        {
            public Slice(Segment segment, int offset, int count)
            {
                Segment = segment ?? throw new ArgumentNullException(nameof(segment));
                if (offset < 0 || count < 0 || offset + count > segment.Values.Length)
                    throw new ArgumentOutOfRangeException(nameof(offset), "Immutable route slice is outside its segment.");
                Offset = offset;
                Count = count;
            }

            public Segment Segment { get; }
            public int Offset { get; }
            public int Count { get; }
        }

        private static readonly ImmutableRouteSequence Empty = new ImmutableRouteSequence(Array.Empty<Slice>());
        private readonly Slice[] m_Slices;

        private ImmutableRouteSequence(Slice[] slices)
        {
            m_Slices = slices ?? throw new ArgumentNullException(nameof(slices));
            int length = 0;
            ulong hash = 0UL;
            for (int i = 0; i < slices.Length; i++)
            {
                Slice slice = slices[i];
                length = checked(length + slice.Count);
                hash = unchecked(hash * slice.Segment.Powers[slice.Count]
                                 + slice.Segment.GetSliceHash(slice.Offset, slice.Count));
            }
            Length = length;
            AuthorityContentHash = hash;
        }

        public int Length { get; }
        public int SliceCount => m_Slices.Length;
        public ulong AuthorityContentHash { get; }

        public int this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length)
                    throw new ArgumentOutOfRangeException(nameof(index));
                int remaining = index;
                for (int i = 0; i < m_Slices.Length; i++)
                {
                    Slice slice = m_Slices[i];
                    if (remaining < slice.Count)
                        return slice.Segment.Values[slice.Offset + remaining];
                    remaining -= slice.Count;
                }
                throw new InvalidOperationException("Immutable route sequence index resolution failed.");
            }
        }

        public static implicit operator ImmutableRouteSequence(int[] values)
        {
            return FromArray(values);
        }

        public static ImmutableRouteSequence FromArray(int[] values)
        {
            if (values == null)
                return null;
            return values.Length == 0
                ? Empty
                : new ImmutableRouteSequence(new[] { new Slice(new Segment(values), 0, values.Length) });
        }

        public static ImmutableRouteSequence Concat(
            int[] prefix,
            int prefixCount,
            ImmutableRouteSequence suffix,
            int suffixStart)
        {
            if (prefix == null)
                throw new ArgumentNullException(nameof(prefix));
            if (prefixCount < 0 || prefixCount > prefix.Length)
                throw new ArgumentOutOfRangeException(nameof(prefixCount));
            if (suffix == null)
                throw new ArgumentNullException(nameof(suffix));
            if (suffixStart < 0 || suffixStart > suffix.Length)
                throw new ArgumentOutOfRangeException(nameof(suffixStart));

            var slices = new List<Slice>(suffix.m_Slices.Length + 1);
            if (prefixCount > 0)
                slices.Add(new Slice(new Segment(prefix), 0, prefixCount));
            AppendSlices(slices, suffix, suffixStart, suffix.Length - suffixStart);
            return slices.Count == 0 ? Empty : new ImmutableRouteSequence(slices.ToArray());
        }

        public static ImmutableRouteSequence Append(
            ImmutableRouteSequence prefix,
            int[] suffix,
            int suffixStart)
        {
            if (prefix == null)
                throw new ArgumentNullException(nameof(prefix));
            if (suffix == null)
                throw new ArgumentNullException(nameof(suffix));
            if (suffixStart < 0 || suffixStart > suffix.Length)
                throw new ArgumentOutOfRangeException(nameof(suffixStart));

            var slices = new List<Slice>(prefix.m_Slices.Length + 1);
            AppendSlices(slices, prefix, 0, prefix.Length);
            if (suffixStart < suffix.Length)
            {
                var segment = new Segment(suffix);
                slices.Add(new Slice(segment, suffixStart, suffix.Length - suffixStart));
            }
            return slices.Count == 0 ? Empty : new ImmutableRouteSequence(slices.ToArray());
        }

        private static void AppendSlices(
            List<Slice> destination,
            ImmutableRouteSequence source,
            int start,
            int count)
        {
            if (count == 0)
                return;
            int remainingStart = start;
            int remainingCount = count;
            for (int i = 0; i < source.m_Slices.Length && remainingCount > 0; i++)
            {
                Slice slice = source.m_Slices[i];
                if (remainingStart >= slice.Count)
                {
                    remainingStart -= slice.Count;
                    continue;
                }
                int take = Math.Min(slice.Count - remainingStart, remainingCount);
                destination.Add(new Slice(slice.Segment, slice.Offset + remainingStart, take));
                remainingCount -= take;
                remainingStart = 0;
            }
            if (remainingCount != 0)
                throw new InvalidOperationException("Immutable route slice composition did not consume the requested range.");
        }

        public void CopyTo(int sourceIndex, int[] destination, int destinationIndex, int count)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (sourceIndex < 0 || count < 0 || sourceIndex + count > Length)
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            if (destinationIndex < 0 || destinationIndex + count > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            for (int i = 0; i < count; i++)
                destination[destinationIndex + i] = this[sourceIndex + i];
        }

        public int[] Clone()
        {
            var values = new int[Length];
            CopyTo(0, values, 0, values.Length);
            return values;
        }

        public ulong GetRangeAuthorityHash(int start, int count)
        {
            if (start < 0 || count < 0 || start + count > Length)
                throw new ArgumentOutOfRangeException(nameof(start));
            int remainingStart = start;
            int remainingCount = count;
            ulong hash = 0UL;
            for (int i = 0; i < m_Slices.Length && remainingCount > 0; i++)
            {
                Slice slice = m_Slices[i];
                if (remainingStart >= slice.Count)
                {
                    remainingStart -= slice.Count;
                    continue;
                }
                int take = Math.Min(slice.Count - remainingStart, remainingCount);
                hash = unchecked(hash * slice.Segment.Powers[take]
                                 + slice.Segment.GetSliceHash(slice.Offset + remainingStart, take));
                remainingCount -= take;
                remainingStart = 0;
            }
            if (remainingCount != 0)
                throw new InvalidOperationException("Immutable route range hash did not consume the requested range.");
            return hash;
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int i = 0; i < Length; i++)
                yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class PathHandle
    {
        public int HandleId;
        public int WorldVersion;
        public int GoalX;
        public int GoalY;
        public ImmutableRouteSequence SectorIds;
        public ImmutableRouteSequence PortalIds;
        public int CurrentSectorIndex;
        public string BuildSource;
        public bool HasCommittedCurrentTileKey;
        public FlowTileCacheKey CommittedCurrentTileKey;
        public int CommittedCorridorGoalX;
        public int CommittedCorridorGoalY;
        public int[] CommittedCorridorSectorIds;
        public int[] CommittedCorridorPortalIds;
        public PathHandle CommittedFunnelView;

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
        public ImmutableRouteSequence SectorIds;
        public ImmutableRouteSequence PortalIds;
        public int LastUsedFrame;
        public ulong AuthorityContentHash;
        public bool HasAuthorityContentHash;
    }

    private readonly struct SectorCorridorPolicyKey : IEquatable<SectorCorridorPolicyKey>
    {
        public readonly int WorldVersion;
        public readonly int AgentTypeId;
        public readonly int MovingTargetId;
        public readonly int SourceIslandId;
        public readonly int GoalSectorId;
        public readonly int GoalCellIndex;
        public readonly int GoalSectorDirtyVersion;

        public SectorCorridorPolicyKey(
            int worldVersion,
            int agentTypeId,
            int movingTargetId,
            int sourceIslandId,
            int goalSectorId,
            int goalCellIndex,
            int goalSectorDirtyVersion)
        {
            WorldVersion = worldVersion;
            AgentTypeId = agentTypeId;
            MovingTargetId = movingTargetId;
            SourceIslandId = sourceIslandId;
            GoalSectorId = goalSectorId;
            GoalCellIndex = goalCellIndex;
            GoalSectorDirtyVersion = goalSectorDirtyVersion;
        }

        public bool Equals(SectorCorridorPolicyKey other)
        {
            return WorldVersion == other.WorldVersion
                   && AgentTypeId == other.AgentTypeId
                   && MovingTargetId == other.MovingTargetId
                   && SourceIslandId == other.SourceIslandId
                   && GoalSectorId == other.GoalSectorId
                   && GoalCellIndex == other.GoalCellIndex
                   && GoalSectorDirtyVersion == other.GoalSectorDirtyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is SectorCorridorPolicyKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ MovingTargetId;
                hash = (hash * 397) ^ SourceIslandId;
                hash = (hash * 397) ^ GoalSectorId;
                hash = (hash * 397) ^ GoalCellIndex;
                hash = (hash * 397) ^ GoalSectorDirtyVersion;
                return hash;
            }
        }
    }

    private sealed class SectorCorridorPolicy : IDisposable
    {
        public int GoalSectorId = -1;
        public int GoalCellIndex = -1;
        public int GoalSectorDirtyVersion = -1;
        public readonly FlowPathKernelSearchState SearchState = new FlowPathKernelSearchState(256);
        public readonly Dictionary<int, PortalHierarchyReversePolicy> HierarchyPolicies =
            new Dictionary<int, PortalHierarchyReversePolicy>(4);
        public ulong HierarchyPoliciesAuthorityContentHash;
        public int LastUsedFrame;
        public ulong AuthorityContentHash;
        public bool HasAuthorityContentHash;

        public void Dispose()
        {
            SearchState.Dispose();
            var connectors = new HashSet<PortalHierarchyConnector>();
            foreach (PortalHierarchyReversePolicy policy in HierarchyPolicies.Values)
            {
                for (PortalHierarchyConnector connector = policy?.GoalConnector;
                     connector != null;
                     connector = connector.Child)
                {
                    if (connectors.Add(connector))
                        connector.Dispose();
                }
            }
            foreach (PortalHierarchyReversePolicy policy in HierarchyPolicies.Values)
                policy?.Dispose();
            HierarchyPolicies.Clear();
        }
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
        public long[] DeterministicIntegration;
        public int LastUsedFrame;
        public bool IsAnalyticClearSector;
        public int SectorId;
        public int PortalId;
        public int SectorDirtyVersion;
        public ulong AuthorityContentHash;
        public bool HasAuthorityContentHash;
    }

    private readonly struct StartPortalChoiceKey : IEquatable<StartPortalChoiceKey>
    {
        public readonly int WorldVersion;
        public readonly int StartSectorId;
        public readonly int StartCellIndex;
        public readonly int GoalSectorId;
        public readonly int GoalCellIndex;
        public readonly int AgentTypeId;
        public readonly int SelectedPortalId;
        public readonly int StartSectorDirtyVersion;
        public readonly int GoalSectorDirtyVersion;

        public StartPortalChoiceKey(
            int worldVersion,
            int startSectorId,
            int startCellIndex,
            int goalSectorId,
            int goalCellIndex,
            int agentTypeId,
            int selectedPortalId,
            int startSectorDirtyVersion,
            int goalSectorDirtyVersion)
        {
            WorldVersion = worldVersion;
            StartSectorId = startSectorId;
            StartCellIndex = startCellIndex;
            GoalSectorId = goalSectorId;
            GoalCellIndex = goalCellIndex;
            AgentTypeId = agentTypeId;
            SelectedPortalId = selectedPortalId;
            StartSectorDirtyVersion = startSectorDirtyVersion;
            GoalSectorDirtyVersion = goalSectorDirtyVersion;
        }

        public bool Equals(StartPortalChoiceKey other)
        {
            return WorldVersion == other.WorldVersion
                   && StartSectorId == other.StartSectorId
                   && StartCellIndex == other.StartCellIndex
                   && GoalSectorId == other.GoalSectorId
                   && GoalCellIndex == other.GoalCellIndex
                   && AgentTypeId == other.AgentTypeId
                   && SelectedPortalId == other.SelectedPortalId
                   && StartSectorDirtyVersion == other.StartSectorDirtyVersion
                   && GoalSectorDirtyVersion == other.GoalSectorDirtyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is StartPortalChoiceKey other && Equals(other);
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
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ SelectedPortalId;
                hash = (hash * 397) ^ StartSectorDirtyVersion;
                hash = (hash * 397) ^ GoalSectorDirtyVersion;
                return hash;
            }
        }
    }

    private sealed class StartPortalChoiceEntry
    {
        public int BestPortalId;
        public long SelectedCost;
        public long BestCost;
        public int LastUsedFrame;
    }

    private sealed class PendingSectorPortalAccess
    {
        public readonly int SectorId;
        public readonly int PortalId;
        public readonly int SectorDirtyVersion;
        public readonly bool IsAnalyticClearSector;
        public long[] DeterministicIntegration;

        public PendingSectorPortalAccess(
            int sectorId,
            int portalId,
            int sectorDirtyVersion,
            bool isAnalyticClearSector,
            long[] deterministicIntegration = null)
        {
            SectorId = sectorId;
            PortalId = portalId;
            SectorDirtyVersion = sectorDirtyVersion;
            IsAnalyticClearSector = isAnalyticClearSector;
            DeterministicIntegration = deterministicIntegration;
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

    private sealed class NavigationDistancePrewarmRequest
    {
        public int AgentTypeId;
        public FixVector2 From;
        public FixVector2 RawGoal;
        public int ResolvedWorldVersion = -1;
        public int ResolvedTopologyVersion = -1;
        public int StartX;
        public int StartY;
        public int StartSectorId = -1;
        public int GoalX;
        public int GoalY;
        public int GoalSectorId = -1;
    }

    private sealed class SharedGoalField
    {
        public SharedGoalFieldKey Key;
        public int GoalSectorId;
        public int GoalX;
        public int GoalY;
        public readonly Dictionary<int, long> NodeCosts = new Dictionary<int, long>(256);
        public readonly Dictionary<int, int> NextNodeTowardGoal = new Dictionary<int, int>(256);
        public readonly Dictionary<int, FirstCrossingPortalCacheEntry> FirstCrossingPortalByStartNode = new Dictionary<int, FirstCrossingPortalCacheEntry>(256);
        public readonly HashSet<int> CompletedDemandStartSectorIds = new HashSet<int>();
        public readonly HashSet<int> CompletedDemandStartCellIndices = new HashSet<int>();
        public DeterministicCostHeap PortalOpenSet;
        public HashSet<int> SettledPortalNodes;
        public ulong NodeCostsAuthorityContentHash;
        public ulong NextNodeTowardGoalAuthorityContentHash;
        public ulong CompletedDemandStartSectorAuthorityContentHash;
        public ulong CompletedDemandStartCellAuthorityContentHash;
        public ulong SettledPortalAuthorityContentHash;
        public int LastUsedFrame;
        public ulong AuthorityContentHash;
        public bool HasAuthorityContentHash;
    }

    private readonly struct FirstCrossingPortalCacheEntry
    {
        public readonly bool IsValid;
        public readonly int PortalId;

        public FirstCrossingPortalCacheEntry(bool isValid, int portalId)
        {
            IsValid = isValid;
            PortalId = portalId;
        }
    }

    private sealed class StrictPortalWindowUnreachableException : InvalidOperationException
    {
        public StrictPortalWindowUnreachableException(string message) : base(message)
        {
        }
    }

    private sealed class PendingNavigationTileException : InvalidOperationException
    {
        public PendingNavigationTileException(string message) : base(message)
        {
        }
    }

    private sealed class AgentNavState
    {
        public int AgentId;
        public int CurrentSectorId = -1;
        public Vector2Int CurrentCell;
        public PathHandle PathHandle;
        public Vector3 CurrentFlowDirection;
        public Vector3 DesiredVelocity;
        public Vector3 ResolvedVelocity;
        public int ResolvedVelocityFrame = -1;
        public MovementMode LastMovementMode = MovementMode.Normal;
        public Vector3 LastGoalWorld;
        public FixVector2 LastGoalWorldFixed;
        public bool HasGoal;
        public int StableGoalX = -1;
        public int StableGoalY = -1;
        public int StableGoalRawX = -1;
        public int StableGoalRawY = -1;
        public Vector3 StableGoalWorld;
        public FixVector2 StableGoalWorldFixed;
        public int StableGoalTargetId = int.MinValue;
        public bool HasFailedPathRequest;
        public int FailedPathWorldVersion;
        public int FailedPathStartCellIndex;
        public int FailedPathGoalCellIndex;
        public int FailedPathStartSectorDirtyVersion;
        public int FailedPathGoalSectorDirtyVersion;
        public bool HasPreparedNavigationSnapshot;
        public bool HasPendingNavigation;
        public bool HasPendingNavigationReplacement;
        public int CommittedMovingTargetId = int.MinValue;
        public int PreparedNavigationFrame = -1;
        public FixVector2 PreparedInputGoalFixed;
        public FixVector2 PreparedNavigationGoalFixed;
        public Fix64 PreparedMaximumTravelDistanceFixed;
        public int LastSteeringFrame = -1;
        public Vector3 LastSteeringGoal;
        public Vector3 LastSteeringDesiredDirection;
        public Vector3 LastSteeringDesiredVelocity;
        public Vector3 LastSteeringBaseVelocity;
        public Vector3 LastSteeringResultPreClamp;
        public Vector3 LastSteeringResult;
        public DesiredDirectionSource LastSteeringDesiredSource = DesiredDirectionSource.Zero;
        public bool LastSteeringHasLineOfSight;
        public float LastSteeringMaxSpeed;
        public int LastFixedFlowFrame = -1;
        public string LastFixedFlowResult = "not-called";
        public FixVector2 LastFixedFlowVelocity = FixVector2.Zero;
        public int PortalTraversalWorldVersion = -1;
        public int PortalTraversalSectorId = -1;
        public int PortalTraversalId = -1;
        public int PortalTraversalSlotIndex = -1;
        public bool PortalTraversalHasCommittedTileSlot;
        public int LastConstraintDiagnosticFrame = -1;
        public int LastPortalRankDiagnosticFrame = -1;
        public int LastCombatClusterDiagnosticFrame = -1;
        public int LastSuccessfulMoveDiagnosticFrame = -1;
        public int LastStableGoalDiagnosticFrame = -1;
        public int LastStuckTraceDiagnosticFrame = -1;
        public int StuckTraceFrames;
        public Vector3 LastStuckTracePosition;
        public float LastStuckTraceGoalDistance = float.PositiveInfinity;
    }

    private sealed class AgentRuntimeData
    {
        public int Id;
        public string CharacterKey;
        public Vector3 Position;
        public FixVector2 PositionFixed;
        public float Radius;
        public Fix64 RadiusFixed;
        public float RegisteredRadius;
        public SideType Side;
        public bool IgnoreAgentCollision;
        public int AgentTypeId;
        public string EntityTypeName;
        public string MoveCompTypeName;
        public string RegistrationSource;
        public bool IsSyntheticRegistration;
        public bool HasNavigationIntent;
        public readonly AgentNavState NavState = new AgentNavState();
    }

    private sealed class NavigationSyncRequest
    {
        public IEntityContext Source;
        public int SourceId;
        public int AgentTypeId;
        public int MovingTargetId;
        public FixVector2 InputGoalPosition;
        public Fix64 MaximumTravelDistance;
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
        PendingFinalGoal = 3,
        Zero = 4,
        PendingBuild = 5
    }

    private readonly struct NavigationGoalReservation
    {
        public readonly int SelfId;
        public readonly FixVector2 Point;
        public readonly Fix64 RequiredDistance;

        public NavigationGoalReservation(int selfId, Vector3 point, float requiredDistance)
        {
            SelfId = selfId;
            Point = new FixVector2((Fix64)point.x, (Fix64)point.z);
            RequiredDistance = Fix64.Max(Fix64.Zero, (Fix64)requiredDistance);
        }

        public NavigationGoalReservation(int selfId, FixVector2 point, Fix64 requiredDistance)
        {
            SelfId = selfId;
            Point = point;
            RequiredDistance = Fix64.Max(Fix64.Zero, requiredDistance);
        }
    }

    private readonly struct FlowTileCacheKey : IEquatable<FlowTileCacheKey>
    {
        public readonly int WorldVersion;
        public readonly int SectorId;
        public readonly TileGoalKind GoalKind;
        public readonly int GoalId;
        public readonly int FinalGoalIndex;
        public readonly int AgentTypeId;
        public readonly int DirtyVersion;

        public FlowTileCacheKey(
            int worldVersion,
            int sectorId,
            TileGoalKind goalKind,
            int goalId,
            int finalGoalIndex,
            int agentTypeId,
            int dirtyVersion)
        {
            WorldVersion = worldVersion;
            SectorId = sectorId;
            GoalKind = goalKind;
            GoalId = goalId;
            FinalGoalIndex = goalKind == TileGoalKind.FinalGoal ? finalGoalIndex : -1;
            AgentTypeId = agentTypeId;
            DirtyVersion = dirtyVersion;
        }

        public bool Equals(FlowTileCacheKey other)
        {
            bool sameIdentity = GoalKind == TileGoalKind.FinalGoal
                ? FinalGoalIndex == other.FinalGoalIndex
                : true;
            return WorldVersion == other.WorldVersion
                   && SectorId == other.SectorId
                   && GoalKind == other.GoalKind
                   && GoalId == other.GoalId
                   && sameIdentity
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
                if (GoalKind == TileGoalKind.FinalGoal)
                    hash = (hash * 397) ^ FinalGoalIndex;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ DirtyVersion;
                return hash;
            }
        }
    }

    private static int CompareFlowTileCacheKeys(FlowTileCacheKey left, FlowTileCacheKey right)
    {
        int order = left.WorldVersion.CompareTo(right.WorldVersion);
        if (order != 0) return order;
        order = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (order != 0) return order;
        order = left.SectorId.CompareTo(right.SectorId);
        if (order != 0) return order;
        order = left.GoalKind.CompareTo(right.GoalKind);
        if (order != 0) return order;
        order = left.GoalId.CompareTo(right.GoalId);
        if (order != 0) return order;
        if (left.GoalKind == TileGoalKind.FinalGoal)
        {
            order = left.FinalGoalIndex.CompareTo(right.FinalGoalIndex);
            if (order != 0) return order;
        }
        return left.DirtyVersion.CompareTo(right.DirtyVersion);
    }

    private sealed class FlowTileDebugIntegrationPayload
    {
        public float[] Values;
        public int ReleasedFrame = -1;

        public FlowTileDebugIntegrationPayload(int cellCount)
        {
            Values = RentIntegrationArray(cellCount);
        }
    }

    private sealed class FlowTileCacheEntry
    {
        public FlowTileCacheKey Key;
        public int StartX;
        public int StartY;
        public int Width;
        public int Height;
        public FlowTileDebugIntegrationPayload DebugIntegrationPayload;
        public byte[] FlowFieldValues;
        public int[] DeterministicIntegrationCosts;
        public FlowLocalPotentialShape PotentialShape;
        public int PotentialOffset;
        public byte[] DeterministicFlowDirectionIndices;
        public ushort[] DeterministicPortalTargetSlotIndices;
        public Vector2Int[] GoalCells;
        public int LastUsedFrame;
        public int LastReferencedFrame;
        public int ActiveReferenceCount;
        public bool UsesClearFlowDescriptor;
        public ulong AuthorityContentHash;
        public bool HasAuthorityContentHash;

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
            // A portal-window field is owned by its exit portal and sector.  The
            // moving terminal goal belongs to PathHandle/funnel and must not
            // become part of a pending portal build identity.
            GoalX = cacheKey.GoalKind == TileGoalKind.FinalGoal ? goalX : -1;
            GoalY = cacheKey.GoalKind == TileGoalKind.FinalGoal ? goalY : -1;
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
        public FlowTileCacheEntry Tile;
        public DeterministicFlowHeap OpenSet;
        public int[] GoalSeedCosts;
        public byte[] PortalHandoffDirectionIndices;
        public Vector2Int[] PortalExternalCells;
        public int[] PortalExternalCosts;
        public int PotentialOffset;
        public FlowLocalPotentialShapeKey PotentialShapeKey;
        public FlowLocalPotentialShape PotentialShape;
        public int Cursor;
        public int SlotTraceCursor = -1;
        public ushort SlotTraceValue;
        public readonly List<int> SlotTraceLocalIndices = new List<int>(32);
        public LogicStateHasher AuthorityHasher;
        public int AuthorityHashSection;
        public LogicStateHasher PotentialShapeHasher;
        public int PotentialShapeHashSection;
        public JobHandle IntegrationDirectionsHandle;
        public bool HasIntegrationDirectionsHandle;
        public NativeArray<int> IntegrationCostsNative;
        public NativeArray<byte> DirectionsNative;
        public FlowTileSectorInputBacking SectorInputBacking;
        public NativeArray<int> GoalLocalIndicesNative;
        public NativeArray<int> GoalSeedCostsNative;
        public NativeArray<byte> PortalHandoffNative;
        public NativeArray<int> ExternalXNative;
        public NativeArray<int> ExternalYNative;
        public NativeArray<int> ExternalCostsNative;
        public NativeArray<int> StatusNative;
    }

    private sealed class GoalProjectionSpatialIndex
    {
        public const int BucketSizeInCells = 8;

        public int BucketCountX;
        public int BucketCountY;
        public GoalProjectionBucket[] Buckets;
        public int[] QueryVisitStamps;
        public int QueryVisitToken;
        public int[] HeapBucketIds;
        public Fix64[] HeapLowerBounds;
        public int HeapCount;
        // Row-major cell lists partitioned by finalized island. They remain
        // useful for diagnostics, but runtime projection uses the hierarchy.
        public int[][] IslandCellIndices;
        public GoalProjectionHierarchyNode[] HierarchyNodes;
        public int HierarchyRootNodeIndex;
    }

    // A node covers a power-of-two block of 8x8 leaf buckets. IslandIds is
    // sorted, so a nearest-point query can reject an entire region before it
    // reaches its cells. Leaf nodes reference one GoalProjectionBucket.
    private sealed class GoalProjectionHierarchyNode
    {
        public int MinBucketX;
        public int MinBucketY;
        public int BucketSpan;
        public int BucketId = -1;
        public int Child0 = -1;
        public int Child1 = -1;
        public int Child2 = -1;
        public int Child3 = -1;
        public int[] IslandIds;
    }

    private sealed class GoalProjectionBucket
    {
        // Both arrays use the same cell-index order (row-major). Island ids are
        // captured from the already-finalized component-to-island authority.
        public int[] CellIndices;
        public int[] IslandIds;
    }

    private sealed class GoalProjectionSpatialIndexBuildJob
    {
        public int ScanIndex;
        public int FinalizeBucketCursor;
        public List<int>[] BucketCellLists;
        public List<int>[] IslandCellLists;
        public GoalProjectionSpatialIndex WorkingIndex;
        public List<GoalProjectionHierarchyNode> HierarchyNodes;
        public int HierarchyLeafSpan;
        public int HierarchyLeafCursor;
        public int[] HierarchyCurrentLevelNodeIndices;
        public int HierarchyCurrentLevelWidth;
        public int HierarchyCurrentLevelHeight;
        public int HierarchyParentCursor;
        public List<int> HierarchyNextLevelNodeIndices;
    }

    private static bool AreIntArraysEqual(int[] left, int[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }

    private static bool AreByteArraysEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }

    private static int AddArrayHash(int hash, int[] values)
    {
        unchecked
        {
            if (values == null)
                return hash * 397;
            for (int i = 0; i < values.Length; i++)
                hash = (hash * 397) ^ values[i];
            return hash;
        }
    }

    private static int CompareFlowLocalPotentialShapeKeys(
        FlowLocalPotentialShapeKey left,
        FlowLocalPotentialShapeKey right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return -1;
        if (right == null)
            return 1;
        int order = left.WorldVersion.CompareTo(right.WorldVersion);
        if (order != 0) return order;
        order = left.AgentTypeId.CompareTo(right.AgentTypeId);
        if (order != 0) return order;
        order = left.SectorId.CompareTo(right.SectorId);
        if (order != 0) return order;
        order = left.PortalId.CompareTo(right.PortalId);
        if (order != 0) return order;
        order = left.DirtyVersion.CompareTo(right.DirtyVersion);
        if (order != 0) return order;
        order = CompareIntArrays(left.NormalizedSeedCosts, right.NormalizedSeedCosts);
        if (order != 0) return order;
        order = CompareIntArrays(left.NormalizedExternalCosts, right.NormalizedExternalCosts);
        return order != 0
            ? order
            : CompareByteArrays(left.PortalHandoffDirectionIndices, right.PortalHandoffDirectionIndices);
    }

    private static int CompareIntArrays(int[] left, int[] right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        int count = Math.Min(left.Length, right.Length);
        for (int i = 0; i < count; i++)
        {
            int order = left[i].CompareTo(right[i]);
            if (order != 0) return order;
        }
        return left.Length.CompareTo(right.Length);
    }

    private static int CompareByteArrays(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        int count = Math.Min(left.Length, right.Length);
        for (int i = 0; i < count; i++)
        {
            int order = left[i].CompareTo(right[i]);
            if (order != 0) return order;
        }
        return left.Length.CompareTo(right.Length);
    }

    private enum FlowTileBuildStage
    {
        Initialize = 0,
        PotentialShapeLookup = 1,
        InitializeIntegration = 2,
        SeedIntegration = 3,
        MaterializePotentialShape = 4,
        Integrate = 5,
        Directions = 6,
        PortalSlots = 7,
        PotentialShapeHash = 8,
        DiagnosticShadow = 9,
        AuthorityHash = 10,
        Commit = 11,
        Complete = 12,
        IntegrationDirectionsPending = 13,
        IntegrationDirectionsCopy = 14
    }

    private sealed class FlowLocalPotentialShapeKey : IEquatable<FlowLocalPotentialShapeKey>
    {
        public int WorldVersion;
        public int AgentTypeId;
        public int SectorId;
        public int PortalId;
        public int DirtyVersion;
        public int[] NormalizedSeedCosts;
        public int[] NormalizedExternalCosts;
        public byte[] PortalHandoffDirectionIndices;

        public bool Equals(FlowLocalPotentialShapeKey other)
        {
            return other != null
                   && WorldVersion == other.WorldVersion
                   && AgentTypeId == other.AgentTypeId
                   && SectorId == other.SectorId
                   && PortalId == other.PortalId
                   && DirtyVersion == other.DirtyVersion
                   && AreIntArraysEqual(NormalizedSeedCosts, other.NormalizedSeedCosts)
                   && AreIntArraysEqual(NormalizedExternalCosts, other.NormalizedExternalCosts)
                   && AreByteArraysEqual(PortalHandoffDirectionIndices, other.PortalHandoffDirectionIndices);
        }

        public override bool Equals(object obj)
        {
            return obj is FlowLocalPotentialShapeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ SectorId;
                hash = (hash * 397) ^ PortalId;
                hash = (hash * 397) ^ DirtyVersion;
                hash = AddArrayHash(hash, NormalizedSeedCosts);
                hash = AddArrayHash(hash, NormalizedExternalCosts);
                if (PortalHandoffDirectionIndices != null)
                {
                    for (int i = 0; i < PortalHandoffDirectionIndices.Length; i++)
                        hash = (hash * 397) ^ PortalHandoffDirectionIndices[i];
                }
                return hash;
            }
        }
    }

    private sealed class FlowLocalPotentialShape
    {
        public int[] NormalizedIntegrationCosts;
        public byte[] DirectionIndices;
        public ushort[] PortalTargetSlotIndices;
        public byte[] FlowFieldValues;
        public int LastUsedFrame;
        public ulong AuthorityContentHash;
        public bool HasAuthorityContentHash;
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
        public SharedGoalField Field;
        public DeterministicCostHeap PortalOpenSet;
        public HashSet<int> DemandStartSectorIds;
        public Dictionary<int, int> DemandStartSectorByCellIndex;
        public HashSet<int> SettledPortalNodes;
        public ulong DemandStartSectorAuthorityContentHash;
        public ulong DemandStartCellAuthorityContentHash;
        public ulong SettledPortalAuthorityContentHash;
        public ulong AuthorityProgressHash;
        public bool HasAuthorityProgressHash;
    }

    private enum SharedGoalFieldBuildStage
    {
        Initialize = 0,
        PortalGraph = 1,
        Commit = 2,
        Complete = 3
    }

    private readonly struct FixedPortalOwnerKey : IEquatable<FixedPortalOwnerKey>
    {
        public FixedPortalOwnerKey(int worldVersion, int agentTypeId, int portalId)
            : this(worldVersion, agentTypeId, 1, portalId)
        {
        }

        public FixedPortalOwnerKey(int worldVersion, int agentTypeId, int kind, int localBottleneckId)
        {
            if (kind != 1 && kind != 2)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Fixed bottleneck kind must be portal or corridor.");
            WorldVersion = worldVersion;
            AgentTypeId = agentTypeId;
            Kind = kind;
            LocalBottleneckId = localBottleneckId;
        }

        public int WorldVersion { get; }
        public int AgentTypeId { get; }
        public int Kind { get; }
        public int LocalBottleneckId { get; }
        public int PortalId => Kind == 1 ? LocalBottleneckId : -1;

        public bool Equals(FixedPortalOwnerKey other) =>
            WorldVersion == other.WorldVersion
            && AgentTypeId == other.AgentTypeId
            && Kind == other.Kind
            && LocalBottleneckId == other.LocalBottleneckId;

        public override bool Equals(object obj) => obj is FixedPortalOwnerKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ Kind;
                return (hash * 397) ^ LocalBottleneckId;
            }
        }
    }

    private sealed class FixedPortalOwnerState
    {
        public FixedPortalOwnerKey Key;
        public int OwnerDirection;
        public int OwnerSinceFrame;
        public int LastEvaluatedFrame;
        public int PositiveMinimumAgentId = int.MaxValue;
        public int NegativeMinimumAgentId = int.MaxValue;
    }

    private sealed class FixedPortalParticipantSnapshot
    {
        public int PositiveMinimumAgentId = int.MaxValue;
        public int NegativeMinimumAgentId = int.MaxValue;
        public bool PositiveOnPortal;
        public bool NegativeOnPortal;
    }

    private sealed class FixedPortalParticipation
    {
        public FixedPortalOwnerKey Key;
        public int Direction;
        public bool OnPortal;
    }

    private sealed class FixedPortalParticipantIndex
    {
        public readonly SortedSet<int> PositiveAgentIds = new SortedSet<int>();
        public readonly SortedSet<int> NegativeAgentIds = new SortedSet<int>();
        public readonly SortedSet<int> PositiveOnPortalAgentIds = new SortedSet<int>();
        public readonly SortedSet<int> NegativeOnPortalAgentIds = new SortedSet<int>();

        public bool IsEmpty => PositiveAgentIds.Count == 0 && NegativeAgentIds.Count == 0;
    }

    private sealed class FixedCorridorDescriptor
    {
        public FixedPortalOwnerKey Key;
        public int ComponentId;
        public int ComponentCellCount;
        public int[] EndpointACellIndices;
        public int[] EndpointBCellIndices;
    }

    private enum FixedCorridorResolution : byte
    {
        Absent = 0,
        Pending = 1,
        Ready = 2,
    }

    private sealed class FixedCorridorBuildJob
    {
        public int ComponentId;
        public int StartIndex;
        public int MinimumCellIndex;
        public int Head;
        public readonly List<int> Queue = new List<int>();
        public readonly HashSet<int> ExternalEndpointCells = new HashSet<int>();
        public readonly HashSet<int> TerminalEndpointCells = new HashSet<int>();
        public ulong ComponentCellContentHash;
        public ulong ExternalEndpointContentHash;
        public ulong TerminalEndpointContentHash;
        public bool ExternalEndpointOverflow;
        public bool TerminalEndpointOverflow;
    }

    private sealed class FixedCorridorLookup
    {
        public FixedCorridorLookup(int narrowWidth)
        {
            NarrowWidth = narrowWidth;
            int connectorQueueCapacity = checked(1 + 2 * narrowWidth * (narrowWidth + 1));
            ConnectorQueue = new int[connectorQueueCapacity];
            ConnectorDistances = new int[connectorQueueCapacity];
        }

        public readonly int NarrowWidth;
        public readonly Dictionary<int, byte> OrientationsByCell = new Dictionary<int, byte>();
        public readonly Dictionary<int, bool> CandidateByCell = new Dictionary<int, bool>();
        public readonly Dictionary<int, int> ComponentIdByCell = new Dictionary<int, int>();
        public readonly Dictionary<int, FixedCorridorDescriptor> DescriptorByComponentId = new Dictionary<int, FixedCorridorDescriptor>();
        public readonly HashSet<int> CompletedComponentIds = new HashSet<int>();
        public readonly HashSet<int> ResolvedNonCorridorCells = new HashSet<int>();
        public readonly SortedSet<int> PendingStartIndices = new SortedSet<int>();
        public readonly int[] ConnectorQueue;
        public readonly int[] ConnectorDistances;
        public FixedCorridorBuildJob ActiveBuildJob;
        public int NextComponentId = 1;
        public ulong ComponentAssignmentContentHash;
        public ulong CompletedComponentContentHash;
        public ulong DescriptorContentHash;
        public ulong PendingStartContentHash;
    }

    private sealed class TestTerrainOverride
    {
        public int AgentTypeId;
        public int Width;
        public int Height;
        public float CellSize;
        public float EncodedCenterClearance;
        public Vector3 Origin;
        public long CellSizeGridRaw;
        public long EncodedCenterClearanceFixedRaw;
        public long AgentRadiusFixedRaw;
        public long OriginXGridRaw;
        public long OriginZGridRaw;
        public bool HasFixedAuthorityPayload;
        public bool[] WalkableMask;
        public Vector3[] CellNavAnchors;
        public FixVector2[] CellNavAnchorsFixedXZ;
        public byte[] CostField;
        public byte[] NeighborTraversalMask;
        public FixVector2[] StaticCollisionVertices;
        public int[] StaticCollisionPathStarts;
        public FlowNavigationGridAsset.DerivedNavigationData DerivedNavigationData;

        public TestTerrainOverride Clone()
        {
            return new TestTerrainOverride
            {
                AgentTypeId = AgentTypeId,
                Width = Width,
                Height = Height,
                CellSize = CellSize,
                EncodedCenterClearance = EncodedCenterClearance,
                Origin = Origin,
                CellSizeGridRaw = CellSizeGridRaw,
                EncodedCenterClearanceFixedRaw = EncodedCenterClearanceFixedRaw,
                AgentRadiusFixedRaw = AgentRadiusFixedRaw,
                OriginXGridRaw = OriginXGridRaw,
                OriginZGridRaw = OriginZGridRaw,
                HasFixedAuthorityPayload = HasFixedAuthorityPayload,
                WalkableMask = WalkableMask != null ? (bool[])WalkableMask.Clone() : null,
                CellNavAnchors = CellNavAnchors != null ? (Vector3[])CellNavAnchors.Clone() : null,
                CellNavAnchorsFixedXZ = CellNavAnchorsFixedXZ != null ? (FixVector2[])CellNavAnchorsFixedXZ.Clone() : null,
                CostField = CostField != null ? (byte[])CostField.Clone() : null,
                NeighborTraversalMask = NeighborTraversalMask != null ? (byte[])NeighborTraversalMask.Clone() : null,
                StaticCollisionVertices = StaticCollisionVertices != null ? (FixVector2[])StaticCollisionVertices.Clone() : null,
                StaticCollisionPathStarts = StaticCollisionPathStarts != null ? (int[])StaticCollisionPathStarts.Clone() : null,
                DerivedNavigationData = DerivedNavigationData
            };
        }
    }

#if UNITY_EDITOR
    private sealed class EditorNavigationPreviewWorld
    {
        public FlowNavigationGridAsset Grid;
        public FlowNavigationGridAsset StaticCollisionSourceGrid;
        public FlowNavigationGridAsset.DerivedNavigationData DerivedNavigationData;
        public bool[] WalkableMask;
        public byte[] NeighborTraversalMask;
        public FixVector2[] StaticCollisionVertices;
        public int[] StaticCollisionPathStarts;
        public NavigationWorld World;
    }
#endif

    private sealed class CircleObstacle
    {
        public int Id;
        public Vector3 Position;
        public float Radius;
        public FixVector2 PositionFixed;
        public Fix64 RadiusFixed;
    }

    private readonly struct MovingTargetAnchorKey : IEquatable<MovingTargetAnchorKey>
    {
        public readonly int TargetId;
        public readonly int AgentTypeId;
        public readonly int IslandId;

        public MovingTargetAnchorKey(int targetId, int agentTypeId, int islandId)
        {
            TargetId = targetId;
            AgentTypeId = agentTypeId;
            IslandId = islandId;
        }

        public bool Equals(MovingTargetAnchorKey other)
        {
            return TargetId == other.TargetId
                   && AgentTypeId == other.AgentTypeId
                   && IslandId == other.IslandId;
        }

        public override bool Equals(object obj)
        {
            return obj is MovingTargetAnchorKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TargetId;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ IslandId;
                return hash;
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
        public FixVector2 ActiveGoalWorldFixed;
        public int ReachabilityFrame = -1;
        public int ReachabilityWorldVersion = -1;
        public int ReachabilityRawGoalX = -1;
        public int ReachabilityRawGoalY = -1;
        public int ReachabilityGoalX = -1;
        public int ReachabilityGoalY = -1;
        public int ReachabilityGoalSectorId = -1;
        public FixVector2 ReachabilityGoalWorldFixed;
        // Projection is target-side authority. Source consumers never finish it.
        public bool HasPendingProjection;
        public int PendingProjectionWorldVersion = -1;
        public int PendingProjectionRawGoalX = -1;
        public int PendingProjectionRawGoalY = -1;
        public FixVector2 PendingProjectionGoalWorldFixed;
        public int PendingProjectionCellCursor;
        public int PendingProjectionBestCellIndex = int.MaxValue;
        public Fix64 PendingProjectionBestDistanceSquared = Fix64.FromRaw(long.MaxValue);
        public int PendingProjectionLeafBucketId = -1;
        public int PendingProjectionLeafCellCursor;
        public readonly List<int> PendingProjectionNodeHeap = new List<int>(16);
        public readonly List<Fix64> PendingProjectionNodeLowerBounds = new List<Fix64>(16);
        public bool HasPinnedSectorCorridorPolicy;
        public SectorCorridorPolicyKey PinnedSectorCorridorPolicyKey;
        public int LastUsedFrame = -1;
    }

    private readonly struct CombatTargetSlotKey : IEquatable<CombatTargetSlotKey>
    {
        public readonly int WorldVersion;
        public readonly int AgentTypeId;
        public readonly int TargetId;
        public readonly int TargetCellIndex;
        public readonly long TargetRawX;
        public readonly long TargetRawY;
        public readonly long StandOffRaw;
        public readonly long MinimumStandOffRaw;
        public readonly long RingSpacingRaw;
        public readonly long ClearanceRaw;
        public readonly int RingCount;
        public readonly int CandidateCount;

        public CombatTargetSlotKey(
            int worldVersion,
            int agentTypeId,
            int targetId,
            int targetCellIndex,
            long targetRawX,
            long targetRawY,
            long standOffRaw,
            long minimumStandOffRaw,
            long ringSpacingRaw,
            long clearanceRaw,
            int ringCount,
            int candidateCount)
        {
            WorldVersion = worldVersion;
            AgentTypeId = agentTypeId;
            TargetId = targetId;
            TargetCellIndex = targetCellIndex;
            TargetRawX = targetRawX;
            TargetRawY = targetRawY;
            StandOffRaw = standOffRaw;
            MinimumStandOffRaw = minimumStandOffRaw;
            RingSpacingRaw = ringSpacingRaw;
            ClearanceRaw = clearanceRaw;
            RingCount = ringCount;
            CandidateCount = candidateCount;
        }

        public bool Equals(CombatTargetSlotKey other)
        {
            return WorldVersion == other.WorldVersion
                   && AgentTypeId == other.AgentTypeId
                   && TargetId == other.TargetId
                   && TargetCellIndex == other.TargetCellIndex
                   && TargetRawX == other.TargetRawX
                   && TargetRawY == other.TargetRawY
                   && StandOffRaw == other.StandOffRaw
                   && MinimumStandOffRaw == other.MinimumStandOffRaw
                   && RingSpacingRaw == other.RingSpacingRaw
                   && ClearanceRaw == other.ClearanceRaw
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
                hash = (hash * 397) ^ TargetRawX.GetHashCode();
                hash = (hash * 397) ^ TargetRawY.GetHashCode();
                hash = (hash * 397) ^ StandOffRaw.GetHashCode();
                hash = (hash * 397) ^ MinimumStandOffRaw.GetHashCode();
                hash = (hash * 397) ^ RingSpacingRaw.GetHashCode();
                hash = (hash * 397) ^ ClearanceRaw.GetHashCode();
                hash = (hash * 397) ^ RingCount;
                hash = (hash * 397) ^ CandidateCount;
                return hash;
            }
        }
    }

    private sealed class CombatTargetSlotEntry
    {
        public FixVector2 TargetPoint;
        public FixVector2[] Points;
        public int[] CellX;
        public int[] CellY;
        public int[] IslandIds;
        public int LastUsedFrame;
        public string BuildSummary;
    }

    private readonly struct AttackAreaCandidateCacheKey : IEquatable<AttackAreaCandidateCacheKey>
    {
        public readonly int WorldVersion;
        public readonly int TopologyVersion;
        public readonly int AgentTypeId;
        public readonly LogicCombatShapeKind ShapeKind;
        public readonly long CenterXRaw;
        public readonly long CenterYRaw;
        public readonly long RadiusRaw;
        public readonly long HalfExtentXRaw;
        public readonly long HalfExtentYRaw;
        public readonly long AttackRangeRaw;
        public readonly long NavigationClearanceRaw;
        public readonly long MinimumSurfaceDistanceRaw;

        public AttackAreaCandidateCacheKey(
            int worldVersion,
            int topologyVersion,
            int agentTypeId,
            LogicCombatShape targetShape,
            int targetSectorId,
            Fix64 attackRange,
            Fix64 navigationClearance,
            Fix64 minimumSurfaceDistance)
        {
            WorldVersion = worldVersion;
            TopologyVersion = topologyVersion;
            AgentTypeId = agentTypeId;
            ShapeKind = targetShape.Kind;
            CenterXRaw = targetShape.Center.x.RawValue;
            CenterYRaw = targetShape.Center.y.RawValue;
            RadiusRaw = targetShape.Radius.RawValue;
            HalfExtentXRaw = targetShape.HalfExtents.x.RawValue;
            HalfExtentYRaw = targetShape.HalfExtents.y.RawValue;
            AttackRangeRaw = attackRange.RawValue;
            NavigationClearanceRaw = navigationClearance.RawValue;
            MinimumSurfaceDistanceRaw = minimumSurfaceDistance.RawValue;
        }

        public bool Equals(AttackAreaCandidateCacheKey other)
        {
            return WorldVersion == other.WorldVersion
                   && TopologyVersion == other.TopologyVersion
                   && AgentTypeId == other.AgentTypeId
                   && ShapeKind == other.ShapeKind
                   && CenterXRaw == other.CenterXRaw
                   && CenterYRaw == other.CenterYRaw
                   && RadiusRaw == other.RadiusRaw
                   && HalfExtentXRaw == other.HalfExtentXRaw
                   && HalfExtentYRaw == other.HalfExtentYRaw
                   && AttackRangeRaw == other.AttackRangeRaw
                   && NavigationClearanceRaw == other.NavigationClearanceRaw
                   && MinimumSurfaceDistanceRaw == other.MinimumSurfaceDistanceRaw;
        }

        public override bool Equals(object obj)
        {
            return obj is AttackAreaCandidateCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ TopologyVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ (int)ShapeKind;
                hash = (hash * 397) ^ CenterXRaw.GetHashCode();
                hash = (hash * 397) ^ CenterYRaw.GetHashCode();
                hash = (hash * 397) ^ RadiusRaw.GetHashCode();
                hash = (hash * 397) ^ HalfExtentXRaw.GetHashCode();
                hash = (hash * 397) ^ HalfExtentYRaw.GetHashCode();
                hash = (hash * 397) ^ AttackRangeRaw.GetHashCode();
                hash = (hash * 397) ^ NavigationClearanceRaw.GetHashCode();
                hash = (hash * 397) ^ MinimumSurfaceDistanceRaw.GetHashCode();
                return hash;
            }
        }
    }

    private readonly struct AttackAreaCandidate
    {
        public AttackAreaCandidate(FixVector2 point, int cellIndex)
        {
            Point = point;
            CellIndex = cellIndex;
        }

        public FixVector2 Point { get; }
        public int CellIndex { get; }
    }

    private sealed class AttackAreaIslandCandidates
    {
        public readonly List<AttackAreaCandidate> Candidates = new List<AttackAreaCandidate>();
    }

    private sealed class AttackAreaCandidateCacheEntry
    {
        public readonly Dictionary<int, AttackAreaIslandCandidates> ByIsland =
            new Dictionary<int, AttackAreaIslandCandidates>();
    }

    private readonly struct TargetingReachabilityCacheKey : IEquatable<TargetingReachabilityCacheKey>
    {
        public TargetingReachabilityCacheKey(
            int worldVersion,
            int topologyVersion,
            int agentTypeId,
            int startIslandId,
            LogicCombatShape targetShape,
            Fix64 attackRange)
        {
            WorldVersion = worldVersion;
            TopologyVersion = topologyVersion;
            AgentTypeId = agentTypeId;
            StartIslandId = startIslandId;
            ShapeKind = targetShape.Kind;
            CenterXRaw = targetShape.Center.x.RawValue;
            CenterYRaw = targetShape.Center.y.RawValue;
            RadiusRaw = targetShape.Radius.RawValue;
            HalfExtentXRaw = targetShape.HalfExtents.x.RawValue;
            HalfExtentYRaw = targetShape.HalfExtents.y.RawValue;
            AttackRangeRaw = attackRange.RawValue;
        }

        public int WorldVersion { get; }
        public int TopologyVersion { get; }
        public int AgentTypeId { get; }
        public int StartIslandId { get; }
        public LogicCombatShapeKind ShapeKind { get; }
        public long CenterXRaw { get; }
        public long CenterYRaw { get; }
        public long RadiusRaw { get; }
        public long HalfExtentXRaw { get; }
        public long HalfExtentYRaw { get; }
        public long AttackRangeRaw { get; }

        public bool Equals(TargetingReachabilityCacheKey other) =>
            WorldVersion == other.WorldVersion
            && TopologyVersion == other.TopologyVersion
            && AgentTypeId == other.AgentTypeId
            && StartIslandId == other.StartIslandId
            && ShapeKind == other.ShapeKind
            && CenterXRaw == other.CenterXRaw
            && CenterYRaw == other.CenterYRaw
            && RadiusRaw == other.RadiusRaw
            && HalfExtentXRaw == other.HalfExtentXRaw
            && HalfExtentYRaw == other.HalfExtentYRaw
            && AttackRangeRaw == other.AttackRangeRaw;

        public override bool Equals(object obj) => obj is TargetingReachabilityCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ TopologyVersion;
                hash = (hash * 397) ^ AgentTypeId;
                hash = (hash * 397) ^ StartIslandId;
                hash = (hash * 397) ^ (int)ShapeKind;
                hash = (hash * 397) ^ CenterXRaw.GetHashCode();
                hash = (hash * 397) ^ CenterYRaw.GetHashCode();
                hash = (hash * 397) ^ RadiusRaw.GetHashCode();
                hash = (hash * 397) ^ HalfExtentXRaw.GetHashCode();
                hash = (hash * 397) ^ HalfExtentYRaw.GetHashCode();
                return (hash * 397) ^ AttackRangeRaw.GetHashCode();
            }
        }
    }

    private readonly struct TargetingReachabilityCacheEntry
    {
        public TargetingReachabilityCacheEntry(
            bool reachable,
            NavigationQueryFailureKind failureKind)
        {
            Reachable = reachable;
            FailureKind = failureKind;
        }

        public bool Reachable { get; }
        public NavigationQueryFailureKind FailureKind { get; }
    }

    private sealed class BoxObstacle
    {
        public int Id;
        public Vector3 Center;
        public Vector3 HalfExtents;
        public FixVector2 CenterFixed;
        public FixVector2 HalfExtentsFixed;
    }

    private sealed class CostStamp
    {
        public int Id;
        public int AgentTypeId;
        public Bounds Bounds;
        public FixVector2 BoundsMinFixed;
        public FixVector2 BoundsMaxFixed;
        public byte Cost;
        public int Width;
        public int Height;
        public float CellSize;
        public Fix64 CellSizeFixed;
        public long CellSizeGridRaw;
        public Vector3 Origin;
        public FixVector2 OriginFixed;
        public long OriginXGridRaw;
        public long OriginZGridRaw;
        public byte[] Costs;
        public ulong AuthorityContentHash;
    }

    private struct QueueNode
    {
        public int Index;
        public float Cost;
    }

    private readonly struct DeterministicFlowNode : IComparable<DeterministicFlowNode>
    {
        public DeterministicFlowNode(int cost, int index)
        {
            Cost = cost;
            Index = index;
        }

        public int Cost { get; }
        public int Index { get; }

        public int CompareTo(DeterministicFlowNode other)
        {
            int result = Cost.CompareTo(other.Cost);
            return result != 0 ? result : Index.CompareTo(other.Index);
        }
    }

    private sealed class DeterministicFlowHeap
    {
        private readonly List<DeterministicFlowNode> _items = new List<DeterministicFlowNode>(256);

        public int Count => _items.Count;

        public void Clear()
        {
            _items.Clear();
        }

        public void Push(int cost, int index)
        {
            _items.Add(new DeterministicFlowNode(cost, index));
            int child = _items.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (_items[parent].CompareTo(_items[child]) <= 0)
                    break;
                (_items[parent], _items[child]) = (_items[child], _items[parent]);
                child = parent;
            }
        }

        public DeterministicFlowNode Pop()
        {
            if (_items.Count == 0)
                throw new InvalidOperationException("DeterministicFlowHeap.Pop failed: heap is empty.");

            DeterministicFlowNode root = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            int parent = 0;
            while (parent < _items.Count)
            {
                int left = parent * 2 + 1;
                if (left >= _items.Count)
                    break;
                int right = left + 1;
                int best = right < _items.Count && _items[right].CompareTo(_items[left]) < 0 ? right : left;
                if (_items[parent].CompareTo(_items[best]) <= 0)
                    break;
                (_items[parent], _items[best]) = (_items[best], _items[parent]);
                parent = best;
            }
            return root;
        }

        public void WriteDeterministicState(LogicStateHasher hasher)
        {
            if (hasher == null)
                throw new ArgumentNullException(nameof(hasher));
            hasher.Add(_items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                hasher.Add(_items[i].Cost);
                hasher.Add(_items[i].Index);
            }
        }
    }

    [BurstCompile]
    private struct DeterministicFlowTileIntegrationDirectionsJob : IJob
    {
        public int Width;
        public int Height;
        public int StartX;
        public int StartY;
        public bool IsPortalGoal;
        public NativeArray<int> IntegrationCosts;
        public NativeArray<byte> Directions;
        [ReadOnly] public NativeArray<bool> Walkable;
        [ReadOnly] public NativeArray<byte> TraversalMask;
        [ReadOnly] public NativeArray<byte> CellCosts;
        [ReadOnly] public NativeArray<int> GoalLocalIndices;
        [ReadOnly] public NativeArray<int> GoalSeedCosts;
        [ReadOnly] public NativeArray<byte> PortalHandoff;
        [ReadOnly] public NativeArray<int> ExternalX;
        [ReadOnly] public NativeArray<int> ExternalY;
        [ReadOnly] public NativeArray<int> ExternalCosts;
        public NativeArray<int> Status;
        public NativeArray<int> HeapCosts;
        public NativeArray<int> HeapIndices;

        public void Execute()
        {
            int count = Width * Height;
            for (int i = 0; i < count; i++)
            {
                IntegrationCosts[i] = int.MaxValue;
                Directions[i] = 0;
            }

            int heapCount = 0;
            for (int i = 0; i < GoalSeedCosts.Length; i++)
            {
                int localIndex = GoalLocalIndices[i];
                int seedCost = GoalSeedCosts[i];
                if (localIndex < 0 || localIndex >= count || seedCost == int.MaxValue)
                    continue;
                if (!Walkable[localIndex] || seedCost < 0)
                    continue;
                if (seedCost < IntegrationCosts[localIndex])
                {
                    IntegrationCosts[localIndex] = seedCost;
                    if (!PushHeap(seedCost, localIndex, ref heapCount))
                    {
                        Status[0] = 1;
                        return;
                    }
                }
            }

            if (heapCount == 0)
            {
                Status[0] = 2;
                return;
            }

            while (heapCount > 0)
            {
                PopHeap(ref heapCount, out int nodeCost, out int nodeIndex);
                if (nodeCost != IntegrationCosts[nodeIndex])
                    continue;

                int worldX = StartX + nodeIndex % Width;
                int worldY = StartY + nodeIndex / Width;
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    int nextX = worldX + CardinalX(directionIndex);
                    int nextY = worldY + CardinalY(directionIndex);
                    if (!IsInside(nextX, nextY))
                        continue;
                    int nextIndex = LocalIndex(nextX, nextY);
                    if (!CanTraverse(nodeIndex, nextIndex, CardinalX(directionIndex), CardinalY(directionIndex)))
                        continue;

                    int candidateCost = ResolveCandidateCost(nextIndex, nextX, nextY);
                    if (candidateCost >= IntegrationCosts[nextIndex])
                        continue;
                    IntegrationCosts[nextIndex] = candidateCost;
                    if (!PushHeap(candidateCost, nextIndex, ref heapCount))
                    {
                        Status[0] = 1;
                        return;
                    }
                }
            }

            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                int currentCost = IntegrationCosts[localIndex];
                if (currentCost == int.MaxValue)
                    continue;
                int worldX = StartX + localIndex % Width;
                int worldY = StartY + localIndex / Width;
                int goalIndex = FindGoalIndex(localIndex);
                if (goalIndex >= 0)
                {
                    if (IsPortalGoal)
                        Directions[localIndex] = PortalHandoff[goalIndex];
                    continue;
                }

                int bestCost = currentCost;
                int bestDirection = -1;
                for (int directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    int nextX = worldX + NeighborX(directionIndex);
                    int nextY = worldY + NeighborY(directionIndex);
                    if (!IsInside(nextX, nextY))
                        continue;
                    int nextIndex = LocalIndex(nextX, nextY);
                    if (!CanTraverse(localIndex, nextIndex, NeighborX(directionIndex), NeighborY(directionIndex)))
                        continue;
                    int nextCost = IntegrationCosts[nextIndex];
                    if (nextCost >= bestCost)
                        continue;
                    bestCost = nextCost;
                    bestDirection = directionIndex;
                }
                if (bestDirection >= 0)
                    Directions[localIndex] = (byte)(bestDirection + 1);
            }
            Status[0] = 0;
        }

        private bool PushHeap(int cost, int index, ref int count)
        {
            if (count >= HeapCosts.Length)
                return false;
            int child = count++;
            HeapCosts[child] = cost;
            HeapIndices[child] = index;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (HeapLessOrEqual(parent, child))
                    break;
                SwapHeap(parent, child);
                child = parent;
            }
            return true;
        }

        private void PopHeap(ref int count, out int cost, out int index)
        {
            cost = HeapCosts[0];
            index = HeapIndices[0];
            count--;
            if (count <= 0)
                return;
            HeapCosts[0] = HeapCosts[count];
            HeapIndices[0] = HeapIndices[count];
            int parent = 0;
            while (true)
            {
                int left = parent * 2 + 1;
                if (left >= count)
                    break;
                int right = left + 1;
                int best = right < count && HeapCompare(right, left) < 0 ? right : left;
                if (HeapLessOrEqual(parent, best))
                    break;
                SwapHeap(parent, best);
                parent = best;
            }
        }

        private int HeapCompare(int left, int right)
        {
            int costCompare = HeapCosts[left].CompareTo(HeapCosts[right]);
            return costCompare != 0 ? costCompare : HeapIndices[left].CompareTo(HeapIndices[right]);
        }

        private bool HeapLessOrEqual(int left, int right)
        {
            return HeapCompare(left, right) <= 0;
        }

        private void SwapHeap(int left, int right)
        {
            int cost = HeapCosts[left];
            HeapCosts[left] = HeapCosts[right];
            HeapCosts[right] = cost;
            int index = HeapIndices[left];
            HeapIndices[left] = HeapIndices[right];
            HeapIndices[right] = index;
        }

        private int ResolveCandidateCost(int localIndex, int worldX, int worldY)
        {
            long horizontal = long.MaxValue;
            long vertical = long.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                int nextX = worldX + CardinalX(i);
                int nextY = worldY + CardinalY(i);
                if (!IsInside(nextX, nextY))
                    continue;
                int nextIndex = LocalIndex(nextX, nextY);
                if (!CanTraverse(localIndex, nextIndex, CardinalX(i), CardinalY(i)))
                    continue;
                int nextCost = IntegrationCosts[nextIndex];
                if (nextCost == int.MaxValue)
                    continue;
                if (CardinalX(i) != 0)
                    horizontal = Math.Min(horizontal, nextCost);
                else
                    vertical = Math.Min(vertical, nextCost);
            }

            int goalIndex = FindGoalIndex(localIndex);
            if (goalIndex >= 0 && goalIndex < ExternalCosts.Length && ExternalCosts[goalIndex] != int.MaxValue)
            {
                int externalX = ExternalX[goalIndex];
                int externalY = ExternalY[goalIndex];
                int dx = externalX - worldX;
                int dy = externalY - worldY;
                if (Math.Abs(dx) + Math.Abs(dy) != 1)
                {
                    Status[0] = 3;
                    return int.MaxValue;
                }
                if (dx != 0)
                    horizontal = Math.Min(horizontal, ExternalCosts[goalIndex]);
                else
                    vertical = Math.Min(vertical, ExternalCosts[goalIndex]);
            }

            byte cellCost = CellCosts[localIndex];
            if (cellCost == byte.MaxValue)
                return int.MaxValue;
            long stepCost = 1024L * Math.Max(1, (int)cellCost);
            return (int)ResolveEikonalUpdate(horizontal, vertical, stepCost);
        }

        private long ResolveEikonalUpdate(long horizontal, long vertical, long stepCost)
        {
            const long unreachable = int.MaxValue;
            if (horizontal == long.MaxValue)
                return vertical == long.MaxValue ? unreachable : ClampCost(vertical + stepCost);
            if (vertical == long.MaxValue)
                return ClampCost(horizontal + stepCost);
            long low = Math.Min(horizontal, vertical);
            long high = Math.Max(horizontal, vertical);
            long difference = high - low;
            if (difference >= stepCost)
                return ClampCost(low + stepCost);
            long discriminant = 2L * stepCost * stepCost - difference * difference;
            long root = IntegerSqrt(discriminant);
            return ClampCost((low + high + root + 1L) / 2L);
        }

        private long ClampCost(long cost)
        {
            return cost < 0 || cost >= int.MaxValue ? int.MaxValue : cost;
        }

        private long IntegerSqrt(long value)
        {
            ulong remainder = (ulong)value;
            ulong result = 0;
            ulong bit = 1UL << 62;
            while (bit > remainder)
                bit >>= 2;
            while (bit != 0)
            {
                if (remainder >= result + bit)
                {
                    remainder -= result + bit;
                    result = (result >> 1) + bit;
                }
                else
                    result >>= 1;
                bit >>= 2;
            }
            return (long)result;
        }

        private bool IsInside(int worldX, int worldY)
        {
            return worldX >= StartX && worldX < StartX + Width && worldY >= StartY && worldY < StartY + Height;
        }

        private int LocalIndex(int worldX, int worldY)
        {
            return worldX - StartX + (worldY - StartY) * Width;
        }

        private int FindGoalIndex(int localIndex)
        {
            for (int i = 0; i < GoalLocalIndices.Length; i++)
            {
                if (GoalLocalIndices[i] == localIndex)
                    return i;
            }
            return -1;
        }

        private bool CanTraverse(int fromIndex, int toIndex, int dx, int dy)
        {
            if (!Walkable[fromIndex] || !Walkable[toIndex])
                return false;
            int offset = OffsetIndex(dx, dy);
            if (offset < 0 || (TraversalMask[fromIndex] & (1 << offset)) == 0)
                return false;
            if (dx == 0 || dy == 0)
                return true;
            int fromX = fromIndex % Width;
            int fromY = fromIndex / Width;
            int sideA = (fromX) + (fromY + dy) * Width;
            int sideB = (fromX + dx) + (fromY) * Width;
            if (fromX + dx < 0 || fromX + dx >= Width || fromY + dy < 0 || fromY + dy >= Height)
                return false;
            return Walkable[sideA]
                   && Walkable[sideB]
                   && HasTraversal(fromIndex, sideA, 0, dy)
                   && HasTraversal(fromIndex, sideB, dx, 0)
                   && HasTraversal(sideA, toIndex, dx, 0)
                   && HasTraversal(sideB, toIndex, 0, dy);
        }

        private bool HasTraversal(int fromIndex, int toIndex, int dx, int dy)
        {
            int offset = OffsetIndex(dx, dy);
            return offset >= 0
                   && (TraversalMask[fromIndex] & (1 << offset)) != 0
                   && Walkable[toIndex];
        }

        private static int OffsetIndex(int dx, int dy)
        {
            if (dx == -1 && dy == -1) return 0;
            if (dx == 0 && dy == -1) return 1;
            if (dx == 1 && dy == -1) return 2;
            if (dx == -1 && dy == 0) return 3;
            if (dx == 1 && dy == 0) return 4;
            if (dx == -1 && dy == 1) return 5;
            if (dx == 0 && dy == 1) return 6;
            if (dx == 1 && dy == 1) return 7;
            return -1;
        }

        private static int CardinalX(int index)
        {
            return index == 0 ? -1 : index == 1 ? 1 : 0;
        }

        private static int CardinalY(int index)
        {
            return index < 2 ? 0 : index == 2 ? -1 : 1;
        }

        private static int NeighborX(int index)
        {
            if (index == 0 || index == 3 || index == 5) return -1;
            return index == 1 || index == 6 ? 0 : 1;
        }

        private static int NeighborY(int index)
        {
            return index <= 2 ? -1 : index <= 4 ? 0 : 1;
        }
    }

    private sealed class MinHeap
    {
        private readonly List<QueueNode> _items = new List<QueueNode>(256);

        public int Count => _items.Count;

        public float PeekCost => _items.Count > 0 ? _items[0].Cost : float.PositiveInfinity;

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

        public void WriteDeterministicState(LogicStateHasher hasher)
        {
            if (hasher == null)
                throw new ArgumentNullException(nameof(hasher));

            hasher.Add(_items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                hasher.Add(_items[i].Index);
                hasher.Add(BitConverter.SingleToInt32Bits(_items[i].Cost));
            }
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (CompareQueueNodes(_items[parent], _items[index]) <= 0)
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
                if (left < _items.Count && CompareQueueNodes(_items[left], _items[best]) < 0)
                    best = left;
                if (right < _items.Count && CompareQueueNodes(_items[right], _items[best]) < 0)
                    best = right;
                if (best == index)
                    return;

                (_items[best], _items[index]) = (_items[index], _items[best]);
                index = best;
            }
        }

        private static int CompareQueueNodes(QueueNode left, QueueNode right)
        {
            int costOrder = left.Cost.CompareTo(right.Cost);
            return costOrder != 0 ? costOrder : left.Index.CompareTo(right.Index);
        }
    }

    private readonly struct DeterministicCostQueueNode
    {
        public DeterministicCostQueueNode(int index, long cost)
        {
            Index = index;
            Cost = cost;
        }

        public int Index { get; }
        public long Cost { get; }
    }

    private sealed class DeterministicCostHeap
    {
        private readonly List<DeterministicCostQueueNode> _items = new List<DeterministicCostQueueNode>(256);

        public int Count => _items.Count;
        public long PeekCost => _items.Count > 0 ? _items[0].Cost : long.MaxValue;
        public ulong AuthorityContentHash { get; private set; }

        public void Clear()
        {
            _items.Clear();
            AuthorityContentHash = 0;
        }

        public void Push(int index, long cost)
        {
            AuthorityContentHash ^= ComputeDeterministicCostQueueNodeAuthorityToken(index, cost);
            _items.Add(new DeterministicCostQueueNode(index, cost));
            SiftUp(_items.Count - 1);
        }

        public DeterministicCostQueueNode Pop()
        {
            if (_items.Count == 0)
                throw new InvalidOperationException("DeterministicCostHeap.Pop failed: heap is empty.");

            DeterministicCostQueueNode root = _items[0];
            AuthorityContentHash ^= ComputeDeterministicCostQueueNodeAuthorityToken(root.Index, root.Cost);
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            if (_items.Count > 0)
                SiftDown(0);
            return root;
        }

        public bool TryPeek(out DeterministicCostQueueNode node)
        {
            if (_items.Count == 0)
            {
                node = default;
                return false;
            }

            node = _items[0];
            return true;
        }

        public void ShiftCosts(long delta)
        {
            if (delta == 0L || _items.Count == 0)
                return;
            AuthorityContentHash = 0UL;
            for (int i = 0; i < _items.Count; i++)
            {
                DeterministicCostQueueNode item = _items[i];
                long shifted = checked(item.Cost + delta);
                _items[i] = new DeterministicCostQueueNode(item.Index, shifted);
                AuthorityContentHash ^= ComputeDeterministicCostQueueNodeAuthorityToken(item.Index, shifted);
            }
        }

        public void WriteDeterministicState(LogicStateHasher hasher)
        {
            if (hasher == null)
                throw new ArgumentNullException(nameof(hasher));

            hasher.Add(_items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                hasher.Add(_items[i].Index);
                hasher.Add(_items[i].Cost);
            }
        }

        public ulong ComputeAuthorityContentHashForValidation()
        {
            ulong contentHash = 0;
            for (int i = 0; i < _items.Count; i++)
                contentHash ^= ComputeDeterministicCostQueueNodeAuthorityToken(_items[i].Index, _items[i].Cost);
            return contentHash;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (Compare(_items[parent], _items[index]) <= 0)
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
                if (left < _items.Count && Compare(_items[left], _items[best]) < 0)
                    best = left;
                if (right < _items.Count && Compare(_items[right], _items[best]) < 0)
                    best = right;
                if (best == index)
                    return;

                (_items[best], _items[index]) = (_items[index], _items[best]);
                index = best;
            }
        }

        private static int Compare(DeterministicCostQueueNode left, DeterministicCostQueueNode right)
        {
            int costOrder = left.Cost.CompareTo(right.Cost);
            return costOrder != 0 ? costOrder : left.Index.CompareTo(right.Index);
        }
    }

}
