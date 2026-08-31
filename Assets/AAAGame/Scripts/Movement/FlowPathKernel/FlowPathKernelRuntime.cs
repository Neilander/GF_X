using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AAAGame.FlowPath
{
    public enum FlowPathKernelPopStatus
    {
        Empty = 0,
        Stale = 1,
        Settled = 2,
        MissingCost = 3,
        CostMismatch = 4,
        AlreadySettled = 5
    }

    public readonly struct FlowPathKernelSearchEntry
    {
        public FlowPathKernelSearchEntry(int node, long cost)
        {
            Node = node;
            Cost = cost;
        }

        public int Node { get; }
        public long Cost { get; }
    }

    public enum FlowPathKernelSearchCommandType
    {
        AddSource = 0,
        Pop = 1,
        Relax = 2
    }

    public struct FlowPathKernelSearchCommand
    {
        public int Type;
        public int CurrentNode;
        public int NextNode;
        public long Cost;
    }

    public readonly struct FlowPathKernelGraphEdge
    {
        public FlowPathKernelGraphEdge(int fromNode, int toNode, long cost)
        {
            FromNode = fromNode;
            ToNode = toNode;
            Cost = cost;
        }

        public int FromNode { get; }
        public int ToNode { get; }
        public long Cost { get; }
    }

    internal struct FlowPathKernelGraphRange
    {
        public int Start;
        public int Count;
    }

    public sealed class FlowPathKernelGraphIndex : IDisposable
    {
        private NativeArray<FlowPathKernelGraphEdge> m_OutgoingEdges;
        private NativeArray<FlowPathKernelGraphEdge> m_IncomingEdges;
        private NativeParallelHashMap<int, FlowPathKernelGraphRange> m_OutgoingRanges;
        private NativeParallelHashMap<int, FlowPathKernelGraphRange> m_IncomingRanges;

        public FlowPathKernelGraphIndex(FlowPathKernelGraphEdge[] edges)
        {
            if (edges == null)
                throw new ArgumentNullException(nameof(edges));
            var outgoing = (FlowPathKernelGraphEdge[])edges.Clone();
            var incoming = (FlowPathKernelGraphEdge[])edges.Clone();
            Array.Sort(outgoing, CompareOutgoing);
            Array.Sort(incoming, CompareIncoming);
            m_OutgoingEdges = new NativeArray<FlowPathKernelGraphEdge>(outgoing, Allocator.Persistent);
            m_IncomingEdges = new NativeArray<FlowPathKernelGraphEdge>(incoming, Allocator.Persistent);
            int capacity = Math.Max(1, edges.Length);
            m_OutgoingRanges = new NativeParallelHashMap<int, FlowPathKernelGraphRange>(capacity, Allocator.Persistent);
            m_IncomingRanges = new NativeParallelHashMap<int, FlowPathKernelGraphRange>(capacity, Allocator.Persistent);
            BuildRanges(outgoing, false, ref m_OutgoingRanges);
            BuildRanges(incoming, true, ref m_IncomingRanges);
        }

        public bool IsCreated => m_OutgoingEdges.IsCreated
                                 && m_IncomingEdges.IsCreated
                                 && m_OutgoingRanges.IsCreated
                                 && m_IncomingRanges.IsCreated;

        internal NativeArray<FlowPathKernelGraphEdge> OutgoingEdges => m_OutgoingEdges;
        internal NativeArray<FlowPathKernelGraphEdge> IncomingEdges => m_IncomingEdges;
        internal NativeParallelHashMap<int, FlowPathKernelGraphRange> OutgoingRanges => m_OutgoingRanges;
        internal NativeParallelHashMap<int, FlowPathKernelGraphRange> IncomingRanges => m_IncomingRanges;

        internal bool TryGetRange(bool reverse, int node, out FlowPathKernelGraphRange range)
        {
            if (!IsCreated)
                throw new ObjectDisposedException(nameof(FlowPathKernelGraphIndex));
            return reverse
                ? m_IncomingRanges.TryGetValue(node, out range)
                : m_OutgoingRanges.TryGetValue(node, out range);
        }

        public void Dispose()
        {
            if (m_OutgoingEdges.IsCreated)
                m_OutgoingEdges.Dispose();
            if (m_IncomingEdges.IsCreated)
                m_IncomingEdges.Dispose();
            if (m_OutgoingRanges.IsCreated)
                m_OutgoingRanges.Dispose();
            if (m_IncomingRanges.IsCreated)
                m_IncomingRanges.Dispose();
        }

        private static void BuildRanges(
            FlowPathKernelGraphEdge[] edges,
            bool incoming,
            ref NativeParallelHashMap<int, FlowPathKernelGraphRange> ranges)
        {
            int cursor = 0;
            while (cursor < edges.Length)
            {
                int node = incoming ? edges[cursor].ToNode : edges[cursor].FromNode;
                int start = cursor++;
                while (cursor < edges.Length
                       && (incoming ? edges[cursor].ToNode : edges[cursor].FromNode) == node)
                {
                    cursor++;
                }
                if (!ranges.TryAdd(node, new FlowPathKernelGraphRange { Start = start, Count = cursor - start }))
                    throw new InvalidOperationException($"Flow path graph contains a duplicate adjacency range node={node}.");
            }
        }

        private static int CompareOutgoing(FlowPathKernelGraphEdge left, FlowPathKernelGraphEdge right)
        {
            int order = left.FromNode.CompareTo(right.FromNode);
            if (order != 0)
                return order;
            order = left.ToNode.CompareTo(right.ToNode);
            return order != 0 ? order : left.Cost.CompareTo(right.Cost);
        }

        private static int CompareIncoming(FlowPathKernelGraphEdge left, FlowPathKernelGraphEdge right)
        {
            int order = left.ToNode.CompareTo(right.ToNode);
            if (order != 0)
                return order;
            order = left.FromNode.CompareTo(right.FromNode);
            return order != 0 ? order : left.Cost.CompareTo(right.Cost);
        }
    }

    public enum FlowPathKernelGraphSliceStopReason
    {
        QuotaExhausted = 0,
        TargetSettled = 1,
        FrontierEmpty = 2
    }

    public struct FlowPathKernelGraphCursor
    {
        public int Stage;
        public int CurrentNode;
        public long CurrentCost;
        public int EdgeCursor;
        public int EdgeEnd;
    }

    public readonly struct FlowPathKernelGraphSliceResult
    {
        public FlowPathKernelGraphSliceResult(
            int operationCount,
            FlowPathKernelGraphSliceStopReason stopReason,
            FlowPathKernelGraphCursor cursor,
            int remainingTargets)
        {
            OperationCount = operationCount;
            StopReason = stopReason;
            Cursor = cursor;
            RemainingTargets = remainingTargets;
        }

        public int OperationCount { get; }
        public FlowPathKernelGraphSliceStopReason StopReason { get; }
        public FlowPathKernelGraphCursor Cursor { get; }
        public int RemainingTargets { get; }
    }

    public sealed class FlowPathKernelSearchState : IDisposable
    {
        private NativeParallelHashMap<int, long> m_Costs;
        private NativeParallelHashMap<int, int> m_Previous;
        private NativeParallelHashSet<int> m_Settled;
        private NativeList<HeapNode> m_Open;
        private NativeList<FlowPathKernelSearchCommand> m_Commands;
        private NativeReference<SearchMetadata> m_Metadata;
        private NativeList<int> m_GraphSliceTargets;
        private JobHandle m_PendingGraphSlice;
        private bool m_HasPendingGraphSlice;
        private FlowPathKernelGraphIndex m_PendingGraph;
        private bool m_PendingGraphReverse;

        public FlowPathKernelSearchState(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Costs = new NativeParallelHashMap<int, long>(capacity, Allocator.Persistent);
            m_Previous = new NativeParallelHashMap<int, int>(capacity, Allocator.Persistent);
            m_Settled = new NativeParallelHashSet<int>(capacity, Allocator.Persistent);
            m_Open = new NativeList<HeapNode>(capacity, Allocator.Persistent);
            m_Commands = new NativeList<FlowPathKernelSearchCommand>(16, Allocator.Persistent);
            m_Metadata = new NativeReference<SearchMetadata>(Allocator.Persistent);
            m_GraphSliceTargets = new NativeList<int>(4, Allocator.Persistent);
        }

        public bool IsCreated => m_Costs.IsCreated;
        public bool HasPendingGraphSlice => m_HasPendingGraphSlice;
        public bool IsPendingGraphSliceCompleted => m_HasPendingGraphSlice && m_PendingGraphSlice.IsCompleted;
        public int OpenCount { get { RequireNoPendingGraphSlice(); return m_Open.Length; } }
        public int ExpansionCount { get { RequireNoPendingGraphSlice(); return m_Metadata.Value.ExpansionCount; } }
        public int CostCount { get { RequireNoPendingGraphSlice(); return m_Costs.Count(); } }
        public int PreviousCount { get { RequireNoPendingGraphSlice(); return m_Previous.Count(); } }
        public int SettledCount { get { RequireNoPendingGraphSlice(); return m_Settled.Count(); } }
        public ulong CostsAuthorityHash { get { RequireNoPendingGraphSlice(); return m_Metadata.Value.CostsAuthorityHash; } }
        public ulong PreviousAuthorityHash { get { RequireNoPendingGraphSlice(); return m_Metadata.Value.PreviousAuthorityHash; } }
        public ulong SettledAuthorityHash { get { RequireNoPendingGraphSlice(); return m_Metadata.Value.SettledAuthorityHash; } }
        public ulong OpenAuthorityHash { get { RequireNoPendingGraphSlice(); return m_Metadata.Value.OpenAuthorityHash; } }

        internal NativeParallelHashMap<int, int> PreviousMap
        {
            get
            {
                RequireCreated();
                RequireNoPendingGraphSlice();
                return m_Previous;
            }
        }

        public void AddSource(int node, long cost)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            BeginCommandSlice();
            AppendAddSource(node, cost);
            ExecuteCommandSlice(out _, out _);
        }

        public FlowPathKernelPopStatus PopOne(out FlowPathKernelSearchEntry entry)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            BeginCommandSlice();
            AppendPop();
            ExecuteCommandSlice(out FlowPathKernelPopStatus status, out entry);
            return status;
        }

        public void Relax(int currentNode, int nextNode, long nextCost)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            BeginCommandSlice();
            AppendRelax(currentNode, nextNode, nextCost);
            ExecuteCommandSlice(out _, out _);
        }

        public void BeginCommandSlice()
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            m_Commands.Clear();
        }

        public void AppendAddSource(int node, long cost)
        {
            RequireCreated();
            EnsureCommandCapacity();
            EnsureCapacityForNode(node, needsPrevious: false);
            m_Commands.Add(new FlowPathKernelSearchCommand
            {
                Type = (int)FlowPathKernelSearchCommandType.AddSource,
                NextNode = node,
                Cost = cost
            });
        }

        public void AppendPop()
        {
            RequireCreated();
            EnsureCommandCapacity();
            EnsureSettledCapacity();
            m_Commands.Add(new FlowPathKernelSearchCommand
            {
                Type = (int)FlowPathKernelSearchCommandType.Pop
            });
        }

        public void AppendRelax(int currentNode, int nextNode, long nextCost)
        {
            RequireCreated();
            EnsureCommandCapacity();
            EnsureCapacityForNode(nextNode, needsPrevious: true);
            m_Commands.Add(new FlowPathKernelSearchCommand
            {
                Type = (int)FlowPathKernelSearchCommandType.Relax,
                CurrentNode = currentNode,
                NextNode = nextNode,
                Cost = nextCost
            });
        }

        public int ExecuteCommandSlice(
            out FlowPathKernelPopStatus popStatus,
            out FlowPathKernelSearchEntry popEntry)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            if (m_Commands.Length == 0)
                throw new InvalidOperationException("Flow path search command slice is empty.");
            new SearchCommandSliceJob
            {
                Commands = m_Commands,
                Costs = m_Costs,
                Previous = m_Previous,
                Settled = m_Settled,
                Open = m_Open,
                Metadata = m_Metadata
            }.Run();
            SearchMetadata metadata = m_Metadata.Value;
            if (metadata.RelaxError != 0)
            {
                throw new InvalidOperationException(
                    $"Flow path search command slice failed with error={metadata.RelaxError}.");
            }
            PopResult result = metadata.PopResult;
            popStatus = (FlowPathKernelPopStatus)result.Status;
            popEntry = new FlowPathKernelSearchEntry(result.Node, result.Cost);
            return metadata.CommandCount;
        }

        public void SetGraphSliceTargets(System.Collections.Generic.IReadOnlyList<int> targets)
        {
            RequireCreated();
            if (targets == null || targets.Count == 0)
                throw new ArgumentException("Flow path graph slice targets are empty.", nameof(targets));
            m_GraphSliceTargets.Clear();
            if (m_GraphSliceTargets.Capacity < targets.Count)
                m_GraphSliceTargets.Capacity = targets.Count;
            for (int i = 0; i < targets.Count; i++)
            {
                int target = targets[i];
                for (int j = 0; j < i; j++)
                {
                    if (targets[j] == target)
                        throw new InvalidOperationException($"Flow path graph slice contains duplicate target={target}.");
                }
                m_GraphSliceTargets.Add(target);
            }
        }

        public FlowPathKernelGraphSliceResult AdvanceGraphSlice(
            FlowPathKernelGraphIndex graph,
            bool reverse,
            int sectorCountX,
            int allowedStartSectorX,
            int allowedStartSectorY,
            int allowedWidthSectors,
            int allowedHeightSectors,
            FlowPathKernelGraphCursor cursor,
            int operationQuota)
        {
            RequireCreated();
            if (graph == null || !graph.IsCreated)
                throw new InvalidOperationException("Flow path graph slice requires a committed graph index.");
            if (m_GraphSliceTargets.Length == 0)
                throw new InvalidOperationException("Flow path graph slice targets were not configured.");
            if (operationQuota <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationQuota));
            if (sectorCountX <= 0)
                throw new ArgumentOutOfRangeException(nameof(sectorCountX));
            bool restricted = allowedWidthSectors > 0 || allowedHeightSectors > 0;
            if (restricted && (allowedWidthSectors <= 0 || allowedHeightSectors <= 0))
                throw new ArgumentException("Flow path graph slice restriction is incomplete.");
            if (cursor.Stage == 1)
            {
                if (!graph.TryGetRange(reverse, cursor.CurrentNode, out FlowPathKernelGraphRange range)
                    || cursor.EdgeCursor < 0
                    || cursor.EdgeCursor > range.Count)
                {
                    throw new InvalidOperationException(
                        $"Flow path graph cursor is outside its immutable adjacency range node={cursor.CurrentNode}, offset={cursor.EdgeCursor}.");
                }
                cursor.EdgeCursor += range.Start;
                cursor.EdgeEnd = range.Start + range.Count;
            }
            int operationCount = 0;
            FlowPathKernelGraphSliceResult result;
            do
            {
                ScheduleGraphSlice(
                    graph,
                    reverse,
                    sectorCountX,
                    allowedStartSectorX,
                    allowedStartSectorY,
                    allowedWidthSectors,
                    allowedHeightSectors,
                    cursor,
                    operationQuota - operationCount);
                result = CompleteScheduledGraphSlice();
                operationCount = checked(operationCount + result.OperationCount);
                cursor = result.Cursor;
                if (result.StopReason != FlowPathKernelGraphSliceStopReason.QuotaExhausted
                    || result.OperationCount == 0)
                    break;
            }
            while (operationCount < operationQuota);
            return new FlowPathKernelGraphSliceResult(
                operationCount,
                result.StopReason,
                cursor,
                result.RemainingTargets);
        }

        public void ScheduleGraphSlice(
            FlowPathKernelGraphIndex graph,
            bool reverse,
            int sectorCountX,
            int allowedStartSectorX,
            int allowedStartSectorY,
            int allowedWidthSectors,
            int allowedHeightSectors,
            FlowPathKernelGraphCursor cursor,
            int operationQuota)
        {
            RequireCreated();
            if (m_HasPendingGraphSlice)
                throw new InvalidOperationException("Flow path graph slice is already scheduled.");
            if (graph == null || !graph.IsCreated)
                throw new InvalidOperationException("Flow path graph slice requires a committed graph index.");
            if (m_GraphSliceTargets.Length == 0)
                throw new InvalidOperationException("Flow path graph slice targets were not configured.");
            if (operationQuota <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationQuota));
            if (sectorCountX <= 0)
                throw new ArgumentOutOfRangeException(nameof(sectorCountX));
            bool restricted = allowedWidthSectors > 0 || allowedHeightSectors > 0;
            if (restricted && (allowedWidthSectors <= 0 || allowedHeightSectors <= 0))
                throw new ArgumentException("Flow path graph slice restriction is incomplete.");
            if (cursor.Stage == 1)
            {
                if (!graph.TryGetRange(reverse, cursor.CurrentNode, out FlowPathKernelGraphRange range)
                    || cursor.EdgeCursor < 0
                    || cursor.EdgeCursor > range.Count)
                {
                    throw new InvalidOperationException(
                        $"Flow path graph cursor is outside its immutable adjacency range node={cursor.CurrentNode}, offset={cursor.EdgeCursor}.");
                }
                cursor.EdgeCursor += range.Start;
                cursor.EdgeEnd = range.Start + range.Count;
            }
            m_PendingGraph = graph;
            m_PendingGraphReverse = reverse;
            m_PendingGraphSlice = new AdvanceGraphSliceJob
            {
                Reverse = reverse ? 1 : 0,
                Targets = m_GraphSliceTargets,
                SectorCountX = sectorCountX,
                Restricted = restricted ? 1 : 0,
                AllowedStartSectorX = allowedStartSectorX,
                AllowedStartSectorY = allowedStartSectorY,
                AllowedWidthSectors = allowedWidthSectors,
                AllowedHeightSectors = allowedHeightSectors,
                OperationQuota = operationQuota,
                InitialCursor = cursor,
                OutgoingEdges = graph.OutgoingEdges,
                IncomingEdges = graph.IncomingEdges,
                OutgoingRanges = graph.OutgoingRanges,
                IncomingRanges = graph.IncomingRanges,
                Costs = m_Costs,
                Previous = m_Previous,
                Settled = m_Settled,
                Open = m_Open,
                Metadata = m_Metadata
            }.Schedule();
            m_HasPendingGraphSlice = true;
            JobHandle.ScheduleBatchedJobs();
        }

        public FlowPathKernelGraphSliceResult CompleteScheduledGraphSlice()
        {
            RequireCreated();
            if (!m_HasPendingGraphSlice)
                throw new InvalidOperationException("Flow path graph slice has no scheduled job.");
            m_PendingGraphSlice.Complete();
            m_HasPendingGraphSlice = false;
            GraphSliceOutput output = m_Metadata.Value.GraphSliceOutput;
            if (output.Error != 0)
                throw new InvalidOperationException($"Flow path graph slice failed with error={output.Error}.");
            if (output.CapacityMask != 0)
                GrowExhaustedGraphSliceCapacity(output.CapacityMask);
            FlowPathKernelGraphCursor cursor = output.Cursor;
            if (cursor.Stage == 1)
            {
                if (!m_PendingGraph.TryGetRange(m_PendingGraphReverse, cursor.CurrentNode, out FlowPathKernelGraphRange range))
                    throw new InvalidOperationException("Flow path graph slice returned a missing adjacency range.");
                cursor.EdgeCursor -= range.Start;
                cursor.EdgeEnd = range.Count;
            }
            else
            {
                cursor.EdgeCursor = 0;
                cursor.EdgeEnd = 0;
            }
            m_PendingGraph = null;
            return new FlowPathKernelGraphSliceResult(
                output.OperationCount,
                (FlowPathKernelGraphSliceStopReason)output.StopReason,
                cursor,
                output.RemainingTargets);
        }

        public bool TryCompleteScheduledGraphSlice(out FlowPathKernelGraphSliceResult result)
        {
            RequireCreated();
            result = default(FlowPathKernelGraphSliceResult);
            if (!m_HasPendingGraphSlice || !m_PendingGraphSlice.IsCompleted)
                return false;
            result = CompleteScheduledGraphSlice();
            return true;
        }

        public bool TryGetCost(int node, out long cost)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            return m_Costs.TryGetValue(node, out cost);
        }

        public bool ContainsSettled(int node)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            return m_Settled.Contains(node);
        }

        public bool TryGetPrevious(int node, out int previous)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            return m_Previous.TryGetValue(node, out previous);
        }

        public void ShiftCosts(long delta)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            if (delta == 0L)
                return;
            using (NativeArray<int> keys = m_Costs.GetKeyArray(Allocator.TempJob))
            {
                new ShiftCostsJob
                {
                    Delta = delta,
                    Keys = keys,
                    Costs = m_Costs,
                    Open = m_Open,
                    Metadata = m_Metadata
                }.Run();
            }
            if (m_Metadata.Value.ShiftError != 0)
                throw new OverflowException($"Flow path kernel cost shift overflowed. delta={delta}.");
        }

        public bool ValidateAuthorityHashes()
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            ulong costsHash = 0UL;
            foreach (var pair in m_Costs)
                costsHash ^= CostToken(pair.Key, pair.Value);
            ulong previousHash = 0UL;
            foreach (var pair in m_Previous)
                previousHash ^= PreviousToken(pair.Key, pair.Value);
            ulong settledHash = 0UL;
            foreach (int node in m_Settled)
                settledHash ^= NodeToken(node);
            ulong openHash = 0UL;
            for (int i = 0; i < m_Open.Length; i++)
                openHash += HeapToken(m_Open[i].Node, m_Open[i].Cost);
            SearchMetadata metadata = m_Metadata.Value;
            return costsHash == metadata.CostsAuthorityHash
                   && previousHash == metadata.PreviousAuthorityHash
                   && settledHash == metadata.SettledAuthorityHash
                   && openHash == metadata.OpenAuthorityHash;
        }

        public void CopyTo(
            Action<int, long> addCost,
            Action<int, int> addPrevious,
            Action<int> addSettled)
        {
            RequireCreated();
            if (addCost == null || addPrevious == null || addSettled == null)
                throw new ArgumentNullException("Flow path kernel search materialization delegates cannot be null.");
            foreach (var pair in m_Costs)
                addCost(pair.Key, pair.Value);
            foreach (var pair in m_Previous)
                addPrevious(pair.Key, pair.Value);
            foreach (int node in m_Settled)
                addSettled(node);
        }

        public void ImportCompleted(
            System.Collections.Generic.IReadOnlyDictionary<int, long> costs,
            System.Collections.Generic.IReadOnlyDictionary<int, int> previous,
            System.Collections.Generic.IReadOnlyCollection<int> settled,
            int expansionCount)
        {
            RequireCreated();
            RequireNoPendingGraphSlice();
            if (costs == null || previous == null || settled == null)
                throw new System.ArgumentNullException("Flow path kernel completed search data cannot be null.");
            if (m_Costs.Count() != 0 || m_Previous.Count() != 0 || m_Settled.Count() != 0 || m_Open.Length != 0)
                throw new System.InvalidOperationException("Flow path kernel completed search import requires an empty state.");
            if (expansionCount < 0)
                throw new System.ArgumentOutOfRangeException(nameof(expansionCount));

            ulong costsHash = 0UL;
            foreach (var pair in costs)
            {
                EnsureCapacityForNode(pair.Key, needsPrevious: false);
                if (!m_Costs.TryAdd(pair.Key, pair.Value))
                    throw new System.InvalidOperationException($"Flow path kernel completed cost import contains duplicate node={pair.Key}.");
                costsHash ^= CostToken(pair.Key, pair.Value);
            }

            ulong previousHash = 0UL;
            foreach (var pair in previous)
            {
                EnsureCapacityForNode(pair.Key, needsPrevious: true);
                if (!m_Previous.TryAdd(pair.Key, pair.Value))
                    throw new System.InvalidOperationException($"Flow path kernel completed previous import contains duplicate node={pair.Key}.");
                previousHash ^= PreviousToken(pair.Key, pair.Value);
            }

            ulong settledHash = 0UL;
            foreach (int node in settled)
            {
                EnsureSettledCapacity();
                if (!m_Settled.Add(node))
                    throw new System.InvalidOperationException($"Flow path kernel completed settled import contains duplicate node={node}.");
                settledHash ^= NodeToken(node);
            }

            SearchMetadata metadata = m_Metadata.Value;
            metadata.ExpansionCount = expansionCount;
            metadata.CostsAuthorityHash = costsHash;
            metadata.PreviousAuthorityHash = previousHash;
            metadata.SettledAuthorityHash = settledHash;
            metadata.OpenAuthorityHash = 0UL;
            m_Metadata.Value = metadata;
        }

        public void Dispose()
        {
            if (m_HasPendingGraphSlice)
            {
                m_PendingGraphSlice.Complete();
                m_HasPendingGraphSlice = false;
            }
            if (m_Costs.IsCreated)
                m_Costs.Dispose();
            if (m_Previous.IsCreated)
                m_Previous.Dispose();
            if (m_Settled.IsCreated)
                m_Settled.Dispose();
            if (m_Open.IsCreated)
                m_Open.Dispose();
            if (m_Commands.IsCreated)
                m_Commands.Dispose();
            if (m_Metadata.IsCreated)
                m_Metadata.Dispose();
            if (m_GraphSliceTargets.IsCreated)
                m_GraphSliceTargets.Dispose();
        }

        private void RequireCreated()
        {
            if (!m_Costs.IsCreated
                || !m_Previous.IsCreated
                || !m_Settled.IsCreated
                || !m_Open.IsCreated
                || !m_Commands.IsCreated
                || !m_Metadata.IsCreated
                || !m_GraphSliceTargets.IsCreated)
            {
                throw new ObjectDisposedException(nameof(FlowPathKernelSearchState));
            }
        }

        private void RequireNoPendingGraphSlice()
        {
            if (m_HasPendingGraphSlice)
                throw new InvalidOperationException("Flow path search state is owned by a pending graph slice job.");
        }

        private void EnsureCapacityForNode(int node, bool needsPrevious)
        {
            if (!m_Costs.ContainsKey(node) && m_Costs.Count() >= m_Costs.Capacity)
                m_Costs.Capacity = checked(Math.Max(m_Costs.Capacity * 2, m_Costs.Capacity + 1));
            if (needsPrevious
                && !m_Previous.ContainsKey(node)
                && m_Previous.Count() >= m_Previous.Capacity)
            {
                m_Previous.Capacity = checked(Math.Max(m_Previous.Capacity * 2, m_Previous.Capacity + 1));
            }
        }

        private void GrowExhaustedGraphSliceCapacity(int capacityMask)
        {
            if ((capacityMask & AllCapacityMasks) == 0 || (capacityMask & ~AllCapacityMasks) != 0)
                throw new InvalidOperationException($"Flow path graph slice reported an invalid capacity mask={capacityMask}.");
            if ((capacityMask & CostsCapacityMask) != 0)
            {
                if (m_Costs.Count() < m_Costs.Capacity)
                    throw new InvalidOperationException("Flow path graph slice reported unsaturated costs capacity.");
                m_Costs.Capacity = checked(Math.Max(m_Costs.Capacity * 2, m_Costs.Capacity + 1));
            }
            if ((capacityMask & PreviousCapacityMask) != 0)
            {
                if (m_Previous.Count() < m_Previous.Capacity)
                    throw new InvalidOperationException("Flow path graph slice reported unsaturated previous capacity.");
                m_Previous.Capacity = checked(Math.Max(m_Previous.Capacity * 2, m_Previous.Capacity + 1));
            }
            if ((capacityMask & SettledCapacityMask) != 0)
            {
                if (m_Settled.Count() < m_Settled.Capacity)
                    throw new InvalidOperationException("Flow path graph slice reported unsaturated settled capacity.");
                m_Settled.Capacity = checked(Math.Max(m_Settled.Capacity * 2, m_Settled.Capacity + 1));
            }
            if ((capacityMask & OpenCapacityMask) != 0)
            {
                if (m_Open.Length < m_Open.Capacity)
                    throw new InvalidOperationException("Flow path graph slice reported unsaturated open capacity.");
                m_Open.Capacity = checked(Math.Max(m_Open.Capacity * 2, m_Open.Capacity + 1));
            }
        }

        private void EnsureSettledCapacity()
        {
            if (m_Settled.Count() >= m_Settled.Capacity)
                m_Settled.Capacity = checked(Math.Max(m_Settled.Capacity * 2, m_Settled.Capacity + 1));
        }

        private void EnsureCommandCapacity()
        {
            if (m_Commands.Length >= m_Commands.Capacity)
                m_Commands.Capacity = checked(Math.Max(m_Commands.Capacity * 2, m_Commands.Capacity + 1));
        }

        private struct HeapNode
        {
            public int Node;
            public long Cost;
        }

        private struct PopResult
        {
            public int Status;
            public int Node;
            public long Cost;
        }

        private struct GraphSliceOutput
        {
            public int OperationCount;
            public int StopReason;
            public int RemainingTargets;
            public int Error;
            public int CapacityMask;
            public FlowPathKernelGraphCursor Cursor;
        }

        private const int CostsCapacityMask = 1 << 0;
        private const int PreviousCapacityMask = 1 << 1;
        private const int SettledCapacityMask = 1 << 2;
        private const int OpenCapacityMask = 1 << 3;
        private const int AllCapacityMasks = CostsCapacityMask
                                             | PreviousCapacityMask
                                             | SettledCapacityMask
                                             | OpenCapacityMask;

        private struct SearchMetadata
        {
            public PopResult PopResult;
            public GraphSliceOutput GraphSliceOutput;
            public int CommandCount;
            public int ExpansionCount;
            public int RelaxError;
            public int ShiftError;
            public ulong CostsAuthorityHash;
            public ulong PreviousAuthorityHash;
            public ulong SettledAuthorityHash;
            public ulong OpenAuthorityHash;
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ value >> 31;
        }

        private static ulong CostToken(int node, long cost)
        {
            return Mix(unchecked((ulong)(uint)node << 32) ^ unchecked((ulong)cost) ^ 0x434F535453UL);
        }

        private static ulong PreviousToken(int node, int previous)
        {
            return Mix(unchecked((ulong)(uint)node << 32) ^ (uint)previous ^ 0x50524556UL);
        }

        private static ulong NodeToken(int node)
        {
            return Mix((uint)node ^ 0x534554544C4544UL);
        }

        private static ulong HeapToken(int node, long cost)
        {
            return Mix(unchecked((ulong)(uint)node << 32) ^ unchecked((ulong)cost) ^ 0x48454150UL);
        }

        private static bool ComesBefore(HeapNode left, HeapNode right)
        {
            return left.Cost < right.Cost || left.Cost == right.Cost && left.Node < right.Node;
        }

        private static void Push(ref NativeList<HeapNode> heap, HeapNode value)
        {
            heap.Add(value);
            int index = heap.Length - 1;
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                HeapNode parentValue = heap[parent];
                if (!ComesBefore(value, parentValue))
                    break;
                heap[index] = parentValue;
                index = parent;
            }
            heap[index] = value;
        }

        private static HeapNode Pop(ref NativeList<HeapNode> heap)
        {
            HeapNode root = heap[0];
            int lastIndex = heap.Length - 1;
            HeapNode tail = heap[lastIndex];
            heap.RemoveAt(lastIndex);
            if (heap.Length == 0)
                return root;
            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= heap.Length)
                    break;
                int right = left + 1;
                int child = right < heap.Length && ComesBefore(heap[right], heap[left]) ? right : left;
                if (!ComesBefore(heap[child], tail))
                    break;
                heap[index] = heap[child];
                index = child;
            }
            heap[index] = tail;
            return root;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct SearchCommandSliceJob : IJob
        {
            [ReadOnly] public NativeList<FlowPathKernelSearchCommand> Commands;
            public NativeParallelHashMap<int, long> Costs;
            public NativeParallelHashMap<int, int> Previous;
            public NativeParallelHashSet<int> Settled;
            public NativeList<HeapNode> Open;
            public NativeReference<SearchMetadata> Metadata;

            public void Execute()
            {
                SearchMetadata metadata = Metadata.Value;
                metadata.PopResult = default;
                metadata.CommandCount = 0;
                metadata.RelaxError = 0;
                for (int i = 0; i < Commands.Length; i++)
                {
                    FlowPathKernelSearchCommand command = Commands[i];
                    switch ((FlowPathKernelSearchCommandType)command.Type)
                    {
                        case FlowPathKernelSearchCommandType.AddSource:
                            AddSource(command.NextNode, command.Cost, ref metadata);
                            break;
                        case FlowPathKernelSearchCommandType.Pop:
                            PopOne(ref metadata);
                            break;
                        case FlowPathKernelSearchCommandType.Relax:
                            Relax(command.CurrentNode, command.NextNode, command.Cost, ref metadata);
                            break;
                        default:
                            metadata.RelaxError = 2;
                            break;
                    }
                    if (metadata.RelaxError != 0)
                        break;
                    metadata.CommandCount++;
                }
                Metadata.Value = metadata;
            }

            private void AddSource(int node, long cost, ref SearchMetadata metadata)
            {
                if (Costs.TryGetValue(node, out long existing) && cost >= existing)
                    return;
                if (Costs.TryGetValue(node, out existing))
                    metadata.CostsAuthorityHash ^= CostToken(node, existing);
                Costs[node] = cost;
                metadata.CostsAuthorityHash ^= CostToken(node, cost);
                Push(ref Open, new HeapNode { Node = node, Cost = cost });
                metadata.OpenAuthorityHash += HeapToken(node, cost);
            }

            private void PopOne(ref SearchMetadata metadata)
            {
                metadata.PopResult = default;
                if (Open.Length == 0)
                    return;
                HeapNode item = Pop(ref Open);
                metadata.OpenAuthorityHash -= HeapToken(item.Node, item.Cost);
                if (!Costs.TryGetValue(item.Node, out long currentCost))
                {
                    metadata.PopResult = new PopResult
                    {
                        Status = (int)FlowPathKernelPopStatus.MissingCost,
                        Node = item.Node,
                        Cost = item.Cost
                    };
                    return;
                }
                if (item.Cost != currentCost)
                {
                    metadata.PopResult = new PopResult
                    {
                        Status = (int)FlowPathKernelPopStatus.CostMismatch,
                        Node = item.Node,
                        Cost = item.Cost
                    };
                    return;
                }
                if (!Settled.Add(item.Node))
                {
                    metadata.PopResult = new PopResult
                    {
                        Status = (int)FlowPathKernelPopStatus.AlreadySettled,
                        Node = item.Node,
                        Cost = item.Cost
                    };
                    return;
                }
                metadata.ExpansionCount++;
                metadata.SettledAuthorityHash ^= NodeToken(item.Node);
                metadata.PopResult = new PopResult
                {
                    Status = (int)FlowPathKernelPopStatus.Settled,
                    Node = item.Node,
                    Cost = currentCost
                };
            }

            private void Relax(int currentNode, int nextNode, long nextCost, ref SearchMetadata metadata)
            {
                if (Settled.Contains(nextNode))
                {
                    if (Costs.TryGetValue(nextNode, out long settledCost) && nextCost < settledCost)
                        metadata.RelaxError = 1;
                    return;
                }
                if (Costs.TryGetValue(nextNode, out long existing) && nextCost >= existing)
                    return;
                if (Costs.TryGetValue(nextNode, out existing))
                    metadata.CostsAuthorityHash ^= CostToken(nextNode, existing);
                Costs[nextNode] = nextCost;
                metadata.CostsAuthorityHash ^= CostToken(nextNode, nextCost);
                if (Previous.TryGetValue(nextNode, out int previous))
                    metadata.PreviousAuthorityHash ^= PreviousToken(nextNode, previous);
                Previous[nextNode] = currentNode;
                metadata.PreviousAuthorityHash ^= PreviousToken(nextNode, currentNode);
                Push(ref Open, new HeapNode { Node = nextNode, Cost = nextCost });
                metadata.OpenAuthorityHash += HeapToken(nextNode, nextCost);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceGraphSliceJob : IJob
        {
            public int Reverse;
            [ReadOnly] public NativeList<int> Targets;
            public int SectorCountX;
            public int Restricted;
            public int AllowedStartSectorX;
            public int AllowedStartSectorY;
            public int AllowedWidthSectors;
            public int AllowedHeightSectors;
            public int OperationQuota;
            public FlowPathKernelGraphCursor InitialCursor;
            [ReadOnly] public NativeArray<FlowPathKernelGraphEdge> OutgoingEdges;
            [ReadOnly] public NativeArray<FlowPathKernelGraphEdge> IncomingEdges;
            [ReadOnly] public NativeParallelHashMap<int, FlowPathKernelGraphRange> OutgoingRanges;
            [ReadOnly] public NativeParallelHashMap<int, FlowPathKernelGraphRange> IncomingRanges;
            public NativeParallelHashMap<int, long> Costs;
            public NativeParallelHashMap<int, int> Previous;
            public NativeParallelHashSet<int> Settled;
            public NativeList<HeapNode> Open;
            public NativeReference<SearchMetadata> Metadata;

            public void Execute()
            {
                SearchMetadata metadata = Metadata.Value;
                var output = new GraphSliceOutput
                {
                    StopReason = (int)FlowPathKernelGraphSliceStopReason.QuotaExhausted,
                    Cursor = InitialCursor
                };
                int remainingTargets = 0;
                for (int targetIndex = 0; targetIndex < Targets.Length; targetIndex++)
                {
                    if (!Settled.Contains(Targets[targetIndex]))
                        remainingTargets++;
                }
                if (remainingTargets == 0)
                {
                    output.StopReason = (int)FlowPathKernelGraphSliceStopReason.TargetSettled;
                    output.RemainingTargets = 0;
                    metadata.GraphSliceOutput = output;
                    Metadata.Value = metadata;
                    return;
                }
                while (output.OperationCount < OperationQuota)
                {
                    if (output.Cursor.Stage == 0)
                    {
                        if (Open.Length == 0)
                        {
                            output.OperationCount++;
                            output.StopReason = (int)FlowPathKernelGraphSliceStopReason.FrontierEmpty;
                            break;
                        }
                        HeapNode item = Open[0];
                        if (!Costs.TryGetValue(item.Node, out long currentCost))
                        {
                            output.Error = 1;
                            break;
                        }
                        bool settlesNode = item.Cost == currentCost && !Settled.Contains(item.Node);
                        if (settlesNode && Settled.Count() >= Settled.Capacity)
                        {
                            output.CapacityMask = SettledCapacityMask;
                            break;
                        }
                        Pop(ref Open);
                        metadata.OpenAuthorityHash -= HeapToken(item.Node, item.Cost);
                        output.OperationCount++;
                        if (!settlesNode)
                            continue;
                        Settled.Add(item.Node);
                        metadata.ExpansionCount++;
                        metadata.SettledAuthorityHash ^= NodeToken(item.Node);
                        for (int targetIndex = 0; targetIndex < Targets.Length; targetIndex++)
                        {
                            if (Targets[targetIndex] == item.Node)
                            {
                                remainingTargets--;
                                break;
                            }
                        }
                        if (remainingTargets == 0)
                        {
                            output.StopReason = (int)FlowPathKernelGraphSliceStopReason.TargetSettled;
                            break;
                        }
                        FlowPathKernelGraphRange range;
                        bool hasRange = Reverse != 0
                            ? IncomingRanges.TryGetValue(item.Node, out range)
                            : OutgoingRanges.TryGetValue(item.Node, out range);
                        if (!hasRange)
                            continue;
                        output.Cursor.Stage = 1;
                        output.Cursor.CurrentNode = item.Node;
                        output.Cursor.CurrentCost = currentCost;
                        output.Cursor.EdgeCursor = range.Start;
                        output.Cursor.EdgeEnd = checked(range.Start + range.Count);
                        continue;
                    }

                    if (output.Cursor.Stage != 1)
                    {
                        output.Error = 2;
                        break;
                    }
                    if (output.Cursor.EdgeCursor >= output.Cursor.EdgeEnd)
                    {
                        output.Cursor.Stage = 0;
                        continue;
                    }
                    FlowPathKernelGraphEdge edge = Reverse != 0
                        ? IncomingEdges[output.Cursor.EdgeCursor]
                        : OutgoingEdges[output.Cursor.EdgeCursor];
                    int nextNode = Reverse != 0 ? edge.FromNode : edge.ToNode;
                    if (!IsAllowed(nextNode))
                    {
                        output.Cursor.EdgeCursor++;
                        output.OperationCount++;
                        continue;
                    }
                    if (edge.Cost < 0L
                        || output.Cursor.CurrentCost < 0L
                        || output.Cursor.CurrentCost > long.MaxValue - edge.Cost)
                    {
                        output.Error = 3;
                        break;
                    }
                    long nextCost = output.Cursor.CurrentCost + edge.Cost;
                    if (Settled.Contains(nextNode))
                    {
                        if (Costs.TryGetValue(nextNode, out long settledCost) && nextCost < settledCost)
                            output.Error = 4;
                        if (output.Error != 0)
                            break;
                        output.Cursor.EdgeCursor++;
                        output.OperationCount++;
                        continue;
                    }
                    if (Costs.TryGetValue(nextNode, out long existing) && nextCost >= existing)
                    {
                        output.Cursor.EdgeCursor++;
                        output.OperationCount++;
                        continue;
                    }
                    bool addsCost = !Costs.ContainsKey(nextNode);
                    bool addsPrevious = !Previous.ContainsKey(nextNode);
                    int capacityMask = 0;
                    if (addsCost && Costs.Count() >= Costs.Capacity)
                        capacityMask |= CostsCapacityMask;
                    if (addsPrevious && Previous.Count() >= Previous.Capacity)
                        capacityMask |= PreviousCapacityMask;
                    if (Open.Length >= Open.Capacity)
                        capacityMask |= OpenCapacityMask;
                    if (capacityMask != 0)
                    {
                        output.CapacityMask = capacityMask;
                        break;
                    }
                    output.Cursor.EdgeCursor++;
                    output.OperationCount++;
                    if (Costs.TryGetValue(nextNode, out existing))
                        metadata.CostsAuthorityHash ^= CostToken(nextNode, existing);
                    Costs[nextNode] = nextCost;
                    metadata.CostsAuthorityHash ^= CostToken(nextNode, nextCost);
                    if (Previous.TryGetValue(nextNode, out int previous))
                        metadata.PreviousAuthorityHash ^= PreviousToken(nextNode, previous);
                    Previous[nextNode] = output.Cursor.CurrentNode;
                    metadata.PreviousAuthorityHash ^= PreviousToken(nextNode, output.Cursor.CurrentNode);
                    Push(ref Open, new HeapNode { Node = nextNode, Cost = nextCost });
                    metadata.OpenAuthorityHash += HeapToken(nextNode, nextCost);
                }
                output.RemainingTargets = remainingTargets;
                metadata.GraphSliceOutput = output;
                Metadata.Value = metadata;
            }

            private bool IsAllowed(int node)
            {
                if (Restricted == 0)
                    return true;
                int sectorId = node >> 16;
                int sectorX = sectorId % SectorCountX;
                int sectorY = sectorId / SectorCountX;
                return sectorX >= AllowedStartSectorX
                       && sectorX < AllowedStartSectorX + AllowedWidthSectors
                       && sectorY >= AllowedStartSectorY
                       && sectorY < AllowedStartSectorY + AllowedHeightSectors;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ShiftCostsJob : IJob
        {
            public long Delta;
            [ReadOnly] public NativeArray<int> Keys;
            public NativeParallelHashMap<int, long> Costs;
            public NativeList<HeapNode> Open;
            public NativeReference<SearchMetadata> Metadata;

            public void Execute()
            {
                SearchMetadata metadata = Metadata.Value;
                metadata.ShiftError = 0;
                for (int i = 0; i < Keys.Length; i++)
                {
                    long cost = Costs[Keys[i]];
                    if (Delta > 0L && cost > long.MaxValue - Delta
                        || Delta < 0L && cost < long.MinValue - Delta)
                    {
                        metadata.ShiftError = 1;
                        Metadata.Value = metadata;
                        return;
                    }
                }
                for (int i = 0; i < Open.Length; i++)
                {
                    long cost = Open[i].Cost;
                    if (Delta > 0L && cost > long.MaxValue - Delta
                        || Delta < 0L && cost < long.MinValue - Delta)
                    {
                        metadata.ShiftError = 1;
                        Metadata.Value = metadata;
                        return;
                    }
                }

                ulong costsHash = 0UL;
                for (int i = 0; i < Keys.Length; i++)
                {
                    int node = Keys[i];
                    long shifted = Costs[node] + Delta;
                    Costs[node] = shifted;
                    costsHash ^= CostToken(node, shifted);
                }
                ulong openHash = 0UL;
                for (int i = 0; i < Open.Length; i++)
                {
                    HeapNode item = Open[i];
                    item.Cost += Delta;
                    Open[i] = item;
                    openHash += HeapToken(item.Node, item.Cost);
                }
                metadata.CostsAuthorityHash = costsHash;
                metadata.OpenAuthorityHash = openHash;
                Metadata.Value = metadata;
            }
        }
    }

    public struct FlowPathKernelRouteTask
    {
        public int Type;
        public int AuthoritySlot;
        public int Cursor;
        public int CurrentNode;
        public int FromNode;
        public int ToNode;
        public int LevelIndex;
        public int Guard;
        public int Started;
        public int WitnessOffset;
        public int WitnessCount;
    }

    public enum FlowPathKernelRouteConversionStatus
    {
        Initialized = 0,
        Advanced = 1,
        Complete = 2
    }

    public enum FlowPathKernelRouteSliceStopReason
    {
        QuotaExhausted = 0,
        RouteComplete = 1,
        ConnectorBoundary = 2,
        SearchAuthorityBoundary = 3,
        RouteNodeBoundary = 4
    }

    public readonly struct FlowPathKernelRouteSliceResult
    {
        public FlowPathKernelRouteSliceResult(int operationCount, FlowPathKernelRouteSliceStopReason stopReason)
        {
            OperationCount = operationCount;
            StopReason = stopReason;
        }

        public int OperationCount { get; }
        public FlowPathKernelRouteSliceStopReason StopReason { get; }
    }

    public sealed class FlowPathKernelRouteMergeIndex : IDisposable
    {
        private NativeParallelHashSet<int> m_Nodes;

        public FlowPathKernelRouteMergeIndex(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Nodes = new NativeParallelHashSet<int>(capacity, Allocator.Persistent);
        }

        public bool IsCreated => m_Nodes.IsCreated;
        public int Count => m_Nodes.IsCreated ? m_Nodes.Count() : 0;

        public bool Add(int node)
        {
            RequireCreated();
            return m_Nodes.Add(node);
        }

        public bool Contains(int node)
        {
            RequireCreated();
            return m_Nodes.Contains(node);
        }

        public void Clear()
        {
            RequireCreated();
            m_Nodes.Clear();
        }

        public void Dispose()
        {
            if (m_Nodes.IsCreated)
                m_Nodes.Dispose();
        }

        internal NativeParallelHashSet<int> Nodes => m_Nodes;

        private void RequireCreated()
        {
            if (!m_Nodes.IsCreated)
                throw new ObjectDisposedException(nameof(FlowPathKernelRouteMergeIndex));
        }
    }

    public readonly struct FlowPathKernelWitnessEdge
    {
        public FlowPathKernelWitnessEdge(
            int levelIndex,
            int clusterId,
            int fromNode,
            int toNode,
            int witnessOffset,
            int witnessCount)
        {
            LevelIndex = levelIndex;
            ClusterId = clusterId;
            FromNode = fromNode;
            ToNode = toNode;
            WitnessOffset = witnessOffset;
            WitnessCount = witnessCount;
        }

        public int LevelIndex { get; }
        public int ClusterId { get; }
        public int FromNode { get; }
        public int ToNode { get; }
        public int WitnessOffset { get; }
        public int WitnessCount { get; }
    }

    public sealed class FlowPathKernelWitnessIndex : IDisposable
    {
        private NativeArray<FlowPathKernelWitnessEdge> m_Edges;
        private NativeArray<FlowPathKernelWitnessEdge> m_RouteEdges;
        private NativeArray<int> m_WitnessNodes;

        public FlowPathKernelWitnessIndex(
            FlowPathKernelWitnessEdge[] edges,
            int[] witnessNodes)
        {
            if (edges == null)
                throw new ArgumentNullException(nameof(edges));
            if (witnessNodes == null)
                throw new ArgumentNullException(nameof(witnessNodes));
            FlowPathKernelWitnessEdge[] routeEdges = (FlowPathKernelWitnessEdge[])edges.Clone();
            Array.Sort(routeEdges, CompareRouteEdges);
            for (int i = 1; i < routeEdges.Length; i++)
            {
                if (CompareRouteEdges(routeEdges[i - 1], routeEdges[i]) == 0)
                {
                    throw new InvalidOperationException(
                        $"Flow path route witness index contains duplicate logical edges. level={routeEdges[i].LevelIndex}, from={routeEdges[i].FromNode}, to={routeEdges[i].ToNode}.");
                }
            }
            m_Edges = new NativeArray<FlowPathKernelWitnessEdge>(edges, Allocator.Persistent);
            m_RouteEdges = new NativeArray<FlowPathKernelWitnessEdge>(routeEdges, Allocator.Persistent);
            m_WitnessNodes = new NativeArray<int>(witnessNodes, Allocator.Persistent);
        }

        public bool IsCreated => m_Edges.IsCreated && m_RouteEdges.IsCreated && m_WitnessNodes.IsCreated;
        public int EdgeCount => m_Edges.IsCreated ? m_Edges.Length : 0;
        internal NativeArray<FlowPathKernelWitnessEdge> Edges => m_Edges;
        internal NativeArray<FlowPathKernelWitnessEdge> RouteEdges => m_RouteEdges;
        internal NativeArray<int> WitnessNodes => m_WitnessNodes;

        public void Dispose()
        {
            if (m_Edges.IsCreated)
                m_Edges.Dispose();
            if (m_RouteEdges.IsCreated)
                m_RouteEdges.Dispose();
            if (m_WitnessNodes.IsCreated)
                m_WitnessNodes.Dispose();
        }

        private static int CompareRouteEdges(FlowPathKernelWitnessEdge left, FlowPathKernelWitnessEdge right)
        {
            int order = left.LevelIndex.CompareTo(right.LevelIndex);
            if (order != 0)
                return order;
            order = left.FromNode.CompareTo(right.FromNode);
            return order != 0 ? order : left.ToNode.CompareTo(right.ToNode);
        }
    }

    public sealed class FlowPathKernelRouteState : IDisposable
    {
        private NativeList<FlowPathKernelRouteTask> m_Tasks;
        private NativeList<int> m_L0Nodes;
        private NativeList<int> m_SectorIds;
        private NativeList<int> m_PortalIds;
        private NativeReference<int> m_LastLogicalNode;
        private NativeReference<int> m_ConvertCursor;
        private NativeReference<int> m_ConversionStatus;
        private NativeReference<ulong> m_TaskAuthorityHash;
        private NativeReference<ulong> m_L0AuthorityHash;
        private NativeReference<ulong> m_SectorAuthorityHash;
        private NativeReference<ulong> m_PortalAuthorityHash;
        private NativeReference<int> m_SliceOperationCount;
        private NativeReference<int> m_SliceStopReason;
        private NativeReference<int> m_Error;
        private JobHandle m_PendingSlice;
        private bool m_HasPendingSlice;

        public FlowPathKernelRouteState(int taskCapacity, int nodeCapacity)
        {
            if (taskCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(taskCapacity));
            if (nodeCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(nodeCapacity));
            m_Tasks = new NativeList<FlowPathKernelRouteTask>(taskCapacity, Allocator.Persistent);
            m_L0Nodes = new NativeList<int>(nodeCapacity, Allocator.Persistent);
            m_SectorIds = new NativeList<int>(nodeCapacity, Allocator.Persistent);
            m_PortalIds = new NativeList<int>(nodeCapacity, Allocator.Persistent);
            m_LastLogicalNode = new NativeReference<int>(Allocator.Persistent);
            m_LastLogicalNode.Value = int.MinValue;
            m_ConvertCursor = new NativeReference<int>(Allocator.Persistent);
            m_ConversionStatus = new NativeReference<int>(Allocator.Persistent);
            m_TaskAuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_L0AuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_SectorAuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_PortalAuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_SliceOperationCount = new NativeReference<int>(Allocator.Persistent);
            m_SliceStopReason = new NativeReference<int>(Allocator.Persistent);
            m_Error = new NativeReference<int>(Allocator.Persistent);
        }

        public bool IsCreated => m_Tasks.IsCreated;
        public bool HasPendingSlice => m_HasPendingSlice;
        public bool IsPendingSliceCompleted => m_HasPendingSlice && m_PendingSlice.IsCompleted;
        public int TaskCount => m_Tasks.Length;
        public int L0NodeCount => m_L0Nodes.Length;
        public int ConvertedSectorCount => m_SectorIds.Length;
        public int ConvertedPortalCount => m_PortalIds.Length;
        public int ConvertCursor => m_ConvertCursor.Value;
        public int LastLogicalNode
        {
            get => m_LastLogicalNode.Value;
            set => m_LastLogicalNode.Value = value;
        }
        public ulong TaskAuthorityHash => m_TaskAuthorityHash.Value;
        public ulong L0AuthorityHash => m_L0AuthorityHash.Value;
        public ulong SectorAuthorityHash => m_SectorAuthorityHash.Value;
        public ulong PortalAuthorityHash => m_PortalAuthorityHash.Value;

        public void Push(FlowPathKernelRouteTask task)
        {
            RequireCreated();
            new PushRouteTaskJob
            {
                Task = task,
                Tasks = m_Tasks,
                TaskAuthorityHash = m_TaskAuthorityHash
            }.Run();
        }

        public FlowPathKernelRouteTask Peek()
        {
            RequireCreated();
            if (m_Tasks.Length == 0)
                throw new InvalidOperationException("Flow path route task stack is empty.");
            return m_Tasks[m_Tasks.Length - 1];
        }

        public int ResolvePendingSearchAuthoritySlot(
            int hierarchyTraversalTaskType,
            int l0TraversalTaskType,
            int downwardTraversalTaskType,
            int connectorTraversalTaskType)
        {
            RequireCreated();
            for (int i = m_Tasks.Length - 1; i >= 0; i--)
            {
                FlowPathKernelRouteTask task = m_Tasks[i];
                if (task.Type == hierarchyTraversalTaskType
                    || task.Type == l0TraversalTaskType
                    || task.Type == downwardTraversalTaskType
                    || task.Type == connectorTraversalTaskType)
                {
                    return task.AuthoritySlot;
                }
            }
            throw new InvalidOperationException("Flow path route task stack has no search authority boundary.");
        }

        public FlowPathKernelRouteTask Pop()
        {
            RequireCreated();
            if (m_Tasks.Length == 0)
                throw new InvalidOperationException("Flow path route task stack is empty.");
            int index = m_Tasks.Length - 1;
            FlowPathKernelRouteTask task = m_Tasks[index];
            m_TaskAuthorityHash.Value ^= TaskToken(index, task);
            m_Tasks.RemoveAt(index);
            return task;
        }

        public void ClearTasks()
        {
            RequireCreated();
            m_Tasks.Clear();
            m_TaskAuthorityHash.Value = 0UL;
        }

        public int GetL0Node(int index)
        {
            RequireCreated();
            if ((uint)index >= (uint)m_L0Nodes.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_L0Nodes[index];
        }

        public int[] CopyL0Nodes()
        {
            RequireCreated();
            return m_L0Nodes.AsArray().ToArray();
        }

        public int GetConvertedSector(int index)
        {
            RequireCreated();
            if ((uint)index >= (uint)m_SectorIds.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_SectorIds[index];
        }

        public int GetConvertedPortal(int index)
        {
            RequireCreated();
            if ((uint)index >= (uint)m_PortalIds.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_PortalIds[index];
        }

        public int[] CopyConvertedSectors(int count)
        {
            RequireCreated();
            if (count < 0 || count > m_SectorIds.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            var result = new int[count];
            for (int i = 0; i < count; i++)
                result[i] = m_SectorIds[i];
            return result;
        }

        public int[] CopyConvertedPortals()
        {
            RequireCreated();
            return m_PortalIds.AsArray().ToArray();
        }

        public FlowPathKernelRouteConversionStatus AdvanceConversion(int startSectorId)
        {
            RequireCreated();
            new AdvanceRouteConversionJob
            {
                StartSectorId = startSectorId,
                L0Nodes = m_L0Nodes,
                SectorIds = m_SectorIds,
                PortalIds = m_PortalIds,
                ConvertCursor = m_ConvertCursor,
                Status = m_ConversionStatus,
                SectorAuthorityHash = m_SectorAuthorityHash,
                PortalAuthorityHash = m_PortalAuthorityHash,
                Error = m_Error
            }.Run();
            ThrowOperationError("conversion");
            return (FlowPathKernelRouteConversionStatus)m_ConversionStatus.Value;
        }

        public void AdvanceTraversal(
            FlowPathKernelSearchState search,
            int expandPairTaskType,
            int successorCount)
        {
            RequireCreated();
            if (search == null || !search.IsCreated)
                throw new InvalidOperationException("Flow path route traversal requires a live search authority.");
            new AdvanceRouteTraversalJob
            {
                ExpandPairTaskType = expandPairTaskType,
                SuccessorCount = successorCount,
                Previous = search.PreviousMap,
                Tasks = m_Tasks,
                L0Nodes = m_L0Nodes,
                LastLogicalNode = m_LastLogicalNode,
                TaskAuthorityHash = m_TaskAuthorityHash,
                L0AuthorityHash = m_L0AuthorityHash,
                Error = m_Error
            }.Run();
            ThrowOperationError("traversal");
        }

        public void AdvanceWitnessTraversal(
            FlowPathKernelWitnessIndex witnessIndex,
            int expandPairTaskType)
        {
            RequireCreated();
            if (witnessIndex == null || !witnessIndex.IsCreated)
                throw new InvalidOperationException("Flow path route witness traversal requires a live index.");
            new AdvanceWitnessTraversalJob
            {
                ExpandPairTaskType = expandPairTaskType,
                WitnessNodes = witnessIndex.WitnessNodes,
                Tasks = m_Tasks,
                L0Nodes = m_L0Nodes,
                LastLogicalNode = m_LastLogicalNode,
                TaskAuthorityHash = m_TaskAuthorityHash,
                L0AuthorityHash = m_L0AuthorityHash,
                Error = m_Error
            }.Run();
            ThrowOperationError("witness traversal");
        }

        public void AdvancePair(
            FlowPathKernelWitnessIndex witnessIndex,
            bool directPortalCrossing,
            int edgeLevelIndex,
            int clusterId,
            int traverseWitnessTaskType)
        {
            RequireCreated();
            if (witnessIndex == null || !witnessIndex.IsCreated)
                throw new InvalidOperationException("Flow path route pair expansion requires a live witness index.");
            new AdvanceRoutePairJob
            {
                DirectPortalCrossing = directPortalCrossing ? 1 : 0,
                EdgeLevelIndex = edgeLevelIndex,
                ClusterId = clusterId,
                TraverseWitnessTaskType = traverseWitnessTaskType,
                Edges = witnessIndex.Edges,
                Tasks = m_Tasks,
                L0Nodes = m_L0Nodes,
                TaskAuthorityHash = m_TaskAuthorityHash,
                L0AuthorityHash = m_L0AuthorityHash,
                Error = m_Error
            }.Run();
            ThrowOperationError("pair expansion");
        }

        public FlowPathKernelRouteSliceResult AdvanceSlice(
            FlowPathKernelSearchState search,
            FlowPathKernelWitnessIndex witnessIndex,
            FlowPathKernelRouteMergeIndex mergeIndex,
            int searchAuthoritySlot,
            int hierarchyTraversalTaskType,
            int l0TraversalTaskType,
            int downwardTraversalTaskType,
            int connectorTraversalTaskType,
            int hierarchyPairTaskType,
            int pairTaskType,
            int witnessTraversalTaskType,
            int connectorBoundaryTaskType,
            int operationQuota)
        {
            RequireCreated();
            if (search == null || !search.IsCreated)
                throw new InvalidOperationException("Flow path route slice requires a live search authority.");
            if (witnessIndex == null || !witnessIndex.IsCreated)
                throw new InvalidOperationException("Flow path route slice requires a live witness index.");
            if (mergeIndex == null || !mergeIndex.IsCreated)
                throw new InvalidOperationException("Flow path route slice requires a live merge index.");
            if (operationQuota <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationQuota));

            ScheduleSlice(
                search,
                witnessIndex,
                mergeIndex,
                searchAuthoritySlot,
                hierarchyTraversalTaskType,
                l0TraversalTaskType,
                downwardTraversalTaskType,
                connectorTraversalTaskType,
                hierarchyPairTaskType,
                pairTaskType,
                witnessTraversalTaskType,
                connectorBoundaryTaskType,
                operationQuota);
            return CompleteScheduledSlice();
        }

        public void ScheduleSlice(
            FlowPathKernelSearchState search,
            FlowPathKernelWitnessIndex witnessIndex,
            FlowPathKernelRouteMergeIndex mergeIndex,
            int searchAuthoritySlot,
            int hierarchyTraversalTaskType,
            int l0TraversalTaskType,
            int downwardTraversalTaskType,
            int connectorTraversalTaskType,
            int hierarchyPairTaskType,
            int pairTaskType,
            int witnessTraversalTaskType,
            int connectorBoundaryTaskType,
            int operationQuota)
        {
            RequireCreated();
            if (m_HasPendingSlice)
                throw new InvalidOperationException("Flow path route slice is already scheduled.");
            if (search == null || !search.IsCreated)
                throw new InvalidOperationException("Flow path route slice requires a live search authority.");
            if (search.HasPendingGraphSlice)
                throw new InvalidOperationException("Flow path route slice cannot overlap a pending graph slice job.");
            if (witnessIndex == null || !witnessIndex.IsCreated)
                throw new InvalidOperationException("Flow path route slice requires a live witness index.");
            if (mergeIndex == null || !mergeIndex.IsCreated)
                throw new InvalidOperationException("Flow path route slice requires a live merge index.");
            if (operationQuota <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationQuota));

            m_PendingSlice = new AdvanceRouteSliceJob
            {
                SearchAuthoritySlot = searchAuthoritySlot,
                HierarchyTraversalTaskType = hierarchyTraversalTaskType,
                L0TraversalTaskType = l0TraversalTaskType,
                DownwardTraversalTaskType = downwardTraversalTaskType,
                ConnectorTraversalTaskType = connectorTraversalTaskType,
                HierarchyPairTaskType = hierarchyPairTaskType,
                PairTaskType = pairTaskType,
                WitnessTraversalTaskType = witnessTraversalTaskType,
                ConnectorBoundaryTaskType = connectorBoundaryTaskType,
                OperationQuota = operationQuota,
                SuccessorCount = search.PreviousCount,
                Previous = search.PreviousMap,
                RouteEdges = witnessIndex.RouteEdges,
                WitnessNodes = witnessIndex.WitnessNodes,
                MergeNodes = mergeIndex.Nodes,
                Tasks = m_Tasks,
                L0Nodes = m_L0Nodes,
                LastLogicalNode = m_LastLogicalNode,
                TaskAuthorityHash = m_TaskAuthorityHash,
                L0AuthorityHash = m_L0AuthorityHash,
                OperationCount = m_SliceOperationCount,
                StopReason = m_SliceStopReason,
                Error = m_Error
            }.Schedule();
            m_HasPendingSlice = true;
            // Flush the worker queue at the hand-off boundary. This lets a
            // short slice finish during the same logic update when available,
            // while preserving the cross-tick pending state for longer work.
            JobHandle.ScheduleBatchedJobs();
        }

        public FlowPathKernelRouteSliceResult CompleteScheduledSlice()
        {
            RequireCreated();
            if (!m_HasPendingSlice)
                throw new InvalidOperationException("Flow path route slice has no scheduled job.");
            m_PendingSlice.Complete();
            m_HasPendingSlice = false;
            ThrowOperationError("route slice");
            return new FlowPathKernelRouteSliceResult(
                m_SliceOperationCount.Value,
                (FlowPathKernelRouteSliceStopReason)m_SliceStopReason.Value);
        }

        public bool TryCompleteScheduledSlice(out FlowPathKernelRouteSliceResult result)
        {
            RequireCreated();
            result = default(FlowPathKernelRouteSliceResult);
            if (!m_HasPendingSlice || !m_PendingSlice.IsCompleted)
                return false;
            result = CompleteScheduledSlice();
            return true;
        }

        public FlowPathKernelRouteSliceResult AdvanceConversionSlice(int startSectorId, int operationQuota)
        {
            RequireCreated();
            if (operationQuota <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationQuota));
            new AdvanceRouteConversionSliceJob
            {
                StartSectorId = startSectorId,
                OperationQuota = operationQuota,
                L0Nodes = m_L0Nodes,
                SectorIds = m_SectorIds,
                PortalIds = m_PortalIds,
                ConvertCursor = m_ConvertCursor,
                Status = m_ConversionStatus,
                SectorAuthorityHash = m_SectorAuthorityHash,
                PortalAuthorityHash = m_PortalAuthorityHash,
                OperationCount = m_SliceOperationCount,
                StopReason = m_SliceStopReason,
                Error = m_Error
            }.Run();
            ThrowOperationError("conversion slice");
            return new FlowPathKernelRouteSliceResult(
                m_SliceOperationCount.Value,
                (FlowPathKernelRouteSliceStopReason)m_SliceStopReason.Value);
        }

        public bool ValidateAuthorityHashes()
        {
            RequireCreated();
            ulong taskHash = 0UL;
            for (int i = 0; i < m_Tasks.Length; i++)
                taskHash ^= TaskToken(i, m_Tasks[i]);
            ulong nodeHash = 0UL;
            for (int i = 0; i < m_L0Nodes.Length; i++)
                nodeHash ^= NodeSequenceToken(i, m_L0Nodes[i]);
            ulong sectorHash = 0UL;
            for (int i = 0; i < m_SectorIds.Length; i++)
                sectorHash ^= SectorSequenceToken(i, m_SectorIds[i]);
            ulong portalHash = 0UL;
            for (int i = 0; i < m_PortalIds.Length; i++)
                portalHash ^= PortalSequenceToken(i, m_PortalIds[i]);
            return taskHash == m_TaskAuthorityHash.Value
                   && nodeHash == m_L0AuthorityHash.Value
                   && sectorHash == m_SectorAuthorityHash.Value
                   && portalHash == m_PortalAuthorityHash.Value;
        }

        public void Dispose()
        {
            if (m_HasPendingSlice)
            {
                m_PendingSlice.Complete();
                m_HasPendingSlice = false;
            }
            if (m_Tasks.IsCreated)
                m_Tasks.Dispose();
            if (m_L0Nodes.IsCreated)
                m_L0Nodes.Dispose();
            if (m_SectorIds.IsCreated)
                m_SectorIds.Dispose();
            if (m_PortalIds.IsCreated)
                m_PortalIds.Dispose();
            if (m_LastLogicalNode.IsCreated)
                m_LastLogicalNode.Dispose();
            if (m_ConvertCursor.IsCreated)
                m_ConvertCursor.Dispose();
            if (m_ConversionStatus.IsCreated)
                m_ConversionStatus.Dispose();
            if (m_TaskAuthorityHash.IsCreated)
                m_TaskAuthorityHash.Dispose();
            if (m_L0AuthorityHash.IsCreated)
                m_L0AuthorityHash.Dispose();
            if (m_SectorAuthorityHash.IsCreated)
                m_SectorAuthorityHash.Dispose();
            if (m_PortalAuthorityHash.IsCreated)
                m_PortalAuthorityHash.Dispose();
            if (m_SliceOperationCount.IsCreated)
                m_SliceOperationCount.Dispose();
            if (m_SliceStopReason.IsCreated)
                m_SliceStopReason.Dispose();
            if (m_Error.IsCreated)
                m_Error.Dispose();
        }

        private void RequireCreated()
        {
            if (!m_Tasks.IsCreated
                || !m_L0Nodes.IsCreated
                || !m_SectorIds.IsCreated
                || !m_PortalIds.IsCreated
                || !m_LastLogicalNode.IsCreated
                || !m_ConvertCursor.IsCreated
                || !m_ConversionStatus.IsCreated
                || !m_TaskAuthorityHash.IsCreated
                || !m_L0AuthorityHash.IsCreated
                || !m_SectorAuthorityHash.IsCreated
                || !m_PortalAuthorityHash.IsCreated
                || !m_SliceOperationCount.IsCreated
                || !m_SliceStopReason.IsCreated
                || !m_Error.IsCreated)
            {
                throw new ObjectDisposedException(nameof(FlowPathKernelRouteState));
            }
        }

        private void ThrowOperationError(string operation)
        {
            int error = m_Error.Value;
            if (error == 0)
                return;
            throw new InvalidOperationException($"Flow path route {operation} failed with error={error}.");
        }

        private static ulong RouteMix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ value >> 31;
        }

        private static ulong TaskToken(int index, FlowPathKernelRouteTask task)
        {
            ulong hash = RouteMix((uint)index ^ 0x52544B534BUL);
            hash = RouteMix(hash ^ (uint)task.Type);
            hash = RouteMix(hash ^ (uint)task.AuthoritySlot);
            hash = RouteMix(hash ^ (uint)task.Cursor);
            hash = RouteMix(hash ^ (uint)task.CurrentNode);
            hash = RouteMix(hash ^ (uint)task.FromNode);
            hash = RouteMix(hash ^ (uint)task.ToNode);
            hash = RouteMix(hash ^ (uint)task.LevelIndex);
            hash = RouteMix(hash ^ (uint)task.Guard);
            hash = RouteMix(hash ^ (uint)task.Started);
            hash = RouteMix(hash ^ (uint)task.WitnessOffset);
            return RouteMix(hash ^ (uint)task.WitnessCount);
        }

        private static ulong NodeSequenceToken(int index, int node)
        {
            return RouteMix(unchecked((ulong)(uint)index << 32) ^ (uint)node ^ 0x4C304E4F4445UL);
        }

        private static ulong SectorSequenceToken(int index, int sectorId)
        {
            return RouteMix(unchecked((ulong)(uint)index << 32) ^ (uint)sectorId ^ 0x534543544F52UL);
        }

        private static ulong PortalSequenceToken(int index, int portalId)
        {
            return RouteMix(unchecked((ulong)(uint)index << 32) ^ (uint)portalId ^ 0x504F5254414CUL);
        }

        private static void PushTask(
            ref NativeList<FlowPathKernelRouteTask> tasks,
            ref ulong authorityHash,
            FlowPathKernelRouteTask task)
        {
            int index = tasks.Length;
            tasks.Add(task);
            authorityHash ^= TaskToken(index, task);
        }

        private static FlowPathKernelRouteTask PopTask(
            ref NativeList<FlowPathKernelRouteTask> tasks,
            ref ulong authorityHash)
        {
            int index = tasks.Length - 1;
            FlowPathKernelRouteTask task = tasks[index];
            authorityHash ^= TaskToken(index, task);
            tasks.RemoveAt(index);
            return task;
        }

        private static bool AppendNode(
            ref NativeList<int> nodes,
            ref ulong authorityHash,
            int node)
        {
            if (nodes.Length > 0 && nodes[nodes.Length - 1] == node)
                return false;
            int index = nodes.Length;
            nodes.Add(node);
            authorityHash ^= NodeSequenceToken(index, node);
            return true;
        }

        private static int CompareEdge(
            FlowPathKernelWitnessEdge edge,
            int levelIndex,
            int clusterId,
            int fromNode,
            int toNode)
        {
            int order = edge.LevelIndex.CompareTo(levelIndex);
            if (order != 0) return order;
            order = edge.ClusterId.CompareTo(clusterId);
            if (order != 0) return order;
            order = edge.FromNode.CompareTo(fromNode);
            return order != 0 ? order : edge.ToNode.CompareTo(toNode);
        }

        private static int CompareRouteEdge(
            FlowPathKernelWitnessEdge edge,
            int levelIndex,
            int fromNode,
            int toNode)
        {
            int order = edge.LevelIndex.CompareTo(levelIndex);
            if (order != 0)
                return order;
            order = edge.FromNode.CompareTo(fromNode);
            return order != 0 ? order : edge.ToNode.CompareTo(toNode);
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceRouteSliceJob : IJob
        {
            public int SearchAuthoritySlot;
            public int HierarchyTraversalTaskType;
            public int L0TraversalTaskType;
            public int DownwardTraversalTaskType;
            public int ConnectorTraversalTaskType;
            public int HierarchyPairTaskType;
            public int PairTaskType;
            public int WitnessTraversalTaskType;
            public int ConnectorBoundaryTaskType;
            public int OperationQuota;
            public int SuccessorCount;
            [ReadOnly] public NativeParallelHashMap<int, int> Previous;
            [ReadOnly] public NativeArray<FlowPathKernelWitnessEdge> RouteEdges;
            [ReadOnly] public NativeArray<int> WitnessNodes;
            [ReadOnly] public NativeParallelHashSet<int> MergeNodes;
            public NativeList<FlowPathKernelRouteTask> Tasks;
            public NativeList<int> L0Nodes;
            public NativeReference<int> LastLogicalNode;
            public NativeReference<ulong> TaskAuthorityHash;
            public NativeReference<ulong> L0AuthorityHash;
            public NativeReference<int> OperationCount;
            public NativeReference<int> StopReason;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                OperationCount.Value = 0;
                StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.QuotaExhausted;
                ulong taskHash = TaskAuthorityHash.Value;
                ulong nodeHash = L0AuthorityHash.Value;

                while (OperationCount.Value < OperationQuota)
                {
                    if (Tasks.Length == 0)
                    {
                        StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.RouteComplete;
                        break;
                    }

                    FlowPathKernelRouteTask current = Tasks[Tasks.Length - 1];
                    if (current.Type == ConnectorBoundaryTaskType)
                    {
                        StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.ConnectorBoundary;
                        break;
                    }

                    bool traversal = current.Type == HierarchyTraversalTaskType
                                     || current.Type == L0TraversalTaskType
                                     || current.Type == DownwardTraversalTaskType
                                     || current.Type == ConnectorTraversalTaskType;
                    if (traversal)
                    {
                        if (current.AuthoritySlot != SearchAuthoritySlot)
                        {
                            StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.SearchAuthorityBoundary;
                            break;
                        }
                        FlowPathKernelRouteTask task = PopTask(ref Tasks, ref taskHash);
                        bool reachedMergeNode = false;
                        if (task.Started == 0)
                        {
                            reachedMergeNode = AppendNode(ref L0Nodes, ref nodeHash, task.CurrentNode)
                                               && IsMergeNode(task.CurrentNode);
                            LastLogicalNode.Value = task.CurrentNode;
                            task.Started = 1;
                            PushTask(ref Tasks, ref taskHash, task);
                        }
                        else if (Previous.TryGetValue(task.CurrentNode, out int nextNode))
                        {
                            task.Guard++;
                            if (task.Guard > SuccessorCount)
                            {
                                Error.Value = 2;
                                break;
                            }
                            int fromNode = task.CurrentNode;
                            task.CurrentNode = nextNode;
                            LastLogicalNode.Value = nextNode;
                            PushTask(ref Tasks, ref taskHash, task);
                            PushTask(ref Tasks, ref taskHash, new FlowPathKernelRouteTask
                            {
                                Type = task.Type == HierarchyTraversalTaskType ? HierarchyPairTaskType : PairTaskType,
                                FromNode = fromNode,
                                ToNode = nextNode,
                                LevelIndex = task.LevelIndex
                            });
                        }
                        OperationCount.Value++;
                        if (reachedMergeNode)
                        {
                            StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.RouteNodeBoundary;
                            break;
                        }
                        continue;
                    }

                    if (current.Type == WitnessTraversalTaskType)
                    {
                        FlowPathKernelRouteTask task = PopTask(ref Tasks, ref taskHash);
                        if (task.WitnessCount <= 0
                            || task.WitnessOffset < 0
                            || task.WitnessOffset + task.WitnessCount > WitnessNodes.Length)
                        {
                            Error.Value = 3;
                            break;
                        }
                        bool reachedMergeNode = false;
                        if (task.Started == 0)
                        {
                            int first = WitnessNodes[task.WitnessOffset];
                            reachedMergeNode = AppendNode(ref L0Nodes, ref nodeHash, first)
                                               && IsMergeNode(first);
                            LastLogicalNode.Value = first;
                            task.Started = 1;
                            task.Cursor = 0;
                            PushTask(ref Tasks, ref taskHash, task);
                        }
                        else if (task.Cursor + 1 < task.WitnessCount)
                        {
                            int fromNode = WitnessNodes[task.WitnessOffset + task.Cursor];
                            task.Cursor++;
                            int toNode = WitnessNodes[task.WitnessOffset + task.Cursor];
                            LastLogicalNode.Value = toNode;
                            PushTask(ref Tasks, ref taskHash, task);
                            PushTask(ref Tasks, ref taskHash, new FlowPathKernelRouteTask
                            {
                                Type = PairTaskType,
                                FromNode = fromNode,
                                ToNode = toNode,
                                LevelIndex = task.LevelIndex
                            });
                        }
                        OperationCount.Value++;
                        if (reachedMergeNode)
                        {
                            StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.RouteNodeBoundary;
                            break;
                        }
                        continue;
                    }

                    if (current.Type == HierarchyPairTaskType || current.Type == PairTaskType)
                    {
                        FlowPathKernelRouteTask task = PopTask(ref Tasks, ref taskHash);
                        int fromSectorId = task.FromNode >> 16;
                        int fromPortalId = task.FromNode & 0xFFFF;
                        int toSectorId = task.ToNode >> 16;
                        int toPortalId = task.ToNode & 0xFFFF;
                        bool directPortalCrossing = fromPortalId == toPortalId && fromSectorId != toSectorId;
                        int edgeLevelIndex = task.Type == HierarchyPairTaskType
                            ? task.LevelIndex
                            : task.LevelIndex <= 0 ? -1 : task.LevelIndex - 1;
                        bool reachedMergeNode = false;
                        if (directPortalCrossing || edgeLevelIndex < 0)
                        {
                            reachedMergeNode = AppendNode(ref L0Nodes, ref nodeHash, task.ToNode)
                                               && IsMergeNode(task.ToNode);
                        }
                        else
                        {
                            int low = 0;
                            int high = RouteEdges.Length - 1;
                            int found = -1;
                            while (low <= high)
                            {
                                int middle = low + ((high - low) >> 1);
                                int order = CompareRouteEdge(
                                    RouteEdges[middle],
                                    edgeLevelIndex,
                                    task.FromNode,
                                    task.ToNode);
                                if (order < 0)
                                    low = middle + 1;
                                else if (order > 0)
                                    high = middle - 1;
                                else
                                {
                                    found = middle;
                                    break;
                                }
                            }
                            if (found < 0)
                            {
                                Error.Value = 4;
                                break;
                            }
                            FlowPathKernelWitnessEdge edge = RouteEdges[found];
                            PushTask(ref Tasks, ref taskHash, new FlowPathKernelRouteTask
                            {
                                Type = WitnessTraversalTaskType,
                                LevelIndex = edgeLevelIndex,
                                WitnessOffset = edge.WitnessOffset,
                                WitnessCount = edge.WitnessCount
                            });
                        }
                        OperationCount.Value++;
                        if (reachedMergeNode)
                        {
                            StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.RouteNodeBoundary;
                            break;
                        }
                        continue;
                    }

                    Error.Value = 6;
                    break;
                }

                TaskAuthorityHash.Value = taskHash;
                L0AuthorityHash.Value = nodeHash;
            }

            private bool IsMergeNode(int node)
            {
                return MergeNodes.IsCreated && MergeNodes.Contains(node);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceRouteConversionSliceJob : IJob
        {
            public int StartSectorId;
            public int OperationQuota;
            [ReadOnly] public NativeList<int> L0Nodes;
            public NativeList<int> SectorIds;
            public NativeList<int> PortalIds;
            public NativeReference<int> ConvertCursor;
            public NativeReference<int> Status;
            public NativeReference<ulong> SectorAuthorityHash;
            public NativeReference<ulong> PortalAuthorityHash;
            public NativeReference<int> OperationCount;
            public NativeReference<int> StopReason;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                OperationCount.Value = 0;
                StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.QuotaExhausted;
                if (L0Nodes.Length == 0)
                {
                    Error.Value = 5;
                    return;
                }
                while (OperationCount.Value < OperationQuota)
                {
                    if (SectorIds.Length == 0)
                    {
                        int index = SectorIds.Length;
                        SectorIds.Add(StartSectorId);
                        SectorAuthorityHash.Value ^= SectorSequenceToken(index, StartSectorId);
                        Status.Value = (int)FlowPathKernelRouteConversionStatus.Initialized;
                        OperationCount.Value++;
                        continue;
                    }
                    int cursor = ConvertCursor.Value;
                    if (cursor + 1 >= L0Nodes.Length)
                    {
                        Status.Value = (int)FlowPathKernelRouteConversionStatus.Complete;
                        StopReason.Value = (int)FlowPathKernelRouteSliceStopReason.RouteComplete;
                        OperationCount.Value++;
                        break;
                    }
                    int fromNode = L0Nodes[cursor];
                    cursor++;
                    int toNode = L0Nodes[cursor];
                    ConvertCursor.Value = cursor;
                    int fromSectorId = fromNode >> 16;
                    int fromPortalId = fromNode & 0xFFFF;
                    int toSectorId = toNode >> 16;
                    int toPortalId = toNode & 0xFFFF;
                    if (fromPortalId == toPortalId && fromSectorId != toSectorId)
                    {
                        int portalIndex = PortalIds.Length;
                        PortalIds.Add(fromPortalId);
                        PortalAuthorityHash.Value ^= PortalSequenceToken(portalIndex, fromPortalId);
                        int sectorIndex = SectorIds.Length;
                        SectorIds.Add(toSectorId);
                        SectorAuthorityHash.Value ^= SectorSequenceToken(sectorIndex, toSectorId);
                    }
                    Status.Value = (int)FlowPathKernelRouteConversionStatus.Advanced;
                    OperationCount.Value++;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PushRouteTaskJob : IJob
        {
            public FlowPathKernelRouteTask Task;
            public NativeList<FlowPathKernelRouteTask> Tasks;
            public NativeReference<ulong> TaskAuthorityHash;

            public void Execute()
            {
                ulong hash = TaskAuthorityHash.Value;
                PushTask(ref Tasks, ref hash, Task);
                TaskAuthorityHash.Value = hash;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceRouteTraversalJob : IJob
        {
            public int ExpandPairTaskType;
            public int SuccessorCount;
            [ReadOnly] public NativeParallelHashMap<int, int> Previous;
            public NativeList<FlowPathKernelRouteTask> Tasks;
            public NativeList<int> L0Nodes;
            public NativeReference<int> LastLogicalNode;
            public NativeReference<ulong> TaskAuthorityHash;
            public NativeReference<ulong> L0AuthorityHash;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                if (Tasks.Length == 0)
                {
                    Error.Value = 1;
                    return;
                }
                ulong taskHash = TaskAuthorityHash.Value;
                ulong nodeHash = L0AuthorityHash.Value;
                FlowPathKernelRouteTask task = PopTask(ref Tasks, ref taskHash);
                if (task.Started == 0)
                {
                    AppendNode(ref L0Nodes, ref nodeHash, task.CurrentNode);
                    LastLogicalNode.Value = task.CurrentNode;
                    task.Started = 1;
                    PushTask(ref Tasks, ref taskHash, task);
                }
                else if (Previous.TryGetValue(task.CurrentNode, out int nextNode))
                {
                    task.Guard++;
                    if (task.Guard > SuccessorCount)
                    {
                        Error.Value = 2;
                        return;
                    }
                    int fromNode = task.CurrentNode;
                    task.CurrentNode = nextNode;
                    LastLogicalNode.Value = nextNode;
                    PushTask(ref Tasks, ref taskHash, task);
                    PushTask(ref Tasks, ref taskHash, new FlowPathKernelRouteTask
                    {
                        Type = ExpandPairTaskType,
                        FromNode = fromNode,
                        ToNode = nextNode,
                        LevelIndex = task.LevelIndex
                    });
                }
                TaskAuthorityHash.Value = taskHash;
                L0AuthorityHash.Value = nodeHash;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceWitnessTraversalJob : IJob
        {
            public int ExpandPairTaskType;
            [ReadOnly] public NativeArray<int> WitnessNodes;
            public NativeList<FlowPathKernelRouteTask> Tasks;
            public NativeList<int> L0Nodes;
            public NativeReference<int> LastLogicalNode;
            public NativeReference<ulong> TaskAuthorityHash;
            public NativeReference<ulong> L0AuthorityHash;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                if (Tasks.Length == 0)
                {
                    Error.Value = 1;
                    return;
                }
                ulong taskHash = TaskAuthorityHash.Value;
                ulong nodeHash = L0AuthorityHash.Value;
                FlowPathKernelRouteTask task = PopTask(ref Tasks, ref taskHash);
                if (task.WitnessCount <= 0
                    || task.WitnessOffset < 0
                    || task.WitnessOffset + task.WitnessCount > WitnessNodes.Length)
                {
                    Error.Value = 3;
                    return;
                }
                if (task.Started == 0)
                {
                    int first = WitnessNodes[task.WitnessOffset];
                    AppendNode(ref L0Nodes, ref nodeHash, first);
                    LastLogicalNode.Value = first;
                    task.Started = 1;
                    task.Cursor = 0;
                    PushTask(ref Tasks, ref taskHash, task);
                }
                else if (task.Cursor + 1 < task.WitnessCount)
                {
                    int fromNode = WitnessNodes[task.WitnessOffset + task.Cursor];
                    task.Cursor++;
                    int toNode = WitnessNodes[task.WitnessOffset + task.Cursor];
                    LastLogicalNode.Value = toNode;
                    PushTask(ref Tasks, ref taskHash, task);
                    PushTask(ref Tasks, ref taskHash, new FlowPathKernelRouteTask
                    {
                        Type = ExpandPairTaskType,
                        FromNode = fromNode,
                        ToNode = toNode,
                        LevelIndex = task.LevelIndex
                    });
                }
                TaskAuthorityHash.Value = taskHash;
                L0AuthorityHash.Value = nodeHash;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceRouteConversionJob : IJob
        {
            public int StartSectorId;
            [ReadOnly] public NativeList<int> L0Nodes;
            public NativeList<int> SectorIds;
            public NativeList<int> PortalIds;
            public NativeReference<int> ConvertCursor;
            public NativeReference<int> Status;
            public NativeReference<ulong> SectorAuthorityHash;
            public NativeReference<ulong> PortalAuthorityHash;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                if (L0Nodes.Length == 0)
                {
                    Error.Value = 5;
                    return;
                }
                if (SectorIds.Length == 0)
                {
                    int index = SectorIds.Length;
                    SectorIds.Add(StartSectorId);
                    SectorAuthorityHash.Value ^= SectorSequenceToken(index, StartSectorId);
                    Status.Value = (int)FlowPathKernelRouteConversionStatus.Initialized;
                    return;
                }
                int cursor = ConvertCursor.Value;
                if (cursor + 1 < L0Nodes.Length)
                {
                    int fromNode = L0Nodes[cursor];
                    cursor++;
                    int toNode = L0Nodes[cursor];
                    ConvertCursor.Value = cursor;
                    int fromSectorId = fromNode >> 16;
                    int fromPortalId = fromNode & 0xFFFF;
                    int toSectorId = toNode >> 16;
                    int toPortalId = toNode & 0xFFFF;
                    if (fromPortalId == toPortalId && fromSectorId != toSectorId)
                    {
                        int portalIndex = PortalIds.Length;
                        PortalIds.Add(fromPortalId);
                        PortalAuthorityHash.Value ^= PortalSequenceToken(portalIndex, fromPortalId);
                        int sectorIndex = SectorIds.Length;
                        SectorIds.Add(toSectorId);
                        SectorAuthorityHash.Value ^= SectorSequenceToken(sectorIndex, toSectorId);
                    }
                    Status.Value = (int)FlowPathKernelRouteConversionStatus.Advanced;
                    return;
                }
                Status.Value = (int)FlowPathKernelRouteConversionStatus.Complete;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AdvanceRoutePairJob : IJob
        {
            public int DirectPortalCrossing;
            public int EdgeLevelIndex;
            public int ClusterId;
            public int TraverseWitnessTaskType;
            [ReadOnly] public NativeArray<FlowPathKernelWitnessEdge> Edges;
            public NativeList<FlowPathKernelRouteTask> Tasks;
            public NativeList<int> L0Nodes;
            public NativeReference<ulong> TaskAuthorityHash;
            public NativeReference<ulong> L0AuthorityHash;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                if (Tasks.Length == 0)
                {
                    Error.Value = 1;
                    return;
                }
                ulong taskHash = TaskAuthorityHash.Value;
                ulong nodeHash = L0AuthorityHash.Value;
                FlowPathKernelRouteTask task = PopTask(ref Tasks, ref taskHash);
                if (DirectPortalCrossing != 0 || EdgeLevelIndex < 0)
                {
                    AppendNode(ref L0Nodes, ref nodeHash, task.ToNode);
                }
                else
                {
                    int low = 0;
                    int high = Edges.Length - 1;
                    int found = -1;
                    while (low <= high)
                    {
                        int middle = low + ((high - low) >> 1);
                        int order = CompareEdge(
                            Edges[middle],
                            EdgeLevelIndex,
                            ClusterId,
                            task.FromNode,
                            task.ToNode);
                        if (order < 0)
                            low = middle + 1;
                        else if (order > 0)
                            high = middle - 1;
                        else
                        {
                            found = middle;
                            break;
                        }
                    }
                    if (found < 0)
                    {
                        Error.Value = 4;
                        return;
                    }
                    FlowPathKernelWitnessEdge edge = Edges[found];
                    PushTask(ref Tasks, ref taskHash, new FlowPathKernelRouteTask
                    {
                        Type = TraverseWitnessTaskType,
                        LevelIndex = EdgeLevelIndex,
                        WitnessOffset = edge.WitnessOffset,
                        WitnessCount = edge.WitnessCount
                    });
                }
                TaskAuthorityHash.Value = taskHash;
                L0AuthorityHash.Value = nodeHash;
            }
        }
    }

    public static class FlowPathKernelRuntime
    {
        private const int PreparationHierarchyTraversalType = 101;
        private const int PreparationHierarchyPairType = 102;
        private const int PreparationWitnessTraversalType = 103;
        private const int PreparationL0PairType = 104;
        private const int PreparationL0TraversalType = 105;
        private const int PreparationDownwardTraversalType = 106;
        private const int PreparationConnectorTraversalType = 107;
        private const int PreparationConnectorBoundaryType = 108;
        private static bool s_IsPrepared;

        public static bool IsPrepared => s_IsPrepared;

        public static void Prepare()
        {
            if (s_IsPrepared)
                return;

            PrepareSearchKernel();
            PrepareRouteKernel();
            s_IsPrepared = true;
        }

        private static void PrepareSearchKernel()
        {
            var edges = new[]
            {
                new FlowPathKernelGraphEdge(0, 1, 2L),
                new FlowPathKernelGraphEdge(1, 2, 1L)
            };
            using (var graph = new FlowPathKernelGraphIndex(edges))
            using (var search = new FlowPathKernelSearchState(8))
            {
                search.BeginCommandSlice();
                search.AppendAddSource(0, 0L);
                int commandCount = search.ExecuteCommandSlice(out FlowPathKernelPopStatus popStatus, out _);
                if (commandCount != 1 || popStatus != FlowPathKernelPopStatus.Empty)
                    throw new InvalidOperationException("Flow path production search preparation command slice failed.");

                search.SetGraphSliceTargets(new[] { 2 });
                var cursor = new FlowPathKernelGraphCursor();
                for (int slice = 0; slice < 8 && !search.ContainsSettled(2); slice++)
                {
                    FlowPathKernelGraphSliceResult result = search.AdvanceGraphSlice(
                        graph,
                        reverse: false,
                        sectorCountX: 1,
                        allowedStartSectorX: 0,
                        allowedStartSectorY: 0,
                        allowedWidthSectors: 0,
                        allowedHeightSectors: 0,
                        cursor,
                        operationQuota: 8);
                    if (result.OperationCount <= 0
                        || result.StopReason == FlowPathKernelGraphSliceStopReason.FrontierEmpty)
                    {
                        throw new InvalidOperationException("Flow path production graph slice preparation failed.");
                    }
                    cursor = result.Cursor;
                }

                if (!search.ContainsSettled(2)
                    || !search.TryGetCost(2, out long cost)
                    || cost != 3L)
                {
                    throw new InvalidOperationException("Flow path production graph slice preparation produced an invalid route cost.");
                }

                search.ShiftCosts(1L);
                if (!search.TryGetCost(2, out cost)
                    || cost != 4L
                    || !search.ValidateAuthorityHashes())
                {
                    throw new InvalidOperationException("Flow path production search preparation authority validation failed.");
                }
            }
        }

        private static void PrepareRouteKernel()
        {
            int highStart = EncodePreparationPortalNode(0, 10);
            int highGoal = EncodePreparationPortalNode(2, 20);
            int[] witnessNodes =
            {
                highStart,
                EncodePreparationPortalNode(1, 10),
                EncodePreparationPortalNode(1, 20),
                highGoal
            };
            using (var search = new FlowPathKernelSearchState(8))
            using (var witnessIndex = new FlowPathKernelWitnessIndex(
                       new[]
                       {
                           new FlowPathKernelWitnessEdge(
                               0,
                               0,
                               highStart,
                               highGoal,
                               0,
                               witnessNodes.Length)
                       },
                       witnessNodes))
            using (var mergeIndex = new FlowPathKernelRouteMergeIndex(1))
            using (var route = new FlowPathKernelRouteState(8, 8))
            {
                search.AddSource(highGoal, 0L);
                if (search.PopOne(out FlowPathKernelSearchEntry goal) != FlowPathKernelPopStatus.Settled)
                    throw new InvalidOperationException("Flow path production route preparation failed to settle its goal.");
                search.Relax(goal.Node, highStart, 1L);
                route.Push(new FlowPathKernelRouteTask
                {
                    Type = PreparationHierarchyTraversalType,
                    AuthoritySlot = 0,
                    CurrentNode = highStart,
                    LevelIndex = 0
                });

                bool complete = false;
                for (int slice = 0; slice < 8 && !complete; slice++)
                {
                    FlowPathKernelRouteSliceResult result;
                    if (route.TaskCount > 0)
                    {
                        result = route.AdvanceSlice(
                            search,
                            witnessIndex,
                            mergeIndex,
                            0,
                            PreparationHierarchyTraversalType,
                            PreparationL0TraversalType,
                            PreparationDownwardTraversalType,
                            PreparationConnectorTraversalType,
                            PreparationHierarchyPairType,
                            PreparationL0PairType,
                            PreparationWitnessTraversalType,
                            PreparationConnectorBoundaryType,
                            64);
                    }
                    else
                    {
                        result = route.AdvanceConversionSlice(0, 64);
                        complete = result.StopReason == FlowPathKernelRouteSliceStopReason.RouteComplete;
                    }
                    if (result.OperationCount <= 0)
                        throw new InvalidOperationException("Flow path production route slice preparation made no progress.");
                }

                if (!complete
                    || route.ConvertedSectorCount != 3
                    || route.ConvertedPortalCount != 2
                    || route.GetConvertedSector(0) != 0
                    || route.GetConvertedSector(1) != 1
                    || route.GetConvertedSector(2) != 2
                    || route.GetConvertedPortal(0) != 10
                    || route.GetConvertedPortal(1) != 20
                    || !route.ValidateAuthorityHashes())
                {
                    throw new InvalidOperationException(
                        $"Flow path production route preparation authority validation failed. " +
                        $"complete={complete}, tasks={route.TaskCount}, l0={route.L0NodeCount}, " +
                        $"sectors={FormatPreparationSequence(route.CopyConvertedSectors(route.ConvertedSectorCount))}, " +
                        $"portals={FormatPreparationSequence(route.CopyConvertedPortals())}, " +
                        $"authorityValid={route.ValidateAuthorityHashes()}.");
                }
            }
        }

        private static int EncodePreparationPortalNode(int sectorId, int portalId)
        {
            return (sectorId << 16) | (portalId & 0xFFFF);
        }

        private static string FormatPreparationSequence(int[] values)
        {
            return values == null ? "null" : string.Join(",", values);
        }
    }
}
