using UnityEngine;

[RequireContext(typeof(BasicEnemyContext))]
[CreateAssetMenu(menuName = "BehaviorTree/Leaf/CheckHealth")]
public class CheckHealthNode : AbstractNode
{
    [SerializeField]
    public float threshold = 50f;
    
    [SerializeField]
    public bool isBelow = true; // true = 低于threshold返回Success，false = 高于

    public override NodeState Execute(IBTContext context)
    {
        var ctx = context as BasicEnemyContext;
        if (ctx == null) return NodeState.Failure;

        if (ctx.self == null) return NodeState.Failure;

        float health = ctx.self.health;

        bool condition = isBelow ? health < threshold : health >= threshold;
        return condition ? NodeState.Success : NodeState.Failure;
    }
}
