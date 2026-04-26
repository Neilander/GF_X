using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class UnitTypeEditorWindow : EditorWindow
{
    private const string EnumFilePath = "Assets/AAAGame/Scripts/Definition/UnitType.cs";
    private const string WeaponOverrideConfigPath = "Assets/AAAGame/Resources/UnitWeaponSOOverrideConfig.asset";

    private List<string> _entries = new List<string>();
    private ReorderableList _reorderableList;
    private string _newEntryName = "";
    private Vector2 _scrollPos;
    private UnitWeaponSOOverrideConfig _weaponOverrideConfig;

    [MenuItem("Tools/Unit Type Editor")]
    public static void Open()
    {
        var window = GetWindow<UnitTypeEditorWindow>("Unit Type Editor");
        window.minSize = new Vector2(300, 400);
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
        _reorderableList = new ReorderableList(_entries, typeof(string), true, true, false, false);

        _reorderableList.drawHeaderCallback = rect =>
        {
            float indexW = 40f;
            float deleteW = 20f;
            float nameW = rect.width - indexW - deleteW - 8f;

            EditorGUI.LabelField(new Rect(rect.x, rect.y, indexW, rect.height), "Index");
            EditorGUI.LabelField(new Rect(rect.x + indexW + 4f, rect.y, nameW, rect.height), "Name");
        };

        _reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            float indexW = 40f;
            float deleteW = 20f;
            float nameW = rect.width - indexW - deleteW - 8f;
            float y = rect.y + 2f;
            float h = rect.height - 4f;

            EditorGUI.LabelField(new Rect(rect.x, y, indexW, h), index.ToString());

            string newName = EditorGUI.TextField(new Rect(rect.x + indexW + 4f, y, nameW, h), _entries[index]);
            if (newName != _entries[index])
            {
                if (IsValidIdentifier(newName))
                {
                    _entries[index] = newName;
                }
            }

            if (GUI.Button(new Rect(rect.x + indexW + nameW + 8f, y, deleteW, h), "x"))
            {
                _entries.RemoveAt(index);
                GUIUtility.ExitGUI();
            }
        };

        _reorderableList.onReorderCallback = list =>
        {
        };
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Unit Type Editor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("像编辑 Layer 一样编辑 UnitType 枚举。拖拽排序，名称即 index。", MessageType.Info);
        EditorGUILayout.Space(4);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        _reorderableList.DoLayoutList();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        _newEntryName = EditorGUILayout.TextField("New Type", _newEntryName);
        GUI.enabled = !string.IsNullOrWhiteSpace(_newEntryName) && IsValidIdentifier(_newEntryName) && !_entries.Contains(_newEntryName);
        if (GUILayout.Button("Add", GUILayout.Width(60)))
        {
            _entries.Add(_newEntryName);
            _newEntryName = "";
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        DrawWeaponSOOverrideSection();

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Save", GUILayout.Height(30)))
        {
            SaveEnum();
            SaveWeaponOverrideConfig();
        }

        EditorGUILayout.Space(4);

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("File", EnumFilePath);
        EditorGUILayout.TextField("Weapon Override Config", WeaponOverrideConfigPath);
        EditorGUI.EndDisabledGroup();
    }

    private void DrawWeaponSOOverrideSection()
    {
        if (_weaponOverrideConfig == null)
        {
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Weapon SO Override", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("可按 UnitType 指定专用 Weapon SO。留空则走默认逻辑。", MessageType.None);

        bool changed = false;
        for (int i = 0; i < _entries.Count; i++)
        {
            string characterKey = _entries[i];
            UnitWeaponSOOverrideEntry entry = GetOrCreateOverrideEntry(characterKey);
            BaseWeaponSO current = string.IsNullOrWhiteSpace(entry.weaponSOPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<BaseWeaponSO>(entry.weaponSOPath);

            BaseWeaponSO next = (BaseWeaponSO)EditorGUILayout.ObjectField(characterKey, current, typeof(BaseWeaponSO), false);
            if (next == current)
            {
                continue;
            }

            entry.weaponSOPath = next == null ? string.Empty : AssetDatabase.GetAssetPath(next);
            changed = true;
        }

        if (changed)
        {
            _weaponOverrideConfig.MarkDirty();
            EditorUtility.SetDirty(_weaponOverrideConfig);
        }
    }

    private void SaveEnum()
    {
        var sb = new System.Text.StringBuilder();
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
        AssetDatabase.Refresh();
    }

    private void EnsureWeaponOverrideConfig()
    {
        _weaponOverrideConfig = AssetDatabase.LoadAssetAtPath<UnitWeaponSOOverrideConfig>(WeaponOverrideConfigPath);
        if (_weaponOverrideConfig != null)
        {
            return;
        }

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
        {
            return;
        }

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
            {
                continue;
            }

            entries.Add(new UnitWeaponSOOverrideEntry
            {
                characterKey = _entries[i],
                weaponSOPath = string.Empty
            });
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        _weaponOverrideConfig.MarkDirty();
        EditorUtility.SetDirty(_weaponOverrideConfig);
        AssetDatabase.SaveAssets();
    }

    private UnitWeaponSOOverrideEntry GetOrCreateOverrideEntry(string characterKey)
    {
        UnitWeaponSOOverrideEntry entry = FindEntry(characterKey);
        if (entry != null)
        {
            return entry;
        }

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
            if (entry == null)
            {
                continue;
            }

            if (entry.characterKey == characterKey)
            {
                return entry;
            }
        }

        return null;
    }

    private void SaveWeaponOverrideConfig()
    {
        if (_weaponOverrideConfig == null)
        {
            return;
        }

        _weaponOverrideConfig.MarkDirty();
        EditorUtility.SetDirty(_weaponOverrideConfig);
        AssetDatabase.SaveAssets();
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");
    }
}
