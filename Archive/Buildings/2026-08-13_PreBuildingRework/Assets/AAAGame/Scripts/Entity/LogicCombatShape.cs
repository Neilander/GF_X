using System;
using System.Collections.Generic;
using UnityEngine;

public enum LogicCombatShapeKind
{
    Circle = 0,
    AxisAlignedBox = 1,
}

public readonly struct LogicCombatShape
{
    private LogicCombatShape(LogicCombatShapeKind kind, FixVector2 center, Fix64 radius, FixVector2 halfExtents)
    {
        Kind = kind;
        Center = center;
        Radius = radius;
        HalfExtents = halfExtents;
    }

    public LogicCombatShapeKind Kind { get; }
    public FixVector2 Center { get; }
    public Fix64 Radius { get; }
    public FixVector2 HalfExtents { get; }

    public static LogicCombatShape Circle(FixVector2 center, Fix64 radius)
    {
        if (radius < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius));
        return new LogicCombatShape(LogicCombatShapeKind.Circle, center, radius, FixVector2.Zero);
    }

    public static LogicCombatShape AxisAlignedBox(FixVector2 center, FixVector2 halfExtents)
    {
        if (halfExtents.x <= Fix64.Zero || halfExtents.y <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Combat box half extents must be positive.");
        return new LogicCombatShape(LogicCombatShapeKind.AxisAlignedBox, center, Fix64.Zero, halfExtents);
    }

    public Fix64 DistanceToSurface(FixVector2 point)
    {
        switch (Kind)
        {
            case LogicCombatShapeKind.Circle:
                Fix64 circleDistance = FixVector2.Distance(point, Center) - Radius;
                return circleDistance > Fix64.Zero ? circleDistance : Fix64.Zero;
            case LogicCombatShapeKind.AxisAlignedBox:
                Fix64 dx = Fix64.Abs(point.x - Center.x) - HalfExtents.x;
                Fix64 dz = Fix64.Abs(point.y - Center.y) - HalfExtents.y;
                dx = dx > Fix64.Zero ? dx : Fix64.Zero;
                dz = dz > Fix64.Zero ? dz : Fix64.Zero;
                return FixVector2.Magnitude(new FixVector2(dx, dz));
            default:
                throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown combat shape kind.");
        }
    }

    public FixVector2 ClosestPoint(FixVector2 point)
    {
        switch (Kind)
        {
            case LogicCombatShapeKind.Circle:
                FixVector2 offset = point - Center;
                if (offset.y == Fix64.Zero)
                {
                    if (offset.x == Fix64.Zero || Fix64.Abs(offset.x) <= Radius)
                        return point;
                    return new FixVector2(
                        Center.x + (offset.x < Fix64.Zero ? -Radius : Radius),
                        Center.y);
                }
                if (offset.x == Fix64.Zero)
                {
                    if (Fix64.Abs(offset.y) <= Radius)
                        return point;
                    return new FixVector2(
                        Center.x,
                        Center.y + (offset.y < Fix64.Zero ? -Radius : Radius));
                }
                Fix64 magnitude = FixVector2.Magnitude(offset);
                if (magnitude <= Radius)
                    return point;
                if (magnitude == Fix64.Zero)
                    return Center;
                return Center + offset * (Radius / magnitude);
            case LogicCombatShapeKind.AxisAlignedBox:
                return new FixVector2(
                    Fix64.Clamp(point.x, Center.x - HalfExtents.x, Center.x + HalfExtents.x),
                    Fix64.Clamp(point.y, Center.y - HalfExtents.y, Center.y + HalfExtents.y));
            default:
                throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown combat shape kind.");
        }
    }
}

public sealed class BuildingCombatShapeCatalog
{
    [Serializable]
    public sealed class Entry
    {
        public string PrefabPath;
        public long CenterXRaw;
        public long CenterZRaw;
        public long HalfExtentXRaw;
        public long HalfExtentZRaw;
    }

    private const string ResourcePath = "BuildingCombatShapeCatalogData";
    private static BuildingCombatShapeCatalog s_Cached;
    private Dictionary<string, Entry> m_ByPrefabPath;

    public List<Entry> Entries { get; private set; }

