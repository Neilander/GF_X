#if UNITY_EDITOR
using GameFramework.Editor.DataTableTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 科技树面板生成器（读表生成 UI 节点与初始布局）。
/// - 读取 TechNodeTable.txt
/// - 在指定 Root 下为每个 TechId 生成/更新一个子物体（带 TechNodeView）
/// - 布局：纵向自下而上（Level=1 在底部），按 TechCategory 分 5 列
/// - 支持保留现有 anchoredPosition（手动微调）；可勾选强制重排
/// - 连线部分：预留，通过扫描 prereq 构建边对象（占位脚本）
/// </summary>
public class TechLayoutGeneratorWindow : EditorWindow
{
    [Serializable]
    private class NodeRow
    {
        public string TechId;
        public int Level;
        public TechCategory Category;
        public string SpriteName;
        public string[] Prereqs;
        public string NameKey;
        public string DescKey;
    }

    private string m_NodeTablePath = "Assets/AAAGame/DataTable/Tech/TechNodeTable.txt";
    private RectTransform m_Root;
    private GameObject m_NodeTemplate;

    // 本次生成计算得到的区域布局（非序列化，仅用于一次生成过程）
    private Dictionary<TechCategory, float> m_RegionLeftByCategory;
    private Dictionary<TechCategory, float> m_RegionWidthByCategory;

    [Header("Layout")]
    private Vector2 m_Origin = Vector2.zero;
    // 说明：Level 的“大行高”会根据该 Level 内需要的子行数量动态计算；这里的参数是 Level 与 Level 之间的额外间隔。
    private float m_LevelGap = 0f;
    private float m_SubRowHeight = 400f;
    // 说明：用于同一大列内“竖向小列(subColumn)”的横向间距（所有 Level 对齐到同一批竖线，不再做行内均分）。
    private float m_SubColumnSpacing = 400f;
    private float m_IconSize = 250f;
    private bool m_RepositionExisting = false;

    [Header("Background")]
    private bool m_GenerateCategoryBackgrounds = true;
    private Color m_ExploreBg = new Color(0.2f, 0.6f, 1.0f, 0.12f);
    private Color m_FightBg = new Color(1.0f, 0.35f, 0.35f, 0.12f);
    private Color m_CraftBg = new Color(0.7f, 0.55f, 0.2f, 0.12f);
    private Color m_EfficiencyBg = new Color(0.35f, 1.0f, 0.55f, 0.12f);
    private Color m_ProfitBg = new Color(1f, 0.95f, 0.3f, 0.12f);

