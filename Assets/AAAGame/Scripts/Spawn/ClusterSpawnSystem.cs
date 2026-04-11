using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// 簇生成系统
/// 按范围批量生成单位，保证单位在NavMesh上且不重叠
/// </summary>
public static class ClusterSpawnSystem
{
    private const float NavMeshSampleRadius = 12f;
    private const float MaxHorizontalSnapDistance = 1.2f;
    private const float FixedSpawnDistance = 0.7f;
    private const float FixedEdgeClearance = 0.2f;

    /// <summary>
    /// 校验生成条件
    /// </summary>
    /// <param name="center">中心位置</param>
    /// <param name="count">生成数量</param>
    /// <param name="radius">生成半径</param>
    /// <param name="minDistance">最小间距</param>
    /// <param name="spawnPositions">生成位置列表</param>
    /// <returns>是否可以生成</returns>
    public static bool ValidateSpawn(Vector3 center, int count, float radius, float minDistance, out List<Vector3> spawnPositions)
    {
        spawnPositions = new List<Vector3>();

        if (count <= 0 || radius <= 0 || minDistance <= 0)
        {
            return false;
        }

        // 在圆形区域内生成候选点（增加到10倍数量）
        int maxAttempts = count * 10;
        int navMeshFailCount = 0;
        int overlapFailCount = 0;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 candidate = GenerateDeterministicPointInCircle(center, radius, i, maxAttempts);

            // 投影到NavMesh（增加搜索半径到3米）
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                Vector3 spawnPos = hit.position;

                // 检查是否与已有生成位置重叠
                bool isOverlap = false;
                foreach (Vector3 existingPos in spawnPositions)
                {
                    if (Vector3.Distance(spawnPos, existingPos) < minDistance)
                    {
                        isOverlap = true;
                        overlapFailCount++;
                        break;
                    }
                }

                // 简化版：只检查与已有生成位置的重叠，不检查物理碰撞
                if (!isOverlap)
                {
                    spawnPositions.Add(spawnPos);

                    // 达到需求数量
                    if (spawnPositions.Count >= count)
                    {
                        return true;
                    }
                }
            }
            else
            {
                navMeshFailCount++;
            }
        }



        // 没有找到足够的位置
        return spawnPositions.Count >= count;
    }

    /// <summary>
    /// 生成簇单位（简化版）
    /// </summary>
    /// <param name="center">中心位置</param>
    /// <param name="count">生成数量</param>
    /// <param name="radius">生成半径</param>
    /// <param name="minDistance">最小间距</param>
    /// <param name="unitIndex">单位索引</param>
    /// <param name="side">阵营</param>
    /// <param name="brainType">AI类型</param>
    /// <returns>是否生成成功</returns>
    public static bool SpawnCluster(Vector3 center, int count, float radius, float minDistance,
        UnitType unitIndex, SideType side, BrainType brainType)
    {
        if (count <= 0 || radius <= 0f || minDistance <= 0f)
        {
            return false;
        }

        Debug.Log($"ClusterSpawnSystem: Spawning {count} units at {center}, unitType={unitIndex}, side={side}");
        if (!TryFindLegalNavMeshPoint(center, FixedEdgeClearance, out Vector3 legalCenter))
        {
            Debug.LogWarning($"ClusterSpawnSystem: spawn failed, center not legal on NavMesh. center={center}");
            return false;
        }

        List<Vector3> spawnPositions = new List<Vector3>(count);
        int maxAttempts = Mathf.Max(count * 120, 240);
        for (int i = 0; i < maxAttempts && spawnPositions.Count < count; i++)
        {
            Vector3 candidate = GenerateDeterministicPointInCircle(legalCenter, radius, i, maxAttempts);
            if (!TryFindLegalNavMeshPoint(candidate, FixedEdgeClearance, out Vector3 spawnPos))
            {
                continue;
            }

            bool isOverlap = false;
            for (int j = 0; j < spawnPositions.Count; j++)
            {
                if (Vector3.Distance(spawnPos, spawnPositions[j]) < FixedSpawnDistance)
                {
                    isOverlap = true;
                    break;
                }
            }

            if (!isOverlap)
            {
                spawnPositions.Add(spawnPos);
            }
        }

        if (spawnPositions.Count < count)
        {
            Debug.LogWarning($"ClusterSpawnSystem: spawn failed, legal points不足. need={count}, got={spawnPositions.Count}, center={center}, fixedDistance={FixedSpawnDistance:F2}, radius={radius:F2}");
            return false;
        }

        foreach (Vector3 pos in spawnPositions)
        {
            SoldierFactory.ShowSoldier(unitIndex, pos + Vector3.up * 0.05f, side, brainType);
        }
        return true;
    }

    private static bool TryFindLegalNavMeshPoint(Vector3 candidate, float edgeClearance, out Vector3 legalPoint)
    {
        legalPoint = Vector3.zero;

        if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, NavMeshSampleRadius, NavMesh.AllAreas))
        {
            return false;
        }

        Vector2 navXZ = new Vector2(navHit.position.x, navHit.position.z);
        Vector2 candidateXZ = new Vector2(candidate.x, candidate.z);
        if (Vector2.Distance(navXZ, candidateXZ) > MaxHorizontalSnapDistance)
        {
            return false;
        }

        if (NavMesh.FindClosestEdge(navHit.position, out NavMeshHit edgeHit, NavMesh.AllAreas))
        {
            if (edgeHit.distance < edgeClearance)
            {
                return false;
            }
        }

        legalPoint = navHit.position;
        return true;
    }

    /// <summary>
    /// 在圆形区域内生成确定性采样点（无随机）。
    /// </summary>
    private static Vector3 GenerateDeterministicPointInCircle(Vector3 center, float radius, int index, int total)
    {
        if (total <= 1 || index <= 0)
        {
            return center;
        }

        const float goldenAngle = 2.39996323f;
        float t = (index + 0.5f) / total;
        float distance = radius * Mathf.Sqrt(Mathf.Clamp01(t));
        float angle = index * goldenAngle;

        float x = center.x + Mathf.Cos(angle) * distance;
        float z = center.z + Mathf.Sin(angle) * distance;

        return new Vector3(x, center.y, z);
    }
}
