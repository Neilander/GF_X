using UnityEngine;

[CreateAssetMenu(menuName = "BehaviorTree/Selector")]
public class SelectorNode : AbstractNode
{
    public override NodeState Execute(IBTContext context)
    {
        foreach (var child in children)
        {
            var result = child.Execute(context);
            if (result == NodeState.Success)
                return NodeState.Success;
        }

        return NodeState.Failure;
    }
}