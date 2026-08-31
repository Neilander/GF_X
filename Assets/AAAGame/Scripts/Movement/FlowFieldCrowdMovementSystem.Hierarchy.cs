using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAAGame.FlowPath;
using UnityGameFramework.Runtime;

public static partial class FlowFieldCrowdMovementSystem
{
    private const int DefaultHierarchyFanout = 4;

    private sealed class PortalHierarchy
    {
        public int Fanout;
        public PortalHierarchyLevel[] Levels;
        public FlowPathKernelWitnessIndex WitnessIndex;
        public FlowPathKernelGraphIndex[] SearchGraphIndexes;
    }

    private sealed class PortalHierarchyLevel
    {
        public int Level;
        public int ClusterSpanSectors;
        public int ClusterCountX;
        public int ClusterCountY;
        public PortalHierarchyCluster[] Clusters;
    }

    private sealed class PortalHierarchyCluster
    {
        public int ClusterId;
        public int StartSectorX;
        public int StartSectorY;
        public int WidthSectors;
        public int HeightSectors;
        public int[] BoundaryNodes;
        public PortalHierarchyEdge[] Edges;
        public Dictionary<int, List<PortalHierarchyEdge>> OutgoingEdgesByNode;
        public Dictionary<int, List<PortalHierarchyEdge>> IncomingEdgesByNode;
    }

    private sealed class PortalHierarchyEdge
    {
        public int FromNode;
        public int ToNode;
        public long DeterministicCost;
        public int[] ChildWitnessNodes;
    }

    private sealed class PortalGraphSearchResult
    {
        public readonly Dictionary<int, long> Costs = new Dictionary<int, long>(256);
        public readonly Dictionary<int, int> PreviousNode = new Dictionary<int, int>(256);
        public readonly HashSet<int> SettledNodes = new HashSet<int>();
        public int ExpansionCount;
    }

    private sealed class PortalHierarchyQueryResult
    {
        public long DeterministicCost;
        public int Level;
        public int ExpansionCount;
        public int[] L0Nodes;
    }

    private sealed class PortalHierarchyConnector : IDisposable
    {
        public int TargetLevelIndex;
        public PortalGraphSearchResult Search;
        public FlowPathKernelSearchState SearchState;
        public PortalHierarchyConnector Child;

        public bool ContainsSettled(int node)
        {
            RequireSingleAuthority();
            return SearchState != null ? SearchState.ContainsSettled(node) : Search.SettledNodes.Contains(node);
        }

        public bool TryGetCost(int node, out long cost)
        {
            RequireSingleAuthority();
            return SearchState != null ? SearchState.TryGetCost(node, out cost) : Search.Costs.TryGetValue(node, out cost);
        }

        public bool TryGetPrevious(int node, out int previous)
        {
            RequireSingleAuthority();
            return SearchState != null
                ? SearchState.TryGetPrevious(node, out previous)
                : Search.PreviousNode.TryGetValue(node, out previous);
        }

        public int PreviousCount
        {
            get
            {
                RequireSingleAuthority();
                return SearchState != null ? SearchState.PreviousCount : Search.PreviousNode.Count;
            }
        }

        public int CostCount
        {
            get
            {
                RequireSingleAuthority();
                return SearchState != null ? SearchState.CostCount : Search.Costs.Count;
            }
        }

        public int SettledCount
        {
            get
            {
                RequireSingleAuthority();
                return SearchState != null ? SearchState.SettledCount : Search.SettledNodes.Count;
            }
        }

        public int ExpansionCount
        {
            get
            {
                RequireSingleAuthority();
                return SearchState != null ? SearchState.ExpansionCount : Search.ExpansionCount;
            }
        }

        public void RequireSingleAuthority()
        {
            bool managed = Search != null;
            bool native = SearchState != null && SearchState.IsCreated;
            if (managed == native)
                throw new InvalidOperationException("Portal hierarchy connector must own exactly one search authority.");
        }

        public void Dispose()
        {
            SearchState?.Dispose();
            SearchState = null;
        }
    }

    private readonly struct HierarchyStartConnectorCacheKey : IEquatable<HierarchyStartConnectorCacheKey>
    {
        public readonly int WorldVersion;
        public readonly int TargetLevelIndex;
        public readonly int SectorId;
        public readonly int CellX;
        public readonly int CellY;

        public HierarchyStartConnectorCacheKey(
            int worldVersion,
            int targetLevelIndex,
            int sectorId,
            int cellX,
            int cellY)
        {
            WorldVersion = worldVersion;
            TargetLevelIndex = targetLevelIndex;
            SectorId = sectorId;
            CellX = cellX;
            CellY = cellY;
        }

        public bool Equals(HierarchyStartConnectorCacheKey other)
        {
            return WorldVersion == other.WorldVersion
                   && TargetLevelIndex == other.TargetLevelIndex
                   && SectorId == other.SectorId
                   && CellX == other.CellX
                   && CellY == other.CellY;
        }

        public override bool Equals(object obj)
        {
            return obj is HierarchyStartConnectorCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldVersion;
                hash = (hash * 397) ^ TargetLevelIndex;
                hash = (hash * 397) ^ SectorId;
                hash = (hash * 397) ^ CellX;
                hash = (hash * 397) ^ CellY;
                return hash;
            }
        }
    }

    private sealed class HierarchyStartConnectorCacheEntry
    {
        public PortalHierarchyConnector Connector;
        public int LastUsedFrame;
    }

    private static readonly Dictionary<HierarchyStartConnectorCacheKey, HierarchyStartConnectorCacheEntry>
        HierarchyStartConnectorCache =
            new Dictionary<HierarchyStartConnectorCacheKey, HierarchyStartConnectorCacheEntry>(256);

#if UNITY_EDITOR
    private readonly struct HierarchyStartConnectorDiagnostic
    {
        public readonly int Level;
        public readonly int SectorId;
        public readonly int CellX;
        public readonly int CellY;
        public readonly int AgentTypeId;
        public readonly int GoalSectorId;
        public readonly int GoalX;
        public readonly int GoalY;
        public readonly int Cluster0;
        public readonly int Cluster1;
        public readonly int Cluster2;

        public HierarchyStartConnectorDiagnostic(
            int level,
            int sectorId,
            int cellX,
            int cellY,
            int agentTypeId,
            int goalSectorId,
            int goalX,
            int goalY,
            int cluster0,
            int cluster1,
            int cluster2)
        {
            Level = level;
            SectorId = sectorId;
            CellX = cellX;
            CellY = cellY;
            AgentTypeId = agentTypeId;
            GoalSectorId = goalSectorId;
            GoalX = goalX;
            GoalY = goalY;
            Cluster0 = cluster0;
            Cluster1 = cluster1;
            Cluster2 = cluster2;
        }
    }

    private static readonly Dictionary<int, List<HierarchyStartConnectorDiagnostic>>
        EditorHierarchyStartConnectorDiagnosticsByRenderFrame =
            new Dictionary<int, List<HierarchyStartConnectorDiagnostic>>(8);
#endif

    private sealed class PortalHierarchyReversePolicy : IDisposable
    {
        public int LevelArrayIndex;
        public int GoalSectorId;
        public int GoalX;
        public int GoalY;
        public PortalHierarchyConnector GoalConnector;
        public ulong GoalConnectorAuthorityContentHash;
        public readonly FlowPathKernelSearchState SearchState = new FlowPathKernelSearchState(256);
        public readonly Dictionary<PortalHierarchyCustomizationKey, PortalHierarchyDownwardCustomization>
            DownwardCustomizations =
                new Dictionary<PortalHierarchyCustomizationKey, PortalHierarchyDownwardCustomization>(16);
        public readonly Dictionary<int, PortalHierarchyL0Witness> L0WitnessesByStartNode =
            new Dictionary<int, PortalHierarchyL0Witness>(32);
        public ulong DownwardCustomizationsAuthorityContentHash;
        public ulong L0WitnessesAuthorityContentHash;

        public void Dispose()
        {
            foreach (PortalHierarchyDownwardCustomization customization in DownwardCustomizations.Values)
                customization?.Dispose();
            DownwardCustomizations.Clear();
            SearchState.Dispose();
        }
    }

    private sealed class PortalHierarchyL0Witness
    {
        public int StartNode;
        public int[] SectorIds;
        public int[] PortalIds;
        public ulong AuthorityContentHash;
    }

    private readonly struct PortalHierarchyCustomizationKey : IEquatable<PortalHierarchyCustomizationKey>
    {
        public readonly int SourceLevelArrayIndex;
        public readonly int ClusterId;
        public readonly int TargetRegionId;

        public PortalHierarchyCustomizationKey(
            int sourceLevelArrayIndex,
            int clusterId,
            int targetRegionId)
        {
            SourceLevelArrayIndex = sourceLevelArrayIndex;
            ClusterId = clusterId;
            TargetRegionId = targetRegionId;
        }

        public bool Equals(PortalHierarchyCustomizationKey other)
        {
            return SourceLevelArrayIndex == other.SourceLevelArrayIndex
                   && ClusterId == other.ClusterId
                   && TargetRegionId == other.TargetRegionId;
        }

        public override bool Equals(object obj)
        {
            return obj is PortalHierarchyCustomizationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceLevelArrayIndex;
                hash = (hash * 397) ^ ClusterId;
                hash = (hash * 397) ^ TargetRegionId;
                return hash;
            }
        }
    }

    private sealed class PortalHierarchyDownwardCustomization : IDisposable
    {
        public int SourceLevelArrayIndex;
        public int ClusterId;
        public int TargetRegionId;
        public PortalGraphSearchResult Search;
        public FlowPathKernelSearchState SearchState;
        public ulong AuthorityContentHash;

        public int ExpansionCount
        {
            get
            {
                RequireSingleAuthority();
                return SearchState != null ? SearchState.ExpansionCount : Search.ExpansionCount;
            }
        }

        public int PreviousCount
        {
            get
            {
                RequireSingleAuthority();
                return SearchState != null ? SearchState.PreviousCount : Search.PreviousNode.Count;
            }
        }

        public bool ContainsSettled(int node)
        {
            RequireSingleAuthority();
            return SearchState != null ? SearchState.ContainsSettled(node) : Search.SettledNodes.Contains(node);
        }

        public bool TryGetCost(int node, out long cost)
        {
            RequireSingleAuthority();
            return SearchState != null ? SearchState.TryGetCost(node, out cost) : Search.Costs.TryGetValue(node, out cost);
        }

        public bool TryGetPrevious(int node, out int previous)
        {
            RequireSingleAuthority();
            return SearchState != null
                ? SearchState.TryGetPrevious(node, out previous)
                : Search.PreviousNode.TryGetValue(node, out previous);
        }

        public void ShiftCosts(long delta)
        {
            RequireSingleAuthority();
            if (SearchState != null)
                SearchState.ShiftCosts(delta);
            else
                ShiftPortalGraphSearchCosts(Search, delta);
        }

        public void Dispose()
        {
            SearchState?.Dispose();
            SearchState = null;
        }

        public void RequireSingleAuthority()
        {
            bool hasManaged = Search != null;
            bool hasNative = SearchState != null && SearchState.IsCreated;
            if (hasManaged == hasNative)
                throw new InvalidOperationException("Downward customization must own exactly one search authority.");
        }
    }

    private sealed class PortalHierarchyBuildJob
    {
        public NavigationWorld World;
        public int Fanout;
        public PortalHierarchy ReuseSource;
        public HashSet<int> AffectedSectors;
        public readonly List<PortalHierarchyLevel> CompletedLevels = new List<PortalHierarchyLevel>(4);
        public int NextLevel = 1;
        public int NextClusterSpanSectors;
        public PortalHierarchyLevel CurrentLevel;
        public int ClusterCursor;
        public PortalHierarchyCluster CurrentCluster;
        public int SourceCursor;
        public PortalHierarchySourceBuildJob CurrentSourceBuild;
        public List<PortalHierarchyEdge> CurrentEdges;
        public PortalHierarchy Result;
        public bool Complete;
    }

    private sealed class PortalHierarchySourceBuildJob
    {
        public NavigationWorld World;
        public PortalHierarchyCluster Cluster;
        public PortalHierarchyLevel LowerLevel;
        public int SourceNode;
        public readonly HashSet<int> TargetNodes = new HashSet<int>();
        public readonly PortalGraphSearchResult Search = new PortalGraphSearchResult();
        public readonly DeterministicCostHeap OpenSet = new DeterministicCostHeap();
        public int RemainingTargets;
        public bool Complete;
#if UNITY_EDITOR
        public long AccumulatedTicks;
#endif
    }

    private static PortalHierarchyBuildJob CreatePortalHierarchyBuildJob(NavigationWorld world, int fanout)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (fanout < 2)
            throw new ArgumentOutOfRangeException(nameof(fanout));
        return new PortalHierarchyBuildJob
        {
            World = world,
            Fanout = fanout,
            NextClusterSpanSectors = fanout
        };
    }

    private static void ProcessPortalHierarchyBuildJob(
        PortalHierarchyBuildJob job,
        long deadlineTicks,
        bool forceComplete)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.Complete)
            return;

        while (!job.Complete)
        {
            if (job.CurrentLevel == null)
            {
                int clusterCountX = CeilDivide(job.World.SectorCountX, job.NextClusterSpanSectors);
                int clusterCountY = CeilDivide(job.World.SectorCountY, job.NextClusterSpanSectors);
                if (clusterCountX <= 1 && clusterCountY <= 1)
                {
                    job.Result = new PortalHierarchy
                    {
                        Fanout = job.Fanout,
                        Levels = job.CompletedLevels.ToArray()
                    };
                    ValidatePortalHierarchy(job.World, job.Result);
                    ValidatePortalHierarchyEdgeCosts(job.World, job.Result);
                    job.Complete = true;
                    break;
                }

                job.CurrentLevel = new PortalHierarchyLevel
                {
                    Level = job.NextLevel,
                    ClusterSpanSectors = job.NextClusterSpanSectors,
                    ClusterCountX = clusterCountX,
                    ClusterCountY = clusterCountY,
                    Clusters = new PortalHierarchyCluster[checked(clusterCountX * clusterCountY)]
                };
                job.ClusterCursor = 0;
            }

            if (job.ClusterCursor >= job.CurrentLevel.Clusters.Length)
            {
                job.CompletedLevels.Add(job.CurrentLevel);
                job.CurrentLevel = null;
                job.NextLevel++;
                if (job.NextClusterSpanSectors > int.MaxValue / job.Fanout)
                    throw new InvalidOperationException("ProcessPortalHierarchyBuildJob failed: cluster span overflow.");
                job.NextClusterSpanSectors *= job.Fanout;
                continue;
            }

            if (job.CurrentCluster == null)
            {
                int clusterX = job.ClusterCursor % job.CurrentLevel.ClusterCountX;
                int clusterY = job.ClusterCursor / job.CurrentLevel.ClusterCountX;
                if (TryReusePortalHierarchyCluster(job, clusterX, clusterY))
                {
                    job.ClusterCursor++;
                    if (!forceComplete && IsBudgetExpired(deadlineTicks, job.ClusterCursor))
                        return;
                    continue;
                }

                job.CurrentCluster = new PortalHierarchyCluster
                {
                    ClusterId = job.ClusterCursor,
                    StartSectorX = clusterX * job.CurrentLevel.ClusterSpanSectors,
                    StartSectorY = clusterY * job.CurrentLevel.ClusterSpanSectors,
                    WidthSectors = Math.Min(job.CurrentLevel.ClusterSpanSectors, job.World.SectorCountX - clusterX * job.CurrentLevel.ClusterSpanSectors),
                    HeightSectors = Math.Min(job.CurrentLevel.ClusterSpanSectors, job.World.SectorCountY - clusterY * job.CurrentLevel.ClusterSpanSectors)
                };
                job.CurrentCluster.BoundaryNodes = CollectPortalHierarchyBoundaryNodes(
                    job.World,
                    job.CurrentLevel,
                    job.CurrentCluster);
                job.CurrentEdges = new List<PortalHierarchyEdge>(
                    checked(job.CurrentCluster.BoundaryNodes.Length * Math.Max(0, job.CurrentCluster.BoundaryNodes.Length - 1)));
                job.SourceCursor = 0;
            }

            if (job.SourceCursor < job.CurrentCluster.BoundaryNodes.Length)
            {
                if (job.CurrentSourceBuild == null)
                {
                    job.CurrentSourceBuild = CreatePortalHierarchySourceBuildJob(
                        job.World,
                        job.CurrentCluster,
                        job.CompletedLevels.Count > 0 ? job.CompletedLevels[job.CompletedLevels.Count - 1] : null,
                        job.SourceCursor);
                }

                ProcessPortalHierarchySourceBuildJob(job.CurrentSourceBuild, deadlineTicks, forceComplete);
                if (!job.CurrentSourceBuild.Complete)
                    return;
                AppendPortalHierarchySourceEdges(
                    job.CurrentCluster,
                    job.CurrentSourceBuild.SourceNode,
                    job.CurrentSourceBuild.Search,
                    job.CurrentEdges);
#if UNITY_EDITOR
                _editorPortalHierarchyMaximumSourceExpansions = Math.Max(
                    _editorPortalHierarchyMaximumSourceExpansions,
                    job.CurrentSourceBuild.Search.ExpansionCount);
                _editorPortalHierarchyMaximumSourceTicks = Math.Max(
                    _editorPortalHierarchyMaximumSourceTicks,
                    job.CurrentSourceBuild.AccumulatedTicks);
#endif
                job.CurrentSourceBuild = null;
                job.SourceCursor++;
                continue;
            }

            job.CurrentEdges.Sort(ComparePortalHierarchyEdges);
            job.CurrentCluster.Edges = job.CurrentEdges.ToArray();
            job.CurrentCluster.OutgoingEdgesByNode = BuildPortalHierarchyOutgoingIndex(job.CurrentCluster);
            job.CurrentCluster.IncomingEdgesByNode = BuildPortalHierarchyIncomingIndex(job.CurrentCluster);
            job.CurrentLevel.Clusters[job.ClusterCursor] = job.CurrentCluster;
            job.CurrentCluster = null;
            job.CurrentEdges = null;
            job.ClusterCursor++;
            if (!forceComplete && IsBudgetExpired(deadlineTicks, job.ClusterCursor))
                return;
        }
    }

    private static bool TryReusePortalHierarchyCluster(
        PortalHierarchyBuildJob job,
        int clusterX,
        int clusterY)
    {
        if (job?.ReuseSource?.Levels == null || job.AffectedSectors == null)
            return false;
        int levelIndex = job.NextLevel - 1;
        if (levelIndex < 0 || levelIndex >= job.ReuseSource.Levels.Length)
            return false;

        PortalHierarchyLevel sourceLevel = job.ReuseSource.Levels[levelIndex];
        if (sourceLevel == null
            || sourceLevel.Level != job.CurrentLevel.Level
            || sourceLevel.ClusterSpanSectors != job.CurrentLevel.ClusterSpanSectors
            || sourceLevel.ClusterCountX != job.CurrentLevel.ClusterCountX
            || sourceLevel.ClusterCountY != job.CurrentLevel.ClusterCountY
            || clusterX < 0 || clusterY < 0
            || clusterX >= sourceLevel.ClusterCountX
            || clusterY >= sourceLevel.ClusterCountY)
            return false;

        int clusterId = clusterY * sourceLevel.ClusterCountX + clusterX;
        if (clusterId < 0 || clusterId >= sourceLevel.Clusters.Length)
            return false;
        PortalHierarchyCluster sourceCluster = sourceLevel.Clusters[clusterId];
        if (sourceCluster == null)
            return false;

        int startSectorX = clusterX * job.CurrentLevel.ClusterSpanSectors;
        int startSectorY = clusterY * job.CurrentLevel.ClusterSpanSectors;
        int widthSectors = Math.Min(
            job.CurrentLevel.ClusterSpanSectors,
            job.World.SectorCountX - startSectorX);
        int heightSectors = Math.Min(
            job.CurrentLevel.ClusterSpanSectors,
            job.World.SectorCountY - startSectorY);
        for (int y = 0; y < heightSectors; y++)
        {
            for (int x = 0; x < widthSectors; x++)
            {
                int sectorId = (startSectorY + y) * job.World.SectorCountX + startSectorX + x;
                if (job.AffectedSectors.Contains(sectorId))
                    return false;
            }
        }

        job.CurrentLevel.Clusters[job.ClusterCursor] = sourceCluster;
        return true;
    }

    private static PortalHierarchy BuildPortalHierarchy(NavigationWorld world, int fanout)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (world.Sectors == null || world.Portals == null || world.PortalsById == null)
            throw new InvalidOperationException("BuildPortalHierarchy failed: L0 portal graph is incomplete.");
        if (fanout < 2)
            throw new ArgumentOutOfRangeException(nameof(fanout), fanout, "Hierarchy fanout must be at least two.");

        var levels = new List<PortalHierarchyLevel>(4);
        int span = fanout;
        int levelIndex = 1;
        PortalHierarchyLevel lowerLevel = null;
        while (CeilDivide(world.SectorCountX, span) > 1 || CeilDivide(world.SectorCountY, span) > 1)
        {
            PortalHierarchyLevel level = BuildPortalHierarchyLevel(world, levelIndex, span, lowerLevel);
            levels.Add(level);
            lowerLevel = level;
            if (span > int.MaxValue / fanout)
                throw new InvalidOperationException("BuildPortalHierarchy failed: cluster span overflow.");
            span *= fanout;
            levelIndex++;
        }

        var hierarchy = new PortalHierarchy
        {
            Fanout = fanout,
            Levels = levels.ToArray()
        };
        ValidatePortalHierarchy(world, hierarchy);
        ValidatePortalHierarchyEdgeCosts(world, hierarchy);
        return hierarchy;
    }

    private static PortalHierarchyLevel BuildPortalHierarchyLevel(
        NavigationWorld world,
        int levelIndex,
        int clusterSpanSectors,
        PortalHierarchyLevel lowerLevel)
    {
        int clusterCountX = CeilDivide(world.SectorCountX, clusterSpanSectors);
        int clusterCountY = CeilDivide(world.SectorCountY, clusterSpanSectors);
        var level = new PortalHierarchyLevel
        {
            Level = levelIndex,
            ClusterSpanSectors = clusterSpanSectors,
            ClusterCountX = clusterCountX,
            ClusterCountY = clusterCountY,
            Clusters = new PortalHierarchyCluster[checked(clusterCountX * clusterCountY)]
        };

        for (int clusterY = 0; clusterY < clusterCountY; clusterY++)
        {
            for (int clusterX = 0; clusterX < clusterCountX; clusterX++)
            {
                int clusterId = clusterY * clusterCountX + clusterX;
                var cluster = new PortalHierarchyCluster
                {
                    ClusterId = clusterId,
                    StartSectorX = clusterX * clusterSpanSectors,
                    StartSectorY = clusterY * clusterSpanSectors,
                    WidthSectors = Math.Min(clusterSpanSectors, world.SectorCountX - clusterX * clusterSpanSectors),
                    HeightSectors = Math.Min(clusterSpanSectors, world.SectorCountY - clusterY * clusterSpanSectors)
                };
                cluster.BoundaryNodes = CollectPortalHierarchyBoundaryNodes(world, level, cluster);
                cluster.Edges = BuildPortalHierarchyClusterEdges(world, cluster, lowerLevel);
                cluster.OutgoingEdgesByNode = BuildPortalHierarchyOutgoingIndex(cluster);
                cluster.IncomingEdgesByNode = BuildPortalHierarchyIncomingIndex(cluster);
                level.Clusters[clusterId] = cluster;
            }
        }

        return level;
    }

    private static int[] CollectPortalHierarchyBoundaryNodes(
        NavigationWorld world,
        PortalHierarchyLevel level,
        PortalHierarchyCluster cluster)
    {
        var nodes = new List<int>(64);
        for (int sectorY = cluster.StartSectorY; sectorY < cluster.StartSectorY + cluster.HeightSectors; sectorY++)
        {
            for (int sectorX = cluster.StartSectorX; sectorX < cluster.StartSectorX + cluster.WidthSectors; sectorX++)
            {
                int sectorId = sectorY * world.SectorCountX + sectorX;
                SectorData sector = world.Sectors[sectorId];
                for (int i = 0; i < sector.PortalIds.Count; i++)
                {
                    int portalId = sector.PortalIds[i];
                    PortalData portal = GetPortalById(world, portalId);
                    int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
                    if (ResolveHierarchyClusterId(world, level, oppositeSectorId) == cluster.ClusterId)
                        continue;
                    nodes.Add(EncodePortalNode(sectorId, portalId));
                }
            }
        }

        nodes.Sort();
        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i] == nodes[i - 1])
                throw new InvalidOperationException($"CollectPortalHierarchyBoundaryNodes failed: duplicate node={nodes[i]} cluster={cluster.ClusterId} level={level.Level}.");
        }
        return nodes.ToArray();
    }

    private static PortalHierarchyEdge[] BuildPortalHierarchyClusterEdges(
        NavigationWorld world,
        PortalHierarchyCluster cluster,
        PortalHierarchyLevel lowerLevel)
    {
        var edges = new List<PortalHierarchyEdge>(checked(cluster.BoundaryNodes.Length * Math.Max(0, cluster.BoundaryNodes.Length - 1)));
        for (int sourceIndex = 0; sourceIndex < cluster.BoundaryNodes.Length; sourceIndex++)
            BuildPortalHierarchySourceEdges(world, cluster, lowerLevel, sourceIndex, edges);

        edges.Sort(ComparePortalHierarchyEdges);
        return edges.ToArray();
    }

    private static void BuildPortalHierarchySourceEdges(
        NavigationWorld world,
        PortalHierarchyCluster cluster,
        PortalHierarchyLevel lowerLevel,
        int sourceIndex,
        List<PortalHierarchyEdge> edges)
    {
        if (sourceIndex < 0 || sourceIndex >= cluster.BoundaryNodes.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        int sourceNode = cluster.BoundaryNodes[sourceIndex];
        var targets = new HashSet<int>(cluster.BoundaryNodes);
#if UNITY_EDITOR
        long sourceStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        PortalGraphSearchResult search = lowerLevel == null
            ? RunRestrictedPortalGraphDijkstra(
                world,
                cluster,
                new[] { sourceNode },
                new[] { 0L },
                reverse: false,
                targets)
            : RunRestrictedPortalHierarchyDijkstra(
                world,
                lowerLevel,
                cluster,
                new[] { sourceNode },
                new[] { 0L },
                reverse: false,
                targets);
#if UNITY_EDITOR
        _editorPortalHierarchyMaximumSourceExpansions = Math.Max(
            _editorPortalHierarchyMaximumSourceExpansions,
            search.ExpansionCount);
        _editorPortalHierarchyMaximumSourceTicks = Math.Max(
            _editorPortalHierarchyMaximumSourceTicks,
            System.Diagnostics.Stopwatch.GetTimestamp() - sourceStartTicks);
#endif
        AppendPortalHierarchySourceEdges(cluster, sourceNode, search, edges);
    }

    private static void AppendPortalHierarchySourceEdges(
        PortalHierarchyCluster cluster,
        int sourceNode,
        PortalGraphSearchResult search,
        List<PortalHierarchyEdge> edges)
    {
        if (cluster == null || search == null || edges == null)
            throw new ArgumentNullException("Hierarchy source edge finalization received a null argument.");
        for (int targetIndex = 0; targetIndex < cluster.BoundaryNodes.Length; targetIndex++)
        {
            int targetNode = cluster.BoundaryNodes[targetIndex];
            if (targetNode == sourceNode || !search.Costs.TryGetValue(targetNode, out long cost))
                continue;
            int[] witness = ReconstructForwardPortalWitness(search.PreviousNode, targetNode);
            if (witness.Length == 0 || witness[0] != sourceNode || witness[witness.Length - 1] != targetNode)
                throw new InvalidOperationException($"BuildPortalHierarchySourceEdges failed: invalid witness cluster={cluster.ClusterId} from={sourceNode} to={targetNode}.");
            edges.Add(new PortalHierarchyEdge
            {
                FromNode = sourceNode,
                ToNode = targetNode,
                DeterministicCost = cost,
                ChildWitnessNodes = witness
            });
        }
    }

    private static PortalHierarchySourceBuildJob CreatePortalHierarchySourceBuildJob(
        NavigationWorld world,
        PortalHierarchyCluster cluster,
        PortalHierarchyLevel lowerLevel,
        int sourceIndex)
    {
        if (world == null || cluster == null)
            throw new ArgumentNullException("Hierarchy source build requires a world and cluster.");
        if (sourceIndex < 0 || sourceIndex >= cluster.BoundaryNodes.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));

        int sourceNode = cluster.BoundaryNodes[sourceIndex];
        DecodePortalNode(sourceNode, out int sourceSectorId, out _);
        if (!HierarchyClusterContainsSector(world, cluster, sourceSectorId))
            throw new InvalidOperationException(
                $"Hierarchy source build found a source outside cluster node={sourceNode} cluster={cluster.ClusterId}.");

        var job = new PortalHierarchySourceBuildJob
        {
            World = world,
            Cluster = cluster,
            LowerLevel = lowerLevel,
            SourceNode = sourceNode,
        };
        for (int i = 0; i < cluster.BoundaryNodes.Length; i++)
            job.TargetNodes.Add(cluster.BoundaryNodes[i]);
        job.RemainingTargets = job.TargetNodes.Count;
        job.Search.Costs.Add(sourceNode, 0L);
        job.OpenSet.Push(sourceNode, 0L);
        return job;
    }

    private static void ProcessPortalHierarchySourceBuildJob(
        PortalHierarchySourceBuildJob job,
        long deadlineTicks,
        bool forceComplete)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        if (job.Complete)
            return;

