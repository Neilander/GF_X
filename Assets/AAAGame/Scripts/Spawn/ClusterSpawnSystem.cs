using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 批量单位生成系统。
/// 负责生成、预检和预览点位计算，并保证三者使用同一套规则。
/// </summary>
public static class ClusterSpawnSystem
{
    private static readonly Fix64 MaxHorizontalSnapDistance = Fix64.FromRaw(4916);
    private static readonly Fix64 FixedSpawnDistance = Fix64.FromRaw(2868);
    private static readonly Fix64 FixedEdgeClearance = Fix64.FromRaw(820);
    private static readonly Fix64 MinAutoSpawnRadius = Fix64.FromRaw(3277);
    private static readonly Fix64 NearbyCenterSearchStep = Fix64.FromRaw(3277);
    private const int NearbyCenterSearchRings = 7;
    private const int NearbyCenterSamplesPerRing = 12;

    public delegate bool SpawnCenterValidator(Vector3 center, float radius);

    public static int ResolveAgentTypeId(UnitType unitType)
    {
        return AgentTypeHelper.ResolveNavAgentTypeId(unitType);
    }

    /// <summary>
    /// 根据单位数量推导编队半径，避免卡牌资源各自配置生成半径。
    /// </summary>
    public static float CalculateAutoSpawnRadius(int count)
    {
        return (float)CalculateAutoSpawnRadiusFixed(count);
    }

    public static Fix64 CalculateAutoSpawnRadiusFixed(int count)
    {
        if (count <= 1)
            return MinAutoSpawnRadius;

        Fix64 spawnDistance = FixedSpawnDistance;
        Fix64 radius = Fix64.Sqrt((Fix64)count) * spawnDistance * Fix64.FromRaw(2253)
                       + spawnDistance * Fix64.FromRaw(1434);
        return Fix64.Max(MinAutoSpawnRadius, radius);
    }

    /// <summary>
    /// 旧版校验接口，保留兼容。
    /// </summary>
    public static bool ValidateSpawn(Vector3 center, int count, float radius, float minDistance, out List<Vector3> spawnPositions)
    {
        spawnPositions = new List<Vector3>(Mathf.Max(0, count));
        if (!ValidateInputs(count, radius, minDistance))
            return false;

        var fixedPositions = new List<FixVector2>(count);
        FixVector2 fixedCenter = ToFixed(center);
        Fix64 fixedRadius = (Fix64)radius;
        Fix64 fixedMinDistance = (Fix64)minDistance;
        int maxAttempts = checked(count * 10);
        for (int i = 0; i < maxAttempts && fixedPositions.Count < count; i++)
        {
            FixVector2 candidate = GenerateDeterministicPointInCircleFixed(fixedCenter, fixedRadius, i, maxAttempts);
            if (!TryFindLegalNavigationPointFixed(candidate, FixedEdgeClearance, 0, out FixVector2 spawnPos))
                continue;
            if (OverlapsSpawnPosition(fixedPositions, spawnPos, fixedMinDistance))
                continue;
            fixedPositions.Add(spawnPos);
        }

        CopyToUnityPositions(fixedPositions, spawnPositions);
        return fixedPositions.Count >= count;
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
        bool avoidExistingAgents = false,
        int unitLevel = 1,
        Action<LogicEntityId> spawned = null,
        Action<EntityParams> configureParams = null)
    {
        return SpawnClusterFixed(
            ToFixed(center),
            count,
            (Fix64)radius,
            (Fix64)minDistance,
            unitIndex,
            side,
            brainType,
            sourceBuildingInstanceId,
            sourceStrongholdId,
            avoidExistingAgents,
            unitLevel,
            spawned,
            configureParams);
    }

