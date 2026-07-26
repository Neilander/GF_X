using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct LogicAgentCollisionBody
{
    public LogicAgentCollisionBody(
        LogicEntityId entityId,
        FixVector2 position,
        Fix64 radius,
        Fix64 inverseMass,
        uint collisionCategory,
        uint collisionMask)
    {
        EntityId = entityId;
        Position = position;
        Radius = radius;
        InverseMass = inverseMass;
        CollisionCategory = collisionCategory;
        CollisionMask = collisionMask;
    }

    public LogicEntityId EntityId { get; }
    public FixVector2 Position { get; }
    public Fix64 Radius { get; }
    public Fix64 InverseMass { get; }
    public uint CollisionCategory { get; }
    public uint CollisionMask { get; }
}

public readonly struct LogicAgentCollisionState
{
    public LogicAgentCollisionState(LogicEntityId entityId, FixVector2 position, FixVector2 correction)
    {
        EntityId = entityId;
        Position = position;
        Correction = correction;
    }

    public LogicEntityId EntityId { get; }
    public FixVector2 Position { get; }
    public FixVector2 Correction { get; }
}

public sealed class LogicAgentCollisionSolveResult
{
    private readonly ReadOnlyCollection<LogicAgentCollisionState> m_States;

    internal LogicAgentCollisionSolveResult(
        List<LogicAgentCollisionState> states,
        int iterationCount,
        int candidatePairCount,
        int residualOverlapCount,
        Fix64 maxResidualPenetration)
    {
        m_States = (states ?? throw new ArgumentNullException(nameof(states))).AsReadOnly();
        IterationCount = iterationCount;
        CandidatePairCount = candidatePairCount;
        ResidualOverlapCount = residualOverlapCount;
        MaxResidualPenetration = maxResidualPenetration;
    }

    public IReadOnlyList<LogicAgentCollisionState> States => m_States;
    public int IterationCount { get; }
    public int CandidatePairCount { get; }
    public int ResidualOverlapCount { get; }
    public Fix64 MaxResidualPenetration { get; }
    public bool Success => ResidualOverlapCount == 0;
}

internal readonly struct LogicAgentCollisionSolveSummary
{
    public LogicAgentCollisionSolveSummary(
        int iterationCount,
        int candidatePairCount,
        int residualOverlapCount,
        Fix64 maxResidualPenetration)
    {
        IterationCount = iterationCount;
        CandidatePairCount = candidatePairCount;
        ResidualOverlapCount = residualOverlapCount;
        MaxResidualPenetration = maxResidualPenetration;
    }

    public int IterationCount { get; }
    public int CandidatePairCount { get; }
    public int ResidualOverlapCount { get; }
    public Fix64 MaxResidualPenetration { get; }
}

