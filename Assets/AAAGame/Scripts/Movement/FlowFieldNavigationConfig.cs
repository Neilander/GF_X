using UnityEngine;

[CreateAssetMenu(fileName = "FlowFieldNavigationConfig", menuName = "Movement/Flow Field Navigation Config")]
public class FlowFieldNavigationConfig : ScriptableObject
{
    [Header("Flow Field")]
    [Tooltip("Must be provided by FlowNavigationGridSource; missing authored grids are errors.")]
    public bool RequireAuthoredNavigationSource = true;
    [Tooltip("Navigation cell size. <= 0 uses the FlowNavigationGridAsset cell size.")]
    [Min(0f)]
    public float NavigationCellSize = 0f;
    [Tooltip("Legacy-compatible bounds padding. Authored grid runtime does not rebuild bounds from scene geometry.")]
    [Min(0f)]
    public float NavigationBoundsPadding = 0.6f;
    [Tooltip("Cell count per sector edge.")]
    [Min(4)]
    public int SectorSizeInCells = 12;
    [Tooltip("Portal width at or below this cell count is treated as a bottleneck.")]
    [Min(1)]
    public int PortalNarrowWidthCells = 2;
    [Tooltip("Wide portal windows are split at this cell width to keep graph anchors precise.")]
    [Min(2)]
    public int PortalMaxWindowWidthCells = 6;
    [Tooltip("Maximum cached flow tiles.")]
    [Min(16)]
    public int FlowTileCacheLimit = 256;
    [Tooltip("Runtime dirty navigation rebuild budget in milliseconds per frame.")]
    [Min(0.05f)]
    public float RuntimeRebuildBudgetMilliseconds = 1.5f;

    [Header("Crowd Steering")]
    [Tooltip("Neighbor prediction time.")]
    [Range(0.05f, 1.5f)]
    public float CrowdPredictionTime = 0.35f;
    [Tooltip("Stable side bias inside bottlenecks.")]
    [Range(0f, 1f)]
    public float LaneBiasStrength = 0.22f;
    [Tooltip("Local boundary avoidance weight.")]
    [Min(0f)]
    public float BoundaryAvoidanceWeight = 1f;
    [Tooltip("Blend between flow direction and local path direction. 1 = stronger local path following.")]
    [Range(0f, 1f)]
    public float PathDirectionBlend = 0.35f;

    [Header("Bottleneck")]
    [Tooltip("Cooldown before switching bottleneck direction.")]
    [Min(0f)]
    public float BottleneckSwitchCooldown = 0.35f;
    [Tooltip("Waiting time before a unit may take bottleneck ownership.")]
    [Min(0.1f)]
    public float BottleneckWaitTimeout = 1.25f;
    [Tooltip("Distance from bottleneck anchor where scheduling starts to affect units.")]
    [Min(0.1f)]
    public float BottleneckInfluenceDistance = 2.2f;
    [Tooltip("How long a bottleneck keeps ownership for trailing same-direction traffic.")]
    [Min(0.05f)]
    public float BottleneckClearanceHoldTime = 0.45f;

    [Header("Debug")]
    public bool DrawNavigationDebug = false;
    public bool DrawFlowFieldDebug = false;
}