#if UNITY_EDITOR
        long invocationStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        int invocationStartExpansions = job.Search.ExpansionCount;
        try
        {
#endif
            while (job.OpenSet.Count > 0 && job.RemainingTargets > 0)
            {
                DeterministicCostQueueNode item = job.OpenSet.Pop();
                if (!job.Search.Costs.TryGetValue(item.Index, out long currentCost)
                    || item.Cost != currentCost
                    || !job.Search.SettledNodes.Add(item.Index))
                {
                    continue;
                }

                job.Search.ExpansionCount++;
                if (job.TargetNodes.Contains(item.Index))
                    job.RemainingTargets--;
                if (job.RemainingTargets > 0)
                {
                    if (job.LowerLevel == null)
                        ExpandPortalHierarchyL0SourceNode(job, item.Index, currentCost);
                    else
                        ExpandPortalHierarchyOverlaySourceNode(job, item.Index, currentCost);
                }

                if (!forceComplete && IsBudgetExpired(deadlineTicks, job.Search.ExpansionCount))
                    return;
            }

            job.Complete = true;
#if UNITY_EDITOR
        }
        finally
        {
            long invocationTicks = System.Diagnostics.Stopwatch.GetTimestamp() - invocationStartTicks;
            int invocationExpansions = job.Search.ExpansionCount - invocationStartExpansions;
            job.AccumulatedTicks += invocationTicks;
            _editorPortalHierarchyMaximumInvocationTicks = Math.Max(
                _editorPortalHierarchyMaximumInvocationTicks,
                invocationTicks);
            _editorPortalHierarchyMaximumInvocationExpansions = Math.Max(
                _editorPortalHierarchyMaximumInvocationExpansions,
                invocationExpansions);
        }