public static class DeterministicAgentCollisionSolver
{
    private readonly struct SpatialCell : IEquatable<SpatialCell>, IComparable<SpatialCell>
    {
        public SpatialCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(SpatialCell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is SpatialCell other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public int CompareTo(SpatialCell other)
        {
            int xComparison = X.CompareTo(other.X);
            return xComparison != 0 ? xComparison : Y.CompareTo(other.Y);
        }
    }

    private readonly struct StablePair
    {
        public StablePair(int firstIndex, int secondIndex)
        {
            FirstIndex = firstIndex;
            SecondIndex = secondIndex;
        }

        public int FirstIndex { get; }
        public int SecondIndex { get; }
    }

    private static readonly List<LogicAgentCollisionBody> BodiesScratch = new List<LogicAgentCollisionBody>();
    private static readonly Dictionary<SpatialCell, List<int>> BucketsScratch =
        new Dictionary<SpatialCell, List<int>>();
    private static readonly List<List<int>> BucketListsScratch = new List<List<int>>();
    private static readonly List<StablePair> PairsScratch = new List<StablePair>();
    private static readonly HashSet<long> PairKeysScratch = new HashSet<long>();
    private static readonly HashSet<long> CandidatePairKeysScratch = new HashSet<long>();
    private static readonly Comparison<LogicAgentCollisionBody> BodyComparison = CompareBodies;
    private static readonly Comparison<StablePair> PairComparison = ComparePairs;
    private static FixVector2[] s_PositionsScratch = Array.Empty<FixVector2>();
    private static FixVector2[] s_CorrectionsScratch = Array.Empty<FixVector2>();
    private static bool s_IsSolving;

    public static LogicAgentCollisionSolveResult Solve(
        IReadOnlyList<LogicAgentCollisionBody> inputBodies,
        int iterationCount,
        Fix64 penetrationEpsilon)
    {
        var states = new List<LogicAgentCollisionState>(inputBodies?.Count ?? 0);
        LogicAgentCollisionSolveSummary summary = SolveInto(
            inputBodies,
            iterationCount,
            penetrationEpsilon,
            states);
        return new LogicAgentCollisionSolveResult(
            states,
            summary.IterationCount,
            summary.CandidatePairCount,
            summary.ResidualOverlapCount,
            summary.MaxResidualPenetration);
    }

    internal static LogicAgentCollisionSolveSummary SolveInto(
        IReadOnlyList<LogicAgentCollisionBody> inputBodies,
        int iterationCount,
        Fix64 penetrationEpsilon,
        List<LogicAgentCollisionState> outputStates)
    {
        if (inputBodies == null)
            throw new ArgumentNullException(nameof(inputBodies));
        if (outputStates == null)
            throw new ArgumentNullException(nameof(outputStates));
        if (iterationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(iterationCount), iterationCount, "Agent collision iteration count must be positive.");
        if (penetrationEpsilon < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(penetrationEpsilon), "Agent collision epsilon cannot be negative.");
        if (s_IsSolving)
            throw new InvalidOperationException("DeterministicAgentCollisionSolver.SolveInto does not support reentrant execution.");

        s_IsSolving = true;
        try
        {
            List<LogicAgentCollisionBody> bodies = BodiesScratch;
            bodies.Clear();
            if (bodies.Capacity < inputBodies.Count)
                bodies.Capacity = inputBodies.Count;
            for (int i = 0; i < inputBodies.Count; i++)
            {
                LogicAgentCollisionBody body = inputBodies[i];
                ValidateBody(body, i);
                bodies.Add(body);
            }

            bodies.Sort(BodyComparison);
            for (int i = 1; i < bodies.Count; i++)
            {
                if (bodies[i - 1].EntityId == bodies[i].EntityId)
                    throw new InvalidOperationException($"DeterministicAgentCollisionSolver.Solve failed: duplicate entity id {bodies[i].EntityId.Value}.");
            }

            EnsureVectorScratchCapacity(bodies.Count);
            FixVector2[] positions = s_PositionsScratch;
            FixVector2[] corrections = s_CorrectionsScratch;
            for (int i = 0; i < bodies.Count; i++)
                positions[i] = bodies[i].Position;

            Fix64 bucketSize = ResolveBucketSize(bodies);
            CandidatePairKeysScratch.Clear();

            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                List<StablePair> pairs = BuildStablePairs(bodies, positions, bucketSize);
                Array.Clear(corrections, 0, bodies.Count);
                for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
                {
                    StablePair pair = pairs[pairIndex];
                    CandidatePairKeysScratch.Add(GetPairKey(pair.FirstIndex, pair.SecondIndex));
                    LogicAgentCollisionBody first = bodies[pair.FirstIndex];
                    LogicAgentCollisionBody second = bodies[pair.SecondIndex];
                    if (!TryGetCorrection(
                            first,
                            second,
                            positions[pair.FirstIndex],
                            positions[pair.SecondIndex],
                            penetrationEpsilon,
                            out FixVector2 firstCorrection,
                            out FixVector2 secondCorrection))
                    {
                        continue;
                    }

                    corrections[pair.FirstIndex] += firstCorrection;
                    corrections[pair.SecondIndex] += secondCorrection;
                }

                for (int i = 0; i < bodies.Count; i++)
                    positions[i] += corrections[i];
            }

            List<StablePair> residualPairs = BuildStablePairs(bodies, positions, bucketSize);
            for (int i = 0; i < residualPairs.Count; i++)
                CandidatePairKeysScratch.Add(GetPairKey(residualPairs[i].FirstIndex, residualPairs[i].SecondIndex));

            MeasureResiduals(
                bodies,
                residualPairs,
                positions,
                penetrationEpsilon,
                out int residualOverlapCount,
                out Fix64 maxResidualPenetration);

            outputStates.Clear();
            if (outputStates.Capacity < bodies.Count)
                outputStates.Capacity = bodies.Count;
            for (int i = 0; i < bodies.Count; i++)
            {
                outputStates.Add(new LogicAgentCollisionState(
                    bodies[i].EntityId,
                    positions[i],
                    positions[i] - bodies[i].Position));
            }

            return new LogicAgentCollisionSolveSummary(
                iterationCount,
                CandidatePairKeysScratch.Count,
                residualOverlapCount,
                maxResidualPenetration);
        }
        finally
        {
            s_IsSolving = false;
        }
    }

