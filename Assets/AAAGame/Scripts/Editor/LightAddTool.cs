using UnityEditor;
using UnityEngine;

/// <summary>
/// 加灯（更通用：往一个 Prefab 资产下添加另一个 Prefab 实例）工具。
///
/// 流程：
///   1. 拖入 Child Prefab（要被添加的，比如灯）
///   2. 拖入 Parent Prefab（目标 prefab 资产）
///   3. 调整 Local Position / Rotation / Scale，Scene 里实时预览
///   4. 点 Apply 把子 prefab 写入到父 prefab 资产
///
/// 预览：在当前场景里临时 InstantiatePrefab 父 prefab，子 prefab 作为子节点。
/// 预览节点 hideFlags = DontSave，不会被场景保存。窗口关闭/换 prefab 时自动清理。
/// </summary>
public class LightAddTool : EditorWindow
{
    private GameObject _childPrefab;
    private GameObject _parentPrefab;

    private Vector3 _localPosition = Vector3.zero;
    private Vector3 _localRotation = Vector3.zero;
    private Vector3 _localScale = Vector3.one;

    // 预览节点
    private GameObject _previewParentInstance;
    private GameObject _previewChildInstance;

    [MenuItem("Tools/加灯工具")]
    public static void Open()
    {
        var w = GetWindow<LightAddTool>("加灯工具");
        w.minSize = new Vector2(340, 360);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Prefab 选择", EditorStyles.boldLabel);

        GameObject newChild = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Child Prefab", "要添加的 prefab（如灯）"),
            _childPrefab, typeof(GameObject), false);

        GameObject newParent = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Parent Prefab", "目标 prefab 资产，子 prefab 会被加到它的根下"),
            _parentPrefab, typeof(GameObject), false);

        bool prefabsChanged = newChild != _childPrefab || newParent != _parentPrefab;
        _childPrefab = newChild;
        _parentPrefab = newParent;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("相对 Transform（相对于 Parent 根）", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _localPosition = EditorGUILayout.Vector3Field("Local Position", _localPosition);
        _localRotation = EditorGUILayout.Vector3Field("Local Rotation", _localRotation);
        _localScale = EditorGUILayout.Vector3Field("Local Scale", _localScale);
        bool transformChanged = EditorGUI.EndChangeCheck();

        if (prefabsChanged) RefreshPreview();
        else if (transformChanged) UpdatePreviewTransform();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(_previewParentInstance == null))
        {
            if (GUILayout.Button("聚焦预览（Frame）"))
            {
                if (_previewChildInstance != null) Selection.activeGameObject = _previewChildInstance;
                else Selection.activeGameObject = _previewParentInstance;
                SceneView.FrameLastActiveSceneView();
            }
        }

        using (new EditorGUI.DisabledScope(!CanApply()))
        {
            if (GUILayout.Button("Apply（写入 Parent Prefab）", GUILayout.Height(28)))
            {
                Apply();
            }
        }

        if (GUILayout.Button("清空预览"))
        {
            ClearPreview();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "拖入两个 Prefab 后会自动在当前 Scene 创建预览。\n" +
            "Apply 修改 Parent Prefab 资产；预览仅在 Editor 临时存在，不会进入场景保存。\n" +
            "关闭窗口或更换 Prefab 会自动清理预览。",
            MessageType.Info);
    }

    private bool CanApply()
    {
        if (_childPrefab == null || _parentPrefab == null) return false;
        var parentPath = AssetDatabase.GetAssetPath(_parentPrefab);
        return !string.IsNullOrEmpty(parentPath) && parentPath.EndsWith(".prefab");
    }

    // ── 预览 ──

    private void RefreshPreview()
    {
        ClearPreview();
        if (_parentPrefab == null) return;

        _previewParentInstance = (GameObject)PrefabUtility.InstantiatePrefab(_parentPrefab);
        if (_previewParentInstance == null) return;
        _previewParentInstance.name = $"[Preview] {_parentPrefab.name}";
        _previewParentInstance.hideFlags = HideFlags.DontSave;
        // 不进入构建/保存：放到根，位置归零便于看
        _previewParentInstance.transform.position = Vector3.zero;
        _previewParentInstance.transform.rotation = Quaternion.identity;

        if (_childPrefab != null)
        {
            _previewChildInstance = (GameObject)PrefabUtility.InstantiatePrefab(_childPrefab, _previewParentInstance.transform);
            if (_previewChildInstance != null)
            {
                _previewChildInstance.name = $"[Preview-Child] {_childPrefab.name}";
                UpdatePreviewTransform();
            }
        }

        Selection.activeGameObject = _previewChildInstance != null ? _previewChildInstance : _previewParentInstance;
        SceneView.FrameLastActiveSceneView();
    }

    private void UpdatePreviewTransform()
    {
        if (_previewChildInstance == null) return;
        var t = _previewChildInstance.transform;
        t.localPosition = _localPosition;
        t.localEulerAngles = _localRotation;
        t.localScale = _localScale;
    }

    private void ClearPreview()
    {
        if (_previewParentInstance != null)
        {
            DestroyImmediate(_previewParentInstance);
        }
        _previewParentInstance = null;
        _previewChildInstance = null;
    }

    // ── 写入资产 ──

    private void Apply()
    {
        if (!CanApply()) return;

        string parentPath = AssetDatabase.GetAssetPath(_parentPrefab);
        GameObject parentContents = PrefabUtility.LoadPrefabContents(parentPath);
        if (parentContents == null)
        {
            Debug.LogError($"[LightAddTool] LoadPrefabContents 失败：{parentPath}");
            return;
        }

        try
        {
            GameObject childInstance = (GameObject)PrefabUtility.InstantiatePrefab(_childPrefab, parentContents.transform);
            if (childInstance == null)
            {
                Debug.LogError("[LightAddTool] InstantiatePrefab 失败");
                return;
            }
            childInstance.transform.localPosition = _localPosition;
            childInstance.transform.localEulerAngles = _localRotation;
            childInstance.transform.localScale = _localScale;

            PrefabUtility.SaveAsPrefabAsset(parentContents, parentPath, out bool success);
            if (success)
                Debug.Log($"[LightAddTool] ✔ 已添加 {_childPrefab.name} 到 {parentPath}");
            else
                Debug.LogError($"[LightAddTool] SaveAsPrefabAsset 返回 false：{parentPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(parentContents);
        }

        // 写入后刷新预览（让预览反映新内容）
        RefreshPreview();
        AssetDatabase.Refresh();
    }

    // ── 生命周期 ──

    private void OnDisable() => ClearPreview();

    private void OnDestroy() => ClearPreview();
}
