using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class EntityPresetPoint : MonoBehaviour
{
    public Vector3 Position => transform.position;

    [Tooltip("预设点id，用于读表")]
    public string Identifier;

    public EntityPresetPointType PointType;
    public bool IsGameEndConditionBuilding;
    public int UnitSpawnCount; // 仅对 Unit 类型有效，表示在战斗阶段开始时以此预设点为中心生成多少个单位

    [Header("测试槽位（仅测试用；勾上后运行期 Identifier 从 TechTestSlotConfig 读）")]
    public bool IsTestSlot;
    public int TestSlotIndex; // 0 / 1 / 2

#if UNITY_EDITOR
    private void OnValidate()
    {
        EntityPresetPointEditorPreview.RequestSync();
    }
#endif
}

public enum EntityPresetPointType { Unit, Hero, Building } //Spawn, Respawn, Patrol, Device

#if UNITY_EDITOR
static class EntityPresetPointEditorPreview
{
    private const string PreviewRootName = "__EntityPresetPreview__";

    private static readonly Dictionary<string, string> BuildingPrefabPathByIdentifier = new(StringComparer.Ordinal);
    private static bool buildingTableLoaded;
    private static bool syncQueued;
    private static bool isSyncing;

    static EntityPresetPointEditorPreview()
    {
        EditorApplication.delayCall += RequestSync;
        EditorApplication.hierarchyChanged += RequestSync;
        EditorApplication.projectChanged += OnProjectChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void RequestSync()
    {
        if (Application.isPlaying || syncQueued)
        {
            return;
        }

        syncQueued = true;
        EditorApplication.delayCall += SyncAll;
    }

    private static void OnProjectChanged()
    {
        buildingTableLoaded = false;
        RequestSync();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingPlayMode)
        {
            buildingTableLoaded = false;
            RequestSync();
        }
    }

    private static void SyncAll()
    {
        syncQueued = false;

        if (Application.isPlaying || isSyncing)
        {
            return;
        }

        isSyncing = true;
        try
        {
            EnsureBuildingCache();

            var points = UnityEngine.Object.FindObjectsOfType<EntityPresetPoint>();
            foreach (var point in points)
            {
                if (point == null)
                {
                    continue;
                }

                SyncPoint(point);
            }
        }
        finally
        {
            isSyncing = false;
        }
    }

    private static void EnsureBuildingCache()
    {
        if (buildingTableLoaded)
        {
            return;
        }

        buildingTableLoaded = true;
        BuildingPrefabPathByIdentifier.Clear();

        if (TryLoadBuildingTable(GetBuildingTableAssetPath(false)))
        {
            return;
        }

        if (TryLoadBuildingTable(GetBuildingTableAssetPath(true)))
        {
            return;
        }

        Debug.LogWarning($"[EntityPresetPointEditorPreview] 未找到 BuildingTable 数据表资源: {UtilityBuiltin.AssetsPath.GetDataTablePath("BuildingTable", false)} / {UtilityBuiltin.AssetsPath.GetDataTablePath("BuildingTable", true)}");
    }

    private static string GetBuildingTableAssetPath(bool useBytes)
    {
        var defaultPath = UtilityBuiltin.AssetsPath.GetDataTablePath("BuildingTable", useBytes);
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(defaultPath) != null)
        {
            return defaultPath;
        }