    private static List<StablePair> BuildStablePairs(
        IReadOnlyList<LogicAgentCollisionBody> bodies,
        IReadOnlyList<FixVector2> positions,
        Fix64 bucketSize)
    {
        BucketsScratch.Clear();
        int usedBucketListCount = 0;
        for (int i = 0; i < bodies.Count; i++)
        {
            SpatialCell cell = ResolveCell(positions[i], bucketSize);
            if (!BucketsScratch.TryGetValue(cell, out List<int> indices))
            {
                if (usedBucketListCount < BucketListsScratch.Count)
                {
                    indices = BucketListsScratch[usedBucketListCount];
                    indices.Clear();
                }
                else
                {
                    indices = new List<int>(4);
                    BucketListsScratch.Add(indices);
                }

                usedBucketListCount++;
                BucketsScratch.Add(cell, indices);
            }
            indices.Add(i);
        }

        PairsScratch.Clear();
        PairKeysScratch.Clear();
        for (int first = 0; first < bodies.Count; first++)
        {
            SpatialCell firstCell = ResolveCell(positions[first], bucketSize);
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    var neighborCell = new SpatialCell(
                        checked(firstCell.X + offsetX),
                        checked(firstCell.Y + offsetY));
                    if (!BucketsScratch.TryGetValue(neighborCell, out List<int> neighbors))
                        continue;

                    for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                    {
                        int second = neighbors[neighborIndex];
                        if (second <= first || !CanCollide(bodies[first], bodies[second]))
                            continue;

                        long pairKey = GetPairKey(first, second);
                        if (PairKeysScratch.Add(pairKey))
                            PairsScratch.Add(new StablePair(first, second));
                    }
                }
            }
        }

        PairsScratch.Sort(PairComparison);
        return PairsScratch;
    }

    private static void EnsureVectorScratchCapacity(int count)
    {
        if (s_PositionsScratch.Length >= count)
            return;

        int capacity = Math.Max(count, Math.Max(16, s_PositionsScratch.Length * 2));
        s_PositionsScratch = new FixVector2[capacity];
        s_CorrectionsScratch = new FixVector2[capacity];
    }

    private static int CompareBodies(LogicAgentCollisionBody left, LogicAgentCollisionBody right)
    {
        return left.EntityId.CompareTo(right.EntityId);
    }

    private static int ComparePairs(StablePair left, StablePair right)
    {
        int firstComparison = left.FirstIndex.CompareTo(right.FirstIndex);
        return firstComparison != 0
            ? firstComparison
            : left.SecondIndex.CompareTo(right.SecondIndex);
    }

    private static Fix64 ResolveBucketSize(IReadOnlyList<LogicAgentCollisionBody> bodies)
    {
        Fix64 maxRadius = Fix64.Zero;
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].Radius > maxRadius)
                maxRadius = bodies[i].Radius;
        }

        Fix64 diameter = maxRadius * (Fix64)2;
        return diameter > Fix64.Zero ? diameter : Fix64.One;
    }

    private static SpatialCell ResolveCell(FixVector2 position, Fix64 bucketSize)
    {
        return new SpatialCell(
            FloorDivide(position.x.RawValue, bucketSize.RawValue),
            FloorDivide(position.y.RawValue, bucketSize.RawValue));
    }

    private static int FloorDivide(long value, long positiveDivisor)
    {
        if (positiveDivisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(positiveDivisor));

        long quotient = value / positiveDivisor;
        if (value < 0 && value % positiveDivisor != 0)
            quotient--;
        return checked((int)quotient);
    }

    private static long GetPairKey(int firstIndex, int secondIndex)
    {
        return ((long)firstIndex << 32) | (uint)secondIndex;
    }

    private static bool TryGetCorrection(
        LogicAgentCollisionBody first,
        LogicAgentCollisionBody second,
        FixVector2 firstPosition,
        FixVector2 secondPosition,
        Fix64 penetrationEpsilon,
        out FixVector2 firstCorrection,
        out FixVector2 secondCorrection)
    {
        firstCorrection = FixVector2.Zero;
        secondCorrection = FixVector2.Zero;

        Fix64 minimumDistance = first.Radius + second.Radius;
        if (minimumDistance <= Fix64.Zero)
            return false;

        FixVector2 delta = secondPosition - firstPosition;
        Fix64 distanceSquared = FixVector2.SqrMagnitude(delta);
        Fix64 minimumDistanceSquared = minimumDistance * minimumDistance;
        if (distanceSquared >= minimumDistanceSquared)
            return false;

        Fix64 distance;
        FixVector2 normal;
        if (distanceSquared == Fix64.Zero)
        {
            distance = Fix64.Zero;
            normal = GetStableOverlapNormal(first.EntityId, second.EntityId);
        }
        else
        {
            distance = Fix64.Sqrt(distanceSquared);
            normal = delta / distance;
        }

        Fix64 penetration = minimumDistance - distance;
        if (penetration <= penetrationEpsilon)
            return false;

        Fix64 inverseMassSum = first.InverseMass + second.InverseMass;
        if (inverseMassSum <= Fix64.Zero)
            return false;

        FixVector2 separation = normal * penetration;
        firstCorrection = -separation * (first.InverseMass / inverseMassSum);
        secondCorrection = separation * (second.InverseMass / inverseMassSum);
        return true;
    }

    private static void MeasureResiduals(
        IReadOnlyList<LogicAgentCollisionBody> bodies,
        IReadOnlyList<StablePair> pairs,
        IReadOnlyList<FixVector2> positions,
        Fix64 penetrationEpsilon,
        out int residualOverlapCount,
        out Fix64 maxResidualPenetration)
    {
        residualOverlapCount = 0;
        maxResidualPenetration = Fix64.Zero;
        for (int i = 0; i < pairs.Count; i++)
        {
            StablePair pair = pairs[i];
            Fix64 minimumDistance = bodies[pair.FirstIndex].Radius + bodies[pair.SecondIndex].Radius;
            if (minimumDistance <= Fix64.Zero)
                continue;

            Fix64 distanceSquared = FixVector2.SqrMagnitude(positions[pair.SecondIndex] - positions[pair.FirstIndex]);
            Fix64 minimumDistanceSquared = minimumDistance * minimumDistance;
            if (distanceSquared >= minimumDistanceSquared)
                continue;

            Fix64 distance = distanceSquared == Fix64.Zero ? Fix64.Zero : Fix64.Sqrt(distanceSquared);
            Fix64 penetration = minimumDistance - distance;
            if (penetration <= penetrationEpsilon)
                continue;

            residualOverlapCount++;
            if (penetration > maxResidualPenetration)
                maxResidualPenetration = penetration;
        }
    }

    private static FixVector2 GetStableOverlapNormal(LogicEntityId first, LogicEntityId second)
    {
        uint hash = unchecked((uint)first.Value * 73856093u) ^ unchecked((uint)second.Value * 19349663u);
        switch (hash & 3u)
        {
            case 0u:
                return new FixVector2(1, 0);
            case 1u:
                return new FixVector2(-1, 0);
            case 2u:
                return new FixVector2(0, 1);
            default:
                return new FixVector2(0, -1);
        }
    }

    private static bool CanCollide(LogicAgentCollisionBody first, LogicAgentCollisionBody second)
    {
        return (first.CollisionMask & second.CollisionCategory) != 0u
               && (second.CollisionMask & first.CollisionCategory) != 0u;
    }

    private static void ValidateBody(LogicAgentCollisionBody body, int index)
    {
        if (!body.EntityId.IsValid)
            throw new InvalidOperationException($"DeterministicAgentCollisionSolver.Solve failed: body at index {index} has an invalid entity id.");
        if (body.Radius < Fix64.Zero)
            throw new InvalidOperationException($"DeterministicAgentCollisionSolver.Solve failed: entity {body.EntityId.Value} has a negative radius.");
        if (body.InverseMass < Fix64.Zero)
            throw new InvalidOperationException($"DeterministicAgentCollisionSolver.Solve failed: entity {body.EntityId.Value} has a negative inverse mass.");
    }
}
