using UnityEngine;

public static class PortView
{
    private const float PortVisualSize = 18f;
    private const float PortHitSize = 28f;

    public struct PortHit
    {
        public AbstractNode node;
        public int portIndex;
    }

    public static void DrawPorts(AbstractNode node, Texture2D portTexture)
    {
        Color prev = GUI.color;

        // 输入口
        if (!(node is RootNode))
        {
            Rect inputRect = GetInputRect(node);
            GUI.color = Color.cyan;
            GUI.DrawTexture(inputRect, portTexture);
        }

        // 输出口
        for (int i = 0; i < node.outputCount; i++)
        {
            Rect rect = GetOutputVisualRect(node, i);
            GUI.color = Color.yellow;
            GUI.DrawTexture(rect, portTexture);
        }

        GUI.color = prev;
    }

    public static bool TryGetClickedOutput(
        AbstractNode node,
        Event e,
        out int portIndex
    )
    {
        portIndex = -1;

        if (e.type != EventType.MouseDown || e.button != 0)
            return false;

        for (int i = 0; i < node.outputCount; i++)
        {
            Rect rect = GetOutputHitRect(node, i);
            if (rect.Contains(e.mousePosition))
            {
                portIndex = i;
                return true;
            }
        }

        return false;
    }

    public static Rect GetInputHitRectInGraph(AbstractNode node)
    {
        Vector2 center = node.GetInputPortPos();
        float half = PortHitSize * 0.5f;

        return new Rect(
            center.x - half,
            center.y - half,
            PortHitSize,
            PortHitSize
        );
    }

    private static Rect GetOutputVisualRect(AbstractNode node, int index)
    {
        float spacing = node.nodeRect.width / (node.outputCount + 1);
        float half = PortVisualSize * 0.5f;
        float x = spacing * (index + 1) - half;

        return new Rect(
            x,
            node.nodeRect.height - half,
            PortVisualSize,
            PortVisualSize
        );
    }

    private static Rect GetOutputHitRect(AbstractNode node, int index)
    {
        float spacing = node.nodeRect.width / (node.outputCount + 1);
        float centerX = spacing * (index + 1);
        float centerY = node.nodeRect.height;
        float half = PortHitSize * 0.5f;

        return new Rect(
            centerX - half,
            centerY - half,
            PortHitSize,
            PortHitSize
        );
    }

    private static Rect GetInputRect(AbstractNode node)
    {
        float half = PortVisualSize * 0.5f;

        return new Rect(
            node.nodeRect.width / 2 - half,
            -half,
            PortVisualSize,
            PortVisualSize
        );
    }
}
