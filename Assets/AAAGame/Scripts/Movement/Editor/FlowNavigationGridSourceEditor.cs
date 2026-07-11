using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FlowNavigationGridSource))]
public sealed class FlowNavigationGridSourceEditor : Editor
{
    private bool _paintEnabled;
    private bool _paintWalkable = true;
    private int _brushRadius;
    private int _resizeWidth = 16;
    private int _resizeHeight = 16;
    private float _resizeCellSize = 1f;
    private bool _resizeDefaultWalkable;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        FlowNavigationGridSource source = (FlowNavigationGridSource)target;
        FlowNavigationGridAsset grid = source.Grid;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(grid == null))
        {
            if (GUILayout.Button("Apply To Flow Field"))
                source.ApplyToFlowField();

            if (GUILayout.Button("Clear Flow Field Source"))
                source.ClearFlowFieldSource();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene Paint", EditorStyles.boldLabel);
        _paintEnabled = EditorGUILayout.Toggle("Enable Paint", _paintEnabled);
        _paintWalkable = EditorGUILayout.Toggle("Paint Walkable", _paintWalkable);
        _brushRadius = EditorGUILayout.IntSlider("Brush Radius", _brushRadius, 0, 8);

        if (grid != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grid Edit", EditorStyles.boldLabel);
            _resizeWidth = EditorGUILayout.IntField("Width", _resizeWidth);
            _resizeHeight = EditorGUILayout.IntField("Height", _resizeHeight);
            _resizeCellSize = EditorGUILayout.FloatField("Cell Size", _resizeCellSize);
            _resizeDefaultWalkable = EditorGUILayout.Toggle("Default Walkable", _resizeDefaultWalkable);

            if (GUILayout.Button("Resize Grid Asset"))
            {
                Undo.RecordObject(grid, "Resize Flow Navigation Grid");
                grid.Resize(Mathf.Max(1, _resizeWidth), Mathf.Max(1, _resizeHeight), Mathf.Max(0.0001f, _resizeCellSize), _resizeDefaultWalkable);
                EditorUtility.SetDirty(grid);
                if (source.ApplyInEditMode)
                    source.ApplyToFlowField();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fill Walkable"))
                    Fill(grid, source, true);
                if (GUILayout.Button("Fill Blocked"))
                    Fill(grid, source, false);
            }
        }

        if (_paintEnabled)
            EditorGUILayout.HelpBox("Scene view: left-click or drag on the grid to paint cells. Hold Alt to orbit without painting.", MessageType.Info);
    }

    private void OnSceneGUI()
    {
        if (!_paintEnabled)
            return;

        FlowNavigationGridSource source = (FlowNavigationGridSource)target;
        FlowNavigationGridAsset grid = source.Grid;
        if (grid == null)
            return;

        Event e = Event.current;
        if (e.alt)
            return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(Vector3.up, grid.Origin);
        if (!plane.Raycast(ray, out float distance))
            return;

        Vector3 hit = ray.GetPoint(distance);
        if (!grid.WorldToCell(hit, out int cellX, out int cellY))
            return;

        Handles.color = _paintWalkable ? new Color(0.1f, 0.9f, 0.2f, 0.8f) : new Color(1f, 0.1f, 0.1f, 0.8f);
        Handles.DrawWireCube(grid.GetCellCenter(cellX, cellY), new Vector3(grid.CellSize, 0.05f, grid.CellSize) * (1 + _brushRadius * 2));

        if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag)
            return;
        if (e.button != 0)
            return;

        Undo.RecordObject(grid, "Paint Flow Navigation Grid");
        for (int y = cellY - _brushRadius; y <= cellY + _brushRadius; y++)
        {
            for (int x = cellX - _brushRadius; x <= cellX + _brushRadius; x++)
                grid.SetCellWalkable(x, y, _paintWalkable);
        }

        EditorUtility.SetDirty(grid);
        if (source.ApplyInEditMode)
            source.ApplyToFlowField();

        e.Use();
        SceneView.RepaintAll();
    }

    private static void Fill(FlowNavigationGridAsset grid, FlowNavigationGridSource source, bool walkable)
    {
        Undo.RecordObject(grid, walkable ? "Fill Flow Grid Walkable" : "Fill Flow Grid Blocked");
        grid.Fill(walkable);
        EditorUtility.SetDirty(grid);
        if (source.ApplyInEditMode)
            source.ApplyToFlowField();
    }
}
