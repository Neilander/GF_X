using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Steering Behaviors 工具类：纯数学计算，所有方法为静态。
/// 所有输入输出都是 Vector3（世界坐标），不依赖 MonoBehaviour。
///
/// 核心力：
/// - Seek：朝目标点移动
/// - Separation：与周围同类保持距离
/// - AvoidEntity：强力避让指定实体（如玩家）
/// </summary>
public static class SteeringMovement
{
    /// <summary>
    /// 趋向力：朝目标点移动，返回归一化方向。
    /// 到达 arriveRadius 内时线性减速。
    /// </summary>
    public static Vector3 Seek(Vector3 position, Vector3 target, float arriveRadius = 0.5f)
    {
        Vector3 desired = target - position;
        desired.y = 0f;
        float dist = desired.magnitude;

        if (dist < 0.01f) return Vector3.zero;

        Vector3 dir = desired / dist; // normalize

        if (arriveRadius > 0f && dist < arriveRadius)
        {
            // 线性减速
            return dir * (dist / arriveRadius);
        }

        return dir;
    }

    /// <summary>
    /// 分离力：与附近的 neighbors 保持距离。
    /// 距离越近，排斥力越强（反比关系）。
    /// </summary>
    /// <param name="position">自身位置</param>
    /// <param name="neighbors">附近单位的位置列表</param>
    /// <param name="separationRadius">分离检测半径</param>
    /// <returns>分离力向量（未归一化，强度与距离成反比）</returns>
    public static Vector3 Separation(Vector3 position, IList<Vector3> neighbors, float separationRadius)
    {
        Vector3 force = Vector3.zero;
        int count = 0;

        for (int i = 0; i < neighbors.Count; i++)
        {
            Vector3 diff = position - neighbors[i];
            diff.y = 0f;
            float dist = diff.magnitude;

            if (dist > separationRadius) continue;

            if (dist < 0.001f)
            {
                // 完全重叠：给一个基于 index 的确定性方向推开
                float angle = i * Mathf.PI * 2f / neighbors.Count;
                force += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                count++;
                continue;
            }

            // 力与距离成反比：越近越强
            force += diff.normalized * (1f - dist / separationRadius);
            count++;
        }

        if (count > 0)
            force /= count;

        return force;
    }

    /// <summary>
    /// 强力避让指定实体（通常是玩家）。
    /// 与 Separation 类似但力度更大，且有最小推离距离。
    /// </summary>
    public static Vector3 AvoidEntity(Vector3 position, Vector3 entityToAvoid, float avoidRadius, float strength = 2f)
    {
        Vector3 diff = position - entityToAvoid;
        diff.y = 0f;
        float dist = diff.magnitude;

        if (dist < 0.001f)
        {
            // 完全重叠时给一个随机方向
            return new Vector3(1f, 0f, 0f) * strength;
        }

        if (dist > avoidRadius) return Vector3.zero;

        // 力度 = strength * (1 - dist/radius)，越近越强
        return diff.normalized * strength * (1f - dist / avoidRadius);
    }

    /// <summary>
    /// 合并多个力，限制最终速度的最大值。
    /// </summary>
    public static Vector3 ClampForce(Vector3 totalForce, float maxSpeed)
    {
        totalForce.y = 0f;
        if (totalForce.sqrMagnitude > maxSpeed * maxSpeed)
        {
            return totalForce.normalized * maxSpeed;
        }
        return totalForce;
    }

    /// <summary>
    /// 收集附近同阵营单位的位置（排除自己）。
    /// 用于 Sim 测试环境。
    /// </summary>
    public static List<Vector3> CollectNeighborPositions(
        IEntityContext self,
        IList<IEntityContext> allEntities,
        float radius)
    {
        var positions = new List<Vector3>();
        Vector3 myPos = self.Position;

        for (int i = 0; i < allEntities.Count; i++)
        {
            var other = allEntities[i];
            if (other == self || !other.Alive) continue;

            float dist = Vector3.Distance(myPos, other.Position);
            if (dist <= radius)
            {
                positions.Add(other.Position);
            }
        }

        return positions;
    }

    /// <summary>
    /// 收集同阵营邻居位置。
    /// </summary>
    public static List<Vector3> CollectSameSideNeighborPositions(
        IEntityContext self,
        IList<IEntityContext> allEntities,
        float radius)
    {
        var positions = new List<Vector3>();
        Vector3 myPos = self.Position;

        for (int i = 0; i < allEntities.Count; i++)
        {
            var other = allEntities[i];
            if (other == self || !other.Alive) continue;
            if (other.Side != self.Side) continue;

            float dist = Vector3.Distance(myPos, other.Position);
            if (dist <= radius)
            {
                positions.Add(other.Position);
            }
        }

        return positions;
    }
}
