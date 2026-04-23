using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 批量单位生成系统。
/// 负责生成、预检和预览点位计算，并保证三者使用同一套规则。
/// </summary>
public static class ClusterSpawnSystem
{
    private const float NavMeshSampleRadius = 12f;
    private const float MaxHorizontalSnapDistance = 1.2f;
    private const float FixedSpawnDistance = 0.7f;
    private const float FixedEdgeClearance = 0.2f;

    /// <summary>
    /// 旧版校验接口，保留兼容。
    /// </summary>
    public static bool ValidateSpawn(Vector3 center, int count, float radius, float minDistance, out List<Vector3> spawnPositions)
    {
        spawnPositions = new List<Vector3>(count);

        if (count <= 0 || radius <= 0f || minDistance <= 0f)
        {
            return false;
        }

        int maxAttempts = count * 10;
        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 candidate = GenerateDeterministicPointInCircle(center, radius, i, maxAttempts);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 spawnPos = hit.position;
            bool isOverlap = false;
            for (int j = 0; j < spawnPositions.Count; j++)
            {
                if (Vector3.Distance(spawnPos, spawnPositions[j]) < minDistance)
                {
                    isOverlap = true;
                    break;
                }
            }

            if (isOverlap)
            {
                continue;
            }

            spawnPositions.Add(spawnPos);
            if (spawnPositions.Count >= count)
            {
                return true;
            }
        }

        return spawnPositions.Count >= count;
    }

    /// <summary>
    /// 实际生成整组单位。
    /// </summary>
    public static bool SpawnCluster(
        Vector3 center,
        int count,
        float radius,
        float minDistance,
        UnitType unitIndex,
        SideType side,
        BrainType brainType,
        string sourceBuildingInstanceId = null)
    {
        if (count <= 0 || radius <= 0f || minDistance <= 0f)
        {
            return false;
        }

        Debug.Log($"ClusterSpawnSystem: Spawning {count} units at {center}, unitType={unitIndex}, side={side}");

        List<Vector3> spawnPositions = new List<Vector3>(count);
        if (!TryGetPreviewSpawnPositions(center, count, radius, minDistance, spawnPositions))
        {
            Debug.LogWarning(
                $"ClusterSpawnSystem: spawn failed, legal points insufficient. need={count}, got={spawnPositions.Count}, center={center}, fixedDistance={FixedSpawnDistance:F2}, radius={radius:F2}");
            return false;
        }

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            Vector3 spawnPosition = spawnPositions[i] + Vector3.up * 0.05f;
            SoldierFactory.ShowSoldier(unitIndex, spawnPosition, side, brainType, sourceBuildingInstanceId);
        }

        return true;
    }

    /// <summary>
    /// 获取与真实生成一致的预览点位。
    /// </summary>
    public static bool TryGetPreviewSpawnPositions(
        Vector3 center,
        int count,
        float radius,
        float minDistance,
        List<Vector3> previewPositions)
    {
        if (previewPositions == null)
        {
            return false;
        }

        previewPositions.Clear();
        if (count <= 0 || radius <= 0f || minDistance <= 0f)
        {
            return false;
        }

        if (!TryFindLegalNavMeshPoint(center, FixedEdgeClearance, out Vector3 legalCenter))
        {
            return false;
        }

        CollectLegalSpawnPositions(legalCenter, count, radius, previewPositions);
        return previewPositions.Count >= count;
    }

    /// <summary>
    /// 预检当前位置是否可以生成整组单位。
    /// </summary>
    public static bool CanSpawnCluster(Vector3 center, int count, float radius, float minDistance)
    {
        List<Vector3> spawnPositions = new List<Vector3>(count);
        return TryGetPreviewSpawnPositions(center, count, radius, minDistance, spawnPositions);
    }

    private static void CollectLegalSpawnPositions(Vector3 legalCenter, int count, float radius, List<Vector3> spawnPositions)
    {
        spawnPositions.Clear();

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

        if (NavMesh.FindClosestEdge(navHit.position, out NavMeshHit edgeHit, NavMesh.AllAreas) && edgeHit.distance < edgeClearance)
        {
            return false;
        }

        legalPoint = navHit.position;
        return true;
    }

    /// <summary>
    /// 在圆形区域内生成确定性采样点。
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
