using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FlowNavigationGrid", menuName = "Movement/Flow Navigation Grid")]
public sealed class FlowNavigationGridAsset : ScriptableObject
{
    private const int StaticCollisionGeometryVersion = 1;

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
    [SerializeField] private int _staticCollisionGeometryVersion;
    [SerializeField] private int[] _staticCollisionPathStarts = Array.Empty<int>();
    [SerializeField] private long[] _staticCollisionVertexFixedRaw = Array.Empty<long>();
    [SerializeField] private int _fixedAuthorityPayloadVersion;
    [SerializeField] private long _cellSizeGridRaw;
    [SerializeField] private long _originXGridRaw;
    [SerializeField] private long _originZGridRaw;
    [SerializeField] private byte[] _sparseCellAnchorFixedRaw = Array.Empty<byte>();
    [SerializeField] private DerivedNavigationData _derivedNavigationData;

    [NonSerialized] private Vector3[] _runtimeCellAnchorsCache;
    [NonSerialized] private FixVector2[] _runtimeCellAnchorsFixedCache;
    [NonSerialized] private FixVector2[] _runtimeStaticCollisionVerticesCache;

    public int AgentTypeId => _agentTypeId;
    public int Width => _width;
    public int Height => _height;
    public float CellSize => _cellSize;
    public Vector3 Origin => _origin;
    public int CellCount => _width * _height;
    public bool HasDerivedNavigationData => _derivedNavigationData != null && _derivedNavigationData.IsValid;
    public bool HasStaticCollisionGeometry => _staticCollisionGeometryVersion == StaticCollisionGeometryVersion
                                              && _staticCollisionPathStarts != null
                                              && _staticCollisionPathStarts.Length >= 2
                                              && _staticCollisionVertexFixedRaw != null
                                              && (_staticCollisionVertexFixedRaw.Length & 1) == 0
                                              && _staticCollisionPathStarts[0] == 0
                                              && _staticCollisionPathStarts[_staticCollisionPathStarts.Length - 1] * 2 == _staticCollisionVertexFixedRaw.Length;
    public bool HasFixedAuthorityPayload => _fixedAuthorityPayloadVersion == NavigationGridFixedMath.AuthorityPayloadVersion
                                            && _cellSizeGridRaw > 0
                                            && _sparseCellAnchorIndices != null
                                            && _sparseCellAnchorFixedRaw != null
                                            && _sparseCellAnchorFixedRaw.Length == _sparseCellAnchorIndices.Length * NavigationGridFixedMath.EncodedFixVector2Size;
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
        public const int CurrentVersion = 6;

        public int Version;
        public int AgentTypeId;
        public int Width;
        public int Height;
        public float CellSize;
        public Vector3 Origin;
        public long CellSizeGridRaw;
        public long OriginXGridRaw;
        public long OriginZGridRaw;
        public int ConfigSectorWorldSizeMillimeters;
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
        public PortalHierarchyDerivedData Hierarchy;

        public bool IsValid => Version == CurrentVersion
                               && Width > 0
                               && Height > 0
                               && CellSize > 0.0001f
                               && CellSizeGridRaw > 0
                               && SectorSizeInCells > 0
                               && SectorCountX > 0
                               && SectorCountY > 0
                               && IslandIds != null
                               && IslandIds.Length == Width * Height
                               && Sectors != null
                               && Sectors.Length == SectorCountX * SectorCountY
                               && AreSectorsValid(Sectors)
                               && Portals != null
                               && Hierarchy != null
                               && Hierarchy.IsValid;

