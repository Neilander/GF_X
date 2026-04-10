using UnityEngine;

public static class SimpleTargeting
{
    public static CompCreature FindNearestEnemy(MAEntity self, float range)
    {
        Collider[] cols = Physics.OverlapSphere(self.transform.position, range);

        float best = float.MaxValue;
        CompCreature bestTarget = null;

        foreach (var c in cols)
        {
            if (c == null) continue;

            // 这里按你继承关系：MAEntity : CompCreature，所以目标用 CompCreature 就够
            var creature = c.GetComponentInParent<CompCreature>();
            if (creature == null) continue;
            if (creature == self) continue;
            if (!(creature is IEntityContext otherContext)) continue;
            if (!EntityCombatTeamHelper.IsEnemy(self, otherContext)) continue;

            float d = (creature.transform.position - self.transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestTarget = creature;
            }
        }

        return bestTarget;
    }
    
    // SimpleTargeting.cs 追加这个方法
    public static Vector3 GetSeparationForce(MAEntity self, float separationRadius)
    {
        Collider[] cols = Physics.OverlapSphere(self.transform.position, separationRadius);
        Vector3 force = Vector3.zero;
        int count = 0;

        foreach (var c in cols)
        {
            if (c == null) continue;

            var other = c.GetComponentInParent<MAEntity>();
            // 忽略自己，也忽略没有 MAEntity 组件的物体
            if (other == null || other == self) continue;

            // 只和相同阵营的实体产生排斥力（你也可以把条件去掉，让它排斥所有人）
            if (EntityCombatTeamHelper.IsAlly(self, other))
            {
                Vector3 diff = self.transform.position - other.transform.position;
                diff.y = 0; // 忽略Y轴，只在平面上散开
                float dist = diff.magnitude;

                if (dist > 0.01f && dist < separationRadius)
                {
                    // 距离越近，排斥力越大 (线性衰减)
                    force += diff.normalized * (1f - dist / separationRadius);
                    count++;
                }
            }
        }

        // 求平均排斥力
        return count > 0 ? force / count : Vector3.zero;
    }
    
    
    public static CompCreature FindNearestPlayer(MAEntity self, float range)
    {
        Collider[] cols = Physics.OverlapSphere(self.transform.position, range);
        float best = float.MaxValue;
        CompCreature bestTarget = null;

        foreach (var c in cols)
        {
            if (c == null) continue;

            var creature = c.GetComponentInParent<CompCreature>();
            if (creature == null || creature == self) continue;

            // 核心过滤：只找标签为 Player 的实体
            if (!creature.CompareTag("Player")) continue;

            float d = (creature.transform.position - self.transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestTarget = creature;
            }
        }

        return bestTarget;
    }
}