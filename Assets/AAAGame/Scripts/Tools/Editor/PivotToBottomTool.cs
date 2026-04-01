using UnityEngine;
using UnityEditor;

public class PivotToBottomTool : EditorWindow
{
    private GameObject _sourceFbx;
    private Vector3 _rotation = new(-90f, 0f, 0f);
    private string _savePath = "Assets/AAAGame/Prefabs";

    private GameObject _preview;
    private Editor _previewEditor;

    [MenuItem("Tools/Pivot To Bottom")]
    static void Open() => GetWindow<PivotToBottomTool>("Pivot To Bottom");

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

        _savePath = EditorGUILayout.TextField("Save Path", _savePath);

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

        // 算底部偏移，把子物体往上抬
        var bounds = GetBounds(_preview);
        _preview.transform.position = new Vector3(0f, -bounds.min.y, 0f);
    }

    void Generate()
    {
        if (!AssetDatabase.IsValidFolder(_savePath))
        {
            Debug.LogError($"路径不存在: {_savePath}");
            return;
        }

        // 全部在场景里做，算和生成用同一个对象，不会有偏差
        var root = new GameObject(_sourceFbx.name);
        var child = (GameObject)PrefabUtility.InstantiatePrefab(_sourceFbx, root.transform);
        child.transform.localRotation = Quaternion.Euler(_rotation);
        child.transform.localPosition = Vector3.zero;

        // 直接在这个实际对象上算 bounds，然后原地调整
        var bounds = GetBounds(root);
        child.transform.localPosition = new Vector3(0f, -bounds.min.y, 0f);

        // 保存 prefab
        string path = $"{_savePath}/{_sourceFbx.name}.prefab";
        path = AssetDatabase.GenerateUniqueAssetPath(path);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        DestroyImmediate(root);

        Debug.Log($"Prefab 已生成: {path}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
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
