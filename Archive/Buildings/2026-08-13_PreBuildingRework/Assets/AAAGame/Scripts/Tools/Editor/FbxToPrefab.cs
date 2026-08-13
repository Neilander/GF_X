using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEditor;

public class FbxToPrefab : EditorWindow
{
    public const string BuildingOutputFolder = "Assets/AAAGame/Prefabs/Entity/Building";
    public const string UnitOutputFolder = "Assets/AAAGame/Prefabs/Entity/Soldier";

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
    private RuntimeAnimatorController _unitAnimatorController;
    private UnitSize _unitSize = UnitSize.Medium;
    private string[] _unitSizeReferencePaths = Array.Empty<string>();
    private int _unitSizeReferenceIndex = -1;
    private bool _createTrailBinding;
    private string _weaponAnchorPath = string.Empty;
    private Vector3 _trailPointOffset = new(0f, 0.38f, 0f);

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
        {
            RefreshUnitSizeReferenceCandidates();
            RebuildPreview();
        }

        EditorGUI.BeginChangeCheck();
        _rotation = EditorGUILayout.Vector3Field("Rotation", _rotation);
        if (EditorGUI.EndChangeCheck())
            RebuildPreview();

        EditorGUI.BeginChangeCheck();
        _prefabType = (PrefabType)EditorGUILayout.EnumPopup("Type", _prefabType);
        if (EditorGUI.EndChangeCheck())
        {
            RefreshUnitSizeReferenceCandidates();
            RebuildPreview();
        }

        EditorGUI.BeginChangeCheck();
        if (_prefabType == PrefabType.Building)
            _diameter = EditorGUILayout.FloatField("Diameter", _diameter);
        else
            _unitSize = (UnitSize)EditorGUILayout.EnumPopup("Unit Size", _unitSize);
        if (EditorGUI.EndChangeCheck())
            RebuildPreview();

        _parentObjectName = EditorGUILayout.TextField(new GUIContent("Prefab Name", "可选。不填则用 FBX 文件名。"), _parentObjectName);
        _replaceSameName = EditorGUILayout.Toggle("Replace Same Name", _replaceSameName);