    public static void PrepareRuntimeDependencies()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Building combat shape catalog cannot be prepared during a logic frame.");
        if (s_Cached != null)
            return;

        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
            throw new InvalidOperationException($"Building combat shape catalog is missing at Resources/{ResourcePath}.json.");
        List<Entry> entries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Entry>>(asset.text);
        if (entries == null)
            throw new InvalidOperationException("Building combat shape catalog JSON did not contain an entry array.");
        s_Cached = new BuildingCombatShapeCatalog { Entries = entries };
        s_Cached.EnsureIndex();
    }

    public static BuildingCombatShapeCatalog LoadRequired()
    {
        if (s_Cached == null)
        {
            if (LogicFrameRuntime.IsExecutingFrame)
                throw new InvalidOperationException("Building combat shape catalog was not prepared before the logic frame.");
            PrepareRuntimeDependencies();
        }
        s_Cached.EnsureIndex();
        return s_Cached;
    }

    public LogicCombatShape ResolveRequired(string prefabPath, Vector3 worldPosition, float worldYawDegrees)
    {
        return ResolveRequired(
            prefabPath,
            new FixVector2((Fix64)worldPosition.x, (Fix64)worldPosition.z),
            BuildingAuthoredShapeTransform.ResolveQuarterTurn(worldYawDegrees));
    }

    public LogicCombatShape ResolveRequired(string prefabPath, FixVector2 worldPosition, int quarterTurn)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            throw new ArgumentException("Building combat shape requires a prefab path.", nameof(prefabPath));
        EnsureIndex();
        if (!m_ByPrefabPath.TryGetValue(prefabPath, out Entry entry))
            throw new InvalidOperationException($"Building combat shape is not authored for prefab '{prefabPath}'.");

        FixVector2 localCenter = new FixVector2(Fix64.FromRaw(entry.CenterXRaw), Fix64.FromRaw(entry.CenterZRaw));
        FixVector2 localHalfExtents = new FixVector2(Fix64.FromRaw(entry.HalfExtentXRaw), Fix64.FromRaw(entry.HalfExtentZRaw));
        if (localHalfExtents.x <= Fix64.Zero || localHalfExtents.y <= Fix64.Zero)
            throw new InvalidOperationException($"Building combat shape '{prefabPath}' has invalid half extents.");
        return BuildingAuthoredShapeTransform.ResolveBox(localCenter, localHalfExtents, worldPosition, quarterTurn);
    }

    private void EnsureIndex()
    {
        if (m_ByPrefabPath != null)
            return;
        m_ByPrefabPath = new Dictionary<string, Entry>(StringComparer.Ordinal);
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry entry = Entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabPath))
                throw new InvalidOperationException($"Building combat shape entry {i} is invalid.");
            if (!m_ByPrefabPath.TryAdd(entry.PrefabPath, entry))
                throw new InvalidOperationException($"Duplicate building combat shape prefab path '{entry.PrefabPath}'.");
        }
    }

}

public sealed class BuildingLogicObstacleShapeCatalog
{
    [Serializable]
    public sealed class BoxEntry
    {
        public long CenterXRaw;
        public long CenterZRaw;
        public long HalfExtentXRaw;
        public long HalfExtentZRaw;
    }

    [Serializable]
    public sealed class PrefabEntry
    {
        public string PrefabPath;
        public List<BoxEntry> Boxes;
    }

    private const string ResourcePath = "BuildingLogicObstacleShapeCatalogData";
    private static BuildingLogicObstacleShapeCatalog s_Cached;
    private Dictionary<string, PrefabEntry> m_ByPrefabPath;

    public List<PrefabEntry> Entries { get; private set; }

