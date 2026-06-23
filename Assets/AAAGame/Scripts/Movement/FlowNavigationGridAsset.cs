using System;
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

    public int AgentTypeId => _agentTypeId;
    public int Width => _width;
    public int Height => _height;
    public float CellSize => _cellSize;
    public Vector3 Origin => _origin;
    public int CellCount => _width * _height;

    public void SetAgentTypeId(int agentTypeId)
    {
        _agentTypeId = agentTypeId;
    }

    public void SetOrigin(Vector3 origin)
    {
        _origin = origin;
    }

    public void Resize(int width, int height, float cellSize, bool defaultWalkable)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Resize failed: invalid size {width}x{height}.");
        if (cellSize <= 0.0001f)
            throw new InvalidOperationException($"FlowNavigationGridAsset.Resize failed: invalid cellSize={cellSize:F4}.");

        bool[] next = new bool[width * height];
        for (int i = 0; i < next.Length; i++)
            next[i] = defaultWalkable;

        int copyWidth = Mathf.Min(_width, width);
        int copyHeight = Mathf.Min(_height, height);
        if (_walkable != null && _walkable.Length == _width * _height)
        {
            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                    next[x + y * width] = _walkable[x + y * _width];
            }
        }

        _width = width;
        _height = height;
        _cellSize = cellSize;
        _walkable = next;
    }

    public void Fill(bool walkable)
    {
        EnsureValidStorage();
        for (int i = 0; i < _walkable.Length; i++)
            _walkable[i] = walkable;
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
        _walkable[x + y * _width] = walkable;
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

    public Vector3[] CreateCellAnchors()
    {
        EnsureValidStorage();
        Vector3[] anchors = new Vector3[_walkable.Length];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
                anchors[x + y * _width] = GetCellCenter(x, y);
        }

        return anchors;
    }

    private void OnValidate()
    {
        _width = Mathf.Max(1, _width);
        _height = Mathf.Max(1, _height);
        _cellSize = Mathf.Max(0.0001f, _cellSize);
        EnsureValidStorage();
    }

    private void EnsureValidStorage()
    {
        int expectedLength = Mathf.Max(1, _width) * Mathf.Max(1, _height);
        if (_walkable != null && _walkable.Length == expectedLength)
            return;

        bool[] next = new bool[expectedLength];
        if (_walkable != null)
        {
            int count = Mathf.Min(_walkable.Length, next.Length);
            Array.Copy(_walkable, next, count);
        }

        _walkable = next;
    }
}
