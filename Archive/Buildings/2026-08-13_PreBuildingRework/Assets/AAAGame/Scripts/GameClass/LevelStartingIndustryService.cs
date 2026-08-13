using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class LevelStartingIndustryService
{
    private static readonly Dictionary<string, bool> s_HasInitialBaseByLevel = new(StringComparer.Ordinal);

    public static async UniTask<bool> HasInitialBaseAsync(string careerLevelIdentifier, bool isVariableExperiment)
    {
        string runtimeLevelIdentifier = CareerRunSettings.ResolveRuntimeLevelIdentifier(
            careerLevelIdentifier,
            isVariableExperiment);
        if (s_HasInitialBaseByLevel.TryGetValue(runtimeLevelIdentifier, out bool cached))
            return cached;

        if (GF.Resource == null)
            throw new InvalidOperationException("Level starting industry inspection requires the resource component.");

        LevelTable runtimeLevel = CareerConfigRuntime.GetLevelRequired(runtimeLevelIdentifier);
        string assetPath = UtilityBuiltin.AssetsPath.GetEntityPath(runtimeLevel.PrefabPath);
        GameObject prefab = await GF.Resource.LoadAssetAwait<GameObject>(assetPath);
        EntityPresetPoint[] points = prefab.GetComponentsInChildren<EntityPresetPoint>(true);
        int initialBaseCount = 0;
        for (int i = 0; i < points.Length; i++)
        {
            EntityPresetPoint point = points[i];
            if (point.PointType == EntityPresetPointType.Building
                && EntityPresetPoint.IsInitialBaseIdentifier(point.Identifier))
            {
                initialBaseCount++;
            }
        }

        if (initialBaseCount > 1)
        {
            throw new InvalidOperationException(
                $"Runtime level '{runtimeLevelIdentifier}' contains {initialBaseCount} initial base placeholders; expected at most one.");
        }

        bool hasInitialBase = initialBaseCount == 1;
        s_HasInitialBaseByLevel.Add(runtimeLevelIdentifier, hasInitialBase);
        return hasInitialBase;
    }
}