    public static bool SpawnClusterFixed(
        FixVector2 center,
        int count,
        Fix64 radius,
        Fix64 minDistance,
        UnitType unitIndex,
        SideType side,
        BrainType brainType,
        string sourceBuildingInstanceId = null,
        string sourceStrongholdId = null,
        bool avoidExistingAgents = false,
        int unitLevel = 1,
        Action<LogicEntityId> spawned = null,
        Action<EntityParams> configureParams = null,
        bool allowBlockedFormationCenter = false)
    {
        if (count <= 0 || radius <= Fix64.Zero || minDistance <= Fix64.Zero)
            return false;

        long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var spawnPositions = new List<FixVector2>(count);
        int agentTypeId = ResolveAgentTypeId(unitIndex);
        bool hasSpawnPositions = TryGetSpawnPositionsFixed(
            center,
            count,
            radius,
            minDistance,
            spawnPositions,
            avoidExistingAgents,
            agentTypeId,
            allowBlockedFormationCenter);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.ClusterSpawnPositions,
            System.Diagnostics.Stopwatch.GetTimestamp() - stageStartTicks);
        if (!hasSpawnPositions)
        {
            Debug.LogWarning(
                $"ClusterSpawnSystem: spawn failed, legal points insufficient. need={count}, got={spawnPositions.Count}, centerRaw=({center.x.RawValue},{center.y.RawValue}), radiusRaw={radius.RawValue}");
            return false;
        }

        stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < spawnPositions.Count; i++)
        {
            LogicEntityId entityId = SoldierFactory.ShowCurrentBattleTroopFixed(
                unitIndex,
                spawnPositions[i],
                0.05f,
                side,
                brainType,
                sourceBuildingInstanceId,
                sourceStrongholdId,
                configureParams,
                unitLevel);
            if (!entityId.IsValid)
                throw new InvalidOperationException($"ClusterSpawnSystem failed to request unit {i}. unit={unitIndex}.");
            spawned?.Invoke(entityId);
        }
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.ClusterSpawnUnits,
            System.Diagnostics.Stopwatch.GetTimestamp() - stageStartTicks);

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
        List<Vector3> previewPositions,
        bool avoidExistingAgents = false)
    {
        return TryGetPreviewSpawnPositions(
            center,
            count,
            radius,
            minDistance,
            previewPositions,
            avoidExistingAgents,
            0);
    }

    public static bool TryGetPreviewSpawnPositions(
        Vector3 center,
        int count,
        float radius,
        float minDistance,
        List<Vector3> previewPositions,
        bool avoidExistingAgents,
        int agentTypeId,
        bool allowBlockedFormationCenter = false)
    {
        if (previewPositions == null)
            throw new ArgumentNullException(nameof(previewPositions));

        previewPositions.Clear();
        if (!ValidateInputs(count, radius, minDistance))
            return false;

        var fixedPositions = new List<FixVector2>(count);
        bool success = TryGetFixedSpawnPositions(
            center,
            count,
            radius,
            minDistance,
            fixedPositions,
            avoidExistingAgents,
            agentTypeId,
            allowBlockedFormationCenter);
        CopyToUnityPositions(fixedPositions, previewPositions);
        return success;
    }

    private static bool TryGetFixedSpawnPositions(
        Vector3 center,
        int count,
        float radius,
        float minDistance,
        List<FixVector2> spawnPositions,
        bool avoidExistingAgents,
        int agentTypeId,
        bool allowBlockedFormationCenter = false)
    {
        return TryGetSpawnPositionsFixed(
            ToFixed(center),
            count,
            (Fix64)radius,
            (Fix64)minDistance,
            spawnPositions,
            avoidExistingAgents,
            agentTypeId,
            allowBlockedFormationCenter);
    }

    public static bool TryGetSpawnPositionsFixed(
        FixVector2 center,
        int count,
        Fix64 radius,
        Fix64 minDistance,
        List<FixVector2> spawnPositions,
        bool avoidExistingAgents,
        int agentTypeId,
        bool allowBlockedFormationCenter = false)
    {
        if (spawnPositions == null)
            throw new ArgumentNullException(nameof(spawnPositions));

        spawnPositions.Clear();
        if (count <= 0 || radius <= Fix64.Zero || minDistance <= Fix64.Zero)
            return false;

        FixVector2 legalCenter = center;
        if (!allowBlockedFormationCenter
            && !TryFindLegalNavigationPointFixed(
                center,
                FixedEdgeClearance,
                agentTypeId,
                out legalCenter))
        {
            return false;
        }

        CollectLegalSpawnPositionsFixed(
            legalCenter,
            count,
            radius,
            spawnPositions,
            avoidExistingAgents,
            agentTypeId);
        return spawnPositions.Count >= count;
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
        return TryResolvePreviewSpawnPositions(
            preferredCenter,
            count,
            radius,
            minDistance,
            centerValidator,
            previewPositions,
            out resolvedCenter,
            0);
    }

    public static bool TryResolvePreviewSpawnPositions(
        Vector3 preferredCenter,
        int count,
        float radius,
        float minDistance,
        SpawnCenterValidator centerValidator,
        List<Vector3> previewPositions,
        out Vector3 resolvedCenter,
        int agentTypeId,
        bool allowBlockedFormationCenter = false)
    {
        resolvedCenter = preferredCenter;

        if (previewPositions == null)
            throw new ArgumentNullException(nameof(previewPositions));

        previewPositions.Clear();
        if (!ValidateInputs(count, radius, minDistance))
            return false;

        FixVector2 fixedPreferredCenter = ToFixed(preferredCenter);
        Fix64 fixedRadius = (Fix64)radius;
        var fixedPositions = new List<FixVector2>(count);
        int totalCandidates = 1 + NearbyCenterSearchRings * NearbyCenterSamplesPerRing;
        for (int i = 0; i < totalCandidates; i++)
        {
            FixVector2 candidate = GenerateNearbyCenterCandidateFixed(fixedPreferredCenter, fixedRadius, i);
            FixVector2 legalCenter = candidate;
            if (!(allowBlockedFormationCenter && i == 0)
                && !TryFindLegalNavigationPointFixed(
                    candidate,
                    FixedEdgeClearance,
                    agentTypeId,
                    out legalCenter))
            {
                continue;
            }

            Vector3 unityLegalCenter = ToUnity(legalCenter, preferredCenter.y);
            if (centerValidator != null && !centerValidator(unityLegalCenter, radius))
                continue;

            CollectLegalSpawnPositionsFixed(
                legalCenter,
                count,
                fixedRadius,
                fixedPositions,
                true,
                agentTypeId);
            if (fixedPositions.Count >= count)
            {
                CopyToUnityPositions(fixedPositions, previewPositions);
                resolvedCenter = unityLegalCenter;
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
        return CanSpawnCluster(center, count, radius, minDistance, avoidExistingAgents, 0);
    }

    public static bool CanSpawnCluster(
        Vector3 center,
        int count,
        float radius,
        float minDistance,
        bool avoidExistingAgents,
        int agentTypeId,
        bool allowBlockedFormationCenter = false)
    {
        var spawnPositions = new List<Vector3>(count);
        return TryGetPreviewSpawnPositions(
            center,
            count,
            radius,
            minDistance,
            spawnPositions,
            avoidExistingAgents,
            agentTypeId,
            allowBlockedFormationCenter);
    }

    private static void CollectLegalSpawnPositionsFixed(
        FixVector2 legalCenter,
        int count,
        Fix64 radius,
        List<FixVector2> spawnPositions,
        bool avoidExistingAgents,
        int agentTypeId)
    {
        spawnPositions.Clear();

        int maxAttempts = Math.Max(checked(count * 120), 240);
        for (int i = 0; i < maxAttempts && spawnPositions.Count < count; i++)
        {
            FixVector2 candidate = GenerateDeterministicPointInCircleFixed(legalCenter, radius, i, maxAttempts);
            if (!TryFindLegalNavigationPointFixed(
                    candidate,
                    FixedEdgeClearance,
                    agentTypeId,
                    out FixVector2 spawnPos))
            {
                continue;
            }

            if (avoidExistingAgents && IsBlockedByExistingAgentFixed(spawnPos))
                continue;

            if (!OverlapsSpawnPosition(spawnPositions, spawnPos, FixedSpawnDistance))
                spawnPositions.Add(spawnPos);
        }
    }

    private static bool TryFindLegalNavigationPointFixed(
        FixVector2 candidate,
        Fix64 edgeClearance,
        int agentTypeId,
        out FixVector2 legalPoint)
    {
        return FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPointFixed(
            candidate,
            agentTypeId,
            MaxHorizontalSnapDistance,
            edgeClearance,
            out legalPoint);
    }

    private static bool IsBlockedByExistingAgentFixed(FixVector2 position)
    {
        return FlowFieldCrowdMovementSystem.IsPositionOccupiedByAgentFixed(
            position,
            FixedSpawnDistance);
    }

    private static FixVector2 GenerateNearbyCenterCandidateFixed(FixVector2 center, Fix64 formationRadius, int index)
    {
        if (index <= 0)
            return center;

        int adjusted = index - 1;
        int ring = adjusted / NearbyCenterSamplesPerRing + 1;
        int ringIndex = adjusted % NearbyCenterSamplesPerRing;
        Fix64 angleOffset = (Fix64)ring * Fix64.FromRaw(1516);
        Fix64 angle = Fix64.PI * (Fix64)2 * (Fix64)ringIndex / (Fix64)NearbyCenterSamplesPerRing + angleOffset;
        Fix64 distance = (Fix64)ring * Fix64.Max(NearbyCenterSearchStep, formationRadius * Fix64.FromRaw(1844));
        return center + new FixVector2(Fix64.Cos(angle) * distance, Fix64.Sin(angle) * distance);
    }

    /// <summary>
    /// 在圆形区域内生成确定性采样点。
    /// </summary>
    private static FixVector2 GenerateDeterministicPointInCircleFixed(
        FixVector2 center,
        Fix64 radius,
        int index,
        int total)
    {
        if (total <= 1 || index <= 0)
            return center;

        Fix64 t = ((Fix64)index + Fix64.FromRaw(2048)) / (Fix64)total;
        Fix64 distance = radius * Fix64.Sqrt(Fix64.Clamp(t, Fix64.Zero, Fix64.One));
        Fix64 angle = (Fix64)index * Fix64.FromRaw(9831);
        return center + new FixVector2(Fix64.Cos(angle) * distance, Fix64.Sin(angle) * distance);
    }

    private static bool OverlapsSpawnPosition(
        List<FixVector2> spawnPositions,
        FixVector2 candidate,
        Fix64 minimumDistance)
    {
        Fix64 minimumDistanceSq = minimumDistance * minimumDistance;
        for (int i = 0; i < spawnPositions.Count; i++)
        {
            if (FixVector2.SqrMagnitude(candidate - spawnPositions[i]) < minimumDistanceSq)
                return true;
        }

        return false;
    }

    private static bool ValidateInputs(int count, float radius, float minDistance)
    {
        return count > 0
               && radius > 0f
               && minDistance > 0f
               && !float.IsNaN(radius)
               && !float.IsInfinity(radius)
               && !float.IsNaN(minDistance)
               && !float.IsInfinity(minDistance);
    }

    private static FixVector2 ToFixed(Vector3 position)
    {
        if (float.IsNaN(position.x)
            || float.IsInfinity(position.x)
            || float.IsNaN(position.z)
            || float.IsInfinity(position.z))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Spawn position must contain finite XZ coordinates.");
        }
        return new FixVector2((Fix64)position.x, (Fix64)position.z);
    }

    private static Vector3 ToUnity(FixVector2 position, float y)
    {
        return new Vector3((float)position.x, y, (float)position.y);
    }

    private static void CopyToUnityPositions(List<FixVector2> source, List<Vector3> destination)
    {
        destination.Clear();
        for (int i = 0; i < source.Count; i++)
            destination.Add(ToUnity(source[i], 0f));
    }
}



