using System;
using UnityEditor;
using UnityEngine;

public static class WallPlaceholderPrefabGenerator
{
    private const string TemplatePath = "Assets/AAAGame/Prefabs/Entity/Building/Buil_Base_Lv0.prefab";
    private const string PreviewPath = "Assets/AAAGame/Prefabs/Entity/Building/Buil_Wall_Lv0.prefab";
    private const string BuiltPath = "Assets/AAAGame/Prefabs/Entity/Building/Buil_Wall_Lv1.prefab";

    [MenuItem("Tools/Buildings/Generate Wall Placeholder Prefabs")]
    public static void Generate()
    {
        GenerateOne(PreviewPath, "Buil_Wall_Lv0");
        GenerateOne(BuiltPath, "Buil_Wall_Lv1");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void GenerateOne(string outputPath, string rootName)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
        if (source == null)
            throw new InvalidOperationException($"Wall prefab template is missing: {TemplatePath}.");
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            instance.name = rootName;
            if (instance.GetComponent<WallBranchView>() == null)
                instance.AddComponent<WallBranchView>();
            Transform display = instance.transform.Find("Display");
            if (display == null)
                throw new InvalidOperationException("Wall prefab template has no Display root.");
            display.localPosition = Vector3.zero;
            display.localRotation = Quaternion.identity;
            display.localScale = Vector3.one;
            for (int i = display.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(display.GetChild(i).gameObject);
            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "WallPresentationPlaceholder";
            placeholder.transform.SetParent(display, false);
            placeholder.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            placeholder.transform.localScale = new Vector3(0.92f, 1.1f, 0.25f);
            UnityEngine.Object.DestroyImmediate(placeholder.GetComponent<Collider>());
            Transform footprint = instance.transform.Find(BuildingFootprint.ColliderObjectName);
            if (footprint == null || !footprint.TryGetComponent(out BoxCollider footprintCollider))
                throw new InvalidOperationException("Wall prefab template has no footprint BoxCollider.");
            footprintCollider.isTrigger = true;
            footprintCollider.size = new Vector3(1f, 1.5f, 1f);
            footprintCollider.center = new Vector3(0f, 0.75f, 0f);
            PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}
