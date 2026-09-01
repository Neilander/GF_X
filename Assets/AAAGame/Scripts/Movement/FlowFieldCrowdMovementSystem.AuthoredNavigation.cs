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
    public static void SetAuthoredNavigationSource(int width, int height, float cellSize, Vector3 origin, bool[] walkableMask)
    {
        SetAuthoredNavigationSource(AnyAgentTypeId, width, height, cellSize, origin, walkableMask, null, null, null);
    }

    public static void SetAuthoredNavigationSource(int agentTypeId, int width, int height, float cellSize, Vector3 origin, bool[] walkableMask, Vector3[] cellNavAnchors = null, byte[] costField = null, byte[] neighborTraversalMask = null, FlowNavigationGridAsset.DerivedNavigationData derivedNavigationData = null, bool useRuntimeReadOnlyReferences = false)
    {
        EnsureAuthoredNavigationMutationBoundary(nameof(SetAuthoredNavigationSource));
        TestTerrainOverride source = CreateTerrainOverride(agentTypeId, width, height, cellSize, origin, walkableMask, cellNavAnchors, costField, neighborTraversalMask, derivedNavigationData, useRuntimeReadOnlyReferences);
        _testTerrainOverride = source;
        AuthoredTerrainSources.Clear();
        AuthoredTerrainSources[source.AgentTypeId] = source;
        MarkWorldDirty("authored-navigation-source");
    }

    public static void SetAuthoredNavigationSourceFixed(
        int agentTypeId,
        int width,
        int height,
        float cellSize,
        Vector3 origin,
        long cellSizeGridRaw,
        long originXGridRaw,
        long originZGridRaw,
        bool[] walkableMask,
        Vector3[] cellNavAnchors,
        FixVector2[] cellNavAnchorsFixedXZ,
        byte[] costField,
        byte[] neighborTraversalMask,
        FlowNavigationGridAsset.DerivedNavigationData derivedNavigationData,
        bool useRuntimeReadOnlyReferences,
        FixVector2[] staticCollisionVertices,
        int[] staticCollisionPathStarts)
    {
        EnsureAuthoredNavigationMutationBoundary(nameof(SetAuthoredNavigationSourceFixed));
        TestTerrainOverride source = CreateTerrainOverride(
            agentTypeId,
            width,
            height,
            cellSize,
            origin,
            walkableMask,
            cellNavAnchors,
            costField,
            neighborTraversalMask,
            derivedNavigationData,
            useRuntimeReadOnlyReferences,
            cellSizeGridRaw,
            originXGridRaw,
            originZGridRaw,
            cellNavAnchorsFixedXZ,
            hasFixedAuthorityPayload: true,
            staticCollisionVertices: staticCollisionVertices,
            staticCollisionPathStarts: staticCollisionPathStarts);
        _testTerrainOverride = source;
        AuthoredTerrainSources.Clear();
        AuthoredTerrainSources[source.AgentTypeId] = source;
        MarkWorldDirty("authored-navigation-source-fixed");
    }

    public static void SetAuthoredNavigationSources(IReadOnlyList<AuthoredNavigationSourceData> sources)
    {
        EnsureAuthoredNavigationMutationBoundary(nameof(SetAuthoredNavigationSources));
        if (sources == null)
            throw new InvalidOperationException("SetAuthoredNavigationSources failed: sources is null.");
        if (sources.Count == 0)
            throw new InvalidOperationException("SetAuthoredNavigationSources failed: sources is empty.");

        Dictionary<int, TestTerrainOverride> nextSources = new Dictionary<int, TestTerrainOverride>(sources.Count);
        for (int i = 0; i < sources.Count; i++)
        {
            AuthoredNavigationSourceData source = sources[i];
            TestTerrainOverride terrain = CreateTerrainOverride(
                source.AgentTypeId,
                source.Width,
                source.Height,
                source.CellSize,
                source.Origin,
                source.WalkableMask,
                source.CellNavAnchors,
                source.CostField,
                source.NeighborTraversalMask,
                source.DerivedNavigationData,
                source.UseRuntimeReadOnlyReferences,
                source.CellSizeGridRaw,
                source.OriginXGridRaw,
                source.OriginZGridRaw,
                source.CellNavAnchorsFixedXZ,
                source.HasFixedAuthorityPayload,
                staticCollisionVertices: source.StaticCollisionVertices,
                staticCollisionPathStarts: source.StaticCollisionPathStarts);
            if (nextSources.ContainsKey(terrain.AgentTypeId))
                throw new InvalidOperationException($"SetAuthoredNavigationSources failed: duplicate agentTypeId={terrain.AgentTypeId}.");

            nextSources.Add(terrain.AgentTypeId, terrain);
        }

        AuthoredTerrainSources.Clear();
        foreach (KeyValuePair<int, TestTerrainOverride> pair in nextSources)
            AuthoredTerrainSources.Add(pair.Key, pair.Value);

        _testTerrainOverride = ResolveDefaultAuthoredTerrainSource();
        MarkWorldDirty("authored-navigation-source");
    }

    public static void ClearAuthoredNavigationSource()
    {
        EnsureAuthoredNavigationMutationBoundary(nameof(ClearAuthoredNavigationSource));
        _testTerrainOverride = null;
        AuthoredTerrainSources.Clear();
        MarkWorldDirty("authored-navigation-source-cleared");
    }

    private static void EnsureAuthoredNavigationMutationBoundary(string operation)
    {
        if (LogicFrameRuntime.IsTimelineRunning && !IsRuntimeNavigationTransitionActive())
        {
            throw new InvalidOperationException(
                $"{operation} failed: authored navigation source cannot change while the logic timeline is running.");
        }
    }

    public static bool HasAuthoredNavigationSource()
    {
        return _testTerrainOverride != null || AuthoredTerrainSources.Count > 0;
    }