#endif
    }

    private static void ExpandPortalHierarchyL0SourceNode(
        PortalHierarchySourceBuildJob job,
        int currentNode,
        long currentCost)
    {
        DecodePortalNode(currentNode, out int sectorId, out int portalId);
        PortalData portal = GetPortalById(job.World, portalId);
        int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
        if (HierarchyClusterContainsSector(job.World, job.Cluster, oppositeSectorId))
        {
            RelaxRestrictedPortalGraphNode(
                job.Search,
                job.OpenSet,
                currentNode,
                EncodePortalNode(oppositeSectorId, portalId),
                AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost),
                reverse: false);
        }

        List<PortalTransition> transitions = GetOutgoingPortalTransitions(job.World.Sectors[sectorId], portalId);
        if (transitions == null)
            return;
        for (int i = 0; i < transitions.Count; i++)
        {
            PortalTransition transition = transitions[i];
            RelaxRestrictedPortalGraphNode(
                job.Search,
                job.OpenSet,
                currentNode,
                EncodePortalNode(sectorId, transition.ToPortalId),
                AddDeterministicPortalCosts(currentCost, transition.DeterministicCost),
                reverse: false);
        }
    }

    private static void ExpandPortalHierarchyOverlaySourceNode(
        PortalHierarchySourceBuildJob job,
        int currentNode,
        long currentCost)
    {
        DecodePortalNode(currentNode, out int sectorId, out int portalId);
        if (!HierarchyClusterContainsSector(job.World, job.Cluster, sectorId))
            throw new InvalidOperationException(
                $"Hierarchy source build expanded outside containing cluster node={currentNode} cluster={job.Cluster.ClusterId}.");

        int clusterId = ResolveHierarchyClusterId(job.World, job.LowerLevel, sectorId);
        PortalHierarchyCluster lowerCluster = job.LowerLevel.Clusters[clusterId];
        if (!lowerCluster.OutgoingEdgesByNode.TryGetValue(currentNode, out List<PortalHierarchyEdge> edges))
        {
            throw new InvalidOperationException(
                $"Hierarchy source build missing overlay node level={job.LowerLevel.Level} cluster={clusterId} node={currentNode}.");
        }
        for (int i = 0; i < edges.Count; i++)
        {
            PortalHierarchyEdge edge = edges[i];
            RelaxRestrictedPortalGraphNode(
                job.Search,
                job.OpenSet,
                currentNode,
                edge.ToNode,
                AddDeterministicPortalCosts(currentCost, edge.DeterministicCost),
                reverse: false);
        }

        PortalData portal = GetPortalById(job.World, portalId);
        int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
        if (ResolveHierarchyClusterId(job.World, job.LowerLevel, oppositeSectorId) == clusterId)
        {
            throw new InvalidOperationException(
                $"Hierarchy source build found an internal overlay crossing level={job.LowerLevel.Level} node={currentNode}.");
        }
        if (HierarchyClusterContainsSector(job.World, job.Cluster, oppositeSectorId))
        {
            RelaxRestrictedPortalGraphNode(
                job.Search,
                job.OpenSet,
                currentNode,
                EncodePortalNode(oppositeSectorId, portalId),
                AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost),
                reverse: false);
        }
    }

    private static Dictionary<int, List<PortalHierarchyEdge>> BuildPortalHierarchyOutgoingIndex(PortalHierarchyCluster cluster)
    {
        var result = new Dictionary<int, List<PortalHierarchyEdge>>(cluster.BoundaryNodes.Length);
        for (int i = 0; i < cluster.BoundaryNodes.Length; i++)
            result.Add(cluster.BoundaryNodes[i], new List<PortalHierarchyEdge>(Math.Max(0, cluster.BoundaryNodes.Length - 1)));
        for (int i = 0; i < cluster.Edges.Length; i++)
        {
            PortalHierarchyEdge edge = cluster.Edges[i];
            if (!result.TryGetValue(edge.FromNode, out List<PortalHierarchyEdge> outgoing))
                throw new InvalidOperationException($"BuildPortalHierarchyOutgoingIndex failed: edge source is not a boundary node cluster={cluster.ClusterId} node={edge.FromNode}.");
            outgoing.Add(edge);
        }
        return result;
    }

    private static Dictionary<int, List<PortalHierarchyEdge>> BuildPortalHierarchyIncomingIndex(PortalHierarchyCluster cluster)
    {
        var result = new Dictionary<int, List<PortalHierarchyEdge>>(cluster.BoundaryNodes.Length);
        for (int i = 0; i < cluster.BoundaryNodes.Length; i++)
            result.Add(cluster.BoundaryNodes[i], new List<PortalHierarchyEdge>(Math.Max(0, cluster.BoundaryNodes.Length - 1)));
        for (int i = 0; i < cluster.Edges.Length; i++)
        {
            PortalHierarchyEdge edge = cluster.Edges[i];
            if (!result.TryGetValue(edge.ToNode, out List<PortalHierarchyEdge> incoming))
                throw new InvalidOperationException($"BuildPortalHierarchyIncomingIndex failed: edge target is not a boundary node cluster={cluster.ClusterId} node={edge.ToNode}.");
            incoming.Add(edge);
        }
        return result;
    }

    private static PortalGraphSearchResult RunRestrictedPortalGraphDijkstra(
        NavigationWorld world,
        PortalHierarchyCluster cluster,
        int[] sourceNodes,
        long[] sourceCosts,
        bool reverse,
        HashSet<int> targetNodes)
    {
        if (sourceNodes == null || sourceCosts == null || sourceNodes.Length != sourceCosts.Length)
            throw new ArgumentException("Restricted portal graph sources are invalid.");

        var result = new PortalGraphSearchResult();
        var open = new DeterministicCostHeap();
        for (int i = 0; i < sourceNodes.Length; i++)
        {
            int node = sourceNodes[i];
            DecodePortalNode(node, out int sectorId, out _);
            if (!HierarchyClusterContainsSector(world, cluster, sectorId))
                throw new InvalidOperationException($"RunRestrictedPortalGraphDijkstra failed: source outside cluster node={node} cluster={cluster.ClusterId}.");
            if (!result.Costs.TryGetValue(node, out long existing) || sourceCosts[i] < existing)
            {
                result.Costs[node] = sourceCosts[i];
                open.Push(node, sourceCosts[i]);
            }
        }

        int remainingTargets = targetNodes?.Count ?? int.MaxValue;
        while (open.Count > 0 && remainingTargets > 0)
        {
            DeterministicCostQueueNode item = open.Pop();
            if (!result.Costs.TryGetValue(item.Index, out long currentCost) || item.Cost != currentCost)
                continue;
            if (!result.SettledNodes.Add(item.Index))
                continue;
            result.ExpansionCount++;
            if (targetNodes != null && targetNodes.Contains(item.Index))
                remainingTargets--;

            DecodePortalNode(item.Index, out int sectorId, out int portalId);
            PortalData portal = GetPortalById(world, portalId);
            int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
            if (HierarchyClusterContainsSector(world, cluster, oppositeSectorId))
            {
                RelaxRestrictedPortalGraphNode(
                    result,
                    open,
                    item.Index,
                    EncodePortalNode(oppositeSectorId, portalId),
                    AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost),
                    reverse);
            }

            SectorData sector = world.Sectors[sectorId];
            List<PortalTransition> transitions = reverse
                ? GetIncomingPortalTransitions(sector, portalId)
                : GetOutgoingPortalTransitions(sector, portalId);
            if (transitions == null)
                continue;
            for (int i = 0; i < transitions.Count; i++)
            {
                PortalTransition transition = transitions[i];
                int nextPortalId = reverse ? transition.FromPortalId : transition.ToPortalId;
                RelaxRestrictedPortalGraphNode(
                    result,
                    open,
                    item.Index,
                    EncodePortalNode(sectorId, nextPortalId),
                    AddDeterministicPortalCosts(currentCost, transition.DeterministicCost),
                    reverse);
            }
        }

        return result;
    }

    private static PortalGraphSearchResult RunRestrictedPortalHierarchyDijkstra(
        NavigationWorld world,
        PortalHierarchyLevel level,
        PortalHierarchyCluster containingCluster,
        int[] sourceNodes,
        long[] sourceCosts,
        bool reverse,
        HashSet<int> targetNodes)
    {
        if (sourceNodes == null || sourceCosts == null || sourceNodes.Length != sourceCosts.Length)
            throw new ArgumentException("Restricted hierarchy sources are invalid.");

        var result = new PortalGraphSearchResult();
        var open = new DeterministicCostHeap();
        for (int i = 0; i < sourceNodes.Length; i++)
        {
            int node = sourceNodes[i];
            DecodePortalNode(node, out int sectorId, out _);
            if (!HierarchyClusterContainsSector(world, containingCluster, sectorId))
                throw new InvalidOperationException($"RunRestrictedPortalHierarchyDijkstra failed: source outside containing cluster node={node} cluster={containingCluster.ClusterId}.");
            if (!result.Costs.TryGetValue(node, out long existing) || sourceCosts[i] < existing)
            {
                result.Costs[node] = sourceCosts[i];
                open.Push(node, sourceCosts[i]);
            }
        }

        int remainingTargets = targetNodes?.Count ?? int.MaxValue;
        while (open.Count > 0 && remainingTargets > 0)
        {
            DeterministicCostQueueNode item = open.Pop();
            if (!result.Costs.TryGetValue(item.Index, out long currentCost) || item.Cost != currentCost)
                continue;
            if (!result.SettledNodes.Add(item.Index))
                continue;
            result.ExpansionCount++;
            if (targetNodes != null && targetNodes.Contains(item.Index))
                remainingTargets--;

            DecodePortalNode(item.Index, out int sectorId, out int portalId);
            int clusterId = ResolveHierarchyClusterId(world, level, sectorId);
            PortalHierarchyCluster cluster = level.Clusters[clusterId];
            if (!HierarchyClusterContainsSector(world, containingCluster, sectorId))
                throw new InvalidOperationException($"RunRestrictedPortalHierarchyDijkstra failed: expanded outside containing cluster node={item.Index} cluster={containingCluster.ClusterId}.");

            Dictionary<int, List<PortalHierarchyEdge>> edgeIndex = reverse
                ? cluster.IncomingEdgesByNode
                : cluster.OutgoingEdgesByNode;
            if (!edgeIndex.TryGetValue(item.Index, out List<PortalHierarchyEdge> edges))
                throw new InvalidOperationException($"RunRestrictedPortalHierarchyDijkstra failed: overlay node missing level={level.Level} cluster={clusterId} node={item.Index}.");
            for (int i = 0; i < edges.Count; i++)
            {
                PortalHierarchyEdge edge = edges[i];
                int nextNode = reverse ? edge.FromNode : edge.ToNode;
                RelaxRestrictedPortalGraphNode(
                    result,
                    open,
                    item.Index,
                    nextNode,
                    AddDeterministicPortalCosts(currentCost, edge.DeterministicCost),
                    reverse);
            }

            PortalData portal = GetPortalById(world, portalId);
            int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
            if (ResolveHierarchyClusterId(world, level, oppositeSectorId) == clusterId)
                throw new InvalidOperationException($"RunRestrictedPortalHierarchyDijkstra failed: overlay boundary node crosses inside same cluster level={level.Level} node={item.Index}.");
            if (HierarchyClusterContainsSector(world, containingCluster, oppositeSectorId))
            {
                RelaxRestrictedPortalGraphNode(
                    result,
                    open,
                    item.Index,
                    EncodePortalNode(oppositeSectorId, portalId),
                    AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost),
                    reverse);
            }
        }

        return result;
    }

    private static void RelaxRestrictedPortalGraphNode(
        PortalGraphSearchResult result,
        DeterministicCostHeap open,
        int currentNode,
        int nextNode,
        long nextCost,
        bool reverse)
    {
        if (result.SettledNodes.Contains(nextNode))
        {
            if (result.Costs.TryGetValue(nextNode, out long settledCost) && nextCost < settledCost)
                throw new InvalidOperationException($"Restricted Dijkstra found a cheaper path to settled node={nextNode}, previous={settledCost}, next={nextCost}.");
            return;
        }
        if (result.Costs.TryGetValue(nextNode, out long existing) && nextCost >= existing)
            return;
        result.Costs[nextNode] = nextCost;
        if (reverse)
            result.PreviousNode[nextNode] = currentNode;
        else
            result.PreviousNode[nextNode] = currentNode;
        open.Push(nextNode, nextCost);
    }

    private static bool TryBuildPortalHierarchyQuery(
        NavigationWorld world,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PortalHierarchyQueryResult result)
    {
        result = null;
        PortalHierarchy hierarchy = world?.Hierarchy;
        if (hierarchy?.Levels == null || hierarchy.Levels.Length == 0)
            return false;

        PortalHierarchyLevel level = null;
        for (int i = hierarchy.Levels.Length - 1; i >= 0; i--)
        {
            PortalHierarchyLevel candidate = hierarchy.Levels[i];
            if (ResolveHierarchyClusterId(world, candidate, startSectorId)
                != ResolveHierarchyClusterId(world, candidate, goalSectorId))
            {
                level = candidate;
                break;
            }
        }
        if (level == null)
            return false;

        int levelArrayIndex = level.Level - 1;
        PortalHierarchyCluster startCluster = level.Clusters[ResolveHierarchyClusterId(world, level, startSectorId)];
        PortalHierarchyCluster goalCluster = level.Clusters[ResolveHierarchyClusterId(world, level, goalSectorId)];
        PortalHierarchyConnector startConnector = BuildHierarchyConnector(
            world,
            hierarchy,
            levelArrayIndex,
            startSectorId,
            startX,
            startY,
            reverse: false);
        PortalHierarchyConnector goalConnector = BuildHierarchyConnector(
            world,
            hierarchy,
            levelArrayIndex,
            goalSectorId,
            goalX,
            goalY,
            reverse: true);

        var open = new DeterministicCostHeap();
        var costs = new Dictionary<int, long>(256);
        var previous = new Dictionary<int, int>(256);
        var settled = new HashSet<int>();
        for (int i = 0; i < startCluster.BoundaryNodes.Length; i++)
        {
            int node = startCluster.BoundaryNodes[i];
            if (!startConnector.Search.Costs.TryGetValue(node, out long cost))
                continue;
            if (!startConnector.Search.SettledNodes.Contains(node))
                throw new InvalidOperationException($"TryBuildPortalHierarchyQuery failed: start connector exposed an unsettled boundary cost node={node}.");
            costs[node] = cost;
            open.Push(node, cost);
        }

        int bestGoalNode = int.MinValue;
        long bestGoalCost = long.MaxValue;
        int expansions = CountPortalHierarchyConnectorExpansions(startConnector)
                         + CountPortalHierarchyConnectorExpansions(goalConnector);
        while (open.Count > 0)
        {
            DeterministicCostQueueNode item = open.Pop();
            if (!costs.TryGetValue(item.Index, out long currentCost) || item.Cost != currentCost || !settled.Add(item.Index))
                continue;
            expansions++;

            if (goalConnector.Search.SettledNodes.Contains(item.Index)
                && goalConnector.Search.Costs.TryGetValue(item.Index, out long goalSuffixCost))
            {
                long total = AddDeterministicPortalCosts(currentCost, goalSuffixCost);
                if (total < bestGoalCost)
                {
                    bestGoalCost = total;
                    bestGoalNode = item.Index;
                }
            }
            if (bestGoalCost != long.MaxValue && open.PeekCost >= bestGoalCost)
                break;

            DecodePortalNode(item.Index, out int sectorId, out int portalId);
            int clusterId = ResolveHierarchyClusterId(world, level, sectorId);
            PortalHierarchyCluster cluster = level.Clusters[clusterId];
            if (!cluster.OutgoingEdgesByNode.TryGetValue(item.Index, out List<PortalHierarchyEdge> outgoing))
                throw new InvalidOperationException($"TryBuildPortalHierarchyQuery failed: node is absent from cluster overlay level={level.Level} cluster={clusterId} node={item.Index}.");
            for (int i = 0; i < outgoing.Count; i++)
            {
                PortalHierarchyEdge edge = outgoing[i];
                RelaxHierarchyQueryNode(costs, previous, settled, open, item.Index, edge.ToNode,
                    AddDeterministicPortalCosts(currentCost, edge.DeterministicCost));
            }

            PortalData portal = GetPortalById(world, portalId);
            int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
            int oppositeNode = EncodePortalNode(oppositeSectorId, portalId);
            if (ResolveHierarchyClusterId(world, level, oppositeSectorId) == clusterId)
                throw new InvalidOperationException($"TryBuildPortalHierarchyQuery failed: boundary node crosses inside same cluster level={level.Level} node={item.Index}.");
            RelaxHierarchyQueryNode(costs, previous, settled, open, item.Index, oppositeNode,
                AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost));
        }

        if (bestGoalNode == int.MinValue)
            return false;

        int[] overlayNodes = ReconstructForwardPortalWitness(previous, bestGoalNode);
        var l0Nodes = new List<int>(overlayNodes.Length * 4);
        AppendHierarchyConnectorWitness(world, hierarchy, startConnector, overlayNodes[0], reverse: false, l0Nodes);
        for (int i = 0; i + 1 < overlayNodes.Length; i++)
        {
            int fromNode = overlayNodes[i];
            int toNode = overlayNodes[i + 1];
            DecodePortalNode(fromNode, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(toNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId && GetOppositeSectorId(GetPortalById(world, fromPortalId), fromSectorId) == toSectorId)
            {
                AppendPortalNode(l0Nodes, toNode);
                continue;
            }

            PortalHierarchyCluster cluster = level.Clusters[ResolveHierarchyClusterId(world, level, fromSectorId)];
            PortalHierarchyEdge edge = FindPortalHierarchyEdge(cluster, fromNode, toNode);
            AppendExpandedHierarchyWitness(world, hierarchy, levelArrayIndex, edge.ChildWitnessNodes, l0Nodes);
        }
        AppendHierarchyConnectorWitness(world, hierarchy, goalConnector, bestGoalNode, reverse: true, l0Nodes);

        result = new PortalHierarchyQueryResult
        {
            DeterministicCost = bestGoalCost,
            Level = level.Level,
            ExpansionCount = expansions,
            L0Nodes = l0Nodes.ToArray()
        };
        return true;
    }

    private static bool IsPortalHierarchyQueryRequired(
        NavigationWorld world,
        int startSectorId,
        int goalSectorId)
    {
        PortalHierarchyLevel[] levels = world?.Hierarchy?.Levels;
        if (levels == null)
            return false;
        for (int i = levels.Length - 1; i >= 0; i--)
        {
            if (ResolveHierarchyClusterId(world, levels[i], startSectorId)
                != ResolveHierarchyClusterId(world, levels[i], goalSectorId))
                return true;
        }
        return false;
    }

    private static bool TryBuildPathHandleWithPortalHierarchy(
        SectorPathCacheKey sectorPathKey,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        handle = null;
        if (!TryBuildPortalHierarchyQuery(
                _world,
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                out PortalHierarchyQueryResult query))
        {
            return false;
        }
        if (query.L0Nodes == null || query.L0Nodes.Length == 0)
            throw new InvalidOperationException("TryBuildPathHandleWithPortalHierarchy failed: hierarchy query returned no L0 witness.");
        if (!TryConvertPortalNodesToPath(
                new List<int>(query.L0Nodes),
                startSectorId,
                goalSectorId,
                out List<int> sectorIds,
                out List<int> portalIds))
        {
            throw new InvalidOperationException(
                $"TryBuildPathHandleWithPortalHierarchy failed: L0 witness cannot be converted startSector={startSectorId} goalSector={goalSectorId} level={query.Level}.");
        }

        _perf.PathPortalGraphNodeExpansions += query.ExpansionCount;
        handle = CreateAndCachePathHandle(sectorPathKey, sectorIds, portalIds, goalX, goalY);
        handle.BuildSource = $"portalHierarchyL{query.Level}";
        return true;
    }

    private static bool TryBuildPathHandleFromPortalHierarchyReversePolicy(
        SectorCorridorPolicy policy,
        SectorPathCacheKey sectorPathKey,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        ref bool policyAuthorityMutationStarted,
        out PathHandle handle)
    {
        handle = null;
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long hierarchyStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        int levelArrayIndex = ResolveHighestRequiredPortalHierarchyLevelIndex(_world, startSectorId, goalSectorId);
        if (levelArrayIndex < 0)
            return false;

        PortalHierarchyReversePolicy hierarchyPolicy = GetOrCreatePortalHierarchyReversePolicy(
            policy,
            levelArrayIndex,
            goalSectorId,
            goalX,
            goalY,
            ref policyAuthorityMutationStarted);
        if (!TryBuildPortalHierarchyDownwardCustomizationChain(
                policy,
                hierarchyPolicy,
                startSectorId,
                startX,
                startY,
                ref policyAuthorityMutationStarted,
                out List<PortalHierarchyDownwardCustomization> customizations))
        {
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCorridorPolicyHierarchy,
                    Stopwatch.GetTimestamp() - hierarchyStartTicks);
            }
            return false;
        }

        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyHierarchy,
                Stopwatch.GetTimestamp() - hierarchyStartTicks);
        }

        return TryCreatePathHandleFromPortalHierarchyDownwardCustomizations(
            policy,
            hierarchyPolicy,
            customizations,
            sectorPathKey,
            startSectorId,
            goalSectorId,
            goalX,
            goalY,
            ref policyAuthorityMutationStarted,
            out handle);
    }

    private static bool ExpandHashedPortalHierarchyReversePolicyToStartCell(
        SectorCorridorPolicyKey key,
        SectorCorridorPolicy policy,
        SectorPathCacheKey sectorPathKey,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        bool deferred = _navigationSyncBatchResolveActive
                        && DeferredSectorCorridorPolicyAuthorityKeys.Contains(key);
        if (!SectorCorridorPolicies.TryGetValue(key, out SectorCorridorPolicy cached)
            || !ReferenceEquals(cached, policy)
            || (!policy.HasAuthorityContentHash && !deferred))
        {
            throw new InvalidOperationException("Cannot expand an uncommitted portal hierarchy reverse policy.");
        }

        bool policyAuthorityMutationStarted = false;
        try
        {
            return TryBuildPathHandleFromPortalHierarchyReversePolicy(
                policy,
                sectorPathKey,
                startSectorId,
                goalSectorId,
                startX,
                startY,
                goalX,
                goalY,
                ref policyAuthorityMutationStarted,
                out handle);
        }
        finally
        {
            if (policyAuthorityMutationStarted)
            {
                if (_navigationSyncBatchResolveActive)
                {
                    DeferredSectorCorridorPolicyAuthorityKeys.Add(key);
                }
                else
                {
                    RefreshSectorCorridorPolicyAuthority(key, policy);
                }
            }
        }
    }

    private static void BeginHashedPortalHierarchyPolicyMutation(
        SectorCorridorPolicy policy,
        ref bool policyAuthorityMutationStarted)
    {
        if (policyAuthorityMutationStarted || !policy.HasAuthorityContentHash)
            return;

        _sectorCorridorPolicyAuthorityContentHash ^= policy.AuthorityContentHash;
        policy.HasAuthorityContentHash = false;
        policyAuthorityMutationStarted = true;
    }

    private static int ResolveHighestRequiredPortalHierarchyLevelIndex(
        NavigationWorld world,
        int startSectorId,
        int goalSectorId)
    {
        PortalHierarchyLevel[] levels = world?.Hierarchy?.Levels;
        if (levels == null)
            return -1;
        for (int i = levels.Length - 1; i >= 0; i--)
        {
            if (ResolveHierarchyClusterId(world, levels[i], startSectorId)
                != ResolveHierarchyClusterId(world, levels[i], goalSectorId))
            {
                return i;
            }
        }
        return -1;
    }

    private static PortalHierarchyReversePolicy GetOrCreatePortalHierarchyReversePolicy(
        SectorCorridorPolicy policy,
        int levelArrayIndex,
        int goalSectorId,
        int goalX,
        int goalY,
        ref bool policyAuthorityMutationStarted)
    {
        if (policy.HierarchyPolicies.TryGetValue(levelArrayIndex, out PortalHierarchyReversePolicy cached))
        {
            if (cached.GoalSectorId != goalSectorId || cached.GoalX != goalX || cached.GoalY != goalY)
            {
                throw new InvalidOperationException(
                    $"Portal hierarchy reverse policy goal mismatch level={levelArrayIndex + 1} cached=({cached.GoalSectorId},{cached.GoalX},{cached.GoalY}) requested=({goalSectorId},{goalX},{goalY}).");
            }
            return cached;
        }

        PortalHierarchy hierarchy = _world.Hierarchy
            ?? throw new InvalidOperationException("Portal hierarchy reverse policy requires a committed hierarchy.");
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long connectorStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PortalHierarchyConnector goalConnector = ResolveOrBuildPortalHierarchyGoalConnector(
            policy,
            _world,
            hierarchy,
            goalSectorId,
            goalX,
            goalY,
            levelArrayIndex);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyGoalConnector,
                Stopwatch.GetTimestamp() - connectorStartTicks);
        }
        PortalHierarchyLevel level = hierarchy.Levels[levelArrayIndex];
        PortalHierarchyCluster goalCluster = level.Clusters[ResolveHierarchyClusterId(_world, level, goalSectorId)];
        var created = new PortalHierarchyReversePolicy
        {
            LevelArrayIndex = levelArrayIndex,
            GoalSectorId = goalSectorId,
            GoalX = goalX,
            GoalY = goalY,
            GoalConnector = goalConnector
        };
        created.GoalConnectorAuthorityContentHash =
            ComputePortalHierarchyConnectorAuthorityContentHash(goalConnector);
        for (int i = 0; i < goalCluster.BoundaryNodes.Length; i++)
        {
            int node = goalCluster.BoundaryNodes[i];
            if (!goalConnector.TryGetCost(node, out long cost))
                continue;
            if (!goalConnector.ContainsSettled(node))
                throw new InvalidOperationException(
                    $"Portal hierarchy reverse policy goal connector exposed an unsettled boundary cost level={level.Level} node={node}.");
            created.SearchState.AddSource(node, cost);
        }
        if (created.SearchState.OpenCount == 0)
        {
            throw new InvalidOperationException(
                $"Portal hierarchy reverse policy has no goal connector level={level.Level} goalSector={goalSectorId} goal=({goalX},{goalY}).");
        }
        BeginHashedPortalHierarchyPolicyMutation(policy, ref policyAuthorityMutationStarted);
        policy.HierarchyPolicies.Add(levelArrayIndex, created);
        return created;
    }

    private static PortalHierarchyConnector ResolveOrBuildPortalHierarchyGoalConnector(
        SectorCorridorPolicy policy,
        NavigationWorld world,
        PortalHierarchy hierarchy,
        int goalSectorId,
        int goalX,
        int goalY,
        int targetLevelIndex)
    {
        for (int levelArrayIndex = targetLevelIndex + 1;
             levelArrayIndex < hierarchy.Levels.Length;
             levelArrayIndex++)
        {
            if (!policy.HierarchyPolicies.TryGetValue(levelArrayIndex, out PortalHierarchyReversePolicy higherPolicy))
                continue;
            ValidatePortalHierarchyGoalPolicy(higherPolicy, goalSectorId, goalX, goalY);
            PortalHierarchyConnector connector = higherPolicy.GoalConnector;
            while (connector != null && connector.TargetLevelIndex > targetLevelIndex)
                connector = connector.Child;
            if (connector == null || connector.TargetLevelIndex != targetLevelIndex)
                throw new InvalidOperationException($"Portal hierarchy goal connector chain is incomplete targetLevel={targetLevelIndex + 1}.");
            return connector;
        }

        PortalHierarchyConnector highestLowerConnector = null;
        for (int levelArrayIndex = targetLevelIndex - 1; levelArrayIndex >= 0; levelArrayIndex--)
        {
            if (!policy.HierarchyPolicies.TryGetValue(levelArrayIndex, out PortalHierarchyReversePolicy lowerPolicy))
                continue;
            ValidatePortalHierarchyGoalPolicy(lowerPolicy, goalSectorId, goalX, goalY);
            highestLowerConnector = lowerPolicy.GoalConnector;
            break;
        }
        _perf.SectorCorridorGoalConnectorBuilds++;
        if (highestLowerConnector == null)
        {
            return BuildHierarchyConnector(
                world,
                hierarchy,
                targetLevelIndex,
                goalSectorId,
                goalX,
                goalY,
                reverse: true);
        }

        PortalHierarchyConnector result = highestLowerConnector;
        for (int levelArrayIndex = result.TargetLevelIndex + 1;
             levelArrayIndex <= targetLevelIndex;
             levelArrayIndex++)
        {
            result = ExtendHierarchyConnector(
                world,
                hierarchy,
                levelArrayIndex,
                goalSectorId,
                reverse: true,
                result);
        }
        return result;
    }

    private static bool TryRebindPortalHierarchyReversePoliciesExactGoal(
        SectorCorridorPolicy ownerPolicy,
        int goalSectorId,
        int goalX,
        int goalY)
    {
        if (ownerPolicy == null)
            throw new ArgumentNullException(nameof(ownerPolicy));
        if (ownerPolicy.HierarchyPolicies.Count == 0)
            return false;
        PortalHierarchy hierarchy = _world.Hierarchy
            ?? throw new InvalidOperationException("Moving-target hierarchy policy rebind requires a committed hierarchy.");

        int highestLevelArrayIndex = -1;
        foreach (int levelArrayIndex in ownerPolicy.HierarchyPolicies.Keys)
            highestLevelArrayIndex = Math.Max(highestLevelArrayIndex, levelArrayIndex);
        if (highestLevelArrayIndex < 0 || highestLevelArrayIndex >= hierarchy.Levels.Length)
            throw new InvalidOperationException($"Moving-target hierarchy policy has an invalid highest level={highestLevelArrayIndex + 1}.");

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long connectorStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        _perf.SectorCorridorGoalConnectorBuilds++;
        PortalHierarchyConnector highestConnector = BuildHierarchyConnector(
            _world,
            hierarchy,
            highestLevelArrayIndex,
            goalSectorId,
            goalX,
            goalY,
            reverse: true);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyGoalConnector,
                Stopwatch.GetTimestamp() - connectorStartTicks);
        }

        var replacementConnectors = new Dictionary<int, PortalHierarchyConnector>(ownerPolicy.HierarchyPolicies.Count);
        bool hasUniformDelta = false;
        long uniformDelta = 0L;
        foreach (KeyValuePair<int, PortalHierarchyReversePolicy> pair in ownerPolicy.HierarchyPolicies)
        {
            int levelArrayIndex = pair.Key;
            PortalHierarchyReversePolicy existing = pair.Value
                ?? throw new InvalidOperationException($"Moving-target hierarchy policy contains a null level={levelArrayIndex + 1}.");
            PortalHierarchyConnector replacement = ResolvePortalHierarchyConnectorAtLevel(
                highestConnector,
                levelArrayIndex);
            if (!ArePortalHierarchyGoalBoundaryCostsUniformlyShifted(
                    hierarchy,
                    levelArrayIndex,
                    existing.GoalSectorId,
                    existing.GoalConnector,
                    goalSectorId,
                    replacement,
                    out long levelDelta))
            {
                return false;
            }
            if (!hasUniformDelta)
            {
                uniformDelta = levelDelta;
                hasUniformDelta = true;
            }
            else if (levelDelta != uniformDelta)
            {
                throw new InvalidOperationException(
                    $"Moving-target hierarchy connector uniform shift differs between levels expected={uniformDelta}, actual={levelDelta}, level={levelArrayIndex + 1}.");
            }
            replacementConnectors.Add(levelArrayIndex, replacement);
        }
        if (!hasUniformDelta)
            return false;

        var previousConnectors = new HashSet<PortalHierarchyConnector>();
        foreach (PortalHierarchyReversePolicy existing in ownerPolicy.HierarchyPolicies.Values)
        {
            for (PortalHierarchyConnector connector = existing?.GoalConnector;
                 connector != null;
                 connector = connector.Child)
            {
                previousConnectors.Add(connector);
            }
        }
        foreach (KeyValuePair<int, PortalHierarchyReversePolicy> pair in ownerPolicy.HierarchyPolicies)
        {
            PortalHierarchyReversePolicy existing = pair.Value;
            ShiftPortalHierarchyReversePolicyCosts(existing, uniformDelta);
            existing.GoalSectorId = goalSectorId;
            existing.GoalX = goalX;
            existing.GoalY = goalY;
            existing.GoalConnector = replacementConnectors[pair.Key];
            existing.GoalConnectorAuthorityContentHash =
                ComputePortalHierarchyConnectorAuthorityContentHash(existing.GoalConnector);
            existing.L0WitnessesByStartNode.Clear();
            existing.L0WitnessesAuthorityContentHash = 0UL;
        }
        var replacementConnectorSet = new HashSet<PortalHierarchyConnector>();
        foreach (PortalHierarchyConnector replacement in replacementConnectors.Values)
        {
            for (PortalHierarchyConnector connector = replacement;
                 connector != null;
                 connector = connector.Child)
            {
                replacementConnectorSet.Add(connector);
            }
        }
        foreach (PortalHierarchyConnector previous in previousConnectors)
        {
            if (!replacementConnectorSet.Contains(previous))
                previous.Dispose();
        }
        return true;
    }

    private static PortalHierarchyConnector ResolvePortalHierarchyConnectorAtLevel(
        PortalHierarchyConnector connector,
        int levelArrayIndex)
    {
        while (connector != null && connector.TargetLevelIndex > levelArrayIndex)
            connector = connector.Child;
        if (connector == null || connector.TargetLevelIndex != levelArrayIndex)
            throw new InvalidOperationException($"Moving-target goal connector chain is incomplete level={levelArrayIndex + 1}.");
        return connector;
    }

    private static bool ArePortalHierarchyGoalBoundaryCostsUniformlyShifted(
        PortalHierarchy hierarchy,
        int levelArrayIndex,
        int previousGoalSectorId,
        PortalHierarchyConnector previousConnector,
        int nextGoalSectorId,
        PortalHierarchyConnector nextConnector,
        out long uniformDelta)
    {
        uniformDelta = 0L;
        if (previousConnector?.Search == null || nextConnector?.Search == null)
            throw new InvalidOperationException("Moving-target hierarchy rebind encountered an invalid goal connector.");
        PortalHierarchyLevel level = hierarchy.Levels[levelArrayIndex];
        int previousClusterId = ResolveHierarchyClusterId(_world, level, previousGoalSectorId);
        int nextClusterId = ResolveHierarchyClusterId(_world, level, nextGoalSectorId);
        if (previousClusterId != nextClusterId)
            return false;
        int[] boundaryNodes = level.Clusters[previousClusterId].BoundaryNodes;
        bool hasDelta = false;
        for (int i = 0; i < boundaryNodes.Length; i++)
        {
            int node = boundaryNodes[i];
            bool previousHasCost = previousConnector.TryGetCost(node, out long previousCost);
            bool nextHasCost = nextConnector.TryGetCost(node, out long nextCost);
            if (previousHasCost != nextHasCost)
                return false;
            if (!previousHasCost)
                continue;
            if (!previousConnector.ContainsSettled(node)
                || !nextConnector.ContainsSettled(node))
            {
                throw new InvalidOperationException(
                    $"Moving-target hierarchy connector exposes an unsettled boundary node={node}, level={level.Level}.");
            }
            long candidateDelta = checked(nextCost - previousCost);
            if (!hasDelta)
            {
                uniformDelta = candidateDelta;
                hasDelta = true;
            }
            else if (candidateDelta != uniformDelta)
            {
                return false;
            }
        }
        return hasDelta;
    }

    private static void ShiftPortalHierarchyReversePolicyCosts(
        PortalHierarchyReversePolicy policy,
        long delta)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));
        if (delta == 0L)
            return;

        policy.SearchState.ShiftCosts(delta);
        policy.DownwardCustomizationsAuthorityContentHash = 0UL;
        foreach (KeyValuePair<PortalHierarchyCustomizationKey, PortalHierarchyDownwardCustomization> pair
                 in policy.DownwardCustomizations)
        {
            PortalHierarchyDownwardCustomization customization = pair.Value
                ?? throw new InvalidOperationException("Moving-target hierarchy rebind encountered a null downward customization.");
            customization.ShiftCosts(delta);
            customization.AuthorityContentHash =
                ComputePortalHierarchyDownwardCustomizationAuthorityContentHash(pair.Key, customization);
            policy.DownwardCustomizationsAuthorityContentHash ^=
                ComputePortalHierarchyCustomizationEntryAuthorityToken(
                    pair.Key,
                    customization.AuthorityContentHash);
        }
    }

    private static ulong ShiftPortalCostDictionary(
        Dictionary<int, long> costs,
        long delta,
        ulong authorityDomain)
    {
        if (costs == null)
            throw new ArgumentNullException(nameof(costs));
        var nodes = new List<int>(costs.Keys);
        ulong authorityHash = 0UL;
        for (int i = 0; i < nodes.Count; i++)
        {
            int node = nodes[i];
            long shifted = checked(costs[node] + delta);
            costs[node] = shifted;
            authorityHash ^= ComputeAuthorityIntLongToken(authorityDomain, node, shifted);
        }
        return authorityHash;
    }

    private static void ShiftPortalGraphSearchCosts(PortalGraphSearchResult search, long delta)
    {
        if (search == null)
            throw new ArgumentNullException(nameof(search));
        if (delta == 0L)
            return;
        var nodes = new List<int>(search.Costs.Keys);
        for (int i = 0; i < nodes.Count; i++)
        {
            int node = nodes[i];
            search.Costs[node] = checked(search.Costs[node] + delta);
        }
    }

    private static void ValidatePortalHierarchyGoalPolicy(
        PortalHierarchyReversePolicy policy,
        int goalSectorId,
        int goalX,
        int goalY)
    {
        if (policy == null
            || policy.GoalSectorId != goalSectorId
            || policy.GoalX != goalX
            || policy.GoalY != goalY
            || policy.GoalConnector == null)
        {
            throw new InvalidOperationException("Portal hierarchy policy cache contains an invalid goal connector.");
        }
    }

    private static bool TryBuildPortalHierarchyDownwardCustomizationChain(
        SectorCorridorPolicy ownerPolicy,
        PortalHierarchyReversePolicy policy,
        int startSectorId,
        int startX,
        int startY,
        ref bool policyAuthorityMutationStarted,
        out List<PortalHierarchyDownwardCustomization> customizations)
    {
        PortalHierarchy hierarchy = _world.Hierarchy
            ?? throw new InvalidOperationException("Portal hierarchy reverse policy requires a committed hierarchy.");
#if UNITY_EDITOR
        RecordEditorHierarchyStartConnectorDiagnostic(
            hierarchy,
            policy.LevelArrayIndex,
            startSectorId,
            startX,
            startY,
            policy.GoalSectorId,
            policy.GoalX,
            policy.GoalY);
#endif
        PortalHierarchyLevel topLevel = hierarchy.Levels[policy.LevelArrayIndex];
        PortalHierarchyCluster topCluster = topLevel.Clusters[
            ResolveHierarchyClusterId(_world, topLevel, startSectorId)];
        if (!EnsurePortalHierarchyReversePolicyClusterCovered(
                ownerPolicy,
                policy,
                topLevel,
                topCluster,
                ref policyAuthorityMutationStarted))
        {
            customizations = null;
            return false;
        }

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long customizationStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        customizations = new List<PortalHierarchyDownwardCustomization>(policy.LevelArrayIndex + 1);
        PortalHierarchyDownwardCustomization sourceCustomization = null;
        try
        {
            for (int sourceLevelArrayIndex = policy.LevelArrayIndex;
                 sourceLevelArrayIndex >= 0;
                 sourceLevelArrayIndex--)
            {
                PortalHierarchyLevel sourceLevel = hierarchy.Levels[sourceLevelArrayIndex];
                PortalHierarchyCluster containingCluster = sourceLevel.Clusters[
                    ResolveHierarchyClusterId(_world, sourceLevel, startSectorId)];
                int targetRegionId = sourceLevelArrayIndex == 0
                    ? startSectorId
                    : ResolveHierarchyClusterId(
                        _world,
                        hierarchy.Levels[sourceLevelArrayIndex - 1],
                        startSectorId);
                var key = new PortalHierarchyCustomizationKey(
                    sourceLevelArrayIndex,
                    containingCluster.ClusterId,
                    targetRegionId);
                if (policy.DownwardCustomizations.TryGetValue(
                        key,
                        out PortalHierarchyDownwardCustomization cached))
                {
                    if (cached == null
                        || cached.SourceLevelArrayIndex != sourceLevelArrayIndex
                        || cached.ClusterId != containingCluster.ClusterId
                        || cached.TargetRegionId != targetRegionId)
                    {
                        throw new InvalidOperationException(
                            $"Portal hierarchy downward customization cache is invalid level={sourceLevel.Level} cluster={containingCluster.ClusterId}.");
                    }
                    cached.RequireSingleAuthority();
                    _perf.HierarchyDownwardCustomizationCacheHits++;
                    customizations.Add(cached);
                    sourceCustomization = cached;
                    continue;
                }

                _perf.HierarchyDownwardCustomizationCacheMisses++;
                CollectPortalHierarchyCustomizationSources(
                    policy,
                    sourceCustomization,
                    containingCluster,
                    sourceLevelArrayIndex == policy.LevelArrayIndex,
                    out int[] sourceNodes,
                    out long[] sourceCosts);
                if (sourceNodes.Length == 0)
                    return false;
                HashSet<int> targetNodes = CollectPortalHierarchyCustomizationTargets(
                    hierarchy,
                    sourceLevelArrayIndex,
                    startSectorId);

                PortalGraphSearchResult search = sourceLevelArrayIndex == 0
                    ? RunRestrictedPortalGraphDijkstra(
                        _world,
                        containingCluster,
                        sourceNodes,
                        sourceCosts,
                        reverse: true,
                        targetNodes)
                    : RunRestrictedPortalHierarchyDijkstra(
                        _world,
                        hierarchy.Levels[sourceLevelArrayIndex - 1],
                        containingCluster,
                        sourceNodes,
                        sourceCosts,
                        reverse: true,
                        targetNodes);
                var created = new PortalHierarchyDownwardCustomization
                {
                    SourceLevelArrayIndex = sourceLevelArrayIndex,
                    ClusterId = containingCluster.ClusterId,
                    TargetRegionId = targetRegionId,
                    Search = search
                };
                created.AuthorityContentHash = ComputePortalHierarchyDownwardCustomizationAuthorityContentHash(
                    key,
                    created);
                BeginHashedPortalHierarchyPolicyMutation(ownerPolicy, ref policyAuthorityMutationStarted);
                policy.DownwardCustomizations.Add(key, created);
                policy.DownwardCustomizationsAuthorityContentHash ^=
                    ComputePortalHierarchyCustomizationEntryAuthorityToken(key, created.AuthorityContentHash);
                _perf.HierarchyDownwardCustomizationExpansions = checked(
                    _perf.HierarchyDownwardCustomizationExpansions + search.ExpansionCount);
                customizations.Add(created);
                sourceCustomization = created;
            }
        }
        finally
        {
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCorridorPolicyDownwardCustomize,
                    Stopwatch.GetTimestamp() - customizationStartTicks);
            }
        }
        return customizations.Count > 0;
    }

    private static bool EnsurePortalHierarchyReversePolicyClusterCovered(
        SectorCorridorPolicy ownerPolicy,
        PortalHierarchyReversePolicy policy,
        PortalHierarchyLevel level,
        PortalHierarchyCluster cluster,
        ref bool policyAuthorityMutationStarted)
    {
        bool covered = ArePortalHierarchyClusterBoundariesSettled(policy, cluster);
        if (!covered && policy.SearchState.OpenCount > 0)
        {
            BeginHashedPortalHierarchyPolicyMutation(ownerPolicy, ref policyAuthorityMutationStarted);
            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            long queryStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            _perf.SectorCorridorPolicyQueries++;
            try
            {
                while (policy.SearchState.OpenCount > 0)
                {
                    FlowPathKernelPopStatus status = policy.SearchState.PopOne(out FlowPathKernelSearchEntry item);
                    if (status == FlowPathKernelPopStatus.CostMismatch
                        || status == FlowPathKernelPopStatus.AlreadySettled
                        || status == FlowPathKernelPopStatus.Stale)
                        continue;
                    if (status != FlowPathKernelPopStatus.Settled)
                        throw new InvalidOperationException($"Portal hierarchy reverse policy kernel pop failed status={status}, node={item.Node}.");
                    _perf.PathPortalGraphNodeExpansions++;
                    ExpandPortalHierarchyReversePolicyNode(policy, level, item.Node, item.Cost);
                    if (ArePortalHierarchyClusterBoundariesSettled(policy, cluster))
                    {
                        covered = true;
                        break;
                    }
                }
            }
            finally
            {
                if (profile)
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - queryStartTicks;
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.FlowCorridorPolicyReverseExpand,
                        elapsedTicks);
                    _perf.SectorCorridorPolicyTicks += elapsedTicks;
                }
            }
        }

        if (!covered && policy.SearchState.OpenCount > 0)
            throw new InvalidOperationException("Portal hierarchy reverse policy cluster coverage stopped before completion.");
        for (int i = 0; i < cluster.BoundaryNodes.Length; i++)
        {
            if (policy.SearchState.ContainsSettled(cluster.BoundaryNodes[i]))
                return true;
        }
        return false;
    }

    private static bool ArePortalHierarchyClusterBoundariesSettled(
        PortalHierarchyReversePolicy policy,
        PortalHierarchyCluster cluster)
    {
        for (int i = 0; i < cluster.BoundaryNodes.Length; i++)
        {
            if (!policy.SearchState.ContainsSettled(cluster.BoundaryNodes[i]))
                return false;
        }
        return true;
    }

    private static void CollectPortalHierarchyCustomizationSources(
        PortalHierarchyReversePolicy policy,
        PortalHierarchyDownwardCustomization sourceCustomization,
        PortalHierarchyCluster containingCluster,
        bool usePolicySources,
        out int[] sourceNodes,
        out long[] sourceCosts)
    {
        var nodes = new List<int>(containingCluster.BoundaryNodes.Length);
        var costs = new List<long>(containingCluster.BoundaryNodes.Length);
        for (int i = 0; i < containingCluster.BoundaryNodes.Length; i++)
        {
            int node = containingCluster.BoundaryNodes[i];
            bool settled = usePolicySources
                ? policy.SearchState.ContainsSettled(node)
                : sourceCustomization != null && sourceCustomization.ContainsSettled(node);
            if (!settled)
                continue;
            bool hasCost = usePolicySources
                ? policy.SearchState.TryGetCost(node, out long cost)
                : sourceCustomization.TryGetCost(node, out cost);
            if (!hasCost)
                throw new InvalidOperationException($"Portal hierarchy customization settled source has no cost node={node}.");
            nodes.Add(node);
            costs.Add(cost);
        }
        sourceNodes = nodes.ToArray();
        sourceCosts = costs.ToArray();
    }

    private static HashSet<int> CollectPortalHierarchyCustomizationTargets(
        PortalHierarchy hierarchy,
        int sourceLevelArrayIndex,
        int startSectorId)
    {
        if (sourceLevelArrayIndex == 0)
        {
            SectorData sector = _world.Sectors[startSectorId];
            var targets = new HashSet<int>();
            for (int i = 0; i < sector.PortalIds.Count; i++)
                targets.Add(EncodePortalNode(startSectorId, sector.PortalIds[i]));
            if (targets.Count == 0)
                throw new InvalidOperationException(
                    $"Portal hierarchy customization start sector has no portals sector={startSectorId}.");
            return targets;
        }

        PortalHierarchyLevel childLevel = hierarchy.Levels[sourceLevelArrayIndex - 1];
        PortalHierarchyCluster childCluster = childLevel.Clusters[
            ResolveHierarchyClusterId(_world, childLevel, startSectorId)];
        if (childCluster.BoundaryNodes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Portal hierarchy customization child cluster has no boundaries level={childLevel.Level} cluster={childCluster.ClusterId}.");
        }
        return new HashSet<int>(childCluster.BoundaryNodes);
    }

    private static bool TryExpandPortalHierarchyReversePolicyToStart(
        SectorCorridorPolicy ownerPolicy,
        PortalHierarchyReversePolicy policy,
        int startSectorId,
        int startX,
        int startY,
        ref bool policyAuthorityMutationStarted,
        out PortalHierarchyConnector startConnector)
    {
        PortalHierarchy hierarchy = _world.Hierarchy
            ?? throw new InvalidOperationException("Portal hierarchy reverse policy requires a committed hierarchy.");
        PortalHierarchyLevel level = hierarchy.Levels[policy.LevelArrayIndex];
        PortalHierarchyCluster startCluster = level.Clusters[ResolveHierarchyClusterId(_world, level, startSectorId)];
#if UNITY_EDITOR
        RecordEditorHierarchyStartConnectorDiagnostic(
            hierarchy,
            policy.LevelArrayIndex,
            startSectorId,
            startX,
            startY,
            policy.GoalSectorId,
            policy.GoalX,
            policy.GoalY);
#endif
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long connectorStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        startConnector = BuildHierarchyConnector(
            _world,
            hierarchy,
            policy.LevelArrayIndex,
            startSectorId,
            startX,
            startY,
            reverse: false);
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyStartConnector,
                Stopwatch.GetTimestamp() - connectorStartTicks);
        }

        bool hasAccessibleBoundary = false;
        bool covered = true;
        for (int i = 0; i < startCluster.BoundaryNodes.Length; i++)
        {
            int node = startCluster.BoundaryNodes[i];
            if (!startConnector.TryGetCost(node, out _))
                continue;
            hasAccessibleBoundary = true;
            if (!policy.SearchState.ContainsSettled(node))
                covered = false;
        }
        if (!hasAccessibleBoundary)
            return false;
        if (covered)
            return true;

        BeginHashedPortalHierarchyPolicyMutation(ownerPolicy, ref policyAuthorityMutationStarted);
        long queryStartTicks = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        _perf.SectorCorridorPolicyQueries++;
        try
        {
            while (policy.SearchState.OpenCount > 0)
            {
                FlowPathKernelPopStatus status = policy.SearchState.PopOne(out FlowPathKernelSearchEntry item);
                if (status == FlowPathKernelPopStatus.CostMismatch
                    || status == FlowPathKernelPopStatus.AlreadySettled
                    || status == FlowPathKernelPopStatus.Stale)
                    continue;
                if (status != FlowPathKernelPopStatus.Settled)
                    throw new InvalidOperationException($"Portal hierarchy reverse policy kernel pop failed status={status}, node={item.Node}.");
                _perf.PathPortalGraphNodeExpansions++;

                ExpandPortalHierarchyReversePolicyNode(policy, level, item.Node, item.Cost);
                covered = true;
                for (int i = 0; i < startCluster.BoundaryNodes.Length; i++)
                {
                    int boundaryNode = startCluster.BoundaryNodes[i];
                    if (startConnector.TryGetCost(boundaryNode, out _)
                        && !policy.SearchState.ContainsSettled(boundaryNode))
                    {
                        covered = false;
                        break;
                    }
                }
                if (covered)
                    return true;
            }
            return false;
        }
        finally
        {
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.FlowCorridorPolicyReverseExpand,
                    Stopwatch.GetTimestamp() - queryStartTicks);
                _perf.SectorCorridorPolicyTicks +=
                    System.Diagnostics.Stopwatch.GetTimestamp() - queryStartTicks;
            }
        }
    }

    private static void ExpandPortalHierarchyReversePolicyNode(
        PortalHierarchyReversePolicy policy,
        PortalHierarchyLevel level,
        int currentNode,
        long currentCost)
    {
        DecodePortalNode(currentNode, out int sectorId, out int portalId);
        int clusterId = ResolveHierarchyClusterId(_world, level, sectorId);
        PortalHierarchyCluster cluster = level.Clusters[clusterId];
        if (!cluster.IncomingEdgesByNode.TryGetValue(currentNode, out List<PortalHierarchyEdge> incoming))
        {
            throw new InvalidOperationException(
                $"Portal hierarchy reverse policy node is absent level={level.Level} cluster={clusterId} node={currentNode}.");
        }
        for (int i = 0; i < incoming.Count; i++)
        {
            PortalHierarchyEdge edge = incoming[i];
            RelaxPortalHierarchyReversePolicyNode(
                policy,
                edge.FromNode,
                currentNode,
                AddDeterministicPortalCosts(currentCost, edge.DeterministicCost));
        }

        PortalData portal = GetPortalById(_world, portalId);
        int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
        if (ResolveHierarchyClusterId(_world, level, oppositeSectorId) == clusterId)
        {
            throw new InvalidOperationException(
                $"Portal hierarchy reverse policy boundary crosses inside same cluster level={level.Level} node={currentNode}.");
        }
        RelaxPortalHierarchyReversePolicyNode(
            policy,
            EncodePortalNode(oppositeSectorId, portalId),
            currentNode,
            AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost));
    }

    private static void RelaxPortalHierarchyReversePolicyNode(
        PortalHierarchyReversePolicy policy,
        int predecessorNode,
        int nextNodeTowardGoal,
        long cost)
    {
        if (cost == long.MaxValue)
            return;
        policy.SearchState.Relax(nextNodeTowardGoal, predecessorNode, cost);
    }

    private static bool TryCreatePathHandleFromPortalHierarchyDownwardCustomizations(
        SectorCorridorPolicy ownerPolicy,
        PortalHierarchyReversePolicy policy,
        List<PortalHierarchyDownwardCustomization> customizations,
        SectorPathCacheKey sectorPathKey,
        int startSectorId,
        int goalSectorId,
        int goalX,
        int goalY,
        ref bool policyAuthorityMutationStarted,
        out PathHandle handle)
    {
        handle = null;
        if (customizations == null || customizations.Count != policy.LevelArrayIndex + 1)
            throw new InvalidOperationException("Portal hierarchy downward customization chain is incomplete.");

        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long reconstructStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PortalHierarchy hierarchy = _world.Hierarchy;
        PortalHierarchyDownwardCustomization leaf = customizations[customizations.Count - 1];
        if (leaf.SourceLevelArrayIndex != 0)
            throw new InvalidOperationException("Portal hierarchy downward customization chain has no raw leaf field.");
        leaf.RequireSingleAuthority();

        long accessStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        SectorData startSector = _world.Sectors[startSectorId];
        int bestStartNode = int.MinValue;
        long bestCost = long.MaxValue;
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            int node = EncodePortalNode(startSectorId, portalId);
            long prefixCost = ResolveDeterministicPortalAccessCost(
                _world,
                startSector,
                startSectorId,
                portalId,
                sectorPathKey.StartCellIndex % _world.Width,
                sectorPathKey.StartCellIndex / _world.Width);
            if (prefixCost == long.MaxValue
                || !leaf.ContainsSettled(node)
                || !leaf.TryGetCost(node, out long suffixCost))
            {
                continue;
            }
            long total = AddDeterministicPortalCosts(prefixCost, suffixCost);
            if (total > bestCost || (total == bestCost && node >= bestStartNode))
                continue;
            bestCost = total;
            bestStartNode = node;
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyStartLeafAccess,
                Stopwatch.GetTimestamp() - accessStartTicks);
        }
        if (bestStartNode == int.MinValue)
            return false;

        if (policy.L0WitnessesByStartNode.TryGetValue(bestStartNode, out PortalHierarchyL0Witness cachedRoute))
        {
            ValidatePortalHierarchyL0Witness(cachedRoute, bestStartNode, startSectorId, goalSectorId);
            _perf.HierarchyL0WitnessCacheHits++;
            handle = CreateAndCachePathHandle(
                sectorPathKey,
                cachedRoute.SectorIds,
                cachedRoute.PortalIds,
                goalX,
                goalY);
            handle.BuildSource = $"portalHierarchyPolicyL{_world.Hierarchy.Levels[policy.LevelArrayIndex].Level}:witnessCache";
            return true;
        }
        _perf.HierarchyL0WitnessCacheMisses++;

        var l0Nodes = new List<int>(32);
        int cursor = bestStartNode;
        AppendPortalNode(l0Nodes, cursor);
        for (int i = customizations.Count - 1; i >= 0; i--)
        {
            PortalHierarchyDownwardCustomization customization = customizations[i];
            int[] witness = ReconstructReversePortalWitness(customization, cursor);
            if (witness.Length == 0 || witness[0] != cursor)
                throw new InvalidOperationException("Portal hierarchy downward customization returned an invalid witness.");
            if (customization.SourceLevelArrayIndex == 0)
            {
                for (int nodeIndex = 1; nodeIndex < witness.Length; nodeIndex++)
                    AppendPortalNode(l0Nodes, witness[nodeIndex]);
            }
            else
            {
                AppendExpandedHierarchyWitness(
                    _world,
                    hierarchy,
                    customization.SourceLevelArrayIndex,
                    witness,
                    l0Nodes);
            }
            cursor = witness[witness.Length - 1];
        }

        var overlayNodes = new List<int>(32) { cursor };
        int guard = 0;
        while (policy.SearchState.TryGetPrevious(cursor, out int nextNode))
        {
            overlayNodes.Add(nextNode);
            cursor = nextNode;
            if (++guard > policy.SearchState.PreviousCount)
                throw new InvalidOperationException("Portal hierarchy reverse policy reconstruction contains a cycle.");
        }
        PortalHierarchyLevel level = hierarchy.Levels[policy.LevelArrayIndex];
        for (int i = 0; i + 1 < overlayNodes.Count; i++)
        {
            int fromNode = overlayNodes[i];
            int toNode = overlayNodes[i + 1];
            DecodePortalNode(fromNode, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(toNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId
                && GetOppositeSectorId(GetPortalById(_world, fromPortalId), fromSectorId) == toSectorId)
            {
                AppendPortalNode(l0Nodes, toNode);
                continue;
            }
            PortalHierarchyCluster cluster = level.Clusters[
                ResolveHierarchyClusterId(_world, level, fromSectorId)];
            PortalHierarchyEdge edge = FindPortalHierarchyEdge(cluster, fromNode, toNode);
            AppendExpandedHierarchyWitness(
                _world,
                hierarchy,
                policy.LevelArrayIndex,
                edge.ChildWitnessNodes,
                l0Nodes);
        }
        AppendHierarchyConnectorWitness(_world, hierarchy, policy.GoalConnector, cursor, true, l0Nodes);

        if (!TryConvertPortalNodesToPath(
                l0Nodes,
                startSectorId,
                goalSectorId,
                out List<int> sectorIds,
                out List<int> portalIds))
        {
            throw new InvalidOperationException(
                $"Portal hierarchy downward customization witness cannot be converted level={level.Level} startSector={startSectorId} goalSector={goalSectorId}.");
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyReconstruct,
                Stopwatch.GetTimestamp() - reconstructStartTicks);
        }
        var createdRoute = new PortalHierarchyL0Witness
        {
            StartNode = bestStartNode,
            SectorIds = sectorIds.ToArray(),
            PortalIds = portalIds.ToArray()
        };
        ValidatePortalHierarchyL0Witness(createdRoute, bestStartNode, startSectorId, goalSectorId);
        createdRoute.AuthorityContentHash = ComputePortalHierarchyL0WitnessAuthorityContentHash(createdRoute);
        BeginHashedPortalHierarchyPolicyMutation(ownerPolicy, ref policyAuthorityMutationStarted);
        policy.L0WitnessesByStartNode.Add(bestStartNode, createdRoute);
        policy.L0WitnessesAuthorityContentHash ^=
            ComputePortalHierarchyL0WitnessEntryAuthorityToken(bestStartNode, createdRoute.AuthorityContentHash);
        handle = CreateAndCachePathHandle(sectorPathKey, createdRoute.SectorIds, createdRoute.PortalIds, goalX, goalY);
        handle.BuildSource = $"portalHierarchyPolicyL{level.Level}";
        return true;
    }

    private static void ValidatePortalHierarchyL0Witness(
        PortalHierarchyL0Witness witness,
        int expectedStartNode,
        int startSectorId,
        int goalSectorId)
    {
        if (witness == null
            || witness.StartNode != expectedStartNode
            || witness.SectorIds == null
            || witness.PortalIds == null
            || witness.SectorIds.Length == 0
            || witness.PortalIds.Length + 1 != witness.SectorIds.Length
            || witness.SectorIds[0] != startSectorId
            || witness.SectorIds[witness.SectorIds.Length - 1] != goalSectorId)
        {
            throw new InvalidOperationException(
                $"Portal hierarchy L0 witness cache is invalid startNode={expectedStartNode} startSector={startSectorId} goalSector={goalSectorId}.");
        }
    }

    private static bool TryCreatePathHandleFromPortalHierarchyReversePolicy(
        PortalHierarchyReversePolicy policy,
        PortalHierarchyConnector startConnector,
        SectorPathCacheKey sectorPathKey,
        int startSectorId,
        int goalSectorId,
        int goalX,
        int goalY,
        out PathHandle handle)
    {
        handle = null;
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long reconstructStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PortalHierarchy hierarchy = _world.Hierarchy;
        PortalHierarchyLevel level = hierarchy.Levels[policy.LevelArrayIndex];
        PortalHierarchyCluster startCluster = level.Clusters[ResolveHierarchyClusterId(_world, level, startSectorId)];
        int bestStartNode = int.MinValue;
        long bestCost = long.MaxValue;
        for (int i = 0; i < startCluster.BoundaryNodes.Length; i++)
        {
            int node = startCluster.BoundaryNodes[i];
            if (!startConnector.TryGetCost(node, out long prefixCost)
                || !policy.SearchState.ContainsSettled(node)
                || !policy.SearchState.TryGetCost(node, out long suffixCost))
            {
                continue;
            }
            long total = AddDeterministicPortalCosts(prefixCost, suffixCost);
            if (total >= bestCost)
                continue;
            bestCost = total;
            bestStartNode = node;
        }
        if (bestStartNode == int.MinValue)
            return false;

        var overlayNodes = new List<int>(32) { bestStartNode };
        int cursor = bestStartNode;
        int guard = 0;
        while (policy.SearchState.TryGetPrevious(cursor, out int nextNode))
        {
            overlayNodes.Add(nextNode);
            cursor = nextNode;
            if (++guard > policy.SearchState.PreviousCount)
                throw new InvalidOperationException("Portal hierarchy reverse policy reconstruction contains a cycle.");
        }

        var l0Nodes = new List<int>(overlayNodes.Count * 4);
        AppendHierarchyConnectorWitness(_world, hierarchy, startConnector, overlayNodes[0], false, l0Nodes);
        for (int i = 0; i + 1 < overlayNodes.Count; i++)
        {
            int fromNode = overlayNodes[i];
            int toNode = overlayNodes[i + 1];
            DecodePortalNode(fromNode, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(toNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId
                && GetOppositeSectorId(GetPortalById(_world, fromPortalId), fromSectorId) == toSectorId)
            {
                AppendPortalNode(l0Nodes, toNode);
                continue;
            }
            PortalHierarchyCluster cluster = level.Clusters[ResolveHierarchyClusterId(_world, level, fromSectorId)];
            PortalHierarchyEdge edge = FindPortalHierarchyEdge(cluster, fromNode, toNode);
            AppendExpandedHierarchyWitness(_world, hierarchy, policy.LevelArrayIndex, edge.ChildWitnessNodes, l0Nodes);
        }
        AppendHierarchyConnectorWitness(_world, hierarchy, policy.GoalConnector, cursor, true, l0Nodes);

        if (!TryConvertPortalNodesToPath(l0Nodes, startSectorId, goalSectorId, out List<int> sectorIds, out List<int> portalIds))
        {
            throw new InvalidOperationException(
                $"Portal hierarchy reverse policy witness cannot be converted level={level.Level} startSector={startSectorId} goalSector={goalSectorId}.");
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyReconstruct,
                Stopwatch.GetTimestamp() - reconstructStartTicks);
        }
        handle = CreateAndCachePathHandle(sectorPathKey, sectorIds, portalIds, goalX, goalY);
        handle.BuildSource = $"portalHierarchyPolicyL{level.Level}";
        return true;
    }

    private static PortalGraphSearchResult BuildHierarchyStartConnector(
        NavigationWorld world,
        PortalHierarchyCluster cluster,
        int startSectorId,
        int startX,
        int startY)
    {
        bool profile = MainThreadFrameProfiler.LoggingEnabled;
        long accessStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        SectorData sector = world.Sectors[startSectorId];
        var nodes = new List<int>(sector.PortalIds.Count);
        var costs = new List<long>(sector.PortalIds.Count);
        for (int i = 0; i < sector.PortalIds.Count; i++)
        {
            int portalId = sector.PortalIds[i];
            long cost = ResolveDeterministicPortalAccessCost(world, sector, startSectorId, portalId, startX, startY);
            if (cost == long.MaxValue)
                continue;
            nodes.Add(EncodePortalNode(startSectorId, portalId));
            costs.Add(cost);
        }
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyStartLeafAccess,
                Stopwatch.GetTimestamp() - accessStartTicks);
        }

        long searchStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PortalGraphSearchResult result = RunRestrictedPortalGraphDijkstra(
            world,
            cluster,
            nodes.ToArray(),
            costs.ToArray(),
            false,
            new HashSet<int>(cluster.BoundaryNodes));
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyStartLeafSearch,
                Stopwatch.GetTimestamp() - searchStartTicks);
        }
        return result;
    }

    private static PortalGraphSearchResult BuildHierarchyGoalConnector(
        NavigationWorld world,
        PortalHierarchyCluster cluster,
        int goalSectorId,
        int goalX,
        int goalY)
    {
        SectorData sector = world.Sectors[goalSectorId];
        var nodes = new List<int>(sector.PortalIds.Count);
        var costs = new List<long>(sector.PortalIds.Count);
        for (int i = 0; i < sector.PortalIds.Count; i++)
        {
            int portalId = sector.PortalIds[i];
            long cost = ResolveDeterministicGoalSectorPortalAccessCost(sector, goalSectorId, portalId, goalX, goalY);
            if (cost == long.MaxValue)
                continue;
            nodes.Add(EncodePortalNode(goalSectorId, portalId));
            costs.Add(cost);
        }
        return RunRestrictedPortalGraphDijkstra(world, cluster, nodes.ToArray(), costs.ToArray(), true, new HashSet<int>(cluster.BoundaryNodes));
    }

    private static PortalHierarchyConnector BuildHierarchyConnector(
        NavigationWorld world,
        PortalHierarchy hierarchy,
        int targetLevelIndex,
        int endpointSectorId,
        int endpointX,
        int endpointY,
        bool reverse)
    {
        if (targetLevelIndex < 0 || targetLevelIndex >= hierarchy.Levels.Length)
            throw new ArgumentOutOfRangeException(nameof(targetLevelIndex));

        HierarchyStartConnectorCacheKey startCacheKey = default;
        if (!reverse)
        {
            startCacheKey = new HierarchyStartConnectorCacheKey(
                world.Version,
                targetLevelIndex,
                endpointSectorId,
                endpointX,
                endpointY);
            if (HierarchyStartConnectorCache.TryGetValue(
                    startCacheKey,
                    out HierarchyStartConnectorCacheEntry cached))
            {
                if (cached?.Connector == null)
                    throw new InvalidOperationException("Hierarchy start connector cache contains a null connector.");
                cached.LastUsedFrame = GetFrameCount();
                _perf.HierarchyStartConnectorCacheHits++;
                return cached.Connector;
            }
            _perf.HierarchyStartConnectorCacheMisses++;
        }

        PortalHierarchyLevel targetLevel = hierarchy.Levels[targetLevelIndex];
        PortalHierarchyCluster targetCluster = targetLevel.Clusters[ResolveHierarchyClusterId(world, targetLevel, endpointSectorId)];
        if (targetLevelIndex == 0)
        {
            PortalGraphSearchResult leafSearch = reverse
                ? BuildHierarchyGoalConnector(world, targetCluster, endpointSectorId, endpointX, endpointY)
                : BuildHierarchyStartConnector(world, targetCluster, endpointSectorId, endpointX, endpointY);
            var leafConnector = new PortalHierarchyConnector
            {
                TargetLevelIndex = targetLevelIndex,
                Search = leafSearch
            };
            if (!reverse)
                CacheHierarchyStartConnector(startCacheKey, leafConnector);
            return leafConnector;
        }

        PortalHierarchyConnector child = BuildHierarchyConnector(
            world,
            hierarchy,
            targetLevelIndex - 1,
            endpointSectorId,
            endpointX,
            endpointY,
            reverse);
        PortalHierarchyConnector connector = ExtendHierarchyConnector(
            world,
            hierarchy,
            targetLevelIndex,
            endpointSectorId,
            reverse,
            child);
        if (!reverse)
            CacheHierarchyStartConnector(startCacheKey, connector);
        return connector;
    }

    private static void CacheHierarchyStartConnector(
        HierarchyStartConnectorCacheKey key,
        PortalHierarchyConnector connector)
    {
        if (connector == null)
            throw new ArgumentNullException(nameof(connector));
        HierarchyStartConnectorCache[key] = new HierarchyStartConnectorCacheEntry
        {
            Connector = connector,
            LastUsedFrame = GetFrameCount()
        };
        TrimHierarchyStartConnectorCache();
    }

    private static void TrimHierarchyStartConnectorCache()
    {
        int limit = Math.Max(64, Config.FlowTileCacheLimit * 2);
        if (HierarchyStartConnectorCache.Count <= limit)
            return;

        HierarchyStartConnectorCacheKey oldestKey = default;
        int oldestFrame = int.MaxValue;
        bool found = false;
        foreach (KeyValuePair<HierarchyStartConnectorCacheKey, HierarchyStartConnectorCacheEntry> pair in HierarchyStartConnectorCache)
        {
            int frame = pair.Value?.LastUsedFrame ?? int.MinValue;
            int order = CompareHierarchyStartConnectorCacheKeys(pair.Key, oldestKey);
            if (found && (frame > oldestFrame || (frame == oldestFrame && order >= 0)))
                continue;
            oldestKey = pair.Key;
            oldestFrame = frame;
            found = true;
        }
        if (!found || !HierarchyStartConnectorCache.Remove(oldestKey))
            throw new InvalidOperationException("Hierarchy start connector cache trim could not remove an entry.");
    }

    private static int CompareHierarchyStartConnectorCacheKeys(
        HierarchyStartConnectorCacheKey left,
        HierarchyStartConnectorCacheKey right)
    {
        int order = left.WorldVersion.CompareTo(right.WorldVersion);
        if (order != 0)
            return order;
        order = left.TargetLevelIndex.CompareTo(right.TargetLevelIndex);
        if (order != 0)
            return order;
        order = left.SectorId.CompareTo(right.SectorId);
        if (order != 0)
            return order;
        order = left.CellX.CompareTo(right.CellX);
        return order != 0 ? order : left.CellY.CompareTo(right.CellY);
    }

    private static void ClearHierarchyStartConnectorCache()
    {
        HierarchyStartConnectorCache.Clear();
    }

    private static PortalHierarchyConnector ExtendHierarchyConnector(
        NavigationWorld world,
        PortalHierarchy hierarchy,
        int targetLevelIndex,
        int endpointSectorId,
        bool reverse,
        PortalHierarchyConnector child)
    {
        if (child == null || child.TargetLevelIndex != targetLevelIndex - 1)
            throw new InvalidOperationException($"ExtendHierarchyConnector failed: invalid child for level={targetLevelIndex + 1}.");
        PortalHierarchyLevel targetLevel = hierarchy.Levels[targetLevelIndex];
        PortalHierarchyCluster targetCluster = targetLevel.Clusters[ResolveHierarchyClusterId(world, targetLevel, endpointSectorId)];
        PortalHierarchyLevel lowerLevel = hierarchy.Levels[targetLevelIndex - 1];
        PortalHierarchyCluster endpointChildCluster = lowerLevel.Clusters[
            ResolveHierarchyClusterId(world, lowerLevel, endpointSectorId)];
        var sourceNodes = new List<int>(endpointChildCluster.BoundaryNodes.Length);
        var sourceCosts = new List<long>(endpointChildCluster.BoundaryNodes.Length);
        for (int i = 0; i < endpointChildCluster.BoundaryNodes.Length; i++)
        {
            int node = endpointChildCluster.BoundaryNodes[i];
            if (!child.TryGetCost(node, out long cost))
                continue;
            if (!child.ContainsSettled(node))
                throw new InvalidOperationException(
                    $"BuildHierarchyConnector failed: child connector exposed an unsettled boundary cost level={lowerLevel.Level} node={node}.");
            sourceNodes.Add(node);
            sourceCosts.Add(cost);
        }

        bool profile = !reverse && MainThreadFrameProfiler.LoggingEnabled;
        long extendStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
        PortalGraphSearchResult search = RunRestrictedPortalHierarchyDijkstra(
            world,
            lowerLevel,
            targetCluster,
            sourceNodes.ToArray(),
            sourceCosts.ToArray(),
            reverse,
            new HashSet<int>(targetCluster.BoundaryNodes));
        if (profile)
        {
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.FlowCorridorPolicyStartExtend,
                Stopwatch.GetTimestamp() - extendStartTicks);
        }
        return new PortalHierarchyConnector
        {
            TargetLevelIndex = targetLevelIndex,
            Search = search,
            Child = child
        };
    }

