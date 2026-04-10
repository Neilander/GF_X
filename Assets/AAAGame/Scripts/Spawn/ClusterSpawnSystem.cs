using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// 簇生成系统
/// 按范围批量生成单位，保证单位在NavMesh上且不重叠
/// </summary>
public static class ClusterSpawnSystem
{
    private const float PreferredNavSampleRadius = 2f;
    private const float FallbackNavSampleRadius = 5f;
    private const float MaxVerticalSnap = 1.5f;
    private const float GroundProbeHeight = 200f;
    private static readonly int PreferredGroundLayerMask = LayerMask.GetMask("Ground");
    private static readonly int FallbackGroundLayerMask = LayerMask.GetMask("Ground", "Default", "Stronghold");

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
            Vector3 candidate = GenerateRandomPointInCircle(center, radius);

            if (TryProjectToHighestGround(candidate, out Vector3 projectedCandidate))
            {
                candidate = projectedCandidate;
            }

            // 投影到NavMesh（增加搜索半径到3米）
            if (TrySampleSpawnPosition(candidate, out Vector3 spawnPos))
            {
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
        Debug.Log($"ClusterSpawnSystem: Spawning {count} units at {center}, unitType={unitIndex}, side={side}");
        List<Vector3> spawnPositions = new List<Vector3>();

        // 直接生成单位，不做复杂验证
        for (int i = 0; i < count; i++)
        {
            // 在圆形区域内生成随机点
            Vector3 candidate = GenerateRandomPointInCircle(center, radius);

            if (TryProjectToHighestGround(candidate, out Vector3 projectedCandidate))
            {
                candidate = projectedCandidate;
            }

            // 投影到NavMesh
            if (TrySampleSpawnPosition(candidate, out Vector3 spawnPos))
            {
                if (spawnPos.y < center.y - MaxVerticalSnap)
                {
                    spawnPos = new Vector3(spawnPos.x, center.y, spawnPos.z);
                }
                spawnPositions.Add(spawnPos);
            }
            else
            {
                // 如果投影失败，直接使用候选点
                if (candidate.y < center.y - MaxVerticalSnap)
                {
                    candidate = new Vector3(candidate.x, center.y, candidate.z);
                }
                spawnPositions.Add(candidate);
            }
        }

        float minSpawnY = float.MaxValue;
        float maxSpawnY = float.MinValue;
        foreach (Vector3 p in spawnPositions)
        {
            if (p.y < minSpawnY) minSpawnY = p.y;
            if (p.y > maxSpawnY) maxSpawnY = p.y;
        }
        Debug.Log($"[CardSpawn] centerY={center.y:F2}, spawnYRange=[{minSpawnY:F2}, {maxSpawnY:F2}], count={spawnPositions.Count}");

        foreach (Vector3 pos in spawnPositions)
        {
            SoldierFactory.ShowSoldier(unitIndex, pos + Vector3.up, side, brainType);
        }
        return true;
    }

    /// <summary>
    /// 在圆形区域内生成随机点
    /// </summary>
    private static Vector3 GenerateRandomPointInCircle(Vector3 center, float radius)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Random.Range(0f, radius);

        float x = center.x + Mathf.Cos(angle) * distance;
        float z = center.z + Mathf.Sin(angle) * distance;

        return new Vector3(x, center.y, z);
    }

    private static bool TrySampleSpawnPosition(Vector3 candidate, out Vector3 spawnPos)
    {
        // Prefer local navmesh around the same floor.
        if (NavMesh.SamplePosition(candidate, out NavMeshHit nearHit, PreferredNavSampleRadius, NavMesh.AllAreas))
        {
            if (Mathf.Abs(nearHit.position.y - candidate.y) <= MaxVerticalSnap)
            {
                spawnPos = nearHit.position;
                return true;
            }
        }

        // Fallback to wider search but still avoid snapping to very different height layers.
        if (NavMesh.SamplePosition(candidate, out NavMeshHit farHit, FallbackNavSampleRadius, NavMesh.AllAreas))
        {
            if (Mathf.Abs(farHit.position.y - candidate.y) <= MaxVerticalSnap)
            {
                spawnPos = farHit.position;
                return true;
            }
        }

        spawnPos = Vector3.zero;
        return false;
    }

    private static bool TryProjectToHighestGround(Vector3 candidate, out Vector3 groundPosition)
    {
        groundPosition = Vector3.zero;

        int layerMask = PreferredGroundLayerMask != 0 ? PreferredGroundLayerMask : FallbackGroundLayerMask;
        if (layerMask == 0)
        {
            return false;
        }

        Ray ray = new Ray(new Vector3(candidate.x, candidate.y + GroundProbeHeight, candidate.z), Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(ray, GroundProbeHeight * 2f, layerMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            if (layerMask != FallbackGroundLayerMask && FallbackGroundLayerMask != 0)
            {
                hits = Physics.RaycastAll(ray, GroundProbeHeight * 2f, FallbackGroundLayerMask, QueryTriggerInteraction.Ignore);
            }

            if (hits == null || hits.Length == 0)
            {
                return false;
            }
        }

        bool found = false;
        float bestY = float.MinValue;
        float bestDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (!found || hit.point.y > bestY + 0.01f ||
                (Mathf.Abs(hit.point.y - bestY) <= 0.01f && hit.distance < bestDistance))
            {
                found = true;
                bestY = hit.point.y;
                bestDistance = hit.distance;
                groundPosition = hit.point;
            }
        }

        return found;
    }
}