#if UNITY_EDITOR
    public static FlowNavigationGridAsset.DerivedNavigationData BuildDerivedNavigationDataForAsset(
        int agentTypeId,
        Fix64 agentRadiusFixed,
        int width,
        int height,
        float cellSize,
        Vector3 origin,
        bool[] walkableMask,
        Vector3[] cellNavAnchors,
        byte[] costField,
        byte[] neighborTraversalMask)
    {
        TestTerrainOverride source = CreateTerrainOverride(
            agentTypeId,
            width,
            height,
            cellSize,
            origin,
            walkableMask,
            cellNavAnchors,
            costField,
            neighborTraversalMask,
            agentRadiusOverrideFixed: agentRadiusFixed);
        WorldBuildJob job = new WorldBuildJob
        {
            AgentTypeId = source.AgentTypeId,
            Width = source.Width,
            Height = source.Height,
            CellSize = source.CellSize,
            EncodedCenterClearance = source.EncodedCenterClearance,
            Origin = source.Origin,
            BaseWalkableMask = (bool[])source.WalkableMask.Clone(),
            BaseCostField = source.CostField != null ? (byte[])source.CostField.Clone() : null,
            BaseNeighborTraversalMask = source.NeighborTraversalMask != null ? (byte[])source.NeighborTraversalMask.Clone() : null,
            StaticCollisionVertices = source.StaticCollisionVertices != null ? (FixVector2[])source.StaticCollisionVertices.Clone() : null,
            StaticCollisionPathStarts = source.StaticCollisionPathStarts != null ? (int[])source.StaticCollisionPathStarts.Clone() : null,
            CellNavAnchors = source.CellNavAnchors != null ? (Vector3[])source.CellNavAnchors.Clone() : null,
            HasProvidedCellNavAnchors = source.CellNavAnchors != null,
            HasProvidedNeighborTraversalMask = source.NeighborTraversalMask != null,
            IncludeRuntimeObstacles = false,
            Stage = WorldBuildStage.CreateWorldShell,
            Reason = "editor-derived-navigation-bake"
        };
        job.CellNavAnchorsFixedXZ = CreateNavigationAnchorFixedXZSnapshot(job.CellNavAnchors);
        job.FreezeAuthorityGridMetadata(source.AgentRadiusFixedRaw);

        CreateWorldBuildShell(job);
        ProcessWorldBuildObstacles(job, long.MaxValue, forceComplete: true);
        InitializeWorldBuildSectors(job);
        ProcessWorldBuildCellNavAnchors(job, long.MaxValue, forceComplete: true);
        ProcessWorldBuildNeighborMask(job, long.MaxValue, forceComplete: true);
        ProcessWorldBuildSymmetrizeNeighborMask(job, long.MaxValue, forceComplete: true);
        ProcessWorldBuildCostField(job, long.MaxValue, forceComplete: true);
        ProcessWorldBuildIslandField(job, long.MaxValue, forceComplete: true);
        BuildGoalProjectionSpatialIndexImmediate(job.WorkingWorld);
        ProcessWorldBuildPortalGraph(job, long.MaxValue, forceComplete: true);
        if (job.WorkingWorld == null)
            throw new InvalidOperationException("BuildDerivedNavigationDataForAsset failed: working world is null after build.");
        job.WorkingWorld.Version = AllocateEditorBakeNavigationWorldVersion();
        try
        {
            CommitPendingSectorPortalAccessEntries(
                job.WorkingWorld,
                job.PendingPortalAccessEntries,
                entriesAreFinalized: false);
            EnsureAllSectorPortalAccessCoverage(job.WorkingWorld, "editor-derived-navigation-bake");
            FinalizeWorldCostStorage(job.WorkingWorld);
            RebuildDeterministicPortalTransitionCosts(job.WorkingWorld);
            ValidateAllSectorPortalAccessCoverage(job.WorkingWorld, "editor-derived-navigation-bake");
            job.WorkingWorld.Hierarchy = BuildPortalHierarchy(job.WorkingWorld, DefaultHierarchyFanout);

            return ExportDerivedNavigationData(job.WorkingWorld);
        }
        finally
        {
            RemoveSectorPortalAccessCacheEntriesForWorldVersion(job.WorkingWorld.Version);
        }
    }
