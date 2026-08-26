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

    public sealed class FlowPathKernelSearchState : IDisposable
    {
        private NativeParallelHashMap<int, long> m_Costs;
        private NativeParallelHashMap<int, int> m_Previous;
        private NativeParallelHashSet<int> m_Settled;
        private NativeList<HeapNode> m_Open;
        private NativeReference<PopResult> m_PopResult;
        private NativeReference<int> m_ExpansionCount;
        private NativeReference<ulong> m_CostsAuthorityHash;
        private NativeReference<ulong> m_PreviousAuthorityHash;
        private NativeReference<ulong> m_SettledAuthorityHash;
        private NativeReference<ulong> m_OpenAuthorityHash;
        private NativeReference<int> m_RelaxError;
        private NativeReference<int> m_ShiftError;

        public FlowPathKernelSearchState(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Costs = new NativeParallelHashMap<int, long>(capacity, Allocator.Persistent);
            m_Previous = new NativeParallelHashMap<int, int>(capacity, Allocator.Persistent);
            m_Settled = new NativeParallelHashSet<int>(capacity, Allocator.Persistent);
            m_Open = new NativeList<HeapNode>(capacity, Allocator.Persistent);
            m_PopResult = new NativeReference<PopResult>(Allocator.Persistent);
            m_ExpansionCount = new NativeReference<int>(Allocator.Persistent);
            m_CostsAuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_PreviousAuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_SettledAuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_OpenAuthorityHash = new NativeReference<ulong>(Allocator.Persistent);
            m_RelaxError = new NativeReference<int>(Allocator.Persistent);
            m_ShiftError = new NativeReference<int>(Allocator.Persistent);
        }

        public bool IsCreated => m_Costs.IsCreated;
        public int OpenCount => m_Open.Length;
        public int ExpansionCount => m_ExpansionCount.Value;
        public int CostCount => m_Costs.Count();
        public int PreviousCount => m_Previous.Count();
        public int SettledCount => m_Settled.Count();
        public ulong CostsAuthorityHash => m_CostsAuthorityHash.Value;
        public ulong PreviousAuthorityHash => m_PreviousAuthorityHash.Value;
        public ulong SettledAuthorityHash => m_SettledAuthorityHash.Value;
        public ulong OpenAuthorityHash => m_OpenAuthorityHash.Value;

        internal NativeParallelHashMap<int, int> PreviousMap
        {
            get
            {
                RequireCreated();
                return m_Previous;
            }
        }

        public void AddSource(int node, long cost)
        {
            RequireCreated();
            EnsureCapacityForNode(node, needsPrevious: false);
            new AddSourceJob
            {
                Node = node,
                Cost = cost,
                Costs = m_Costs,
                Open = m_Open,
                CostsAuthorityHash = m_CostsAuthorityHash,
                OpenAuthorityHash = m_OpenAuthorityHash
            }.Run();
        }

        public FlowPathKernelPopStatus PopOne(out FlowPathKernelSearchEntry entry)
        {
            RequireCreated();
            EnsureSettledCapacity();
            new PopSettledJob
            {
                Costs = m_Costs,
                Settled = m_Settled,
                Open = m_Open,
                Result = m_PopResult,
                ExpansionCount = m_ExpansionCount,
                SettledAuthorityHash = m_SettledAuthorityHash,
                OpenAuthorityHash = m_OpenAuthorityHash
            }.Run();
            PopResult result = m_PopResult.Value;
            entry = new FlowPathKernelSearchEntry(result.Node, result.Cost);
            return (FlowPathKernelPopStatus)result.Status;
        }

        public void Relax(int currentNode, int nextNode, long nextCost)
        {
            RequireCreated();
            EnsureCapacityForNode(nextNode, needsPrevious: true);
            new RelaxJob
            {
                CurrentNode = currentNode,
                NextNode = nextNode,
                NextCost = nextCost,
                Costs = m_Costs,
                Previous = m_Previous,
                Settled = m_Settled,
                Open = m_Open,
                CostsAuthorityHash = m_CostsAuthorityHash,
                PreviousAuthorityHash = m_PreviousAuthorityHash,
                OpenAuthorityHash = m_OpenAuthorityHash,
                Error = m_RelaxError
            }.Run();
            if (m_RelaxError.Value != 0)
            {
                throw new InvalidOperationException(
                    $"Flow path kernel found a cheaper path to a settled node. node={nextNode}, cost={nextCost}.");
            }
        }

        public bool TryGetCost(int node, out long cost)
        {
            RequireCreated();
            return m_Costs.TryGetValue(node, out cost);
        }

        public bool ContainsSettled(int node)
        {
            RequireCreated();
            return m_Settled.Contains(node);
        }

        public bool TryGetPrevious(int node, out int previous)
        {
            RequireCreated();
            return m_Previous.TryGetValue(node, out previous);
        }

        public void ShiftCosts(long delta)
        {
            RequireCreated();
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
                    CostsAuthorityHash = m_CostsAuthorityHash,
                    OpenAuthorityHash = m_OpenAuthorityHash,
                    Error = m_ShiftError
                }.Run();
            }
            if (m_ShiftError.Value != 0)
                throw new OverflowException($"Flow path kernel cost shift overflowed. delta={delta}.");
        }

        public bool ValidateAuthorityHashes()
        {
            RequireCreated();
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
            return costsHash == m_CostsAuthorityHash.Value
                   && previousHash == m_PreviousAuthorityHash.Value
                   && settledHash == m_SettledAuthorityHash.Value
                   && openHash == m_OpenAuthorityHash.Value;
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

        public void Dispose()
        {
            if (m_Costs.IsCreated)
                m_Costs.Dispose();
            if (m_Previous.IsCreated)
                m_Previous.Dispose();
            if (m_Settled.IsCreated)
                m_Settled.Dispose();
            if (m_Open.IsCreated)
                m_Open.Dispose();
            if (m_PopResult.IsCreated)
                m_PopResult.Dispose();
            if (m_ExpansionCount.IsCreated)
                m_ExpansionCount.Dispose();
            if (m_CostsAuthorityHash.IsCreated)
                m_CostsAuthorityHash.Dispose();
            if (m_PreviousAuthorityHash.IsCreated)
                m_PreviousAuthorityHash.Dispose();
            if (m_SettledAuthorityHash.IsCreated)
                m_SettledAuthorityHash.Dispose();
            if (m_OpenAuthorityHash.IsCreated)
                m_OpenAuthorityHash.Dispose();
            if (m_RelaxError.IsCreated)
                m_RelaxError.Dispose();
            if (m_ShiftError.IsCreated)
                m_ShiftError.Dispose();
        }

        private void RequireCreated()
        {
            if (!m_Costs.IsCreated
                || !m_Previous.IsCreated
                || !m_Settled.IsCreated
                || !m_Open.IsCreated
                || !m_PopResult.IsCreated
                || !m_ExpansionCount.IsCreated
                || !m_CostsAuthorityHash.IsCreated
                || !m_PreviousAuthorityHash.IsCreated
                || !m_SettledAuthorityHash.IsCreated
                || !m_OpenAuthorityHash.IsCreated
                || !m_RelaxError.IsCreated
                || !m_ShiftError.IsCreated)
            {
                throw new ObjectDisposedException(nameof(FlowPathKernelSearchState));
            }
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

        private void EnsureSettledCapacity()
        {
            if (m_Settled.Count() >= m_Settled.Capacity)
                m_Settled.Capacity = checked(Math.Max(m_Settled.Capacity * 2, m_Settled.Capacity + 1));
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
        private struct AddSourceJob : IJob
        {
            public int Node;
            public long Cost;
            public NativeParallelHashMap<int, long> Costs;
            public NativeList<HeapNode> Open;
            public NativeReference<ulong> CostsAuthorityHash;
            public NativeReference<ulong> OpenAuthorityHash;

            public void Execute()
            {
                if (Costs.TryGetValue(Node, out long existing) && Cost >= existing)
                    return;
                if (Costs.TryGetValue(Node, out existing))
                    CostsAuthorityHash.Value ^= CostToken(Node, existing);
                Costs[Node] = Cost;
                CostsAuthorityHash.Value ^= CostToken(Node, Cost);
                Push(ref Open, new HeapNode { Node = Node, Cost = Cost });
                OpenAuthorityHash.Value += HeapToken(Node, Cost);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PopSettledJob : IJob
        {
            [ReadOnly] public NativeParallelHashMap<int, long> Costs;
            public NativeParallelHashSet<int> Settled;
            public NativeList<HeapNode> Open;
            public NativeReference<PopResult> Result;
            public NativeReference<int> ExpansionCount;
            public NativeReference<ulong> SettledAuthorityHash;
            public NativeReference<ulong> OpenAuthorityHash;

            public void Execute()
            {
                Result.Value = default;
                if (Open.Length == 0)
                    return;
                HeapNode item = Pop(ref Open);
                OpenAuthorityHash.Value -= HeapToken(item.Node, item.Cost);
                if (!Costs.TryGetValue(item.Node, out long currentCost))
                {
                    Result.Value = new PopResult
                    {
                        Status = (int)FlowPathKernelPopStatus.MissingCost,
                        Node = item.Node,
                        Cost = item.Cost
                    };
                    return;
                }
                if (item.Cost != currentCost)
                {
                    Result.Value = new PopResult
                    {
                        Status = (int)FlowPathKernelPopStatus.CostMismatch,
                        Node = item.Node,
                        Cost = item.Cost
                    };
                    return;
                }
                if (!Settled.Add(item.Node))
                {
                    Result.Value = new PopResult
                    {
                        Status = (int)FlowPathKernelPopStatus.AlreadySettled,
                        Node = item.Node,
                        Cost = item.Cost
                    };
                    return;
                }
                ExpansionCount.Value++;
                SettledAuthorityHash.Value ^= NodeToken(item.Node);
                Result.Value = new PopResult
                {
                    Status = (int)FlowPathKernelPopStatus.Settled,
                    Node = item.Node,
                    Cost = currentCost
                };
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct RelaxJob : IJob
        {
            public int CurrentNode;
            public int NextNode;
            public long NextCost;
            public NativeParallelHashMap<int, long> Costs;
            public NativeParallelHashMap<int, int> Previous;
            [ReadOnly] public NativeParallelHashSet<int> Settled;
            public NativeList<HeapNode> Open;
            public NativeReference<ulong> CostsAuthorityHash;
            public NativeReference<ulong> PreviousAuthorityHash;
            public NativeReference<ulong> OpenAuthorityHash;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                if (Settled.Contains(NextNode))
                {
                    if (Costs.TryGetValue(NextNode, out long settledCost) && NextCost < settledCost)
                        Error.Value = 1;
                    return;
                }
                if (Costs.TryGetValue(NextNode, out long existing) && NextCost >= existing)
                    return;
                if (Costs.TryGetValue(NextNode, out existing))
                    CostsAuthorityHash.Value ^= CostToken(NextNode, existing);
                Costs[NextNode] = NextCost;
                CostsAuthorityHash.Value ^= CostToken(NextNode, NextCost);
                if (Previous.TryGetValue(NextNode, out int previous))
                    PreviousAuthorityHash.Value ^= PreviousToken(NextNode, previous);
                Previous[NextNode] = CurrentNode;
                PreviousAuthorityHash.Value ^= PreviousToken(NextNode, CurrentNode);
                Push(ref Open, new HeapNode { Node = NextNode, Cost = NextCost });
                OpenAuthorityHash.Value += HeapToken(NextNode, NextCost);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ShiftCostsJob : IJob
        {
            public long Delta;
            [ReadOnly] public NativeArray<int> Keys;
            public NativeParallelHashMap<int, long> Costs;
            public NativeList<HeapNode> Open;
            public NativeReference<ulong> CostsAuthorityHash;
            public NativeReference<ulong> OpenAuthorityHash;
            public NativeReference<int> Error;

            public void Execute()
            {
                Error.Value = 0;
                for (int i = 0; i < Keys.Length; i++)
                {
                    long cost = Costs[Keys[i]];
                    if (Delta > 0L && cost > long.MaxValue - Delta
                        || Delta < 0L && cost < long.MinValue - Delta)
                    {
                        Error.Value = 1;
                        return;
                    }
                }
                for (int i = 0; i < Open.Length; i++)
                {
                    long cost = Open[i].Cost;
                    if (Delta > 0L && cost > long.MaxValue - Delta
                        || Delta < 0L && cost < long.MinValue - Delta)
                    {
                        Error.Value = 1;
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
                CostsAuthorityHash.Value = costsHash;
                OpenAuthorityHash.Value = openHash;
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
        private NativeArray<int> m_WitnessNodes;

        public FlowPathKernelWitnessIndex(
            FlowPathKernelWitnessEdge[] edges,
            int[] witnessNodes)
        {
            if (edges == null)
                throw new ArgumentNullException(nameof(edges));
            if (witnessNodes == null)
                throw new ArgumentNullException(nameof(witnessNodes));
            m_Edges = new NativeArray<FlowPathKernelWitnessEdge>(edges, Allocator.Persistent);
            m_WitnessNodes = new NativeArray<int>(witnessNodes, Allocator.Persistent);
        }

        public bool IsCreated => m_Edges.IsCreated && m_WitnessNodes.IsCreated;
        public int EdgeCount => m_Edges.IsCreated ? m_Edges.Length : 0;
        internal NativeArray<FlowPathKernelWitnessEdge> Edges => m_Edges;
        internal NativeArray<int> WitnessNodes => m_WitnessNodes;

        public void Dispose()
        {
            if (m_Edges.IsCreated)
                m_Edges.Dispose();
            if (m_WitnessNodes.IsCreated)
                m_WitnessNodes.Dispose();
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
        private NativeReference<int> m_Error;

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
            m_Error = new NativeReference<int>(Allocator.Persistent);
        }

        public bool IsCreated => m_Tasks.IsCreated;
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
            if (edgeLevelIndex >= 0 && (witnessIndex == null || !witnessIndex.IsCreated))
                throw new InvalidOperationException("Flow path route pair expansion requires a live witness index.");
            new AdvanceRoutePairJob
            {
                DirectPortalCrossing = directPortalCrossing ? 1 : 0,
                EdgeLevelIndex = edgeLevelIndex,
                ClusterId = clusterId,
                TraverseWitnessTaskType = traverseWitnessTaskType,
                Edges = edgeLevelIndex >= 0 ? witnessIndex.Edges : default,
                Tasks = m_Tasks,
                L0Nodes = m_L0Nodes,
                TaskAuthorityHash = m_TaskAuthorityHash,
                L0AuthorityHash = m_L0AuthorityHash,
                Error = m_Error
            }.Run();
            ThrowOperationError("pair expansion");
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

        private static void AppendNode(
            ref NativeList<int> nodes,
            ref ulong authorityHash,
            int node)
        {
            if (nodes.Length > 0 && nodes[nodes.Length - 1] == node)
                return;
            int index = nodes.Length;
            nodes.Add(node);
            authorityHash ^= NodeSequenceToken(index, node);
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
        private const int PreparationSeed = 0x13579BDF;
        private const int PreparationResult = unchecked((PreparationSeed * 397) ^ 0x2468ACE0);
        private static bool s_IsPrepared;

        public static bool IsPrepared => s_IsPrepared;

        public static void Prepare()
        {
            using (var result = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            {
                new PreparationJob
                {
                    Seed = PreparationSeed,
                    Result = result
                }.Run();

                if (result[0] != PreparationResult)
                {
                    throw new InvalidOperationException(
                        $"Flow path Burst kernel preparation failed. expected={PreparationResult}, actual={result[0]}.");
                }
            }

            s_IsPrepared = true;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PreparationJob : IJob
        {
            public int Seed;
            public NativeArray<int> Result;

            public void Execute()
            {
                Result[0] = unchecked((Seed * 397) ^ 0x2468ACE0);
            }
        }
    }
}
