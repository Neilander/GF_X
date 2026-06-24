using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public sealed class FlowNavigationGridSource : MonoBehaviour
{
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
        if (_applyOnEnable && (Application.isPlaying || _applyInEditMode))
            ApplyToFlowField();
    }

    private void OnDisable()
    {
        if (_clearOnDisable && (Application.isPlaying || _applyInEditMode))
            FlowFieldCrowdMovementSystem.ClearAuthoredNavigationSource();
    }

    private void OnValidate()
    {
        if (_applyInEditMode && isActiveAndEnabled)
            ApplyToFlowField();
    }

    [ContextMenu("Apply To Flow Field")]
    public void ApplyToFlowField()
    {
        List<FlowNavigationGridAsset> grids = CollectConfiguredGrids();
        if (grids.Count == 0)
            throw new InvalidOperationException("FlowNavigationGridSource.ApplyToFlowField failed: no grid is configured.");

        if (grids.Count == 1)
        {
            FlowNavigationGridAsset grid = grids[0];
            FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
                grid.AgentTypeId,
                grid.Width,
                grid.Height,
                grid.CellSize,
                grid.Origin,
                grid.CreateWalkableMaskCopy(),
                grid.CreateCellAnchors(),
                grid.CreateCostFieldCopy());
            return;
        }

        AuthoredNavigationSourceData[] sources = new AuthoredNavigationSourceData[grids.Count];
        HashSet<int> agentTypeIds = new HashSet<int>();
        for (int i = 0; i < grids.Count; i++)
        {
            FlowNavigationGridAsset grid = grids[i];
            if (!agentTypeIds.Add(grid.AgentTypeId))
                throw new InvalidOperationException($"FlowNavigationGridSource.ApplyToFlowField failed: duplicate grid agentTypeId={grid.AgentTypeId}.");

            sources[i] = new AuthoredNavigationSourceData(
                grid.AgentTypeId,
                grid.Width,
                grid.Height,
                grid.CellSize,
                grid.Origin,
                grid.CreateWalkableMaskCopy(),
                grid.CreateCellAnchors(),
                grid.CreateCostFieldCopy());
        }

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(sources);
    }

    [ContextMenu("Clear Flow Field Source")]
    public void ClearFlowFieldSource()
    {
        FlowFieldCrowdMovementSystem.ClearAuthoredNavigationSource();
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
