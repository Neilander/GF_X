public class RootNode : AbstractNode
{
    public override NodeState Execute(IBTContext context)
    {
        if (children.Count == 0)
            return NodeState.Failure;

        return children[0].Execute(context);
    }
}