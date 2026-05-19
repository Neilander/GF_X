using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
    private const float MinAutoSpawnRadius = 0.8f;
    private const float NearbyCenterSearchStep = 0.8f;
    private const int NearbyCenterSearchRings = 7;
    private const int NearbyCenterSamplesPerRing = 12;

    public delegate bool SpawnCenterValidator(Vector3 center, float radius);

    /// <summary>
    /// 根据单位数量推导编队半径，避免卡牌资源各自配置生成半径。
    /// </summary>
    public static float CalculateAutoSpawnRadius(int count)
    {
        if (count <= 1)
        {
            return MinAutoSpawnRadius;
        }

        float radius = Mathf.Sqrt(count) * FixedSpawnDistance * 0.55f + FixedSpawnDistance * 0.35f;
        return Mathf.Max(MinAutoSpawnRadius, radius);
    }

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
                if ((spawnPos - spawnPositions[j]).sqrMagnitude < minDistance * minDistance)
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
        string sourceBuildingInstanceId = null,
        string sourceStrongholdId = null,
        bool avoidExistingAgents = false)
    {
        if (count <= 0 || radius <= 0f || minDistance <= 0f)
        {
            return false;
        }

        Debug.Log($"ClusterSpawnSystem: Spawning {count} units at {center}, unitType={unitIndex}, side={side}");

        List<Vector3> spawnPositions = new List<Vector3>(count);
        if (!TryGetPreviewSpawnPositions(center, count, radius, minDistance, spawnPositions, avoidExistingAgents))
        {
            Debug.LogWarning(
                $"ClusterSpawnSystem: spawn failed, legal points insufficient. need={count}, got={spawnPositions.Count}, center={center}, fixedDistance={FixedSpawnDistance:F2}, radius={radius:F2}");
            return false;
        }

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            Vector3 spawnPosition = spawnPositions[i] + Vector3.up * 0.05f;
            SoldierFactory.ShowSoldier(unitIndex, spawnPosition, side, brainType, sourceBuildingInstanceId, sourceStrongholdId);
        }

        return true;
    }

    /// <summary>
    /// 异步分帧生成整组单位，返回实际成功 Show 的单位数量。
    /// </summary>
    public static async UniTask<int> SpawnClusterAwait(
        Vector3 center,
        int count,
        float radius,
        float minDistance,
        UnitType unitIndex,
        SideType side,
        BrainType brainType,
        string sourceBuildingInstanceId = null,
        int yieldEveryUnits = 2,
        Func<bool> keepSpawningPredicate = null,
        string sourceStrongholdId = null,
        bool avoidExistingAgents = false)
    {
        if (count <= 0 || radius <= 0f || minDistance <= 0f)
        {
            return 0;
        }

        List<Vector3> spawnPositions = new List<Vector3>(count);
        if (!TryGetPreviewSpawnPositions(center, count, radius, minDistance, spawnPositions, avoidExistingAgents))
        {
            Debug.LogWarning(
                $"ClusterSpawnSystem: spawn failed, legal points insufficient. need={count}, got={spawnPositions.Count}, center={center}, fixedDistance={FixedSpawnDistance:F2}, radius={radius:F2}");
            return 0;
        }

        int spawnedCount = 0;
        int batchSize = Mathf.Max(1, yieldEveryUnits);
        for (int i = 0; i < spawnPositions.Count; i++)
        {
            if (keepSpawningPredicate != null && !keepSpawningPredicate())
            {
                break;
            }

            Vector3 spawnPosition = spawnPositions[i] + Vector3.up * 0.05f;
            bool shown = await SoldierFactory.ShowSoldierAwait(
                unitIndex,
                spawnPosition,
                side,
                brainType,
                sourceBuildingInstanceId,
                sourceStrongholdId,
                keepSpawningPredicate);

            if (shown)
            {
                spawnedCount++;
            }

            if ((i + 1) % batchSize == 0)
            {
                await UniTask.Yield();
            }
        }

        return spawnedCount;
    }

    /// <summary>
    /// 获取与真实生成一致的预览点位。
    /// </summary>
    public static bool TryGetPreviewSpawnPositions(
        Vector3 center,
        int count,
        float radius,
        float minDistance,
        List<Vector3> previewPositions,
        bool avoidExistingAgents = false)
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

        CollectLegalSpawnPositions(legalCenter, count, radius, previewPositions, avoidExistingAgents);
        return previewPositions.Count >= count;
    }

    /// <summary>
    /// 获取可部署编队点位。若目标点不可用，会在附近按确定性螺旋搜索合法中心点。
    /// </summary>
    public static bool TryResolvePreviewSpawnPositions(
        Vector3 preferredCenter,
        int count,
        float radius,
        float minDistance,
        SpawnCenterValidator centerValidator,
        List<Vector3> previewPositions,
        out Vector3 resolvedCenter)
    {
        resolvedCenter = preferredCenter;

        if (previewPositions == null)
        {
            return false;
        }

        previewPositions.Clear();
        if (count <= 0 || radius <= 0f || minDistance <= 0f)
        {
            return false;
        }

        int totalCandidates = 1 + NearbyCenterSearchRings * NearbyCenterSamplesPerRing;
        for (int i = 0; i < totalCandidates; i++)
        {
            Vector3 candidate = GenerateNearbyCenterCandidate(preferredCenter, radius, i);
            if (!TryFindLegalNavMeshPoint(candidate, FixedEdgeClearance, out Vector3 legalCenter))
            {
                continue;
            }

            if (centerValidator != null && !centerValidator(legalCenter, radius))
            {
                continue;
            }

            CollectLegalSpawnPositions(legalCenter, count, radius, previewPositions, true);
            if (previewPositions.Count >= count)
            {
                resolvedCenter = legalCenter;
                return true;
            }
        }

        previewPositions.Clear();
        return false;
    }

    /// <summary>
    /// 预检当前位置是否可以生成整组单位。
    /// </summary>
    public static bool CanSpawnCluster(Vector3 center, int count, float radius, float minDistance, bool avoidExistingAgents = false)
    {
        List<Vector3> spawnPositions = new List<Vector3>(count);
        return TryGetPreviewSpawnPositions(center, count, radius, minDistance, spawnPositions, avoidExistingAgents);
    }

    private static void CollectLegalSpawnPositions(Vector3 legalCenter, int count, float radius, List<Vector3> spawnPositions, bool avoidExistingAgents)
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

            if (avoidExistingAgents && IsBlockedByExistingAgent(spawnPos))
            {
                continue;
            }

            bool isOverlap = false;
            for (int j = 0; j < spawnPositions.Count; j++)
            {
                if ((spawnPos - spawnPositions[j]).sqrMagnitude < FixedSpawnDistance * FixedSpawnDistance)
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
        if ((navXZ - candidateXZ).sqrMagnitude > MaxHorizontalSnapDistance * MaxHorizontalSnapDistance)
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

    private static bool IsBlockedByExistingAgent(Vector3 position)
    {
        if (!GroupMoveManager.HasInstance || GroupMoveManager.Instance.Coordinator == null)
        {
            return false;
        }

        foreach (var pair in GroupMoveManager.Instance.Coordinator.AllAgents)
        {
            GroupMoveCoordinator.AgentData agent = pair.Value;
            if (agent.IgnoreAgentCollision)
            {
                continue;
            }

            Vector2 agentXZ = new Vector2(agent.Position.x, agent.Position.z);
            Vector2 posXZ = new Vector2(position.x, position.z);
            float requiredDistance = Mathf.Max(FixedSpawnDistance, agent.Radius + FixedSpawnDistance * 0.5f);
            if ((agentXZ - posXZ).sqrMagnitude < requiredDistance * requiredDistance)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector3 GenerateNearbyCenterCandidate(Vector3 center, float formationRadius, int index)
    {
        if (index <= 0)
        {
            return center;
        }

        int adjusted = index - 1;
        int ring = adjusted / NearbyCenterSamplesPerRing + 1;
        int ringIndex = adjusted % NearbyCenterSamplesPerRing;
        float angleOffset = ring * 0.37f;
        float angle = Mathf.PI * 2f * ringIndex / NearbyCenterSamplesPerRing + angleOffset;
        float distance = ring * Mathf.Max(NearbyCenterSearchStep, formationRadius * 0.45f);
        return center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
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