#if UNITY_EDITOR
    private static void RecordEditorHierarchyStartConnectorDiagnostic(
        PortalHierarchy hierarchy,
        int levelArrayIndex,
        int sectorId,
        int cellX,
        int cellY,
        int goalSectorId,
        int goalX,
        int goalY)
    {
        int cluster0 = levelArrayIndex >= 0
            ? ResolveHierarchyClusterId(_world, hierarchy.Levels[0], sectorId)
            : -1;
        int cluster1 = levelArrayIndex >= 1
            ? ResolveHierarchyClusterId(_world, hierarchy.Levels[1], sectorId)
            : -1;
        int cluster2 = levelArrayIndex >= 2
            ? ResolveHierarchyClusterId(_world, hierarchy.Levels[2], sectorId)
            : -1;
        int renderFrame = UnityEngine.Time.frameCount;
        if (!EditorHierarchyStartConnectorDiagnosticsByRenderFrame.TryGetValue(
                renderFrame,
                out List<HierarchyStartConnectorDiagnostic> diagnostics))
        {
            diagnostics = new List<HierarchyStartConnectorDiagnostic>(16);
            EditorHierarchyStartConnectorDiagnosticsByRenderFrame.Add(renderFrame, diagnostics);
        }
        diagnostics.Add(
            new HierarchyStartConnectorDiagnostic(
                levelArrayIndex + 1,
                sectorId,
                cellX,
                cellY,
                _world.AgentTypeId,
                goalSectorId,
                goalX,
                goalY,
                cluster0,
                cluster1,
                cluster2));
    }

    private static void ResetEditorHierarchyStartConnectorDiagnostics()
    {
        EditorHierarchyStartConnectorDiagnosticsByRenderFrame.Clear();
    }

    public static string GetEditorTestHierarchyStartConnectorDiagnostics(int renderFrame)
    {
        if (!EditorHierarchyStartConnectorDiagnosticsByRenderFrame.TryGetValue(
                renderFrame,
                out List<HierarchyStartConnectorDiagnostic> diagnostics))
        {
            TrimEditorHierarchyStartConnectorDiagnostics(renderFrame);
            return "none";
        }

        var parts = new string[diagnostics.Count];
        for (int i = 0; i < diagnostics.Count; i++)
        {
            HierarchyStartConnectorDiagnostic item = diagnostics[i];
            parts[i] = $"t{item.AgentTypeId}:L{item.Level}:s{item.SectorId}@({item.CellX},{item.CellY})->g{item.GoalSectorId}@({item.GoalX},{item.GoalY})/c({item.Cluster0},{item.Cluster1},{item.Cluster2})";
        }
        TrimEditorHierarchyStartConnectorDiagnostics(renderFrame);
        return string.Join(";", parts);
    }

    private static void TrimEditorHierarchyStartConnectorDiagnostics(int completedRenderFrame)
    {
        var expired = new List<int>();
        foreach (int renderFrame in EditorHierarchyStartConnectorDiagnosticsByRenderFrame.Keys)
        {
            if (renderFrame <= completedRenderFrame)
                expired.Add(renderFrame);
        }
        for (int i = 0; i < expired.Count; i++)
            EditorHierarchyStartConnectorDiagnosticsByRenderFrame.Remove(expired[i]);
    }
