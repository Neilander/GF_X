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
    private const string DefaultSlotConfigPath = "Assets/AAAGame/Resources/TechTestSlotConfig.asset";
    private const int MaxSlotCount = 3;

    private GlobalBuffManager m_TargetManager;
    private TextAsset m_BuildingTableAsset;
    private TechTestSlotConfig m_SlotConfig;

    private Vector2 m_ScrollPosition;
    private bool m_StatusBoardFoldout;

    // TechId -> 归属 Lv1 建筑 Identifier（不带 _Lv1 后缀，就是表里原始 Identifier）
    private readonly Dictionary<string, string> m_TechToBuilding = new(StringComparer.Ordinal);
    // 所有 Lv1 建筑 Identifier（表里原始 Identifier）
    private readonly List<string> m_AllBuildingIds = new();
    // 建筑 Identifier -> 它覆盖的 TechId 列表
    private readonly Dictionary<string, List<string>> m_BuildingToTechs = new(StringComparer.Ordinal);

    // 当前用户勾选的建筑（最多 MaxSlotCount 个）
    private readonly List<string> m_SelectedBuildings = new();

    [MenuItem("Tools/Tech Effect Binding Tool")]
    private static void Open()
    {
        var window = GetWindow<TechEffectBindingToolWindow>("Tech Effect Binding");
        window.minSize = new Vector2(780f, 520f);
        window.TryAutoAssignDefaults();
    }

    private void OnEnable()
    {
        TryAutoAssignDefaults();
        RebuildIndex();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tech Effect 绑定 & 测试", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_TargetManager = (GlobalBuffManager)EditorGUILayout.ObjectField("GlobalBuffManager", m_TargetManager, typeof(GlobalBuffManager), true);
            m_BuildingTableAsset = (TextAsset)EditorGUILayout.ObjectField("BuildingTable Txt", m_BuildingTableAsset, typeof(TextAsset), false);
            m_SlotConfig = (TechTestSlotConfig)EditorGUILayout.ObjectField("TechTestSlotConfig", m_SlotConfig, typeof(TechTestSlotConfig), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("自动查找"))
                {
                    TryAutoAssignDefaults();
                    RebuildIndex();
                }

                using (new EditorGUI.DisabledScope(m_TargetManager == null || m_BuildingTableAsset == null))
                {
                    if (GUILayout.Button("一键扫描并同步 Binding"))
                    {
                        SyncBindings();
                        RebuildIndex();
                    }
                }

                using (new EditorGUI.DisabledScope(m_TargetManager == null))
                {
                    if (GUILayout.Button("自动绑定 Effect 资产"))
                    {
                        AutoBindEffectAssets();
                    }
                }
            }
        }

        if (m_TargetManager == null)
        {
            EditorGUILayout.HelpBox("请先指定场景中的 GlobalBuffManager。", MessageType.Warning);
            return;
        }

        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
        DrawStatusBoard();
        EditorGUILayout.Space(10);
        DrawTestSetup();
        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 顶部：Tech 状态板（默认折叠）
    // ─────────────────────────────────────────────────────────────────────
    private void DrawStatusBoard()
    {
        var bindings = m_TargetManager.TechEffectBindings;
        int tested = 0, untested = 0, unbound = 0;
        if (bindings != null)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b == null) continue;
                if (b.Effect == null) unbound++;
                else if (b.Tested) tested++;
                else untested++;
            }
        }

        string header = $"Tech 状态板  总览: ✅ {tested} / ⬜ {untested} / ❌ {unbound}  (共 {bindings?.Count ?? 0})";
        m_StatusBoardFoldout = EditorGUILayout.Foldout(m_StatusBoardFoldout, header, true);
        if (!m_StatusBoardFoldout)
            return;

        if (bindings == null || bindings.Count == 0)
        {
            EditorGUILayout.HelpBox("Binding 列表为空，先点「一键扫描并同步 Binding」。", MessageType.Info);
            return;
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null) continue;
                DrawBindingRow(binding);
            }
        }
    }

    private void DrawBindingRow(TechEffectBinding binding)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            string statusIcon = GetStatusIcon(binding);
            GUILayout.Label(statusIcon, GUILayout.Width(24f));

            EditorGUILayout.SelectableLabel(binding.TechId ?? string.Empty, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(260f));

            EditorGUI.BeginChangeCheck();
            var effect = (TechEffectSO)EditorGUILayout.ObjectField(binding.Effect, typeof(TechEffectSO), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m_TargetManager, "Change Tech Effect Binding");
                binding.Effect = effect;
                MarkManagerDirty();
            }

            using (new EditorGUI.DisabledScope(binding.Effect == null))
            {
                if (GUILayout.Button(binding.Tested ? "✅ 已测试" : "⬜ 未测试", GUILayout.Width(90f)))
                {
                    Undo.RecordObject(m_TargetManager, "Toggle Tech Tested");
                    binding.Tested = !binding.Tested;
                    MarkManagerDirty();
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 下半：测试设置（选建筑 + 覆盖面 + 应用）
    // ─────────────────────────────────────────────────────────────────────
    private void DrawTestSetup()
    {
        EditorGUILayout.LabelField("测试设置", EditorStyles.boldLabel);

        if (m_SlotConfig == null)
        {
            EditorGUILayout.HelpBox("TechTestSlotConfig 未分配。请在上方指定或自动查找。", MessageType.Warning);
            return;
        }

        if (m_AllBuildingIds.Count == 0)
        {
            EditorGUILayout.HelpBox("建筑索引为空。请先确保 BuildingTable 已分配并点击「自动查找」。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField($"勾选最多 {MaxSlotCount} 个建筑（按勾选顺序对应 Slot 0 / 1 / 2）：");

        using (new EditorGUILayout.VerticalScope("box"))
        {
            for (int i = 0; i < m_AllBuildingIds.Count; i++)
            {
                string buildingId = m_AllBuildingIds[i];
                int selectedIndex = m_SelectedBuildings.IndexOf(buildingId);
                bool wasSelected = selectedIndex >= 0;

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool nowSelected = EditorGUILayout.ToggleLeft(buildingId, wasSelected, GUILayout.Width(260f));
                    if (nowSelected != wasSelected)
                    {
                        if (nowSelected)
                        {
                            if (m_SelectedBuildings.Count >= MaxSlotCount)
                            {
                                EditorUtility.DisplayDialog("Tech 测试", $"最多只能选择 {MaxSlotCount} 个建筑。", "OK");
                            }
                            else
                            {
                                m_SelectedBuildings.Add(buildingId);
                            }
                        }
                        else
                        {
                            m_SelectedBuildings.RemoveAt(selectedIndex);
                        }
                    }

                    string slotLabel = wasSelected ? $"Slot {selectedIndex}" : "";
                    GUILayout.Label(slotLabel, GUILayout.Width(60f));

                    GUILayout.Label(BuildCoverageSummary(buildingId));
                }
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("覆盖 tech（选中建筑的并集）：", EditorStyles.boldLabel);
        DrawCoverageUnion();

        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(m_SelectedBuildings.Count == 0))
            {
                if (GUILayout.Button("应用到 TechTestSlotConfig", GUILayout.Height(28f)))
                {
                    ApplyToSlotConfig();
                }
            }

            if (GUILayout.Button("清空槽位", GUILayout.Height(28f), GUILayout.Width(120f)))
            {
                ClearSlotConfig();
            }
        }
    }

    private void DrawCoverageUnion()
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bid in m_SelectedBuildings)
        {
            if (!m_BuildingToTechs.TryGetValue(bid, out var list)) continue;
            foreach (var tid in list) union.Add(tid);
        }

        if (union.Count == 0)
        {
            EditorGUILayout.HelpBox("未选择建筑或选中建筑无 tech。", MessageType.None);
            return;
        }

        var bindings = m_TargetManager.TechEffectBindings;
        var bindingByTech = new Dictionary<string, TechEffectBinding>(StringComparer.Ordinal);
        if (bindings != null)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] != null && !string.IsNullOrWhiteSpace(bindings[i].TechId))
                    bindingByTech[bindings[i].TechId] = bindings[i];
            }
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            foreach (var tid in union)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bindingByTech.TryGetValue(tid, out var b);
                    GUILayout.Label(GetStatusIcon(b), GUILayout.Width(24f));
                    EditorGUILayout.SelectableLabel(tid, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                    using (new EditorGUI.DisabledScope(b == null || b.Effect == null))
                    {
                        if (GUILayout.Button(b != null && b.Tested ? "✅" : "⬜", GUILayout.Width(32f)))
                        {
                            Undo.RecordObject(m_TargetManager, "Toggle Tech Tested");
                            b.Tested = !b.Tested;
                            MarkManagerDirty();
                        }
                    }
                }
            }
        }
    }

    private string BuildCoverageSummary(string buildingId)
    {
        if (!m_BuildingToTechs.TryGetValue(buildingId, out var list) || list == null || list.Count == 0)
            return "0 tech";

        var bindings = m_TargetManager.TechEffectBindings;
        int tested = 0, untested = 0, unbound = 0;
        if (bindings != null)
        {
            var set = new HashSet<string>(list, StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b == null || !set.Contains(b.TechId)) continue;
                if (b.Effect == null) unbound++;
                else if (b.Tested) tested++;
                else untested++;
            }
        }
        int total = list.Count;
        return $"{total} tech (✅ {tested} / ⬜ {untested} / ❌ {unbound})";
    }

    private void ApplyToSlotConfig()
    {
        if (m_SlotConfig == null) return;
        Undo.RecordObject(m_SlotConfig, "Apply Test Slot Config");
        if (m_SlotConfig.SlotBuildingIds == null || m_SlotConfig.SlotBuildingIds.Length != MaxSlotCount)
        {
            m_SlotConfig.SlotBuildingIds = new string[MaxSlotCount];
        }

        for (int i = 0; i < MaxSlotCount; i++)
        {
            m_SlotConfig.SlotBuildingIds[i] = i < m_SelectedBuildings.Count ? ResolveLv1Identifier(m_SelectedBuildings[i]) : null;
        }

        EditorUtility.SetDirty(m_SlotConfig);
        AssetDatabase.SaveAssetIfDirty(m_SlotConfig);
        EditorUtility.DisplayDialog("Tech 测试", "已写入 TechTestSlotConfig。进入 Play 模式即可看到 3 个测试建筑。", "OK");
    }

    private void ClearSlotConfig()
    {
        if (m_SlotConfig == null) return;
        Undo.RecordObject(m_SlotConfig, "Clear Test Slot Config");
        m_SlotConfig.SlotBuildingIds = new string[MaxSlotCount];
        m_SelectedBuildings.Clear();
        EditorUtility.SetDirty(m_SlotConfig);
        AssetDatabase.SaveAssetIfDirty(m_SlotConfig);
    }

    // 表里原始 Identifier（如 Buil_OtakuDesk）加 "_Lv1" 后缀；底座（以 Lv0 结尾）直接返回。
    private static string ResolveLv1Identifier(string rawIdentifier)
    {
        if (string.IsNullOrWhiteSpace(rawIdentifier)) return rawIdentifier;
        if (rawIdentifier.EndsWith("Lv0", StringComparison.Ordinal)) return rawIdentifier;
        return rawIdentifier + "_Lv1";
    }

    private static string GetStatusIcon(TechEffectBinding binding)
    {
        if (binding == null) return "❓";
        if (binding.Effect == null) return "❌";
        return binding.Tested ? "✅" : "⬜";
    }

    // ─────────────────────────────────────────────────────────────────────
    // 索引 / 同步逻辑
    // ─────────────────────────────────────────────────────────────────────
    private void RebuildIndex()
    {
        m_TechToBuilding.Clear();
        m_AllBuildingIds.Clear();
        m_BuildingToTechs.Clear();

        if (m_BuildingTableAsset == null) return;

        using (var reader = new StringReader(m_BuildingTableAsset.text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;

                var row = new BuildingTable();
                if (!row.ParseDataRow(line, null)) continue;

                // 只收正式建筑（排除 Lv0 底座）
                if (string.IsNullOrWhiteSpace(row.Identifier)) continue;
                if (row.Identifier.EndsWith("Lv0", StringComparison.Ordinal)) continue;

                m_AllBuildingIds.Add(row.Identifier);
                var techs = new List<string>();
                TryAdd(row.Tech1ID, techs);
                TryAdd(row.Tech2ID, techs);
                TryAdd(row.Tech3ID, techs);
                TryAdd(row.Tech4ID, techs);
                m_BuildingToTechs[row.Identifier] = techs;

                for (int i = 0; i < techs.Count; i++)
                {
                    m_TechToBuilding[techs[i]] = row.Identifier;
                }
            }
        }

        // 清理选中里不存在的
        m_SelectedBuildings.RemoveAll(b => !m_BuildingToTechs.ContainsKey(b));
    }

    private static void TryAdd(string techId, List<string> list)
    {
        if (!string.IsNullOrWhiteSpace(techId)) list.Add(techId);
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

        var existingById = new Dictionary<string, TechEffectBinding>(StringComparer.Ordinal);
        var bindings = m_TargetManager.TechEffectBindings;
        if (bindings != null)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.TechId) || existingById.ContainsKey(binding.TechId))
                    continue;

                existingById.Add(binding.TechId, binding);
            }
        }

        bindings.Clear();
        for (int i = 0; i < techIds.Count; i++)
        {
            string techId = techIds[i];
            TechEffectSO effect = null;
            bool tested = false;
            if (existingById.TryGetValue(techId, out var old) && old != null)
            {
                effect = old.Effect;
                tested = old.Tested;
            }
            bindings.Add(new TechEffectBinding
            {
                TechId = techId,
                Effect = effect,
                Tested = tested,
            });
        }

        MarkManagerDirty();
        EditorUtility.DisplayDialog("Tech Effect Binding", $"同步完成，共 {bindings.Count} 条 TechEffect 绑定。", "OK");
    }

    /// <summary>
    /// 扫描项目中所有 TechEffectSO 资产（.asset 文件名 == TechId），
    /// 按 TechId 匹配到 bindings；对 Effect 为空的条目自动填入匹配到的 SO。
    /// 不会覆盖已有 Effect。
    /// </summary>
    private void AutoBindEffectAssets()
    {
        var bindings = m_TargetManager.TechEffectBindings;
        if (bindings == null || bindings.Count == 0)
        {
            EditorUtility.DisplayDialog("自动绑定", "Binding 列表为空，请先点「一键扫描并同步 Binding」。", "OK");
            return;
        }

        // 扫所有 TechEffectSO 资产，按文件名建索引
        var byName = new Dictionary<string, TechEffectSO>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets("t:TechEffectSO");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<TechEffectSO>(path);
            if (so == null) continue;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            if (!byName.ContainsKey(fileName))
                byName.Add(fileName, so);
        }

        Undo.RecordObject(m_TargetManager, "Auto Bind Tech Effects");

        int filled = 0, already = 0, missing = 0, overwritten = 0;
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (b == null || string.IsNullOrWhiteSpace(b.TechId)) continue;

            if (byName.TryGetValue(b.TechId, out var so))
            {
                if (b.Effect == null)
                {
                    b.Effect = so;
                    filled++;
                }
                else if (b.Effect != so)
                {
                    // 已有 Effect 但类型/实例不同 —— 不覆盖，仅计数提示
                    overwritten++;
                }
                else
                {
                    already++;
                }
            }
            else
            {
                missing++;
            }
        }

        MarkManagerDirty();

        string msg = $"扫描到 {byName.Count} 个 TechEffectSO 资产。\n\n" +
                     $"✅ 新绑定: {filled}\n" +
                     $"• 已绑定且一致: {already}\n" +
                     $"⚠ 已有不同 Effect（未覆盖）: {overwritten}\n" +
                     $"❌ 找不到同名资产: {missing}";
        EditorUtility.DisplayDialog("自动绑定 Effect 资产", msg, "OK");
    }

    private void TryAutoAssignDefaults()
    {
        if (m_TargetManager == null)
            m_TargetManager = FindFirstObjectByType<GlobalBuffManager>();

        if (m_BuildingTableAsset == null)
            m_BuildingTableAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultBuildingTablePath);

        if (m_SlotConfig == null)
            m_SlotConfig = AssetDatabase.LoadAssetAtPath<TechTestSlotConfig>(DefaultSlotConfigPath);
    }

    private static List<string> CollectDistinctTechIds(TextAsset buildingTableAsset)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (buildingTableAsset == null) return result;

        using (var reader = new StringReader(buildingTableAsset.text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;

                var row = new BuildingTable();
                if (!row.ParseDataRow(line, null)) continue;

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
        if (string.IsNullOrWhiteSpace(techId) || !seen.Add(techId)) return;
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
}
#endif
