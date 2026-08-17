#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildingFootprintPrefabTools
{
    private const string BuildingFolder = "Assets/AAAGame/Prefabs/Entity/Building";
    private const string BuildingTablePath = "Assets/AAAGame/DataTable/Build/BuildingTable.txt";
    private const string RuntimePrefabPrefix = "Assets/AAAGame/Prefabs/Entity/";

    [MenuItem("Tools/AAAGame/Normalize Building Footprints")]
    public static void NormalizeAll()
    {
        Dictionary<string, BuilType> typesByPath = ReadBuildingPrefabTypes();
        RequireCompletePrefabCoverage(typesByPath);

        int changed = 0;
        foreach (KeyValuePair<string, BuilType> pair in typesByPath)
        {
            NormalizePrefab(pair.Key, pair.Value);
            changed++;
        }

        BuildingLogicObstacleShapeBaker.Bake();
        BuildingCombatShapeBaker.Bake();
        AssetDatabase.Refresh();
        ValidateAll();
        Debug.Log($"[BuildingFootprintPrefabTools] Normalized {changed} building prefabs.");
    }

    [MenuItem("Tools/AAAGame/Validate Building Footprints")]
    public static void ValidateAll()
    {
        Dictionary<string, BuilType> typesByPath = ReadBuildingPrefabTypes();
        RequireCompletePrefabCoverage(typesByPath);
        foreach (KeyValuePair<string, BuilType> pair in typesByPath)
            ValidatePrefab(pair.Key, pair.Value);
        Debug.Log($"[BuildingFootprintPrefabTools] Validated {typesByPath.Count} building prefabs.");
    }

    private static Dictionary<string, BuilType> ReadBuildingPrefabTypes()
    {
        var result = new Dictionary<string, BuilType>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(BuildingTablePath);
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            string[] columns = line.Split('\t');
            if (columns.Length <= 12)
                throw new InvalidOperationException($"Building table row has too few columns. line={lineIndex + 1}.");
            if (!TryParseBuildingType(columns[6], out BuilType buildingType))
                throw new InvalidOperationException($"Building table row has an invalid type. line={lineIndex + 1}, value={columns[6]}.");

            for (int columnIndex = 10; columnIndex <= 12; columnIndex++)
            {
                if (string.IsNullOrWhiteSpace(columns[columnIndex]))
                    continue;
                string assetPath = RuntimePrefabPrefix + columns[columnIndex].Trim() + ".prefab";
                if (result.TryGetValue(assetPath, out BuilType existingType) && existingType != buildingType)
                {
                    throw new InvalidOperationException(
                        $"Building prefab is assigned to conflicting types. path={assetPath}, first={existingType}, second={buildingType}.");
                }
                result[assetPath] = buildingType;
            }
        }

        return result;
    }

    private static bool TryParseBuildingType(string tableValue, out BuilType buildingType)
    {
        const string prefix = "BuilType.";
        string value = tableValue != null ? tableValue.Trim() : string.Empty;
        if (value.StartsWith(prefix, StringComparison.Ordinal))
            value = value.Substring(prefix.Length);
        return Enum.TryParse(value, false, out buildingType);
    }

    private static void RequireCompletePrefabCoverage(IReadOnlyDictionary<string, BuilType> typesByPath)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { BuildingFolder });
        if (guids.Length != typesByPath.Count)
        {
            throw new InvalidOperationException(
                $"Building table/prefab coverage mismatch. table={typesByPath.Count}, folder={guids.Length}.");
        }

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!typesByPath.ContainsKey(path))
                throw new InvalidOperationException($"Building prefab has no table type. path={path}.");
        }
    }

    private static void NormalizePrefab(string assetPath, BuilType buildingType)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            Transform display = root.transform.Find(EntityPresentationBindings.DisplayObjectName);
            if (display == null)
                throw new InvalidOperationException($"Building prefab has no Display root. path={assetPath}.");

            Bounds visualBounds = GetLocalVisualBounds(root.transform, display);
            float currentDiameter = Mathf.Max(visualBounds.size.x, visualBounds.size.z);
            if (currentDiameter <= Mathf.Epsilon)
                throw new InvalidOperationException($"Building prefab has invalid Display bounds. path={assetPath}.");

            float targetSize = BuildingFootprint.ResolveWorldSize(buildingType);
            display.localScale *= targetSize / currentDiameter;
            visualBounds = GetLocalVisualBounds(root.transform, display);
            display.localPosition -= new Vector3(visualBounds.center.x, visualBounds.min.y, visualBounds.center.z);
            visualBounds = GetLocalVisualBounds(root.transform, display);

            RemoveExistingColliders(root);
            CreateFootprintCollider(root, targetSize, visualBounds, IsLevelZeroPrefab(assetPath));
            UpdateProjectileOrigin(root, visualBounds);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void RemoveExistingColliders(GameObject root)
    {
        for (int childIndex = root.transform.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = root.transform.GetChild(childIndex);
            if (child.name.StartsWith(AutoBoxColliderFromMesh.AutoBoxNamePrefix, StringComparison.Ordinal)
                || string.Equals(child.name, BuildingFootprint.ColliderObjectName, StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        Collider[] remaining = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < remaining.Length; i++)
            UnityEngine.Object.DestroyImmediate(remaining[i]);
    }

    private static void CreateFootprintCollider(
        GameObject root,
        float footprintSize,
        Bounds visualBounds,
        bool isLevelZero)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
            throw new InvalidOperationException("Project is missing required layer 'Ground'.");

        var colliderObject = new GameObject(BuildingFootprint.ColliderObjectName);
        colliderObject.layer = groundLayer;
        colliderObject.transform.SetParent(root.transform, false);
        BoxCollider collider = colliderObject.AddComponent<BoxCollider>();
        collider.isTrigger = isLevelZero;
        collider.center = new Vector3(0f, visualBounds.center.y, 0f);
        collider.size = new Vector3(
            footprintSize,
            Mathf.Max(visualBounds.size.y, 0.01f),
            footprintSize);
    }

    private static void UpdateProjectileOrigin(GameObject root, Bounds visualBounds)
    {
        EntityPresentationBindings bindings = root.GetComponent<EntityPresentationBindings>();
        if (bindings == null || bindings.ProjectileOrigin == null)
            throw new InvalidOperationException($"Building prefab has no ProjectileOrigin binding. prefab={root.name}.");
        bindings.ProjectileOrigin.localPosition = visualBounds.center;
        EditorUtility.SetDirty(bindings);
    }

    private static void ValidatePrefab(string assetPath, BuilType buildingType)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            Transform display = root.transform.Find(EntityPresentationBindings.DisplayObjectName);
            if (display == null)
                throw new InvalidOperationException($"Building prefab has no Display root. path={assetPath}.");

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != 1 || colliders[0] is not BoxCollider box)
                throw new InvalidOperationException($"Building prefab must contain exactly one BoxCollider. path={assetPath}, count={colliders.Length}.");

            float expectedSize = BuildingFootprint.ResolveWorldSize(buildingType);
            if (!Approximately(box.size.x, expectedSize)
                || !Approximately(box.size.z, expectedSize)
                || !Approximately(box.center.x, 0f)
                || !Approximately(box.center.z, 0f))
            {
                throw new InvalidOperationException(
                    $"Building footprint collider mismatch. path={assetPath}, expected={expectedSize}, size={box.size}, center={box.center}.");
            }
            if (box.isTrigger != IsLevelZeroPrefab(assetPath))
                throw new InvalidOperationException($"Building level-zero trigger state mismatch. path={assetPath}.");

            Bounds visualBounds = GetLocalVisualBounds(root.transform, display);
            float visualDiameter = Mathf.Max(visualBounds.size.x, visualBounds.size.z);
            if (!Approximately(visualBounds.min.y, 0f))
            {
                throw new InvalidOperationException(
                    $"Building Display is not grounded. path={assetPath}, minY={visualBounds.min.y}.");
            }
            if (!Approximately(visualDiameter, expectedSize))
            {
                throw new InvalidOperationException(
                    $"Building Display diameter mismatch. path={assetPath}, expected={expectedSize}, actual={visualDiameter}.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Bounds GetLocalVisualBounds(Transform root, Transform display)
    {
        bool initialized = false;
        Bounds result = default;
        MeshFilter[] meshFilters = display.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            if (meshFilters[i].sharedMesh != null)
                Encapsulate(meshFilters[i].sharedMesh.bounds, meshFilters[i].transform, root, ref result, ref initialized);
        }

        SkinnedMeshRenderer[] skinnedRenderers = display.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            if (skinnedRenderers[i].sharedMesh != null)
                Encapsulate(skinnedRenderers[i].sharedMesh.bounds, skinnedRenderers[i].transform, root, ref result, ref initialized);
        }

        if (!initialized)
            throw new InvalidOperationException($"Building Display has no mesh bounds. prefab={root.name}.");
        return result;
    }

    private static void Encapsulate(
        Bounds localBounds,
        Transform meshTransform,
        Transform root,
        ref Bounds result,
        ref bool initialized)
    {
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;
        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            Vector3 corner = new Vector3(
                (cornerIndex & 1) == 0 ? min.x : max.x,
                (cornerIndex & 2) == 0 ? min.y : max.y,
                (cornerIndex & 4) == 0 ? min.z : max.z);
            Vector3 localPoint = root.InverseTransformPoint(meshTransform.TransformPoint(corner));
            if (!initialized)
            {
                result = new Bounds(localPoint, Vector3.zero);
                initialized = true;
            }
            else
            {
                result.Encapsulate(localPoint);
            }
        }
    }

    private static bool IsLevelZeroPrefab(string assetPath)
    {
        return assetPath.EndsWith("_Lv0.prefab", StringComparison.Ordinal);
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) <= 0.0001f;
    }
}
#endif
