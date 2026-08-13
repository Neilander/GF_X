using System;
using AAAGame.MiniMap;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class ObjectiveDestinationService
{
    private static bool s_IsActive;
    private static bool s_PresentationDirty;
    private static int s_DestinationId;
    private static FixVector2 s_Position;
    private static Fix64 s_WorldRadius;

    public static bool IsActive => s_IsActive;
    public static int ActiveDestinationId => s_IsActive
        ? s_DestinationId
        : throw new InvalidOperationException("No objective destination is active.");
    public static FixVector2 ActivePosition => s_IsActive
        ? s_Position
        : throw new InvalidOperationException("No objective destination is active.");
    public static Fix64 ActiveWorldRadius => s_IsActive
        ? s_WorldRadius
        : throw new InvalidOperationException("No objective destination is active.");

    public static void Activate(int destinationId)
    {
        if (destinationId < 0)
            throw new ArgumentOutOfRangeException(nameof(destinationId), destinationId, "Destination ID must be non-negative.");

        LevelEntity level = LevelEntity.ActiveLevelEntity
                            ?? throw new InvalidOperationException("Activating an objective destination requires an active LevelEntity.");
        EntityPresetPoint[] points = level.GetComponentsInChildren<EntityPresetPoint>(true);
        EntityPresetPoint destination = null;
        for (int i = 0; i < points.Length; i++)
        {
            EntityPresetPoint point = points[i];
            if (point == null
                || point.PointType != EntityPresetPointType.Destination
                || point.DestinationId != destinationId)
            {
                continue;
            }

            if (destination != null)
                throw new InvalidOperationException($"Level contains duplicate Destination ID {destinationId}.");
            destination = point;
        }

        if (destination == null)
            throw new InvalidOperationException($"Level does not contain Destination ID {destinationId}.");
        if (!float.IsFinite(destination.DestinationRadius) || destination.DestinationRadius <= 0f)
        {
            throw new InvalidOperationException(
                $"Destination {destinationId} has invalid configured radius {destination.DestinationRadius}.");
        }

        Fix64 configuredRadius = (Fix64)destination.DestinationRadius;
        Fix64 worldRadius = ConvertConfiguredRadius(configuredRadius);
        Vector3 worldPosition = destination.Position;

        s_DestinationId = destinationId;
        s_Position = new FixVector2((Fix64)worldPosition.x, (Fix64)worldPosition.z);
        s_WorldRadius = worldRadius;
        s_IsActive = true;
        s_PresentationDirty = true;

        Log.Info(
            "[ObjectiveDestination] Activated id={0}, configuredRadius={1}, worldRadius={2}, position=({3},{4}).",
            destinationId,
            destination.DestinationRadius,
            (float)worldRadius,
            worldPosition.x,
            worldPosition.z);
    }

    public static bool IsReached(FixVector2 position)
    {
        if (!s_IsActive)
            throw new InvalidOperationException("Cannot test destination reach state when no destination is active.");
        return Contains(position, s_Position, s_WorldRadius);
    }

    public static bool Contains(FixVector2 position, FixVector2 center, Fix64 radius)
    {
        if (radius <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Destination radius must be positive.");

        Fix64 deltaX = position.x - center.x;
        Fix64 deltaY = position.y - center.y;
        return deltaX * deltaX + deltaY * deltaY <= radius * radius;
    }

    public static Fix64 ConvertConfiguredRadius(Fix64 configuredRadius)
    {
        if (configuredRadius <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(configuredRadius), configuredRadius, "Configured destination radius must be positive.");
        return DistanceUnitConverter.ConvertToWorld(configuredRadius);
    }

    public static void Clear()
    {
        if (!s_IsActive)
            return;

        s_IsActive = false;
        s_DestinationId = 0;
        s_Position = FixVector2.Zero;
        s_WorldRadius = Fix64.Zero;
        s_PresentationDirty = true;
    }

    public static void PublishPendingPresentation()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Objective destination presentation cannot run during a logic frame.");
        if (!s_PresentationDirty)
            return;

        s_PresentationDirty = false;
        if (s_IsActive)
        {
            ObjectiveDestinationPresenter.Show(
                s_DestinationId,
                new Vector3((float)s_Position.x, 0f, (float)s_Position.y),
                (float)s_WorldRadius);
        }
        else
        {
            ObjectiveDestinationPresenter.Hide();
        }
    }
}

internal static class ObjectiveDestinationPresenter
{
    private const int CircleSegments = 96;
    private static readonly Color ObjectiveGreen = new Color(0.2f, 1f, 0.25f, 0.9f);

    private static GameObject s_Root;
    private static Material s_Material;
    private static int s_MinimapUnitId = -1;

    public static void Show(int destinationId, Vector3 worldPosition, float worldRadius)
    {
        if (!float.IsFinite(worldRadius) || worldRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(worldRadius), worldRadius, "Destination world radius must be positive.");

        LevelEntity level = LevelEntity.ActiveLevelEntity
                            ?? throw new InvalidOperationException("Objective destination presentation requires an active LevelEntity.");
        MinimapManager minimap = GameEntry.GetComponent<MinimapManager>()
                                 ?? throw new InvalidOperationException("Objective destination presentation requires MinimapManager.");
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            throw new InvalidOperationException("Objective destination preview requires the Sprites/Default shader.");

        bool hasRoot = s_Root != null;
        bool hasMinimapMarker = s_MinimapUnitId >= 0;
        if (hasRoot != hasMinimapMarker)
            throw new InvalidOperationException("Objective destination presentation state is inconsistent.");
        if (hasRoot)
            Hide();

        s_Root = new GameObject($"ObjectiveDestination_{destinationId}");
        s_Root.transform.position = worldPosition;
        s_Root.transform.SetParent(level.transform, true);

        LineRenderer line = s_Root.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = CircleSegments;
        line.widthMultiplier = 0.12f;
        s_Material = new Material(shader);
        line.material = s_Material;
        line.startColor = ObjectiveGreen;
        line.endColor = ObjectiveGreen;
        line.numCapVertices = 2;
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / CircleSegments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * worldRadius, 0.12f, Mathf.Sin(angle) * worldRadius));
        }

        s_MinimapUnitId = minimap.RegisterUnit(
            worldPosition,
            SideType.NoSide,
            MinimapUnitType.Objective);
    }

    public static void Hide()
    {
        if (s_MinimapUnitId >= 0)
        {
            MinimapManager minimap = GameEntry.GetComponent<MinimapManager>()
                                     ?? throw new InvalidOperationException("Cannot remove objective destination marker without MinimapManager.");
            minimap.UnregisterUnit(s_MinimapUnitId);
            s_MinimapUnitId = -1;
        }

        if (s_Root != null)
            UnityEngine.Object.Destroy(s_Root);
        if (s_Material != null)
            UnityEngine.Object.Destroy(s_Material);
        s_Root = null;
        s_Material = null;
    }
}