    public static void Open()
    {
        GetWindow<TechLayoutGeneratorWindow>("Tech Panel Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("表格与根节点", EditorStyles.boldLabel);
        m_NodeTablePath = EditorGUILayout.TextField("TechNodeTable", m_NodeTablePath);
        m_Root = (RectTransform)EditorGUILayout.ObjectField("Root RectTransform", m_Root, typeof(RectTransform), true);

        m_NodeTemplate = (GameObject)EditorGUILayout.ObjectField("Node Template (Prefab)", m_NodeTemplate, typeof(GameObject), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("纵向布局参数", EditorStyles.boldLabel);
        m_Origin = EditorGUILayout.Vector2Field("Origin (Level1 center)", m_Origin);
        m_LevelGap = EditorGUILayout.FloatField("LevelGap", m_LevelGap);
        m_SubRowHeight = EditorGUILayout.FloatField("SubRowHeight (same level)", m_SubRowHeight);
        m_SubColumnSpacing = EditorGUILayout.FloatField("SubColumnSpacing", m_SubColumnSpacing);
        m_IconSize = EditorGUILayout.FloatField("IconSize", m_IconSize);
        m_RepositionExisting = EditorGUILayout.ToggleLeft("Reposition Existing Nodes (override manual tweaks)", m_RepositionExisting);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("列背景（5类）", EditorStyles.boldLabel);
        m_GenerateCategoryBackgrounds = EditorGUILayout.ToggleLeft("Generate/Update Category Backgrounds", m_GenerateCategoryBackgrounds);
        using (new EditorGUI.DisabledScope(!m_GenerateCategoryBackgrounds))
        {
            m_ExploreBg = EditorGUILayout.ColorField("Explore", m_ExploreBg);
            m_FightBg = EditorGUILayout.ColorField("Fight", m_FightBg);
            m_CraftBg = EditorGUILayout.ColorField("Craft", m_CraftBg);
            m_EfficiencyBg = EditorGUILayout.ColorField("Efficiency", m_EfficiencyBg);
            m_ProfitBg = EditorGUILayout.ColorField("Profit", m_ProfitBg);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(m_Root == null))
        {
            if (GUILayout.Button("Generate/Update Nodes"))
            {
                try
                {
                    GenerateOrUpdateNodes();
                    EditorUtility.DisplayDialog("Tech Panel", "Nodes updated.", "OK");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    EditorUtility.DisplayDialog("Tech Panel", ex.Message, "OK");
                }
            }

            if (GUILayout.Button("Refresh Edges"))
            {
                try
                {
                    RefreshEdges();
                    EditorUtility.DisplayDialog("Tech Panel", "Edges refreshed.", "OK");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    EditorUtility.DisplayDialog("Tech Panel", ex.Message, "OK");
                }
            }
        }
    }

    private void GenerateOrUpdateNodes()
    {
        string nodeFullPath = ToFullPath(m_NodeTablePath);
        if (!File.Exists(nodeFullPath))
        {
            throw new FileNotFoundException("TechNodeTable not found", nodeFullPath);
        }

        var nodes = ReadNodes(nodeFullPath);

        if (nodes.Count <= 0) return;

        // 同一等级内：如果存在前置依赖也在同一级，则需要把后置节点摆在更高的子行
        var nodeById = new Dictionary<string, NodeRow>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null || string.IsNullOrWhiteSpace(n.TechId)) continue;
            if (!nodeById.ContainsKey(n.TechId)) nodeById.Add(n.TechId, n);
        }

        var subRowById = ComputeSameLevelSubRows(nodes, nodeById, out int maxSubRow);

        // 每个 Level 的最大子行数，用于动态计算大行高
        var maxSubRowByLevel = new Dictionary<int, int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null) continue;
            int sr = subRowById.TryGetValue(n.TechId, out var v) ? v : 0;
            if (!maxSubRowByLevel.TryGetValue(n.Level, out var cur) || sr > cur)
            {
                maxSubRowByLevel[n.Level] = sr;
            }
        }

        int maxLevelInTable = nodes.Max(n => n.Level);
        maxLevelInTable = Mathf.Max(1, maxLevelInTable);

        // 动态计算每个 Level 的基准 Y（Level=1 在底部，向上递增）
        var baseYByLevel = ComputeLevelBaseY(maxLevelInTable, maxSubRowByLevel);

        // 计算内容纵向范围，用于背景高度
        float minY = baseYByLevel[1];
        float maxY = baseYByLevel[maxLevelInTable] + Mathf.Max(0, maxSubRowByLevel.TryGetValue(maxLevelInTable, out var lastMaxSr) ? lastMaxSr : 0) * Mathf.Max(0f, m_SubRowHeight);
        float halfIcon = Mathf.Max(1f, m_IconSize) * 0.5f;
        minY -= halfIcon;
        maxY += halfIcon;

        // 为同分类依赖链分配“竖向小列(subColumn)”：优先与同类型前置科技对齐（即使等级不同）。
        var subColumnById = ComputeSubColumns(nodes, nodeById, subRowById);

        // 区域总宽度由区域小列总数决定；全部小列之间间隔宽度固定。
        ComputeRegionLayout(nodes, subColumnById, out m_RegionLeftByCategory, out m_RegionWidthByCategory);

        if (m_GenerateCategoryBackgrounds)
        {
            EnsureCategoryBackgrounds(m_Root, minY, maxY);
        }

        if (m_NodeTemplate == null)
        {
            throw new InvalidOperationException("Tech Panel Generator: 必须指定 Node Template (Prefab)。当前仅保留 prefab 工作流。");
        }

        Dictionary<string, TechNodeView> existing = FindExistingNodes(m_Root);

        // 按 Level/SubRow/Category 排序，保证前置更可能先落位
        var ordered = nodes
            .OrderBy(n => n.Level)
            .ThenBy(n => subRowById.TryGetValue(n.TechId, out var sr) ? sr : 0)
            .ThenBy(n => (int)n.Category)
            .ThenBy(n => n.TechId, StringComparer.Ordinal)
            .ToList();

        for (int index = 0; index < ordered.Count; index++)
        {
            var n = ordered[index];
            if (n == null || string.IsNullOrWhiteSpace(n.TechId)) continue;

            int level = n.Level;
            var category = n.Category;
            int subRow = subRowById.TryGetValue(n.TechId, out var sr) ? sr : 0;
            int subCol = subColumnById.TryGetValue(n.TechId, out var sc) ? sc : 0;

            float rowY = baseYByLevel[level] + Mathf.Max(0f, m_SubRowHeight) * Mathf.Max(0, subRow);
            float x = GetSubColumnX(category, subCol);
            Vector2 desiredPos = new Vector2(x, rowY);

            if (!existing.TryGetValue(n.TechId, out var node) || node == null)
            {
                GameObject go;
                go = (GameObject)PrefabUtility.InstantiatePrefab(m_NodeTemplate);
                if (go == null)
                    go = Instantiate(m_NodeTemplate);
                go.name = n.TechId;
                go.transform.SetParent(m_Root, false);

                node = go.GetComponent<TechNodeView>();
                if (node == null) node = go.AddComponent<TechNodeView>();
                node.techId = n.TechId;

                var rt = node.transform as RectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                ApplyIconSize(rt);

                var img = FindIconImage(go.transform);
                TryApplySprite(img, n.SpriteName);
                ApplyNameKey(go.transform, n.NameKey);

                rt.anchoredPosition = desiredPos;
                existing[n.TechId] = node;
            }
            else
            {
                // 确保节点上有 TechNodeView 且写入 techId/rect（仅保留 prefab 工作流下需要的数据）
                node.techId = n.TechId;

                var rt = node.transform as RectTransform;
                ApplyIconSize(rt);

                var img = FindIconImage(node.transform);
                TryApplySprite(img, n.SpriteName);
                ApplyNameKey(node.transform, n.NameKey);

                if (m_RepositionExisting)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = desiredPos;
                }
            }
        }
    }

    private static void TryApplySprite(UnityEngine.UI.Image img, string spriteName)
    {
        if (img == null) return;
        if (string.IsNullOrWhiteSpace(spriteName)) return;

        string spritePath = UtilityBuiltin.AssetsPath.GetSpritesPath(spriteName);
        if (string.IsNullOrWhiteSpace(spritePath))
        {
            GF.LogWarning("Tech Panel Generator: SpritePath 为空, SpriteName=" + spriteName);
            return;
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite != null)
        {
            img.sprite = sprite;
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            GF.LogWarning("Tech Panel Generator: Sprite 加载失败(AssetDatabase): " + spritePath);
        }
    }

    private static Image FindIconImage(Transform node)
    {
        if (node == null) return null;

        var self = node.GetComponent<Image>();
        if (self != null) return self;

        var icon = node.Find("Icon");
        if (icon != null)
        {
            var img = icon.GetComponent<Image>();
            if (img != null) return img;
        }

        return node.GetComponentInChildren<Image>(true);
    }

    private static void ApplyNameKey(Transform node, string nameKey)
    {
        if (node == null) return;
        if (string.IsNullOrWhiteSpace(nameKey)) return;

        // 约定优先：子物体 Name 上的 UIStringKey
        var name = node.Find("Name");
        if (name != null && name.TryGetComponent<UnityGameFramework.Runtime.UIStringKey>(out var k1))
        {
            k1.Key = nameKey;
            return;
        }

        // 退化：节点下第一个 UIStringKey
        var all = node.GetComponentsInChildren<UnityGameFramework.Runtime.UIStringKey>(true);
        if (all != null && all.Length > 0)
        {
            all[0].Key = nameKey;
        }
    }

    private Dictionary<int, float> ComputeLevelBaseY(int maxLevel, Dictionary<int, int> maxSubRowByLevel)
    {
        var result = new Dictionary<int, float>();
        maxLevel = Mathf.Max(1, maxLevel);

        float levelGap = Mathf.Max(0f, m_LevelGap);
        float icon = Mathf.Max(1f, m_IconSize);
        float subH = Mathf.Max(0f, m_SubRowHeight);

        // Level1 在 Origin.y
        result[1] = m_Origin.y;

        for (int level = 2; level <= maxLevel; level++)
        {
            int prev = level - 1;
            int prevMaxSub = maxSubRowByLevel.TryGetValue(prev, out var v) ? v : 0;
            prevMaxSub = Mathf.Max(0, prevMaxSub);

            // 该 Level 的“有效高度”取决于子行数量：
            // - 子行 0..prevMaxSub（共 prevMaxSub+1 行），顶部比底部多 prevMaxSub*subH
            // - 再加 iconSize 确保节点不与下一等级重叠
            float prevSpan = icon + prevMaxSub * subH + levelGap;
            result[level] = result[prev] + prevSpan;
        }

        return result;
    }

    private void EnsureCategoryBackgrounds(RectTransform root, float minY, float maxY)
    {
        if (root == null) return;
        float totalHeight = Mathf.Max(1f, (maxY - minY));
        float centerY = (minY + maxY) * 0.5f;

        var bgRoot = root.Find("__Background") as RectTransform;
        if (bgRoot == null)
        {
            var go = new GameObject("__Background", typeof(RectTransform));
            go.transform.SetParent(root, false);
            bgRoot = go.GetComponent<RectTransform>();
            bgRoot.anchorMin = new Vector2(0.5f, 0.5f);
            bgRoot.anchorMax = new Vector2(0.5f, 0.5f);
            bgRoot.pivot = new Vector2(0.5f, 0.5f);
            bgRoot.anchoredPosition = Vector2.zero;
            bgRoot.sizeDelta = Vector2.zero;
            bgRoot.SetAsFirstSibling();
        }
        else
        {
            bgRoot.SetAsFirstSibling();
        }

        EnsureCategoryBg(bgRoot, TechCategory.Explore, "Explore", m_ExploreBg, centerY, totalHeight);
        EnsureCategoryBg(bgRoot, TechCategory.Fight, "Fight", m_FightBg, centerY, totalHeight);
        EnsureCategoryBg(bgRoot, TechCategory.Craft, "Craft", m_CraftBg, centerY, totalHeight);
        EnsureCategoryBg(bgRoot, TechCategory.Efficiency, "Efficiency", m_EfficiencyBg, centerY, totalHeight);
        EnsureCategoryBg(bgRoot, TechCategory.Profit, "Profit", m_ProfitBg, centerY, totalHeight);
    }

    private void EnsureCategoryBg(RectTransform bgRoot, TechCategory category, string name, Color color, float centerY, float height)
    {
        if (m_RegionLeftByCategory == null || m_RegionWidthByCategory == null) return;
        if (!m_RegionLeftByCategory.TryGetValue(category, out var left)) left = 0f;
        if (!m_RegionWidthByCategory.TryGetValue(category, out var width)) width = Mathf.Max(1f, m_SubColumnSpacing);
        width = Mathf.Max(1f, width);
        float centerX = left + width * 0.5f;
        EnsureOneCategoryBg(bgRoot, category, name, color, new Vector2(centerX, centerY), new Vector2(width, height));
    }

    private float GetSubColumnX(TechCategory category, int subColumn)
    {
        // 规则：
        // - 区域总宽度由区域小列总数决定
        // - 全部小列之间间隔宽度固定 = SubColumnSpacing
        // - 各区域“最边缘小列到背景边界距离”相同 ≈ SubColumnSpacing/2
        float spacing = Mathf.Max(1f, m_SubColumnSpacing);
        subColumn = Mathf.Max(0, subColumn);

        if (m_RegionLeftByCategory == null || !m_RegionLeftByCategory.TryGetValue(category, out var left)) return 0f;
        float edge = spacing * 0.5f;
        return left + edge + subColumn * spacing;
    }

    private void ComputeRegionLayout(List<NodeRow> nodes, Dictionary<string, int> subColumnById, out Dictionary<TechCategory, float> regionLeftByCategory, out Dictionary<TechCategory, float> regionWidthByCategory)
    {
        regionLeftByCategory = new Dictionary<TechCategory, float>();
        regionWidthByCategory = new Dictionary<TechCategory, float>();

        float spacing = Mathf.Max(1f, m_SubColumnSpacing);

        // 统计每个分类使用的小列数（maxSubCol + 1）
        int[] maxCol = new int[5] { -1, -1, -1, -1, -1 };
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null || string.IsNullOrWhiteSpace(n.TechId)) continue;
            if (!subColumnById.TryGetValue(n.TechId, out var col)) continue;
            int idx = Mathf.Clamp((int)n.Category, 0, 4);
            if (col > maxCol[idx]) maxCol[idx] = col;
        }

        float[] widths = new float[5];
        float total = 0f;
        for (int i = 0; i < 5; i++)
        {
            int count = Mathf.Max(1, maxCol[i] + 1);

            // 背景边距=spacing/2，且列距=spacing，因此区域宽度=count*spacing，能保证：
            // - 第 0 列在 left+spacing/2
            // - 最后一列在 right-spacing/2
            widths[i] = Mathf.Max(1f, count * spacing);
            total += widths[i];
        }

        // 5 个区域整体以 Origin.x 为中心排列；区域之间无缝贴合
        float leftCursor = m_Origin.x - total * 0.5f;
        for (int i = 0; i < 5; i++)
        {
            var cat = (TechCategory)i;
            regionLeftByCategory[cat] = leftCursor;
            regionWidthByCategory[cat] = widths[i];
            leftCursor += widths[i];
        }
    }

    private static Dictionary<string, int> ComputeSubColumns(List<NodeRow> nodes, Dictionary<string, NodeRow> nodeById, Dictionary<string, int> subRowById)
    {
        // 占用检查：同一 (Level, SubRow, Category) 下，同一 subColumn 只能放一个节点
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        var subColById = new Dictionary<string, int>(StringComparer.Ordinal);

        var ordered = nodes
            .OrderBy(n => n.Level)
            .ThenBy(n => subRowById.TryGetValue(n.TechId, out var sr) ? sr : 0)
            .ThenBy(n => (int)n.Category)
            .ThenBy(n => n.TechId, StringComparer.Ordinal)
            .ToList();

        static string OccKey(int level, int subRow, TechCategory cat, int col) => level + "|" + subRow + "|" + (int)cat + "|" + col;

        int FindFirstFree(int level, int subRow, TechCategory cat, int preferred)
        {
            preferred = Mathf.Max(0, preferred);
            // 先尝试 preferred，再从 0 递增找空位
            if (!occupied.Contains(OccKey(level, subRow, cat, preferred))) return preferred;
            for (int col = 0; col < 256; col++)
            {
                if (!occupied.Contains(OccKey(level, subRow, cat, col))) return col;
            }
            return preferred;
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            var n = ordered[i];
            if (n == null || string.IsNullOrWhiteSpace(n.TechId)) continue;

            int level = n.Level;
            int subRow = subRowById.TryGetValue(n.TechId, out var sr) ? sr : 0;

            int preferred = 0;
            bool hasPreferred = false;

            // 同类型依赖：优先沿用同类型前置科技的 subColumn（即使等级不同）
            if (n.Prereqs != null)
            {
                for (int p = 0; p < n.Prereqs.Length; p++)
                {
                    var prereqId = n.Prereqs[p];
                    if (string.IsNullOrWhiteSpace(prereqId)) continue;
                    if (!nodeById.TryGetValue(prereqId, out var prereqNode) || prereqNode == null) continue;
                    if (prereqNode.Category != n.Category) continue;
                    if (!subColById.TryGetValue(prereqId, out var prereqCol)) continue;

                    preferred = prereqCol;
                    hasPreferred = true;
                    break; // 取第一个同类型前置作为主链
                }
            }

            int assigned = FindFirstFree(level, subRow, n.Category, hasPreferred ? preferred : 0);
            subColById[n.TechId] = assigned;
            occupied.Add(OccKey(level, subRow, n.Category, assigned));
        }

        return subColById;
    }

    private static Dictionary<string, int> ComputeSameLevelSubRows(List<NodeRow> nodes, Dictionary<string, NodeRow> nodeById, out int maxSubRow)
    {
        var memo = new Dictionary<string, int>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        int Dfs(string techId)
        {
            if (string.IsNullOrWhiteSpace(techId)) return 0;
            if (memo.TryGetValue(techId, out var v)) return v;
            if (!nodeById.TryGetValue(techId, out var node) || node == null) return 0;

            if (visiting.Contains(techId))
            {
                Debug.LogWarning("Tech Panel Generator: 同级前置依赖存在环，techId=" + techId);
                return 0;
            }

            visiting.Add(techId);
            int best = 0;
            if (node.Prereqs != null)
            {
                for (int i = 0; i < node.Prereqs.Length; i++)
                {
                    var prereq = node.Prereqs[i];
                    if (string.IsNullOrWhiteSpace(prereq)) continue;
                    if (!nodeById.TryGetValue(prereq, out var p) || p == null) continue;
                    if (p.Level != node.Level) continue; // 只考虑同一级

                    best = Mathf.Max(best, Dfs(prereq) + 1);
                }
            }
            visiting.Remove(techId);

            memo[techId] = best;
            return best;
        }

        maxSubRow = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null || string.IsNullOrWhiteSpace(n.TechId)) continue;
            int sr = Dfs(n.TechId);
            memo[n.TechId] = sr;
            if (sr > maxSubRow) maxSubRow = sr;
        }

        return memo;
    }

    private static void EnsureOneCategoryBg(RectTransform bgRoot, TechCategory category, string name, Color color, Vector2 anchoredPos, Vector2 size)
    {
        var t = bgRoot.Find(name) as RectTransform;
        if (t == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(bgRoot, false);
            t = go.GetComponent<RectTransform>();
            t.anchorMin = new Vector2(0.5f, 0.5f);
            t.anchorMax = new Vector2(0.5f, 0.5f);
            t.pivot = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
        }

        t.anchoredPosition = anchoredPos;
        t.sizeDelta = size;

        var image = t.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
            image.raycastTarget = false;
        }
    }

    private static Dictionary<string, TechNodeView> FindExistingNodes(RectTransform root)
    {
        var dict = new Dictionary<string, TechNodeView>(StringComparer.Ordinal);
        if (root == null) return dict;

        var nodes = root.GetComponentsInChildren<TechNodeView>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            if (n == null) continue;

            // prefab 工作流下，节点 GameObject.name 会被设置为 TechId；这里允许 techId 为空时用 name 兜底。
            string key = !string.IsNullOrWhiteSpace(n.techId) ? n.techId : n.gameObject.name;
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (!dict.ContainsKey(key)) dict.Add(key, n);
        }
        return dict;
    }

    private List<NodeRow> ReadNodes(string fullPath)
    {
        var processor = DataTableGenerator.CreateDataTableProcessor(fullPath);
        int colId = FindColumn(processor, "Identifier");
        int colLevel = FindColumn(processor, "Level");
        int colCategory = FindColumn(processor, "Category");
        int colSprite = FindColumn(processor, "SpriteName");
        int colPrereq = FindColumn(processor, "PrereqTechIds");
        int colName = FindColumn(processor, "NameKey");
        int colDesc = FindColumn(processor, "DescriptionKey");

        var result = new List<NodeRow>();
        for (int r = 4; r < processor.RawRowCount; r++)
        {
            if (processor.IsCommentRow(r)) continue;

            string techId = processor.GetValue(r, colId);
            if (string.IsNullOrWhiteSpace(techId)) continue;

            int level = int.Parse(processor.GetValue(r, colLevel));
            var cat = DataTableExtension.ParseEnum<TechCategory>(processor.GetValue(r, colCategory));
            string sprite = processor.GetValue(r, colSprite);
            string nameKey = processor.GetValue(r, colName);
            string descKey = processor.GetValue(r, colDesc);

            // 解析 string[]
            var prereqStr = processor.GetValue(r, colPrereq);
            var prereqs = DataTableExtension.ParseArray<string>(prereqStr) ?? Array.Empty<string>();

            result.Add(new NodeRow
            {
                TechId = techId,
                Level = level,
                Category = cat,
                SpriteName = sprite,
                Prereqs = prereqs,
                NameKey = nameKey,
                DescKey = descKey,
            });
        }

        return result;
    }

    private static int FindColumn(DataTableProcessor processor, string columnName)
    {
        for (int i = 0; i < processor.RawColumnCount; i++)
        {
            if (processor.GetName(i) == columnName)
            {
                return i;
            }
        }
        throw new InvalidOperationException("Column not found: " + columnName);
    }

    private static string ToFullPath(string assetPath)
    {
        if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", assetPath));
        }
        return System.IO.Path.GetFullPath(assetPath);
    }

    private void ApplyIconSize(RectTransform rect)
    {
        if (rect == null) return;
        float s = Mathf.Max(1f, m_IconSize);
        rect.sizeDelta = new Vector2(s, s);
    }

    private void RefreshEdges()
    {
        if (m_Root == null) return;

        string nodeFullPath = ToFullPath(m_NodeTablePath);
        if (!File.Exists(nodeFullPath))
        {
            Debug.LogError("Tech Panel Generator: TechNodeTable not found: " + m_NodeTablePath);
            return;
        }

        var rows = ReadNodes(nodeFullPath);
        var prereqById = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r == null || string.IsNullOrWhiteSpace(r.TechId)) continue;
            prereqById[r.TechId] = r.Prereqs;
        }

        var nodes = FindExistingNodes(m_Root);
        var edgesRoot = EnsureEdgesRoot(m_Root);

        var desired = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kv in nodes)
        {
            var to = kv.Value;
            if (to == null || string.IsNullOrWhiteSpace(to.techId)) continue;

            if (!prereqById.TryGetValue(to.techId, out var prereqs) || prereqs == null)
                continue;

            for (int i = 0; i < prereqs.Length; i++)
            {
                var prereq = prereqs[i];
                if (string.IsNullOrWhiteSpace(prereq)) continue;
                if (!nodes.TryGetValue(prereq, out var fromNode)) continue;

                string key = prereq + "->" + to.techId;
                desired.Add(key);

                var edgeTransform = edgesRoot.Find(key);
                GameObject edgeGo;
                TechEdgeView edge;
                TechEdgeLine line;
                if (edgeTransform == null)
                {
                    edgeGo = new GameObject(key, typeof(RectTransform), typeof(Image), typeof(TechEdgeView), typeof(TechEdgeLine));
                    edgeGo.transform.SetParent(edgesRoot, false);

                    var img = edgeGo.GetComponent<Image>();
                    img.raycastTarget = false;
                    img.color = Color.white;
                    img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

                    edge = edgeGo.GetComponent<TechEdgeView>();
                    line = edgeGo.GetComponent<TechEdgeLine>();
                }
                else
                {
                    edgeGo = edgeTransform.gameObject;
                    edge = edgeGo.GetComponent<TechEdgeView>();
                    line = edgeGo.GetComponent<TechEdgeLine>();
                }

                edge.fromNode = fromNode.GetComponent<RectTransform>();
                edge.toNode = to.GetComponent<RectTransform>();

                if (line != null)
                {
                    line.from = edge.fromNode;
                    line.to = edge.toNode;
                    line.UpdateLine();
                }
            }
        }

        // 删除多余边
        var children = new List<Transform>();
        foreach (Transform child in edgesRoot) children.Add(child);
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child == null) continue;
            if (!desired.Contains(child.name))
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private static RectTransform EnsureEdgesRoot(RectTransform root)
    {
        var t = root.Find("__Edges");
        if (t == null)
        {
            var go = new GameObject("__Edges", typeof(RectTransform));
            go.transform.SetParent(root, false);
            t = go.transform;

            var rt = (RectTransform)t;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.SetAsFirstSibling();
        }
        return (RectTransform)t;
    }
}
#endif
