#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class TechEffectBindingToolWindow : EditorWindow
{
    private const string DefaultBuildingTablePath = "Assets/AAAGame/DataTable/Build/BuildingTable.txt";

    private GlobalBuffManager m_TargetManager;
    private TextAsset m_BuildingTableAsset;
    private Vector2 m_ScrollPosition;

    [MenuItem("Tools/Tech Effect Binding Tool")]
    private static void Open()
    {
        var window = GetWindow<TechEffectBindingToolWindow>("Tech Effect Binding");
        window.minSize = new Vector2(720f, 360f);
        window.TryAutoAssignDefaults();
    }

    private void OnEnable()
    {
        TryAutoAssignDefaults();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tech Effect 绑定工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("扫描编译后的 BuildingTable.txt，去重得到 techId 列表，再与 GlobalBuffManager 上现有绑定同步。已有 tech 的 Effect 会保留，新 tech 会补进来，旧 tech 会移除。", MessageType.Info);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_TargetManager = (GlobalBuffManager)EditorGUILayout.ObjectField("GlobalBuffManager", m_TargetManager, typeof(GlobalBuffManager), true);
            m_BuildingTableAsset = (TextAsset)EditorGUILayout.ObjectField("BuildingTable Txt", m_BuildingTableAsset, typeof(TextAsset), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("自动查找"))
                {
                    TryAutoAssignDefaults();
                }

                using (new EditorGUI.DisabledScope(m_TargetManager == null || m_BuildingTableAsset == null))
                {
                    if (GUILayout.Button("一键扫描并同步"))
                    {
                        SyncBindings();
                    }
                }
            }
        }

        if (m_TargetManager == null)
        {
            EditorGUILayout.HelpBox("请先指定场景中的 GlobalBuffManager。", MessageType.Warning);
            return;
        }

        DrawBindingList();
    }

    private void DrawBindingList()
    {
        var bindings = m_TargetManager.TechEffectBindings;
        if (bindings == null)
        {
            EditorGUILayout.HelpBox("当前绑定列表为空。", MessageType.None);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"当前绑定数: {bindings.Count}", EditorStyles.boldLabel);

        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (binding == null)
                continue;

            EditorGUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(binding.TechId ?? string.Empty, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(260f));

                EditorGUI.BeginChangeCheck();
                var effect = (TechEffectSO)EditorGUILayout.ObjectField(binding.Effect, typeof(TechEffectSO), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m_TargetManager, "Change Tech Effect Binding");
                    binding.Effect = effect;
                    MarkManagerDirty();
                }
            }

            DrawSeparator();
            EditorGUILayout.Space(2f);
        }

        EditorGUILayout.EndScrollView();
    }

    private void SyncBindings()
    {
        List<string> techIds = CollectDistinctTechIds(m_BuildingTableAsset);
        if (techIds.Count == 0)
        {
            EditorUtility.DisplayDialog("Tech Effect Binding", "没有从 BuildingTable 中扫到任何 TechId。", "OK");
            return;
        }

        Undo.RecordObject(m_TargetManager, "Sync Tech Effect Bindings");

        var existingById = new Dictionary<string, TechEffectSO>(StringComparer.Ordinal);
        var bindings = m_TargetManager.TechEffectBindings;
        if (bindings != null)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.TechId) || existingById.ContainsKey(binding.TechId))
                    continue;

                existingById.Add(binding.TechId, binding.Effect);
            }
        }

        bindings.Clear();
        for (int i = 0; i < techIds.Count; i++)
        {
            string techId = techIds[i];
            existingById.TryGetValue(techId, out var effect);
            bindings.Add(new TechEffectBinding
            {
                TechId = techId,
                Effect = effect
            });
        }

        MarkManagerDirty();
        EditorUtility.DisplayDialog("Tech Effect Binding", $"同步完成，共 {bindings.Count} 条 TechEffect 绑定。", "OK");
    }

    private void TryAutoAssignDefaults()
    {
        if (m_TargetManager == null)
        {
            m_TargetManager = FindFirstObjectByType<GlobalBuffManager>();
        }

        if (m_BuildingTableAsset == null)
        {
            m_BuildingTableAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultBuildingTablePath);
        }
    }

    private static List<string> CollectDistinctTechIds(TextAsset buildingTableAsset)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (buildingTableAsset == null)
            return result;

        using (var reader = new StringReader(buildingTableAsset.text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                    continue;

                var row = new BuildingTable();
                if (!row.ParseDataRow(line, null))
                    continue;

                AddTechId(row.Tech1ID, seen, result);
                AddTechId(row.Tech2ID, seen, result);
                AddTechId(row.Tech3ID, seen, result);
                AddTechId(row.Tech4ID, seen, result);
            }
        }

        return result;
    }

    private static void AddTechId(string techId, HashSet<string> seen, List<string> result)
    {
        if (string.IsNullOrWhiteSpace(techId) || !seen.Add(techId))
            return;

        result.Add(techId);
    }

    private void MarkManagerDirty()
    {
        m_TargetManager.MarkTechEffectBindingsDirty();
        EditorUtility.SetDirty(m_TargetManager);
        if (m_TargetManager.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(m_TargetManager.gameObject.scene);
        }
    }

    private static void DrawSeparator()
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(rect, new Color(0.28f, 0.28f, 0.28f, 1f));
    }
}
#endif
