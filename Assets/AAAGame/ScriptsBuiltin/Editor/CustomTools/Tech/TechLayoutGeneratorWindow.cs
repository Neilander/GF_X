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
/// 科技树面板生成器（读表生成 UI 节点与初始极坐标，旧节点不改）。
/// - 读取 TechNodeTable.txt
/// - 在指定 Root 下为每个 TechId 生成/更新一个子物体（带 TechNodeView）
/// - 旧节点保留半径/角度（支持手动微调）
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
    }

    private string m_NodeTablePath = "Assets/AAAGame/DataTable/Tech/TechNodeTable.txt";
    private RectTransform m_Root;
    private Vector2 m_Center = Vector2.zero;
    private int m_MaxLevel = 10;
    private float m_RadiusMin = 120f;
    private float m_RadiusMax = 520f;
    private float m_IconSize = 80f;
    private float m_SectorWidthDeg = 60f;
    private float m_StartAngleDeg = 90f;

    [MenuItem("CustomTools/Tech/Tech Panel Generator")]
    public static void Open()
    {
        GetWindow<TechLayoutGeneratorWindow>("Tech Panel Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("表格与根节点", EditorStyles.boldLabel);
        m_NodeTablePath = EditorGUILayout.TextField("TechNodeTable", m_NodeTablePath);
        m_Root = (RectTransform)EditorGUILayout.ObjectField("Root RectTransform", m_Root, typeof(RectTransform), true);
        m_Center = EditorGUILayout.Vector2Field("Center", m_Center);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("布局参数", EditorStyles.boldLabel);
        m_MaxLevel = EditorGUILayout.IntField("MaxLevel", m_MaxLevel);
        m_RadiusMin = EditorGUILayout.FloatField("RadiusMin", m_RadiusMin);
        m_RadiusMax = EditorGUILayout.FloatField("RadiusMax", m_RadiusMax);
        m_IconSize = EditorGUILayout.FloatField("IconSize", m_IconSize);
        m_SectorWidthDeg = EditorGUILayout.FloatField("SectorWidthDeg", m_SectorWidthDeg);
        m_StartAngleDeg = EditorGUILayout.FloatField("StartAngleDeg", m_StartAngleDeg);

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
        var grouped = nodes
            .GroupBy(n => (n.Level, n.Category))
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.TechId, StringComparer.Ordinal).ToList());

        Dictionary<string, TechNodeView> existing = FindExistingNodes(m_Root);

        foreach (var kv in grouped)
        {
            int level = kv.Key.Level;
            var category = kv.Key.Category;
            var list = kv.Value;

            float t = m_MaxLevel <= 1 ? 0f : Mathf.Clamp01((level - 1f) / (m_MaxLevel - 1f));
            float r = Mathf.Lerp(m_RadiusMin, m_RadiusMax, t);
            float center = m_StartAngleDeg + (int)category * 72f;
            float width = Mathf.Clamp(m_SectorWidthDeg, 0f, 72f);
            float step = list.Count <= 0 ? 0f : (width / list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                if (string.IsNullOrWhiteSpace(n.TechId)) continue;

                TechNodeView view = null;
                if (!existing.TryGetValue(n.TechId, out view))
                {
                    var go = new GameObject(n.TechId, typeof(RectTransform), typeof(TechNodeView), typeof(UnityEngine.UI.Image));
                    go.transform.SetParent(m_Root, false);
                    view = go.GetComponent<TechNodeView>();
                    view.rect = go.GetComponent<RectTransform>();
                    view.techId = n.TechId;
                    view.level = n.Level;
                    view.category = n.Category;
                    view.prereqTechIds = n.Prereqs;

                    ApplyIconSize(view.rect);

                    // 节点交互脚本（悬浮提示/长按点亮/灰度）
                    EnsureNodeRuntimeComponents(go, view);

                    // 自动设置图标
                    var img = go.GetComponent<UnityEngine.UI.Image>();
                    if (img != null && !string.IsNullOrWhiteSpace(n.SpriteName))
                    {
                        string spritePath = UtilityBuiltin.AssetsPath.GetSpritesPath(n.SpriteName);
                        if (string.IsNullOrWhiteSpace(spritePath))
                        {
                            GF.LogWarning("Tech Panel Generator: SpritePath 为空, SpriteName=" + n.SpriteName);
                        }
                        else
                        {
                            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

                            if (sprite != null)
                            {
                                img.sprite = sprite;
                            }
                            else if (!EditorApplication.isPlaying)
                            {
                                GF.LogWarning("Tech Panel Generator: Sprite 加载失败(AssetDatabase): " + spritePath);
                            }
                        }
                    }

                    EnsureNodeTooltip(go.transform, out var tooltipRoot, out var tooltipText);
                    BindNodeInteract(go, view, img, tooltipRoot, tooltipText);

                    float angle = center - width / 2f + (i + 0.5f) * step;
                    view.SetPolar(r, angle);
                    view.ApplyPosition(m_Center, 1f);
                }
                else
                {
                    // 旧节点：只同步标识，不覆盖 radius/angle（保留手动微调）
                    view.level = n.Level;
                    view.category = n.Category;
                    view.prereqTechIds = n.Prereqs;

                    var rect = view.GetComponent<RectTransform>();
                    ApplyIconSize(rect);

                    EnsureNodeRuntimeComponents(view.gameObject, view);
                    var img = view.GetComponent<UnityEngine.UI.Image>();
                    EnsureNodeTooltip(view.transform, out var tooltipRoot, out var tooltipText);
                    BindNodeInteract(view.gameObject, view, img, tooltipRoot, tooltipText);

                    view.ApplyPosition(m_Center, 1f);
                }
            }
        }
    }

    private static Dictionary<string, TechNodeView> FindExistingNodes(RectTransform root)
    {
        var dict = new Dictionary<string, TechNodeView>(StringComparer.Ordinal);
        var views = root.GetComponentsInChildren<TechNodeView>(true);
        foreach (var v in views)
        {
            if (!string.IsNullOrWhiteSpace(v.techId) && !dict.ContainsKey(v.techId))
                dict.Add(v.techId, v);
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

        var result = new List<NodeRow>();
        for (int r = 4; r < processor.RawRowCount; r++)
        {
            if (processor.IsCommentRow(r)) continue;

            string techId = processor.GetValue(r, colId);
            if (string.IsNullOrWhiteSpace(techId)) continue;

            int level = int.Parse(processor.GetValue(r, colLevel));
            var cat = DataTableExtension.ParseEnum<TechCategory>(processor.GetValue(r, colCategory));
            string sprite = processor.GetValue(r, colSprite);

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

    private static void EnsureNodeRuntimeComponents(GameObject nodeGo, TechNodeView view)
    {
        if (nodeGo.GetComponent<HoldProgress>() == null)
        {
            nodeGo.AddComponent<HoldProgress>();
        }

        var interact = nodeGo.GetComponent<TechNodeInteract>();
        if (interact == null)
        {
            interact = nodeGo.AddComponent<TechNodeInteract>();
        }

        // 尽量保证 TechNodeView 绑定
        if (view != null)
        {
            var so = new SerializedObject(interact);
            so.FindProperty("nodeView").objectReferenceValue = view;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static Image EnsureHoldFill(GameObject nodeGo, HoldProgress hold)
    {
        var t = nodeGo.transform.Find("Fill");
        if (t == null)
        {
            var go = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(nodeGo.transform, false);
            t = go.transform;

            var rt = (RectTransform)t;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        var img = t.GetComponent<Image>();
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.fillClockwise = true;
        if (img.color.a <= 0.001f)
        {
            img.color = new Color(1f, 1f, 1f, 0.35f);
        }

        if (hold != null)
        {
            var so = new SerializedObject(hold);
            so.FindProperty("fillImage").objectReferenceValue = img;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        return img;
    }

    private static void EnsureNodeTooltip(Transform node, out GameObject tooltipRoot, out Text tooltipText)
    {
        var t = node.Find("Tooltip");
        if (t == null)
        {
            var go = new GameObject("Tooltip", typeof(RectTransform));
            go.transform.SetParent(node, false);
            t = go.transform;

            var rt = (RectTransform)t;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(40f, 40f);
            rt.sizeDelta = new Vector2(360f, 220f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(t, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
        }

        tooltipRoot = t.gameObject;
        tooltipText = t.GetComponentInChildren<Text>(true);
        if (tooltipText != null)
        {
            tooltipText.raycastTarget = false;
            tooltipText.supportRichText = true;
            tooltipText.alignment = TextAnchor.UpperLeft;
            if (tooltipText.font == null)
                tooltipText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        if (tooltipRoot != null)
            tooltipRoot.SetActive(false);
    }

    private static void BindNodeInteract(GameObject nodeGo, TechNodeView view, UnityEngine.UI.Image icon, GameObject tooltipRoot, Text tooltipText)
    {
        var hold = nodeGo.GetComponent<HoldProgress>();
        if (hold == null) hold = nodeGo.AddComponent<HoldProgress>();
        var fill = EnsureHoldFill(nodeGo, hold);

        var interact = nodeGo.GetComponent<TechNodeInteract>();
        if (interact == null) interact = nodeGo.AddComponent<TechNodeInteract>();

        var so = new SerializedObject(interact);
        so.FindProperty("nodeView").objectReferenceValue = view;
        so.FindProperty("iconImage").objectReferenceValue = icon;
        so.FindProperty("holdProgress").objectReferenceValue = hold;
        so.FindProperty("tooltipRoot").objectReferenceValue = tooltipRoot;
        so.FindProperty("tooltipText").objectReferenceValue = tooltipText;
        // 确保 hold 的 fill 已绑定
        if (fill != null)
        {
            var holdSO = new SerializedObject(hold);
            holdSO.FindProperty("fillImage").objectReferenceValue = fill;
            holdSO.ApplyModifiedPropertiesWithoutUndo();
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private void RefreshEdges()
    {
        if (m_Root == null) return;

        var nodes = FindExistingNodes(m_Root);
        var edgesRoot = EnsureEdgesRoot(m_Root);

        var desired = new HashSet<string>(StringComparer.Ordinal);

        foreach (var to in nodes.Values)
        {
            if (to == null || string.IsNullOrWhiteSpace(to.techId)) continue;
            if (to.prereqTechIds == null) continue;

            for (int i = 0; i < to.prereqTechIds.Length; i++)
            {
                var prereq = to.prereqTechIds[i];
                if (string.IsNullOrWhiteSpace(prereq)) continue;
                if (!nodes.TryGetValue(prereq, out var fromView)) continue;

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

                edge.fromNode = fromView.GetComponent<RectTransform>();
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
