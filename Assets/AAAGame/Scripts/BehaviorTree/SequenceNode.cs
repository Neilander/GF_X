using UnityEngine;

[CreateAssetMenu(menuName = "BehaviorTree/Sequence")]
public class SequenceNode : AbstractNode
{
    public override NodeState Execute(IBTContext context)
    {
        foreach (var child in children)
        {
            var result = child.Execute(context);
            if (result == NodeState.Failure)
                return NodeState.Failure;
        }

        return NodeState.Success;
    }
}