using UnityEditor;
using UnityEngine;

public static class NodeView
{
    public static void DrawNode(
        AbstractNode node,
        bool isSelected,
        GUIStyle nodeStyle,
        GUIStyle chipStyle,
        GUIStyle chipTextStyle,
        Texture2D portTexture
    )
    {
        if (isSelected)
        {
            Rect highlight = new Rect(0, 0, node.nodeRect.width, node.nodeRect.height);
            EditorGUI.DrawRect(highlight, new Color(0.3f, 0.5f, 1f, 0.25f));
        }

        DrawTitle(node, chipStyle, chipTextStyle);
        PortView.DrawPorts(node, portTexture);
    }

    private static void DrawTitle(AbstractNode node, GUIStyle chipStyle, GUIStyle textStyle)
    {
        string nodeName = node.GetNodeName();
        Vector2 size = textStyle.CalcSize(new GUIContent(nodeName));

        float width = Mathf.Max(size.x + 24f, 120f);
        float height = Mathf.Max(size.y + 8f, 22f);

        Rect chipRect = new Rect(8, 10, width, height);

        GUI.Box(chipRect, GUIContent.none, chipStyle);
        GUI.Label(
            new Rect(chipRect.x + 12, chipRect.y + 4, size.x, size.y),
            nodeName,
            textStyle
        );
    }
}