#endif

    private static TestTerrainOverride CreateTerrainOverride(
        int agentTypeId,
        int width,
        int height,
        float cellSize,
        Vector3 origin,
        bool[] walkableMask,
        Vector3[] cellNavAnchors,
        byte[] costField,
        byte[] neighborTraversalMask,
        FlowNavigationGridAsset.DerivedNavigationData derivedNavigationData = null,
        bool useRuntimeReadOnlyReferences = false,
        long cellSizeGridRaw = 0,
        long originXGridRaw = 0,
        long originZGridRaw = 0,
        FixVector2[] cellNavAnchorsFixedXZ = null,
        bool hasFixedAuthorityPayload = false,
        Fix64? agentRadiusOverrideFixed = null,
        FixVector2[] staticCollisionVertices = null,
        int[] staticCollisionPathStarts = null)
    {
        if (agentTypeId == MAEntity.UnknownNavAgentTypeId)
            throw new InvalidOperationException("CreateTerrainOverride failed: explicit agentTypeId is Unknown.");
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"CreateTerrainOverride failed: invalid size {width}x{height}.");
        if (cellSize <= 0.0001f)
            throw new InvalidOperationException($"CreateTerrainOverride failed: invalid cellSize={cellSize:F4}.");
        if (walkableMask == null)
            throw new InvalidOperationException("CreateTerrainOverride failed: walkableMask is null.");
        if (walkableMask.Length != width * height)
            throw new InvalidOperationException(
                $"CreateTerrainOverride failed: mask length {walkableMask.Length} does not match {width}x{height}.");
        if (cellNavAnchors != null && cellNavAnchors.Length != width * height)
            throw new InvalidOperationException(
                $"CreateTerrainOverride failed: anchor length {cellNavAnchors.Length} does not match {width}x{height}.");
        if (costField != null && costField.Length != width * height)
            throw new InvalidOperationException(
                $"CreateTerrainOverride failed: cost length {costField.Length} does not match {width}x{height}.");
        if (neighborTraversalMask != null && neighborTraversalMask.Length != width * height)
            throw new InvalidOperationException(
                $"CreateTerrainOverride failed: neighbor traversal length {neighborTraversalMask.Length} does not match {width}x{height}.");
        bool hasStaticCollisionGeometry = staticCollisionVertices != null || staticCollisionPathStarts != null;
        ValidateStaticCollisionGeometry(
            staticCollisionVertices,
            staticCollisionPathStarts,
            hasStaticCollisionGeometry,
            "CreateTerrainOverride");
        if (derivedNavigationData != null)
            ValidateDerivedNavigationDataMetadata(derivedNavigationData, agentTypeId, width, height, cellSize, origin, "CreateTerrainOverride");

        if (hasFixedAuthorityPayload)
        {
            if (!hasStaticCollisionGeometry)
                throw new InvalidOperationException("CreateTerrainOverride failed: fixed authored navigation requires deterministic static collision geometry.");
            if (cellSizeGridRaw <= 0)
                throw new InvalidOperationException("CreateTerrainOverride failed: fixed authority cell size must be positive.");
            if (cellSizeGridRaw != NavigationGridFixedMath.FloatToGridRaw(cellSize)
                || originXGridRaw != NavigationGridFixedMath.FloatToGridRaw(origin.x)
                || originZGridRaw != NavigationGridFixedMath.FloatToGridRaw(origin.z))
            {
                throw new InvalidOperationException("CreateTerrainOverride failed: fixed authority metadata does not match the authored float shadow.");
            }
            if (cellNavAnchorsFixedXZ == null || cellNavAnchorsFixedXZ.Length != width * height)
            {
                throw new InvalidOperationException(
                    $"CreateTerrainOverride failed: fixed anchor length does not match {width}x{height}. actual={cellNavAnchorsFixedXZ?.Length ?? -1}.");
            }
            if (derivedNavigationData == null
                || derivedNavigationData.CellSizeGridRaw != cellSizeGridRaw
                || derivedNavigationData.OriginXGridRaw != originXGridRaw
                || derivedNavigationData.OriginZGridRaw != originZGridRaw)
            {
                throw new InvalidOperationException("CreateTerrainOverride failed: derived navigation fixed metadata does not match the authored payload.");
            }
        }

        bool hasAgentTypeEncodedClearance = derivedNavigationData != null || agentTypeId != AnyAgentTypeId;
        Fix64 agentRadiusFixed = agentRadiusOverrideFixed ?? ResolveAgentTypeRadiusFixed(agentTypeId);
        if (agentRadiusFixed <= Fix64.Zero)
            throw new InvalidOperationException($"CreateTerrainOverride failed: agent radius must be positive. agentType={agentTypeId}, raw={agentRadiusFixed.RawValue}.");
        Fix64 encodedCenterClearanceFixed = hasAgentTypeEncodedClearance
            ? Fix64.Max(
                Fix64.Zero,
                agentRadiusFixed - (hasFixedAuthorityPayload
                    ? NavigationGridFixedMath.GridRawToFix64(cellSizeGridRaw)
                    : (Fix64)cellSize) * Fix64.FromRaw(820))
            : Fix64.Zero;
        float encodedCenterClearance = hasFixedAuthorityPayload
            ? (float)encodedCenterClearanceFixed
            : hasAgentTypeEncodedClearance
                ? Mathf.Max(0f, (float)agentRadiusFixed - cellSize * 0.2f)
                : 0f;

        return new TestTerrainOverride
        {
            AgentTypeId = agentTypeId,
            Width = width,
            Height = height,
            CellSize = cellSize,
            EncodedCenterClearance = encodedCenterClearance,
            Origin = origin,
            CellSizeGridRaw = cellSizeGridRaw,
            EncodedCenterClearanceFixedRaw = hasFixedAuthorityPayload
                ? encodedCenterClearanceFixed.RawValue
                : ((Fix64)encodedCenterClearance).RawValue,
            AgentRadiusFixedRaw = agentRadiusFixed.RawValue,
            OriginXGridRaw = originXGridRaw,
            OriginZGridRaw = originZGridRaw,
            HasFixedAuthorityPayload = hasFixedAuthorityPayload,
            WalkableMask = useRuntimeReadOnlyReferences ? walkableMask : (bool[])walkableMask.Clone(),
            CellNavAnchors = cellNavAnchors != null
                ? useRuntimeReadOnlyReferences ? cellNavAnchors : (Vector3[])cellNavAnchors.Clone()
                : null,
            CellNavAnchorsFixedXZ = cellNavAnchorsFixedXZ != null
                ? useRuntimeReadOnlyReferences ? cellNavAnchorsFixedXZ : (FixVector2[])cellNavAnchorsFixedXZ.Clone()
                : null,
            CostField = costField != null
                ? useRuntimeReadOnlyReferences ? costField : (byte[])costField.Clone()
                : null,
            NeighborTraversalMask = neighborTraversalMask != null
                ? useRuntimeReadOnlyReferences ? neighborTraversalMask : (byte[])neighborTraversalMask.Clone()
                : null,
            StaticCollisionVertices = hasStaticCollisionGeometry
                ? useRuntimeReadOnlyReferences ? staticCollisionVertices : (FixVector2[])staticCollisionVertices.Clone()
                : null,
            StaticCollisionPathStarts = hasStaticCollisionGeometry
                ? useRuntimeReadOnlyReferences ? staticCollisionPathStarts : (int[])staticCollisionPathStarts.Clone()
                : null,
            DerivedNavigationData = derivedNavigationData
        };
    }

    private static void ValidateDerivedNavigationDataMetadata(
        FlowNavigationGridAsset.DerivedNavigationData data,
        int agentTypeId,
        int width,
        int height,
        float cellSize,
        Vector3 origin,
        string caller)
    {
        if (data == null || !data.IsValid)
            throw new InvalidOperationException($"{caller} failed: derived navigation data is invalid.");
        if (data.AgentTypeId != agentTypeId
            || data.Width != width
            || data.Height != height
            || !Mathf.Approximately(data.CellSize, cellSize)
            || data.Origin != origin)
        {
            throw new InvalidOperationException(
                $"{caller} failed: derived navigation data metadata does not match source. " +
                $"source=(agent={agentTypeId},size={width}x{height},cell={cellSize:F4},origin={origin}) " +
                $"derived=(agent={data.AgentTypeId},size={data.Width}x{data.Height},cell={data.CellSize:F4},origin={data.Origin}).");
        }
    }

    private static void ValidateStaticCollisionGeometry(
        FixVector2[] vertices,
        int[] pathStarts,
        bool isProvided,
        string caller)
    {
        if (!isProvided)
            return;
        if (vertices == null || vertices.Length < 3)
            throw new InvalidOperationException($"{caller} failed: static collision vertices are missing.");
        if (pathStarts == null || pathStarts.Length < 2 || pathStarts[0] != 0 || pathStarts[pathStarts.Length - 1] != vertices.Length)
            throw new InvalidOperationException($"{caller} failed: static collision path starts are invalid.");
        for (int pathIndex = 0; pathIndex < pathStarts.Length - 1; pathIndex++)
        {
            int start = pathStarts[pathIndex];
            int end = pathStarts[pathIndex + 1];
            if (start < 0 || end > vertices.Length || end - start < 3)
                throw new InvalidOperationException($"{caller} failed: static collision path {pathIndex} is invalid. start={start} end={end}.");
            for (int i = start; i < end; i++)
            {
                if (vertices[i] == vertices[i + 1 < end ? i + 1 : start])
                    throw new InvalidOperationException($"{caller} failed: static collision path {pathIndex} contains a zero-length edge at vertex={i}.");
            }
        }
    }

    private static void ValidateDerivedNavigationDataAgainstRuntimeConfig(FlowNavigationGridAsset.DerivedNavigationData data, string caller)
    {
        if (data.ConfigSectorWorldSizeMillimeters != Config.SectorWorldSizeMillimeters
            || data.ConfigPortalNarrowWidthCells != Config.PortalNarrowWidthCells
            || data.ConfigPortalMaxWindowWidthCells != Config.PortalMaxWindowWidthCells)
        {
            throw new InvalidOperationException(
                $"{caller} failed: derived navigation data was baked with different flow graph config. " +
                $"baked=(sectorMm={data.ConfigSectorWorldSizeMillimeters},narrow={data.ConfigPortalNarrowWidthCells},maxWindow={data.ConfigPortalMaxWindowWidthCells}) " +
                $"runtime=(sectorMm={Config.SectorWorldSizeMillimeters},narrow={Config.PortalNarrowWidthCells},maxWindow={Config.PortalMaxWindowWidthCells}). " +
                "Regenerate the FlowNavigationGridAsset with the current FlowFieldNavigationConfig.");
        }

        int expectedSectorSize = ResolveRuntimeSectorSizeInCells(data.CellSizeGridRaw);
        if (data.SectorSizeInCells != expectedSectorSize)
        {
            throw new InvalidOperationException(
                $"{caller} failed: derived sector size mismatch. baked={data.SectorSizeInCells} runtimeExpected={expectedSectorSize} cellSize={data.CellSize:F4}.");
        }
    }

    private static FlowNavigationGridAsset.DerivedNavigationData ExportDerivedNavigationData(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("ExportDerivedNavigationData failed: world is null.");
        if (world.Sectors == null || world.Portals == null || world.IslandIds == null)
            throw new InvalidOperationException("ExportDerivedNavigationData failed: world derived fields are incomplete.");

        FlowNavigationGridAsset.DerivedNavigationData data = new FlowNavigationGridAsset.DerivedNavigationData
        {
            Version = FlowNavigationGridAsset.DerivedNavigationData.CurrentVersion,
            AgentTypeId = world.AgentTypeId,
            Width = world.Width,
            Height = world.Height,
            CellSize = world.CellSize,
            Origin = world.Origin,
            CellSizeGridRaw = world.CellSizeGridRaw,
            OriginXGridRaw = world.OriginXGridRaw,
            OriginZGridRaw = world.OriginZGridRaw,
            ConfigSectorWorldSizeMillimeters = Config.SectorWorldSizeMillimeters,
            ConfigPortalNarrowWidthCells = Config.PortalNarrowWidthCells,
            ConfigPortalMaxWindowWidthCells = Config.PortalMaxWindowWidthCells,
            SectorSizeInCells = world.SectorSizeInCells,
            SectorCountX = world.SectorCountX,
            SectorCountY = world.SectorCountY,
            IslandCount = world.IslandCount,
            MainIslandId = world.MainIslandId,
            MainIslandSize = world.MainIslandSize,
            NextPortalId = world.NextPortalId,
            IslandIds = (int[])world.IslandIds.Clone(),
            Sectors = ExportDerivedSectors(world.Sectors),
            Portals = ExportDerivedPortals(world.Portals),
            Hierarchy = ExportPortalHierarchy(world.Hierarchy)
        };
        if (!data.IsValid)
            throw new InvalidOperationException("ExportDerivedNavigationData failed: exported data is invalid.");

        return data;
    }

    private static FlowNavigationGridAsset.SectorDerivedData[] ExportDerivedSectors(SectorData[] sectors)
    {
        FlowNavigationGridAsset.SectorDerivedData[] result = new FlowNavigationGridAsset.SectorDerivedData[sectors.Length];
        for (int i = 0; i < sectors.Length; i++)
        {
            SectorData sector = sectors[i];
            if (sector == null)
                throw new InvalidOperationException($"ExportDerivedSectors failed: sector is null at index={i}.");

            FlowNavigationGridAsset.PortalTransitionDerivedData[] transitions = new FlowNavigationGridAsset.PortalTransitionDerivedData[sector.PortalTransitions.Count];
            for (int j = 0; j < transitions.Length; j++)
            {
                PortalTransition transition = sector.PortalTransitions[j];
                transitions[j] = new FlowNavigationGridAsset.PortalTransitionDerivedData
                {
                    FromPortalId = transition.FromPortalId,
                    ToPortalId = transition.ToPortalId,
                    Cost = DequantizeDeterministicPortalCost(transition.DeterministicCost),
                    DeterministicCost = transition.DeterministicCost
                };
            }

            result[i] = new FlowNavigationGridAsset.SectorDerivedData
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
                LocalComponentIds = sector.LocalComponentIds != null ? (int[])sector.LocalComponentIds.Clone() : Array.Empty<int>(),
                LocalComponentCount = sector.LocalComponentCount,
                PortalIds = sector.PortalIds.ToArray(),
                PortalTransitions = transitions
            };
        }

        return result;
    }

    private static FlowNavigationGridAsset.PortalDerivedData[] ExportDerivedPortals(PortalData[] portals)
    {
        FlowNavigationGridAsset.PortalDerivedData[] result = new FlowNavigationGridAsset.PortalDerivedData[portals.Length];
        for (int i = 0; i < portals.Length; i++)
        {
            PortalData portal = portals[i];
            if (portal == null)
                throw new InvalidOperationException($"ExportDerivedPortals failed: portal is null at index={i}.");

            result[i] = new FlowNavigationGridAsset.PortalDerivedData
            {
                PortalId = portal.PortalId,
                SectorAId = portal.SectorAId,
                SectorBId = portal.SectorBId,
                CellsA = portal.CellsA != null ? (Vector2Int[])portal.CellsA.Clone() : Array.Empty<Vector2Int>(),
                CellsB = portal.CellsB != null ? (Vector2Int[])portal.CellsB.Clone() : Array.Empty<Vector2Int>(),
                WorldCenter = portal.WorldCenter,
                WidthCells = portal.WidthCells,
                IsNarrow = portal.IsNarrow,
                IsVerticalBoundary = portal.IsVerticalBoundary
            };
        }

        return result;
    }

    private static NavigationWorld ImportDerivedNavigationWorld(TestTerrainOverride source)
    {
        if (source == null)
            throw new InvalidOperationException("ImportDerivedNavigationWorld failed: source is null.");
        FlowNavigationGridAsset.DerivedNavigationData data = source.DerivedNavigationData;
        ValidateDerivedNavigationDataMetadata(data, source.AgentTypeId, source.Width, source.Height, source.CellSize, source.Origin, "ImportDerivedNavigationWorld");
        ValidateDerivedNavigationDataAgainstRuntimeConfig(data, "ImportDerivedNavigationWorld");

        int cellCount = source.Width * source.Height;
        if (source.WalkableMask == null || source.WalkableMask.Length != cellCount)
            throw new InvalidOperationException("ImportDerivedNavigationWorld failed: source walkable mask is invalid.");
        if (source.NeighborTraversalMask == null || source.NeighborTraversalMask.Length != cellCount)
            throw new InvalidOperationException("ImportDerivedNavigationWorld failed: source neighbor traversal mask is required for prebaked worlds.");
        if (source.CostField == null || source.CostField.Length != cellCount)
            throw new InvalidOperationException("ImportDerivedNavigationWorld failed: source cost field is required for prebaked worlds.");
        if (source.CellNavAnchors == null || source.CellNavAnchors.Length != cellCount)
            throw new InvalidOperationException("ImportDerivedNavigationWorld failed: source cell anchors are required for prebaked worlds.");

        NavigationWorld world = new NavigationWorld
        {
            AgentTypeId = source.AgentTypeId,
            Width = source.Width,
            Height = source.Height,
            CellSize = source.CellSize,
            EncodedCenterClearance = source.EncodedCenterClearance,
            Origin = source.Origin,
            BaseWalkableMask = (bool[])source.WalkableMask.Clone(),
            BaseNeighborTraversalMask = (byte[])source.NeighborTraversalMask.Clone(),
            StaticCollisionVertices = source.StaticCollisionVertices != null ? (FixVector2[])source.StaticCollisionVertices.Clone() : null,
            StaticCollisionPathStarts = source.StaticCollisionPathStarts != null ? (int[])source.StaticCollisionPathStarts.Clone() : null,
            SourceCostField = (byte[])source.CostField.Clone(),
            WalkableMask = (bool[])source.WalkableMask.Clone(),
            CostField = (byte[])source.CostField.Clone(),
            SectorCostFields = null,
            CellNavAnchors = (Vector3[])source.CellNavAnchors.Clone(),
            CellNavAnchorsFixedXZ = source.HasFixedAuthorityPayload
                ? (FixVector2[])source.CellNavAnchorsFixedXZ.Clone()
                : CreateNavigationAnchorFixedXZSnapshot(source.CellNavAnchors),
            NeighborTraversalMask = (byte[])source.NeighborTraversalMask.Clone(),
            IslandIds = (int[])data.IslandIds.Clone(),
            IslandCount = data.IslandCount,
            MainIslandId = data.MainIslandId,
            MainIslandSize = data.MainIslandSize,
            SectorSizeInCells = data.SectorSizeInCells,
            SectorCountX = data.SectorCountX,
            SectorCountY = data.SectorCountY,
            Sectors = ImportDerivedSectors(data.Sectors),
            Portals = ImportDerivedPortals(data.Portals),
            PortalsById = new Dictionary<int, PortalData>(data.Portals.Length),
            NextPortalId = data.NextPortalId
        };
        if (source.HasFixedAuthorityPayload)
        {
            world.SetAuthorityGridMetadata(
                source.CellSizeGridRaw,
                source.EncodedCenterClearanceFixedRaw,
                source.AgentRadiusFixedRaw,
                source.OriginXGridRaw,
                source.OriginZGridRaw);
        }
        else
        {
            world.FreezeAuthorityGridMetadata();
        }

        for (int index = 0; index < cellCount; index++)
        {
            SymmetrizeNeighborTraversalMaskCell(
                world,
                index % world.Width,
                index / world.Width,
                "ImportDerivedNavigationWorld");
        }
        PruneIsolatedWalkableCells(world, "prebaked-import");
        world.BaseWalkableMask = (bool[])world.WalkableMask.Clone();
        world.BaseNeighborTraversalMask = (byte[])world.NeighborTraversalMask.Clone();
        RebuildAllSectorLocalComponents(world);

        for (int i = 0; i < world.Portals.Length; i++)
        {
            PortalData portal = world.Portals[i];
            if (world.PortalsById.ContainsKey(portal.PortalId))
                throw new InvalidOperationException($"ImportDerivedNavigationWorld failed: duplicate portal id {portal.PortalId}.");

            world.PortalsById.Add(portal.PortalId, portal);
            world.UsedPortalIds.Add(portal.PortalId);
            world.PortalIdsBySignature.Add(BuildPortalSignature(portal.SectorAId, portal.SectorBId, portal.CellsA, portal.IsVerticalBoundary), portal.PortalId);
        }

        world.Hierarchy = ImportPortalHierarchy(world, data.Hierarchy);

        ValidateImportedDerivedNavigationWorld(world);
        ReplaceFlowTileWorldInputBackings(world);
        return world;
    }