        private static bool AreSectorsValid(SectorDerivedData[] sectors)
        {
            for (int sectorIndex = 0; sectorIndex < sectors.Length; sectorIndex++)
            {
                SectorDerivedData sector = sectors[sectorIndex];
                if (sector == null || sector.PortalIds == null || sector.PortalTransitions == null)
                    return false;
                for (int transitionIndex = 0; transitionIndex < sector.PortalTransitions.Length; transitionIndex++)
                {
                    PortalTransitionDerivedData transition = sector.PortalTransitions[transitionIndex];
                    if (transition == null || !transition.IsValid)
                        return false;
                }
            }
            return true;
        }
    }

    public readonly struct FixedAuthorityMetadata
    {
        public readonly long CellSizeGridRaw;
        public readonly long OriginXGridRaw;
        public readonly long OriginZGridRaw;

        public FixedAuthorityMetadata(long cellSizeGridRaw, long originXGridRaw, long originZGridRaw)
        {
            CellSizeGridRaw = cellSizeGridRaw;
            OriginXGridRaw = originXGridRaw;
            OriginZGridRaw = originZGridRaw;
        }
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
        public long DeterministicCost;

        public bool IsValid => DeterministicCost >= 0
                               && DeterministicCost < long.MaxValue
                               && !float.IsNaN(Cost)
                               && !float.IsInfinity(Cost)
                               && Cost >= 0f;
    }

    [Serializable]
    public sealed class PortalHierarchyDerivedData
    {
        public int Fanout;
        public PortalHierarchyLevelDerivedData[] Levels = Array.Empty<PortalHierarchyLevelDerivedData>();

        public bool IsValid
        {
            get
            {
                if (Fanout < 2 || Levels == null)
                    return false;
                for (int i = 0; i < Levels.Length; i++)
                {
                    if (Levels[i] == null || !Levels[i].IsValid)
                        return false;
                }
                return true;
            }
        }
    }

    [Serializable]
    public sealed class PortalHierarchyLevelDerivedData
    {
        public int Level;
        public int ClusterSpanSectors;
        public int ClusterCountX;
        public int ClusterCountY;
        public PortalHierarchyClusterDerivedData[] Clusters = Array.Empty<PortalHierarchyClusterDerivedData>();

        public bool IsValid
        {
            get
            {
                if (Level <= 0
                    || ClusterSpanSectors <= 0
                    || ClusterCountX <= 0
                    || ClusterCountY <= 0
                    || Clusters == null
                    || (long)Clusters.Length != (long)ClusterCountX * ClusterCountY)
                {
                    return false;
                }
                for (int i = 0; i < Clusters.Length; i++)
                {
                    if (Clusters[i] == null || !Clusters[i].IsValid)
                        return false;
                }
                return true;
            }
        }
    }

    [Serializable]
    public sealed class PortalHierarchyClusterDerivedData
    {
        public int ClusterId;
        public int StartSectorX;
        public int StartSectorY;
        public int WidthSectors;
        public int HeightSectors;
        public int[] BoundaryNodes = Array.Empty<int>();
        public PortalHierarchyEdgeDerivedData[] Edges = Array.Empty<PortalHierarchyEdgeDerivedData>();

        public bool IsValid
        {
            get
            {
                if (ClusterId < 0
                    || StartSectorX < 0
                    || StartSectorY < 0
                    || WidthSectors <= 0
                    || HeightSectors <= 0
                    || BoundaryNodes == null
                    || Edges == null)
                {
                    return false;
                }
                for (int i = 0; i < Edges.Length; i++)
                {
                    if (Edges[i] == null || !Edges[i].IsValid)
                        return false;
                }
                return true;
            }
        }
    }

    [Serializable]
    public sealed class PortalHierarchyEdgeDerivedData
    {
        public int FromNode;
        public int ToNode;
        public long DeterministicCost;
        public int[] ChildWitnessNodes = Array.Empty<int>();

        public bool IsValid => DeterministicCost >= 0
                               && ChildWitnessNodes != null
                               && ChildWitnessNodes.Length >= 2
                               && ChildWitnessNodes[0] == FromNode
                               && ChildWitnessNodes[ChildWitnessNodes.Length - 1] == ToNode;
    }

    public void SetAgentTypeId(int agentTypeId)
    {
        _agentTypeId = agentTypeId;
    }

    public void SetOrigin(Vector3 origin)
    {
        _origin = origin;
        ResetCellAnchorsToCenters();
        _derivedNavigationData = null;
        RebuildFixedAuthorityPayload();
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
        InvalidateRuntimeCellAnchorCaches();
        RebuildFixedAuthorityPayload();
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
        RebuildFixedAuthorityPayload();
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

        InvalidateRuntimeCellAnchorCaches();
        RebuildFixedAuthorityPayload();
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

        InvalidateRuntimeCellAnchorCaches();
        RebuildFixedAuthorityPayload();
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
        InvalidateRuntimeCellAnchorCaches();
        RebuildFixedAuthorityPayload();
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

    public FixedAuthorityMetadata GetFixedAuthorityMetadata()
    {
        ValidateFixedAuthorityPayload("FlowNavigationGridAsset.GetFixedAuthorityMetadata");
        return new FixedAuthorityMetadata(_cellSizeGridRaw, _originXGridRaw, _originZGridRaw);
    }

    public FixVector2[] GetCellAnchorsFixedRuntimeReadOnlyReference()
    {
        ValidateFixedAuthorityPayload("FlowNavigationGridAsset.GetCellAnchorsFixedRuntimeReadOnlyReference");
        if (_runtimeCellAnchorsFixedCache == null || _runtimeCellAnchorsFixedCache.Length != CellCount)
            _runtimeCellAnchorsFixedCache = BuildDenseFixedCellAnchors();
        return _runtimeCellAnchorsFixedCache;
    }

    public void RebuildFixedAuthorityPayload()
    {
        EnsureValidStorage();
        if (HasDenseCellAnchors())
            StoreSparseCellAnchors(_cellAnchors);

        _fixedAuthorityPayloadVersion = NavigationGridFixedMath.AuthorityPayloadVersion;
        _cellSizeGridRaw = NavigationGridFixedMath.FloatToGridRaw(_cellSize);
        _originXGridRaw = NavigationGridFixedMath.FloatToGridRaw(_origin.x);
        _originZGridRaw = NavigationGridFixedMath.FloatToGridRaw(_origin.z);
        _sparseCellAnchorFixedRaw = NavigationGridFixedMath.EncodeFixVector2XZ(_sparseCellAnchorValues);
        _runtimeCellAnchorsFixedCache = null;
        ValidateFixedAuthorityPayload("FlowNavigationGridAsset.RebuildFixedAuthorityPayload");
    }

    public void UpgradeFixedAuthorityPayloadAndDerivedMetadata()
    {
        RebuildFixedAuthorityPayload();
        if (_derivedNavigationData == null)
            throw new InvalidOperationException("FlowNavigationGridAsset upgrade failed: derived navigation data is missing.");

        DerivedNavigationData data = _derivedNavigationData;
        bool supportedVersion = data.Version == DerivedNavigationData.CurrentVersion;
        bool hasValidTopology = data.Width > 0
                                && data.Height > 0
                                && data.CellSize > 0.0001f
                                && data.SectorSizeInCells > 0
                                && data.SectorCountX > 0
                                && data.SectorCountY > 0
                                && data.IslandIds != null
                                && data.IslandIds.Length == data.Width * data.Height
                                && data.Sectors != null
                                && data.Sectors.Length == data.SectorCountX * data.SectorCountY
                                && data.Portals != null;
        if (!supportedVersion || !hasValidTopology)
        {
            throw new InvalidOperationException(
                $"FlowNavigationGridAsset upgrade failed: derived navigation schema version={data.Version} requires a full navigation rebake; " +
                $"metadata upgrade cannot produce required v{DerivedNavigationData.CurrentVersion} hierarchy data.");
        }
        if (data.AgentTypeId != _agentTypeId
            || data.Width != _width
            || data.Height != _height
            || !Mathf.Approximately(data.CellSize, _cellSize)
            || data.Origin != _origin)
        {
            throw new InvalidOperationException("FlowNavigationGridAsset upgrade failed: derived metadata does not match the asset.");
        }

        DerivedNavigationData upgraded = CloneDerivedNavigationData(data);
        upgraded.Version = DerivedNavigationData.CurrentVersion;
        upgraded.CellSizeGridRaw = _cellSizeGridRaw;
        upgraded.OriginXGridRaw = _originXGridRaw;
        upgraded.OriginZGridRaw = _originZGridRaw;
        if (!upgraded.IsValid)
            throw new InvalidOperationException("FlowNavigationGridAsset upgrade failed: upgraded derived navigation data is invalid.");
        _derivedNavigationData = upgraded;
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

    public void SetStaticCollisionGeometry(FixVector2[] vertices, int[] pathStarts)
    {
        if (vertices == null || vertices.Length < 3)
            throw new InvalidOperationException("FlowNavigationGridAsset.SetStaticCollisionGeometry failed: vertices are missing.");
        if (pathStarts == null || pathStarts.Length < 2 || pathStarts[0] != 0 || pathStarts[pathStarts.Length - 1] != vertices.Length)
            throw new InvalidOperationException("FlowNavigationGridAsset.SetStaticCollisionGeometry failed: path starts are invalid.");

        for (int pathIndex = 0; pathIndex < pathStarts.Length - 1; pathIndex++)
        {
            int start = pathStarts[pathIndex];
            int end = pathStarts[pathIndex + 1];
            if (start < 0 || end > vertices.Length || end - start < 3)
                throw new InvalidOperationException($"FlowNavigationGridAsset.SetStaticCollisionGeometry failed: path {pathIndex} is invalid. start={start} end={end}.");
            for (int i = start; i < end; i++)
            {
                if (vertices[i] == vertices[i + 1 < end ? i + 1 : start])
                    throw new InvalidOperationException($"FlowNavigationGridAsset.SetStaticCollisionGeometry failed: path {pathIndex} contains a zero-length edge at vertex={i}.");
            }
        }

        _staticCollisionGeometryVersion = StaticCollisionGeometryVersion;
        _staticCollisionPathStarts = (int[])pathStarts.Clone();
        _staticCollisionVertexFixedRaw = new long[vertices.Length * 2];
        for (int i = 0; i < vertices.Length; i++)
        {
            _staticCollisionVertexFixedRaw[i * 2] = vertices[i].x.RawValue;
            _staticCollisionVertexFixedRaw[i * 2 + 1] = vertices[i].y.RawValue;
        }
        _runtimeStaticCollisionVerticesCache = null;
        ValidateStaticCollisionGeometry("FlowNavigationGridAsset.SetStaticCollisionGeometry");
    }

    public void ClearStaticCollisionGeometry()
    {
        _staticCollisionGeometryVersion = 0;
        _staticCollisionPathStarts = Array.Empty<int>();
        _staticCollisionVertexFixedRaw = Array.Empty<long>();
        _runtimeStaticCollisionVerticesCache = null;
    }

    public FixVector2[] GetStaticCollisionVerticesRuntimeReadOnlyReference()
    {
        ValidateStaticCollisionGeometry("FlowNavigationGridAsset.GetStaticCollisionVerticesRuntimeReadOnlyReference");
        int vertexCount = _staticCollisionVertexFixedRaw.Length / 2;
        if (_runtimeStaticCollisionVerticesCache == null || _runtimeStaticCollisionVerticesCache.Length != vertexCount)
        {
            _runtimeStaticCollisionVerticesCache = new FixVector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                _runtimeStaticCollisionVerticesCache[i] = new FixVector2(
                    Fix64.FromRaw(_staticCollisionVertexFixedRaw[i * 2]),
                    Fix64.FromRaw(_staticCollisionVertexFixedRaw[i * 2 + 1]));
            }
        }
        return _runtimeStaticCollisionVerticesCache;
    }

    public int[] GetStaticCollisionPathStartsRuntimeReadOnlyReference()
    {
        ValidateStaticCollisionGeometry("FlowNavigationGridAsset.GetStaticCollisionPathStartsRuntimeReadOnlyReference");
        return _staticCollisionPathStarts;
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
            || data.Origin != _origin
            || data.CellSizeGridRaw != _cellSizeGridRaw
            || data.OriginXGridRaw != _originXGridRaw
            || data.OriginZGridRaw != _originZGridRaw)
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
        RebuildFixedAuthorityPayload();
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
            CellSizeGridRaw = source.CellSizeGridRaw,
            OriginXGridRaw = source.OriginXGridRaw,
            OriginZGridRaw = source.OriginZGridRaw,
            ConfigSectorWorldSizeMillimeters = source.ConfigSectorWorldSizeMillimeters,
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
            Portals = ClonePortalDerivedData(source.Portals),
            Hierarchy = ClonePortalHierarchyDerivedData(source.Hierarchy)
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
                Cost = transition.Cost,
                DeterministicCost = transition.DeterministicCost
            };
        }

        return clone;
    }

    private static PortalHierarchyDerivedData ClonePortalHierarchyDerivedData(PortalHierarchyDerivedData source)
    {
        if (source == null)
            return null;
        var clone = new PortalHierarchyDerivedData
        {
            Fanout = source.Fanout,
            Levels = new PortalHierarchyLevelDerivedData[source.Levels?.Length ?? 0]
        };
        for (int levelIndex = 0; levelIndex < clone.Levels.Length; levelIndex++)
        {
            PortalHierarchyLevelDerivedData level = source.Levels[levelIndex]
                ?? throw new InvalidOperationException($"FlowNavigationGridAsset.ClonePortalHierarchyDerivedData failed: level is null index={levelIndex}.");
            var levelClone = new PortalHierarchyLevelDerivedData
            {
                Level = level.Level,
                ClusterSpanSectors = level.ClusterSpanSectors,
                ClusterCountX = level.ClusterCountX,
                ClusterCountY = level.ClusterCountY,
                Clusters = new PortalHierarchyClusterDerivedData[level.Clusters?.Length ?? 0]
            };
            for (int clusterIndex = 0; clusterIndex < levelClone.Clusters.Length; clusterIndex++)
            {
                PortalHierarchyClusterDerivedData cluster = level.Clusters[clusterIndex]
                    ?? throw new InvalidOperationException($"FlowNavigationGridAsset.ClonePortalHierarchyDerivedData failed: cluster is null level={levelIndex} index={clusterIndex}.");
                var clusterClone = new PortalHierarchyClusterDerivedData
                {
                    ClusterId = cluster.ClusterId,
                    StartSectorX = cluster.StartSectorX,
                    StartSectorY = cluster.StartSectorY,
                    WidthSectors = cluster.WidthSectors,
                    HeightSectors = cluster.HeightSectors,
                    BoundaryNodes = cluster.BoundaryNodes != null ? (int[])cluster.BoundaryNodes.Clone() : Array.Empty<int>(),
                    Edges = new PortalHierarchyEdgeDerivedData[cluster.Edges?.Length ?? 0]
                };
                for (int edgeIndex = 0; edgeIndex < clusterClone.Edges.Length; edgeIndex++)
                {
                    PortalHierarchyEdgeDerivedData edge = cluster.Edges[edgeIndex]
                        ?? throw new InvalidOperationException($"FlowNavigationGridAsset.ClonePortalHierarchyDerivedData failed: edge is null level={levelIndex} cluster={clusterIndex} index={edgeIndex}.");
                    clusterClone.Edges[edgeIndex] = new PortalHierarchyEdgeDerivedData
                    {
                        FromNode = edge.FromNode,
                        ToNode = edge.ToNode,
                        DeterministicCost = edge.DeterministicCost,
                        ChildWitnessNodes = edge.ChildWitnessNodes != null ? (int[])edge.ChildWitnessNodes.Clone() : Array.Empty<int>()
                    };
                }
                levelClone.Clusters[clusterIndex] = clusterClone;
            }
            clone.Levels[levelIndex] = levelClone;
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
        InvalidateRuntimeCellAnchorCaches();
        NormalizeCellAnchors();
    }

    private void EnsureNeighborTraversalStorage(int expectedLength)
    {
        if (_neighborTraversalMask != null && _neighborTraversalMask.Length == expectedLength)
            return;

        _neighborTraversalMask = new byte[expectedLength];
        _hasAuthoredNeighborTraversalMask = false;
    }

    private void ValidateStaticCollisionGeometry(string caller)
    {
        if (!HasStaticCollisionGeometry)
        {
            throw new InvalidOperationException(
                $"{caller} failed: deterministic static collision geometry is missing or invalid. Rebuild the FlowNavigationGridAsset.");
        }

        int vertexCount = _staticCollisionVertexFixedRaw.Length / 2;
        for (int pathIndex = 0; pathIndex < _staticCollisionPathStarts.Length - 1; pathIndex++)
        {
            int start = _staticCollisionPathStarts[pathIndex];
            int end = _staticCollisionPathStarts[pathIndex + 1];
            if (start < 0 || end > vertexCount || end - start < 3)
                throw new InvalidOperationException($"{caller} failed: deterministic static collision path {pathIndex} is invalid.");
        }
    }

    private void ResetCellAnchorsToCenters()
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        _cellAnchors = Array.Empty<Vector3>();
        _sparseCellAnchorIndices = Array.Empty<int>();
        _sparseCellAnchorValues = Array.Empty<Vector3>();
        InvalidateRuntimeCellAnchorCaches();
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
        InvalidateRuntimeCellAnchorCaches();
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

    private FixVector2[] BuildDenseFixedCellAnchors()
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        FixVector2[] anchors = new FixVector2[expectedLength];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                anchors[x + y * _width] = NavigationGridFixedMath.GridCellCenterFixed(
                    _cellSizeGridRaw,
                    _originXGridRaw,
                    _originZGridRaw,
                    x,
                    y);
            }
        }

        for (int i = 0; i < _sparseCellAnchorIndices.Length; i++)
            anchors[_sparseCellAnchorIndices[i]] = NavigationGridFixedMath.DecodeFixVector2XZ(_sparseCellAnchorFixedRaw, i);
        return anchors;
    }

    private void ValidateFixedAuthorityPayload(string caller)
    {
        if (_fixedAuthorityPayloadVersion != NavigationGridFixedMath.AuthorityPayloadVersion)
        {
            throw new InvalidOperationException(
                $"{caller} failed: fixed authority payload version is invalid. " +
                $"expected={NavigationGridFixedMath.AuthorityPayloadVersion} actual={_fixedAuthorityPayloadVersion}. Rebuild the FlowNavigationGridAsset.");
        }
        if (_cellSizeGridRaw <= 0)
            throw new InvalidOperationException($"{caller} failed: fixed authority cell size must be positive.");
        if (_cellSizeGridRaw != NavigationGridFixedMath.FloatToGridRaw(_cellSize)
            || _originXGridRaw != NavigationGridFixedMath.FloatToGridRaw(_origin.x)
            || _originZGridRaw != NavigationGridFixedMath.FloatToGridRaw(_origin.z))
        {
            throw new InvalidOperationException(
                $"{caller} failed: fixed authority metadata does not match the authored float shadow. Rebuild the FlowNavigationGridAsset.");
        }
        if (!HasValidSparseCellAnchors(CellCount))
            throw new InvalidOperationException($"{caller} failed: sparse navigation anchors are invalid.");

        int expectedPayloadLength = checked(_sparseCellAnchorIndices.Length * NavigationGridFixedMath.EncodedFixVector2Size);
        if (_sparseCellAnchorFixedRaw == null || _sparseCellAnchorFixedRaw.Length != expectedPayloadLength)
        {
            throw new InvalidOperationException(
                $"{caller} failed: fixed anchor payload length is invalid. expected={expectedPayloadLength} actual={_sparseCellAnchorFixedRaw?.Length ?? -1}.");
        }

        for (int i = 0; i < _sparseCellAnchorValues.Length; i++)
        {
            FixVector2 fixedAnchor = NavigationGridFixedMath.DecodeFixVector2XZ(_sparseCellAnchorFixedRaw, i);
            Vector3 floatAnchor = _sparseCellAnchorValues[i];
            if (fixedAnchor.x.RawValue != ((Fix64)floatAnchor.x).RawValue
                || fixedAnchor.y.RawValue != ((Fix64)floatAnchor.z).RawValue)
            {
                throw new InvalidOperationException(
                    $"{caller} failed: fixed anchor does not match the authored float shadow. sparseIndex={i} cellIndex={_sparseCellAnchorIndices[i]}.");
            }
        }
    }

    private void InvalidateRuntimeCellAnchorCaches()
    {
        _runtimeCellAnchorsCache = null;
        _runtimeCellAnchorsFixedCache = null;
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
