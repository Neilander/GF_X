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

    public static LogicAgentCollisionSolveResult Solve(
        IReadOnlyList<LogicAgentCollisionBody> inputBodies,
        int iterationCount,
        Fix64 penetrationEpsilon)
    {
        if (inputBodies == null)
            throw new ArgumentNullException(nameof(inputBodies));
        if (iterationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(iterationCount), iterationCount, "Agent collision iteration count must be positive.");
        if (penetrationEpsilon < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(penetrationEpsilon), "Agent collision epsilon cannot be negative.");

        var bodies = new List<LogicAgentCollisionBody>(inputBodies.Count);
        for (int i = 0; i < inputBodies.Count; i++)
        {
            LogicAgentCollisionBody body = inputBodies[i];
            ValidateBody(body, i);
            bodies.Add(body);
        }

        bodies.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
        for (int i = 1; i < bodies.Count; i++)
        {
            if (bodies[i - 1].EntityId == bodies[i].EntityId)
                throw new InvalidOperationException($"DeterministicAgentCollisionSolver.Solve failed: duplicate entity id {bodies[i].EntityId.Value}.");
        }

        var positions = new FixVector2[bodies.Count];
        var corrections = new FixVector2[bodies.Count];
        for (int i = 0; i < bodies.Count; i++)
            positions[i] = bodies[i].Position;

        Fix64 bucketSize = ResolveBucketSize(bodies);
        var candidatePairKeys = new HashSet<long>();

        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            List<StablePair> pairs = BuildStablePairs(bodies, positions, bucketSize);
            Array.Clear(corrections, 0, corrections.Length);
            for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                StablePair pair = pairs[pairIndex];
                candidatePairKeys.Add(GetPairKey(pair.FirstIndex, pair.SecondIndex));
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

            for (int i = 0; i < positions.Length; i++)
                positions[i] += corrections[i];
        }

        List<StablePair> residualPairs = BuildStablePairs(bodies, positions, bucketSize);
        for (int i = 0; i < residualPairs.Count; i++)
            candidatePairKeys.Add(GetPairKey(residualPairs[i].FirstIndex, residualPairs[i].SecondIndex));

        MeasureResiduals(
            bodies,
            residualPairs,
            positions,
            penetrationEpsilon,
            out int residualOverlapCount,
            out Fix64 maxResidualPenetration);

        var states = new List<LogicAgentCollisionState>(bodies.Count);
        for (int i = 0; i < bodies.Count; i++)
        {
            states.Add(new LogicAgentCollisionState(
                bodies[i].EntityId,
                positions[i],
                positions[i] - bodies[i].Position));
        }

        return new LogicAgentCollisionSolveResult(
            states,
            iterationCount,
            candidatePairKeys.Count,
            residualOverlapCount,
            maxResidualPenetration);
    }

    private static List<StablePair> BuildStablePairs(
        IReadOnlyList<LogicAgentCollisionBody> bodies,
        IReadOnlyList<FixVector2> positions,
        Fix64 bucketSize)
    {
        var buckets = new Dictionary<SpatialCell, List<int>>();
        for (int i = 0; i < bodies.Count; i++)
        {
            SpatialCell cell = ResolveCell(positions[i], bucketSize);
            if (!buckets.TryGetValue(cell, out List<int> indices))
            {
                indices = new List<int>();
                buckets.Add(cell, indices);
            }
            indices.Add(i);
        }

        var pairs = new List<StablePair>();
        var pairKeys = new HashSet<long>();
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
                    if (!buckets.TryGetValue(neighborCell, out List<int> neighbors))
                        continue;

                    for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                    {
                        int second = neighbors[neighborIndex];
                        if (second <= first || !CanCollide(bodies[first], bodies[second]))
                            continue;

                        long pairKey = GetPairKey(first, second);
                        if (pairKeys.Add(pairKey))
                            pairs.Add(new StablePair(first, second));
                    }
                }
            }
        }

        pairs.Sort((left, right) =>
        {
            int firstComparison = left.FirstIndex.CompareTo(right.FirstIndex);
            return firstComparison != 0
                ? firstComparison
                : left.SecondIndex.CompareTo(right.SecondIndex);
        });
        return pairs;
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