#if UNITY_EDITOR
    private static NavigationWorld GetOrCreateEditorNavigationPreviewWorld(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset staticCollisionSourceGrid)
    {
        if (staticCollisionSourceGrid == null)
            throw new ArgumentNullException(nameof(staticCollisionSourceGrid));
        if (!staticCollisionSourceGrid.HasStaticCollisionGeometry)
        {
            throw new InvalidOperationException(
                $"Navigation preview collision source grid '{staticCollisionSourceGrid.name}' has no deterministic static collision geometry.");
        }

        int gridInstanceId = grid.GetInstanceID();
        FlowNavigationGridAsset.DerivedNavigationData derivedData =
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        bool[] walkableMask = grid.GetWalkableMaskRuntimeReadOnlyReference();
        byte[] neighborTraversalMask = grid.GetNeighborTraversalMaskRuntimeReadOnlyReference();
        FixVector2[] staticCollisionVertices =
            staticCollisionSourceGrid.GetStaticCollisionVerticesRuntimeReadOnlyReference();
        int[] staticCollisionPathStarts =
            staticCollisionSourceGrid.GetStaticCollisionPathStartsRuntimeReadOnlyReference();
        if (derivedData == null || !derivedData.IsValid)
            throw new InvalidOperationException($"Navigation preview grid '{grid.name}' has no valid derived navigation data.");
        if (neighborTraversalMask == null)
            throw new InvalidOperationException($"Navigation preview grid '{grid.name}' has no authored traversal mask.");

        if (EditorNavigationPreviewWorlds.TryGetValue(gridInstanceId, out EditorNavigationPreviewWorld cached)
            && cached.Grid == grid
            && cached.StaticCollisionSourceGrid == staticCollisionSourceGrid
            && ReferenceEquals(cached.DerivedNavigationData, derivedData)
            && ReferenceEquals(cached.WalkableMask, walkableMask)
            && ReferenceEquals(cached.NeighborTraversalMask, neighborTraversalMask)
            && ReferenceEquals(cached.StaticCollisionVertices, staticCollisionVertices)
            && ReferenceEquals(cached.StaticCollisionPathStarts, staticCollisionPathStarts))
        {
            return cached.World;
        }

        FlowNavigationGridAsset.FixedAuthorityMetadata fixedMetadata = grid.GetFixedAuthorityMetadata();
        Fix64 previewRadius = NavigationGridFixedMath.GridRawToFix64(fixedMetadata.CellSizeGridRaw);
        TestTerrainOverride source = CreateTerrainOverride(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            walkableMask,
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            neighborTraversalMask,
            derivedData,
            useRuntimeReadOnlyReferences: true,
            cellSizeGridRaw: fixedMetadata.CellSizeGridRaw,
            originXGridRaw: fixedMetadata.OriginXGridRaw,
            originZGridRaw: fixedMetadata.OriginZGridRaw,
            cellNavAnchorsFixedXZ: grid.GetCellAnchorsFixedRuntimeReadOnlyReference(),
            hasFixedAuthorityPayload: true,
            agentRadiusOverrideFixed: previewRadius,
            staticCollisionVertices: staticCollisionVertices,
            staticCollisionPathStarts: staticCollisionPathStarts);
        NavigationWorld world = ImportDerivedNavigationWorld(source);
        EditorNavigationPreviewWorlds[gridInstanceId] = new EditorNavigationPreviewWorld
        {
            Grid = grid,
            StaticCollisionSourceGrid = staticCollisionSourceGrid,
            DerivedNavigationData = derivedData,
            WalkableMask = walkableMask,
            NeighborTraversalMask = neighborTraversalMask,
            StaticCollisionVertices = staticCollisionVertices,
            StaticCollisionPathStarts = staticCollisionPathStarts,
            World = world
        };
        return world;
    }
