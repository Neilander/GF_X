#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class AutoBoxColliderFromMesh
{
    // 参数（要调改这里，单次 Editor 工具用，不做 GUI 窗口）
    public const int DefaultXZResolution = 16;  // 底面 XZ 切成 N*N 格，越大越精细越慢
    public const int DefaultYLayers = 1;        // Y 方向分层数，>1 可处理坡顶/尖塔
    public const string AutoBoxNamePrefix = "_AutoBox_";

    [MenuItem("Tools/Mesh Collider/Apply Box Collider Approximation")]
    private static void ApplyMenu()
    {
        var roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0)
        {
            Debug.LogWarning("[AutoBoxCollider] 没选中任何物体");
            return;
        }

        int totalBoxes = 0, ok = 0;
        foreach (var root in roots)
        {
            var boxes = ComputeApproximation(root, DefaultXZResolution, DefaultYLayers);
            if (boxes.Count == 0)
            {
                Debug.LogWarning($"[AutoBoxCollider] {root.name}: 没有有效几何或未产生 box");
                continue;
            }
            Apply(root, boxes);
            totalBoxes += boxes.Count;
            ok++;
        }

        AutoBoxColliderPreview.InvalidateCache();
        SceneView.RepaintAll();
        Debug.Log($"[AutoBoxCollider] 处理 {ok}/{roots.Length} 个 root, 共生成 {totalBoxes} 个 BoxCollider");
    }

    [MenuItem("Tools/Mesh Collider/Apply Box Collider Approximation", true)]
    private static bool ApplyValidate() => Selection.gameObjects != null && Selection.gameObjects.Length > 0;

    [MenuItem("Tools/Mesh Collider/Toggle Box Collider Preview")]
    private static void TogglePreviewMenu()
    {
        AutoBoxColliderPreview.Enabled = !AutoBoxColliderPreview.Enabled;
        AutoBoxColliderPreview.InvalidateCache();
        Debug.Log($"[AutoBoxCollider] 预览线框: {(AutoBoxColliderPreview.Enabled ? "开" : "关")}");
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Mesh Collider/Toggle Box Collider Preview", true)]
    private static bool TogglePreviewValidate()
    {
        Menu.SetChecked("Tools/Mesh Collider/Toggle Box Collider Preview", AutoBoxColliderPreview.Enabled);
        return true;
    }

    [MenuItem("Tools/Mesh Collider/Clear Auto Boxes On Selection")]
    private static void ClearAutoBoxesMenu()
    {
        var roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0) return;
        int cleared = 0;
        foreach (var root in roots)
            cleared += RemoveAutoBoxChildren(root);
        AutoBoxColliderPreview.InvalidateCache();
        SceneView.RepaintAll();
        Debug.Log($"[AutoBoxCollider] 清理 {cleared} 个自动生成的 box");
    }

    /// <summary>
    /// XZ 平面体素化：把 root 下所有 mesh 的顶面投影切成 xzRes * xzRes 格，
    /// 只保留"格子中心点落在投影内部"的格子（宁可缺不要撑），再 greedy 合并成矩形。
    /// Y 方向按 yLayers 分层，每层独立 voxelize，避免顶部变窄时盒子凸出。
    /// </summary>
    internal static List<Bounds> ComputeApproximation(GameObject root, int xzRes, int yLayers)
    {
        var result = new List<Bounds>();
        if (root == null) return result;
        xzRes = Mathf.Max(4, xzRes);
        yLayers = Mathf.Max(1, yLayers);

        // 1) 收集所有顶点 / 三角面到 root 局部空间
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var rootTf = root.transform;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var v = mesh.vertices;
            var t = mesh.triangles;
            var childTf = mf.transform;
            int baseIdx = verts.Count;
            for (int i = 0; i < v.Length; i++)
            {
                var world = childTf.TransformPoint(v[i]);
                verts.Add(rootTf.InverseTransformPoint(world));
            }
            for (int i = 0; i < t.Length; i++) tris.Add(t[i] + baseIdx);
        }

        if (verts.Count == 0 || tris.Count == 0) return result;

        var bounds = new Bounds(verts[0], Vector3.zero);
        for (int i = 1; i < verts.Count; i++) bounds.Encapsulate(verts[i]);

        float xMin = bounds.min.x, xMax = bounds.max.x;
        float zMin = bounds.min.z, zMax = bounds.max.z;
        float yMin = bounds.min.y, yMax = bounds.max.y;
        if (xMax - xMin < 1e-5f || zMax - zMin < 1e-5f || yMax - yMin < 1e-5f) return result;

        float dx = (xMax - xMin) / xzRes;
        float dz = (zMax - zMin) / xzRes;
        int triCount = tris.Count / 3;

        for (int layer = 0; layer < yLayers; layer++)
        {
            float layerYMin = yMin + (yMax - yMin) * layer / yLayers;
            float layerYMax = yMin + (yMax - yMin) * (layer + 1) / yLayers;

            // 过滤与此层相交的三角面
            var layerTris = new List<int>();
            for (int t = 0; t < triCount; t++)
            {
                var va = verts[tris[t * 3]];
                var vb = verts[tris[t * 3 + 1]];
                var vc = verts[tris[t * 3 + 2]];
                float triMinY = Mathf.Min(va.y, Mathf.Min(vb.y, vc.y));
                float triMaxY = Mathf.Max(va.y, Mathf.Max(vb.y, vc.y));
                if (triMaxY < layerYMin || triMinY > layerYMax) continue;
                layerTris.Add(t);
            }
            if (layerTris.Count == 0) continue;

            // 2) XZ 体素化：中心点在投影三角面内 → solid
            var solid = new bool[xzRes, xzRes];
            for (int ix = 0; ix < xzRes; ix++)
            {
                float cx = xMin + (ix + 0.5f) * dx;
                for (int iz = 0; iz < xzRes; iz++)
                {
                    float cz = zMin + (iz + 0.5f) * dz;
                    var p = new Vector2(cx, cz);
                    foreach (var t in layerTris)
                    {
                        var va = verts[tris[t * 3]];
                        var vb = verts[tris[t * 3 + 1]];
                        var vc = verts[tris[t * 3 + 2]];
                        if (PointInTriangleXZ(p, va, vb, vc))
                        {
                            solid[ix, iz] = true;
                            break;
                        }
                    }
                }
            }

            // 3) 2D greedy merge 成尽量大的矩形
            var used = new bool[xzRes, xzRes];
            for (int ix = 0; ix < xzRes; ix++)
                for (int iz = 0; iz < xzRes; iz++)
                {
                    if (!solid[ix, iz] || used[ix, iz]) continue;

                    int xEnd = ix;
                    while (xEnd + 1 < xzRes && solid[xEnd + 1, iz] && !used[xEnd + 1, iz]) xEnd++;

                    int zEnd = iz;
                    while (zEnd + 1 < xzRes)
                    {
                        bool rowOk = true;
                        for (int x = ix; x <= xEnd; x++)
                        {
                            if (!solid[x, zEnd + 1] || used[x, zEnd + 1]) { rowOk = false; break; }
                        }
                        if (!rowOk) break;
                        zEnd++;
                    }

                    for (int x = ix; x <= xEnd; x++)
                        for (int z = iz; z <= zEnd; z++)
                            used[x, z] = true;

                    float bx0 = xMin + ix * dx;
                    float bx1 = xMin + (xEnd + 1) * dx;
                    float bz0 = zMin + iz * dz;
                    float bz1 = zMin + (zEnd + 1) * dz;
                    var center = new Vector3((bx0 + bx1) * 0.5f, (layerYMin + layerYMax) * 0.5f, (bz0 + bz1) * 0.5f);
                    var size = new Vector3(bx1 - bx0, layerYMax - layerYMin, bz1 - bz0);
                    result.Add(new Bounds(center, size));
                }
        }

        return result;
    }

    private static void Apply(GameObject root, List<Bounds> boxes)
    {
        RemoveAutoBoxChildren(root);
        for (int i = 0; i < boxes.Count; i++)
        {
            var go = new GameObject($"{AutoBoxNamePrefix}{i}");
            Undo.RegisterCreatedObjectUndo(go, "Auto Box Collider");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = boxes[i].center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var bc = Undo.AddComponent<BoxCollider>(go);
            bc.center = Vector3.zero;
            bc.size = boxes[i].size;
        }
        EditorUtility.SetDirty(root);
    }

    private static int RemoveAutoBoxChildren(GameObject root)
    {
        var toDelete = new List<GameObject>();
        for (int i = 0; i < root.transform.childCount; i++)
        {
            var c = root.transform.GetChild(i);
            if (c.name.StartsWith(AutoBoxNamePrefix)) toDelete.Add(c.gameObject);
        }
        foreach (var go in toDelete) Undo.DestroyObjectImmediate(go);
        return toDelete.Count;
    }

    private static bool PointInTriangleXZ(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var a2 = new Vector2(a.x, a.z);
        var b2 = new Vector2(b.x, b.z);
        var c2 = new Vector2(c.x, c.z);
        float d1 = Sign(p, a2, b2);
        float d2 = Sign(p, b2, c2);
        float d3 = Sign(p, c2, a2);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}

