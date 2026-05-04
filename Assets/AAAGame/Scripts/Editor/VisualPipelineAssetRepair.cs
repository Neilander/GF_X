using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class VisualPipelineAssetRepair
{
    private const string LogPrefix = "[VisualPipelineRepair]";
    private const string RendererPath = "Assets/Settings/Medium_PipelineAsset_ForwardRenderer.asset";
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("Tools/Visual/Reimport Medium Renderer")]
    public static void ReimportMediumRendererFromMenu()
    {
        ReimportMediumRenderer(false);
    }

    public static void ReimportMediumRenderer()
    {
        ReimportMediumRenderer(true);
    }

    private static void ReimportMediumRenderer(bool exitWhenDone)
    {
        try
        {
            Debug.Log(LogPrefix + " Begin");
            AssetDatabase.ImportAsset(RendererPath, ImportAssetOptions.ForceUpdate);

            bool changed = EnsureFeaturesActive("before");
            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(RendererPath, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            bool stillChanged = EnsureFeaturesActive("after");
            if (stillChanged)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(RendererPath, ImportAssetOptions.ForceUpdate);
            }

            bool hasInactive = LoadFeatures().Any(feature => feature != null && !IsFeatureActive(feature));
            Debug.LogFormat("[VisualPipelineRepair] Done. hasInactive={0}", hasInactive);
            if (exitWhenDone)
            {
                EditorApplication.Exit(hasInactive ? 2 : 0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (exitWhenDone)
            {
                EditorApplication.Exit(1);
            }
        }
    }

    private static bool EnsureFeaturesActive(string phase)
    {
        bool changed = false;
        var features = LoadFeatures();
        Debug.LogFormat("[VisualPipelineRepair] FeatureCount phase={0}, count={1}", phase, features.Length);

        for (int i = 0; i < features.Length; i++)
        {
            var feature = features[i];
            if (feature == null)
            {
                Debug.LogFormat("[VisualPipelineRepair] Feature phase={0}, index={1}, value=null", phase, i);
                continue;
            }

            string diskActive = DiskActiveText(feature);
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[VisualPipelineRepair] Feature phase={0}, index={1}, name={2}, type={3}, id={4}, active={5}, diskActive={6}",
                phase,
                i,
                feature.name,
                feature.GetType().FullName,
                feature.GetInstanceID(),
                IsFeatureActive(feature),
                diskActive));

            if (IsFeatureActive(feature))
            {
                continue;
            }

            SetFeatureActive(feature, true);
            EditorUtility.SetDirty(feature);
            changed = true;
            Debug.LogFormat("[VisualPipelineRepair] Activated feature phase={0}, index={1}, id={2}", phase, i, feature.GetInstanceID());
        }

        return changed;
    }

    private static UnityEngine.Object[] LoadFeatures()
    {
        return AssetDatabase.LoadAllAssetsAtPath(RendererPath)
            .Where(IsRendererFeature)
            .OrderBy(feature => feature.GetInstanceID())
            .ToArray();
    }

    private static bool IsRendererFeature(UnityEngine.Object value)
    {
        if (value == null)
        {
            return false;
        }

        Type type = value.GetType();
        while (type != null)
        {
            if (type.FullName == "UnityEngine.Rendering.Universal.ScriptableRendererFeature")
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static bool IsFeatureActive(UnityEngine.Object feature)
    {
        var property = feature.GetType().GetProperty("isActive", InstanceFlags);
        return property != null && property.GetValue(feature) is bool active && active;
    }

    private static void SetFeatureActive(UnityEngine.Object feature, bool active)
    {
        var method = feature.GetType().GetMethod("SetActive", InstanceFlags);
        method?.Invoke(feature, new object[] { active });
    }

    private static string DiskActiveText(UnityEngine.Object value)
    {
        if (value == null)
        {
            return "null";
        }

        string path = AssetDatabase.GetAssetPath(value);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return "none";
        }

        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string _, out long localId))
        {
            return "no-local-id";
        }

        string marker = string.Format(CultureInfo.InvariantCulture, "--- !u!114 &{0}", localId);
        string[] lines = File.ReadAllLines(path);
        bool inObject = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                inObject = line.TrimEnd() == marker;
                continue;
            }

            if (!inObject)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (trimmed.StartsWith("m_Active:", StringComparison.Ordinal))
            {
                return trimmed.Substring("m_Active:".Length).Trim();
            }
        }

        return "missing";
    }
}
