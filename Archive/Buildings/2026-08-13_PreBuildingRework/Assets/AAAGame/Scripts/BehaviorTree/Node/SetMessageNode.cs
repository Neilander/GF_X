
using UnityEngine;

[RequireContext(typeof(BasicEnemyContext))]
[CreateAssetMenu(menuName = "BehaviorTree/Leaf/SetMessage")]
public class SetMessageNode : AbstractNode
{
    [SerializeField]
    public string message = "";

    public override NodeState Execute(IBTContext context)
    {
        var ctx = context as BasicEnemyContext;
        if (ctx == null) return NodeState.Failure;

        ctx.outMessage = message;
        return NodeState.Success;
    }
}
