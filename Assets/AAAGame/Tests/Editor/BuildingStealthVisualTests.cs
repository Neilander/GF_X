using NUnit.Framework;
using System.Reflection;
using AAAGame.MiniMap;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[TestFixture]
public sealed class BuildingStealthVisualTests
{
    private const string TrapPrefabPath = "Assets/AAAGame/Prefabs/Entity/Building/Buil_Trap_Lv1.prefab";
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.BuildBeforeInvade, nameof(BuildingStealthVisualTests));
    }

    [Test]
    public void LocalTrapStealth_ChangesOneRuntimeMaterialBetweenTransparentAndOpaque()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPrefabPath);
        Assert.NotNull(prefab, $"Missing trap prefab: {TrapPrefabPath}");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Material originalMaterial = null;
        try
        {
            BuildingEntity building = instance.GetComponent<BuildingEntity>()
                                      ?? instance.AddComponent<BuildingEntity>();
            Renderer renderer = instance.GetComponentInChildren<Renderer>(true);
            Assert.NotNull(building, "Runtime building view was not created.");
            Assert.NotNull(renderer, "Trap prefab has no visual renderer.");

            originalMaterial = renderer.sharedMaterial;
            Assert.NotNull(originalMaterial);
            Assert.AreEqual(0f, originalMaterial.GetFloat(SurfaceId));

            building.OwnerFactionID = EntitySideHelper.PlayerFactionId;
            building.SetPermanentStealthVisibility(true);

            Material stealthMaterial = renderer.sharedMaterial;
            Assert.True(renderer.enabled, "A local stealthed trap must remain visible.");
            Assert.AreNotSame(originalMaterial, stealthMaterial);
            Assert.AreEqual(1f, stealthMaterial.GetFloat(SurfaceId));
            Assert.AreEqual((int)RenderQueue.Transparent, stealthMaterial.renderQueue);
            Assert.True(stealthMaterial.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));

            var propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            Assert.AreEqual(0.35f, propertyBlock.GetColor(BaseColorId).a, 0.001f);

            building.SetPermanentStealthVisibility(false);

            Assert.True(renderer.enabled);
            Assert.AreSame(stealthMaterial, renderer.sharedMaterial);
            Assert.AreEqual(0f, stealthMaterial.GetFloat(SurfaceId));
            Assert.AreEqual(originalMaterial.renderQueue, stealthMaterial.renderQueue);
            Assert.False(stealthMaterial.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
            renderer.GetPropertyBlock(propertyBlock);
            Assert.AreEqual(1f, propertyBlock.GetColor(BaseColorId).a, 0.001f);
        }
        finally
        {
            DestroyInstanceAndRuntimeMaterial(instance, originalMaterial);
        }
    }

    [Test]
    public void EnemyTrapReveal_PreservesOwnershipColor()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPrefabPath);
        Assert.NotNull(prefab, $"Missing trap prefab: {TrapPrefabPath}");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            BuildingEntity building = instance.GetComponent<BuildingEntity>()
                                      ?? instance.AddComponent<BuildingEntity>();
            Renderer renderer = instance.GetComponentInChildren<Renderer>(true);
            Assert.NotNull(building, "Runtime building view was not created.");
            Assert.NotNull(renderer, "Trap prefab has no visual renderer.");

            Material originalMaterial = renderer.sharedMaterial;
            var propertyBlock = new MaterialPropertyBlock();

            Color enemyBuildingColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            building.OwnerFactionID = EntitySideHelper.EnemyFactionId;
            building.SetOwnershipVisualColor(true, enemyBuildingColor);
            building.SetPermanentStealthVisibility(true);

            Assert.False(renderer.enabled, "An enemy stealthed trap must be completely hidden.");
            Assert.AreSame(originalMaterial, renderer.sharedMaterial);

            building.SetPermanentStealthVisibility(false);

            Assert.True(renderer.enabled);
            renderer.GetPropertyBlock(propertyBlock);
            Assert.AreEqual(enemyBuildingColor, propertyBlock.GetColor(BaseColorId));
            Assert.AreEqual(enemyBuildingColor, propertyBlock.GetColor(ColorId));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [TestCase(EntitySideHelper.PlayerFactionId)]
    [TestCase(EntitySideHelper.EnemyFactionId)]
    public void GameEndConditionBuilding_KeepsItsNormalMinimapReport(int ownerFactionId)
    {
        var instance = new GameObject("GameEndConditionBuilding_MinimapTest");
        try
        {
            BuildingEntity building = instance.AddComponent<BuildingEntity>();
            building.buildingData = CreateBuildingData(1);
            building.OwnerFactionID = ownerFactionId;
            SetPrivateProperty(building, nameof(BuildingEntity.IsGameEndConditionBuilding), true);

            InvokePrivate(building, "EnsureMinimapReportComponent");

            MinimapReportComponent[] reports = instance.GetComponents<MinimapReportComponent>();
            Assert.AreEqual(1, reports.Length, "A building owns only its normal minimap report.");
            Assert.IsNull(GetReportIconName(reports[0]));
            Assert.True(GetReportVisibility(reports[0]));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public void MinimapReports_KeepLv0AndStealthVisibilityRules(int buildingLevel, bool initiallyVisible)
    {
        var instance = new GameObject("HiddenConditionBuilding_MinimapTest");
        try
        {
            BuildingEntity building = instance.AddComponent<BuildingEntity>();
            building.buildingData = CreateBuildingData(buildingLevel);
            building.OwnerFactionID = EntitySideHelper.EnemyFactionId;
            SetPrivateProperty(building, nameof(BuildingEntity.IsGameEndConditionBuilding), true);

            InvokePrivate(building, "EnsureMinimapReportComponent");
            MinimapReportComponent[] reports = instance.GetComponents<MinimapReportComponent>();
            Assert.AreEqual(1, reports.Length);
            Assert.AreEqual(initiallyVisible, GetReportVisibility(FindReport(reports, null)));

            if (!initiallyVisible)
                return;

            SetPrivateField(building, "_stealthMinimapHidden", true);
            InvokePrivate(building, "UpdateMinimapReportVisibility");
            Assert.False(GetReportVisibility(FindReport(reports, null)));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static BuildingData CreateBuildingData(int level)
    {
        return new BuildingData(
            $"Buil_MinimapTest_Lv{level}",
            BuilType.Prod,
            Archetype.None,
            string.Empty,
            string.Empty,
            string.Empty,
            level,
            0,
            Fix64.One,
            null,
            Fix64.Zero,
            System.Array.Empty<Fix64>(),
            string.Empty,
            0,
            System.Array.Empty<string>());
    }

    private static MinimapReportComponent FindReport(MinimapReportComponent[] reports, string iconName)
    {
        foreach (MinimapReportComponent report in reports)
        {
            if (GetReportIconName(report) == iconName)
                return report;
        }

        Assert.Fail($"Missing minimap report with icon '{iconName ?? "<building>"}'.");
        return null;
    }

    private static string GetReportIconName(MinimapReportComponent report)
    {
        return (string)typeof(MinimapReportComponent)
            .GetField("iconPrefabName", InstancePrivate)
            ?.GetValue(report);
    }

    private static bool GetReportVisibility(MinimapReportComponent report)
    {
        return (bool)(typeof(MinimapReportComponent)
            .GetField("isVisible", InstancePrivate)
            ?.GetValue(report)
            ?? false);
    }

    private static void InvokePrivate(BuildingEntity building, string methodName)
    {
        MethodInfo method = typeof(BuildingEntity).GetMethod(methodName, InstancePrivate);
        Assert.NotNull(method, $"Missing BuildingEntity.{methodName}.");
        method.Invoke(building, null);
    }

    private static void SetPrivateProperty(BuildingEntity building, string propertyName, object value)
    {
        PropertyInfo property = typeof(BuildingEntity).GetProperty(propertyName, InstancePrivate | BindingFlags.Public);
        Assert.NotNull(property, $"Missing BuildingEntity.{propertyName}.");
        property.SetValue(building, value);
    }

    private static void SetPrivateField(BuildingEntity building, string fieldName, object value)
    {
        FieldInfo field = typeof(BuildingEntity).GetField(fieldName, InstancePrivate);
        Assert.NotNull(field, $"Missing BuildingEntity.{fieldName}.");
        field.SetValue(building, value);
    }

    private static void DestroyInstanceAndRuntimeMaterial(GameObject instance, Material originalMaterial)
    {
        if (instance == null)
            return;

        Renderer renderer = instance.GetComponentInChildren<Renderer>(true);
        Material runtimeMaterial = renderer != null ? renderer.sharedMaterial : null;
        if (renderer != null && originalMaterial != null)
            renderer.sharedMaterial = originalMaterial;
        if (runtimeMaterial != null && runtimeMaterial != originalMaterial)
            Object.DestroyImmediate(runtimeMaterial);

        Object.DestroyImmediate(instance);
    }
}
