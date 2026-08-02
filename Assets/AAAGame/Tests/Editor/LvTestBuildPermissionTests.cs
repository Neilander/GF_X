using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LvTestBuildPermissionTests
{
    private const string LevelPrefabPath = "Assets/AAAGame/Prefabs/Entity/Level/LvTest.prefab";
    private const string PlayerBaseId = "Buil_FireHQ_Lv1";
    private const string PlayerStrongholdId = "SH_0_0";

    [Test]
    public void LvTest_PlayerStrongholdContainsFirefightingBaseForLv0Permissions()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LevelPrefabPath);
        Assert.That(root, Is.Not.Null, $"Cannot load {LevelPrefabPath}.");

        try
        {
            EntityPresetPoint basePoint = FindRequiredBuildingPoint(root, PlayerBaseId);
            Component manager = FindTileWorldCreatorManager(root);
            object configuration = GetRequiredFieldValue<object>(manager, "configuration");
            float cellSize = GetRequiredFieldValue<float>(configuration, "cellSize");
            object playerLayer = FindRequiredBlueprintLayer(configuration, PlayerStrongholdId);
            IEnumerable positions = GetRequiredFieldValue<IEnumerable>(playerLayer, "allPositions");

            Vector3 localPosition = manager.transform.InverseTransformPoint(basePoint.Position);
            int cellX = Mathf.RoundToInt(localPosition.x / cellSize);
            int cellY = Mathf.RoundToInt(localPosition.z / cellSize);
            bool belongsToPlayerStronghold = false;
            foreach (Vector2 position in positions)
            {
                if (Mathf.RoundToInt(position.x) == cellX && Mathf.RoundToInt(position.y) == cellY)
                {
                    belongsToPlayerStronghold = true;
                    break;
                }
            }

            Assert.That(
                belongsToPlayerStronghold,
                Is.True,
                $"{PlayerBaseId} at cell ({cellX},{cellY}) must belong to {PlayerStrongholdId} so its Lv1 milestone unlocks Lv0 construction options.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static EntityPresetPoint FindRequiredBuildingPoint(GameObject root, string buildingId)
    {
        EntityPresetPoint result = null;
        EntityPresetPoint[] points = root.GetComponentsInChildren<EntityPresetPoint>(true);
        for (int i = 0; i < points.Length; i++)
        {
            EntityPresetPoint point = points[i];
            if (point == null
                || point.PointType != EntityPresetPointType.Building
                || !string.Equals(point.Identifier, buildingId, StringComparison.Ordinal))
            {
                continue;
            }

            if (result != null)
                throw new InvalidOperationException($"LvTest contains duplicate {buildingId} preset points.");
            result = point;
        }

        return result ?? throw new InvalidOperationException($"LvTest is missing required player base {buildingId}.");
    }

    private static Component FindTileWorldCreatorManager(GameObject root)
    {
        Type managerType = Type.GetType("GiantGrey.TileWorldCreator.TileWorldCreatorManager, GiantGrey.TileWorldCreator")
                           ?? throw new InvalidOperationException("TileWorldCreatorManager type is unavailable.");
        return root.GetComponentInChildren(managerType, true)
               ?? throw new InvalidOperationException("LvTest has no TileWorldCreatorManager.");
    }

    private static object FindRequiredBlueprintLayer(object configuration, string layerName)
    {
        IList folders = GetRequiredFieldValue<IList>(configuration, "blueprintLayerFolders");
        for (int folderIndex = 0; folderIndex < folders.Count; folderIndex++)
        {
            object folder = folders[folderIndex];
            if (folder == null)
                continue;
            IList layers = GetRequiredFieldValue<IList>(folder, "blueprintLayers");
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                object layer = layers[layerIndex];
                if (layer != null
                    && string.Equals(GetRequiredFieldValue<string>(layer, "layerName"), layerName, StringComparison.Ordinal))
                {
                    return layer;
                }
            }
        }

        throw new InvalidOperationException($"LvTest is missing blueprint layer {layerName}.");
    }

    private static T GetRequiredFieldValue<T>(object instance, string fieldName)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException($"Field {fieldName} is missing on {instance.GetType().FullName}.");
        object value = field.GetValue(instance)
                       ?? throw new InvalidOperationException($"Field {fieldName} is null on {instance.GetType().FullName}.");
        if (value is T typed)
            return typed;
        throw new InvalidOperationException($"Field {fieldName} on {instance.GetType().FullName} is not {typeof(T).FullName}.");
    }
}
