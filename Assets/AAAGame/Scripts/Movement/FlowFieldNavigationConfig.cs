using UnityEngine;

[CreateAssetMenu(fileName = "FlowFieldNavigationConfig", menuName = "Movement/Flow Field Navigation Config")]
public class FlowFieldNavigationConfig : ScriptableObject
{
    [Header("Flow Field")]
    [Tooltip("Must be provided by FlowNavigationGridSource; missing authored grids are errors.")]
    public bool RequireAuthoredNavigationSource = true;
    [Tooltip("Sector edge length in world-space millimeters. Runtime cell count is derived from each authored grid's fixed cell size.")]
    [Min(1)]
    public int SectorWorldSizeMillimeters = 3600;
#if UNITY_EDITOR
    [System.NonSerialized]
    public int EditorTestSectorSizeInCells;
#endif
    [Tooltip("Portal width at or below this cell count is treated as a bottleneck.")]
    [Min(1)]
    public int PortalNarrowWidthCells = 2;
    [Tooltip("Wide portal windows are split at this cell width to keep graph anchors precise.")]
    [Min(2)]
    public int PortalMaxWindowWidthCells = 6;
    [Tooltip("Maximum cached flow tiles.")]
    [Min(16)]
    public int FlowTileCacheLimit = 256;
    [Tooltip("Maximum world-build operations processed per logic Tick.")]
    [Min(1)]
    public int WorldBuildOperationQuota = 32768;
    [Tooltip("Maximum runtime-dirty rebuild operations processed per logic Tick.")]
    [Min(1)]
    public int RuntimeRebuildOperationQuota = 512;
    [Tooltip("Maximum deterministic flow tiles committed per logic Tick before float shadow work.")]
    [Min(1)]
    public int DeterministicFlowTileCommitQuota = 8;
    [Tooltip("Maximum deterministic flow-tile cell operations processed per logic Tick.")]
    [Min(1)]
    public int FlowTileBuildOperationQuota = 2048;
    [Tooltip("Maximum deterministic shared-goal graph operations processed per logic Tick.")]
    [Min(1)]
    public int SharedGoalBuildOperationQuota = 2048;
    [Tooltip("Maximum deterministic multi-source portal path operations processed per logic Tick.")]
    [Min(1)]
    public int PathRequestOperationQuota = 2048;
    [Header("Debug")]
    [Tooltip("Run the fixed-point static collision solver in shadow mode without changing authoritative movement.")]
    public bool EnableDeterministicStaticCollisionShadow = true;
    [Tooltip("World-space displacement difference that counts as a static collision shadow mismatch.")]
    [Min(0f)]
    public float StaticCollisionShadowMismatchTolerance = 0.03f;
    [Tooltip("Minimum logic Tick interval between static collision shadow mismatch logs.")]
    [Min(1)]
    public int StaticCollisionShadowLogIntervalTicks = 300;
    public bool DrawNavigationDebug = false;
    public bool DrawFlowFieldDebug = false;
}