    public static void PrepareRuntimeDependencies()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Building logic obstacle catalog cannot be prepared during a logic frame.");
        if (s_Cached != null)
            return;

        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
            throw new InvalidOperationException($"Building logic obstacle catalog is missing at Resources/{ResourcePath}.json.");
        List<PrefabEntry> entries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<PrefabEntry>>(asset.text);
        if (entries == null)
            throw new InvalidOperationException("Building logic obstacle catalog JSON did not contain an entry array.");
        s_Cached = new BuildingLogicObstacleShapeCatalog { Entries = entries };
        s_Cached.EnsureIndex();
    }

    public static BuildingLogicObstacleShapeCatalog LoadRequired()
    {
        if (s_Cached == null)
        {
            if (LogicFrameRuntime.IsExecutingFrame)
                throw new InvalidOperationException("Building logic obstacle catalog was not prepared before the logic frame.");
            PrepareRuntimeDependencies();
        }
        s_Cached.EnsureIndex();
        return s_Cached;
    }

    public IReadOnlyList<LogicCombatShape> ResolveRequired(string prefabPath, Vector3 worldPosition, float worldYawDegrees)
    {
        return ResolveRequired(
            prefabPath,
            new FixVector2((Fix64)worldPosition.x, (Fix64)worldPosition.z),
            BuildingAuthoredShapeTransform.ResolveQuarterTurn(worldYawDegrees));
    }

    public IReadOnlyList<LogicCombatShape> ResolveRequired(string prefabPath, FixVector2 worldPosition, int quarterTurn)
    {
        EnsureIndex();
        if (!m_ByPrefabPath.TryGetValue(prefabPath, out PrefabEntry entry))
            throw new InvalidOperationException($"Building logic obstacle shapes are not authored for prefab '{prefabPath}'.");
        if (entry.Boxes == null)
            throw new InvalidOperationException($"Building logic obstacle entry '{prefabPath}' has a null box list.");

        var result = new LogicCombatShape[entry.Boxes.Count];
        for (int i = 0; i < entry.Boxes.Count; i++)
        {
            BoxEntry box = entry.Boxes[i];
            if (box == null)
                throw new InvalidOperationException($"Building logic obstacle entry '{prefabPath}' contains a null box.");
            FixVector2 localCenter = new FixVector2(Fix64.FromRaw(box.CenterXRaw), Fix64.FromRaw(box.CenterZRaw));
            FixVector2 localHalfExtents = new FixVector2(Fix64.FromRaw(box.HalfExtentXRaw), Fix64.FromRaw(box.HalfExtentZRaw));
            result[i] = BuildingAuthoredShapeTransform.ResolveBox(localCenter, localHalfExtents, worldPosition, quarterTurn);
        }
        return result;
    }

    private void EnsureIndex()
    {
        if (m_ByPrefabPath != null)
            return;
        m_ByPrefabPath = new Dictionary<string, PrefabEntry>(StringComparer.Ordinal);
        for (int i = 0; i < Entries.Count; i++)
        {
            PrefabEntry entry = Entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabPath))
                throw new InvalidOperationException($"Building logic obstacle entry {i} is invalid.");
            if (!m_ByPrefabPath.TryAdd(entry.PrefabPath, entry))
                throw new InvalidOperationException($"Duplicate building logic obstacle prefab path '{entry.PrefabPath}'.");
        }
    }
}

public static class BuildingAuthoredShapeTransform
{
    public static LogicCombatShape ResolveBox(
        FixVector2 localCenter,
        FixVector2 localHalfExtents,
        Vector3 worldPosition,
        float worldYawDegrees)
    {
        return ResolveBox(
            localCenter,
            localHalfExtents,
            new FixVector2((Fix64)worldPosition.x, (Fix64)worldPosition.z),
            ResolveQuarterTurn(worldYawDegrees));
    }

    public static LogicCombatShape ResolveBox(
        FixVector2 localCenter,
        FixVector2 localHalfExtents,
        FixVector2 worldPosition,
        int quarterTurn)
    {
        if (localHalfExtents.x <= Fix64.Zero || localHalfExtents.y <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(localHalfExtents));
        if (quarterTurn < 0 || quarterTurn > 3)
            throw new ArgumentOutOfRangeException(nameof(quarterTurn));
        FixVector2 rotatedCenter;
        FixVector2 worldHalfExtents;
        switch (quarterTurn)
        {
            case 0:
                rotatedCenter = localCenter;
                worldHalfExtents = localHalfExtents;
                break;
            case 1:
                rotatedCenter = new FixVector2(localCenter.y, -localCenter.x);
                worldHalfExtents = new FixVector2(localHalfExtents.y, localHalfExtents.x);
                break;
            case 2:
                rotatedCenter = new FixVector2(-localCenter.x, -localCenter.y);
                worldHalfExtents = localHalfExtents;
                break;
            case 3:
                rotatedCenter = new FixVector2(-localCenter.y, localCenter.x);
                worldHalfExtents = new FixVector2(localHalfExtents.y, localHalfExtents.x);
                break;
            default:
                throw new InvalidOperationException("Invalid building quarter turn.");
        }
        return LogicCombatShape.AxisAlignedBox(worldPosition + rotatedCenter, worldHalfExtents);
    }

    public static int ResolveQuarterTurn(float yawDegrees)
    {
        if (float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees))
            throw new ArgumentOutOfRangeException(nameof(yawDegrees));
        int rounded = Mathf.RoundToInt(yawDegrees / 90f);
        float snapped = rounded * 90f;
        if (Mathf.Abs(Mathf.DeltaAngle(yawDegrees, snapped)) > 0.01f)
            throw new InvalidOperationException($"Building combat shape requires a quarter-turn yaw. yaw={yawDegrees:R}.");
        int quarterTurn = rounded % 4;
        return quarterTurn < 0 ? quarterTurn + 4 : quarterTurn;
    }
}