[InitializeOnLoad]
internal static class AutoBoxColliderPreview
{
    private const string PrefKey = "AutoBoxColliderPreview.Enabled";
    private static readonly Color LineColor = new Color(0.2f, 1f, 0.3f, 1f);
    private static readonly Dictionary<int, List<Bounds>> _cache = new Dictionary<int, List<Bounds>>();

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, false); // 默认关闭，手动开
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    public static void InvalidateCache() => _cache.Clear();

    static AutoBoxColliderPreview()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        Selection.selectionChanged -= OnSelectionChanged;
        Selection.selectionChanged += OnSelectionChanged;
    }

    private static void OnSelectionChanged()
    {
        _cache.Clear();
        if (Enabled) SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView sv)
    {
        if (!Enabled) return;
        var targets = Selection.gameObjects;
        if (targets == null || targets.Length == 0) return;

        foreach (var root in targets)
        {
            if (root == null) continue;
            int id = root.GetInstanceID();
            if (!_cache.TryGetValue(id, out var boxes))
            {
                boxes = AutoBoxColliderFromMesh.ComputeApproximation(root,
                    AutoBoxColliderFromMesh.DefaultXZResolution,
                    AutoBoxColliderFromMesh.DefaultYLayers);
                _cache[id] = boxes;
            }
            DrawBoxes(root.transform, boxes);
        }
    }

    private static void DrawBoxes(Transform rootTf, List<Bounds> boxes)
    {
        if (boxes == null || boxes.Count == 0) return;
        var prevMatrix = Handles.matrix;
        var prevColor = Handles.color;
        Handles.matrix = rootTf.localToWorldMatrix;
        Handles.color = LineColor;
        foreach (var b in boxes)
            Handles.DrawWireCube(b.center, b.size);
        Handles.matrix = prevMatrix;
        Handles.color = prevColor;
    }
}
#endif
