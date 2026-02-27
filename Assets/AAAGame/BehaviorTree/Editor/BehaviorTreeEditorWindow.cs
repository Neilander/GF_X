using System;
using UnityEditor;
using UnityEngine;

public class BehaviorTreeEditorWindow : EditorWindow
{
    [SerializeField] private BehaviorTreeGraph graph;

    private GUIStyle nodeStyle;
    private GUIStyle chipStyle;
    private GUIStyle chipTextStyle;
    private Texture2D chipBgTex;
    
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
        HandleZoom();
        HandlePan();
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

        DrawBackground();
        DrawToolbar();

        if (graph == null)
        {
            EditorGUILayout.HelpBox("Select a BehaviorTreeGraph asset to edit.", MessageType.Info);
            return;
        }

        Matrix4x4 prevMatrix = GUI.matrix;

        Matrix4x4 translation = Matrix4x4.TRS(panOffset, Quaternion.identity, Vector3.one);
        Matrix4x4 scale = Matrix4x4.Scale(Vector3.one * zoom);

        GUI.matrix = translation * scale;

        BeginWindows();
        DrawAllNodes();
        EndWindows();
        DrawLinkPreview();

        GUI.matrix = prevMatrix;
        TryCompleteLink();
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
            menu.AddItem(new GUIContent("Create/Selector"), false, () =>
            {
                CreateNode<SelectorNode>(graphPos, "Selector");
            });

            if (selectedNode != null && selectedNode != graph.root)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    DeleteNode(selectedNode);
                });
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

        // 生成唯一ID
        newNode.nodeID = System.Guid.NewGuid().ToString();

        // 默认名字
        newNode.name = string.IsNullOrEmpty(defaultName) 
            ? typeof(T).Name 
            : defaultName;

        // 默认大小
        newNode.nodeRect = new Rect(position.x, position.y, 200, 100);
        newNode.children.Add(null);

        // 加入Graph
        graph.nodes.Add(newNode);

        // 作为子资产
        AssetDatabase.AddObjectToAsset(newNode, graph);

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();

        return newNode;
    }
    
    private Vector2 ScreenToGraph(Vector2 screenPos)
    {
        return (screenPos - panOffset) / zoom;
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
        
        int portIndex;
        if (PortView.TryGetClickedOutput(node, Event.current, out portIndex))
        {
            StartLink(node, portIndex);
            Event.current.Use();
        }

        if (node is SelectorNode || node is SequenceNode)
            DrawPortButtons(node);

        if (Event.current.button == 0)
            GUI.DragWindow();
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
                node.children.RemoveAt(node.children.Count - 1);
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
    
    private void DeleteNode(AbstractNode node)
    {
        if (node == null) return;

        // 可选：不允许删 root
        if (node == graph.root)
            return;

        graph.nodes.Remove(node);

        AssetDatabase.RemoveObjectFromAsset(node);
        DestroyImmediate(node, true);

        selectedNode = null;

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();
    }
    
    private void HandleDeleteKey()
    {
        Event e = Event.current;

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
        Vector2 end = ScreenToGraph(Event.current.mousePosition);

        Handles.BeginGUI();
        Handles.DrawBezier(
            start,
            end,
            start + Vector2.up * 50,
            end + Vector2.down * 50,
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

        Vector2 graphPos = ScreenToGraph(e.mousePosition);

        foreach (var node in graph.nodes)
        {
            if (node == null || node == linkFromNode)
                continue;

            // 检测 input 口
            Rect inputRect = new Rect(
                node.nodeRect.x + node.nodeRect.width / 2 - 6,
                node.nodeRect.y - 6,
                12,
                12
            );

            if (inputRect.Contains(graphPos))
            {
                // 连接！
                linkFromNode.children[linkFromPort] = node;
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
