using UnityEngine;

public class EnemyAIBrain : IControlBrain, ITickBrain
{
    public Vector2 Move { get; private set; }
    public bool Attack { get; private set; }
    // ... 忽略 Skill 布尔值 ...
    public bool Skill1 { get; private set; }
    public bool Skill2 { get; private set; }
    public bool Skill3 { get; private set; }

    public float AggroRange = 6f;
    public float AttackRange = 1.6f;
    public float ForgetRange = 8f;

    // === 新增：分离力参数 ===
    public float SeparationRadius = 1.5f;
    public float SeparationWeight = 1.2f;

    private CompCreature _target;

    public void Tick(MAEntity self, float dt)
    {
        Attack = false;
        Skill1 = Skill2 = Skill3 = false;

        _target = self.targetComp?.CurrentTarget;

        Vector3 desiredMove = Vector3.zero;

        if (_target != null)
        {
            Vector3 to = _target.transform.position - self.transform.position;
            float d2 = to.sqrMagnitude;

            if (d2 > AttackRange * AttackRange)
                desiredMove = new Vector3(to.x, 0, to.z).normalized;
            else
                Attack = true;
        }

        // 获取排斥力并混合
        Vector3 separation = SimpleTargeting.GetSeparationForce(self, SeparationRadius);
        Vector3 finalMove = desiredMove + separation * SeparationWeight;

        if (finalMove.sqrMagnitude > 0.01f && !Attack)
        {
            Move = new Vector2(finalMove.x, finalMove.z).normalized;
        }
        else
        {
            Move = Vector2.zero;
        }
    }
}