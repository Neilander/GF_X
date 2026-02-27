using System.Collections.Generic;
using UnityEngine;

public enum NodeState
{
    Success,
    Failure
}

public abstract class AbstractNode : ScriptableObject
{
    [HideInInspector]
    public List<AbstractNode> children = new List<AbstractNode>();
    [HideInInspector]
    public AbstractNode parent;
    [HideInInspector]
    public Rect nodeRect = new Rect(200, 200, 180, 80);
    
    public string nodeID;

    public int outputCount => children.Count;

    public abstract NodeState Execute(IBTContext context);

    // 可选：用于编辑器显示
    public virtual string GetNodeName()
    {
        return GetType().Name;
    }
    
    public Vector2 GetInputPortPos()
    {
        return new Vector2(
            nodeRect.x + nodeRect.width / 2,
            nodeRect.y
        );
    }
    
    public Vector2 GetOutputPortPos(int index)
    {
        float spacing = nodeRect.width / (outputCount + 1);
        float x = nodeRect.x + spacing * (index + 1);

        return new Vector2(
            x,
            nodeRect.y + nodeRect.height
        );
    }
}
