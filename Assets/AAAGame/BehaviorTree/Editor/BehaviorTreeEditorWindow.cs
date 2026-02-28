using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

public class BehaviorTreeEditorWindow : EditorWindow
{
    [SerializeField] private BehaviorTreeGraph graph;

    private GUIStyle nodeStyle;
    private GUIStyle chipStyle;
    private GUIStyle chipTextStyle;
    private GUIStyle noteTextAreaStyle;
    private Texture2D chipBgTex;
    private Texture2D noteBgTex;
    
    private float zoom = 1f;
    private const float zoomMin = 0.5f;
    private const float zoomMax = 2f;
    
    private Vector2 panOffset = Vector2.zero;
    private bool isPanning = false;
    
    private AbstractNode selectedNode;
    
    private Texture2D portTexture;
    
    private bool isLinking;
    private AbstractNode linkFromNode;
    private int linkFromPort;
    private double notificationClearAt;
    private const string noteControlPrefix = "BTNodeNote_";
    
    private Dictionary<AbstractNode, SerializedObject> _serializedObjects = new Dictionary<AbstractNode, SerializedObject>();

    [MenuItem("Tools/BehaviorTree/Editor")]
    public static void Open()
    {
        GetWindow<BehaviorTreeEditorWindow>("BT Editor");
    }

    private void OnEnable()
    {
        if (portTexture == null)
            InitPortTexture();
    }

    private void InitPortTexture()
    {
        int size = 32;
        portTexture = new Texture2D(size, size);

        Color clear = new Color(0,0,0,0);
        Color white = Color.white;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                portTexture.SetPixel(x, y, dist <= radius ? white : clear);
            }
        }

        portTexture.Apply();
    }

    private void OnSelectionChange()
    {
        if (Selection.activeObject is BehaviorTreeGraph g)
        {
            graph = g;
            Repaint();
        }
    }
    
    private void StartLink(AbstractNode node, int portIndex)
    {
        isLinking = true;
        linkFromNode = node;
        linkFromPort = portIndex;
        Debug.Log($"StartLink: {node.name} port {portIndex}");
    }

    private void OnGUI()
    {
        if (notificationClearAt > 0 && EditorApplication.timeSinceStartup >= notificationClearAt)
        {
            RemoveNotification();
            notificationClearAt = 0;
        }

        HandleZoom();
        HandlePan();
        HandleDeselectOnEmptyClick();
        HandleContextMenu();
        HandleDeleteKey();
        // 初始化节点样式
        if (nodeStyle == null)
        {
            nodeStyle = new GUIStyle("flow node 0");

            // 把所有状态的背景统一成 normal
            nodeStyle.onNormal.background  = nodeStyle.normal.background;
            nodeStyle.onActive.background  = nodeStyle.normal.background;
            nodeStyle.onFocused.background = nodeStyle.normal.background;
            nodeStyle.active.background    = nodeStyle.normal.background;
            nodeStyle.focused.background   = nodeStyle.normal.background;

            nodeStyle.alignment = TextAnchor.UpperLeft;
            nodeStyle.padding = new RectOffset(12, 12, 12, 12);
        }

        // 初始化 Chip 样式
        if (chipStyle == null)
        {
            Color chipColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            chipBgTex = MakeRoundedTex(32, 32, 10, chipColor);

            chipStyle = new GUIStyle();
            chipStyle.normal.background = chipBgTex;
            chipStyle.border = new RectOffset(10, 10, 10, 10);
            chipStyle.padding = new RectOffset(12, 12, 4, 4);

            chipTextStyle = new GUIStyle(EditorStyles.label);
            chipTextStyle.normal.textColor = Color.white;
            
        }

        if (noteTextAreaStyle == null)
        {
            noteBgTex = new Texture2D(1, 1);
            noteBgTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.08f));
            noteBgTex.Apply();

            noteTextAreaStyle = new GUIStyle(EditorStyles.textArea);
            noteTextAreaStyle.normal.background = noteBgTex;
            noteTextAreaStyle.focused.background = noteBgTex;
            noteTextAreaStyle.hover.background = noteBgTex;
            noteTextAreaStyle.active.background = noteBgTex;
            noteTextAreaStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            noteTextAreaStyle.padding = new RectOffset(6, 6, 4, 4);
        }

        DrawBackground();
        DrawToolbar();

        if (graph == null)
        {
            EditorGUILayout.HelpBox("Select a BehaviorTreeGraph asset to edit.", MessageType.Info);
            return;
        }

        GUI.EndGroup();  // 打破 Unity 隐式裁剪

        Matrix4x4 prevMatrix = GUI.matrix;
        Matrix4x4 translation = Matrix4x4.TRS(panOffset, Quaternion.identity, Vector3.one);
        Matrix4x4 scale = Matrix4x4.Scale(Vector3.one * zoom);
        GUI.matrix = translation * scale;

        BeginWindows();
        DrawAllNodes();
        EndWindows();
        DrawConnections();
        DrawLinkPreview();
        TryCompleteLink();

        GUI.matrix = prevMatrix;

