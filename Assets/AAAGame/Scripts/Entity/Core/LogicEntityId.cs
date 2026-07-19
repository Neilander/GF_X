using System;

public readonly struct LogicEntityId : IEquatable<LogicEntityId>, IComparable<LogicEntityId>
{
    public LogicEntityId(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Logic entity id must be positive.");

        Value = value;
    }

    public int Value { get; }
    public bool IsValid => Value > 0;

    public int CompareTo(LogicEntityId other) => Value.CompareTo(other.Value);
    public bool Equals(LogicEntityId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is LogicEntityId other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => IsValid ? Value.ToString() : "Invalid";

    public static bool operator ==(LogicEntityId left, LogicEntityId right) => left.Equals(right);
    public static bool operator !=(LogicEntityId left, LogicEntityId right) => !left.Equals(right);
    public static bool operator <(LogicEntityId left, LogicEntityId right) => left.Value < right.Value;
    public static bool operator >(LogicEntityId left, LogicEntityId right) => left.Value > right.Value;
}

public readonly struct LogicEntityIdAllocatorSnapshot
{
    public LogicEntityIdAllocatorSnapshot(int lastAllocatedValue)
    {
        if (lastAllocatedValue < 0)
            throw new ArgumentOutOfRangeException(nameof(lastAllocatedValue));

        LastAllocatedValue = lastAllocatedValue;
    }

    public int LastAllocatedValue { get; }
}

public static class LogicEntityIdAllocator
{
    private static int s_LastAllocatedValue;

    public static bool IsActive { get; private set; }
    public static int LastAllocatedValue => s_LastAllocatedValue;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicEntityIdAllocator.BeginTimeline failed: allocator is already active.");

        IsActive = true;
        s_LastAllocatedValue = 0;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        IsActive = false;
        s_LastAllocatedValue = 0;
    }

    public static LogicEntityId Allocate()
    {
        EnsureActive();
        s_LastAllocatedValue = checked(s_LastAllocatedValue + 1);
        return new LogicEntityId(s_LastAllocatedValue);
    }

    public static LogicEntityIdAllocatorSnapshot CaptureSnapshot()
    {
        EnsureActive();
        return new LogicEntityIdAllocatorSnapshot(s_LastAllocatedValue);
    }

    public static void RestoreSnapshot(LogicEntityIdAllocatorSnapshot snapshot)
    {
        EnsureActive();
        s_LastAllocatedValue = snapshot.LastAllocatedValue;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicEntityIdAllocator operation failed: allocator is not active.");
    }
}

public static class LogicEntityObstacleId
{
    private const int LocalIdCapacity = 2048;
    private const int MaxLocalOrdinal = LocalIdCapacity - 2;
    private const int MaxEntityId = 1048576;

    public static int FromBuildingCollider(LogicEntityId entityId, int localOrdinal)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Building obstacle id requires a valid logic entity id.", nameof(entityId));
        if (entityId.Value > MaxEntityId)
            throw new ArgumentOutOfRangeException(nameof(entityId), entityId.Value, $"Building obstacle encoding supports entity ids up to {MaxEntityId}.");
        if (localOrdinal < 0 || localOrdinal > MaxLocalOrdinal)
            throw new ArgumentOutOfRangeException(nameof(localOrdinal), localOrdinal, $"Building obstacle local ordinal must be in 0..{MaxLocalOrdinal}.");

        int encoded = checked((entityId.Value - 1) * LocalIdCapacity + localOrdinal + 1);
        return -encoded;
    }
}

public readonly struct LogicPersistentIdAllocatorSnapshot
{
    public LogicPersistentIdAllocatorSnapshot(int lastBuildingInstanceValue)
    {
        if (lastBuildingInstanceValue < 0)
            throw new ArgumentOutOfRangeException(nameof(lastBuildingInstanceValue));

        LastBuildingInstanceValue = lastBuildingInstanceValue;
    }

    public int LastBuildingInstanceValue { get; }
}

public static class LogicPersistentIdAllocator
{
    private static int s_LastBuildingInstanceValue;

    public static bool IsActive { get; private set; }

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicPersistentIdAllocator.BeginTimeline failed: allocator is already active.");

        IsActive = true;
        s_LastBuildingInstanceValue = 0;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        IsActive = false;
        s_LastBuildingInstanceValue = 0;
    }

    public static string AllocateBuildingInstanceId()
    {
        EnsureActive();
        s_LastBuildingInstanceValue = checked(s_LastBuildingInstanceValue + 1);
        return $"building-{s_LastBuildingInstanceValue:D10}";
    }

    public static LogicPersistentIdAllocatorSnapshot CaptureSnapshot()
    {
        EnsureActive();
        return new LogicPersistentIdAllocatorSnapshot(s_LastBuildingInstanceValue);
    }

    public static void RestoreSnapshot(LogicPersistentIdAllocatorSnapshot snapshot)
    {
        EnsureActive();
        s_LastBuildingInstanceValue = snapshot.LastBuildingInstanceValue;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicPersistentIdAllocator operation failed: allocator is not active.");
    }
}
