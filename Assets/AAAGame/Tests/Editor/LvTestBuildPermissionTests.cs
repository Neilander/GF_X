using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LvTestBuildPermissionTests
{
    private const string LevelPrefabPath = "Assets/AAAGame/Prefabs/Entity/Level/LvTest.prefab";
    private const string UnexpectedBuildingId = "Buil_FireHQ_Lv1";

    [Test]
    public void LvTest_DoesNotContainInjectedFirefightingBase()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LevelPrefabPath);
        Assert.That(root, Is.Not.Null, $"Cannot load {LevelPrefabPath}.");

        try
        {
            EntityPresetPoint[] points = root.GetComponentsInChildren<EntityPresetPoint>(true);
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                Assert.That(
                    point == null
                    || point.PointType != EntityPresetPointType.Building
                    || !string.Equals(point.Identifier, UnexpectedBuildingId, System.StringComparison.Ordinal),
                    Is.True,
                    $"LvTest must not contain an injected {UnexpectedBuildingId} preset point.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