#endif

    private static SectorData[] ImportDerivedSectors(FlowNavigationGridAsset.SectorDerivedData[] source)
    {
        if (source == null)
            throw new InvalidOperationException("ImportDerivedSectors failed: source is null.");

        SectorData[] result = new SectorData[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            FlowNavigationGridAsset.SectorDerivedData sector = source[i];
            if (sector == null)
                throw new InvalidOperationException($"ImportDerivedSectors failed: sector is null at index={i}.");

            SectorData imported = new SectorData
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
                LocalComponentIds = sector.LocalComponentIds != null ? (int[])sector.LocalComponentIds.Clone() : Array.Empty<int>(),
                LocalComponentCount = sector.LocalComponentCount
            };
            if (sector.PortalIds != null)
                imported.PortalIds.AddRange(sector.PortalIds);
            if (sector.PortalTransitions != null)
            {
                for (int j = 0; j < sector.PortalTransitions.Length; j++)
                {
                    FlowNavigationGridAsset.PortalTransitionDerivedData transition = sector.PortalTransitions[j];
                    if (transition == null)
                        throw new InvalidOperationException($"ImportDerivedSectors failed: transition is null sector={sector.SectorId} index={j}.");

                    imported.PortalTransitions.Add(new PortalTransition
                    {
                        FromPortalId = transition.FromPortalId,
                        ToPortalId = transition.ToPortalId,
                        DeterministicCost = transition.DeterministicCost
                    });
                }
            }
            RebuildIncomingPortalTransitionIndex(imported);

            result[i] = imported;
        }

        return result;
    }

    private static PortalData[] ImportDerivedPortals(FlowNavigationGridAsset.PortalDerivedData[] source)
    {
        if (source == null)
            throw new InvalidOperationException("ImportDerivedPortals failed: source is null.");

        PortalData[] result = new PortalData[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            FlowNavigationGridAsset.PortalDerivedData portal = source[i];
            if (portal == null)
                throw new InvalidOperationException($"ImportDerivedPortals failed: portal is null at index={i}.");
            if (portal.CellsA == null || portal.CellsA.Length == 0 || portal.CellsB == null || portal.CellsB.Length == 0)
                throw new InvalidOperationException($"ImportDerivedPortals failed: portal cells are invalid portal={portal.PortalId}.");
            if (portal.CellsA.Length != portal.CellsB.Length)
                throw new InvalidOperationException($"ImportDerivedPortals failed: portal side count mismatch portal={portal.PortalId} a={portal.CellsA.Length} b={portal.CellsB.Length}.");

            result[i] = new PortalData
            {
                PortalId = portal.PortalId,
                SectorAId = portal.SectorAId,
                SectorBId = portal.SectorBId,
                CellsA = (Vector2Int[])portal.CellsA.Clone(),
                CellsB = (Vector2Int[])portal.CellsB.Clone(),
                WorldCenter = portal.WorldCenter,
                WidthCells = portal.WidthCells,
                IsNarrow = portal.IsNarrow,
                IsVerticalBoundary = portal.IsVerticalBoundary
            };
        }

        return result;
    }

    private static void ValidateImportedDerivedNavigationWorld(NavigationWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("ValidateImportedDerivedNavigationWorld failed: world is null.");
        int cellCount = world.Width * world.Height;
        if (world.IslandIds == null || world.IslandIds.Length != cellCount)
            throw new InvalidOperationException("ValidateImportedDerivedNavigationWorld failed: island ids length mismatch.");
        if (world.Sectors == null || world.Sectors.Length != world.SectorCountX * world.SectorCountY)
            throw new InvalidOperationException("ValidateImportedDerivedNavigationWorld failed: sector array length mismatch.");
        if (world.Portals == null || world.PortalsById == null)
            throw new InvalidOperationException("ValidateImportedDerivedNavigationWorld failed: portal storage is invalid.");

        for (int i = 0; i < world.Sectors.Length; i++)
        {
            SectorData sector = world.Sectors[i];
            if (sector == null)
                throw new InvalidOperationException($"ValidateImportedDerivedNavigationWorld failed: sector is null index={i}.");
            if (sector.SectorId != i)
                throw new InvalidOperationException($"ValidateImportedDerivedNavigationWorld failed: sector id mismatch index={i} sectorId={sector.SectorId}.");
            if (sector.LocalComponentIds == null || sector.LocalComponentIds.Length != sector.Width * sector.Height)
                throw new InvalidOperationException($"ValidateImportedDerivedNavigationWorld failed: local component length mismatch sector={sector.SectorId}.");
        }

        for (int i = 0; i < world.Portals.Length; i++)
        {
            PortalData portal = world.Portals[i];
            if (portal.SectorAId < 0 || portal.SectorAId >= world.Sectors.Length || portal.SectorBId < 0 || portal.SectorBId >= world.Sectors.Length)
                throw new InvalidOperationException($"ValidateImportedDerivedNavigationWorld failed: portal sector out of range portal={portal.PortalId}.");
            if (!world.Sectors[portal.SectorAId].PortalIds.Contains(portal.PortalId)
                || !world.Sectors[portal.SectorBId].PortalIds.Contains(portal.PortalId))
            {
                throw new InvalidOperationException($"ValidateImportedDerivedNavigationWorld failed: portal is not referenced by both sectors portal={portal.PortalId}.");
            }
        }
    }

    private static NavigationWorld ApplyInitialRuntimeOverlayToPrebakedWorldIfNeeded(
        NavigationWorld staticWorld,
        int agentTypeId,
        out List<PendingSectorPortalAccess> pendingPortalAccessEntries)
    {
        pendingPortalAccessEntries = null;
        if (staticWorld == null)
            throw new InvalidOperationException("ApplyInitialRuntimeOverlayToPrebakedWorldIfNeeded failed: static world is null.");
        if (CircleObstacles.Count == 0 && BoxObstacles.Count == 0 && CostStamps.Count == 0)
            return staticWorld;

        HashSet<int> dirtySectors = new HashSet<int>();
        foreach (CircleObstacle circle in CircleObstacles.Values)
        {
            FixVector2 radius = new FixVector2(circle.RadiusFixed, circle.RadiusFixed);
            CollectDirtySectorsFixed(staticWorld, circle.PositionFixed - radius, circle.PositionFixed + radius, dirtySectors, includeNeighbors: true);
        }

        foreach (BoxObstacle box in BoxObstacles.Values)
        {
            FixVector2 halfExtents = ResolveBoxObstacleNavigationHalfExtentsFixed(staticWorld, box);
            CollectDirtySectorsFixed(staticWorld, box.CenterFixed - halfExtents, box.CenterFixed + halfExtents, dirtySectors, includeNeighbors: true);
        }

        foreach (CostStamp stamp in CostStamps.Values)
        {
            if (stamp.AgentTypeId != AnyAgentTypeId && stamp.AgentTypeId != agentTypeId)
                continue;

            CollectDirtySectorsFixed(staticWorld, stamp.BoundsMinFixed, stamp.BoundsMaxFixed, dirtySectors, includeNeighbors: true);
        }

        if (dirtySectors.Count == 0)
            return staticWorld;

        HashSet<int> costDirtySectors = ExpandDirtySectorsByCellRadius(
            staticWorld,
            dirtySectors,
            ResolveWallCostPaddingCells(staticWorld, 1));
        NavigationWorld patchedWorld = ApplyRuntimeDirtyOverlayImmediate(
            staticWorld,
            dirtySectors,
            costDirtySectors,
            CreateSortedCircleObstacleSnapshot(CircleObstacles.Values),
            CreateSortedBoxObstacleSnapshot(BoxObstacles.Values),
            CreateSortedCostStampSnapshot(CostStamps.Values),
            "initial-prebaked-overlay",
            out pendingPortalAccessEntries);
        LogNoStacktrace(
            $"[FlowPrebakedWorldOverlay] agentType={agentTypeId} dirtySectors={dirtySectors.Count} costSectors={costDirtySectors.Count} " +
            $"circleCount={CircleObstacles.Count} boxCount={BoxObstacles.Count} costStampCount={CostStamps.Count}");
        return patchedWorld;
    }

    private static NavigationWorld ApplyRuntimeDirtyOverlayImmediate(
        NavigationWorld targetWorld,
        HashSet<int> dirtySectors,
        HashSet<int> costDirtySectors,
        List<CircleObstacle> circleObstacles,
        List<BoxObstacle> boxObstacles,
        List<CostStamp> costStamps,
        string reason,
        out List<PendingSectorPortalAccess> pendingPortalAccessEntries)
    {
        pendingPortalAccessEntries = null;
        if (targetWorld == null)
            throw new InvalidOperationException("ApplyRuntimeDirtyOverlayImmediate failed: target world is null.");
        if (dirtySectors == null || dirtySectors.Count == 0)
            return targetWorld;
        if (costDirtySectors == null)
            throw new InvalidOperationException("ApplyRuntimeDirtyOverlayImmediate failed: cost dirty sectors are null.");

        RuntimeDirtyRebuildJob job = new RuntimeDirtyRebuildJob
        {
            TargetWorld = targetWorld,
            WorkingWorld = CloneNavigationWorldForRuntimeDirty(targetWorld, dirtySectors),
            DirtySectors = dirtySectors,
            CostDirtySectors = costDirtySectors,
            DirtySectorIds = CreateSortedSectorIdSnapshot(dirtySectors, "dirty sectors"),
            CostDirtySectorIds = CreateSortedSectorIdSnapshot(costDirtySectors, "cost dirty sectors"),
            CircleObstacles = CreateSortedCircleObstacleSnapshot(circleObstacles),
            BoxObstacles = CreateSortedBoxObstacleSnapshot(boxObstacles),
            CostStamps = CreateSortedCostStampSnapshot(costStamps),
            Stage = RuntimeDirtyRebuildStage.ResetWalkable,
            Reason = reason,
            IsInitialWorldOverlay = true
        };

        while (job.Stage != RuntimeDirtyRebuildStage.Complete)
        {
            switch (job.Stage)
            {
                case RuntimeDirtyRebuildStage.ResetWalkable:
                    ProcessRuntimeDirtyResetWalkable(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.ApplyObstacles:
                    ProcessRuntimeDirtyObstacles(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.NeighborMask:
                    ProcessRuntimeDirtyNeighborMask(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.SymmetrizeNeighborMask:
                    SymmetrizeNeighborTraversalMaskForSectors(job.WorkingWorld, job.DirtySectors);
                    PruneIsolatedWalkableCellsForSectors(job.WorkingWorld, job.DirtySectors, "initial-prebaked-overlay");
                    job.SectorCursor = 0;
                    job.Stage = RuntimeDirtyRebuildStage.CostField;
                    break;
                case RuntimeDirtyRebuildStage.CostField:
                    ProcessRuntimeDirtyCostField(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.IslandField:
                    ProcessRuntimeDirtyIslandField(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.SectorComponents:
                    ProcessRuntimeDirtySectorComponents(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.GoalProjectionIndex:
                    ProcessRuntimeDirtyGoalProjectionIndex(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.PortalGraph:
                    ProcessRuntimeDirtyPortalGraph(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.Hierarchy:
                    ProcessRuntimeDirtyHierarchy(job, long.MaxValue, forceComplete: true);
                    break;
                case RuntimeDirtyRebuildStage.PrepareCommit:
                    job.Stage = RuntimeDirtyRebuildStage.Complete;
                    break;
                case RuntimeDirtyRebuildStage.Commit:
                    job.Stage = RuntimeDirtyRebuildStage.Complete;
                    break;
                default:
                    throw new InvalidOperationException($"ApplyRuntimeDirtyOverlayImmediate failed: unknown stage {job.Stage}.");
            }
        }

        pendingPortalAccessEntries = job.PendingPortalAccessEntries;
        PrepareRuntimeDirtyFlowTileWorldInputBackings(job);
        DisposeFlowTileWorldInputBackings(targetWorld);
        return job.WorkingWorld;
    }

    private static TestTerrainOverride ResolveDefaultAuthoredTerrainSource()
    {
        if (AuthoredTerrainSources.TryGetValue(AnyAgentTypeId, out TestTerrainOverride anySource))
            return anySource;
        if (AuthoredTerrainSources.TryGetValue(ResolvePreferredAgentTypeId(0), out TestTerrainOverride defaultSource))
            return defaultSource;

        TestTerrainOverride selectedSource = null;
        int selectedAgentTypeId = int.MaxValue;
        foreach (KeyValuePair<int, TestTerrainOverride> pair in AuthoredTerrainSources)
        {
            if (pair.Value == null)
                throw new InvalidOperationException($"Authored navigation source is null. agentTypeId={pair.Key}.");
            if (pair.Key >= selectedAgentTypeId)
                continue;

            selectedAgentTypeId = pair.Key;
            selectedSource = pair.Value;
        }

        return selectedSource;
    }

}
