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

    private GameObject _preview;
    private Editor _previewEditor;

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

        _prefabType = (PrefabType)EditorGUILayout.EnumPopup("Type", _prefabType);
        _parentObjectName = EditorGUILayout.TextField("Parent Name", _parentObjectName);
        _replaceSameName = EditorGUILayout.Toggle("Replace Same Name", _replaceSameName);

        // 预览
        if (_preview != null)
        {
            GUILayout.Label("Preview", EditorStyles.boldLabel);
            if (_previewEditor == null)
                _previewEditor = Editor.CreateEditor(_preview);
            _previewEditor.OnInteractivePreviewGUI(
                GUILayoutUtility.GetRect(256, 256), GUIStyle.none);
        }

        EditorGUI.BeginDisabledGroup(_sourceFbx == null);
        if (GUILayout.Button("Generate Prefab", GUILayout.Height(30)))
            Generate();
        EditorGUI.EndDisabledGroup();
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
            if (!TryGetColliderMesh(child.transform, out var colliderMesh, out _))
            {
                Debug.LogError("建筑类型要求生成 MeshCollider，但未找到可用 Mesh。请确认 FBX 下存在 MeshFilter 或 SkinnedMeshRenderer。");
                DestroyImmediate(root);
                return;
            }

            var meshCollider = child.GetComponent<MeshCollider>();
            if (meshCollider == null)
                meshCollider = child.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = colliderMesh;
            meshCollider.convex = true;
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