// 补回 Unity 的隐式 group（21 是 tab 高度）
        GUI.BeginGroup(new Rect(0, 21, position.width, position.height));

        
    }
    
    private SerializedObject GetSerializedObject(AbstractNode node)
    {
        if (!_serializedObjects.TryGetValue(node, out var so) || so == null)
        {
            so = new SerializedObject(node);
            _serializedObjects[node] = so;
        }
        return so;
    }
    
    private void HandleZoom()
    {
        Event e = Event.current;

        if (e.type == EventType.ScrollWheel)
        {
            Vector2 mousePos = e.mousePosition;

            float oldZoom = zoom;
            float delta = -e.delta.y * 0.05f;
            zoom = Mathf.Clamp(zoom + delta, zoomMin, zoomMax);

            // 缩放比例变化
            float zoomFactor = zoom / oldZoom;

            // 关键：围绕鼠标位置修正平移
            panOffset = (panOffset - mousePos) * zoomFactor + mousePos;

            e.Use();
        }
    }
    
    private void HandlePan()
    {
        Event e = Event.current;

        if (e.button == 2)
        {
            if (e.type == EventType.MouseDown)
            {
                isPanning = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isPanning)
            {
                panOffset += e.delta;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                isPanning = false;
                e.Use();
            }
        }
    }
    
    private void HandleContextMenu()
    {
        Event e = Event.current;

        if (e.type == EventType.ContextClick)
        {
            Vector2 mousePos = ScreenToGraph(e.mousePosition);

            if (IsMouseOverAnyNode(mousePos))
                return;

            Vector2 graphPos = mousePos;

            GenericMenu menu = new GenericMenu();

            // 组合节点手动加
            menu.AddItem(new GUIContent("Create/Selector"), false, () => CreateNode<SelectorNode>(graphPos, "Selector"));
            menu.AddItem(new GUIContent("Create/Sequence"), false, () => CreateNode<SequenceNode>(graphPos, "Sequence"));

            // 自动扫描所有叶节点
            var leafTypes = TypeCache.GetTypesDerivedFrom<AbstractNode>()
                .Where(t => !t.IsAbstract
                            && t != typeof(SelectorNode)
                            && t != typeof(SequenceNode)
                            && t != typeof(RootNode));

            foreach (var type in leafTypes)
            {
                var capturedType = type;
                string name = type.Name;

                // 如果有 CreateAssetMenu，用它的 menuName 作为路径
                var attr = type.GetCustomAttribute<CreateAssetMenuAttribute>();
                if (attr != null && !string.IsNullOrEmpty(attr.menuName))
                    name = attr.menuName.Replace("BehaviorTree/Leaf/", "");

                menu.AddItem(new GUIContent($"Create/Leaf/{name}"), false, () =>
                {
                    var node = CreateNode(capturedType, graphPos, name);
                });
            }

            if (selectedNode != null && selectedNode != graph.root)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Delete"), false, () => DeleteNode(selectedNode));
            }

            menu.ShowAsContext();
            e.Use();
        }
    }
    
    private bool IsMouseOverAnyNode(Vector2 graphPos)
    {
        if (graph == null) return false;

        if (graph.root != null && graph.root.nodeRect.Contains(graphPos))
            return true;

        if (graph.nodes != null)
        {
            foreach (var node in graph.nodes)
            {
                if (node != null && node.nodeRect.Contains(graphPos))
                    return true;
            }
        }

        return false;
    }
    private T CreateNode<T>(Vector2 position, string defaultName = null) 
        where T : AbstractNode
    {
        if (graph == null) return null;

        T newNode = ScriptableObject.CreateInstance<T>();

        newNode.nodeID = System.Guid.NewGuid().ToString();

        newNode.name = string.IsNullOrEmpty(defaultName) 
            ? typeof(T).Name 
            : defaultName;

        newNode.nodeRect = new Rect(position.x, position.y, 200, 100);

        // 只有组合节点才默认加一个子节点槽
        if (newNode is SelectorNode || newNode is SequenceNode)
            newNode.children.Add(null);

        graph.nodes.Add(newNode);

        AssetDatabase.AddObjectToAsset(newNode, graph);

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();

        return newNode;
    }
    private AbstractNode CreateNode(System.Type type, Vector2 position, string defaultName = null)
    {
        if (graph == null) return null;

        AbstractNode newNode = (AbstractNode)ScriptableObject.CreateInstance(type);

        newNode.nodeID = System.Guid.NewGuid().ToString();
        newNode.name = string.IsNullOrEmpty(defaultName) ? type.Name : defaultName;
        newNode.nodeRect = new Rect(position.x, position.y, 200, 100);

        if (newNode is SelectorNode || newNode is SequenceNode)
            newNode.children.Add(null);

        graph.nodes.Add(newNode);
        AssetDatabase.AddObjectToAsset(newNode, graph);
        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();

        return newNode;
    }
    
    private Vector2 ScreenToGraph(Vector2 screenPos)
    {
        return (screenPos - panOffset) / zoom;
    }

    private Vector2 GraphToScreen(Vector2 graphPos)
    {
        return graphPos * zoom + panOffset;
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            graph = (BehaviorTreeGraph)EditorGUILayout.ObjectField(
                graph,
                typeof(BehaviorTreeGraph),
                false,
                GUILayout.Width(300)
            );

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Ping", EditorStyles.toolbarButton))
            {
                if (graph != null)
                    EditorGUIUtility.PingObject(graph);
            }
        }
    }

    private void DrawAllNodes()
    {
        if (graph.root != null)
        {
            graph.root.nodeRect = GUI.Window(
                99999,
                graph.root.nodeRect,
                DrawNodeWindow,
                GUIContent.none,
                nodeStyle
            );
        }

        if (graph.nodes == null) return;

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            AbstractNode node = graph.nodes[i];
            if (node == null) continue;

            node.nodeRect = GUI.Window(
                i,
                node.nodeRect,
                DrawNodeWindow,
                GUIContent.none,
                nodeStyle
            );
        }
    }

    private void DrawBackground()
    {
        EditorGUI.DrawRect(
            new Rect(0, 0, position.width, position.height),
            new Color(0.13f, 0.13f, 0.13f)
        );
    }

    private void DrawConnections()
    {
        if (graph == null) return;

        Handles.BeginGUI();

        DrawNodeConnections(graph.root);

        if (graph.nodes != null)
        {
            foreach (var node in graph.nodes)
            {
                DrawNodeConnections(node);
            }
        }

        Handles.EndGUI();
    }

    private void DrawNodeConnections(AbstractNode fromNode)
    {
        if (fromNode == null || fromNode.children == null)
            return;

        for (int i = 0; i < fromNode.children.Count; i++)
        {
            AbstractNode toNode = fromNode.children[i];
            if (toNode == null) continue;

            Vector2 start = fromNode.GetOutputPortPos(i);
            Vector2 end = toNode.GetInputPortPos();

            Handles.DrawBezier(
                start,
                end,
                start + Vector2.up * 50f,
                end + Vector2.down * 50f,
                Color.white,
                null,
                2f
            );
        }
    }

    private void DrawNodeWindow(int id)
    {
        if (graph == null) return;

        AbstractNode node =
            id == 99999 ? graph.root : graph.nodes[id];

        if (node == null) return;
        
        HandleNodeSelection(node);

        NodeView.DrawNode(
            node,
            selectedNode == node,
            nodeStyle,
            chipStyle,
            chipTextStyle,
            portTexture
        );

        Rect noteRect = GetNodeNoteRect(node);
        DrawNodeNoteField(node, noteRect);
        
        int portIndex;
        if (PortView.TryGetClickedOutput(node, Event.current, out portIndex))
        {
            StartLink(node, portIndex);
            Event.current.Use();
        }

        // 改成
        bool isComposite = node is SelectorNode || node is SequenceNode;
        if (isComposite)
            DrawPortButtons(node);
        
        bool isLeaf = node.outputCount == 0 && !(node is RootNode);
        if (isLeaf)
            DrawLeafFields(node);

        // 只允许上半部分拖动（避开 note 区域）
        float dragHeight = GetNodeNoteRect(node).y; // note 开始的 y 就是上半部分的高度
        Rect dragRect = new Rect(0f, 0f, node.nodeRect.width, dragHeight);

        if (Event.current.button == 0)
            GUI.DragWindow(dragRect);
    }
    
    private void DrawLeafFields(AbstractNode node)
    {
        SerializedObject so = GetSerializedObject(node);
        so.Update();

        Rect noteRect = GetNodeNoteRect(node);
        float y = noteRect.yMax + 6f; // note 下方开始
        float x = 10f;
        float width = node.nodeRect.width - 20f;

        SerializedProperty prop = so.GetIterator();
        prop.NextVisible(true);
        while (prop.NextVisible(false))
        {
            if (prop.name == "nodeID" || prop.name == "note" ||
                prop.name == "children" || prop.name == "parent" ||
                prop.name == "nodeRect")
                continue;

            float height = EditorGUI.GetPropertyHeight(prop, true);
            Rect rect = new Rect(x, y, width, height);
            EditorGUI.PropertyField(rect, prop, true);
            y += height + 2f;
        }
        float requiredHeight = y + 10f;
        if (Mathf.Abs(node.nodeRect.height - requiredHeight) > 1f)
        {
            node.nodeRect.height = requiredHeight;
            EditorUtility.SetDirty(node);
        }

        so.ApplyModifiedProperties();
    }

    private void DrawNodeNoteField(AbstractNode node, Rect noteRect)
    {
        string controlName = noteControlPrefix + node.nodeID;
    
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (!noteRect.Contains(e.mousePosition))
            {
                // 点击在 note 以外，强制清掉焦点
                if (GUI.GetNameOfFocusedControl() == controlName)
                    GUI.FocusControl(string.Empty);
            }
        }

        EditorGUI.BeginChangeCheck();
        GUI.SetNextControlName(controlName);
        string next = EditorGUI.TextArea(noteRect, node.note ?? string.Empty, noteTextAreaStyle);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(node, "Edit Node Note");
            node.note = next;
            EditorUtility.SetDirty(node);
            EditorUtility.SetDirty(graph);
        }
    }

    private Rect GetNodeNoteRect(AbstractNode node)
    {
        float width = node.nodeRect.width - 20f;
        float height = 24f;
        float top = (node.nodeRect.height - height) * 0.5f;
        top = Mathf.Clamp(top, 42f, node.nodeRect.height - height - 14f);
        return new Rect(10f, top, width, height);
    }

    private void HandleDeselectOnEmptyClick()
    {
        Event e = Event.current;

        if (e.type != EventType.MouseDown || e.button != 0)
            return;

        Vector2 graphPos = ScreenToGraph(e.mousePosition);
        if (IsMouseOverAnyNode(graphPos))
            return;

        if (selectedNode != null)
        {
            selectedNode = null;
            GUI.changed = true;
            Repaint();
        }
    }
    private void HandleNodeSelection(AbstractNode node)
    {
        Event e = Event.current;

        if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
        {
            selectedNode = node;
            GUI.changed = true;
        }

        if (e.type == EventType.MouseUp && e.button == 1)
        {
            selectedNode = node;

            GenericMenu menu = new GenericMenu();

            if (node != graph.root)
                menu.AddItem(new GUIContent("Delete"), false, () => DeleteNode(node));

            menu.ShowAsContext();
            e.Use();
        }
    }
    
    private void DrawPortButtons(AbstractNode node)
    {
        const int maxPorts = 5;

        float y = node.nodeRect.height - 28f;

        Rect minusRect = new Rect(8, y, 20, 18);
        Rect plusRect  = new Rect(node.nodeRect.width - 28, y, 20, 18);

        if (GUI.Button(minusRect, "-"))
        {
            if (node.outputCount > 1)
            {
                int removeIndex = node.children.Count - 1;
                DisconnectOutput(node, removeIndex);
                node.children.RemoveAt(removeIndex);
                GUI.changed = true;
            }
        }

        if (GUI.Button(plusRect, "+"))
        {
            if (node.outputCount < maxPorts)
            {
                node.children.Add(null);
                GUI.changed = true;
            }
        }
    }
    
   
    private Texture2D MakeRoundedTex(int width, int height, int radius, Color col)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color clear = new Color(0, 0, 0, 0);
        Color[] pixels = new Color[width * height];

        float r = radius;
        float edgeSoftness = 2f;   // 边缘平滑宽度，可调

        Vector2 tl = new Vector2(r, height - r);
        Vector2 tr = new Vector2(width - r-1, height - r);
        Vector2 bl = new Vector2(r, r);
        Vector2 br = new Vector2(width - r-1, r);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float alpha = 1f;

                if (x < r && y < r)
                    alpha = SmoothCorner(new Vector2(x, y), bl, r, edgeSoftness);
                else if (x < r && y > height - r)
                    alpha = SmoothCorner(new Vector2(x, y), tl, r, edgeSoftness);
                else if (x > width - r && y < r)
                    alpha = SmoothCorner(new Vector2(x, y), br, r, edgeSoftness);
                else if (x > width - r && y > height - r)
                    alpha = SmoothCorner(new Vector2(x, y), tr, r, edgeSoftness);

                if (alpha <= 0f)
                    pixels[y * width + x] = clear;
                else
                    pixels[y * width + x] = new Color(col.r, col.g, col.b, col.a * alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private float SmoothCorner(Vector2 p, Vector2 center, float radius, float softness)
    {
        float dist = Vector2.Distance(p, center);
        float delta = dist - radius;

        if (delta <= 0f) return 1f;
        if (delta >= softness) return 0f;

        return 1f - Mathf.SmoothStep(0f, softness, delta);
    }

    private void ConnectNodes(AbstractNode fromNode, int fromPort, AbstractNode toNode)
    {
        if (fromNode == null || toNode == null)
            return;

        if (fromNode == toNode)
            return;

        if (fromPort < 0 || fromPort >= fromNode.children.Count)
            return;

        if (WouldCreateCycle(fromNode, toNode))
        {
            ShowQuickNotification("Cannot create cycle", 0.7);
            return;
        }

        // Same output can only point to one target.
        DisconnectOutput(fromNode, fromPort);

        // Each node can only have one parent.
        DisconnectInput(toNode);

        fromNode.children[fromPort] = toNode;
        toNode.parent = fromNode;

        GUI.changed = true;
        EditorUtility.SetDirty(graph);
    }

    private bool WouldCreateCycle(AbstractNode fromNode, AbstractNode toNode)
    {
        AbstractNode current = fromNode;
        while (current != null)
        {
            if (current == toNode)
                return true;
            current = current.parent;
        }

        return false;
    }

    private void ShowQuickNotification(string message, double durationSeconds)
    {
        ShowNotification(new GUIContent(message));
        notificationClearAt = EditorApplication.timeSinceStartup + durationSeconds;
    }

    private void DisconnectInput(AbstractNode node)
    {
        if (node == null || node.parent == null)
            return;

        AbstractNode oldParent = node.parent;

        if (oldParent.children != null)
        {
            for (int i = 0; i < oldParent.children.Count; i++)
            {
                if (oldParent.children[i] == node)
                    oldParent.children[i] = null;
            }
        }

        node.parent = null;
        GUI.changed = true;
        EditorUtility.SetDirty(graph);
    }

    private void DisconnectOutput(AbstractNode parentNode, int portIndex)
    {
        if (parentNode == null || parentNode.children == null)
            return;

        if (portIndex < 0 || portIndex >= parentNode.children.Count)
            return;

        AbstractNode child = parentNode.children[portIndex];
        if (child != null && child.parent == parentNode)
            child.parent = null;

        parentNode.children[portIndex] = null;
        GUI.changed = true;
        EditorUtility.SetDirty(graph);
    }
    
    private void DeleteNode(AbstractNode node)
    {
        if (node == null) return;

        // 可选：不允许删 root
        if (node == graph.root)
            return;

        DisconnectInput(node);

        if (node.children != null)
        {
            for (int i = 0; i < node.children.Count; i++)
                DisconnectOutput(node, i);
        }

        graph.nodes.Remove(node);
        _serializedObjects.Remove(node);

        AssetDatabase.RemoveObjectFromAsset(node);
        DestroyImmediate(node, true);

        selectedNode = null;

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();
    }
    
    private void HandleDeleteKey()
    {
        Event e = Event.current;
        string focusedControl = GUI.GetNameOfFocusedControl();
        bool typingNote = !string.IsNullOrEmpty(focusedControl) &&
                          focusedControl.StartsWith(noteControlPrefix);

        if (typingNote)
            return;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Backspace)
        {
            if (selectedNode != null)
            {
                DeleteNode(selectedNode);
                e.Use();
            }
        }
    }
    
    public void OpenGraph(BehaviorTreeGraph g)
    {
        graph = g;
        Repaint();
    }
    
    private void DrawLinkPreview()
    {
        if (!isLinking || linkFromNode == null)
            return;

        Vector2 start = linkFromNode.GetOutputPortPos(linkFromPort);
        Vector2 end = Event.current.mousePosition; // 直接用，不转换

        Handles.BeginGUI();
        Handles.DrawBezier(
            start,
            end,
            start + Vector2.up * 50f,
            end + Vector2.down * 50f,
            Color.white,
            null,
            2f
        );
        Handles.EndGUI();

        Repaint();
    }
    
    private void TryCompleteLink()
    {
        if (!isLinking)
            return;

        Event e = Event.current;

        if (e.type != EventType.MouseUp || e.button != 0)
            return;

        Vector2 graphPos = Event.current.mousePosition;

        foreach (var node in graph.nodes)
        {
            if (node == null || node == linkFromNode)
                continue;

            // 检测 input 口
            Rect inputRect = PortView.GetInputHitRectInGraph(node);

            if (inputRect.Contains(graphPos))
            {
                ConnectNodes(linkFromNode, linkFromPort, node);
                Debug.Log($"Linked {linkFromNode.name} -> {node.name}");
                break;
            }
        }

        isLinking = false;
        linkFromNode = null;
        linkFromPort = -1;

        e.Use();
    }
}
