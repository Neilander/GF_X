using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class UnitTypeEditorWindow : EditorWindow
{
    private const string EnumFilePath = "Assets/AAAGame/Scripts/Definition/UnitType.cs";
    private const string WeaponOverrideConfigPath = "Assets/AAAGame/Resources/UnitWeaponSOOverrideConfig.asset";
    private const string CharacterDataTablePath = "Assets/AAAGame/DataTable/CharacterDataDetail.txt";
    private const string CardDataDirectory = "Assets/Resources/CardData";
    private const int MaxGeneratedCardLevel = 3;

    private List<string> _entries = new List<string>();
    private ReorderableList _reorderableList;
    private Vector2 _scrollPos;
    private Vector2 _weaponOverrideScrollPos;
    private UnitWeaponSOOverrideConfig _weaponOverrideConfig;
    private string _syncStatus = string.Empty;
    private bool _showWeaponOverrides;

    private sealed class CharacterTableEntry
    {
        public int Id;
        public string CharacterKey;
        public bool IsHero;
    }

    [MenuItem("Tools/Unit Type Editor")]
    public static void Open()
    {
        var window = GetWindow<UnitTypeEditorWindow>("Unit Type Editor");
        window.minSize = new Vector2(360, 440);
    }

    private void OnEnable()
    {
        EnsureWeaponOverrideConfig();
        LoadEnum();
        BuildList();
        SyncWeaponOverrideEntries();
    }

    private void LoadEnum()
    {
        _entries.Clear();
        string fullPath = Path.Combine(Application.dataPath, "..", EnumFilePath);
        if (!File.Exists(fullPath)) return;

        string content = File.ReadAllText(fullPath);
        var match = Regex.Match(content, @"enum\s+UnitType\s*\{([^}]*)\}", RegexOptions.Singleline);
        if (!match.Success) return;

        string body = match.Groups[1].Value;
        var memberMatches = Regex.Matches(body, @"(\w+)\s*=");
        foreach (Match m in memberMatches)
        {
            _entries.Add(m.Groups[1].Value);
        }
    }

    private void BuildList()
    {
        _reorderableList = new ReorderableList(_entries, typeof(string), false, true, false, false);

        _reorderableList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "UnitType（由 CharacterDataDetail 同步）");
        };

        _reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            float indexW = 44f;
            float y = rect.y + 2f;
            float h = rect.height - 4f;

            EditorGUI.LabelField(new Rect(rect.x, y, indexW, h), index.ToString());
            EditorGUI.LabelField(new Rect(rect.x + indexW + 4f, y, rect.width - indexW - 4f, h), _entries[index]);
        };
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Unit Type Editor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "从 CharacterDataDetail 生成 UnitType、同步武器覆盖项，并重建精简 CardData。CardData 只保留单位、等级和可选 sprite，其余读单位表/建筑运行时数据。",
            MessageType.Info);

        if (GUILayout.Button("Sync From CharacterDataDetail", GUILayout.Height(32)))
        {
            SyncFromCharacterDataTable();
        }

        if (!string.IsNullOrWhiteSpace(_syncStatus))
        {
            EditorGUILayout.HelpBox(_syncStatus, MessageType.Info);
        }

        EditorGUILayout.Space(6);
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        _reorderableList.DoLayoutList();
        EditorGUILayout.EndScrollView();

        DrawWeaponSOOverrideSection();

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Save Current Enum And Weapon Overrides", GUILayout.Height(28)))
        {
            SaveEnum(true);
            SaveWeaponOverrideConfig();
        }

        EditorGUILayout.Space(4);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("Enum", EnumFilePath);
        EditorGUILayout.TextField("Character Table", CharacterDataTablePath);
        EditorGUILayout.TextField("CardData Output", CardDataDirectory);
        EditorGUILayout.TextField("Weapon Override", WeaponOverrideConfigPath);
        EditorGUI.EndDisabledGroup();
    }

    private void DrawWeaponSOOverrideSection()
    {
        if (_weaponOverrideConfig == null)
            return;

        EditorGUILayout.Space(6);
        int count = _weaponOverrideConfig.Entries != null ? _weaponOverrideConfig.Entries.Count : 0;
        _showWeaponOverrides = EditorGUILayout.Foldout(_showWeaponOverrides, $"Weapon SO Override ({count})", true);
        if (!_showWeaponOverrides)
            return;

        EditorGUILayout.HelpBox("按 CharacterKey 指定专用 Weapon SO。留空走默认武器。", MessageType.None);

        bool changed = false;
        _weaponOverrideScrollPos = EditorGUILayout.BeginScrollView(_weaponOverrideScrollPos, GUILayout.MaxHeight(180f));
        for (int i = 0; i < _entries.Count; i++)
        {
            string characterKey = _entries[i];
            UnitWeaponSOOverrideEntry entry = GetOrCreateOverrideEntry(characterKey);
            BaseWeaponSO current = string.IsNullOrWhiteSpace(entry.weaponSOPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<BaseWeaponSO>(entry.weaponSOPath);

            BaseWeaponSO next = (BaseWeaponSO)EditorGUILayout.ObjectField(characterKey, current, typeof(BaseWeaponSO), false);
            if (next == current)
                continue;

            entry.weaponSOPath = next == null ? string.Empty : AssetDatabase.GetAssetPath(next);
            changed = true;
        }
        EditorGUILayout.EndScrollView();

        if (changed)
        {
            _weaponOverrideConfig.MarkDirty();
            EditorUtility.SetDirty(_weaponOverrideConfig);
        }
    }

    private void SyncFromCharacterDataTable()
    {
        List<CharacterTableEntry> characterEntries = ReadCharacterTableEntries();
        if (characterEntries.Count == 0)
        {
            _syncStatus = $"未从 {CharacterDataTablePath} 读取到单位。请先刷新数据表。";
            return;
        }

        _entries = new List<string>(characterEntries.Count);
        foreach (var entry in characterEntries)
        {
            _entries.Add(entry.CharacterKey);
        }

        var spritesByCardKey = CollectExistingCardSprites();
        BuildList();
        SaveEnum(false);
        SyncWeaponOverrideEntries();
        SaveWeaponOverrideConfig();
        int cardCount = RebuildCardDataAssets(characterEntries, spritesByCardKey);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        _syncStatus = $"同步完成：UnitType={_entries.Count}，CardData={cardCount}。若新增了枚举名，等待 Unity 编译完成后即可使用。";
    }

    private void SaveEnum(bool refreshAssetDatabase)
    {
        var sb = new StringBuilder();
        sb.AppendLine("public enum UnitType");
        sb.AppendLine("{");
        for (int i = 0; i < _entries.Count; i++)
        {
            sb.AppendLine($"    {_entries[i]} = {i},");
        }
        sb.AppendLine("}");

        string fullPath = Path.Combine(Application.dataPath, "..", EnumFilePath);
        string dir = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, sb.ToString());
        if (refreshAssetDatabase)
        {
            AssetDatabase.Refresh();
        }
    }

    private void EnsureWeaponOverrideConfig()
    {
        _weaponOverrideConfig = AssetDatabase.LoadAssetAtPath<UnitWeaponSOOverrideConfig>(WeaponOverrideConfigPath);
        if (_weaponOverrideConfig != null)
            return;

        string dir = Path.GetDirectoryName(WeaponOverrideConfigPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _weaponOverrideConfig = ScriptableObject.CreateInstance<UnitWeaponSOOverrideConfig>();
        AssetDatabase.CreateAsset(_weaponOverrideConfig, WeaponOverrideConfigPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void SyncWeaponOverrideEntries()
    {
        if (_weaponOverrideConfig == null)
            return;

        bool changed = false;
        var entries = _weaponOverrideConfig.Entries;

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i] == null || !_entries.Contains(entries[i].characterKey))
            {
                entries.RemoveAt(i);
                changed = true;
            }
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            if (FindEntry(_entries[i]) != null)
                continue;

            entries.Add(new UnitWeaponSOOverrideEntry
            {
                characterKey = _entries[i],
                weaponSOPath = string.Empty
            });
            changed = true;
        }

        if (!changed)
            return;

        _weaponOverrideConfig.MarkDirty();
        EditorUtility.SetDirty(_weaponOverrideConfig);
        AssetDatabase.SaveAssets();
    }

    private UnitWeaponSOOverrideEntry GetOrCreateOverrideEntry(string characterKey)
    {
        UnitWeaponSOOverrideEntry entry = FindEntry(characterKey);
        if (entry != null)
            return entry;

        entry = new UnitWeaponSOOverrideEntry
        {
            characterKey = characterKey,
            weaponSOPath = string.Empty
        };
        _weaponOverrideConfig.Entries.Add(entry);
        _weaponOverrideConfig.MarkDirty();
        EditorUtility.SetDirty(_weaponOverrideConfig);
        return entry;
    }

    private UnitWeaponSOOverrideEntry FindEntry(string characterKey)
    {
        var entries = _weaponOverrideConfig.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry != null && entry.characterKey == characterKey)
                return entry;
        }

        return null;
    }

    private void SaveWeaponOverrideConfig()
    {
        if (_weaponOverrideConfig == null)
            return;

        _weaponOverrideConfig.MarkDirty();
        EditorUtility.SetDirty(_weaponOverrideConfig);
        AssetDatabase.SaveAssets();
    }

    private static List<CharacterTableEntry> ReadCharacterTableEntries()
    {
        var result = new List<CharacterTableEntry>();
        if (!File.Exists(CharacterDataTablePath))
        {
            Debug.LogWarning($"Character data table not found: {CharacterDataTablePath}");
            return result;
        }

        string[] lines = File.ReadAllLines(CharacterDataTablePath, Encoding.UTF8);
        Dictionary<string, int> columns = null;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] cells = line.Split('\t');
            if (cells.Length == 0)
                continue;

            if (cells[0] == "#" && cells.Length > 2 && cells[1] == "ID")
            {
                columns = BuildColumnMap(cells);
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal) || columns == null)
                continue;

            string characterKey = GetCell(cells, columns, "CharacterKey");
            if (!IsValidIdentifier(characterKey))
                continue;

            result.Add(new CharacterTableEntry
            {
                Id = ParseInt(GetCell(cells, columns, "ID"), result.Count),
                CharacterKey = characterKey,
                IsHero = GetCell(cells, columns, "UnitTags").Contains("UnitTag.Hero")
            });
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    private static Dictionary<string, int> BuildColumnMap(string[] headerCells)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < headerCells.Length; i++)
        {
            string name = headerCells[i];
            if (!string.IsNullOrWhiteSpace(name) && !result.ContainsKey(name))
            {
                result[name] = i;
            }
        }

        return result;
    }

    private static string GetCell(string[] cells, Dictionary<string, int> columns, string columnName)
    {
        if (columns == null || !columns.TryGetValue(columnName, out int index))
            return string.Empty;

        return index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out int result) ? result : fallback;
    }

    private static int RebuildCardDataAssets(List<CharacterTableEntry> characterEntries, Dictionary<string, Sprite> spritesByCardKey)
    {
        EnsureDirectory(CardDataDirectory);

        spritesByCardKey ??= new Dictionary<string, Sprite>(StringComparer.Ordinal);
        DeleteExistingCardDataAssets();

        int cardCount = 0;
        for (int i = 0; i < characterEntries.Count; i++)
        {
            var entry = characterEntries[i];
            if (entry == null || entry.IsHero)
                continue;

            for (int level = 1; level <= MaxGeneratedCardLevel; level++)
            {
                string assetPath = $"{CardDataDirectory}/{entry.CharacterKey}_Lv{level}.asset";
                CardData cardData = CreateInstance<CardData>();
                cardData.Configure(i, level);
                AssetDatabase.CreateAsset(cardData, assetPath);

                if (spritesByCardKey.TryGetValue(BuildCardKey(entry.CharacterKey, level), out Sprite sprite) && sprite != null)
                {
                    var serializedObject = new SerializedObject(cardData);
                    serializedObject.FindProperty("m_CardSprite").objectReferenceValue = sprite;
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorUtility.SetDirty(cardData);
                cardCount++;
            }
        }

        return cardCount;
    }

    private static Dictionary<string, Sprite> CollectExistingCardSprites()
    {
        var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        if (!Directory.Exists(CardDataDirectory))
            return result;

        string[] guids = AssetDatabase.FindAssets("t:CardData", new[] { CardDataDirectory });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CardData cardData = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (cardData != null && cardData.CardSprite != null)
            {
                string key = BuildCardKey(cardData.SoldierIndex.ToString(), cardData.RequiredLv);
                if (!result.ContainsKey(key))
                {
                    result[key] = cardData.CardSprite;
                }

                continue;
            }

            if (!TryCollectLegacyCardSprite(path, out string legacyKey, out Sprite legacySprite))
                continue;

            if (!result.ContainsKey(legacyKey))
            {
                result[legacyKey] = legacySprite;
            }
        }

        return result;
    }

    private static bool TryCollectLegacyCardSprite(string assetPath, out string cardKey, out Sprite sprite)
    {
        cardKey = string.Empty;
        sprite = null;

        if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
            return false;

        string content = File.ReadAllText(assetPath, Encoding.UTF8);
        if (!TryReadIntYamlField(content, "soldierIndex", out int unitTypeValue)
            || !TryReadIntYamlField(content, "requiredLv", out int requiredLevel))
        {
            return false;
        }

        string unitTypeName = Enum.GetName(typeof(UnitType), unitTypeValue);
        if (string.IsNullOrWhiteSpace(unitTypeName))
            return false;

        if (!TryReadSpriteYamlField(content, "cardSprite", out sprite)
            && !TryReadSpriteYamlField(content, "m_CardSprite", out sprite))
        {
            return false;
        }

        cardKey = BuildCardKey(unitTypeName, requiredLevel);
        return true;
    }

    private static bool TryReadIntYamlField(string content, string fieldName, out int value)
    {
        value = 0;
        var match = Regex.Match(content, $@"^\s*{Regex.Escape(fieldName)}:\s*(-?\d+)\s*$", RegexOptions.Multiline);
        return match.Success && int.TryParse(match.Groups[1].Value, out value);
    }

    private static bool TryReadSpriteYamlField(string content, string fieldName, out Sprite sprite)
    {
        sprite = null;
        var match = Regex.Match(
            content,
            $@"^\s*{Regex.Escape(fieldName)}:\s*\{{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]+),\s*type:\s*\d+\}}\s*$",
            RegexOptions.Multiline);

        if (!match.Success || match.Groups[1].Value == "0")
            return false;

        string spritePath = AssetDatabase.GUIDToAssetPath(match.Groups[2].Value);
        if (string.IsNullOrWhiteSpace(spritePath))
            return false;

        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        return sprite != null;
    }

    private static void DeleteExistingCardDataAssets()
    {
        if (!Directory.Exists(CardDataDirectory))
            return;

        string[] guids = AssetDatabase.FindAssets("t:CardData", new[] { CardDataDirectory });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.DeleteAsset(path);
        }
    }

    private static string BuildCardKey(string characterKey, int level)
    {
        return $"{characterKey}_Lv{level}";
    }

    private static void EnsureDirectory(string assetDirectory)
    {
        if (Directory.Exists(assetDirectory))
            return;

        string[] parts = assetDirectory.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");
    }
}
