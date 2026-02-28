using UnityEngine;

[CreateAssetMenu(menuName = "BehaviorTree/Leaf/CheckPriority")]
public class CheckPriorityNode : AbstractNode
{
    [SerializeField]
    public int requiredPriority = 5;
    
    [SerializeField]
    public bool isGreater = true;

    public override NodeState Execute(IBTContext context)
    {
        if (context.priority >= requiredPriority)
            return NodeState.Success;

        return NodeState.Failure;
    }
}