        string[] candidateFolders = { "Assets/AAAGame/DataTable" };
        string[] guids = AssetDatabase.FindAssets("BuildingTable t:TextAsset", candidateFolders);
        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                continue;
            }

            if (useBytes)
            {
                if (assetPath.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                {
                    return assetPath;
                }
            }
            else if (assetPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return assetPath;
            }
        }

        return defaultPath;
    }

    private static bool TryLoadBuildingTable(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
        if (textAsset == null)
        {
            return false;
        }

        if (assetPath.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
        {
            LoadBuildingRowsFromBytes(textAsset.bytes);
        }
        else
        {
            LoadBuildingRowsFromText(textAsset.text);
        }

        return true;
    }

    private static void LoadBuildingRowsFromText(string tableText)
    {
        using (var reader = new StringReader(tableText))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                {
                    continue;
                }

                var row = new BuildingTable();
                if (!row.ParseDataRow(line, null))
                {
                    Debug.LogWarning($"[EntityPresetPointEditorPreview] 无法解析 BuildingTable 行: {line}");
                    continue;
                }

                RegisterBuildingRow(row);
            }
        }
    }

    private static void LoadBuildingRowsFromBytes(byte[] tableBytes)
    {
        using (var stream = new MemoryStream(tableBytes, false))
        using (var reader = new BinaryReader(stream))
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                int rowLength = Read7BitEncodedInt32(reader);
                int rowStart = (int)reader.BaseStream.Position;

                var row = new BuildingTable();
                if (!row.ParseDataRow(tableBytes, rowStart, rowLength, null))
                {
                    Debug.LogWarning("[EntityPresetPointEditorPreview] 无法解析 BuildingTable 二进制行。");
                    reader.BaseStream.Position += rowLength;
                    continue;
                }

                RegisterBuildingRow(row);
                reader.BaseStream.Position += rowLength;
            }
        }
    }

    private static int Read7BitEncodedInt32(BinaryReader reader)
    {
        int count = 0;
        int shift = 0;
        byte byteValue;

        do
        {
            if (shift == 5 * 7)
            {
                throw new FormatException("7-bit encoded int32 is too large.");
            }

            byteValue = reader.ReadByte();
            count |= (byteValue & 0x7F) << shift;
            shift += 7;
        }
        while ((byteValue & 0x80) != 0);

        return count;
    }

    private static void RegisterBuildingRow(BuildingTable row)
    {
        if (row == null || string.IsNullOrEmpty(row.Identifier))
        {
            return;
        }

        if (row.Identifier.EndsWith("Lv0", StringComparison.Ordinal))
        {
            AddBuildingPreviewPath(row.Identifier, row.Lv1PrefabPath);
            return;
        }

        int maxLv = row.Type == BuilType.Tech ? 1 : 3;
        if (maxLv >= 1)
        {
            AddBuildingPreviewPath(row.Identifier + "_Lv1", row.Lv1PrefabPath);
        }

        if (maxLv >= 2)
        {
            AddBuildingPreviewPath(row.Identifier + "_Lv2", row.Lv2PrefabPath);
        }

        if (maxLv >= 3)
        {
            AddBuildingPreviewPath(row.Identifier + "_Lv3", row.Lv3PrefabPath);
        }
    }

    private static void AddBuildingPreviewPath(string identifier, string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(prefabPath))
        {
            return;
        }

        BuildingPrefabPathByIdentifier[identifier] = prefabPath;
    }

    private static void SyncPoint(EntityPresetPoint point)
    {
        if (!TryGetPointPrefabAssetPath(point, out var prefabAssetPath))
        {
            RemovePreview(point.transform);
            return;
        }

        var previewRoot = FindPreviewRoot(point.transform);
        if (previewRoot != null)
        {
            var marker = previewRoot.GetComponent<EntityPresetPointPreviewMarker>();
            if (marker != null && marker.SourceIdentifier == point.Identifier && marker.SourcePrefabAssetPath == prefabAssetPath)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(previewRoot.gameObject);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[EntityPresetPointEditorPreview] 无法加载预览 prefab: {prefabAssetPath}");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, point.transform);
        if (instance == null)
        {
            Debug.LogWarning($"[EntityPresetPointEditorPreview] 无法实例化预览 prefab: {prefabAssetPath}");
            return;
        }

        instance.name = PreviewRootName;
        SetPreviewFlagsRecursive(instance);

        var newMarker = instance.AddComponent<EntityPresetPointPreviewMarker>();
        newMarker.SourceIdentifier = point.Identifier;
        newMarker.SourcePrefabAssetPath = prefabAssetPath;
        newMarker.PointType = point.PointType;

        DisableMonoBehaviours(instance);
    }

    private static bool TryGetPointPrefabAssetPath(EntityPresetPoint point, out string prefabAssetPath)
    {
        prefabAssetPath = null;
        if (point == null)
        {
            return false;
        }

        switch (point.PointType)
        {
            case EntityPresetPointType.Building:
                return TryGetBuildingPrefabAssetPath(point.Identifier, out prefabAssetPath);

            case EntityPresetPointType.Unit:
            case EntityPresetPointType.Hero:
                return TryGetUnitPrefabAssetPath(point.Identifier, out prefabAssetPath);

            default:
                return false;
        }
    }

    private static bool TryGetBuildingPrefabAssetPath(string identifier, out string prefabAssetPath)
    {
        EnsureBuildingCache();

        if (string.IsNullOrWhiteSpace(identifier) || !BuildingPrefabPathByIdentifier.TryGetValue(identifier, out var prefabPath))
        {
            prefabAssetPath = null;
            return false;
        }

        prefabAssetPath = UtilityBuiltin.AssetsPath.GetEntityPath(prefabPath);
        return true;
    }

    private static bool TryGetUnitPrefabAssetPath(string identifier, out string prefabAssetPath)
    {
        prefabAssetPath = null;

        if (!UnitTypeHelper.TryParseUnitType(identifier, out var unitType))
        {
            return false;
        }

        var prefabName = UnitTypeHelper.GetSoldierPrefabName(unitType);
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        prefabAssetPath = UtilityBuiltin.AssetsPath.GetEntityPath(prefabName);
        return true;
    }

    private static Transform FindPreviewRoot(Transform parent)
    {
        if (parent == null)
        {
            return null;
        }

        return parent.Find(PreviewRootName);
    }

    private static void RemovePreview(Transform parent)
    {
        var previewRoot = FindPreviewRoot(parent);
        if (previewRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(previewRoot.gameObject);
        }
    }

    private static void SetPreviewFlagsRecursive(GameObject root)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (var transform in transforms)
        {
            transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    private static void DisableMonoBehaviours(GameObject root)
    {
        var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var behaviour in behaviours)
        {
            if (behaviour != null)
            {
                behaviour.enabled = false;
            }
        }
    }

    private sealed class EntityPresetPointPreviewMarker : MonoBehaviour
    {
        public string SourceIdentifier;
        public string SourcePrefabAssetPath;
        public EntityPresetPointType PointType;
    }
}
#endif