        if (_prefabType == PrefabType.Building)
        {
            EditorGUI.BeginChangeCheck();
            _boxGrid = EditorGUILayout.IntSlider("Box Grid (粒度)", _boxGrid, 2, 16);
            if (EditorGUI.EndChangeCheck())
                RebuildBoxPreview();
        }
        else
        {
            if (_unitSizeReferencePaths.Length == 0)
            {
                EditorGUILayout.HelpBox("选择 FBX 后必须确认用于尺寸校准的身体 Mesh。", MessageType.Info);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                _unitSizeReferenceIndex = EditorGUILayout.Popup(
                    new GUIContent("Size Reference", "用于匹配配置半径的身体 Mesh；武器和特效不应选入。"),
                    _unitSizeReferenceIndex,
                    _unitSizeReferencePaths);
                if (EditorGUI.EndChangeCheck())
                    RebuildPreview();
            }

            _unitAnimatorController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                "Animator Controller",
                _unitAnimatorController,
                typeof(RuntimeAnimatorController),
                false);
            _createTrailBinding = EditorGUILayout.Toggle("Create Trail Binding", _createTrailBinding);
            if (_createTrailBinding)
            {
                _weaponAnchorPath = EditorGUILayout.TextField(
                    new GUIContent("Weapon Anchor Path", "FBX 根节点下的相对路径；留空时只接受 Humanoid 右手骨。"),
                    _weaponAnchorPath);
                _trailPointOffset = EditorGUILayout.Vector3Field("Trail Point Offset", _trailPointOffset);
            }
        }

        // 3D 预览
        if (_preview != null)
        {
            GUILayout.Label("Preview", EditorStyles.boldLabel);
            if (_previewEditor == null)
                _previewEditor = Editor.CreateEditor(_preview);
            _previewEditor.OnInteractivePreviewGUI(
                GUILayoutUtility.GetRect(256, 256), GUIStyle.none);
            if (_prefabType == PrefabType.Unit)
                DrawUnitSizeMetrics();
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

        if (!TryApplyConfiguredSize(_preview.transform, out var previewScaleError))
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
            throw new InvalidOperationException($"Prefab output folder does not exist. path={savePath}.");
        if (_prefabType == PrefabType.Unit && _unitAnimatorController == null)
            throw new InvalidOperationException("Unit prefab generation requires an explicit Animator Controller.");

        var rootName = string.IsNullOrWhiteSpace(_parentObjectName)
            ? _sourceFbx.name
            : _parentObjectName.Trim();

        var root = new GameObject(rootName);
        try
        {
            var displayObject = new GameObject(EntityPresentationBindings.DisplayObjectName);
            Transform display = displayObject.transform;
            display.SetParent(root.transform, false);
            var child = (GameObject)PrefabUtility.InstantiatePrefab(_sourceFbx, display);
            if (child == null)
                child = Instantiate(_sourceFbx, display);
            child.transform.localRotation = Quaternion.Euler(_rotation);
            child.transform.localPosition = Vector3.zero;

            if (!TryApplyConfiguredSize(child.transform, out var scaleError))
                throw new InvalidOperationException(scaleError);
            Renderer sizeReferenceRenderer = _prefabType == PrefabType.Unit
                ? ResolveSelectedUnitSizeReferenceRenderer(child.transform)
                : null;
            if (!TryAlignToBottomAndCenter(root.transform, child.transform, out var alignError))
                throw new InvalidOperationException(alignError);

            Transform projectileOrigin = CreateProjectileOrigin(root, display);
            Transform weaponAnchor = null;
            Transform trailPoint = null;
            Animator animator;

            if (_prefabType == PrefabType.Building)
            {
                SetLayerRecursively(child.transform, RequireLayer("Ground"));
                var boxes = AutoBoxColliderFromMesh.ComputeApproximation(root, _boxGrid, AutoBoxColliderFromMesh.DefaultYLayers);
                if (boxes.Count == 0)
                    throw new InvalidOperationException("Building prefab generation produced no BoxCollider approximation.");
                AutoBoxColliderFromMesh.Apply(root, boxes, recordUndo: false);
                animator = child.GetComponentInChildren<Animator>(true);
            }
            else
            {
                animator = ConfigureUnitRuntime(root, child, _unitAnimatorController);
                if (_createTrailBinding)
                {
                    weaponAnchor = ResolveUnitWeaponAnchor(child.transform, animator, _weaponAnchorPath);
                    var trailPointObject = new GameObject("TrailPoint");
                    trailPoint = trailPointObject.transform;
                    trailPoint.SetParent(weaponAnchor, false);
                    trailPoint.localPosition = _trailPointOffset;
                    root.AddComponent<WeaponAttackTrailEffect>();
                }
            }

            EntityPresentationBindings bindings = root.AddComponent<EntityPresentationBindings>();
            bindings.Configure(display, sizeReferenceRenderer, animator, projectileOrigin, weaponAnchor, trailPoint, false);

            string path = $"{savePath}/{rootName}.prefab";
            if (!_replaceSameName)
                path = AssetDatabase.GenerateUniqueAssetPath(path);
            EntityPresentationPrefabTools.ValidateBindingsOrThrow(
                bindings,
                _prefabType == PrefabType.Unit,
                path);
            PrefabUtility.SaveAsPrefabAsset(root, path);

            Debug.Log($"Prefab 已生成: {path}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }
        finally
        {
            DestroyImmediate(root);
        }
    }

    static string GetSavePath(PrefabType prefabType)
    {
        return prefabType == PrefabType.Building
            ? BuildingOutputFolder
            : UnitOutputFolder;
    }

    private static int RequireLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            throw new InvalidOperationException($"Project is missing required layer '{layerName}'.");
        return layer;
    }

    private static Transform CreateProjectileOrigin(GameObject root, Transform display)
    {
        Bounds bounds = GetBounds(display.gameObject);
        if (bounds.size == Vector3.zero)
            throw new InvalidOperationException("Cannot create ProjectileOrigin because Display has no valid mesh bounds.");

        var socketsObject = new GameObject(EntityPresentationBindings.SocketsObjectName);
        socketsObject.transform.SetParent(root.transform, false);
        var originObject = new GameObject(EntityPresentationBindings.ProjectileOriginObjectName);
        originObject.transform.SetParent(socketsObject.transform, false);
        originObject.transform.position = bounds.center;
        return originObject.transform;
    }

    private static Animator ConfigureUnitRuntime(
        GameObject root,
        GameObject modelRoot,
        RuntimeAnimatorController animatorController)
    {
        Animator sourceAnimator = modelRoot.GetComponentInChildren<Animator>(true);
        if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.avatar.isValid)
            throw new InvalidOperationException("Unit FBX requires a valid imported Avatar.");

        Avatar avatar = sourceAnimator.avatar;
        if (PrefabUtility.IsPartOfPrefabInstance(modelRoot))
            PrefabUtility.UnpackPrefabInstance(modelRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Animator[] modelAnimators = modelRoot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < modelAnimators.Length; i++)
            DestroyImmediate(modelAnimators[i]);

        Animator animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = animatorController;
        animator.applyRootMotion = false;

        Bounds bounds = GetBounds(modelRoot);
        float height = bounds.size.y;
        float radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
        if (height <= Mathf.Epsilon || radius <= Mathf.Epsilon)
            throw new InvalidOperationException("Unit FBX has invalid bounds for CharacterController generation.");

        root.layer = RequireLayer("character");
        CharacterController characterController = root.AddComponent<CharacterController>();
        characterController.height = height;
        characterController.radius = Mathf.Min(radius, height * 0.5f);
        characterController.center = root.transform.InverseTransformPoint(bounds.center);

        var hurtBoxObject = new GameObject("HurtBox");
        hurtBoxObject.layer = RequireLayer("Hurt");
        hurtBoxObject.transform.SetParent(root.transform, false);
        BoxCollider hurtCollider = hurtBoxObject.AddComponent<BoxCollider>();
        hurtCollider.isTrigger = true;
        hurtCollider.center = characterController.center;
        hurtCollider.size = new Vector3(
            characterController.radius * 2f,
            characterController.height,
            characterController.radius * 2f);
        hurtBoxObject.AddComponent<HurtBox>();
        return animator;
    }

    private static Transform ResolveUnitWeaponAnchor(Transform modelRoot, Animator animator, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            Transform explicitAnchor = modelRoot.Find(relativePath.Trim());
            if (explicitAnchor == null)
                throw new InvalidOperationException($"Weapon Anchor Path does not exist below the FBX root. path={relativePath}.");
            return explicitAnchor;
        }

        if (animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException("Empty Weapon Anchor Path requires a Humanoid Avatar.");
        animator.Rebind();
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (rightHand == null)
            throw new InvalidOperationException("Humanoid Avatar does not expose a RightHand transform.");
        return rightHand;
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

    private bool TryApplyConfiguredSize(Transform targetRoot, out string error)
    {
        if (_prefabType == PrefabType.Building)
            return TryApplyDiameter(targetRoot, _diameter, out error);

        Renderer sizeReferenceRenderer;
        try
        {
            sizeReferenceRenderer = ResolveSelectedUnitSizeReferenceRenderer(targetRoot);
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }

        float targetRadius = EntityPresentationPrefabTools.GetUnitVisualRadius(_unitSize);
        Bounds bounds = EntityPresentationPrefabTools.GetUnitSizeReferenceBounds(sizeReferenceRenderer);
        float currentDiameter = Mathf.Max(bounds.size.x, bounds.size.z);
        targetRoot.localScale *= targetRadius * 2f / currentDiameter;
        error = null;
        return true;
    }

    private void RefreshUnitSizeReferenceCandidates()
    {
        _unitSizeReferencePaths = Array.Empty<string>();
        _unitSizeReferenceIndex = -1;
        if (_sourceFbx == null || _prefabType != PrefabType.Unit)
            return;

        Renderer[] renderers = _sourceFbx.GetComponentsInChildren<Renderer>(true);
        Renderer defaultRenderer = EntityPresentationPrefabTools.FindDefaultUnitSizeReferenceRenderer(_sourceFbx.transform);
        var paths = new List<string>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, _sourceFbx.transform);
            if (paths.Contains(path))
                throw new InvalidOperationException($"FBX has multiple mesh renderers on the same transform. path={path}.");
            paths.Add(path);
            if (renderer == defaultRenderer)
                _unitSizeReferenceIndex = paths.Count - 1;
        }

        if (paths.Count == 0 || _unitSizeReferenceIndex < 0)
            throw new InvalidOperationException($"Unit FBX has no valid MeshRenderer or SkinnedMeshRenderer. asset={_sourceFbx.name}.");
        _unitSizeReferencePaths = paths.ToArray();
    }

    private Renderer ResolveSelectedUnitSizeReferenceRenderer(Transform modelRoot)
    {
        if (_unitSizeReferenceIndex < 0 || _unitSizeReferenceIndex >= _unitSizeReferencePaths.Length)
            throw new InvalidOperationException("Unit generation requires an explicit Size Reference selection.");
        string path = _unitSizeReferencePaths[_unitSizeReferenceIndex];
        Transform target = string.IsNullOrEmpty(path) ? modelRoot : modelRoot.Find(path);
        if (target == null)
            throw new InvalidOperationException($"Size Reference path does not exist below the FBX root. path={path}.");
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            throw new InvalidOperationException($"Size Reference is not a mesh renderer. path={path}.");
        return renderer;
    }

    private void DrawUnitSizeMetrics()
    {
        Renderer renderer = ResolveSelectedUnitSizeReferenceRenderer(_preview.transform);
        Bounds referenceBounds = EntityPresentationPrefabTools.GetUnitSizeReferenceBounds(renderer);
        float actualRadius = Mathf.Max(referenceBounds.size.x, referenceBounds.size.z) * 0.5f;
        float targetRadius = EntityPresentationPrefabTools.GetUnitVisualRadius(_unitSize);
        EditorGUILayout.LabelField("Body Radius", $"{actualRadius:F4} (target {targetRadius:F4})");
        EditorGUILayout.LabelField("Body Height", referenceBounds.size.y.ToString("F4"));
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
