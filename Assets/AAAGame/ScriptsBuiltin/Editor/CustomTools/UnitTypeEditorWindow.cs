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

    private List<string> _entries = new List<string>();
    private ReorderableList _reorderableList;
    private string _newEntryName = "";
    private Vector2 _scrollPos;

    [MenuItem("CustomTools/Unit Type Editor")]
    public static void Open()
    {
        var window = GetWindow<UnitTypeEditorWindow>("Unit Type Editor");
        window.minSize = new Vector2(300, 400);
    }

    private void OnEnable()
    {
        LoadEnum();
        BuildList();
    }

    private void LoadEnum()
    {
        _entries.Clear();
        string fullPath = Path.Combine(Application.dataPath, "..", EnumFilePath);
        if (!File.Exists(fullPath)) return;

        string content = File.ReadAllText(fullPath);

        // 匹配 enum 体内的成员名（忽略 = 后面的数字）
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

            // Index 标签
            EditorGUI.LabelField(new Rect(rect.x, y, indexW, h), index.ToString());

            // 名称编辑
            string newName = EditorGUI.TextField(new Rect(rect.x + indexW + 4f, y, nameW, h), _entries[index]);
            if (newName != _entries[index])
            {
                // 验证合法的 C# 标识符
                if (IsValidIdentifier(newName))
                {
                    _entries[index] = newName;
                }
            }

            // 删除按钮
            if (GUI.Button(new Rect(rect.x + indexW + nameW + 8f, y, deleteW, h), "x"))
            {
                _entries.RemoveAt(index);
                GUIUtility.ExitGUI();
            }
        };

        _reorderableList.onReorderCallback = list =>
        {
            // 拖拽排序后不需要额外操作，index 会在保存时自动重新分配
        };
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Unit Type Editor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("像编辑 Layer 一样编辑 UnitType 枚举。拖拽排序，名称即 index。", MessageType.Info);
        EditorGUILayout.Space(4);

        // 列表
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        _reorderableList.DoLayoutList();
        EditorGUILayout.EndScrollView();

        // 添加新条目
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

        // 保存按钮
        if (GUILayout.Button("Save", GUILayout.Height(30)))
        {
            SaveEnum();
        }

        EditorGUILayout.Space(4);

        // 显示文件路径
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("File", EnumFilePath);
        EditorGUI.EndDisabledGroup();
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

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");
    }
}
