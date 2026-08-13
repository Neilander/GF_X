using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class EntityPresetPoint : MonoBehaviour
{
    public const string InitialBaseIdentifier = "InitBase_Lv1";

    public Vector3 Position => transform.position;

    public static bool IsInitialBaseIdentifier(string identifier)
    {
        return string.Equals(identifier, InitialBaseIdentifier, StringComparison.Ordinal);
    }

    [Tooltip("预设点id，用于读表")]
    public string Identifier;

    public EntityPresetPointType PointType;
    public bool IsGameEndConditionBuilding;
    public int UnitSpawnCount; // 仅对 Unit 类型有效，表示在战斗阶段开始时以此预设点为中心生成多少个单位
    public int DefendSpawnWeight = 1; // 仅对 DefendSpawn 类型有效，表示该点在防御阶段的出怪权重
    [Tooltip("仅对 Destination 类型有效：关卡内唯一目标点 ID")]
    public int DestinationId;
    [Tooltip("仅对 Destination 类型有效：以游戏距离为单位的目标范围半径")]
    public float DestinationRadius;
    [Tooltip("仅对 Building 类型有效：勾选后使用该点位配置的橙髓初始存量；不勾选则使用 GameConfig.ResourcePointInitialAmount")]
    public bool UseCustomCoinReserves;
    [Tooltip("仅对 Building 类型有效：橙髓初始存量（需勾选 UseCustomCoinReserves）")]
    public int CustomCoinReserves;

    [Header("测试槽位（仅测试用；勾上后运行期 Identifier 从 TechTestSlotConfig 读）")]
    public bool IsTestSlot;
    public int TestSlotIndex; // 0 / 1 / 2

#if UNITY_EDITOR
    private void OnValidate()
    {
        EntityPresetPointEditorPreview.RequestSync();
    }
#endif

    public bool TryGetInitialCoinReserves(out int coinReserves)
    {
        coinReserves = Mathf.Max(0, CustomCoinReserves);
        return UseCustomCoinReserves;
    }
}

public enum EntityPresetPointType { Unit, Hero, Building, DefendSpawn, Destination, Teleportation } //Spawn, Respawn, Patrol, Device

#if UNITY_EDITOR
static class EntityPresetPointEditorPreview
{
    private const string PreviewRootName = "__EntityPresetPreview__";

    private static readonly Dictionary<string, string> BuildingPrefabPathByIdentifier = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> UnitPrefabPathByCharacterKey = new(StringComparer.Ordinal);
    private static string defaultHeroCharacterKey;
    private static bool buildingTableLoaded;
    private static bool characterTableLoaded;
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
        characterTableLoaded = false;
        RequestSync();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingPlayMode)
        {
            buildingTableLoaded = false;
            characterTableLoaded = false;
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
            EnsureCharacterCache();

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

    private static void EnsureCharacterCache()
    {
        if (characterTableLoaded)
        {
            return;
        }

        characterTableLoaded = true;
        UnitPrefabPathByCharacterKey.Clear();
        defaultHeroCharacterKey = null;

        if (TryLoadCharacterTable(GetCharacterTableAssetPath(false)))
        {
            return;
        }

        if (TryLoadCharacterTable(GetCharacterTableAssetPath(true)))
        {
            return;
        }

        Debug.LogWarning($"[EntityPresetPointEditorPreview] 未找到 CharacterDataDetail 数据表资源: {UtilityBuiltin.AssetsPath.GetDataTablePath("CharacterDataDetail", false)} / {UtilityBuiltin.AssetsPath.GetDataTablePath("CharacterDataDetail", true)}");
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

    private static string GetCharacterTableAssetPath(bool useBytes)
    {
        var defaultPath = UtilityBuiltin.AssetsPath.GetDataTablePath("CharacterDataDetail", useBytes);
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(defaultPath) != null)
        {
            return defaultPath;
        }

        string[] candidateFolders = { "Assets/AAAGame/DataTable" };
        string[] guids = AssetDatabase.FindAssets("CharacterDataDetail t:TextAsset", candidateFolders);
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

    private static bool TryLoadCharacterTable(string assetPath)
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
            LoadCharacterRowsFromBytes(textAsset.bytes);
        }
        else
        {
            LoadCharacterRowsFromText(textAsset.text);
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

    private static void LoadCharacterRowsFromText(string tableText)
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

                var row = new CharacterDataDetail();
                if (!row.ParseDataRow(line, null))
                {
                    Debug.LogWarning($"[EntityPresetPointEditorPreview] 无法解析 CharacterDataDetail 行: {line}");
                    continue;
                }

                RegisterCharacterRow(row);
            }
        }
    }

    private static void LoadCharacterRowsFromBytes(byte[] tableBytes)
    {
        using (var stream = new MemoryStream(tableBytes, false))
        using (var reader = new BinaryReader(stream))
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                int rowLength = Read7BitEncodedInt32(reader);
                int rowStart = (int)reader.BaseStream.Position;

                var row = new CharacterDataDetail();
                if (!row.ParseDataRow(tableBytes, rowStart, rowLength, null))
                {
                    Debug.LogWarning("[EntityPresetPointEditorPreview] 无法解析 CharacterDataDetail 二进制行。");
                    reader.BaseStream.Position += rowLength;
                    continue;
                }

                RegisterCharacterRow(row);
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

    private static void RegisterCharacterRow(CharacterDataDetail row)
    {
        if (row == null)
        {
            return;
        }

        AddCharacterPreviewPath(row.CharacterKey, row.PrefabPath);
        if (defaultHeroCharacterKey == null && row.UnitTags != null && Array.IndexOf(row.UnitTags, UnitTag.Hero) >= 0)
            defaultHeroCharacterKey = row.CharacterKey;
    }

    private static void AddBuildingPreviewPath(string identifier, string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(prefabPath))
        {
            return;
        }

        BuildingPrefabPathByIdentifier[identifier] = prefabPath;
    }

    private static void AddCharacterPreviewPath(string characterKey, string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(prefabPath))
        {
            return;
        }

        UnitPrefabPathByCharacterKey[characterKey] = prefabPath;
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
            case EntityPresetPointType.DefendSpawn:
                return TryGetUnitPrefabAssetPath(point.Identifier, out prefabAssetPath);

            case EntityPresetPointType.Destination:
            case EntityPresetPointType.Teleportation:
                return false;

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
        EnsureCharacterCache();

        string characterKey = string.IsNullOrWhiteSpace(identifier) ? string.Empty : identifier.Trim();
        if (UnitTypeHelper.TryParseUnitType(identifier, out var unitType))
        {
            if (unitType == UnitType.Unit_Hero)
            {
                if (string.IsNullOrWhiteSpace(defaultHeroCharacterKey))
                    throw new InvalidOperationException("CharacterDataDetail has no hero row for level preview.");
                characterKey = defaultHeroCharacterKey;
            }
            else
            {
                characterKey = unitType.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(characterKey) || !UnitPrefabPathByCharacterKey.TryGetValue(characterKey, out var prefabPath))
        {
            return false;
        }

        prefabAssetPath = UtilityBuiltin.AssetsPath.GetEntityPath(prefabPath);
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
