#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class BuildingLogicObstacleShapeBaker
{
    private const string PrefabFolder = "Assets/AAAGame/Prefabs/Entity/Building";
    private const string RuntimePrefabPrefix = "Assets/AAAGame/Prefabs/Entity/";
    private const string OutputPath = "Assets/AAAGame/Resources/BuildingLogicObstacleShapeCatalogData.json";

    [MenuItem("Tools/AAAGame/Bake Building Logic Obstacle Shapes")]
    public static void Bake()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        var entries = new List<BuildingLogicObstacleShapeCatalog.PrefabEntry>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            entries.Add(BakePrefab(assetPath));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.PrefabPath, right.PrefabPath));
        string json = JsonConvert.SerializeObject(entries, Formatting.Indented)
            .Replace("\r\n", "\n")
            .Replace("\n", "\r\n");
        File.WriteAllText(OutputPath, json + "\r\n", new UTF8Encoding(false));
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[BuildingLogicObstacleShapeBaker] Baked {entries.Count} prefabs to {OutputPath}.");
    }

    private static BuildingLogicObstacleShapeCatalog.PrefabEntry BakePrefab(string assetPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            var boxes = new List<BuildingLogicObstacleShapeCatalog.BoxEntry>();
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                if (!(collider is BoxCollider))
                {
                    throw new InvalidOperationException(
                        $"Building logic obstacle baker only accepts BoxCollider. prefab={assetPath}, collider={collider.name}, type={collider.GetType().Name}.");
                }

                Bounds bounds = collider.bounds;
                Vector3 min = root.transform.InverseTransformPoint(bounds.min);
                Vector3 max = root.transform.InverseTransformPoint(bounds.max);
                Vector3 center = (min + max) * 0.5f;
                Vector3 half = Vector3.Max(max - min, min - max) * 0.5f;
                boxes.Add(new BuildingLogicObstacleShapeCatalog.BoxEntry
                {
                    CenterXRaw = ((Fix64)center.x).RawValue,
                    CenterZRaw = ((Fix64)center.z).RawValue,
                    HalfExtentXRaw = ((Fix64)half.x).RawValue,
                    HalfExtentZRaw = ((Fix64)half.z).RawValue,
                });
            }

            boxes.Sort(CompareBoxes);
            string runtimePath = assetPath.Substring(
                RuntimePrefabPrefix.Length,
                assetPath.Length - RuntimePrefabPrefix.Length - ".prefab".Length);
            return new BuildingLogicObstacleShapeCatalog.PrefabEntry
            {
                PrefabPath = runtimePath,
                Boxes = boxes,
            };
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int CompareBoxes(
        BuildingLogicObstacleShapeCatalog.BoxEntry left,
        BuildingLogicObstacleShapeCatalog.BoxEntry right)
    {
        int result = left.CenterXRaw.CompareTo(right.CenterXRaw);
        if (result != 0)
            return result;
        result = left.CenterZRaw.CompareTo(right.CenterZRaw);
        if (result != 0)
            return result;
        result = left.HalfExtentXRaw.CompareTo(right.HalfExtentXRaw);
        return result != 0 ? result : left.HalfExtentZRaw.CompareTo(right.HalfExtentZRaw);
    }
}
#endif
