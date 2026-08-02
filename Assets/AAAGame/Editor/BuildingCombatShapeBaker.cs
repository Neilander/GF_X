#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class BuildingCombatShapeBaker
{
    private const string PrefabFolder = "Assets/AAAGame/Prefabs/Entity/Building";
    private const string RuntimePrefabPrefix = "Assets/AAAGame/Prefabs/Entity/";
    private const string OutputPath = "Assets/AAAGame/Resources/BuildingCombatShapeCatalogData.json";

    [MenuItem("Tools/AAAGame/Bake Building Combat Shapes")]
    public static void Bake()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        var entries = new List<BuildingCombatShapeCatalog.Entry>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            entries.Add(BakePrefab(assetPath));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.PrefabPath, right.PrefabPath));
        var builder = new StringBuilder(entries.Count * 160);
        builder.Append("[\r\n");
        for (int i = 0; i < entries.Count; i++)
        {
            builder.Append("  ");
            builder.Append(JsonConvert.SerializeObject(entries[i], Formatting.None));
            builder.Append(i + 1 < entries.Count ? ",\r\n" : "\r\n");
        }
        builder.Append("]\r\n");

        File.WriteAllText(OutputPath, builder.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[BuildingCombatShapeBaker] Baked {entries.Count} prefabs to {OutputPath}.");
    }

    private static BuildingCombatShapeCatalog.Entry BakePrefab(string assetPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            bool hasBounds = false;
            double minX = 0d;
            double minZ = 0d;
            double maxX = 0d;
            double maxZ = 0d;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;
                if (!(collider is BoxCollider))
                {
                    throw new InvalidOperationException(
                        $"Building combat shape baker only accepts BoxCollider. prefab={assetPath}, collider={collider.name}, type={collider.GetType().Name}.");
                }

                Bounds bounds = collider.bounds;
                Vector3 min = root.transform.InverseTransformPoint(bounds.min);
                Vector3 max = root.transform.InverseTransformPoint(bounds.max);
                if (!hasBounds)
                {
                    minX = Math.Min(min.x, max.x);
                    minZ = Math.Min(min.z, max.z);
                    maxX = Math.Max(min.x, max.x);
                    maxZ = Math.Max(min.z, max.z);
                    hasBounds = true;
                }
                else
                {
                    minX = Math.Min(minX, Math.Min(min.x, max.x));
                    minZ = Math.Min(minZ, Math.Min(min.z, max.z));
                    maxX = Math.Max(maxX, Math.Max(min.x, max.x));
                    maxZ = Math.Max(maxZ, Math.Max(min.z, max.z));
                }
            }

            double halfExtentX = (maxX - minX) * 0.5d;
            double halfExtentZ = (maxZ - minZ) * 0.5d;
            if (!hasBounds || halfExtentX <= 0d || halfExtentZ <= 0d)
                throw new InvalidOperationException($"Building combat shape baker found no valid BoxCollider bounds. prefab={assetPath}.");

            string runtimePath = assetPath.Substring(
                RuntimePrefabPrefix.Length,
                assetPath.Length - RuntimePrefabPrefix.Length - ".prefab".Length);
            return new BuildingCombatShapeCatalog.Entry
            {
                PrefabPath = runtimePath,
                CenterXRaw = ((Fix64)((minX + maxX) * 0.5d)).RawValue,
                CenterZRaw = ((Fix64)((minZ + maxZ) * 0.5d)).RawValue,
                HalfExtentXRaw = ((Fix64)halfExtentX).RawValue,
                HalfExtentZRaw = ((Fix64)halfExtentZ).RawValue,
            };
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