#endif

    private static int CountPortalHierarchyConnectorExpansions(PortalHierarchyConnector connector)
    {
        int count = 0;
        for (PortalHierarchyConnector cursor = connector; cursor != null; cursor = cursor.Child)
            count = checked(count + cursor.ExpansionCount);
        return count;
    }

    private static void AppendHierarchyConnectorWitness(
        NavigationWorld world,
        PortalHierarchy hierarchy,
        PortalHierarchyConnector connector,
        int boundaryNode,
        bool reverse,
        List<int> l0Nodes)
    {
        connector.RequireSingleAuthority();
        int[] levelWitness;
        if (connector.SearchState == null)
        {
            levelWitness = reverse
                ? ReconstructReversePortalWitness(connector.Search.PreviousNode, boundaryNode)
                : ReconstructForwardPortalWitness(connector.Search.PreviousNode, boundaryNode);
        }
        else
        {
            var nodes = new List<int>(32) { boundaryNode };
            int cursor = boundaryNode;
            int guard = 0;
            while (connector.TryGetPrevious(cursor, out int previous))
            {
                nodes.Add(previous);
                cursor = previous;
                if (++guard > connector.PreviousCount)
                    throw new InvalidOperationException("Portal hierarchy connector contains a cycle.");
            }
            if (!reverse)
                nodes.Reverse();
            levelWitness = nodes.ToArray();
        }
        if (connector.Child == null)
        {
            AppendPortalWitness(l0Nodes, levelWitness);
            return;
        }

        int childJoinNode = reverse ? levelWitness[levelWitness.Length - 1] : levelWitness[0];
        if (!reverse)
            AppendHierarchyConnectorWitness(world, hierarchy, connector.Child, childJoinNode, false, l0Nodes);
        AppendExpandedHierarchyWitness(world, hierarchy, connector.TargetLevelIndex, levelWitness, l0Nodes);
        if (reverse)
            AppendHierarchyConnectorWitness(world, hierarchy, connector.Child, childJoinNode, true, l0Nodes);
    }

    private static void AppendExpandedHierarchyWitness(
        NavigationWorld world,
        PortalHierarchy hierarchy,
        int edgeLevelIndex,
        int[] witnessNodes,
        List<int> l0Nodes)
    {
        if (witnessNodes == null || witnessNodes.Length == 0)
            throw new InvalidOperationException("AppendExpandedHierarchyWitness failed: witness is empty.");
        if (edgeLevelIndex <= 0)
        {
            AppendPortalWitness(l0Nodes, witnessNodes);
            return;
        }

        PortalHierarchyLevel childLevel = hierarchy.Levels[edgeLevelIndex - 1];
        AppendPortalNode(l0Nodes, witnessNodes[0]);
        for (int i = 0; i + 1 < witnessNodes.Length; i++)
        {
            int fromNode = witnessNodes[i];
            int toNode = witnessNodes[i + 1];
            DecodePortalNode(fromNode, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(toNode, out int toSectorId, out int toPortalId);
            if (fromPortalId == toPortalId
                && GetOppositeSectorId(GetPortalById(world, fromPortalId), fromSectorId) == toSectorId)
            {
                AppendPortalNode(l0Nodes, toNode);
                continue;
            }

            PortalHierarchyCluster cluster = childLevel.Clusters[ResolveHierarchyClusterId(world, childLevel, fromSectorId)];
            PortalHierarchyEdge edge = FindPortalHierarchyEdge(cluster, fromNode, toNode);
            AppendExpandedHierarchyWitness(world, hierarchy, edgeLevelIndex - 1, edge.ChildWitnessNodes, l0Nodes);
        }
    }

    private static void RelaxHierarchyQueryNode(
        Dictionary<int, long> costs,
        Dictionary<int, int> previous,
        HashSet<int> settled,
        DeterministicCostHeap open,
        int currentNode,
        int nextNode,
        long nextCost)
    {
        if (settled.Contains(nextNode))
        {
            if (costs.TryGetValue(nextNode, out long settledCost) && nextCost < settledCost)
                throw new InvalidOperationException($"Hierarchy Dijkstra found a cheaper path to settled node={nextNode}, previous={settledCost}, next={nextCost}.");
            return;
        }
        if (costs.TryGetValue(nextNode, out long existing) && nextCost >= existing)
            return;
        costs[nextNode] = nextCost;
        previous[nextNode] = currentNode;
        open.Push(nextNode, nextCost);
    }

    private static PortalHierarchyEdge FindPortalHierarchyEdge(PortalHierarchyCluster cluster, int fromNode, int toNode)
    {
        if (!cluster.OutgoingEdgesByNode.TryGetValue(fromNode, out List<PortalHierarchyEdge> outgoing))
            throw new InvalidOperationException($"FindPortalHierarchyEdge failed: source is missing cluster={cluster.ClusterId} node={fromNode}.");
        for (int i = 0; i < outgoing.Count; i++)
        {
            if (outgoing[i].ToNode == toNode)
                return outgoing[i];
        }
        throw new InvalidOperationException($"FindPortalHierarchyEdge failed: edge is missing cluster={cluster.ClusterId} from={fromNode} to={toNode}.");
    }

    private static int[] ReconstructForwardPortalWitness(Dictionary<int, int> previous, int targetNode)
    {
        var reversed = new List<int>(32) { targetNode };
        int cursor = targetNode;
        int guard = 0;
        while (previous.TryGetValue(cursor, out int predecessor))
        {
            cursor = predecessor;
            reversed.Add(cursor);
            guard++;
            if (guard > previous.Count)
                throw new InvalidOperationException("ReconstructForwardPortalWitness failed: predecessor cycle.");
        }
        reversed.Reverse();
        return reversed.ToArray();
    }

    private static int[] ReconstructReversePortalWitness(Dictionary<int, int> nextTowardGoal, int startNode)
    {
        var nodes = new List<int>(32) { startNode };
        int cursor = startNode;
        int guard = 0;
        while (nextTowardGoal.TryGetValue(cursor, out int next))
        {
            cursor = next;
            nodes.Add(cursor);
            guard++;
            if (guard > nextTowardGoal.Count)
                throw new InvalidOperationException("ReconstructReversePortalWitness failed: successor cycle.");
        }
        return nodes.ToArray();
    }

    private static int[] ReconstructReversePortalWitness(
        PortalHierarchyDownwardCustomization customization,
        int startNode)
    {
        if (customization == null)
            throw new ArgumentNullException(nameof(customization));
        customization.RequireSingleAuthority();
        var nodes = new List<int>(32) { startNode };
        int cursor = startNode;
        int guard = 0;
        while (customization.TryGetPrevious(cursor, out int next))
        {
            nodes.Add(next);
            cursor = next;
            if (++guard > customization.PreviousCount)
                throw new InvalidOperationException("Portal hierarchy downward customization contains a cycle.");
        }
        return nodes.ToArray();
    }

    private static void AppendPortalWitness(List<int> target, int[] witness)
    {
        if (witness == null || witness.Length == 0)
            throw new InvalidOperationException("AppendPortalWitness failed: witness is empty.");
        for (int i = 0; i < witness.Length; i++)
            AppendPortalNode(target, witness[i]);
    }

    private static void AppendPortalNode(List<int> target, int node)
    {
        if (target.Count == 0 || target[target.Count - 1] != node)
            target.Add(node);
    }

    private static bool HierarchyClusterContainsSector(NavigationWorld world, PortalHierarchyCluster cluster, int sectorId)
    {
        int sectorX = sectorId % world.SectorCountX;
        int sectorY = sectorId / world.SectorCountX;
        return sectorX >= cluster.StartSectorX
               && sectorX < cluster.StartSectorX + cluster.WidthSectors
               && sectorY >= cluster.StartSectorY
               && sectorY < cluster.StartSectorY + cluster.HeightSectors;
    }

    private static int ResolveHierarchyClusterId(NavigationWorld world, PortalHierarchyLevel level, int sectorId)
    {
        int sectorX = sectorId % world.SectorCountX;
        int sectorY = sectorId / world.SectorCountX;
        return sectorY / level.ClusterSpanSectors * level.ClusterCountX + sectorX / level.ClusterSpanSectors;
    }

    private static int CeilDivide(int value, int divisor)
    {
        return checked((value + divisor - 1) / divisor);
    }

    private static int ComparePortalHierarchyEdges(PortalHierarchyEdge left, PortalHierarchyEdge right)
    {
        int compare = left.FromNode.CompareTo(right.FromNode);
        return compare != 0 ? compare : left.ToNode.CompareTo(right.ToNode);
    }

    private static FlowNavigationGridAsset.PortalHierarchyDerivedData ExportPortalHierarchy(PortalHierarchy hierarchy)
    {
        if (hierarchy?.Levels == null)
            throw new InvalidOperationException("ExportPortalHierarchy failed: hierarchy is missing.");
        var data = new FlowNavigationGridAsset.PortalHierarchyDerivedData
        {
            Fanout = hierarchy.Fanout,
            Levels = new FlowNavigationGridAsset.PortalHierarchyLevelDerivedData[hierarchy.Levels.Length]
        };
        for (int levelIndex = 0; levelIndex < hierarchy.Levels.Length; levelIndex++)
        {
            PortalHierarchyLevel level = hierarchy.Levels[levelIndex]
                ?? throw new InvalidOperationException($"ExportPortalHierarchy failed: level is null index={levelIndex}.");
            var levelData = new FlowNavigationGridAsset.PortalHierarchyLevelDerivedData
            {
                Level = level.Level,
                ClusterSpanSectors = level.ClusterSpanSectors,
                ClusterCountX = level.ClusterCountX,
                ClusterCountY = level.ClusterCountY,
                Clusters = new FlowNavigationGridAsset.PortalHierarchyClusterDerivedData[level.Clusters.Length]
            };
            for (int clusterIndex = 0; clusterIndex < level.Clusters.Length; clusterIndex++)
            {
                PortalHierarchyCluster cluster = level.Clusters[clusterIndex]
                    ?? throw new InvalidOperationException($"ExportPortalHierarchy failed: cluster is null level={levelIndex} index={clusterIndex}.");
                var clusterData = new FlowNavigationGridAsset.PortalHierarchyClusterDerivedData
                {
                    ClusterId = cluster.ClusterId,
                    StartSectorX = cluster.StartSectorX,
                    StartSectorY = cluster.StartSectorY,
                    WidthSectors = cluster.WidthSectors,
                    HeightSectors = cluster.HeightSectors,
                    BoundaryNodes = (int[])cluster.BoundaryNodes.Clone(),
                    Edges = new FlowNavigationGridAsset.PortalHierarchyEdgeDerivedData[cluster.Edges.Length]
                };
                for (int edgeIndex = 0; edgeIndex < cluster.Edges.Length; edgeIndex++)
                {
                    PortalHierarchyEdge edge = cluster.Edges[edgeIndex];
                    clusterData.Edges[edgeIndex] = new FlowNavigationGridAsset.PortalHierarchyEdgeDerivedData
                    {
                        FromNode = edge.FromNode,
                        ToNode = edge.ToNode,
                        DeterministicCost = edge.DeterministicCost,
                        ChildWitnessNodes = (int[])edge.ChildWitnessNodes.Clone()
                    };
                }
                levelData.Clusters[clusterIndex] = clusterData;
            }
            data.Levels[levelIndex] = levelData;
        }
        return data;
    }

    private static PortalHierarchy ImportPortalHierarchy(
        NavigationWorld world,
        FlowNavigationGridAsset.PortalHierarchyDerivedData data)
    {
        if (data == null || data.Fanout < 2 || data.Levels == null)
            throw new InvalidOperationException("ImportPortalHierarchy failed: hierarchy data is invalid.");
        var hierarchy = new PortalHierarchy
        {
            Fanout = data.Fanout,
            Levels = new PortalHierarchyLevel[data.Levels.Length]
        };
        for (int levelIndex = 0; levelIndex < data.Levels.Length; levelIndex++)
        {
            FlowNavigationGridAsset.PortalHierarchyLevelDerivedData sourceLevel = data.Levels[levelIndex]
                ?? throw new InvalidOperationException($"ImportPortalHierarchy failed: level is null index={levelIndex}.");
            if (sourceLevel.Level != levelIndex + 1
                || sourceLevel.ClusterSpanSectors <= 0
                || sourceLevel.ClusterCountX <= 0
                || sourceLevel.ClusterCountY <= 0
                || sourceLevel.Clusters == null
                || sourceLevel.Clusters.Length != sourceLevel.ClusterCountX * sourceLevel.ClusterCountY)
            {
                throw new InvalidOperationException($"ImportPortalHierarchy failed: level metadata is invalid index={levelIndex}.");
            }
            var level = new PortalHierarchyLevel
            {
                Level = sourceLevel.Level,
                ClusterSpanSectors = sourceLevel.ClusterSpanSectors,
                ClusterCountX = sourceLevel.ClusterCountX,
                ClusterCountY = sourceLevel.ClusterCountY,
                Clusters = new PortalHierarchyCluster[sourceLevel.Clusters.Length]
            };
            for (int clusterIndex = 0; clusterIndex < sourceLevel.Clusters.Length; clusterIndex++)
            {
                FlowNavigationGridAsset.PortalHierarchyClusterDerivedData sourceCluster = sourceLevel.Clusters[clusterIndex]
                    ?? throw new InvalidOperationException($"ImportPortalHierarchy failed: cluster is null level={levelIndex} index={clusterIndex}.");
                if (sourceCluster.ClusterId != clusterIndex
                    || sourceCluster.WidthSectors <= 0
                    || sourceCluster.HeightSectors <= 0
                    || sourceCluster.BoundaryNodes == null
                    || sourceCluster.Edges == null)
                {
                    throw new InvalidOperationException($"ImportPortalHierarchy failed: cluster metadata is invalid level={levelIndex} index={clusterIndex}.");
                }
                var cluster = new PortalHierarchyCluster
                {
                    ClusterId = sourceCluster.ClusterId,
                    StartSectorX = sourceCluster.StartSectorX,
                    StartSectorY = sourceCluster.StartSectorY,
                    WidthSectors = sourceCluster.WidthSectors,
                    HeightSectors = sourceCluster.HeightSectors,
                    BoundaryNodes = (int[])sourceCluster.BoundaryNodes.Clone(),
                    Edges = new PortalHierarchyEdge[sourceCluster.Edges.Length]
                };
                for (int edgeIndex = 0; edgeIndex < sourceCluster.Edges.Length; edgeIndex++)
                {
                    FlowNavigationGridAsset.PortalHierarchyEdgeDerivedData sourceEdge = sourceCluster.Edges[edgeIndex]
                        ?? throw new InvalidOperationException($"ImportPortalHierarchy failed: edge is null level={levelIndex} cluster={clusterIndex} index={edgeIndex}.");
                    if (sourceEdge.DeterministicCost < 0
                        || sourceEdge.DeterministicCost == long.MaxValue
                        || sourceEdge.ChildWitnessNodes == null
                        || sourceEdge.ChildWitnessNodes.Length < 2)
                    {
                        throw new InvalidOperationException($"ImportPortalHierarchy failed: edge payload is invalid level={levelIndex} cluster={clusterIndex} index={edgeIndex}.");
                    }
                    cluster.Edges[edgeIndex] = new PortalHierarchyEdge
                    {
                        FromNode = sourceEdge.FromNode,
                        ToNode = sourceEdge.ToNode,
                        DeterministicCost = sourceEdge.DeterministicCost,
                        ChildWitnessNodes = (int[])sourceEdge.ChildWitnessNodes.Clone()
                    };
                }
                cluster.OutgoingEdgesByNode = BuildPortalHierarchyOutgoingIndex(cluster);
                cluster.IncomingEdgesByNode = BuildPortalHierarchyIncomingIndex(cluster);
                level.Clusters[clusterIndex] = cluster;
            }
            hierarchy.Levels[levelIndex] = level;
        }
        ValidatePortalHierarchy(world, hierarchy);
        return hierarchy;
    }

    private static void ValidatePortalHierarchy(NavigationWorld world, PortalHierarchy hierarchy)
    {
        if (hierarchy?.Levels == null)
            throw new InvalidOperationException("ValidatePortalHierarchy failed: hierarchy is missing.");
        for (int levelIndex = 0; levelIndex < hierarchy.Levels.Length; levelIndex++)
        {
            PortalHierarchyLevel level = hierarchy.Levels[levelIndex];
            for (int clusterIndex = 0; clusterIndex < level.Clusters.Length; clusterIndex++)
            {
                PortalHierarchyCluster cluster = level.Clusters[clusterIndex];
                for (int nodeIndex = 0; nodeIndex < cluster.BoundaryNodes.Length; nodeIndex++)
                {
                    int node = cluster.BoundaryNodes[nodeIndex];
                    DecodePortalNode(node, out int sectorId, out int portalId);
                    if (!HierarchyClusterContainsSector(world, cluster, sectorId)
                        || !TryGetPortalById(world, portalId, out PortalData portal)
                        || ResolveHierarchyClusterId(world, level, GetOppositeSectorId(portal, sectorId)) == cluster.ClusterId)
                    {
                        throw new InvalidOperationException($"ValidatePortalHierarchy failed: invalid boundary node level={level.Level} cluster={cluster.ClusterId} node={node}.");
                    }
                }
                for (int edgeIndex = 0; edgeIndex < cluster.Edges.Length; edgeIndex++)
                {
                    PortalHierarchyEdge edge = cluster.Edges[edgeIndex];
                    if (Array.BinarySearch(cluster.BoundaryNodes, edge.FromNode) < 0
                        || Array.BinarySearch(cluster.BoundaryNodes, edge.ToNode) < 0
                        || edge.ChildWitnessNodes[0] != edge.FromNode
                        || edge.ChildWitnessNodes[edge.ChildWitnessNodes.Length - 1] != edge.ToNode)
                    {
                        throw new InvalidOperationException($"ValidatePortalHierarchy failed: invalid edge level={level.Level} cluster={cluster.ClusterId} edge={edgeIndex}.");
                    }
                }
            }
        }
    }

    private static void ValidatePortalHierarchyEdgeCosts(NavigationWorld world, PortalHierarchy hierarchy)
    {
        if (world == null || hierarchy?.Levels == null)
            throw new InvalidOperationException("ValidatePortalHierarchyEdgeCosts failed: world or hierarchy is missing.");
        for (int levelIndex = 0; levelIndex < hierarchy.Levels.Length; levelIndex++)
        {
            PortalHierarchyLevel level = hierarchy.Levels[levelIndex];
            for (int clusterIndex = 0; clusterIndex < level.Clusters.Length; clusterIndex++)
            {
                PortalHierarchyCluster cluster = level.Clusters[clusterIndex];
                for (int edgeIndex = 0; edgeIndex < cluster.Edges.Length; edgeIndex++)
                {
                    PortalHierarchyEdge edge = cluster.Edges[edgeIndex];
                    long witnessCost = CalculateHierarchyEdgeWitnessCost(
                        world,
                        hierarchy,
                        levelIndex,
                        edge.ChildWitnessNodes);
                    if (witnessCost == edge.DeterministicCost)
                        continue;
                    throw new InvalidOperationException(
                        $"ValidatePortalHierarchyEdgeCosts failed: edge cost differs from child witness " +
                        $"level={level.Level} cluster={cluster.ClusterId} edge={edgeIndex} " +
                        $"from={edge.FromNode} to={edge.ToNode} stored={edge.DeterministicCost} witness={witnessCost}.");
                }
            }
        }
    }

    private static void EnsureFlowPathKernelWitnessIndex(PortalHierarchy hierarchy)
    {
        if (hierarchy?.Levels == null)
            throw new InvalidOperationException("Flow path kernel witness index requires a committed hierarchy.");
        if (hierarchy.WitnessIndex != null)
        {
            if (!hierarchy.WitnessIndex.IsCreated)
                throw new InvalidOperationException("Flow path kernel witness index was disposed while still owned by a hierarchy.");
            return;
        }

        var records = new List<FlowPathKernelWitnessEdge>();
        var witnessNodes = new List<int>();
        for (int levelIndex = 0; levelIndex < hierarchy.Levels.Length; levelIndex++)
        {
            PortalHierarchyLevel level = hierarchy.Levels[levelIndex]
                ?? throw new InvalidOperationException("Flow path kernel witness index encountered a null hierarchy level.");
            for (int clusterIndex = 0; clusterIndex < level.Clusters.Length; clusterIndex++)
            {
                PortalHierarchyCluster cluster = level.Clusters[clusterIndex]
                    ?? throw new InvalidOperationException("Flow path kernel witness index encountered a null cluster.");
                for (int edgeIndex = 0; edgeIndex < cluster.Edges.Length; edgeIndex++)
                {
                    PortalHierarchyEdge edge = cluster.Edges[edgeIndex]
                        ?? throw new InvalidOperationException("Flow path kernel witness index encountered a null edge.");
                    if (edge.ChildWitnessNodes == null || edge.ChildWitnessNodes.Length == 0)
                        throw new InvalidOperationException("Flow path kernel witness index encountered an empty witness.");
                    int offset = witnessNodes.Count;
                    witnessNodes.AddRange(edge.ChildWitnessNodes);
                    records.Add(new FlowPathKernelWitnessEdge(
                        levelIndex,
                        cluster.ClusterId,
                        edge.FromNode,
                        edge.ToNode,
                        offset,
                        edge.ChildWitnessNodes.Length));
                }
            }
        }
        records.Sort(CompareFlowPathKernelWitnessEdges);
        for (int i = 1; i < records.Count; i++)
        {
            if (CompareFlowPathKernelWitnessEdges(records[i - 1], records[i]) == 0)
                throw new InvalidOperationException("Flow path kernel witness index contains a duplicate edge key.");
        }
        hierarchy.WitnessIndex = new FlowPathKernelWitnessIndex(records.ToArray(), witnessNodes.ToArray());
    }

    private static void EnsureFlowPathKernelSearchGraphIndexes(NavigationWorld world)
    {
        if (world == null || world.Sectors == null || world.Portals == null || world.Hierarchy?.Levels == null)
            throw new InvalidOperationException("Flow path kernel graph indexes require a committed world and hierarchy.");
        if (world.L0SearchGraphIndex != null || world.Hierarchy.SearchGraphIndexes != null)
            throw new InvalidOperationException("Flow path kernel graph indexes were initialized more than once.");

        world.L0SearchGraphIndex = BuildFlowPathKernelL0SearchGraphIndex(world);

        PortalHierarchyLevel[] levels = world.Hierarchy.Levels;
        world.Hierarchy.SearchGraphIndexes = new FlowPathKernelGraphIndex[levels.Length];
        for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            PortalHierarchyLevel level = levels[levelIndex];
            var edges = new List<FlowPathKernelGraphEdge>();
            for (int clusterIndex = 0; clusterIndex < level.Clusters.Length; clusterIndex++)
            {
                PortalHierarchyCluster cluster = level.Clusters[clusterIndex]
                    ?? throw new InvalidOperationException(
                        $"Flow path hierarchy graph is missing level={levelIndex + 1} cluster={clusterIndex}.");
                for (int edgeIndex = 0; edgeIndex < cluster.Edges.Length; edgeIndex++)
                {
                    PortalHierarchyEdge edge = cluster.Edges[edgeIndex];
                    edges.Add(new FlowPathKernelGraphEdge(edge.FromNode, edge.ToNode, edge.DeterministicCost));
                }
            }
            AddPortalCrossingGraphEdges(world, edges, level);
            world.Hierarchy.SearchGraphIndexes[levelIndex] = new FlowPathKernelGraphIndex(edges.ToArray());
        }
    }

    private static FlowPathKernelGraphIndex BuildFlowPathKernelL0SearchGraphIndex(NavigationWorld world)
    {
        var l0Edges = new List<FlowPathKernelGraphEdge>();
        for (int sectorId = 0; sectorId < world.Sectors.Length; sectorId++)
        {
            SectorData sector = world.Sectors[sectorId]
                ?? throw new InvalidOperationException($"Flow path L0 graph is missing sector={sectorId}.");
            for (int edgeIndex = 0; edgeIndex < sector.PortalTransitions.Count; edgeIndex++)
            {
                PortalTransition edge = sector.PortalTransitions[edgeIndex];
                if (edge.DeterministicCost == long.MaxValue)
                    continue;
                l0Edges.Add(new FlowPathKernelGraphEdge(
                    EncodePortalNode(sectorId, edge.FromPortalId),
                    EncodePortalNode(sectorId, edge.ToPortalId),
                    edge.DeterministicCost));
            }
        }
        AddPortalCrossingGraphEdges(world, l0Edges, null);
        return new FlowPathKernelGraphIndex(l0Edges.ToArray());
    }

    private static void AddPortalCrossingGraphEdges(
        NavigationWorld world,
        List<FlowPathKernelGraphEdge> edges,
        PortalHierarchyLevel level)
    {
        for (int i = 0; i < world.Portals.Length; i++)
        {
            PortalData portal = world.Portals[i]
                ?? throw new InvalidOperationException($"Flow path graph is missing portal index={i}.");
            if (level != null
                && ResolveHierarchyClusterId(world, level, portal.SectorAId)
                == ResolveHierarchyClusterId(world, level, portal.SectorBId))
            {
                continue;
            }
            int nodeA = EncodePortalNode(portal.SectorAId, portal.PortalId);
            int nodeB = EncodePortalNode(portal.SectorBId, portal.PortalId);
            edges.Add(new FlowPathKernelGraphEdge(nodeA, nodeB, DeterministicPortalCrossingCost));
            edges.Add(new FlowPathKernelGraphEdge(nodeB, nodeA, DeterministicPortalCrossingCost));
        }
    }

    private static int CompareFlowPathKernelWitnessEdges(
        FlowPathKernelWitnessEdge left,
        FlowPathKernelWitnessEdge right)
    {
        int order = left.LevelIndex.CompareTo(right.LevelIndex);
        if (order != 0) return order;
        order = left.ClusterId.CompareTo(right.ClusterId);
        if (order != 0) return order;
        order = left.FromNode.CompareTo(right.FromNode);
        return order != 0 ? order : left.ToNode.CompareTo(right.ToNode);
    }

    private static void DisposeFlowPathKernelWitnessIndex(PortalHierarchy hierarchy)
    {
        if (hierarchy == null)
            return;
        if (hierarchy.WitnessIndex != null)
        {
            hierarchy.WitnessIndex.Dispose();
            hierarchy.WitnessIndex = null;
        }
        if (hierarchy.SearchGraphIndexes != null)
        {
            for (int i = 0; i < hierarchy.SearchGraphIndexes.Length; i++)
                hierarchy.SearchGraphIndexes[i]?.Dispose();
            hierarchy.SearchGraphIndexes = null;
        }
    }

    public static void BuildEditorTestPortalHierarchy()
    {
        if (_world == null)
            throw new InvalidOperationException("BuildEditorTestPortalHierarchy failed: world is unavailable.");
        PortalHierarchy previousHierarchy = _world.Hierarchy;
        PortalHierarchy replacementHierarchy = BuildPortalHierarchy(_world, DefaultHierarchyFanout);
        if (!ReferenceEquals(previousHierarchy, replacementHierarchy))
            DisposeFlowPathKernelWitnessIndex(previousHierarchy);
        _world.Hierarchy = replacementHierarchy;
    }

    public static int GetEditorTestPortalHierarchyLevelCount()
    {
        return _world?.Hierarchy?.Levels?.Length ?? 0;
    }

    public static bool TryCompareEditorTestPortalHierarchyWithFullL0(
        int startX,
        int startY,
        int goalX,
        int goalY,
        out long hierarchyCost,
        out long fullL0Cost,
        out int hierarchyExpansionCount,
        out int fullL0ExpansionCount,
        out int hierarchyLevel)
    {
        hierarchyCost = long.MaxValue;
        fullL0Cost = long.MaxValue;
        hierarchyExpansionCount = 0;
        fullL0ExpansionCount = 0;
        hierarchyLevel = 0;
        if (_world == null)
            throw new InvalidOperationException("TryCompareEditorTestPortalHierarchyWithFullL0 failed: world is unavailable.");
        if (!_world.TryGetSectorId(startX, startY, out int startSectorId)
            || !_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
            throw new ArgumentOutOfRangeException(nameof(startX), "Hierarchy comparison cells must be inside the world.");

        if (!TryBuildPortalHierarchyQuery(_world, startSectorId, goalSectorId, startX, startY, goalX, goalY, out PortalHierarchyQueryResult hierarchyResult))
            return false;
        if (!TryBuildFullL0DijkstraQuery(_world, startSectorId, goalSectorId, startX, startY, goalX, goalY, out PortalHierarchyQueryResult fullResult))
            return false;

        hierarchyCost = hierarchyResult.DeterministicCost;
        fullL0Cost = fullResult.DeterministicCost;
        hierarchyExpansionCount = hierarchyResult.ExpansionCount;
        fullL0ExpansionCount = fullResult.ExpansionCount;
        hierarchyLevel = hierarchyResult.Level;
        return true;
    }

    public static string GetEditorTestPortalHierarchyExactnessDiagnostics(
        int startX,
        int startY,
        int goalX,
        int goalY)
    {
        if (_world == null)
            throw new InvalidOperationException("Portal hierarchy exactness diagnostics require an active world.");
        if (!_world.TryGetSectorId(startX, startY, out int startSectorId)
            || !_world.TryGetSectorId(goalX, goalY, out int goalSectorId))
            throw new ArgumentOutOfRangeException(nameof(startX), "Hierarchy diagnostic cells must be inside the world.");
        if (!TryBuildPortalHierarchyQuery(_world, startSectorId, goalSectorId, startX, startY, goalX, goalY, out PortalHierarchyQueryResult hierarchyResult))
            return BuildPortalHierarchyUnreachableDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY);
        if (!TryBuildFullL0DijkstraQuery(_world, startSectorId, goalSectorId, startX, startY, goalX, goalY, out PortalHierarchyQueryResult fullResult))
            return "fullL0=unreachable";

        long hierarchyWitnessCost = CalculatePortalWitnessCost(
            _world,
            hierarchyResult.L0Nodes,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY);
        long fullWitnessCost = CalculatePortalWitnessCost(
            _world,
            fullResult.L0Nodes,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY);
        int prefix = 0;
        int sharedLength = Math.Min(hierarchyResult.L0Nodes.Length, fullResult.L0Nodes.Length);
        while (prefix < sharedLength && hierarchyResult.L0Nodes[prefix] == fullResult.L0Nodes[prefix])
            prefix++;
        int suffix = 0;
        while (suffix < sharedLength - prefix
               && hierarchyResult.L0Nodes[hierarchyResult.L0Nodes.Length - 1 - suffix]
               == fullResult.L0Nodes[fullResult.L0Nodes.Length - 1 - suffix])
        {
            suffix++;
        }

        return $"hierarchyReported={hierarchyResult.DeterministicCost}, hierarchyWitness={hierarchyWitnessCost}, " +
               $"fullReported={fullResult.DeterministicCost}, fullWitness={fullWitnessCost}, " +
               $"hierarchyNodes={hierarchyResult.L0Nodes.Length}, fullNodes={fullResult.L0Nodes.Length}, " +
               $"sharedPrefix={prefix}, sharedSuffix={suffix}, " +
               $"hierarchyWindow={FormatPortalWitnessWindow(hierarchyResult.L0Nodes, prefix)}, " +
               $"fullWindow={FormatPortalWitnessWindow(fullResult.L0Nodes, prefix)}, " +
               FindFirstPortalHierarchyEdgeCostMismatch(_world, _world.Hierarchy);
    }

    private static string BuildPortalHierarchyUnreachableDiagnostics(
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY)
    {
        bool fullL0Reachable = TryBuildFullL0DijkstraQuery(
            _world,
            startSectorId,
            goalSectorId,
            startX,
            startY,
            goalX,
            goalY,
            out PortalHierarchyQueryResult fullResult);
        PortalHierarchyLevel selectedLevel = null;
        PortalHierarchyLevel[] levels = _world.Hierarchy?.Levels;
        if (levels != null)
        {
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                if (ResolveHierarchyClusterId(_world, levels[i], startSectorId)
                    == ResolveHierarchyClusterId(_world, levels[i], goalSectorId))
                    continue;
                selectedLevel = levels[i];
                break;
            }
        }

        if (selectedLevel == null)
            return $"hierarchy=unreachable selectedLevel=none fullL0Reachable={fullL0Reachable}";

        int levelIndex = selectedLevel.Level - 1;
        int startClusterId = ResolveHierarchyClusterId(_world, selectedLevel, startSectorId);
        int goalClusterId = ResolveHierarchyClusterId(_world, selectedLevel, goalSectorId);
        PortalHierarchyCluster startCluster = selectedLevel.Clusters[startClusterId];
        PortalHierarchyCluster goalCluster = selectedLevel.Clusters[goalClusterId];
        PortalHierarchyConnector startConnector = BuildHierarchyConnector(
            _world, _world.Hierarchy, levelIndex, startSectorId, startX, startY, reverse: false);
        PortalHierarchyConnector goalConnector = BuildHierarchyConnector(
            _world, _world.Hierarchy, levelIndex, goalSectorId, goalX, goalY, reverse: true);
        int sharedSettledNodes = 0;
        foreach (int node in startConnector.Search.SettledNodes)
        {
            if (goalConnector.Search.SettledNodes.Contains(node))
                sharedSettledNodes++;
        }

        return $"hierarchy=unreachable level={selectedLevel.Level} startCluster={startClusterId} goalCluster={goalClusterId} " +
               $"startBoundary={startCluster.BoundaryNodes.Length} startCosts={startConnector.Search.Costs.Count} startSettled={startConnector.Search.SettledNodes.Count} " +
               $"goalBoundary={goalCluster.BoundaryNodes.Length} goalCosts={goalConnector.Search.Costs.Count} goalSettled={goalConnector.Search.SettledNodes.Count} " +
               $"sharedSettled={sharedSettledNodes} fullL0Reachable={fullL0Reachable} " +
               $"fullL0Cost={(fullL0Reachable ? fullResult.DeterministicCost : long.MaxValue)} fullL0Expansions={(fullL0Reachable ? fullResult.ExpansionCount : 0)}";
    }

    private static string FindFirstPortalHierarchyEdgeCostMismatch(
        NavigationWorld world,
        PortalHierarchy hierarchy)
    {
        for (int levelIndex = 0; levelIndex < hierarchy.Levels.Length; levelIndex++)
        {
            PortalHierarchyLevel level = hierarchy.Levels[levelIndex];
            for (int clusterIndex = 0; clusterIndex < level.Clusters.Length; clusterIndex++)
            {
                PortalHierarchyCluster cluster = level.Clusters[clusterIndex];
                for (int edgeIndex = 0; edgeIndex < cluster.Edges.Length; edgeIndex++)
                {
                    PortalHierarchyEdge edge = cluster.Edges[edgeIndex];
                    long witnessCost = CalculateHierarchyEdgeWitnessCost(
                        world,
                        hierarchy,
                        levelIndex,
                        edge.ChildWitnessNodes);
                    if (witnessCost != edge.DeterministicCost)
                    {
                        return $"edgeMismatch=level:{level.Level}/cluster:{cluster.ClusterId}/edge:{edgeIndex}/" +
                               $"from:{edge.FromNode}/to:{edge.ToNode}/stored:{edge.DeterministicCost}/witness:{witnessCost}/" +
                               $"window:{FormatPortalWitnessWindow(edge.ChildWitnessNodes, 0)}/" +
                               $"segments:{FormatHierarchyEdgeWitnessCosts(world, hierarchy, levelIndex, edge.ChildWitnessNodes)}";
                    }
                }
            }
        }
        return "edgeMismatch=none";
    }

    private static long CalculateHierarchyEdgeWitnessCost(
        NavigationWorld world,
        PortalHierarchy hierarchy,
        int levelIndex,
        int[] witnessNodes)
    {
        if (witnessNodes == null || witnessNodes.Length < 2)
            throw new InvalidOperationException("Hierarchy edge witness requires at least two nodes.");
        long cost = 0L;
        for (int i = 0; i + 1 < witnessNodes.Length; i++)
        {
            int fromNode = witnessNodes[i];
            int toNode = witnessNodes[i + 1];
            DecodePortalNode(fromNode, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(toNode, out int toSectorId, out int toPortalId);
            long edgeCost;
            if (fromPortalId == toPortalId
                && GetOppositeSectorId(GetPortalById(world, fromPortalId), fromSectorId) == toSectorId)
            {
                edgeCost = DeterministicPortalCrossingCost;
            }
            else if (levelIndex == 0)
            {
                if (fromSectorId != toSectorId)
                    throw new InvalidOperationException($"L0 hierarchy witness contains an invalid edge from={fromNode} to={toNode}.");
                edgeCost = ResolveDeterministicPortalTransitionCost(world, fromSectorId, fromPortalId, toPortalId);
            }
            else
            {
                PortalHierarchyLevel childLevel = hierarchy.Levels[levelIndex - 1];
                PortalHierarchyCluster childCluster = childLevel.Clusters[
                    ResolveHierarchyClusterId(world, childLevel, fromSectorId)];
                edgeCost = FindPortalHierarchyEdge(childCluster, fromNode, toNode).DeterministicCost;
            }
            cost = AddDeterministicPortalCosts(cost, edgeCost);
        }
        return cost;
    }

    private static string FormatHierarchyEdgeWitnessCosts(
        NavigationWorld world,
        PortalHierarchy hierarchy,
        int levelIndex,
        int[] witnessNodes)
    {
        var parts = new List<string>(witnessNodes.Length - 1);
        for (int i = 0; i + 1 < witnessNodes.Length; i++)
        {
            int fromNode = witnessNodes[i];
            int toNode = witnessNodes[i + 1];
            DecodePortalNode(fromNode, out int fromSectorId, out int fromPortalId);
            DecodePortalNode(toNode, out int toSectorId, out int toPortalId);
            long edgeCost;
            if (fromPortalId == toPortalId
                && GetOppositeSectorId(GetPortalById(world, fromPortalId), fromSectorId) == toSectorId)
            {
                edgeCost = DeterministicPortalCrossingCost;
            }
            else if (levelIndex == 0)
            {
                edgeCost = ResolveDeterministicPortalTransitionCost(world, fromSectorId, fromPortalId, toPortalId);
            }
            else
            {
                PortalHierarchyLevel childLevel = hierarchy.Levels[levelIndex - 1];
                PortalHierarchyCluster childCluster = childLevel.Clusters[
                    ResolveHierarchyClusterId(world, childLevel, fromSectorId)];
                edgeCost = FindPortalHierarchyEdge(childCluster, fromNode, toNode).DeterministicCost;
            }
            parts.Add($"{fromSectorId}/{fromPortalId}>{toSectorId}/{toPortalId}:{edgeCost}");
        }
        return string.Join(",", parts);
    }

    private static long CalculatePortalWitnessCost(
        NavigationWorld world,
        int[] nodes,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY)
    {
        if (nodes == null || nodes.Length == 0)
            throw new InvalidOperationException("Portal witness cost requires at least one node.");
        DecodePortalNode(nodes[0], out int firstSectorId, out int firstPortalId);
        if (firstSectorId != startSectorId)
            throw new InvalidOperationException($"Portal witness starts in sector={firstSectorId}, expected={startSectorId}.");
        long cost = ResolveDeterministicPortalAccessCost(
            world,
            world.Sectors[startSectorId],
            startSectorId,
            firstPortalId,
            startX,
            startY);
        for (int i = 0; i + 1 < nodes.Length; i++)
        {
            DecodePortalNode(nodes[i], out int fromSectorId, out int fromPortalId);
            DecodePortalNode(nodes[i + 1], out int toSectorId, out int toPortalId);
            long edgeCost;
            if (fromPortalId == toPortalId
                && GetOppositeSectorId(GetPortalById(world, fromPortalId), fromSectorId) == toSectorId)
            {
                edgeCost = DeterministicPortalCrossingCost;
            }
            else
            {
                if (fromSectorId != toSectorId)
                    throw new InvalidOperationException($"Portal witness contains an invalid edge from={nodes[i]} to={nodes[i + 1]}.");
                edgeCost = ResolveDeterministicPortalTransitionCost(world, fromSectorId, fromPortalId, toPortalId);
            }
            cost = AddDeterministicPortalCosts(cost, edgeCost);
        }
        DecodePortalNode(nodes[nodes.Length - 1], out int lastSectorId, out int lastPortalId);
        if (lastSectorId != goalSectorId)
            throw new InvalidOperationException($"Portal witness ends in sector={lastSectorId}, expected={goalSectorId}.");
        return AddDeterministicPortalCosts(
            cost,
            ResolveDeterministicGoalSectorPortalAccessCost(
                world.Sectors[goalSectorId],
                goalSectorId,
                lastPortalId,
                goalX,
                goalY));
    }

    private static string FormatPortalWitnessWindow(int[] nodes, int center)
    {
        if (nodes == null)
            return "null";
        int start = Math.Max(0, center - 3);
        int end = Math.Min(nodes.Length, center + 4);
        var parts = new List<string>(end - start);
        for (int i = start; i < end; i++)
        {
            DecodePortalNode(nodes[i], out int sectorId, out int portalId);
            parts.Add($"{i}:{sectorId}/{portalId}");
        }
        return string.Join(">", parts);
    }

    private static bool TryBuildFullL0DijkstraQuery(
        NavigationWorld world,
        int startSectorId,
        int goalSectorId,
        int startX,
        int startY,
        int goalX,
        int goalY,
        out PortalHierarchyQueryResult result)
    {
        result = null;
        SectorData startSector = world.Sectors[startSectorId];
        var open = new DeterministicCostHeap();
        var costs = new Dictionary<int, long>(256);
        var previous = new Dictionary<int, int>(256);
        var settled = new HashSet<int>();
        for (int i = 0; i < startSector.PortalIds.Count; i++)
        {
            int portalId = startSector.PortalIds[i];
            long cost = ResolveDeterministicPortalAccessCost(world, startSector, startSectorId, portalId, startX, startY);
            if (cost == long.MaxValue)
                continue;
            int node = EncodePortalNode(startSectorId, portalId);
            costs[node] = cost;
            open.Push(node, cost);
        }

        int bestGoalNode = int.MinValue;
        long bestGoalCost = long.MaxValue;
        int expansions = 0;
        while (open.Count > 0)
        {
            DeterministicCostQueueNode item = open.Pop();
            if (!costs.TryGetValue(item.Index, out long currentCost) || item.Cost != currentCost || !settled.Add(item.Index))
                continue;
            expansions++;
            DecodePortalNode(item.Index, out int sectorId, out int portalId);
            if (sectorId == goalSectorId)
            {
                long suffix = ResolveDeterministicGoalSectorPortalAccessCost(world.Sectors[goalSectorId], goalSectorId, portalId, goalX, goalY);
                if (suffix != long.MaxValue)
                {
                    long total = AddDeterministicPortalCosts(currentCost, suffix);
                    if (total < bestGoalCost)
                    {
                        bestGoalCost = total;
                        bestGoalNode = item.Index;
                    }
                }
            }
            if (bestGoalCost != long.MaxValue && open.PeekCost >= bestGoalCost)
                break;

            PortalData portal = GetPortalById(world, portalId);
            int oppositeSectorId = GetOppositeSectorId(portal, sectorId);
            RelaxHierarchyQueryNode(costs, previous, settled, open, item.Index,
                EncodePortalNode(oppositeSectorId, portalId),
                AddDeterministicPortalCosts(currentCost, DeterministicPortalCrossingCost));
            List<PortalTransition> transitions = GetOutgoingPortalTransitions(world.Sectors[sectorId], portalId);
            if (transitions == null)
                continue;
            for (int i = 0; i < transitions.Count; i++)
            {
                PortalTransition transition = transitions[i];
                RelaxHierarchyQueryNode(costs, previous, settled, open, item.Index,
                    EncodePortalNode(sectorId, transition.ToPortalId),
                    AddDeterministicPortalCosts(currentCost, transition.DeterministicCost));
            }
        }

        if (bestGoalNode == int.MinValue)
            return false;
        result = new PortalHierarchyQueryResult
        {
            DeterministicCost = bestGoalCost,
            ExpansionCount = expansions,
            Level = 0,
            L0Nodes = ReconstructForwardPortalWitness(previous, bestGoalNode)
        };
        return true;
    }
}
