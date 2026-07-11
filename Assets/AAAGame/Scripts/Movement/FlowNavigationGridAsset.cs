using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FlowNavigationGrid", menuName = "Movement/Flow Navigation Grid")]
public sealed class FlowNavigationGridAsset : ScriptableObject
{
    [SerializeField] private int _agentTypeId = int.MinValue + 1;
    [SerializeField] private int _width = 16;
    [SerializeField] private int _height = 16;
    [SerializeField] private float _cellSize = 1f;
    [SerializeField] private Vector3 _origin;
    [SerializeField] private bool[] _walkable = new bool[16 * 16];
    [SerializeField] private byte[] _costs = new byte[16 * 16];
    [SerializeField] private Vector3[] _cellAnchors = new Vector3[16 * 16];
    [SerializeField] private int[] _sparseCellAnchorIndices = Array.Empty<int>();
    [SerializeField] private Vector3[] _sparseCellAnchorValues = Array.Empty<Vector3>();
    [SerializeField] private byte[] _neighborTraversalMask = Array.Empty<byte>();
    [SerializeField] private bool _hasAuthoredNeighborTraversalMask;
    [SerializeField] private DerivedNavigationData _derivedNavigationData;

    [NonSerialized] private Vector3[] _runtimeCellAnchorsCache;

    public int AgentTypeId => _agentTypeId;
    public int Width => _width;
    public int Height => _height;
    public float CellSize => _cellSize;
    public Vector3 Origin => _origin;
    public int CellCount => _width * _height;
    public bool HasDerivedNavigationData => _derivedNavigationData != null && _derivedNavigationData.IsValid;
    public bool HasAuthoredCellAnchors
    {
        get
        {
            EnsureValidStorage();
            return HasDenseCellAnchors()
                   || (_sparseCellAnchorIndices != null && _sparseCellAnchorIndices.Length > 0);
        }
    }

    [Serializable]
    public sealed class DerivedNavigationData
    {
        public const int CurrentVersion = 2;

        public int Version;
        public int AgentTypeId;
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public int ConfigSectorSizeInCells;
        public int ConfigPortalNarrowWidthCells;
        public int ConfigPortalMaxWindowWidthCells;
        public int SectorSizeInCells;
        public int SectorCountX;
        public int SectorCountY;
        public int IslandCount;
        public int MainIslandId;
        public int MainIslandSize;
        public int NextPortalId;
        public int[] IslandIds = Array.Empty<int>();
        public SectorDerivedData[] Sectors = Array.Empty<SectorDerivedData>();
        public PortalDerivedData[] Portals = Array.Empty<PortalDerivedData>();

        public bool IsValid => Version == CurrentVersion
                               && Width > 0
                               && Height > 0
                               && CellSize > 0.0001f
                               && SectorSizeInCells > 0
                               && SectorCountX > 0
                               && SectorCountY > 0
                               && IslandIds != null
                               && IslandIds.Length == Width * Height
                               && Sectors != null
                               && Sectors.Length == SectorCountX * SectorCountY
                               && Portals != null;
    }

    [Serializable]
    public sealed class SectorDerivedData
    {
        public int SectorId;
        public int StartX;
        public int StartY;
        public int Width;
        public int Height;
        public Vector3 Center;
        public int DirtyVersion;
        public bool IsClearCostField;
        public bool IsClearFlowTile;
        public int UniformIslandId;
        public int[] LocalComponentIds = Array.Empty<int>();
        public int LocalComponentCount;
        public int[] PortalIds = Array.Empty<int>();
        public PortalTransitionDerivedData[] PortalTransitions = Array.Empty<PortalTransitionDerivedData>();
    }

    [Serializable]
    public sealed class PortalDerivedData
    {
        public int PortalId;
        public int SectorAId;
        public int SectorBId;
        public Vector2Int[] CellsA = Array.Empty<Vector2Int>();
        public Vector2Int[] CellsB = Array.Empty<Vector2Int>();
        public Vector3 WorldCenter;
        public int WidthCells;
        public bool IsNarrow;
        public bool IsVerticalBoundary;
    }

