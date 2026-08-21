using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class EntityPresentationPrefabTools
{
    private const string UnitFolder = "Assets/AAAGame/Prefabs/Entity/Soldier";
    private const string BuildingFolder = "Assets/AAAGame/Prefabs/Entity/Building";
    private const string UnitTablePath = "Assets/AAAGame/DataTable/CharacterDataDetail.txt";
    private const string BuildingTablePath = "Assets/AAAGame/DataTable/Build/BuildingTable.txt";
    private const string GameConfigPath = "Assets/AAAGame/Config/GameConfig.txt";
    private const string UnitProxySource = UnitFolder + "/背锅侠.prefab";
    private const string BuildingProxySource = BuildingFolder + "/Buil_DeliveryHub_Lv1.prefab";

    [MenuItem("Tools/Entity Presentation/Migrate Existing Entity Prefabs")]
    public static void MigrateExistingEntityPrefabs()
    {
        int changed = MigrateAllExistingPrefabs();
        AssetDatabase.SaveAssets();
        Debug.Log($"[EntityPresentationPrefabTools] Migrated {changed} entity prefabs.");
    }

    [MenuItem("Tools/Entity Presentation/Generate Missing Proxy Prefabs")]
    public static void GenerateMissingProxyPrefabs()
    {
        RequireAsset(UnitProxySource);
        RequireAsset(BuildingProxySource);
        MigrateAllExistingPrefabs();

        int unitCount = CreateMissingPrefabs(ReadUnitPrefabPaths(), UnitProxySource, true);
        int buildingCount = CreateMissingPrefabs(ReadBuildingPrefabPaths(), BuildingProxySource, false);
        MigrateAllExistingPrefabs();
        int resizedUnitCount = NormalizeUnitVisualRadiiInternal();
        ExecuteRequiredMenuItem("Tools/Buildings/Bake Logic Obstacle Shapes");
        ExecuteRequiredMenuItem("Tools/Buildings/Bake Combat Shapes");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[EntityPresentationPrefabTools] Created proxies. units={unitCount}, buildings={buildingCount}, resizedUnits={resizedUnitCount}.");
    }

    [MenuItem("Tools/Entity Presentation/Normalize Unit Visual Radii")]
    public static void NormalizeUnitVisualRadii()
    {
        int changed = NormalizeUnitVisualRadiiInternal();
        AssetDatabase.SaveAssets();
        Debug.Log($"[EntityPresentationPrefabTools] Normalized unit visual radii. changed={changed}.");
    }

    public static float GetUnitVisualRadius(UnitSize size)
    {
        string configKey = size switch
        {
            UnitSize.Small => "SmallUnitCollisionRadius",
            UnitSize.Medium => "MediumUnitCollisionRadius",
            UnitSize.Large => "LargeUnitCollisionRadius",
            UnitSize.SuperLarge => "SuperLargeUnitCollisionRadius",
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown unit size."),
        };

        string radiusText = ReadRequiredGameConfigText(configKey);
        return (float)FixedConfigReader.ParseFixedConfigText(configKey, radiusText);
    }

    [MenuItem("Tools/Entity Presentation/Validate Entity Prefabs")]
    public static void ValidateEntityPrefabs()
    {
        int unitCount = ValidateFolder(UnitFolder, true);
        int buildingCount = ValidateFolder(BuildingFolder, false);
        Debug.Log($"[EntityPresentationPrefabTools] Validation passed. units={unitCount}, buildings={buildingCount}.");
    }

    private static int MigrateAllExistingPrefabs()
    {
        int changed = 0;
        changed += MigrateFolder(UnitFolder, true);
        changed += MigrateFolder(BuildingFolder, false);
        return changed;
    }

    private static int MigrateFolder(string folder, bool unit)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        int changed = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (MigratePrefab(path, unit))
                changed++;
        }
        return changed;
    }

    private static bool MigratePrefab(string path, bool unit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool changed = false;
            Transform display = EnsureDisplay(root, ref changed);
            Animator animator = unit ? ResolveRequiredAnimator(root, display) : ResolveOptionalAnimator(root, display);
            Transform projectileOrigin = EnsureProjectileOrigin(root, display, ref changed);
            ResolveTrailBindings(root, out Transform weaponAnchor, out Transform trailPoint);

            if (unit)
                EnsureUnitRuntimeStructure(root, ref changed);

            EntityPresentationBindings bindings = root.GetComponent<EntityPresentationBindings>();
            if (bindings == null)
            {
                bindings = root.AddComponent<EntityPresentationBindings>();
                changed = true;
            }

            Renderer sizeReferenceRenderer = unit
                ? bindings.SizeReferenceRenderer ?? FindDefaultUnitSizeReferenceRenderer(display)
                : null;
            bool placeholder = bindings.IsPlaceholder;
            if (bindings.DisplayRoot != display
                || bindings.SizeReferenceRenderer != sizeReferenceRenderer
                || bindings.Animator != animator
                || bindings.ProjectileOrigin != projectileOrigin
                || bindings.WeaponAnchor != weaponAnchor
                || bindings.TrailPoint != trailPoint)
            {
                bindings.Configure(display, sizeReferenceRenderer, animator, projectileOrigin, weaponAnchor, trailPoint, placeholder);
                EditorUtility.SetDirty(bindings);
                changed = true;
            }

            ValidateBindingsOrThrow(bindings, unit, path);
            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, path);
            return changed;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform EnsureDisplay(GameObject root, ref bool changed)
    {
        var matches = new List<Transform>();
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            if (child.name == EntityPresentationBindings.DisplayObjectName)
                matches.Add(child);
        }
        if (matches.Count > 1)
            throw new InvalidOperationException($"Entity prefab has duplicate Display roots. entity={root.name}.");
        if (matches.Count == 1)
            return matches[0];

        var displayObject = new GameObject(EntityPresentationBindings.DisplayObjectName);
        Transform display = displayObject.transform;
        display.SetParent(root.transform, false);
        changed = true;

        var visualRoots = new List<Transform>();
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            if (child == display || child.name == EntityPresentationBindings.SocketsObjectName || child.name == "HurtBox")
                continue;
            if (child.GetComponentInChildren<Renderer>(true) != null)
                visualRoots.Add(child);
        }
        if (visualRoots.Count == 0)
            throw new InvalidOperationException($"Entity prefab has no visual hierarchy to move under Display. entity={root.name}.");
        for (int i = 0; i < visualRoots.Count; i++)
            visualRoots[i].SetParent(display, true);
        return display;
    }

    private static Transform EnsureProjectileOrigin(GameObject root, Transform display, ref bool changed)
    {
        EntityPresentationBindings existingBindings = root.GetComponent<EntityPresentationBindings>();
        if (existingBindings != null && existingBindings.ProjectileOrigin != null)
            return existingBindings.ProjectileOrigin;

        Transform sockets = root.transform.Find(EntityPresentationBindings.SocketsObjectName);
        if (sockets == null)
        {
            var socketsObject = new GameObject(EntityPresentationBindings.SocketsObjectName);
            sockets = socketsObject.transform;
            sockets.SetParent(root.transform, false);
            changed = true;
        }

        Transform origin = sockets.Find(EntityPresentationBindings.ProjectileOriginObjectName);
        if (origin == null)
        {
            var originObject = new GameObject(EntityPresentationBindings.ProjectileOriginObjectName);
            origin = originObject.transform;
            origin.SetParent(sockets, false);
            origin.localPosition = ResolvePresentationCenter(root.transform, display);
            changed = true;
        }
        return origin;
    }

    private static Vector3 ResolvePresentationCenter(Transform root, Transform display)
    {
        return root.InverseTransformPoint(GetPresentationBounds(display).center);
    }

    private static Bounds GetPresentationBounds(Transform display)
    {
        Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is not MeshRenderer && renderers[i] is not SkinnedMeshRenderer)
                continue;
            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }
        if (!hasBounds || bounds.size.y <= Mathf.Epsilon)
            throw new InvalidOperationException($"Display has no valid mesh bounds. entity={display.root.name}.");
        return bounds;
    }

    public static Renderer FindDefaultUnitSizeReferenceRenderer(Transform display)
    {
        if (display == null)
            throw new ArgumentNullException(nameof(display));

        Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
        Renderer result = null;
        float greatestHeight = 0f;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;
            if (renderer.bounds.size.y <= greatestHeight)
                continue;
            result = renderer;
            greatestHeight = renderer.bounds.size.y;
        }

        if (result == null || greatestHeight <= Mathf.Epsilon)
            throw new InvalidOperationException($"Unit Display has no valid mesh renderer for SizeReferenceRenderer. entity={display.root.name}.");
        return result;
    }

    public static Bounds GetUnitSizeReferenceBounds(Renderer renderer)
    {
        if (renderer == null)
            throw new ArgumentNullException(nameof(renderer));
        if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            throw new InvalidOperationException($"Unit size reference must be a MeshRenderer or SkinnedMeshRenderer. actual={renderer.GetType().Name}.");
        Bounds bounds = renderer.bounds;
        if (bounds.size.y <= Mathf.Epsilon || Mathf.Max(bounds.size.x, bounds.size.z) <= Mathf.Epsilon)
            throw new InvalidOperationException($"Unit size reference has invalid bounds. renderer={renderer.name}.");
        return bounds;
    }

    private static void ResolveTrailBindings(GameObject root, out Transform weaponAnchor, out Transform trailPoint)
    {
        EntityPresentationBindings bindings = root.GetComponent<EntityPresentationBindings>();
        weaponAnchor = bindings != null ? bindings.WeaponAnchor : null;
        trailPoint = bindings != null ? bindings.TrailPoint : null;

        WeaponAttackTrailEffect effect = root.GetComponentInChildren<WeaponAttackTrailEffect>(true);
        if (effect == null)
            return;

        var serializedEffect = new SerializedObject(effect);
        SerializedProperty legacyWeaponAnchor = serializedEffect.FindProperty("weaponAnchor");
        SerializedProperty legacyTrailPoint = serializedEffect.FindProperty("trailPoint");
        if (weaponAnchor == null && legacyWeaponAnchor != null)
            weaponAnchor = legacyWeaponAnchor.objectReferenceValue as Transform;
        if (trailPoint == null && legacyTrailPoint != null)
            trailPoint = legacyTrailPoint.objectReferenceValue as Transform;
        if (weaponAnchor == null || trailPoint == null)
        {
            throw new InvalidOperationException(
                $"WeaponAttackTrailEffect requires explicit WeaponAnchor and TrailPoint before migration. entity={root.name}.");
        }
    }

    private static void EnsureUnitRuntimeStructure(GameObject root, ref bool changed)
    {
        CharacterController controller = root.GetComponent<CharacterController>();
        if (controller == null)
            throw new InvalidOperationException($"Existing unit prefab has no CharacterController. entity={root.name}.");

        Transform hurtBox = root.transform.Find("HurtBox");
        if (hurtBox == null)
        {
            var hurtBoxObject = new GameObject("HurtBox");
            hurtBox = hurtBoxObject.transform;
            hurtBox.SetParent(root.transform, false);
            int hurtLayer = LayerMask.NameToLayer("Hurt");
            if (hurtLayer < 0)
                throw new InvalidOperationException("Project is missing required Hurt layer.");
            hurtBoxObject.layer = hurtLayer;
            var collider = hurtBoxObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = controller.center;
            collider.size = new Vector3(controller.radius * 2f, controller.height, controller.radius * 2f);
            hurtBoxObject.AddComponent<HurtBox>();
            changed = true;
            return;
        }

        if (hurtBox.GetComponent<BoxCollider>() == null || hurtBox.GetComponent<HurtBox>() == null)
            throw new InvalidOperationException($"Unit HurtBox requires BoxCollider and HurtBox components. entity={root.name}.");
    }

    private static Animator ResolveRequiredAnimator(GameObject root, Transform display)
    {
        Animator animator = ResolveOptionalAnimator(root, display);
        if (animator == null)
            throw new InvalidOperationException($"Unit prefab has no Animator with RuntimeAnimatorController. entity={root.name}.");
        return animator;
    }

    private static Animator ResolveOptionalAnimator(GameObject root, Transform display)
    {
        Animator rootAnimator = root.GetComponent<Animator>();
        if (rootAnimator != null && rootAnimator.runtimeAnimatorController != null)
            return rootAnimator;

        Animator[] animators = display.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i].runtimeAnimatorController != null)
                return animators[i];
        }
        return null;
    }

    private static int CreateMissingPrefabs(IReadOnlyList<string> runtimePaths, string sourcePath, bool unit)
    {
        int created = 0;
        for (int i = 0; i < runtimePaths.Count; i++)
        {
            string runtimePath = runtimePaths[i];
            string assetPath = "Assets/AAAGame/Prefabs/Entity/" + runtimePath + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null)
                continue;
            if (!AssetDatabase.CopyAsset(sourcePath, assetPath))
                throw new InvalidOperationException($"Failed to create proxy prefab. source={sourcePath}, target={assetPath}.");

            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                root.name = Path.GetFileNameWithoutExtension(assetPath);
                EntityPresentationBindings bindings = root.GetComponent<EntityPresentationBindings>();
                if (bindings == null)
                    throw new InvalidOperationException($"Proxy source has no EntityPresentationBindings. source={sourcePath}.");
                bindings.Configure(
                    bindings.DisplayRoot,
                    bindings.SizeReferenceRenderer,
                    bindings.Animator,
                    bindings.ProjectileOrigin,
                    bindings.WeaponAnchor,
                    bindings.TrailPoint,
                    true);
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            MigratePrefab(assetPath, unit);
            created++;
        }
        return created;
    }

    private static int NormalizeUnitVisualRadiiInternal()
    {
        Dictionary<string, UnitSize> sizesByPath = ReadUnitSizesByRuntimePath();
        int changed = 0;
        foreach (KeyValuePair<string, UnitSize> pair in sizesByPath)
        {
            string assetPath = "Assets/AAAGame/Prefabs/Entity/" + pair.Key + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                throw new FileNotFoundException("Cannot normalize a missing unit prefab.", assetPath);
            EntityPresentationBindings assetBindings = prefab.GetComponent<EntityPresentationBindings>();
            if (assetBindings == null)
                throw new InvalidOperationException($"Unit prefab has no EntityPresentationBindings. path={assetPath}.");
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                EntityPresentationBindings bindings = root.GetComponent<EntityPresentationBindings>()
                                                     ?? throw new InvalidOperationException($"Unit prefab has no EntityPresentationBindings. path={assetPath}.");
                Bounds bounds = GetUnitSizeReferenceBounds(bindings.SizeReferenceRenderer);
                float currentRadius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
                if (currentRadius <= Mathf.Epsilon)
                    throw new InvalidOperationException($"Unit has no valid horizontal visual radius. path={assetPath}.");
                float targetRadius = GetUnitVisualRadius(pair.Value);
                float scaleFactor = targetRadius / currentRadius;
                if (bindings.ProjectileOrigin == null)
                    throw new InvalidOperationException($"Unit has no ProjectileOrigin binding. path={assetPath}.");
                if (Mathf.Abs(scaleFactor - 1f) > 0.0001f)
                    bindings.DisplayRoot.localScale *= scaleFactor;

                Bounds presentationBounds = GetPresentationBounds(bindings.DisplayRoot);
                CharacterController controller = root.GetComponent<CharacterController>()
                                                 ?? throw new InvalidOperationException($"Unit has no CharacterController. path={assetPath}.");
                controller.radius = targetRadius;
                controller.height = presentationBounds.size.y;
                controller.center = root.transform.InverseTransformPoint(presentationBounds.center);

                BoxCollider hurtCollider = root.transform.Find("HurtBox")?.GetComponent<BoxCollider>()
                                             ?? throw new InvalidOperationException($"Unit has no HurtBox BoxCollider. path={assetPath}.");
                hurtCollider.center = controller.center;
                hurtCollider.size = new Vector3(targetRadius * 2f, controller.height, targetRadius * 2f);
                bindings.ProjectileOrigin.localPosition = ResolvePresentationCenter(root.transform, bindings.DisplayRoot);
                ValidateBindingsOrThrow(bindings, true, assetPath);
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                changed++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        return changed;
    }

    private static Dictionary<string, UnitSize> ReadUnitSizesByRuntimePath()
    {
        var result = new Dictionary<string, UnitSize>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(UnitTablePath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] columns = lines[i].Split('\t');
            if (columns.Length <= 12 || string.IsNullOrWhiteSpace(columns[6]) || string.IsNullOrWhiteSpace(columns[12]))
                throw new InvalidOperationException($"CharacterDataDetail row {i + 1} has no PrefabPath or Size.");
            const string sizePrefix = "UnitSize.";
            string sizeText = columns[12];
            if (!sizeText.StartsWith(sizePrefix, StringComparison.Ordinal)
                || !Enum.TryParse(sizeText.Substring(sizePrefix.Length), out UnitSize size))
            {
                throw new InvalidOperationException($"CharacterDataDetail row {i + 1} has invalid Size '{sizeText}'.");
            }
            string prefabPath = columns[6];
            if (result.TryGetValue(prefabPath, out UnitSize existingSize))
            {
                if (existingSize != size)
                {
                    throw new InvalidOperationException(
                        $"CharacterDataDetail maps PrefabPath '{prefabPath}' to conflicting sizes '{existingSize}' and '{size}'.");
                }
                continue;
            }
            result.Add(prefabPath, size);
        }
        return result;
    }

    private static string ReadRequiredGameConfigText(string configKey)
    {
        if (string.IsNullOrWhiteSpace(configKey))
            throw new ArgumentException("GameConfig key must not be empty.", nameof(configKey));
        if (!File.Exists(GameConfigPath))
            throw new FileNotFoundException("GameConfig file is missing.", GameConfigPath);

        foreach (string line in File.ReadLines(GameConfigPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] columns = line.Split('\t');
            for (int i = 0; i < columns.Length; i++)
            {
                if (!string.Equals(columns[i].Trim(), configKey, StringComparison.Ordinal))
                    continue;
                for (int valueIndex = columns.Length - 1; valueIndex >= 0; valueIndex--)
                {
                    string value = columns[valueIndex].Trim();
                    if (value.Length > 0)
                        return value;
                }
                throw new InvalidOperationException($"GameConfig '{configKey}' has no value.");
            }
        }
        throw new InvalidOperationException($"GameConfig '{configKey}' was not found in {GameConfigPath}.");
    }

    private static List<string> ReadUnitPrefabPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(UnitTablePath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] columns = lines[i].Split('\t');
            if (columns.Length <= 6 || string.IsNullOrWhiteSpace(columns[6]))
                throw new InvalidOperationException($"CharacterDataDetail row {i + 1} has no PrefabPath.");
            paths.Add(columns[6]);
        }
        return new List<string>(paths);
    }

    private static List<string> ReadBuildingPrefabPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(BuildingTablePath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] columns = lines[i].Split('\t');
            for (int column = 10; column <= 12; column++)
            {
                if (columns.Length > column && !string.IsNullOrWhiteSpace(columns[column]))
                    paths.Add(columns[column]);
            }
        }
        return new List<string>(paths);
    }

    private static int ValidateFolder(string folder, bool unit)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException($"Cannot load entity prefab. path={path}.");
            EntityPresentationBindings bindings = prefab.GetComponent<EntityPresentationBindings>();
            if (bindings == null)
                throw new InvalidOperationException($"Entity prefab is missing EntityPresentationBindings. path={path}.");
            ValidateBindingsOrThrow(bindings, unit, path);
        }
        return guids.Length;
    }

    public static void ValidateBindingsOrThrow(EntityPresentationBindings bindings, bool unit, string assetPath)
    {
        if (bindings == null)
            throw new ArgumentNullException(nameof(bindings));

        bindings.ValidateOrThrow(unit);
        if (!unit)
            return;

        RuntimeAnimatorController controller = bindings.Animator.runtimeAnimatorController;
        AnimatorController animatorController = ResolveAnimatorController(controller, assetPath);
        RequireAnimatorParameter(animatorController, "Moving", AnimatorControllerParameterType.Bool, assetPath);
        RequireAnimatorParameter(animatorController, "Attack", AnimatorControllerParameterType.Trigger, assetPath);
    }

    private static AnimatorController ResolveAnimatorController(RuntimeAnimatorController controller, string assetPath)
    {
        RuntimeAnimatorController current = controller;
        var visited = new HashSet<RuntimeAnimatorController>();
        while (current is AnimatorOverrideController overrideController)
        {
            if (!visited.Add(current))
                throw new InvalidOperationException($"Animator override controller cycle detected. prefab={assetPath}, controller={current.name}.");
            current = overrideController.runtimeAnimatorController;
        }

        if (current is AnimatorController animatorController)
            return animatorController;

        throw new InvalidOperationException(
            $"Unit Animator controller must resolve to an AnimatorController asset. prefab={assetPath}, controller={controller.name}.");
    }

    private static void RequireAnimatorParameter(
        AnimatorController controller,
        string parameterName,
        AnimatorControllerParameterType parameterType,
        string assetPath)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.name == parameterName && parameter.type == parameterType)
                return;
        }

        throw new InvalidOperationException(
            $"Unit Animator is missing {parameterType} parameter '{parameterName}'. prefab={assetPath}, controller={AssetDatabase.GetAssetPath(controller)}.");
    }

    private static void RequireAsset(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            throw new FileNotFoundException("Required proxy source prefab is missing.", path);
    }

    private static void ExecuteRequiredMenuItem(string menuPath)
    {
        if (!EditorApplication.ExecuteMenuItem(menuPath))
            throw new InvalidOperationException($"Required editor menu item failed or is missing. menu={menuPath}.");
    }
}
