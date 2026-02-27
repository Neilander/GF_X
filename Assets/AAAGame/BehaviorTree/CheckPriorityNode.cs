using UnityEngine;

[CreateAssetMenu(menuName = "BehaviorTree/Leaf/CheckPriority")]
public class CheckPriorityNode : AbstractNode
{
    public int requiredPriority = 5;

    public override NodeState Execute(IBTContext context)
    {
        if (context.Priority >= requiredPriority)
            return NodeState.Success;

        return NodeState.Failure;
    }
}