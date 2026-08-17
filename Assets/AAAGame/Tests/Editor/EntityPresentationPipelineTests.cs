﻿using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class EntityPresentationPipelineTests
{
    private const string UnitTablePath = "Assets/AAAGame/DataTable/CharacterDataDetail.txt";
    private const string BuildingTablePath = "Assets/AAAGame/DataTable/Build/BuildingTable.txt";

    [Test]
    public void DataTableEntityPrefabs_ExistAndSatisfyPresentationContract()
    {
        ValidateReferencedPrefabs(ReadUnitPaths(), true);
        ValidateReferencedPrefabs(ReadBuildingPaths(), false);
    }

    [Test]
    public void MalformedDisplayHierarchy_ThrowsWithEntityContext()
    {
        var root = new GameObject("MalformedEntity");
        try
        {
            var container = new GameObject("Container");
            container.transform.SetParent(root.transform, false);
            var display = new GameObject(EntityPresentationBindings.DisplayObjectName);
            display.transform.SetParent(container.transform, false);
            display.AddComponent<MeshRenderer>();
            EntityPresentationBindings bindings = root.AddComponent<EntityPresentationBindings>();
            bindings.Configure(display.transform, null, null, null, null, null, false);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => bindings.ValidateOrThrow(false));
            StringAssert.Contains("direct child", exception.Message);
            StringAssert.Contains("MalformedEntity", exception.Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PartialTrailBinding_ThrowsExplicitly()
    {
        var root = new GameObject("PartialTrailEntity");
        try
        {
            var display = new GameObject(EntityPresentationBindings.DisplayObjectName);
            display.transform.SetParent(root.transform, false);
            display.AddComponent<MeshRenderer>();
            var weaponAnchor = new GameObject("WeaponAnchor");
            weaponAnchor.transform.SetParent(display.transform, false);
            EntityPresentationBindings bindings = root.AddComponent<EntityPresentationBindings>();
            bindings.Configure(display.transform, null, null, null, weaponAnchor.transform, null, false);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => bindings.ValidateOrThrow(false));
            StringAssert.Contains("both WeaponAnchor and TrailPoint", exception.Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ColliderBelowDisplay_ThrowsExplicitly()
    {
        var root = new GameObject("DisplayColliderEntity");
        try
        {
            var display = new GameObject(EntityPresentationBindings.DisplayObjectName);
            display.transform.SetParent(root.transform, false);
            display.AddComponent<MeshRenderer>();
            display.AddComponent<BoxCollider>();
            EntityPresentationBindings bindings = root.AddComponent<EntityPresentationBindings>();
            bindings.Configure(display.transform, null, null, null, null, null, false);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => bindings.ValidateOrThrow(false));
            StringAssert.Contains("visual-only", exception.Message);
            StringAssert.Contains("Collider", exception.Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ProjectilePresentationOffset_ConvergesByLogicTravelAndCompletesExactly()
    {
        Vector2 offset = new Vector2(3f, 4f);
        Vector2 advanced = Projectile.AdvancePresentationOffset(
            offset,
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, (Fix64)2),
            false);

        Assert.AreEqual(3f, advanced.magnitude, 0.0001f);
        Assert.AreEqual(Vector2.zero, Projectile.AdvancePresentationOffset(
            advanced,
            new FixVector2(Fix64.Zero, (Fix64)2),
            new FixVector2(Fix64.Zero, (Fix64)2),
            true));
    }

    [Test]
    public void FbxToPrefab_UsesCanonicalEntityOutputFolders()
    {
        Assert.AreEqual("Assets/AAAGame/Prefabs/Entity/Soldier", FbxToPrefab.UnitOutputFolder);
        Assert.AreEqual("Assets/AAAGame/Prefabs/Entity/Building", FbxToPrefab.BuildingOutputFolder);
    }

    [Test]
    public void UnitVisualAndPrefabColliders_MatchConfiguredCollisionRadius()
    {
        string[] lines = File.ReadAllLines(UnitTablePath);
        int validatedCount = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] columns = lines[i].Split('\t');
            Assert.Greater(columns.Length, 12, $"Invalid unit table row: {i + 1}");
            UnitSize size = ParseUnitSize(columns[12], i + 1);
            string assetPath = "Assets/AAAGame/Prefabs/Entity/" + columns[6] + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Assert.NotNull(prefab, assetPath);
            EntityPresentationBindings assetBindings = prefab.GetComponent<EntityPresentationBindings>();
            Assert.NotNull(assetBindings, assetPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                EntityPresentationBindings bindings = instance.GetComponent<EntityPresentationBindings>();
                Bounds bounds = EntityPresentationPrefabTools.GetUnitSizeReferenceBounds(bindings.SizeReferenceRenderer);
                float actualRadius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
                float expectedRadius = EntityPresentationPrefabTools.GetUnitVisualRadius(size);
                Assert.AreEqual(expectedRadius, actualRadius, 0.001f, $"Unit visual radius mismatch: {assetPath}");

                CharacterController controller = instance.GetComponent<CharacterController>();
                Assert.NotNull(controller, assetPath);
                Assert.AreEqual(expectedRadius, controller.radius, 0.001f, $"Unit CharacterController radius mismatch: {assetPath}");

                Transform hurtBox = instance.transform.Find("HurtBox");
                Assert.NotNull(hurtBox, assetPath);
                BoxCollider hurtCollider = hurtBox.GetComponent<BoxCollider>();
                Assert.NotNull(hurtCollider, assetPath);
                Assert.AreEqual(expectedRadius * 2f, hurtCollider.size.x, 0.001f, $"Unit HurtBox width mismatch: {assetPath}");
                Assert.AreEqual(expectedRadius * 2f, hurtCollider.size.z, 0.001f, $"Unit HurtBox depth mismatch: {assetPath}");
                validatedCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        Assert.Greater(validatedCount, 0);
        Assert.Less(
            EntityPresentationPrefabTools.GetUnitVisualRadius(UnitSize.Small),
            EntityPresentationPrefabTools.GetUnitVisualRadius(UnitSize.Medium));
        Assert.Less(
            EntityPresentationPrefabTools.GetUnitVisualRadius(UnitSize.Medium),
            EntityPresentationPrefabTools.GetUnitVisualRadius(UnitSize.Large));
    }

    [Test]
    public void HeroSizeReference_ExcludesWeaponAndTrailBounds()
    {
        const string prefabPath = "Assets/AAAGame/Prefabs/Entity/Soldier/英雄.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.NotNull(prefab, prefabPath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            EntityPresentationBindings bindings = instance.GetComponent<EntityPresentationBindings>();
            Assert.NotNull(bindings, prefabPath);
            Assert.NotNull(bindings.SizeReferenceRenderer, prefabPath);
            Assert.IsTrue(bindings.SizeReferenceRenderer is MeshRenderer || bindings.SizeReferenceRenderer is SkinnedMeshRenderer);
            Assert.IsFalse(bindings.SizeReferenceRenderer is TrailRenderer);
            Assert.AreSame(
                bindings.SizeReferenceRenderer,
                EntityPresentationPrefabTools.FindDefaultUnitSizeReferenceRenderer(bindings.DisplayRoot),
                "Hero default size reference selection no longer resolves to the authored body mesh.");
            if (bindings.WeaponAnchor != null)
                Assert.IsFalse(bindings.SizeReferenceRenderer.transform.IsChildOf(bindings.WeaponAnchor));

            Bounds bodyBounds = EntityPresentationPrefabTools.GetUnitSizeReferenceBounds(bindings.SizeReferenceRenderer);
            Bounds displayBounds = GetVisualBounds(bindings.DisplayRoot);
            float bodyRadius = Mathf.Max(bodyBounds.size.x, bodyBounds.size.z) * 0.5f;
            float displayRadius = Mathf.Max(displayBounds.size.x, displayBounds.size.z) * 0.5f;
            Assert.Less(bodyRadius, displayRadius * 0.6f, "Hero size reference still includes the weapon/trail-expanded bounds.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void DefendLv0_UsesTechLv0WithoutSolidCollision()
    {
        const string prefabPath = "Assets/AAAGame/Prefabs/Entity/Building/Buil_Def_Lv0.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.NotNull(prefab, prefabPath);

        Transform display = prefab.transform.Find(EntityPresentationBindings.DisplayObjectName);
        Assert.NotNull(display, $"Prefab has no direct Display child: {prefabPath}");
        Assert.NotNull(FindDescendant(display, "TechLv0"), $"Defend Lv0 must use the restored TechLv0 model: {prefabPath}");
        EntityPresentationBindings bindings = prefab.GetComponent<EntityPresentationBindings>();
        Assert.NotNull(bindings, prefabPath);
        Assert.IsFalse(bindings.IsPlaceholder, $"Defend Lv0 must no longer be marked as a placeholder: {prefabPath}");

        Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
        Assert.IsNotEmpty(colliders, $"Defend Lv0 placeholder has no authored interaction collider: {prefabPath}");
        foreach (Collider collider in colliders)
            Assert.IsTrue(collider.isTrigger, $"Defend Lv0 placeholder contains a solid collider at '{GetPath(collider.transform)}': {prefabPath}");
    }

    [Test]
    public void Lv0BuildingVisuals_AreGrounded()
    {
        string[] prefabPaths =
        {
            "Assets/AAAGame/Prefabs/Entity/Building/Buil_Prod_Lv0.prefab",
            "Assets/AAAGame/Prefabs/Entity/Building/Buil_Def_Lv0.prefab",
            "Assets/AAAGame/Prefabs/Entity/Building/Buil_Army_Lv0.prefab",
            "Assets/AAAGame/Prefabs/Entity/Building/Buil_Base_Lv0.prefab"
        };

        for (int i = 0; i < prefabPaths.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
            Assert.NotNull(prefab, prefabPaths[i]);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                Transform display = instance.transform.Find(EntityPresentationBindings.DisplayObjectName);
                Assert.NotNull(display, prefabPaths[i]);
                Bounds bounds = GetVisualBounds(display);
                Assert.AreEqual(0f, bounds.min.y, 0.0002f, prefabPaths[i]);
                Assert.AreEqual(0f, bounds.center.x, 0.0002f, prefabPaths[i]);
                Assert.AreEqual(0f, bounds.center.z, 0.0002f, prefabPaths[i]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void DefaultRangedWeapon_ReferencesExistingProjectilePrefab()
    {
        const string weaponPath = "Assets/AAAGame/SOs/Weapon/ranged_default.asset";
        RangedWeaponSO weapon = AssetDatabase.LoadAssetAtPath<RangedWeaponSO>(weaponPath);
        Assert.NotNull(weapon, weaponPath);
        Assert.AreEqual("Projectile", weapon.ProjectileName);
        Assert.NotNull(
            AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/AAAGame/Prefabs/Entity/{weapon.ProjectileName}.prefab"),
            $"Default ranged weapon references a missing projectile prefab: {weapon.ProjectileName}");
    }

    private static void ValidateReferencedPrefabs(IReadOnlyCollection<string> runtimePaths, bool unit)
    {
        foreach (string runtimePath in runtimePaths)
        {
            string assetPath = "Assets/AAAGame/Prefabs/Entity/" + runtimePath + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Assert.NotNull(prefab, $"Data table references a missing prefab: {assetPath}");
            EntityPresentationBindings bindings = prefab.GetComponent<EntityPresentationBindings>();
            Assert.NotNull(bindings, $"Prefab has no EntityPresentationBindings: {assetPath}");
            Assert.DoesNotThrow(() => EntityPresentationPrefabTools.ValidateBindingsOrThrow(bindings, unit, assetPath));

            WeaponAttackTrailEffect[] trailEffects = prefab.GetComponentsInChildren<WeaponAttackTrailEffect>(true);
            if (trailEffects.Length > 0)
                Assert.DoesNotThrow(() => bindings.RequireTrailBinding(out _, out _), assetPath);
        }
    }

    private static HashSet<string> ReadUnitPaths()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(UnitTablePath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] columns = lines[i].Split('\t');
            Assert.Greater(columns.Length, 6, $"Invalid unit table row: {i + 1}");
            result.Add(columns[6]);
        }
        return result;
    }

    private static HashSet<string> ReadBuildingPaths()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(BuildingTablePath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("#", StringComparison.Ordinal))
                continue;
            string[] columns = lines[i].Split('\t');
            for (int column = 10; column <= 12; column++)
            {
                if (columns.Length > column && !string.IsNullOrWhiteSpace(columns[column]))
                    result.Add(columns[column]);
            }
        }
        return result;
    }

    private static UnitSize ParseUnitSize(string value, int row)
    {
        const string prefix = "UnitSize.";
        Assert.IsTrue(value.StartsWith(prefix, StringComparison.Ordinal), $"Invalid unit size at row {row}: {value}");
        Assert.IsTrue(Enum.TryParse(value.Substring(prefix.Length), out UnitSize size), $"Invalid unit size at row {row}: {value}");
        return size;
    }

    private static Bounds GetVisualBounds(Transform display)
    {
        Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is not MeshRenderer && renderers[i] is not SkinnedMeshRenderer)
                continue;
            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }
        Assert.IsTrue(hasBounds, $"Display has no visual renderer: {GetPath(display)}");
        return bounds;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            if (string.Equals(descendants[i].name, name, StringComparison.Ordinal))
                return descendants[i];
        }
        return null;
    }

    private static string GetPath(Transform target)
    {
        string path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }
        return path;
    }
}
