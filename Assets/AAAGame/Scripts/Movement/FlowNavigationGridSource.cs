using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public sealed class FlowNavigationGridSource : MonoBehaviour
{
    private static int s_AppliedSourceInstanceId;
    private static int s_PendingClearSourceInstanceId;

    [SerializeField] private FlowNavigationGridAsset _grid;
    [SerializeField] private FlowNavigationGridAsset[] _movementTypeGrids = Array.Empty<FlowNavigationGridAsset>();
    [SerializeField] private bool _applyOnEnable = true;
    [SerializeField] private bool _applyInEditMode;
    [SerializeField] private bool _clearOnDisable = true;
    [SerializeField] private bool _drawGizmos = true;
    [SerializeField] private int _maxGizmoCells = 4096;

    public FlowNavigationGridAsset Grid => _grid;
    public IReadOnlyList<FlowNavigationGridAsset> MovementTypeGrids => _movementTypeGrids;
    public bool ApplyInEditMode => _applyInEditMode;

    public void Configure(FlowNavigationGridAsset grid, bool applyOnEnable, bool applyInEditMode, bool clearOnDisable)
    {
        Configure(grid, Array.Empty<FlowNavigationGridAsset>(), applyOnEnable, applyInEditMode, clearOnDisable);
    }

    public void Configure(FlowNavigationGridAsset grid, IReadOnlyList<FlowNavigationGridAsset> movementTypeGrids, bool applyOnEnable, bool applyInEditMode, bool clearOnDisable)
    {
        if (grid == null)
            throw new InvalidOperationException("FlowNavigationGridSource.Configure failed: grid is null.");

        _grid = grid;
        _movementTypeGrids = movementTypeGrids != null ? CopyMovementTypeGrids(movementTypeGrids, grid) : Array.Empty<FlowNavigationGridAsset>();
        _applyOnEnable = applyOnEnable;
        _applyInEditMode = applyInEditMode;
        _clearOnDisable = clearOnDisable;
    }

    private void OnEnable()
    {
        CancelPendingClearIfOwned();
        if (!_applyOnEnable)
            return;

        bool isPlaying = Application.isPlaying;
        if (!ShouldApplyOnEnable(isPlaying, _applyInEditMode, LogicFrameRuntime.IsActive))
        {
            if (isPlaying)
            {
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    this,
                    "[FlowNavigationGridSource] Automatic runtime apply skipped because the logic runtime is not active. source={0} id={1}",
                    name,
                    GetInstanceID());
            }

            return;
        }

        ApplyToFlowField();
    }

    private void OnDisable()
    {
        if (_clearOnDisable && (Application.isPlaying || _applyInEditMode))
            ClearAppliedSourceIfOwned();
    }

    private void OnValidate()
    {
        if (_applyInEditMode && isActiveAndEnabled)
            ApplyToFlowField();
    }

    private static bool ShouldApplyOnEnable(bool isPlaying, bool applyInEditMode, bool logicRuntimeActive)
    {
        return isPlaying ? logicRuntimeActive : applyInEditMode;
    }

    [ContextMenu("Apply To Flow Field")]
    public void ApplyToFlowField()
    {
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        List<FlowNavigationGridAsset> grids = CollectConfiguredGrids();
        if (grids.Count == 0)
            throw new InvalidOperationException("FlowNavigationGridSource.ApplyToFlowField failed: no grid is configured.");

        if (grids.Count == 1)
        {
            FlowNavigationGridAsset grid = grids[0];
            FlowNavigationGridAsset.DerivedNavigationData derivedData = RequireDerivedNavigationData(grid);
            FlowNavigationGridAsset.FixedAuthorityMetadata fixedMetadata = grid.GetFixedAuthorityMetadata();
            FlowFieldCrowdMovementSystem.SetAuthoredNavigationSourceFixed(
                grid.AgentTypeId,
                grid.Width,
                grid.Height,
                grid.CellSize,
                grid.Origin,
                fixedMetadata.CellSizeGridRaw,
                fixedMetadata.OriginXGridRaw,
                fixedMetadata.OriginZGridRaw,
                grid.GetWalkableMaskRuntimeReadOnlyReference(),
                grid.GetCellAnchorsRuntimeReadOnlyReferenceOrNull(),
                grid.GetCellAnchorsFixedRuntimeReadOnlyReference(),
                grid.GetCostFieldRuntimeReadOnlyReference(),
                grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
                derivedData,
                useRuntimeReadOnlyReferences: true);
            s_AppliedSourceInstanceId = GetInstanceID();
            LogSourceLifecycle("apply-single", grids);
            stopwatch.Stop();
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[FlowNavigationGridSourceTiming] action=apply-single elapsedMs={0:F3} grids={1}",
                stopwatch.Elapsed.TotalMilliseconds,
                FormatGridSummary(grids));
            return;
        }

        AuthoredNavigationSourceData[] sources = new AuthoredNavigationSourceData[grids.Count];
        HashSet<int> agentTypeIds = new HashSet<int>();
        for (int i = 0; i < grids.Count; i++)
        {
            FlowNavigationGridAsset grid = grids[i];
            if (!agentTypeIds.Add(grid.AgentTypeId))
                throw new InvalidOperationException($"FlowNavigationGridSource.ApplyToFlowField failed: duplicate grid agentTypeId={grid.AgentTypeId}.");

            FlowNavigationGridAsset.FixedAuthorityMetadata fixedMetadata = grid.GetFixedAuthorityMetadata();
            sources[i] = new AuthoredNavigationSourceData(
                grid.AgentTypeId,
                grid.Width,
                grid.Height,
                grid.CellSize,
                grid.Origin,
                grid.GetWalkableMaskRuntimeReadOnlyReference(),
                grid.GetCellAnchorsRuntimeReadOnlyReferenceOrNull(),
                grid.GetCostFieldRuntimeReadOnlyReference(),
                grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
                RequireDerivedNavigationData(grid),
                useRuntimeReadOnlyReferences: true,
                cellSizeGridRaw: fixedMetadata.CellSizeGridRaw,
                originXGridRaw: fixedMetadata.OriginXGridRaw,
                originZGridRaw: fixedMetadata.OriginZGridRaw,
                cellNavAnchorsFixedXZ: grid.GetCellAnchorsFixedRuntimeReadOnlyReference(),
                hasFixedAuthorityPayload: true);
        }

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(sources);
        s_AppliedSourceInstanceId = GetInstanceID();
        LogSourceLifecycle("apply-multiple", grids);
        stopwatch.Stop();
        Debug.LogFormat(
            LogType.Log,
            LogOption.NoStacktrace,
            null,
            "[FlowNavigationGridSourceTiming] action=apply-multiple elapsedMs={0:F3} grids={1}",
            stopwatch.Elapsed.TotalMilliseconds,
            FormatGridSummary(grids));
    }

    [ContextMenu("Clear Flow Field Source")]
    public void ClearFlowFieldSource()
    {
        ClearAppliedSourceIfOwned();
    }

    private void ClearAppliedSourceIfOwned()
    {
        if (s_AppliedSourceInstanceId != GetInstanceID())
        {
            LogSourceLifecycle("skip-clear-not-owner", CollectConfiguredGrids());
            return;
        }

        if (LogicFrameRuntime.IsTimelineRunning && !FlowFieldCrowdMovementSystem.IsRuntimeNavigationTransitionActive())
        {
            if (s_PendingClearSourceInstanceId != 0 && s_PendingClearSourceInstanceId != s_AppliedSourceInstanceId)
            {
                throw new InvalidOperationException(
                    $"FlowNavigationGridSource deferred clear owner conflict. applied={s_AppliedSourceInstanceId}, pending={s_PendingClearSourceInstanceId}.");
            }

            s_PendingClearSourceInstanceId = s_AppliedSourceInstanceId;
            LogicFrameRuntime.Ended -= HandleLogicRuntimeEnded;
            LogicFrameRuntime.Ended += HandleLogicRuntimeEnded;
            LogSourceLifecycle("defer-clear-until-runtime-ended", CollectConfiguredGrids());
            return;
        }

        LogicFrameRuntime.Ended -= HandleLogicRuntimeEnded;
        s_PendingClearSourceInstanceId = 0;
        FlowFieldCrowdMovementSystem.ClearAuthoredNavigationSource();
        s_AppliedSourceInstanceId = 0;
        LogSourceLifecycle("clear-owned", CollectConfiguredGrids());
    }

    private void CancelPendingClearIfOwned()
    {
        int instanceId = GetInstanceID();
        if (s_PendingClearSourceInstanceId != instanceId)
            return;

        s_PendingClearSourceInstanceId = 0;
        LogicFrameRuntime.Ended -= HandleLogicRuntimeEnded;
    }

    private static void HandleLogicRuntimeEnded()
    {
        LogicFrameRuntime.Ended -= HandleLogicRuntimeEnded;
        int pendingOwner = s_PendingClearSourceInstanceId;
        s_PendingClearSourceInstanceId = 0;
        if (pendingOwner == 0)
            throw new InvalidOperationException("FlowNavigationGridSource runtime-ended clear has no pending owner.");
        if (s_AppliedSourceInstanceId != pendingOwner)
        {
            throw new InvalidOperationException(
                $"FlowNavigationGridSource runtime-ended clear owner changed. applied={s_AppliedSourceInstanceId}, pending={pendingOwner}.");
        }

        FlowFieldCrowdMovementSystem.ClearAuthoredNavigationSource();
        s_AppliedSourceInstanceId = 0;
    }

    private void LogSourceLifecycle(string action, IReadOnlyList<FlowNavigationGridAsset> grids)
    {
        if (!Application.isPlaying || !GameDebugSettings.IsEnabled(DebugCategory.Move))
            return;

        string gridSummary = FormatGridSummary(grids);
        GameDebugSettings.Log(
            DebugCategory.Move,
            $"[FlowNavigationGridSource] action={action} source={name} id={GetInstanceID()} owner={s_AppliedSourceInstanceId} active={isActiveAndEnabled} grids={gridSummary}");
    }

    private static string FormatGridSummary(IReadOnlyList<FlowNavigationGridAsset> grids)
    {
        if (grids == null || grids.Count == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(96);
        builder.Append('[');
        for (int i = 0; i < grids.Count; i++)
        {
            if (i > 0)
                builder.Append(';');

            FlowNavigationGridAsset grid = grids[i];
            if (grid == null)
            {
                builder.Append("null");
                continue;
            }

            builder.Append(grid.name)
                .Append(":agent=").Append(grid.AgentTypeId)
                .Append(":size=").Append(grid.Width).Append('x').Append(grid.Height);
        }

        builder.Append(']');
        return builder.ToString();
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos || _grid == null)
            return;

        int cellCount = _grid.CellCount;
        if (cellCount > _maxGizmoCells)
            return;

        float size = _grid.CellSize * 0.92f;
        Vector3 cubeSize = new Vector3(size, 0.02f, size);
        for (int y = 0; y < _grid.Height; y++)
        {
            for (int x = 0; x < _grid.Width; x++)
            {
                Gizmos.color = _grid.IsCellWalkable(x, y)
                    ? new Color(0.1f, 0.8f, 0.25f, 0.22f)
                    : new Color(0.9f, 0.1f, 0.1f, 0.22f);
                Gizmos.DrawCube(_grid.GetCellCenter(x, y), cubeSize);
            }
        }

        Gizmos.color = new Color(1f, 1f, 1f, 0.45f);
        Vector3 center = _grid.Origin + new Vector3(_grid.Width * _grid.CellSize * 0.5f, 0f, _grid.Height * _grid.CellSize * 0.5f);
        Vector3 boundsSize = new Vector3(_grid.Width * _grid.CellSize, 0.02f, _grid.Height * _grid.CellSize);
        Gizmos.DrawWireCube(center, boundsSize);
    }

    private List<FlowNavigationGridAsset> CollectConfiguredGrids()
    {
        List<FlowNavigationGridAsset> grids = new List<FlowNavigationGridAsset>();
        if (_grid != null)
            grids.Add(_grid);
        if (_movementTypeGrids == null)
            return grids;

        for (int i = 0; i < _movementTypeGrids.Length; i++)
        {
            FlowNavigationGridAsset grid = _movementTypeGrids[i];
            if (grid == null || grids.Contains(grid))
                continue;

            grids.Add(grid);
        }

        return grids;
    }

    private static FlowNavigationGridAsset.DerivedNavigationData RequireDerivedNavigationData(FlowNavigationGridAsset grid)
    {
        if (grid == null)
            throw new InvalidOperationException("FlowNavigationGridSource.RequireDerivedNavigationData failed: grid is null.");

        FlowNavigationGridAsset.DerivedNavigationData data = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        if (data == null || !data.IsValid)
        {
            throw new InvalidOperationException(
                $"FlowNavigationGridSource.ApplyToFlowField failed: grid '{grid.name}' has no valid baked derived navigation data. " +
                "Regenerate the level FlowNavigationGridAsset from the terrain prefab before entering play mode.");
        }

        return data;
    }

    private static FlowNavigationGridAsset[] CopyMovementTypeGrids(IReadOnlyList<FlowNavigationGridAsset> movementTypeGrids, FlowNavigationGridAsset primaryGrid)
    {
        List<FlowNavigationGridAsset> copy = new List<FlowNavigationGridAsset>(movementTypeGrids.Count);
        for (int i = 0; i < movementTypeGrids.Count; i++)
        {
            FlowNavigationGridAsset grid = movementTypeGrids[i];
            if (grid == null || grid == primaryGrid || copy.Contains(grid))
                continue;

            copy.Add(grid);
        }

        return copy.ToArray();
    }
}
