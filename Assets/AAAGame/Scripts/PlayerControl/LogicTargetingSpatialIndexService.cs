using System;
using System.Collections.Generic;

public static class LogicTargetingSpatialIndexService
{
    private static readonly Fix64 BucketSize = (Fix64)8;
    private static readonly Dictionary<long, List<IEntityContext>> Buckets =
        new Dictionary<long, List<IEntityContext>>();
    private static readonly List<IEntityContext> CompositeWallEntities = new List<IEntityContext>();
    private static Fix64 s_MaximumIndexedExtent;
    private static bool s_IsActive;
    private static int s_BuildCount;
    private static int s_CandidateVisitCount;
    private static ulong s_SnapshotFrame;
    private static bool s_HasSnapshot;

    public static void BeginTargetingPhase()
    {
        if (s_IsActive)
            throw new InvalidOperationException("Targeting spatial index is already active.");

        Buckets.Clear();
        CompositeWallEntities.Clear();
        s_MaximumIndexedExtent = Fix64.Zero;
        s_CandidateVisitCount = 0;
        s_SnapshotFrame = LogicFrameRuntime.CurrentFrame;
        s_HasSnapshot = true;
        IList<IEntityContext> entities = EntityRegistry.AllEntities
            ?? throw new InvalidOperationException("Targeting spatial index cannot read a null entity registry.");
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                ?? throw new InvalidOperationException($"Targeting spatial index found a null registry entity at index {i}.");
            if (!entity.LogicEntityId.IsValid)
                throw new InvalidOperationException($"Targeting spatial index found an invalid logic id at index {i}.");

            if (LogicWallRuntime.TryGetBranch(entity.LogicEntityId, out _))
            {
                CompositeWallEntities.Add(entity);
                continue;
            }

            LogicCombatShape shape = LogicFrameRuntime.IsTicking
                ? LogicEntityFrameSnapshotService.GetRequiredCurrent(entity).CombatShape
                : entity.CombatShape;
            s_MaximumIndexedExtent = Fix64.Max(s_MaximumIndexedExtent, ResolveBoundingExtent(shape));
            FixVector2 position = entity.LogicFramePositionFixed();
            long key = PackBucket(ResolveBucket(position.x), ResolveBucket(position.y));
            if (!Buckets.TryGetValue(key, out List<IEntityContext> bucket))
            {
                bucket = new List<IEntityContext>();
                Buckets.Add(key, bucket);
            }
            bucket.Add(entity);
        }

        foreach (List<IEntityContext> bucket in Buckets.Values)
            bucket.Sort(CompareEntityIds);
        CompositeWallEntities.Sort(CompareEntityIds);
        s_BuildCount++;
        s_IsActive = true;
    }

    public static void EndTargetingPhase()
    {
        if (!s_IsActive)
            throw new InvalidOperationException("Targeting spatial index is not active.");
        s_IsActive = false;
    }

    public static void Reset()
    {
        s_IsActive = false;
        Buckets.Clear();
        CompositeWallEntities.Clear();
        s_MaximumIndexedExtent = Fix64.Zero;
        s_CandidateVisitCount = 0;
        s_BuildCount = 0;
        s_SnapshotFrame = 0;
        s_HasSnapshot = false;
    }

    public static void CollectCandidates(
        FixVector2 center,
        Fix64 surfaceRange,
        List<IEntityContext> results)
    {
        if (surfaceRange < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(surfaceRange));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        if (!s_IsActive)
        {
            BeginTargetingPhase();
            try
            {
                CollectCandidatesFromActiveIndex(center, surfaceRange, results);
            }
            finally
            {
                EndTargetingPhase();
            }
            return;
        }

        CollectCandidatesFromActiveIndex(center, surfaceRange, results);
    }

    private static void CollectCandidatesFromActiveIndex(
        FixVector2 center,
        Fix64 surfaceRange,
        List<IEntityContext> results)
    {
        if (!s_IsActive)
            throw new InvalidOperationException("Targeting spatial index query requires an active targeting phase.");

        results.Clear();
        Fix64 centerRange = surfaceRange + s_MaximumIndexedExtent;
        int minimumX = ResolveBucket(center.x - centerRange);
        int maximumX = ResolveBucket(center.x + centerRange);
        int minimumY = ResolveBucket(center.y - centerRange);
        int maximumY = ResolveBucket(center.y + centerRange);
        for (int y = minimumY; y <= maximumY; y++)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                if (!Buckets.TryGetValue(PackBucket(x, y), out List<IEntityContext> bucket))
                    continue;
                s_CandidateVisitCount += bucket.Count;
                results.AddRange(bucket);
            }
        }

        s_CandidateVisitCount += CompositeWallEntities.Count;
        results.AddRange(CompositeWallEntities);
    }

    public static int BuildCount => s_BuildCount;
    public static int CandidateVisitCount => s_CandidateVisitCount;

    public static bool HasEnemyInRange(
        IEntityContext self,
        Fix64 range,
        List<IEntityContext> scratch)
    {
        if (self == null)
            throw new ArgumentNullException(nameof(self));
        if (range < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(range));
        if (scratch == null)
            throw new ArgumentNullException(nameof(scratch));

        bool temporaryPhase = false;
        if (!s_IsActive)
        {
            if (!s_HasSnapshot || s_SnapshotFrame != LogicFrameRuntime.CurrentFrame)
            {
                BeginTargetingPhase();
                temporaryPhase = true;
            }
        }

        try
        {
            CollectCandidatesFromActiveIndex(self.LogicFramePositionFixed(), range, scratch);
            Fix64 rangeSquared = range * range;
            FixVector2 selfPosition = self.LogicFramePositionFixed();
            for (int i = 0; i < scratch.Count; i++)
            {
                IEntityContext candidate = scratch[i]
                    ?? throw new InvalidOperationException($"Targeting spatial index found a null candidate at index {i}.");
                if (ReferenceEquals(candidate, self)
                    || !WeaponTargetRules.IsValidTargetForCurrentWeapon(self, candidate))
                    continue;
                FixVector2 delta = candidate.LogicFramePositionFixed() - selfPosition;
                if (FixVector2.SqrMagnitude(delta) <= rangeSquared)
                    return true;
            }

            return false;
        }
        finally
        {
            if (temporaryPhase)
                EndTargetingPhase();
        }
    }

    private static Fix64 ResolveBoundingExtent(LogicCombatShape shape)
    {
        switch (shape.Kind)
        {
            case LogicCombatShapeKind.Circle:
                return shape.Radius;
            case LogicCombatShapeKind.AxisAlignedBox:
                return FixVector2.Magnitude(shape.HalfExtents);
            default:
                throw new InvalidOperationException($"Unsupported targeting combat shape kind {shape.Kind}.");
        }
    }

    private static int ResolveBucket(Fix64 coordinate)
    {
        long divisor = BucketSize.RawValue;
        long quotient = coordinate.RawValue / divisor;
        long remainder = coordinate.RawValue % divisor;
        if (remainder < 0)
            quotient--;
        return checked((int)quotient);
    }

    private static long PackBucket(int x, int y) => ((long)x << 32) ^ (uint)y;

    private static int CompareEntityIds(IEntityContext left, IEntityContext right) =>
        left.LogicEntityId.CompareTo(right.LogicEntityId);
}