    [Serializable]
    public sealed class PortalTransitionDerivedData
    {
        public int FromPortalId;
        public int ToPortalId;
        public float Cost;
    }

    public void SetAgentTypeId(int agentTypeId)
    {
        _agentTypeId = agentTypeId;
    }

    public void SetOrigin(Vector3 origin)
    {
        _origin = origin;
        ResetCellAnchorsToCenters();
    }

    public void Resize(int width, int height, float cellSize, bool defaultWalkable)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Resize failed: invalid size {width}x{height}.");
        if (cellSize <= 0.0001f)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Resize failed: invalid cellSize={cellSize:F4}.");

        int nextLength = width * height;
        bool[] next = new bool[nextLength];
        Vector3[] nextAnchors = new Vector3[nextLength];
        for (int i = 0; i < next.Length; i++)
            next[i] = defaultWalkable;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                nextAnchors[x + y * width] = new Vector3(
                    _origin.x + (x + 0.5f) * cellSize,
                    _origin.y,
                    _origin.z + (y + 0.5f) * cellSize);
            }
        }

        int copyWidth = Mathf.Min(_width, width);
        int copyHeight = Mathf.Min(_height, height);
        Vector3[] sourceAnchors = GetCellAnchorsRuntimeReadOnlyReference();
        bool canCopyAnchors = sourceAnchors != null && sourceAnchors.Length == _width * _height;
        if (_walkable != null && _walkable.Length == _width * _height)
        {
            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    next[x + y * width] = _walkable[x + y * _width];
                    if (canCopyAnchors)
                        nextAnchors[x + y * width] = sourceAnchors[x + y * _width];
                }
            }
        }

        _width = width;
        _height = height;
        _cellSize = cellSize;
        _walkable = next;
        _cellAnchors = nextAnchors;
        _neighborTraversalMask = new byte[nextLength];
        _hasAuthoredNeighborTraversalMask = false;
        _derivedNavigationData = null;
        NormalizeCellAnchors();
        StoreSparseCellAnchors(_cellAnchors);
        _costs = CreateDefaultCosts(_walkable);
        InvalidateRuntimeCellAnchorsCache();
    }

    public void Overwrite(
        int agentTypeId,
        int width,
        int height,
        float cellSize,
        Vector3 origin,
        bool[] walkable,
        byte[] costs,
        Vector3[] anchors,
        byte[] neighborTraversalMask)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Overwrite failed: invalid size {width}x{height}.");
        if (cellSize <= 0.0001f)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Overwrite failed: invalid cellSize={cellSize:F4}.");

        int expectedLength = width * height;
        if (walkable == null || walkable.Length != expectedLength)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Overwrite failed: walkable length is invalid. expected={expectedLength} actual={walkable?.Length ?? -1}.");
        if (costs == null || costs.Length != expectedLength)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Overwrite failed: costs length is invalid. expected={expectedLength} actual={costs?.Length ?? -1}.");
        if (anchors == null || anchors.Length != expectedLength)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Overwrite failed: anchors length is invalid. expected={expectedLength} actual={anchors?.Length ?? -1}.");
        if (neighborTraversalMask == null || neighborTraversalMask.Length != expectedLength)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Overwrite failed: neighbor mask length is invalid. expected={expectedLength} actual={neighborTraversalMask?.Length ?? -1}.");

        _agentTypeId = agentTypeId;
        _width = width;
        _height = height;
        _cellSize = cellSize;
        _origin = origin;
        _walkable = (bool[])walkable.Clone();
        _costs = (byte[])costs.Clone();
        Vector3[] normalizedAnchors = (Vector3[])anchors.Clone();
        _neighborTraversalMask = (byte[])neighborTraversalMask.Clone();
        _hasAuthoredNeighborTraversalMask = true;
        _derivedNavigationData = null;

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int index = x + y * _width;
                if (!_walkable[index])
                {
                    _costs[index] = byte.MaxValue;
                    normalizedAnchors[index] = GetCellCenter(x, y);
                    _neighborTraversalMask[index] = 0;
                    continue;
                }

                Vector3 anchor = normalizedAnchors[index];
                if (_costs[index] == 0 || _costs[index] == byte.MaxValue)
                    _costs[index] = 1;
                if (!IsFinite(anchor) || !IsAnchorInsideCell(anchor, x, y))
                    throw new InvalidOperationException($"FlowNavigationGridAsset.Overwrite failed: anchor is invalid for walkable cell ({x},{y}).");
            }
        }

        StoreSparseCellAnchors(normalizedAnchors);
    }

    public void Fill(bool walkable)
    {
        EnsureValidStorage();
        for (int i = 0; i < _walkable.Length; i++)
        {
            _walkable[i] = walkable;
            _costs[i] = walkable ? (byte)1 : byte.MaxValue;
            if (!walkable)
                SetStoredCellAnchor(i, i % _width, i / _width, GetCellCenter(i % _width, i / _width));
            if (!walkable && _hasAuthoredNeighborTraversalMask)
                _neighborTraversalMask[i] = 0;
        }

        InvalidateRuntimeCellAnchorsCache();
    }

    public bool IsCellWalkable(int x, int y)
    {
        EnsureValidStorage();
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return false;
        return _walkable[x + y * _width];
    }

    public void SetCellWalkable(int x, int y, bool walkable)
    {
        EnsureValidStorage();
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;
        int index = x + y * _width;
        _walkable[index] = walkable;
        _costs[index] = walkable ? (byte)1 : byte.MaxValue;
        if (!walkable)
        {
            SetStoredCellAnchor(index, x, y, GetCellCenter(x, y));
            if (_hasAuthoredNeighborTraversalMask)
                _neighborTraversalMask[index] = 0;
        }

        InvalidateRuntimeCellAnchorsCache();
    }

    public Vector3 GetCellAnchor(int x, int y)
    {
        EnsureValidStorage();
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            throw new InvalidOperationException($"FlowNavigationGridAsset.GetCellAnchor failed: cell ({x},{y}) is outside {_width}x{_height}.");

        int index = x + y * _width;
        Vector3 anchor = GetStoredCellAnchor(index, x, y);
        return IsFinite(anchor) ? anchor : GetCellCenter(x, y);
    }

    public void SetCellAnchor(int x, int y, Vector3 anchor)
    {
        EnsureValidStorage();
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;
        if (!IsFinite(anchor))
            throw new InvalidOperationException($"FlowNavigationGridAsset.SetCellAnchor failed: anchor is not finite for cell ({x},{y}).");

        SetStoredCellAnchor(x + y * _width, x, y, anchor);
        InvalidateRuntimeCellAnchorsCache();
    }

    public void SetCellNeighborTraversalMask(int x, int y, byte mask)
    {
        EnsureValidStorage();
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;

        _hasAuthoredNeighborTraversalMask = true;
        int index = x + y * _width;
        _neighborTraversalMask[index] = _walkable[index] ? mask : (byte)0;
    }

    public void ClearAuthoredNeighborTraversalMask()
    {
        EnsureValidStorage();
        Array.Clear(_neighborTraversalMask, 0, _neighborTraversalMask.Length);
        _hasAuthoredNeighborTraversalMask = false;
    }

    public byte GetCellCost(int x, int y)
    {
        EnsureValidStorage();
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return byte.MaxValue;
        return _costs[x + y * _width];
    }

    public void SetCellCost(int x, int y, byte cost)
    {
        EnsureValidStorage();
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;

        int index = x + y * _width;
        if (!_walkable[index])
        {
            _costs[index] = byte.MaxValue;
            return;
        }

        _costs[index] = (byte)Mathf.Clamp(cost, 1, 254);
    }

    public bool WorldToCell(Vector3 position, out int x, out int y)
    {
        Vector3 local = position - _origin;
        x = Mathf.FloorToInt(local.x / _cellSize);
        y = Mathf.FloorToInt(local.z / _cellSize);
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }

    public Vector3 GetCellCenter(int x, int y)
    {
        return new Vector3(
            _origin.x + (x + 0.5f) * _cellSize,
            _origin.y,
            _origin.z + (y + 0.5f) * _cellSize);
    }

    public bool[] CreateWalkableMaskCopy()
    {
        EnsureValidStorage();
        return (bool[])_walkable.Clone();
    }

    public bool[] GetWalkableMaskRuntimeReadOnlyReference()
    {
        EnsureValidStorage();
        return _walkable;
    }

    public byte[] CreateCostFieldCopy()
    {
        EnsureValidStorage();
        return (byte[])_costs.Clone();
    }

    public byte[] GetCostFieldRuntimeReadOnlyReference()
    {
        EnsureValidStorage();
        return _costs;
    }

    public Vector3[] CreateCellAnchors()
    {
        EnsureValidStorage();
        return (Vector3[])GetCellAnchorsRuntimeReadOnlyReference().Clone();
    }

    public Vector3[] GetCellAnchorsRuntimeReadOnlyReference()
    {
        EnsureValidStorage();
        if (HasDenseCellAnchors())
            return _cellAnchors;

        if (_runtimeCellAnchorsCache == null || _runtimeCellAnchorsCache.Length != CellCount)
            _runtimeCellAnchorsCache = BuildDenseCellAnchors();

        return _runtimeCellAnchorsCache;
    }

    public Vector3[] GetCellAnchorsRuntimeReadOnlyReferenceOrNull()
    {
        return HasAuthoredCellAnchors ? GetCellAnchorsRuntimeReadOnlyReference() : null;
    }

    public byte[] CreateNeighborTraversalMaskCopy()
    {
        EnsureValidStorage();
        return _hasAuthoredNeighborTraversalMask ? (byte[])_neighborTraversalMask.Clone() : null;
    }

    public byte[] GetNeighborTraversalMaskRuntimeReadOnlyReference()
    {
        EnsureValidStorage();
        return _hasAuthoredNeighborTraversalMask ? _neighborTraversalMask : null;
    }

    public DerivedNavigationData CreateDerivedNavigationDataCopy()
    {
        EnsureValidStorage();
        return CloneDerivedNavigationData(_derivedNavigationData);
    }

    public DerivedNavigationData GetDerivedNavigationDataRuntimeReadOnlyReference()
    {
        EnsureValidStorage();
        return _derivedNavigationData;
    }

    public void SetDerivedNavigationData(DerivedNavigationData data)
    {
        EnsureValidStorage();
        if (data == null || !data.IsValid)
            throw new InvalidOperationException("FlowNavigationGridAsset.SetDerivedNavigationData failed: data is invalid.");
        if (data.AgentTypeId != _agentTypeId
            || data.Width != _width
            || data.Height != _height
            || !Mathf.Approximately(data.CellSize, _cellSize)
            || data.Origin != _origin)
        {
            throw new InvalidOperationException(
                $"FlowNavigationGridAsset.SetDerivedNavigationData failed: derived metadata does not match asset. " +
                $"asset=(agent={_agentTypeId},size={_width}x{_height},cell={_cellSize:F4},origin={_origin}) " +
                $"derived=(agent={data.AgentTypeId},size={data.Width}x{data.Height},cell={data.CellSize:F4},origin={data.Origin}).");
        }

        _derivedNavigationData = CloneDerivedNavigationData(data);
    }

    public void CompactCellAnchorsForStorage()
    {
        EnsureValidStorage();
        if (!HasDenseCellAnchors())
            return;

        StoreSparseCellAnchors(_cellAnchors);
    }

    public void ClearDerivedNavigationData()
    {
        _derivedNavigationData = null;
    }

    private void OnValidate()
    {
        _width = Mathf.Max(1, _width);
        _height = Mathf.Max(1, _height);
        _cellSize = Mathf.Max(0.0001f, _cellSize);
        EnsureValidStorage();
    }

    private static DerivedNavigationData CloneDerivedNavigationData(DerivedNavigationData source)
    {
        if (source == null)
            return null;

        DerivedNavigationData clone = new DerivedNavigationData
        {
            Version = source.Version,
            AgentTypeId = source.AgentTypeId,
            Width = source.Width,
            Height = source.Height,
            CellSize = source.CellSize,
            Origin = source.Origin,
            ConfigSectorSizeInCells = source.ConfigSectorSizeInCells,
            ConfigPortalNarrowWidthCells = source.ConfigPortalNarrowWidthCells,
            ConfigPortalMaxWindowWidthCells = source.ConfigPortalMaxWindowWidthCells,
            SectorSizeInCells = source.SectorSizeInCells,
            SectorCountX = source.SectorCountX,
            SectorCountY = source.SectorCountY,
            IslandCount = source.IslandCount,
            MainIslandId = source.MainIslandId,
            MainIslandSize = source.MainIslandSize,
            NextPortalId = source.NextPortalId,
            IslandIds = source.IslandIds != null ? (int[])source.IslandIds.Clone() : Array.Empty<int>(),
            Sectors = CloneSectorDerivedData(source.Sectors),
            Portals = ClonePortalDerivedData(source.Portals)
        };
        return clone;
    }

    private static SectorDerivedData[] CloneSectorDerivedData(SectorDerivedData[] source)
    {
        if (source == null)
            return Array.Empty<SectorDerivedData>();

        SectorDerivedData[] clone = new SectorDerivedData[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            SectorDerivedData sector = source[i];
            if (sector == null)
                throw new InvalidOperationException($"FlowNavigationGridAsset.CloneSectorDerivedData failed: sector is null at index={i}.");

            clone[i] = new SectorDerivedData
            {
                SectorId = sector.SectorId,
                StartX = sector.StartX,
                StartY = sector.StartY,
                Width = sector.Width,
                Height = sector.Height,
                Center = sector.Center,
                DirtyVersion = sector.DirtyVersion,
                IsClearCostField = sector.IsClearCostField,
                IsClearFlowTile = sector.IsClearFlowTile,
                UniformIslandId = sector.UniformIslandId,
                LocalComponentIds = sector.LocalComponentIds != null ? (int[])sector.LocalComponentIds.Clone() : Array.Empty<int>(),
                LocalComponentCount = sector.LocalComponentCount,
                PortalIds = sector.PortalIds != null ? (int[])sector.PortalIds.Clone() : Array.Empty<int>(),
                PortalTransitions = ClonePortalTransitionDerivedData(sector.PortalTransitions)
            };
        }

        return clone;
    }

    private static PortalDerivedData[] ClonePortalDerivedData(PortalDerivedData[] source)
    {
        if (source == null)
            return Array.Empty<PortalDerivedData>();

        PortalDerivedData[] clone = new PortalDerivedData[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            PortalDerivedData portal = source[i];
            if (portal == null)
                throw new InvalidOperationException($"FlowNavigationGridAsset.ClonePortalDerivedData failed: portal is null at index={i}.");

            clone[i] = new PortalDerivedData
            {
                PortalId = portal.PortalId,
                SectorAId = portal.SectorAId,
                SectorBId = portal.SectorBId,
                CellsA = portal.CellsA != null ? (Vector2Int[])portal.CellsA.Clone() : Array.Empty<Vector2Int>(),
                CellsB = portal.CellsB != null ? (Vector2Int[])portal.CellsB.Clone() : Array.Empty<Vector2Int>(),
                WorldCenter = portal.WorldCenter,
                WidthCells = portal.WidthCells,
                IsNarrow = portal.IsNarrow,
                IsVerticalBoundary = portal.IsVerticalBoundary
            };
        }

        return clone;
    }

    private static PortalTransitionDerivedData[] ClonePortalTransitionDerivedData(PortalTransitionDerivedData[] source)
    {
        if (source == null)
            return Array.Empty<PortalTransitionDerivedData>();

        PortalTransitionDerivedData[] clone = new PortalTransitionDerivedData[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            PortalTransitionDerivedData transition = source[i];
            if (transition == null)
                throw new InvalidOperationException($"FlowNavigationGridAsset.ClonePortalTransitionDerivedData failed: transition is null at index={i}.");

            clone[i] = new PortalTransitionDerivedData
            {
                FromPortalId = transition.FromPortalId,
                ToPortalId = transition.ToPortalId,
                Cost = transition.Cost
            };
        }

        return clone;
    }

    private void EnsureValidStorage()
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        if (_walkable != null && _walkable.Length == expectedLength)
        {
            if (_costs == null || _costs.Length != expectedLength)
                _costs = CreateDefaultCosts(_walkable);
            EnsureAnchorStorage(expectedLength);
            EnsureNeighborTraversalStorage(expectedLength);
            NormalizeCosts();
            return;
        }

        bool[] next = new bool[expectedLength];
        if (_walkable != null)
        {
            int count = Mathf.Min(_walkable.Length, next.Length);
            Array.Copy(_walkable, next, count);
        }

        _walkable = next;
        _costs = CreateDefaultCosts(_walkable);
        EnsureAnchorStorage(expectedLength);
        EnsureNeighborTraversalStorage(expectedLength);
    }

    private void EnsureAnchorStorage(int expectedLength)
    {
        if (HasDenseCellAnchors(expectedLength))
        {
            NormalizeCellAnchors();
            return;
        }

        if (HasValidSparseCellAnchors(expectedLength))
            return;

        _cellAnchors = Array.Empty<Vector3>();
        _sparseCellAnchorIndices = Array.Empty<int>();
        _sparseCellAnchorValues = Array.Empty<Vector3>();
        InvalidateRuntimeCellAnchorsCache();
        NormalizeCellAnchors();
    }

    private void EnsureNeighborTraversalStorage(int expectedLength)
    {
        if (_neighborTraversalMask != null && _neighborTraversalMask.Length == expectedLength)
            return;

        _neighborTraversalMask = new byte[expectedLength];
        _hasAuthoredNeighborTraversalMask = false;
    }

    private void ResetCellAnchorsToCenters()
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        _cellAnchors = Array.Empty<Vector3>();
        _sparseCellAnchorIndices = Array.Empty<int>();
        _sparseCellAnchorValues = Array.Empty<Vector3>();
        InvalidateRuntimeCellAnchorsCache();
    }

    private void NormalizeCellAnchors()
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        if (!HasDenseCellAnchors(expectedLength))
        {
            ValidateSparseCellAnchors(expectedLength);
            return;
        }

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int index = x + y * _width;
                if (!IsFinite(_cellAnchors[index]) || !IsAnchorInsideCell(_cellAnchors[index], x, y))
                    _cellAnchors[index] = GetCellCenter(x, y);
            }
        }
    }

    private bool HasDenseCellAnchors()
    {
        return HasDenseCellAnchors(CellCount);
    }

    private bool HasDenseCellAnchors(int expectedLength)
    {
        return _cellAnchors != null && _cellAnchors.Length == expectedLength;
    }

    private bool HasValidSparseCellAnchors(int expectedLength)
    {
        return _sparseCellAnchorIndices != null
               && _sparseCellAnchorValues != null
               && _sparseCellAnchorIndices.Length == _sparseCellAnchorValues.Length
               && _sparseCellAnchorIndices.Length <= expectedLength;
    }

    private void ValidateSparseCellAnchors(int expectedLength)
    {
        if (!HasValidSparseCellAnchors(expectedLength))
        {
            _sparseCellAnchorIndices = Array.Empty<int>();
            _sparseCellAnchorValues = Array.Empty<Vector3>();
            return;
        }

        for (int i = 0; i < _sparseCellAnchorIndices.Length; i++)
        {
            int index = _sparseCellAnchorIndices[i];
            if (index < 0 || index >= expectedLength)
                throw new InvalidOperationException($"FlowNavigationGridAsset.ValidateSparseCellAnchors failed: anchor index is out of range. index={index} expectedLength={expectedLength}.");

            int x = index % _width;
            int y = index / _width;
            Vector3 anchor = _sparseCellAnchorValues[i];
            if (!IsFinite(anchor) || !IsAnchorInsideCell(anchor, x, y))
                throw new InvalidOperationException($"FlowNavigationGridAsset.ValidateSparseCellAnchors failed: anchor is invalid for cell ({x},{y}).");
        }
    }

    private void StoreSparseCellAnchors(Vector3[] anchors)
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        if (anchors == null || anchors.Length != expectedLength)
            throw new InvalidOperationException($"FlowNavigationGridAsset.StoreSparseCellAnchors failed: anchor length is invalid. expected={expectedLength} actual={anchors?.Length ?? -1}.");

        List<int> indices = new List<int>();
        List<Vector3> values = new List<Vector3>();
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int index = x + y * _width;
                Vector3 anchor = anchors[index];
                if (!IsFinite(anchor) || !IsAnchorInsideCell(anchor, x, y))
                    anchor = GetCellCenter(x, y);

                if (IsDefaultCellCenter(anchor, x, y))
                    continue;

                indices.Add(index);
                values.Add(anchor);
            }
        }

        _cellAnchors = Array.Empty<Vector3>();
        _sparseCellAnchorIndices = indices.Count > 0 ? indices.ToArray() : Array.Empty<int>();
        _sparseCellAnchorValues = values.Count > 0 ? values.ToArray() : Array.Empty<Vector3>();
        InvalidateRuntimeCellAnchorsCache();
    }

    private Vector3[] BuildDenseCellAnchors()
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        Vector3[] anchors = new Vector3[expectedLength];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
                anchors[x + y * _width] = GetCellCenter(x, y);
        }

        if (HasValidSparseCellAnchors(expectedLength))
        {
            for (int i = 0; i < _sparseCellAnchorIndices.Length; i++)
                anchors[_sparseCellAnchorIndices[i]] = _sparseCellAnchorValues[i];
        }

        return anchors;
    }

    private Vector3 GetStoredCellAnchor(int index, int x, int y)
    {
        if (HasDenseCellAnchors())
            return _cellAnchors[index];

        if (HasValidSparseCellAnchors(CellCount))
        {
            for (int i = 0; i < _sparseCellAnchorIndices.Length; i++)
            {
                if (_sparseCellAnchorIndices[i] == index)
                    return _sparseCellAnchorValues[i];
            }
        }

        return GetCellCenter(x, y);
    }

    private void SetStoredCellAnchor(int index, int x, int y, Vector3 anchor)
    {
        if (HasDenseCellAnchors())
        {
            _cellAnchors[index] = anchor;
            return;
        }

        Vector3[] anchors = GetCellAnchorsRuntimeReadOnlyReference();
        Vector3[] copy = (Vector3[])anchors.Clone();
        copy[index] = anchor;
        StoreSparseCellAnchors(copy);
    }

    private bool IsDefaultCellCenter(Vector3 anchor, int x, int y)
    {
        Vector3 center = GetCellCenter(x, y);
        float epsilon = Mathf.Max(0.00001f, _cellSize * 0.001f);
        return Mathf.Abs(anchor.x - center.x) <= epsilon
               && Mathf.Abs(anchor.y - center.y) <= epsilon
               && Mathf.Abs(anchor.z - center.z) <= epsilon;
    }

    private void InvalidateRuntimeCellAnchorsCache()
    {
        _runtimeCellAnchorsCache = null;
    }

    private void NormalizeCosts()
    {
        for (int i = 0; i < _costs.Length; i++)
        {
            if (!_walkable[i])
            {
                _costs[i] = byte.MaxValue;
                continue;
            }

            if (_costs[i] == 0 || _costs[i] == byte.MaxValue)
                _costs[i] = 1;
        }
    }

    private static byte[] CreateDefaultCosts(bool[] walkable)
    {
        if (walkable == null)
            return Array.Empty<byte>();

        byte[] costs = new byte[walkable.Length];
        for (int i = 0; i < costs.Length; i++)
            costs[i] = walkable[i] ? (byte)1 : byte.MaxValue;
        return costs;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x)
               && !float.IsNaN(value.y)
               && !float.IsNaN(value.z)
               && !float.IsInfinity(value.x)
               && !float.IsInfinity(value.y)
               && !float.IsInfinity(value.z);
    }

    private bool IsAnchorInsideCell(Vector3 anchor, int x, int y)
    {
        float epsilon = Mathf.Max(0.00001f, _cellSize * 0.001f);
        float minX = _origin.x + x * _cellSize;
        float maxX = minX + _cellSize;
        float minZ = _origin.z + y * _cellSize;
        float maxZ = minZ + _cellSize;
        return anchor.x > minX + epsilon
               && anchor.x < maxX - epsilon
               && anchor.z > minZ + epsilon
               && anchor.z < maxZ - epsilon;
    }
}
