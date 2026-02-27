using UnityEngine;

public static class PortView
{
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
            Rect rect = GetOutputRect(node, i);
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
            Rect rect = GetOutputRect(node, i);
            if (rect.Contains(e.mousePosition))
            {
                portIndex = i;
                return true;
            }
        }

        return false;
    }

    private static Rect GetOutputRect(AbstractNode node, int index)
    {
        float spacing = node.nodeRect.width / (node.outputCount + 1);
        float x = spacing * (index + 1) - 6;

        return new Rect(
            x,
            node.nodeRect.height - 6,
            12,
            12
        );
    }

    private static Rect GetInputRect(AbstractNode node)
    {
        return new Rect(
            node.nodeRect.width / 2 - 6,
            -6,
            12,
            12
        );
    }
}
