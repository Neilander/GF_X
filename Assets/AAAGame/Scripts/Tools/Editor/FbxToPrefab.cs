using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class FbxToPrefab : EditorWindow
{
    private enum PrefabType
    {
        Building,
        Unit
    }

    private GameObject _sourceFbx;
    private Vector3 _rotation = new(-90f, 0f, 0f);
    private float _diameter = 1f;
    private string _parentObjectName = string.Empty;
    private bool _replaceSameName = true;
    private PrefabType _prefabType = PrefabType.Building;
    private int _boxGrid = 6; // Box 近似粒度（等于 AutoBoxColliderFromMesh 的 XZResolution）

    private GameObject _preview;
    private Editor _previewEditor;
    private List<Bounds> _boxPreview = new List<Bounds>();
    private Bounds _boxPreviewAABB; // boxes 的 XZ 外包，用于俯视图映射

    [MenuItem("Tools/FbxToPrefab")]
    static void Open() => GetWindow<FbxToPrefab>("FbxToPrefab");

    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        _sourceFbx = (GameObject)EditorGUILayout.ObjectField("FBX", _sourceFbx, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
            RebuildPreview();

        EditorGUI.BeginChangeCheck();
        _rotation = EditorGUILayout.Vector3Field("Rotation", _rotation);
        if (EditorGUI.EndChangeCheck())
            RebuildPreview();

        EditorGUI.BeginChangeCheck();
        _diameter = EditorGUILayout.FloatField("Diameter", _diameter);
        if (EditorGUI.EndChangeCheck())
            RebuildPreview();

        EditorGUI.BeginChangeCheck();
        _prefabType = (PrefabType)EditorGUILayout.EnumPopup("Type", _prefabType);
        if (EditorGUI.EndChangeCheck())
            RebuildBoxPreview();
        _parentObjectName = EditorGUILayout.TextField(new GUIContent("Prefab Name", "可选。不填则用 FBX 文件名。"), _parentObjectName);
        _replaceSameName = EditorGUILayout.Toggle("Replace Same Name", _replaceSameName);

        if (_prefabType == PrefabType.Building)
        {
            EditorGUI.BeginChangeCheck();
            _boxGrid = EditorGUILayout.IntSlider("Box Grid (粒度)", _boxGrid, 2, 16);
            if (EditorGUI.EndChangeCheck())
                RebuildBoxPreview();
        }

        // 3D 预览
        if (_preview != null)
        {
            GUILayout.Label("Preview", EditorStyles.boldLabel);
            if (_previewEditor == null)
                _previewEditor = Editor.CreateEditor(_preview);
            _previewEditor.OnInteractivePreviewGUI(
                GUILayoutUtility.GetRect(256, 256), GUIStyle.none);
        }

        // Box 近似俯视图
        if (_prefabType == PrefabType.Building && _preview != null && _boxPreview.Count > 0)
        {
            GUILayout.Label($"Box Approximation (Top View) — {_boxPreview.Count} boxes", EditorStyles.boldLabel);
            DrawBoxPreviewTopView(GUILayoutUtility.GetRect(256, 256));
        }

        EditorGUI.BeginDisabledGroup(_sourceFbx == null);
        if (GUILayout.Button("Generate Prefab", GUILayout.Height(30)))
            Generate();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawBoxPreviewTopView(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.14f));

        float aabbW = Mathf.Max(_boxPreviewAABB.size.x, 1e-5f);
        float aabbH = Mathf.Max(_boxPreviewAABB.size.z, 1e-5f);
        float margin = 6f;
        float innerW = rect.width - margin * 2f;
        float innerH = rect.height - margin * 2f;
        float scale = Mathf.Min(innerW / aabbW, innerH / aabbH);
        float drawnW = aabbW * scale;
        float drawnH = aabbH * scale;
        float offsetX = rect.x + margin + (innerW - drawnW) * 0.5f;
        float offsetY = rect.y + margin + (innerH - drawnH) * 0.5f;

        Vector2 ToScreen(float x, float z)
        {
            float u = (x - _boxPreviewAABB.min.x) * scale;
            // Z 翻转，让俯视图上方是 +Z
            float v = drawnH - (z - _boxPreviewAABB.min.z) * scale;
            return new Vector2(offsetX + u, offsetY + v);
        }

        // 外框
        var outlineMin = ToScreen(_boxPreviewAABB.min.x, _boxPreviewAABB.max.z);
        EditorGUI.DrawRect(new Rect(outlineMin.x, outlineMin.y, drawnW, drawnH), new Color(0f, 0f, 0f, 0f));
        Handles.BeginGUI();
        Handles.color = new Color(0.5f, 0.5f, 0.5f);
        DrawRectOutline(new Rect(outlineMin.x, outlineMin.y, drawnW, drawnH));

        // 每个 box
        Handles.color = new Color(0.2f, 1f, 0.3f);
        foreach (var b in _boxPreview)
        {
            var topLeft = ToScreen(b.center.x - b.size.x * 0.5f, b.center.z + b.size.z * 0.5f);
            float w = b.size.x * scale;
            float h = b.size.z * scale;
            var r = new Rect(topLeft.x, topLeft.y, w, h);
            EditorGUI.DrawRect(r, new Color(0.2f, 1f, 0.3f, 0.25f));
            DrawRectOutline(r);
        }
        Handles.EndGUI();
    }

    private static void DrawRectOutline(Rect r)
    {
        Handles.DrawLine(new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin));
        Handles.DrawLine(new Vector3(r.xMax, r.yMin), new Vector3(r.xMax, r.yMax));
        Handles.DrawLine(new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax));
        Handles.DrawLine(new Vector3(r.xMin, r.yMax), new Vector3(r.xMin, r.yMin));
    }

    void RebuildPreview()
    {
        DestroyPreview();
        if (_sourceFbx == null) return;

        _preview = Instantiate(_sourceFbx);
        _preview.transform.rotation = Quaternion.Euler(_rotation);
        _preview.hideFlags = HideFlags.HideAndDontSave;

        if (!TryApplyDiameter(_preview.transform, _diameter, out var previewScaleError))
        {
            Debug.LogError(previewScaleError);
            return;
        }

        if (!TryAlignToBottomAndCenter(_preview.transform, _preview.transform, out var previewAlignError))
        {
            Debug.LogError(previewAlignError);
        }

        RebuildBoxPreview();
    }

    void RebuildBoxPreview()
    {
        _boxPreview.Clear();
        if (_sourceFbx == null || _prefabType != PrefabType.Building)
        {
            Repaint();
            return;
        }

        // 用和 Generate 完全一样的 root+child 结构来算 box，保证预览和实际生成一致
        var tempRoot = new GameObject("_tempBoxCalc");
        tempRoot.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var child = (GameObject)PrefabUtility.InstantiatePrefab(_sourceFbx, tempRoot.transform);
            if (child == null) child = Instantiate(_sourceFbx, tempRoot.transform);
            child.transform.localRotation = Quaternion.Euler(_rotation);
            child.transform.localPosition = Vector3.zero;

            if (!TryApplyDiameter(child.transform, _diameter, out _)) return;
            if (!TryAlignToBottomAndCenter(tempRoot.transform, child.transform, out _)) return;

            _boxPreview = AutoBoxColliderFromMesh.ComputeApproximation(tempRoot, _boxGrid, AutoBoxColliderFromMesh.DefaultYLayers);
            if (_boxPreview.Count > 0)
            {
                _boxPreviewAABB = _boxPreview[0];
                for (int i = 1; i < _boxPreview.Count; i++)
                    _boxPreviewAABB.Encapsulate(_boxPreview[i]);
            }
        }
        finally
        {
            DestroyImmediate(tempRoot);
        }
        Repaint();
    }

    void Generate()
    {
        string savePath = GetSavePath(_prefabType);
        if (!AssetDatabase.IsValidFolder(savePath))
        {
            Debug.LogError($"路径不存在: {savePath}");
            return;
        }

        var rootName = string.IsNullOrWhiteSpace(_parentObjectName)
            ? _sourceFbx.name
            : _parentObjectName.Trim();

        // 全部在场景里做，算和生成用同一个对象，不会有偏差
        var root = new GameObject(rootName);
        var child = (GameObject)PrefabUtility.InstantiatePrefab(_sourceFbx, root.transform);
        child.transform.localRotation = Quaternion.Euler(_rotation);
        child.transform.localPosition = Vector3.zero;

        if (!TryApplyDiameter(child.transform, _diameter, out var scaleError))
        {
            Debug.LogError(scaleError);
            DestroyImmediate(root);
            return;
        }

        if (!TryAlignToBottomAndCenter(root.transform, child.transform, out var alignError))
        {
            Debug.LogError(alignError);
            DestroyImmediate(root);
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
        {
            Debug.LogError("未找到 Layer: Ground，请先在项目里创建该 Layer。");
            DestroyImmediate(root);
            return;
        }
        SetLayerRecursively(child.transform, groundLayer);

        if (_prefabType == PrefabType.Building)
        {
            // 用体素化多 BoxCollider 代替 Convex MeshCollider（Convex MeshCollider 参与 NavMesh 烘焙在部分平台/版本下无效）
            var boxes = AutoBoxColliderFromMesh.ComputeApproximation(root, _boxGrid, AutoBoxColliderFromMesh.DefaultYLayers);
            if (boxes.Count == 0)
            {
                Debug.LogError("建筑类型要求生成 BoxCollider，但未找到可用 Mesh 或近似结果为空。");
                DestroyImmediate(root);
                return;
            }
            AutoBoxColliderFromMesh.Apply(root, boxes, recordUndo: false);
        }

        // 保存 prefab
        string path = $"{savePath}/{rootName}.prefab";
        if (!_replaceSameName)
            path = AssetDatabase.GenerateUniqueAssetPath(path);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        DestroyImmediate(root);

        Debug.Log($"Prefab 已生成: {path}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }

    static string GetSavePath(PrefabType prefabType)
    {
        return prefabType == PrefabType.Building
            ? "Assets/AAAGame/Prefabs/Entity/Building"
            : "Assets/AAAGame/Prefabs/Entity/Unit";
    }

    static bool TryApplyDiameter(Transform targetRoot, float targetDiameter, out string error)
    {
        if (targetDiameter <= 0f)
        {
            error = "直径必须大于 0。";
            return false;
        }

        var bounds = GetBounds(targetRoot.gameObject);
        float currentDiameter = Mathf.Max(bounds.size.x, bounds.size.z);
        if (currentDiameter <= Mathf.Epsilon)
        {
            error = "无法计算有效的 mesh bound 直径，请确认 FBX 下存在有效 Mesh。";
            return false;
        }

        float scaleFactor = targetDiameter / currentDiameter;
        targetRoot.localScale *= scaleFactor;
        error = null;
        return true;
    }

    static bool TryAlignToBottomAndCenter(Transform boundsRoot, Transform childRoot, out string error)
    {
        var bounds = GetBounds(boundsRoot.gameObject);
        var childLocalPosition = childRoot.localPosition;
        childLocalPosition.y = -bounds.min.y;
        childRoot.localPosition = childLocalPosition;

        if (!TryGetColliderMesh(childRoot, out var mesh, out var meshTransform))
        {
            error = "未找到可用于对齐中心的 Mesh，请确认 FBX 下存在 MeshFilter 或 SkinnedMeshRenderer。";
            return false;
        }

        var centerInRoot = boundsRoot.InverseTransformPoint(meshTransform.TransformPoint(mesh.bounds.center));
        childLocalPosition = childRoot.localPosition;
        childLocalPosition.x -= centerInRoot.x;
        childLocalPosition.z -= centerInRoot.z;
        childRoot.localPosition = childLocalPosition;

        error = null;
        return true;
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root)
            SetLayerRecursively(child, layer);
    }

    static bool TryGetColliderMesh(Transform root, out Mesh mesh, out Transform meshTransform)
    {
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            mesh = mf.sharedMesh;
            meshTransform = mf.transform;
            return true;
        }

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null) continue;
            mesh = smr.sharedMesh;
            meshTransform = smr.transform;
            return true;
        }

        mesh = null;
        meshTransform = null;
        return false;
    }

    /// <summary>
    /// 从 MeshFilter/SkinnedMeshRenderer 的 Mesh 本地包围盒手动算世界 Bounds，
    /// 不依赖 Renderer.bounds 的帧刷新。
    /// </summary>
    static Bounds GetBounds(GameObject go)
    {
        bool first = true;
        var result = new Bounds();

        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            EncapsulateMesh(mf.sharedMesh.bounds, mf.transform, ref result, ref first);
        }

        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null) continue;
            EncapsulateMesh(smr.sharedMesh.bounds, smr.transform, ref result, ref first);
        }

        return result;
    }

    static void EncapsulateMesh(Bounds localBounds, Transform t, ref Bounds result, ref bool first)
    {
        // 把本地 AABB 的 8 个角转到世界空间
        var min = localBounds.min;
        var max = localBounds.max;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y,
                (i & 4) == 0 ? min.z : max.z
            );
            var worldPt = t.TransformPoint(corner);

            if (first)
            {
                result = new Bounds(worldPt, Vector3.zero);
                first = false;
            }
            else
            {
                result.Encapsulate(worldPt);
            }
        }
    }

    void DestroyPreview()
    {
        if (_previewEditor != null)
        {
            DestroyImmediate(_previewEditor);
            _previewEditor = null;
        }
        if (_preview != null)
        {
            DestroyImmediate(_preview);
            _preview = null;
        }
    }

    void OnDisable() => DestroyPreview();
}
