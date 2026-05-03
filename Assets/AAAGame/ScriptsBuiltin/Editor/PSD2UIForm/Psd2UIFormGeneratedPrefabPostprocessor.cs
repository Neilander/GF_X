using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AAAGame.EditorTools.Psd2UIForm
{
    internal sealed class Psd2UIFormGeneratedPrefabPostprocessor : AssetPostprocessor
    {
        private const float TargetWidth = 1920f;
        private const float TargetHeight = 1080f;
        private const long MaxReplacementTextureBytes = 64L * 1024L * 1024L;
        private const string ScaleUserDataPrefix = "Psd2UIFormScaledHash:";
        private const string CardSingleLayerName = "卡牌单个";
        private const string PsdLayerNodeTypeName = "UGF.EditorTools.Psd2UGUI.PsdLayerNode";
        private const string PsdConverterTypeName = "UGF.EditorTools.Psd2UGUI.Psd2UIFormConverter";
        private const string PsdDocumentTypeName = "cn.efunstudio.psdreader.PsdParser.PsdDocument";
        private const string PsdLayerTypeName = "cn.efunstudio.psdreader.PsdParser.PsdLayer";
        private const string ImageSourceTypeName = "cn.efunstudio.psdreader.PsdParser.IImageSource";
        private const string PsdLayerRendererTypeName = "cn.efunstudio.psdreader.PsdParser.PsdLayerRenderer";
        private const string PsdRenderedImageTypeName = "cn.efunstudio.psdreader.PsdParser.PsdRenderedImage";
        private const int ImageUIType = 1;
        private const int LegacyTextUIType = 3;
        private const int LegacyButtonUIType = 4;
        private const int LegacyDropdownUIType = 5;
        private const int LegacyInputFieldUIType = 6;
        private const int LegacyToggleUIType = 7;
        private const int TMPTextUIType = 12;
        private const int TMPButtonUIType = 13;
        private const int TMPDropdownUIType = 14;
        private const int TMPInputFieldUIType = 15;
        private const int TMPToggleUIType = 16;
        private const string ImageHelperTypeName = "UGF.EditorTools.Psd2UGUI.ImageHelper";
        private const string TextHelperTypeName = "UGF.EditorTools.Psd2UGUI.TextHelper";
        private const string TMPTextHelperTypeName = "UGF.EditorTools.Psd2UGUI.TMPTextHelper";
        private const string ButtonHelperTypeName = "UGF.EditorTools.Psd2UGUI.ButtonHelper";
        private const string TMPButtonHelperTypeName = "UGF.EditorTools.Psd2UGUI.TMPButtonHelper";
        private const string DropdownHelperTypeName = "UGF.EditorTools.Psd2UGUI.DropdownHelper";
        private const string TMPDropdownHelperTypeName = "UGF.EditorTools.Psd2UGUI.TMPDropdownHelper";
        private const string InputFieldHelperTypeName = "UGF.EditorTools.Psd2UGUI.InputFieldHelper";
        private const string TMPInputFieldHelperTypeName = "UGF.EditorTools.Psd2UGUI.TMPInputFieldHelper";
        private const string ToggleHelperTypeName = "UGF.EditorTools.Psd2UGUI.ToggleHelper";
        private const string TMPToggleHelperTypeName = "UGF.EditorTools.Psd2UGUI.TMPToggleHelper";

        private static readonly HashSet<string> PendingPrefabPaths = new HashSet<string>();
        private static readonly HashSet<string> PendingParsedPrefabPaths = new HashSet<string>();
        private static readonly HashSet<string> PendingImagePaths = new HashSet<string>();
        private static bool s_DelayCallRegistered;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string assetPath in importedAssets)
            {
                string normalizedAssetPath = NormalizeAssetPath(assetPath);
                if (normalizedAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    if (normalizedAssetPath.EndsWith("_psd_layers_parsed.prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        PendingParsedPrefabPaths.Add(normalizedAssetPath);
                    }
                    else if (normalizedAssetPath.StartsWith("Assets/AAAGame/Prefabs/UI/", StringComparison.OrdinalIgnoreCase))
                    {
                        PendingPrefabPaths.Add(normalizedAssetPath);
                    }
                }
                else if (normalizedAssetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                         normalizedAssetPath.StartsWith("Assets/AAAGame/Sprites/", StringComparison.OrdinalIgnoreCase))
                {
                    PendingImagePaths.Add(normalizedAssetPath);
                }
            }

            if ((PendingPrefabPaths.Count == 0 && PendingParsedPrefabPaths.Count == 0 && PendingImagePaths.Count == 0) || s_DelayCallRegistered)
            {
                return;
            }

            s_DelayCallRegistered = true;
            EditorApplication.delayCall += ProcessPendingPrefabs;
        }

        [MenuItem("Tools/Psd2UIForm/Fix Selected UIForm Scale")]
        private static void FixSelectedUIFormScale()
        {
            Dictionary<string, ParsedPrefabInfo> parsedPrefabs = BuildParsedPrefabMap();
            bool changed = false;
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= ScalePrefabIfNeeded(path, parsedPrefabs, true);
                }
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        [MenuItem("Tools/Psd2UIForm/Replace Selected Exported Images")]
        private static void ReplaceSelectedExportedImages()
        {
            ExportMap exportMap = BuildExportMap();
            bool changed = false;
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= ReplaceExportedImageIfNeeded(path, exportMap);
                }
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void ProcessPendingPrefabs()
        {
            s_DelayCallRegistered = false;

            string[] prefabPaths = new string[PendingPrefabPaths.Count];
            PendingPrefabPaths.CopyTo(prefabPaths);
            PendingPrefabPaths.Clear();

            string[] parsedPrefabPaths = new string[PendingParsedPrefabPaths.Count];
            PendingParsedPrefabPaths.CopyTo(parsedPrefabPaths);
            PendingParsedPrefabPaths.Clear();

            string[] imagePaths = new string[PendingImagePaths.Count];
            PendingImagePaths.CopyTo(imagePaths);
            PendingImagePaths.Clear();
            bool hasGenerationSignal = parsedPrefabPaths.Length > 0 || imagePaths.Length > 0;

            bool changed = false;
            foreach (string path in parsedPrefabPaths)
            {
                changed |= SanitizeParsedPrefab(path);
            }

            if (hasGenerationSignal)
            {
                Dictionary<string, ParsedPrefabInfo> parsedPrefabs = BuildParsedPrefabMap();
                foreach (string path in prefabPaths)
                {
                    changed |= ScalePrefabIfNeeded(path, parsedPrefabs, parsedPrefabPaths.Length > 0);
                }
            }

            ExportMap exportMap = BuildExportMap();
            HashSet<string> imagePathsToReplace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in imagePaths)
            {
                imagePathsToReplace.Add(NormalizeAssetPath(path));
            }

            if (parsedPrefabPaths.Length > 0)
            {
                foreach (string pngPath in exportMap.EntriesByPngPath.Keys)
                {
                    if (File.Exists(Path.GetFullPath(pngPath)))
                    {
                        imagePathsToReplace.Add(pngPath);
                    }
                }
            }

            foreach (string path in imagePathsToReplace)
            {
                changed |= ReplaceExportedImageIfNeeded(path, exportMap);
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("Tools/Psd2UIForm/Fix All Generated UIForms")]
        private static void ProcessExistingGeneratedPrefabs()
        {
            bool changed = SanitizeAllParsedPrefabs();
            Dictionary<string, ParsedPrefabInfo> parsedPrefabs = BuildParsedPrefabMap();
            foreach (string prefabPath in parsedPrefabs.Keys)
            {
                if (File.Exists(Path.GetFullPath(prefabPath)))
                {
                    changed |= ScalePrefabIfNeeded(prefabPath, parsedPrefabs, false);
                }
            }

            ExportMap exportMap = BuildExportMap();
            foreach (string pngPath in exportMap.EntriesByPngPath.Keys)
            {
                if (File.Exists(Path.GetFullPath(pngPath)))
                {
                    changed |= ReplaceExportedImageIfNeeded(pngPath, exportMap);
                }
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static bool ScalePrefabIfNeeded(string prefabPath, Dictionary<string, ParsedPrefabInfo> parsedPrefabs, bool fromGenerationSignal)
        {
            string normalizedPrefabPath = NormalizeAssetPath(prefabPath);
            if (!parsedPrefabs.TryGetValue(normalizedPrefabPath, out ParsedPrefabInfo info))
            {
                return false;
            }

            if (info.PsdSize.x <= 0f || info.PsdSize.y <= 0f)
            {
                Debug.LogWarning($"PSD2UIForm scale skipped because PSD size is unknown: {info.PsdAssetPath}");
                return false;
            }

            string currentHash = ComputeFileHash(prefabPath);
            AssetImporter importer = AssetImporter.GetAtPath(prefabPath);
            bool sameScaledContent = importer != null && importer.userData == ScaleUserDataPrefix + currentHash;

            float scaleX = TargetWidth / info.PsdSize.x;
            float scaleY = TargetHeight / info.PsdSize.y;
            bool canScale = scaleX > 0f && scaleY > 0f && (!Mathf.Approximately(scaleX, 1f) || !Mathf.Approximately(scaleY, 1f));
            if (scaleX <= 0f || scaleY <= 0f)
            {
                return false;
            }

            GameObject root = null;
            bool saved = false;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                {
                    return false;
                }

                saved |= RemoveDuplicateGeneratedObjects(root.transform, info);
                RectTransform[] rectTransforms = root.GetComponentsInChildren<RectTransform>(true);
                if (rectTransforms.Length <= 1)
                {
                    return false;
                }

                saved |= NormalizeRootRect(root.transform as RectTransform);
                saved |= EnsureRootCanvasScaler(root);
                if (fromGenerationSignal)
                {
                    saved |= RenameGeneratedPrefabObjectsByPluginAliases(root.transform, info);
                    saved |= RestoreMissingGeneratedContainers(root.transform, info.ParsedHierarchy);
                    saved |= RenameGeneratedPrefabObjectsByParsedHierarchy(root.transform, info.ParsedHierarchy);
                }
                saved |= NormalizeGeneratedImages(root);
                saved |= NormalizeGeneratedImageSprites(root, info, Path.GetFileNameWithoutExtension(prefabPath), fromGenerationSignal);
                bool shouldScale = canScale && !sameScaledContent && (fromGenerationSignal || IsInPsdCoordinateSpace(rectTransforms, root.transform, info.PsdSize));

                if (shouldScale)
                {
                    HashSet<RectTransform> correctedRects = new HashSet<RectTransform>();
                    bool scaledChanged = ApplyPsdBoundsToGeneratedPrefab(root.transform, info, scaleX, scaleY, correctedRects);

                    foreach (RectTransform rectTransform in rectTransforms)
                    {
                        if (rectTransform.transform == root.transform || correctedRects.Contains(rectTransform))
                        {
                            continue;
                        }

                        scaledChanged |= ScaleRectTransform(rectTransform, scaleX, scaleY);
                    }

                    float textScale = Mathf.Min(scaleX, scaleY);
                    scaledChanged |= ScaleText(root, textScale);
                    scaledChanged |= ScaleLayouts(root, scaleX, scaleY, textScale);
                    saved |= scaledChanged;
                }

                if (saved)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (!saved)
            {
                return false;
            }

            AssetImporter updatedImporter = AssetImporter.GetAtPath(prefabPath);
            if (updatedImporter != null)
            {
                updatedImporter.userData = ScaleUserDataPrefix + ComputeFileHash(prefabPath);
                updatedImporter.SaveAndReimport();
            }

            Debug.Log($"PSD2UIForm generated prefab normalized: {prefabPath} ({info.PsdSize.x:0.#}x{info.PsdSize.y:0.#} -> {TargetWidth:0.#}x{TargetHeight:0.#}, scale {scaleX:0.####},{scaleY:0.####})");
            return true;
        }

        private static bool RemoveDuplicateGeneratedObjects(Transform root, ParsedPrefabInfo info)
        {
            int expectedCount = info.GeneratedNamesInParsedHierarchyOrder.Count;
            int currentCount = root.GetComponentsInChildren<Transform>(true).Length - 1;
            if (expectedCount <= 0 || currentCount <= expectedCount)
            {
                return false;
            }

            bool changed = false;
            if (info.RootChildCount > 0 &&
                root.childCount > info.RootChildCount &&
                root.childCount % info.RootChildCount == 0)
            {
                int removeCount = root.childCount - info.RootChildCount;
                for (int i = removeCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject, true);
                    changed = true;
                }

                return true;
            }

            foreach (Transform parent in root.GetComponentsInChildren<Transform>(true).Reverse())
            {
                Dictionary<string, Transform> keptBySignature = new Dictionary<string, Transform>(StringComparer.Ordinal);
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    Transform child = parent.GetChild(i);
                    string signature = BuildGeneratedObjectSignature(child);
                    if (!keptBySignature.ContainsKey(signature))
                    {
                        keptBySignature.Add(signature, child);
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(child.gameObject, true);
                    changed = true;
                }
            }

            return changed;
        }

        private static string BuildGeneratedObjectSignature(Transform transform)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            AppendGeneratedObjectSignature(transform, builder);
            return builder.ToString();
        }

        private static void AppendGeneratedObjectSignature(Transform transform, System.Text.StringBuilder builder)
        {
            builder.Append("Name:");
            builder.Append(transform.gameObject.name);
            builder.Append('|');

            RectTransform rectTransform = transform as RectTransform;
            if (rectTransform != null)
            {
                AppendVector2(builder, rectTransform.anchorMin);
                AppendVector2(builder, rectTransform.anchorMax);
                AppendVector2(builder, rectTransform.anchoredPosition);
                AppendVector2(builder, rectTransform.sizeDelta);
                AppendVector2(builder, rectTransform.pivot);
            }

            Image image = transform.GetComponent<Image>();
            if (image != null)
            {
                builder.Append("|Image:");
                builder.Append(image.sprite != null ? AssetDatabase.GetAssetPath(image.sprite) : string.Empty);
            }

            TMP_Text tmpText = transform.GetComponent<TMP_Text>();
            if (tmpText != null)
            {
                builder.Append("|TMP:");
                builder.Append(tmpText.text);
            }

            builder.Append("|Children:").Append(transform.childCount).Append('[');
            for (int i = 0; i < transform.childCount; i++)
            {
                AppendGeneratedObjectSignature(transform.GetChild(i), builder);
            }

            builder.Append(']');
        }

        private static void AppendVector2(System.Text.StringBuilder builder, Vector2 value)
        {
            builder.Append(Mathf.RoundToInt(value.x * 100f));
            builder.Append(',');
            builder.Append(Mathf.RoundToInt(value.y * 100f));
            builder.Append(';');
        }

        private static bool NormalizeGeneratedImages(GameObject root)
        {
            bool changed = false;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.type != Image.Type.Sliced && image.type != Image.Type.Tiled)
                {
                    continue;
                }

                image.type = Image.Type.Simple;
                changed = true;
            }

            return changed;
        }

        private static bool NormalizeGeneratedImageSprites(GameObject root, ParsedPrefabInfo info, string preferredUiFormAlias, bool allowRename)
        {
            bool changed = false;
            foreach (Transform parent in root.GetComponentsInChildren<Transform>(true))
            {
                Dictionary<string, int> siblingNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    Image image = child.GetComponent<Image>();
                    ExportEntry entry = image != null ? FindExportEntryForGeneratedImage(image, info) : null;
                    string desiredName = entry != null ? SanitizeUnityObjectName(entry.SourceLayerName) : child.gameObject.name;
                    string uniqueName = MakeUniqueSiblingName(desiredName, siblingNameCounts);
                    if (allowRename && entry != null && child.gameObject.name != uniqueName)
                    {
                        child.gameObject.name = uniqueName;
                        EditorUtility.SetDirty(child.gameObject);
                        changed = true;
                    }

                    if (entry == null)
                    {
                        continue;
                    }

                    Sprite sprite = FindExportedSpriteForEntry(entry, info, preferredUiFormAlias);
                    if (sprite != null && image.sprite != sprite)
                    {
                        image.sprite = sprite;
                        EditorUtility.SetDirty(image);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private static bool RenameGeneratedPrefabObjectsByPluginAliases(Transform root, ParsedPrefabInfo info)
        {
            if (info.SourceNamesByPluginGeneratedName.Count == 0)
            {
                return false;
            }

            bool changed = false;
            foreach (Transform parent in root.GetComponentsInChildren<Transform>(true))
            {
                Dictionary<string, int> siblingNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    string desiredName = info.SourceNamesByPluginGeneratedName.TryGetValue(child.gameObject.name, out string sourceName)
                        ? sourceName
                        : child.gameObject.name;
                    string uniqueName = MakeUniqueSiblingName(desiredName, siblingNameCounts);
                    if (child.gameObject.name == uniqueName)
                    {
                        continue;
                    }

                    child.gameObject.name = uniqueName;
                    EditorUtility.SetDirty(child.gameObject);
                    changed = true;
                }
            }

            return changed;
        }

        private static ExportEntry FindExportEntryForGeneratedImage(Image image, ParsedPrefabInfo info)
        {
            ExportEntry entry = FindExportEntryByObjectName(image.gameObject.name, info);
            if (entry != null)
            {
                return entry;
            }

            return FindExportEntryBySprite(image.sprite, info);
        }

        private static ExportEntry FindExportEntryBySprite(Sprite sprite, ParsedPrefabInfo info)
        {
            if (sprite == null)
            {
                return null;
            }

            string spritePath = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrWhiteSpace(spritePath))
            {
                return null;
            }

            string spriteName = Path.GetFileNameWithoutExtension(spritePath);
            ExportEntry bestEntry = null;
            int bestPriority = int.MinValue;
            foreach (ExportEntry entry in info.Entries)
            {
                foreach (ExportNameCandidate candidate in GetExportFileNameCandidates(entry))
                {
                    if (!string.Equals(candidate.Name, spriteName, StringComparison.OrdinalIgnoreCase) ||
                        candidate.Priority <= bestPriority)
                    {
                        continue;
                    }

                    bestEntry = entry;
                    bestPriority = candidate.Priority;
                }
            }

            return bestEntry;
        }

        private static Sprite FindExportedSpriteForEntry(ExportEntry entry, ParsedPrefabInfo info, string preferredUiFormAlias)
        {
            foreach (string uiFormAlias in EnumeratePreferredUiFormAliases(info, preferredUiFormAlias))
            {
                string imageOutputDir = $"Assets/AAAGame/Sprites/{uiFormAlias}";
                foreach (ExportNameCandidate candidate in GetExportFileNameCandidates(entry).OrderByDescending(candidate => candidate.Priority))
                {
                    string spritePath = NormalizeAssetPath($"{imageOutputDir}/{candidate.Name}.png");
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                    if (sprite != null)
                    {
                        return sprite;
                    }
                }
            }

            return null;
        }

        private static ExportEntry FindExportEntryByObjectName(string objectName, ParsedPrefabInfo info)
        {
            string sanitizedObjectName = SanitizeUnityObjectName(objectName);
            foreach (ExportEntry entry in info.Entries)
            {
                if (string.Equals(SanitizeUnityObjectName(entry.SourceLayerName), sanitizedObjectName, StringComparison.Ordinal) ||
                    string.Equals(SanitizeUnityObjectName(entry.ObjectName), sanitizedObjectName, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumeratePreferredUiFormAliases(ParsedPrefabInfo info, string preferredUiFormAlias)
        {
            if (!string.IsNullOrWhiteSpace(preferredUiFormAlias) &&
                info.UiFormAliases.Any(alias => string.Equals(alias, preferredUiFormAlias, StringComparison.OrdinalIgnoreCase)))
            {
                yield return preferredUiFormAlias;
            }

            foreach (string alias in info.UiFormAliases)
            {
                if (!string.Equals(alias, preferredUiFormAlias, StringComparison.OrdinalIgnoreCase))
                {
                    yield return alias;
                }
            }
        }

        private static bool NormalizeRootRect(RectTransform rootRect)
        {
            if (rootRect == null)
            {
                return false;
            }

            bool changed = rootRect.anchorMin != Vector2.zero ||
                           rootRect.anchorMax != Vector2.one ||
                           rootRect.pivot != new Vector2(0.5f, 0.5f) ||
                           rootRect.anchoredPosition != Vector2.zero ||
                           rootRect.sizeDelta != Vector2.zero ||
                           rootRect.localScale != Vector3.one;
            if (!changed)
            {
                return false;
            }

            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = Vector2.zero;
            rootRect.localScale = Vector3.one;
            return true;
        }

        private static bool EnsureRootCanvasScaler(GameObject root)
        {
            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                return false;
            }

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            bool changed = false;
            if (scaler == null)
            {
                scaler = root.AddComponent<CanvasScaler>();
                changed = true;
            }

            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                changed = true;
            }

            Vector2 targetResolution = new Vector2(TargetWidth, TargetHeight);
            if (scaler.referenceResolution != targetResolution)
            {
                scaler.referenceResolution = targetResolution;
                changed = true;
            }

            if (scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight)
            {
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                changed = true;
            }

            if (!Mathf.Approximately(scaler.matchWidthOrHeight, 0f))
            {
                scaler.matchWidthOrHeight = 0f;
                changed = true;
            }

            return changed;
        }

        private static bool IsInPsdCoordinateSpace(RectTransform[] rectTransforms, Transform root, Vector2 psdSize)
        {
            float maxExpectedX = TargetWidth * 1.25f;
            float maxExpectedY = TargetHeight * 1.25f;
            float minRawX = TargetWidth + (psdSize.x - TargetWidth) * 0.25f;
            float minRawY = TargetHeight + (psdSize.y - TargetHeight) * 0.25f;

            foreach (RectTransform rectTransform in rectTransforms)
            {
                if (rectTransform.transform == root)
                {
                    continue;
                }

                Vector2 anchoredPosition = rectTransform.anchoredPosition;
                Vector2 sizeDelta = rectTransform.sizeDelta;
                float absX = Mathf.Max(Mathf.Abs(anchoredPosition.x), Mathf.Abs(sizeDelta.x));
                float absY = Mathf.Max(Mathf.Abs(anchoredPosition.y), Mathf.Abs(sizeDelta.y));
                if ((absX > maxExpectedX && absX > minRawX) || (absY > maxExpectedY && absY > minRawY))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ScaleRectTransform(RectTransform rectTransform, float scaleX, float scaleY)
        {
            Vector2 anchoredPosition = rectTransform.anchoredPosition;
            Vector2 sizeDelta = rectTransform.sizeDelta;
            Vector2 targetPosition = new Vector2(anchoredPosition.x * scaleX, anchoredPosition.y * scaleY);
            Vector2 targetSize = new Vector2(sizeDelta.x * scaleX, sizeDelta.y * scaleY);
            if (rectTransform.anchoredPosition == targetPosition && rectTransform.sizeDelta == targetSize)
            {
                return false;
            }

            rectTransform.anchoredPosition = targetPosition;
            rectTransform.sizeDelta = targetSize;
            return true;
        }

        private static bool ApplyPsdBoundsToGeneratedPrefab(
            Transform root,
            ParsedPrefabInfo info,
            float scaleX,
            float scaleY,
            HashSet<RectTransform> correctedRects)
        {
            if (root == null || info?.PsdHierarchy == null)
            {
                return false;
            }

            bool changed = false;
            ApplyPsdBoundsToGeneratedPrefab(root, info.PsdHierarchy, scaleX, scaleY, correctedRects, ref changed);
            return changed;
        }

        private static void ApplyPsdBoundsToGeneratedPrefab(
            Transform current,
            PsdLayerHierarchyNode psd,
            float scaleX,
            float scaleY,
            HashSet<RectTransform> correctedRects,
            ref bool changed)
        {
            if (current == null || psd == null)
            {
                return;
            }

            Dictionary<string, PsdLayerHierarchyNode> psdChildrenByName = new Dictionary<string, PsdLayerHierarchyNode>(StringComparer.Ordinal);
            foreach (PsdLayerHierarchyNode psdChild in psd.Children)
            {
                if (!string.IsNullOrWhiteSpace(psdChild.Name))
                {
                    psdChildrenByName[psdChild.Name] = psdChild;
                }
            }

            for (int i = 0; i < current.childCount; i++)
            {
                Transform currentChild = current.GetChild(i);
                if (!psdChildrenByName.TryGetValue(currentChild.gameObject.name, out PsdLayerHierarchyNode psdChild))
                {
                    continue;
                }

                RectTransform rectTransform = currentChild as RectTransform;
                if (rectTransform != null)
                {
                    correctedRects.Add(rectTransform);
                    if (ApplyPsdBoundsToRectTransform(rectTransform, psd.Bounds, psdChild.Bounds, scaleX, scaleY))
                    {
                        changed = true;
                    }
                }

                ApplyPsdBoundsToGeneratedPrefab(currentChild, psdChild, scaleX, scaleY, correctedRects, ref changed);
            }
        }

        private static bool ApplyPsdBoundsToRectTransform(
            RectTransform rectTransform,
            Rect parentBounds,
            Rect childBounds,
            float scaleX,
            float scaleY)
        {
            if (rectTransform == null || childBounds.width <= 0f || childBounds.height <= 0f)
            {
                return false;
            }

            Vector2 anchor = new Vector2(0.5f, 0.5f);
            Vector2 targetSize = new Vector2(childBounds.width * scaleX, childBounds.height * scaleY);
            Vector2 parentCenter = new Vector2(parentBounds.xMin + (parentBounds.width * 0.5f), parentBounds.yMin + (parentBounds.height * 0.5f));
            Vector2 childCenter = new Vector2(childBounds.xMin + (childBounds.width * 0.5f), childBounds.yMin + (childBounds.height * 0.5f));
            Vector2 targetPosition = new Vector2(
                (childCenter.x - parentCenter.x) * scaleX,
                (parentCenter.y - childCenter.y) * scaleY);

            bool changed = rectTransform.anchorMin != anchor ||
                           rectTransform.anchorMax != anchor ||
                           rectTransform.pivot != anchor ||
                           rectTransform.localScale != Vector3.one ||
                           rectTransform.anchoredPosition != targetPosition ||
                           rectTransform.sizeDelta != targetSize;
            if (!changed)
            {
                return false;
            }

            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = anchor;
            rectTransform.localScale = Vector3.one;
            rectTransform.anchoredPosition = targetPosition;
            rectTransform.sizeDelta = targetSize;
            return true;
        }

        private static bool ScaleText(GameObject root, float scale)
        {
            bool changed = false;
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                int targetFontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * scale));
                float targetLineSpacing = text.lineSpacing * scale;
                if (text.fontSize != targetFontSize || !Mathf.Approximately(text.lineSpacing, targetLineSpacing))
                {
                    text.fontSize = targetFontSize;
                    text.lineSpacing = targetLineSpacing;
                    changed = true;
                }
            }

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                float targetFontSize = text.fontSize * scale;
                float targetFontSizeMin = text.fontSizeMin * scale;
                float targetFontSizeMax = text.fontSizeMax * scale;
                float targetLineSpacing = text.lineSpacing * scale;
                float targetParagraphSpacing = text.paragraphSpacing * scale;
                float targetCharacterSpacing = text.characterSpacing * scale;
                float targetWordSpacing = text.wordSpacing * scale;
                Vector4 targetMargin = text.margin * scale;
                if (!Mathf.Approximately(text.fontSize, targetFontSize) ||
                    !Mathf.Approximately(text.fontSizeMin, targetFontSizeMin) ||
                    !Mathf.Approximately(text.fontSizeMax, targetFontSizeMax) ||
                    !Mathf.Approximately(text.lineSpacing, targetLineSpacing) ||
                    !Mathf.Approximately(text.paragraphSpacing, targetParagraphSpacing) ||
                    !Mathf.Approximately(text.characterSpacing, targetCharacterSpacing) ||
                    !Mathf.Approximately(text.wordSpacing, targetWordSpacing) ||
                    text.margin != targetMargin)
                {
                    text.fontSize = targetFontSize;
                    text.fontSizeMin = targetFontSizeMin;
                    text.fontSizeMax = targetFontSizeMax;
                    text.lineSpacing = targetLineSpacing;
                    text.paragraphSpacing = targetParagraphSpacing;
                    text.characterSpacing = targetCharacterSpacing;
                    text.wordSpacing = targetWordSpacing;
                    text.margin = targetMargin;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ScaleLayouts(GameObject root, float scaleX, float scaleY, float uniformScale)
        {
            bool changed = false;
            foreach (LayoutElement layout in root.GetComponentsInChildren<LayoutElement>(true))
            {
                float minWidth = ScaleIfNonNegative(layout.minWidth, scaleX);
                float minHeight = ScaleIfNonNegative(layout.minHeight, scaleY);
                float preferredWidth = ScaleIfNonNegative(layout.preferredWidth, scaleX);
                float preferredHeight = ScaleIfNonNegative(layout.preferredHeight, scaleY);
                if (!Mathf.Approximately(layout.minWidth, minWidth) ||
                    !Mathf.Approximately(layout.minHeight, minHeight) ||
                    !Mathf.Approximately(layout.preferredWidth, preferredWidth) ||
                    !Mathf.Approximately(layout.preferredHeight, preferredHeight))
                {
                    layout.minWidth = minWidth;
                    layout.minHeight = minHeight;
                    layout.preferredWidth = preferredWidth;
                    layout.preferredHeight = preferredHeight;
                    changed = true;
                }
            }

            foreach (GridLayoutGroup grid in root.GetComponentsInChildren<GridLayoutGroup>(true))
            {
                Vector2 cellSize = new Vector2(grid.cellSize.x * scaleX, grid.cellSize.y * scaleY);
                Vector2 spacing = new Vector2(grid.spacing.x * scaleX, grid.spacing.y * scaleY);
                changed |= grid.cellSize != cellSize || grid.spacing != spacing;
                grid.cellSize = cellSize;
                grid.spacing = spacing;
                changed |= ScalePadding(grid.padding, scaleX, scaleY);
            }

            foreach (HorizontalOrVerticalLayoutGroup layoutGroup in root.GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true))
            {
                float spacing = layoutGroup.spacing * uniformScale;
                if (!Mathf.Approximately(layoutGroup.spacing, spacing))
                {
                    layoutGroup.spacing = spacing;
                    changed = true;
                }

                changed |= ScalePadding(layoutGroup.padding, scaleX, scaleY);
            }

            return changed;
        }

        private static float ScaleIfNonNegative(float value, float scale)
        {
            return value >= 0f ? value * scale : value;
        }

        private static bool ScalePadding(RectOffset padding, float scaleX, float scaleY)
        {
            int left = Mathf.RoundToInt(padding.left * scaleX);
            int right = Mathf.RoundToInt(padding.right * scaleX);
            int top = Mathf.RoundToInt(padding.top * scaleY);
            int bottom = Mathf.RoundToInt(padding.bottom * scaleY);
            if (padding.left == left && padding.right == right && padding.top == top && padding.bottom == bottom)
            {
                return false;
            }

            padding.left = left;
            padding.right = right;
            padding.top = top;
            padding.bottom = bottom;
            return true;
        }

        private static bool ReplaceExportedImageIfNeeded(string pngPath, ExportMap exportMap)
        {
            if (!exportMap.EntriesByPngPath.TryGetValue(NormalizeAssetPath(pngPath), out ExportEntry entry))
            {
                return false;
            }

            if (PsdRawPngExporter.TryReadLayerBounds(entry.PsdAssetPath, entry.SourceLayerName, out Rect bounds) &&
                IsTooLargeForTextureEncoding(bounds.width, bounds.height))
            {
                Debug.LogWarning($"PSD2UIForm image replacement skipped because the layer is too large to encode safely: {pngPath} ({bounds.width:0}x{bounds.height:0})");
                return false;
            }

            byte[] pngBytes;
            try
            {
                pngBytes = PsdRawPngExporter.ExportLayer(entry.PsdAssetPath, entry.SourceLayerName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"PSD2UIForm image replacement failed: {pngPath}\n{exception}");
                return false;
            }

            string fullPath = Path.GetFullPath(pngPath);
            if (File.Exists(fullPath))
            {
                byte[] existingBytes = File.ReadAllBytes(fullPath);
                if (existingBytes.SequenceEqual(pngBytes))
                {
                    return false;
                }
            }

            File.WriteAllBytes(fullPath, pngBytes);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"PSD2UIForm exported image replaced from PSD channels: {pngPath} <- {entry.SourceLayerName}");
            return true;
        }

        internal static bool SanitizeAllParsedPrefabs()
        {
            bool changed = false;
            string[] parsedPrefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/AAAGame/PSD" });
            foreach (string guid in parsedPrefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("_psd_layers_parsed.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= SanitizeParsedPrefab(path);
                }
            }

            return changed;
        }

        internal static bool SanitizePsdLayerNodes(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            bool changed = false;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                changed |= SanitizePsdLayerNode(behaviour, root.name);
            }

            changed |= SetAllChildrenActive(root.transform);
            changed |= PreserveChineseLayerNames(root.transform);
            return changed;
        }

        private static bool SanitizeParsedPrefab(string parsedPrefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(parsedPrefabPath);
            if (prefab == null)
            {
                return false;
            }

            bool changed = false;
            foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                changed |= SanitizePsdLayerNode(behaviour, parsedPrefabPath);
            }

            changed |= SetAllChildrenActive(prefab.transform);
            changed |= PreserveChineseLayerNames(prefab.transform);
            if (changed)
            {
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(parsedPrefabPath, ImportAssetOptions.ForceUpdate);
            }

            return changed;
        }

        private static bool SanitizePsdLayerNode(MonoBehaviour behaviour, string ownerName)
        {
            if (behaviour == null || behaviour.GetType().FullName != PsdLayerNodeTypeName)
            {
                return false;
            }

            SerializedObject serializedObject = new SerializedObject(behaviour);
            SerializedProperty uiType = serializedObject.FindProperty("UIType");
            if (uiType == null)
            {
                return false;
            }

            bool changed = false;
            if (uiType.intValue == LegacyTextUIType)
            {
                uiType.intValue = TMPTextUIType;
                changed = true;
            }
            else if (uiType.intValue == LegacyButtonUIType && HasAssignedButtonText(behaviour.gameObject))
            {
                uiType.intValue = TMPButtonUIType;
                changed = true;
            }
            else if (uiType.intValue == LegacyDropdownUIType)
            {
                uiType.intValue = TMPDropdownUIType;
                changed = true;
            }
            else if (uiType.intValue == LegacyInputFieldUIType)
            {
                uiType.intValue = TMPInputFieldUIType;
                changed = true;
            }
            else if (uiType.intValue == LegacyToggleUIType)
            {
                uiType.intValue = TMPToggleUIType;
                changed = true;
            }

            SerializedProperty roleUIType = serializedObject.FindProperty("RoleUIType");
            if (roleUIType != null && roleUIType.intValue == LegacyTextUIType)
            {
                roleUIType.intValue = TMPTextUIType;
                changed = true;
            }

            if (uiType.intValue == TMPButtonUIType && !HasAssignedButtonText(behaviour.gameObject))
            {
                uiType.intValue = ImageUIType;
                changed = true;
                changed |= ReplaceHelperComponent(behaviour.gameObject, behaviour, TMPButtonHelperTypeName, ImageHelperTypeName, "image");
                Debug.Log($"PSD2UIForm parsed node downgraded from TMPButton to Image because it has no button text binding: {ownerName}/{behaviour.gameObject.name}");
            }
            else
            {
                changed |= ReplaceLegacyTextHelpers(behaviour.gameObject, behaviour, uiType.intValue);
            }

            if (!changed)
            {
                return false;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(behaviour);
            return true;
        }

        private static bool SetAllChildrenActive(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            bool changed = false;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root || child.gameObject.activeSelf)
                {
                    continue;
                }

                child.gameObject.SetActive(true);
                EditorUtility.SetDirty(child.gameObject);
                changed = true;
            }

            return changed;
        }

        private static bool IsTooLargeForTextureEncoding(float width, float height)
        {
            return width > 0f && height > 0f && width * height * 4f > MaxReplacementTextureBytes;
        }

        private static bool RenameGeneratedPrefabObjectsByParsedHierarchy(Transform root, ParsedNameNode parsedRoot)
        {
            if (parsedRoot == null)
            {
                return false;
            }

            bool changed = false;
            RenameGeneratedPrefabObjectsByParsedHierarchy(root, parsedRoot, ref changed);
            return changed;
        }

        private static bool RestoreMissingGeneratedContainers(Transform root, ParsedNameNode parsedRoot)
        {
            if (root == null || parsedRoot == null)
            {
                return false;
            }

            bool changed = false;
            RestoreMissingGeneratedContainers(root, parsedRoot, ref changed);
            return changed;
        }

        private static void RestoreMissingGeneratedContainers(Transform current, ParsedNameNode parsed, ref bool changed)
        {
            if (current == null || parsed == null)
            {
                return;
            }

            Dictionary<string, Transform> currentChildrenByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform child in current)
            {
                if (!currentChildrenByName.ContainsKey(child.gameObject.name))
                {
                    currentChildrenByName.Add(child.gameObject.name, child);
                }
            }

            HashSet<Transform> reservedChildren = new HashSet<Transform>();
            foreach (ParsedNameNode parsedChild in parsed.Children)
            {
                if (currentChildrenByName.TryGetValue(parsedChild.Name, out Transform existingChild))
                {
                    reservedChildren.Add(existingChild);
                }
            }

            foreach (ParsedNameNode parsedChild in parsed.Children)
            {
                if (!currentChildrenByName.TryGetValue(parsedChild.Name, out Transform currentChild))
                {
                    List<Transform> adoptableChildren = FindAdoptableChildrenForParsedNode(current, parsed, parsedChild, reservedChildren);
                    if (adoptableChildren.Count > 0)
                    {
                        int siblingIndex = adoptableChildren.Min(child => child.GetSiblingIndex());
                        RectTransform createdContainer = CreateGeneratedContainer(current, parsedChild.Name, siblingIndex);
                        foreach (Transform adoptableChild in adoptableChildren)
                        {
                            adoptableChild.SetParent(createdContainer, false);
                            EditorUtility.SetDirty(adoptableChild.gameObject);
                        }

                        EditorUtility.SetDirty(createdContainer.gameObject);
                        currentChild = createdContainer;
                        currentChildrenByName[parsedChild.Name] = currentChild;
                        reservedChildren.Add(currentChild);
                        changed = true;
                    }
                }

                if (currentChild != null)
                {
                    RestoreMissingGeneratedContainers(currentChild, parsedChild, ref changed);
                }
            }
        }

        private static List<Transform> FindAdoptableChildrenForParsedNode(
            Transform current,
            ParsedNameNode parsedParent,
            ParsedNameNode targetParsedNode,
            HashSet<Transform> reservedChildren)
        {
            HashSet<string> targetNames = CollectParsedSubtreeNames(targetParsedNode);
            HashSet<string> siblingNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ParsedNameNode sibling in parsedParent.Children)
            {
                if (ReferenceEquals(sibling, targetParsedNode))
                {
                    continue;
                }

                siblingNames.UnionWith(CollectParsedSubtreeNames(sibling));
            }

            List<Transform> adoptable = new List<Transform>();
            foreach (Transform child in current)
            {
                if (reservedChildren.Contains(child))
                {
                    continue;
                }

                HashSet<string> currentNames = CollectCurrentSubtreeNames(child);
                bool matchesTarget = currentNames.Overlaps(targetNames);
                bool matchesSibling = currentNames.Overlaps(siblingNames);
                if (matchesTarget && !matchesSibling)
                {
                    adoptable.Add(child);
                }
            }

            return adoptable;
        }

        private static HashSet<string> CollectParsedSubtreeNames(ParsedNameNode node)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            CollectParsedSubtreeNames(node, names);
            return names;
        }

        private static void CollectParsedSubtreeNames(ParsedNameNode node, HashSet<string> names)
        {
            if (node == null || names == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(node.Name))
            {
                names.Add(node.Name);
            }

            foreach (ParsedNameNode child in node.Children)
            {
                CollectParsedSubtreeNames(child, names);
            }
        }

        private static HashSet<string> CollectCurrentSubtreeNames(Transform transform)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            CollectCurrentSubtreeNames(transform, names);
            return names;
        }

        private static void CollectCurrentSubtreeNames(Transform transform, HashSet<string> names)
        {
            if (transform == null || names == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(transform.gameObject.name))
            {
                names.Add(transform.gameObject.name);
            }

            foreach (Transform child in transform)
            {
                CollectCurrentSubtreeNames(child, names);
            }
        }

        private static RectTransform CreateGeneratedContainer(Transform parent, string name, int siblingIndex)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = parent.gameObject.layer;

            RectTransform rectTransform = (RectTransform)gameObject.transform;
            rectTransform.SetParent(parent, false);
            rectTransform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, parent.childCount - 1)));
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(100f, 100f);
            rectTransform.localScale = Vector3.one;
            return rectTransform;
        }

        private static void RenameGeneratedPrefabObjectsByParsedHierarchy(Transform current, ParsedNameNode parsed, ref bool changed)
        {
            if (current == null || parsed == null || current.childCount != parsed.Children.Count)
            {
                return;
            }

            int childCount = current.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform currentChild = current.GetChild(i);
                ParsedNameNode parsedChild = parsed.Children[i];
                if (!string.IsNullOrWhiteSpace(parsedChild.Name) && currentChild.gameObject.name != parsedChild.Name)
                {
                    currentChild.gameObject.name = parsedChild.Name;
                    EditorUtility.SetDirty(currentChild.gameObject);
                    changed = true;
                }

                RenameGeneratedPrefabObjectsByParsedHierarchy(currentChild, parsedChild, ref changed);
            }
        }

        private static bool ReplaceLegacyTextHelpers(GameObject node, MonoBehaviour layerNode, int uiType)
        {
            bool changed = false;
            if (uiType == TMPTextUIType)
            {
                changed |= ReplaceHelperComponent(node, layerNode, TextHelperTypeName, TMPTextHelperTypeName, "text");
            }
            else if (uiType == TMPButtonUIType)
            {
                changed |= ReplaceHelperComponent(node, layerNode, ButtonHelperTypeName, TMPButtonHelperTypeName, null);
            }
            else if (uiType == TMPDropdownUIType)
            {
                changed |= ReplaceHelperComponent(node, layerNode, DropdownHelperTypeName, TMPDropdownHelperTypeName, null);
            }
            else if (uiType == TMPInputFieldUIType)
            {
                changed |= ReplaceHelperComponent(node, layerNode, InputFieldHelperTypeName, TMPInputFieldHelperTypeName, null);
            }
            else if (uiType == TMPToggleUIType)
            {
                changed |= ReplaceHelperComponent(node, layerNode, ToggleHelperTypeName, TMPToggleHelperTypeName, null);
            }

            return changed;
        }

        private static bool ReplaceHelperComponent(
            GameObject node,
            MonoBehaviour layerNode,
            string legacyHelperTypeName,
            string tmpHelperTypeName,
            string defaultLayerReferenceField)
        {
            bool changed = false;
            MonoBehaviour targetHelper = null;
            foreach (MonoBehaviour helper in node.GetComponents<MonoBehaviour>())
            {
                if (helper == null || helper.GetType().FullName != tmpHelperTypeName)
                {
                    continue;
                }

                targetHelper = helper;
                break;
            }

            foreach (MonoBehaviour helper in node.GetComponents<MonoBehaviour>())
            {
                if (helper == null || helper.GetType().FullName != legacyHelperTypeName)
                {
                    continue;
                }

                if (targetHelper == null)
                {
                    Type targetType = helper.GetType().Assembly.GetType(tmpHelperTypeName, false);
                    if (targetType == null)
                    {
                        continue;
                    }

                    targetHelper = (MonoBehaviour)node.AddComponent(targetType);
                }

                CopyHelperReferences(helper, targetHelper, layerNode, defaultLayerReferenceField);
                UnityEngine.Object.DestroyImmediate(helper, true);
                EditorUtility.SetDirty(targetHelper);
                EditorUtility.SetDirty(node);
                changed = true;
            }

            return changed;
        }

        private static void CopyHelperReferences(
            MonoBehaviour sourceHelper,
            MonoBehaviour targetHelper,
            MonoBehaviour layerNode,
            string defaultLayerReferenceField)
        {
            SerializedObject source = new SerializedObject(sourceHelper);
            SerializedObject target = new SerializedObject(targetHelper);
            SerializedProperty sourceProperty = source.GetIterator();
            bool enterChildren = true;
            while (sourceProperty.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (sourceProperty.name == "m_Script" || sourceProperty.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                SerializedProperty targetProperty = target.FindProperty(sourceProperty.name);
                if (targetProperty != null && targetProperty.propertyType == SerializedPropertyType.ObjectReference)
                {
                    targetProperty.objectReferenceValue = sourceProperty.objectReferenceValue;
                }
            }

            if (!string.IsNullOrEmpty(defaultLayerReferenceField))
            {
                SerializedProperty targetProperty = target.FindProperty(defaultLayerReferenceField);
                if (targetProperty != null &&
                    targetProperty.propertyType == SerializedPropertyType.ObjectReference &&
                    targetProperty.objectReferenceValue == null)
                {
                    targetProperty.objectReferenceValue = layerNode;
                }
            }

            target.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool PreserveChineseLayerNames(Transform root)
        {
            bool changed = false;
            foreach (Transform parent in root.GetComponentsInChildren<Transform>(true))
            {
                Dictionary<string, int> siblingNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Transform child in parent)
                {
                    string desiredName = ReadPreservedLayerName(child.gameObject);
                    string uniqueName = MakeUniqueSiblingName(desiredName ?? child.name, siblingNameCounts);
                    if (desiredName == null || child.name == uniqueName)
                    {
                        continue;
                    }

                    child.gameObject.name = uniqueName;
                    EditorUtility.SetDirty(child.gameObject);
                    changed = true;
                }
            }

            return changed;
        }

        private static string ReadPreservedLayerName(GameObject gameObject)
        {
            foreach (MonoBehaviour behaviour in gameObject.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.GetType().FullName != PsdLayerNodeTypeName)
                {
                    continue;
                }

                SerializedObject serializedObject = new SerializedObject(behaviour);
                string sourceLayerName = serializedObject.FindProperty("sourceLayerName")?.stringValue;
                if (string.IsNullOrWhiteSpace(sourceLayerName) || !ContainsCjk(sourceLayerName))
                {
                    return null;
                }

                return SanitizeUnityObjectName(sourceLayerName);
            }

            return null;
        }

        private static string MakeUniqueSiblingName(string baseName, Dictionary<string, int> siblingNameCounts)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "Layer";
            }

            if (!siblingNameCounts.TryGetValue(baseName, out int count))
            {
                siblingNameCounts[baseName] = 1;
                return baseName;
            }

            count++;
            siblingNameCounts[baseName] = count;
            return $"{baseName}_{count}";
        }

        private static string SanitizeUnityObjectName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (Array.IndexOf(invalidChars, c) >= 0 || char.IsControl(c))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars).Trim();
        }

        private static bool ContainsCjk(string value)
        {
            foreach (char c in value)
            {
                if ((c >= '\u3400' && c <= '\u4dbf') ||
                    (c >= '\u4e00' && c <= '\u9fff') ||
                    (c >= '\uf900' && c <= '\ufaff'))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAssignedButtonText(GameObject node)
        {
            foreach (MonoBehaviour behaviour in node.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.GetType().FullName == PsdLayerNodeTypeName)
                {
                    continue;
                }

                SerializedObject serializedObject = new SerializedObject(behaviour);
                SerializedProperty text = serializedObject.FindProperty("text");
                if (text != null && text.objectReferenceValue != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static ExportMap BuildExportMap()
        {
            ExportMap map = new ExportMap();
            Dictionary<string, int> prioritiesByPngPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ParsedPrefabInfo> parsedPrefabs = BuildParsedPrefabMap();
            foreach (ParsedPrefabInfo info in new HashSet<ParsedPrefabInfo>(parsedPrefabs.Values))
            {
                foreach (string uiFormAlias in info.UiFormAliases)
                {
                    string imageOutputDir = $"Assets/AAAGame/Sprites/{uiFormAlias}";
                    foreach (ExportEntry entry in info.Entries)
                    {
                        entry.PsdAssetPath = info.PsdAssetPath;
                        foreach (ExportNameCandidate candidate in GetExportFileNameCandidates(entry))
                        {
                            string pngPath = NormalizeAssetPath($"{imageOutputDir}/{candidate.Name}.png");
                            if (prioritiesByPngPath.TryGetValue(pngPath, out int existingPriority) &&
                                existingPriority >= candidate.Priority)
                            {
                                continue;
                            }

                            prioritiesByPngPath[pngPath] = candidate.Priority;
                            map.EntriesByPngPath[pngPath] = entry;
                        }
                    }
                }
            }

            return map;
        }

        private static IEnumerable<ExportNameCandidate> GetExportFileNameCandidates(ExportEntry entry)
        {
            Dictionary<string, int> candidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            AddExportFileNameCandidates(entry.ObjectName, candidates);
            AddExportFileNameCandidates(entry.SourceLayerName, candidates);
            foreach (KeyValuePair<string, int> candidate in candidates)
            {
                yield return new ExportNameCandidate(candidate.Key, candidate.Value);
            }
        }

        private static void AddExportFileNameCandidates(string objectName, Dictionary<string, int> candidates)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return;
            }

            AddExportFileNameCandidate(candidates, objectName, 100);

            string normalizedName = NormalizePsd2UIFormFileName(objectName);
            if (!string.Equals(normalizedName, objectName, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(normalizedName))
            {
                AddExportFileNameCandidate(candidates, normalizedName, 100);
            }

            string suffixBaseName = !string.IsNullOrWhiteSpace(normalizedName) ? normalizedName : objectName;
            for (int i = 1; i <= 32; i++)
            {
                AddExportFileNameCandidate(candidates, $"{suffixBaseName}_{i}", 10);
            }
        }

        private static void AddExportFileNameCandidate(Dictionary<string, int> candidates, string name, int priority)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (candidates.TryGetValue(name, out int existingPriority) && existingPriority >= priority)
            {
                return;
            }

            candidates[name] = priority;
        }

        private static string NormalizePsd2UIFormFileName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] chars = name.Trim().ToCharArray();
            bool previousUnderscore = false;
            List<char> normalized = new List<char>(chars.Length);
            foreach (char c in chars)
            {
                bool keep = char.IsLetterOrDigit(c) ||
                            (c >= '\u3400' && c <= '\u4dbf') ||
                            (c >= '\u4e00' && c <= '\u9fff') ||
                            (c >= '\uf900' && c <= '\ufaff');
                if (keep && Array.IndexOf(invalidChars, c) < 0)
                {
                    normalized.Add(c);
                    previousUnderscore = false;
                    continue;
                }

                if (!previousUnderscore)
                {
                    normalized.Add('_');
                    previousUnderscore = true;
                }
            }

            return new string(normalized.ToArray()).Trim('_');
        }

        private static string NormalizePsd2UIFormGeneratedPinyinName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            List<char> normalized = new List<char>(name.Length * 4);
            bool previousUnderscore = false;
            foreach (char c in name.Trim())
            {
                if (Array.IndexOf(invalidChars, c) >= 0 || char.IsControl(c))
                {
                    AppendGeneratedNameUnderscore(normalized, ref previousUnderscore);
                    continue;
                }

                if (TryGetKnownPinyin(c, out string pinyin))
                {
                    foreach (char pinyinChar in pinyin)
                    {
                        normalized.Add(pinyinChar);
                    }

                    previousUnderscore = false;
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    normalized.Add(c);
                    previousUnderscore = false;
                    continue;
                }

                AppendGeneratedNameUnderscore(normalized, ref previousUnderscore);
            }

            return new string(normalized.ToArray()).Trim('_');
        }

        private static void AppendGeneratedNameUnderscore(List<char> normalized, ref bool previousUnderscore)
        {
            if (previousUnderscore)
            {
                return;
            }

            normalized.Add('_');
            previousUnderscore = true;
        }

        private static bool TryGetKnownPinyin(char c, out string pinyin)
        {
            switch (c)
            {
                case '图': pinyin = "tu"; return true;
                case '层': pinyin = "ceng"; return true;
                case '组': pinyin = "zu"; return true;
                case '拷': pinyin = "kao"; return true;
                case '贝': pinyin = "bei"; return true;
                case '椭': pinyin = "tuo"; return true;
                case '圆': pinyin = "yuan"; return true;
                case '矩': pinyin = "ju"; return true;
                case '形': pinyin = "xing"; return true;
                case '条': pinyin = "tiao"; return true;
                case '件': pinyin = "jian"; return true;
                case '文': pinyin = "wen"; return true;
                case '案': pinyin = "an"; return true;
                case '升': pinyin = "sheng"; return true;
                case '级': pinyin = "ji"; return true;
                case '底': pinyin = "di"; return true;
                case '地': pinyin = "di"; return true;
                case '小': pinyin = "xiao"; return true;
                case '终': pinyin = "zhong"; return true;
                case '意': pinyin = "yi"; return true;
                case '识': pinyin = "shi"; return true;
                case '容': pinyin = "rong"; return true;
                case '量': pinyin = "liang"; return true;
                case '指': pinyin = "zhi"; return true;
                case '令': pinyin = "ling"; return true;
                case '元': pinyin = "yuan"; return true;
                case '标': pinyin = "biao"; return true;
                case '占': pinyin = "zhan"; return true;
                case '位': pinyin = "wei"; return true;
                case '点': pinyin = "dian"; return true;
                case '完': pinyin = "wan"; return true;
                case '成': pinyin = "cheng"; return true;
                case '效': pinyin = "xiao"; return true;
                case '果': pinyin = "guo"; return true;
                case '卡': pinyin = "ka"; return true;
                case '槽': pinyin = "cao"; return true;
                case '本': pinyin = "ben"; return true;
                case '体': pinyin = "ti"; return true;
                case '删': pinyin = "shan"; return true;
                case '除': pinyin = "chu"; return true;
                case '背': pinyin = "bei"; return true;
                case '景': pinyin = "jing"; return true;
                case '方': pinyin = "fang"; return true;
                case '便': pinyin = "bian"; return true;
                case '看': pinyin = "kan"; return true;
                case '过': pinyin = "guo"; return true;
                case '程': pinyin = "cheng"; return true;
                case '中': pinyin = "zhong"; return true;
                case '进': pinyin = "jin"; return true;
                case '度': pinyin = "du"; return true;
                case '选': pinyin = "xuan"; return true;
                case '择': pinyin = "ze"; return true;
                case '展': pinyin = "zhan"; return true;
                case '开': pinyin = "kai"; return true;
                case '顶': pinyin = "ding"; return true;
                case '天': pinyin = "tian"; return true;
                case '数': pinyin = "shu"; return true;
                case '运': pinyin = "yun"; return true;
                case '营': pinyin = "ying"; return true;
                case '置': pinyin = "zhi"; return true;
                case '示': pinyin = "shi"; return true;
                case '片': pinyin = "pian"; return true;
                case '支': pinyin = "zhi"; return true;
                case '满': pinyin = "man"; return true;
                case '信': pinyin = "xin"; return true;
                case '息': pinyin = "xi"; return true;
                case '查': pinyin = "cha"; return true;
                case '阅': pinyin = "yue"; return true;
                case '任': pinyin = "ren"; return true;
                case '务': pinyin = "wu"; return true;
                case '目': pinyin = "mu"; return true;
                case '一': pinyin = "yi"; return true;
                case '二': pinyin = "er"; return true;
                case '三': pinyin = "san"; return true;
                case '快': pinyin = "kuai"; return true;
                case '捷': pinyin = "jie"; return true;
                case '键': pinyin = "jian"; return true;
                case '单': pinyin = "dan"; return true;
                case '名': pinyin = "ming"; return true;
                case '称': pinyin = "cheng"; return true;
                case '实': pinyin = "shi"; return true;
                case '习': pinyin = "xi"; return true;
                case '生': pinyin = "sheng"; return true;
                case '面': pinyin = "mian"; return true;
                case '试': pinyin = "shi"; return true;
                case '间': pinyin = "jian"; return true;
                default:
                    pinyin = null;
                    return false;
            }
        }

        private static Dictionary<string, ParsedPrefabInfo> BuildParsedPrefabMap()
        {
            Dictionary<string, ParsedPrefabInfo> map = new Dictionary<string, ParsedPrefabInfo>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> matchScoresByPrefabPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] parsedPrefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/AAAGame/PSD" });
            foreach (string guid in parsedPrefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("_psd_layers_parsed.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                ParsedPrefabInfo info = ReadParsedPrefabInfo(prefab);
                if (string.IsNullOrEmpty(info.UiFormName) || string.IsNullOrEmpty(info.PsdAssetPath))
                {
                    continue;
                }

                info.PsdSize = ReadPsdSize(info.PsdAssetPath);
                info.PsdHierarchy = PsdRawPngExporter.ReadHierarchy(info.PsdAssetPath, info.PsdSize);
                AddUiFormAlias(info, info.UiFormName);
                foreach (string alias in FindUiFormAliasesBySpriteOverlap(info))
                {
                    AddUiFormAlias(info, alias);
                }

                foreach (string alias in info.UiFormAliases)
                {
                    string uiFormPath = NormalizeAssetPath($"Assets/AAAGame/Prefabs/UI/{alias}.prefab");
                    int score = string.Equals(alias, info.UiFormName, StringComparison.OrdinalIgnoreCase)
                        ? int.MaxValue
                        : CountSpriteOverlap(alias, info);
                    if (matchScoresByPrefabPath.TryGetValue(uiFormPath, out int existingScore) && existingScore >= score)
                    {
                        continue;
                    }

                    matchScoresByPrefabPath[uiFormPath] = score;
                    map[uiFormPath] = info;
                }
            }

            return map;
        }

        private static ParsedPrefabInfo ReadParsedPrefabInfo(GameObject prefab)
        {
            ParsedPrefabInfo info = new ParsedPrefabInfo();
            info.RootChildCount = prefab.transform.childCount;
            info.ParsedHierarchy = BuildParsedNameTree(prefab.transform, null);
            foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform == prefab.transform)
                {
                    continue;
                }

                info.GeneratedNamesInParsedHierarchyOrder.Add(SanitizeUnityObjectName(transform.gameObject.name));
            }

            foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                SerializedObject serializedObject = new SerializedObject(behaviour);
                if (type.FullName == PsdConverterTypeName)
                {
                    info.UiFormName = serializedObject.FindProperty("uiFormName")?.stringValue;
                    UnityEngine.Object psdAsset = serializedObject.FindProperty("psdAsset")?.objectReferenceValue;
                    info.PsdAssetPath = psdAsset != null ? AssetDatabase.GetAssetPath(psdAsset) : null;
                }
                else if (type.FullName == PsdLayerNodeTypeName)
                {
                    AddPluginGeneratedNameAliases(behaviour.gameObject, info);

                    SerializedProperty markToExport = serializedObject.FindProperty("markToExport");
                    SerializedProperty sourceLayerName = serializedObject.FindProperty("sourceLayerName");
                    if (markToExport == null || sourceLayerName == null)
                    {
                        continue;
                    }

                    if (!markToExport.boolValue || string.IsNullOrEmpty(sourceLayerName.stringValue))
                    {
                        continue;
                    }

                    info.Entries.Add(new ExportEntry
                    {
                        ObjectName = ReadPreservedLayerName(behaviour.gameObject) ?? behaviour.gameObject.name,
                        SourceLayerName = sourceLayerName.stringValue
                    });
                }
            }

            return info;
        }

        private static void AddPluginGeneratedNameAliases(GameObject gameObject, ParsedPrefabInfo info)
        {
            string sourceName = ReadPreservedLayerName(gameObject);
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return;
            }

            AddPluginGeneratedNameAlias(info, NormalizePsd2UIFormFileName(sourceName), sourceName);
            AddPluginGeneratedNameAlias(info, NormalizePsd2UIFormGeneratedPinyinName(sourceName), sourceName);
        }

        private static void AddPluginGeneratedNameAlias(ParsedPrefabInfo info, string alias, string sourceName)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.Equals(alias, sourceName, StringComparison.Ordinal))
            {
                return;
            }

            if (!info.SourceNamesByPluginGeneratedName.ContainsKey(alias))
            {
                info.SourceNamesByPluginGeneratedName.Add(alias, sourceName);
            }
        }

        private static ParsedNameNode BuildParsedNameTree(Transform transform, Dictionary<string, int> siblingNameCounts)
        {
            string desiredName = ReadPreservedLayerName(transform.gameObject) ?? SanitizeUnityObjectName(transform.gameObject.name);
            ParsedNameNode node = new ParsedNameNode
            {
                Name = siblingNameCounts != null ? MakeUniqueSiblingName(desiredName, siblingNameCounts) : desiredName
            };

            Dictionary<string, int> childNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < transform.childCount; i++)
            {
                node.Children.Add(BuildParsedNameTree(transform.GetChild(i), childNameCounts));
            }

            return node;
        }

        private static void AddUiFormAlias(ParsedPrefabInfo info, string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            alias = SanitizeUnityObjectName(alias);
            if (string.IsNullOrWhiteSpace(alias) ||
                info.UiFormAliases.Any(existing => string.Equals(existing, alias, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            info.UiFormAliases.Add(alias);
        }

        private static IEnumerable<string> FindUiFormAliasesBySpriteOverlap(ParsedPrefabInfo info)
        {
            string spritesRoot = Path.GetFullPath("Assets/AAAGame/Sprites");
            if (!Directory.Exists(spritesRoot) || info.Entries.Count == 0)
            {
                yield break;
            }

            foreach (string directory in Directory.GetDirectories(spritesRoot))
            {
                string alias = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                int overlap = CountSpriteOverlap(alias, info);
                if (overlap >= Mathf.Min(3, info.Entries.Count))
                {
                    yield return alias;
                }
            }
        }

        private static int CountSpriteOverlap(string uiFormAlias, ParsedPrefabInfo info)
        {
            string imageOutputDir = Path.GetFullPath($"Assets/AAAGame/Sprites/{uiFormAlias}");
            if (!Directory.Exists(imageOutputDir))
            {
                return 0;
            }

            HashSet<string> pngNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(imageOutputDir, "*.png"))
            {
                pngNames.Add(Path.GetFileNameWithoutExtension(file));
            }

            int overlap = 0;
            foreach (ExportEntry entry in info.Entries)
            {
                foreach (ExportNameCandidate candidate in GetExportFileNameCandidates(entry))
                {
                    if (pngNames.Contains(candidate.Name))
                    {
                        overlap++;
                        break;
                    }
                }
            }

            return overlap;
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static Vector2 ReadPsdSize(string psdAssetPath)
        {
            Vector2 sizeFromHeader = ReadPsdHeaderSize(psdAssetPath);
            if (sizeFromHeader.x > 0f && sizeFromHeader.y > 0f)
            {
                return sizeFromHeader;
            }

            Vector2 sizeFromDocument = ReadPsdDocumentSize(psdAssetPath);
            if (sizeFromDocument.x > 0f && sizeFromDocument.y > 0f)
            {
                return sizeFromDocument;
            }

            Texture2D psdTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(psdAssetPath);
            if (psdTexture != null)
            {
                return new Vector2(psdTexture.width, psdTexture.height);
            }

            return Vector2.zero;
        }

        private static Vector2 ReadPsdHeaderSize(string psdAssetPath)
        {
            string fullPath = Path.GetFullPath(psdAssetPath);
            if (!File.Exists(fullPath))
            {
                return Vector2.zero;
            }

            byte[] header = new byte[26];
            using (FileStream stream = File.OpenRead(fullPath))
            {
                if (stream.Read(header, 0, header.Length) != header.Length)
                {
                    return Vector2.zero;
                }
            }

            if (header[0] != '8' || header[1] != 'B' || header[2] != 'P' || header[3] != 'S')
            {
                return Vector2.zero;
            }

            int height = ReadBigEndianInt32(header, 14);
            int width = ReadBigEndianInt32(header, 18);
            return new Vector2(width, height);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int startIndex)
        {
            return (bytes[startIndex] << 24) |
                   (bytes[startIndex + 1] << 16) |
                   (bytes[startIndex + 2] << 8) |
                   bytes[startIndex + 3];
        }

        private static Vector2 ReadPsdDocumentSize(string psdAssetPath)
        {
            Assembly psdAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "cn.efunstudio.psd2ugui");
            Type documentType = psdAssembly?.GetType(PsdDocumentTypeName, false);
            MethodInfo createMethod = documentType?.GetMethod("Create", new[] { typeof(string) });
            PropertyInfo widthProperty = documentType?.GetProperty("Width");
            PropertyInfo heightProperty = documentType?.GetProperty("Height");
            if (createMethod == null || widthProperty == null || heightProperty == null)
            {
                return Vector2.zero;
            }

            object document = null;
            try
            {
                document = createMethod.Invoke(null, new object[] { Path.GetFullPath(psdAssetPath) });
                if (document == null)
                {
                    return Vector2.zero;
                }

                float width = Convert.ToSingle(widthProperty.GetValue(document));
                float height = Convert.ToSingle(heightProperty.GetValue(document));
                return new Vector2(width, height);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"PSD2UIForm failed to read PSD document size, fallback to imported texture size: {psdAssetPath}\n{exception.Message}");
                return Vector2.zero;
            }
            finally
            {
                if (document is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static string ComputeFileHash(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                return string.Empty;
            }

            using (System.Security.Cryptography.SHA1 sha1 = System.Security.Cryptography.SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(File.ReadAllBytes(fullPath));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private sealed class ExportMap
        {
            public readonly Dictionary<string, ExportEntry> EntriesByPngPath = new Dictionary<string, ExportEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ParsedPrefabInfo
        {
            public string UiFormName;
            public string PsdAssetPath;
            public Vector2 PsdSize;
            public PsdLayerHierarchyNode PsdHierarchy;
            public int RootChildCount;
            public ParsedNameNode ParsedHierarchy;
            public readonly List<ExportEntry> Entries = new List<ExportEntry>();
            public readonly List<string> UiFormAliases = new List<string>();
            public readonly List<string> GeneratedNamesInParsedHierarchyOrder = new List<string>();
            public readonly Dictionary<string, string> SourceNamesByPluginGeneratedName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ParsedNameNode
        {
            public string Name;
            public readonly List<ParsedNameNode> Children = new List<ParsedNameNode>();
        }

        private sealed class PsdLayerHierarchyNode
        {
            public string Name;
            public Rect Bounds;
            public readonly List<PsdLayerHierarchyNode> Children = new List<PsdLayerHierarchyNode>();
        }

        private sealed class ExportEntry
        {
            public string ObjectName;
            public string SourceLayerName;
            public string PsdAssetPath;
        }

        private readonly struct ExportNameCandidate
        {
            public readonly string Name;
            public readonly int Priority;

            public ExportNameCandidate(string name, int priority)
            {
                Name = name;
                Priority = priority;
            }
        }

        private static class PsdRawPngExporter
        {
            private static Assembly s_PsdAssembly;
            private static Type s_DocumentType;
            private static Type s_LayerType;
            private static Type s_ImageSourceType;
            private static Type s_LayerRendererType;
            private static Type s_RenderedImageType;
            private static MethodInfo s_RenderLeafMethod;
            private static MethodInfo s_RenderGroupMethod;

            public static byte[] ExportLayer(string psdAssetPath, string sourceLayerName)
            {
                EnsureTypes();

                string psdFullPath = Path.GetFullPath(psdAssetPath);
                object document = s_DocumentType.GetMethod("Create", new[] { typeof(string) })?.Invoke(null, new object[] { psdFullPath });
                if (document == null)
                {
                    throw new InvalidOperationException($"Could not open PSD: {psdAssetPath}");
                }

                try
                {
                    object layer = FindLayerByName(document, sourceLayerName);
                    if (layer == null)
                    {
                        throw new InvalidOperationException($"PSD layer not found: {sourceLayerName}");
                    }

                    RawImage image = RenderLayer(layer, true, true);
                    if (image.Width <= 0 || image.Height <= 0)
                    {
                        throw new InvalidOperationException($"PSD layer has no pixels: {sourceLayerName}");
                    }

                    if (IsTooLargeForTextureEncoding(image.Width, image.Height))
                    {
                        throw new InvalidOperationException($"PSD layer is too large to encode safely: {sourceLayerName} ({image.Width}x{image.Height})");
                    }

                    Texture2D texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false);
                    texture.LoadRawTextureData(image.RgbaBottomUp);
                    texture.Apply(false, false);
                    byte[] pngBytes = texture.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(texture);
                    return pngBytes;
                }
                finally
                {
                    if (document is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            public static PsdLayerHierarchyNode ReadHierarchy(string psdAssetPath, Vector2 fallbackDocumentSize)
            {
                EnsureTypes();

                string psdFullPath = Path.GetFullPath(psdAssetPath);
                object document = s_DocumentType.GetMethod("Create", new[] { typeof(string) })?.Invoke(null, new object[] { psdFullPath });
                if (document == null)
                {
                    return null;
                }

                try
                {
                    PsdLayerHierarchyNode root = new PsdLayerHierarchyNode
                    {
                        Name = string.Empty,
                        Bounds = new Rect(
                            0f,
                            0f,
                            ReadDocumentDimension(document, "Width", fallbackDocumentSize.x),
                            ReadDocumentDimension(document, "Height", fallbackDocumentSize.y))
                    };

                    IEnumerable children = (IEnumerable)s_DocumentType.GetProperty("Childs")?.GetValue(document);
                    if (children != null)
                    {
                        Dictionary<string, int> siblingNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (object child in children)
                        {
                            root.Children.Add(BuildHierarchyNode(child, siblingNameCounts));
                        }
                    }

                    return root;
                }
                finally
                {
                    if (document is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            public static bool TryReadLayerBounds(string psdAssetPath, string sourceLayerName, out Rect bounds)
            {
                EnsureTypes();

                string psdFullPath = Path.GetFullPath(psdAssetPath);
                object document = s_DocumentType.GetMethod("Create", new[] { typeof(string) })?.Invoke(null, new object[] { psdFullPath });
                if (document == null)
                {
                    bounds = default;
                    return false;
                }

                try
                {
                    object layer = FindLayerByName(document, sourceLayerName);
                    if (layer == null)
                    {
                        bounds = default;
                        return false;
                    }

                    bounds = new Rect(
                        ReadIntProperty(layer, "Left"),
                        ReadIntProperty(layer, "Top"),
                        ReadIntProperty(layer, "Width"),
                        ReadIntProperty(layer, "Height"));
                    return bounds.width > 0f && bounds.height > 0f;
                }
                finally
                {
                    if (document is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            private static void EnsureTypes()
            {
                if (s_PsdAssembly == null)
                {
                    s_PsdAssembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(assembly => assembly.GetName().Name == "cn.efunstudio.psd2ugui");
                }

                if (s_PsdAssembly == null)
                {
                    throw new InvalidOperationException("PSD2UIForm assembly is not loaded.");
                }

                if (s_DocumentType == null)
                {
                    s_DocumentType = s_PsdAssembly.GetType(PsdDocumentTypeName, true);
                }

                if (s_LayerType == null)
                {
                    s_LayerType = s_PsdAssembly.GetType(PsdLayerTypeName, true);
                }

                if (s_ImageSourceType == null)
                {
                    s_ImageSourceType = s_PsdAssembly.GetType(ImageSourceTypeName, true);
                }

                if (s_LayerRendererType == null)
                {
                    s_LayerRendererType = s_PsdAssembly.GetType(PsdLayerRendererTypeName, false);
                }

                if (s_RenderedImageType == null)
                {
                    s_RenderedImageType = s_PsdAssembly.GetType(PsdRenderedImageTypeName, false);
                }

                if (s_LayerRendererType != null)
                {
                    if (s_RenderLeafMethod == null)
                    {
                        s_RenderLeafMethod = s_LayerRendererType.GetMethod(
                            "RenderLeaf",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { s_LayerType },
                            null);
                    }

                    if (s_RenderGroupMethod == null)
                    {
                        s_RenderGroupMethod = s_LayerRendererType.GetMethod(
                            "RenderGroup",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { s_LayerType, typeof(bool), typeof(bool) },
                            null);
                    }
                }

            }

            private static object FindLayerByName(object document, string sourceLayerName)
            {
                IEnumerable childs = (IEnumerable)s_DocumentType.GetProperty("Childs")?.GetValue(document);
                foreach (object layer in EnumerateLayers(childs))
                {
                    string name = (string)s_LayerType.GetProperty("Name")?.GetValue(layer);
                    if (name == sourceLayerName)
                    {
                        return layer;
                    }
                }

                return null;
            }

            private static IEnumerable<object> EnumerateLayers(IEnumerable layers)
            {
                if (layers == null)
                {
                    yield break;
                }

                foreach (object layer in layers)
                {
                    yield return layer;

                    IEnumerable childs = (IEnumerable)s_LayerType.GetProperty("Childs")?.GetValue(layer);
                    foreach (object child in EnumerateLayers(childs))
                    {
                        yield return child;
                    }
                }
            }

            private static RawImage RenderLayer(object layer, bool includeHiddenRoot = false, bool includeAncestorOpacity = false)
            {
                bool isVisible = (bool)(s_LayerType.GetProperty("IsVisible")?.GetValue(layer) ?? true);
                if (!isVisible && !includeHiddenRoot)
                {
                    return RawImage.Empty;
                }

                IEnumerable childEnumerable = (IEnumerable)s_LayerType.GetProperty("Childs")?.GetValue(layer);
                object[] children = childEnumerable != null ? childEnumerable.Cast<object>().ToArray() : Array.Empty<object>();
                if (children.Length > 0)
                {
                    RawImage group = RenderGroup(children, includeHiddenRoot);
                    if (!group.IsEmpty)
                    {
                        group.MultiplyAlpha(ReadLayerOpacity(layer, includeAncestorOpacity));
                        return group;
                    }

                    return RenderWithLayerRenderer(layer, true);
                }

                RawImage leaf = RenderLeaf(layer, includeAncestorOpacity);
                return !leaf.IsEmpty ? leaf : RenderWithLayerRenderer(layer, false);
            }

            private static RawImage RenderGroup(object[] children, bool includeHiddenChildren)
            {
                List<RawImage> renderedChildren = new List<RawImage>();
                foreach (object child in children.Reverse())
                {
                    RawImage image = RenderLayer(child, includeHiddenChildren);
                    if (!image.IsEmpty)
                    {
                        renderedChildren.Add(image);
                    }
                }

                if (renderedChildren.Count == 0)
                {
                    return RawImage.Empty;
                }

                int left = renderedChildren.Min(image => image.Left);
                int top = renderedChildren.Min(image => image.Top);
                int right = renderedChildren.Max(image => image.Left + image.Width);
                int bottom = renderedChildren.Max(image => image.Top + image.Height);
                RawImage group = new RawImage(left, top, right - left, bottom - top);

                foreach (RawImage child in renderedChildren)
                {
                    group.Composite(child);
                }

                return group;
            }

            private static RawImage RenderLeaf(object layer, bool includeAncestorOpacity)
            {
                int left = (int)s_LayerType.GetProperty("Left")?.GetValue(layer);
                int top = (int)s_LayerType.GetProperty("Top")?.GetValue(layer);
                int width = ReadIntProperty(layer, "Width");
                int height = ReadIntProperty(layer, "Height");
                if (width <= 0 || height <= 0)
                {
                    return RawImage.Empty;
                }

                IEnumerable channels = (IEnumerable)s_ImageSourceType.GetProperty("Channels")?.GetValue(layer);
                Dictionary<int, byte[]> channelData = new Dictionary<int, byte[]>();
                Dictionary<int, float> channelOpacity = new Dictionary<int, float>();
                foreach (object channel in channels)
                {
                    int channelType = Convert.ToInt32(channel.GetType().GetProperty("Type")?.GetValue(channel));
                    byte[] data = (byte[])channel.GetType().GetProperty("Data")?.GetValue(channel);
                    if (data != null && data.Length >= width * height)
                    {
                        channelData[channelType] = data;
                        object opacityValue = channel.GetType().GetProperty("Opacity")?.GetValue(channel);
                        channelOpacity[channelType] = opacityValue != null ? Mathf.Clamp01(Convert.ToSingle(opacityValue)) : 1f;
                    }
                }

                if (!channelData.TryGetValue(0, out byte[] red) &&
                    !channelData.TryGetValue(1, out red) &&
                    !channelData.TryGetValue(2, out red))
                {
                    return RawImage.Empty;
                }

                channelData.TryGetValue(0, out red);
                channelData.TryGetValue(1, out byte[] green);
                channelData.TryGetValue(2, out byte[] blue);
                channelData.TryGetValue(-1, out byte[] alpha);

                if (red == null)
                {
                    red = green ?? blue;
                }

                if (green == null)
                {
                    green = red;
                }

                if (blue == null)
                {
                    blue = red;
                }

                RawImage image = new RawImage(left, top, width, height);
                float opacity = ReadLayerOpacity(layer, includeAncestorOpacity);
                if (channelOpacity.TryGetValue(-1, out float alphaOpacity))
                {
                    opacity *= alphaOpacity;
                }

                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * width;
                    int dstRow = (height - 1 - y) * width;
                    for (int x = 0; x < width; x++)
                    {
                        int src = srcRow + x;
                        int dst = (dstRow + x) * 4;
                        image.RgbaBottomUp[dst] = red[src];
                        image.RgbaBottomUp[dst + 1] = green[src];
                        image.RgbaBottomUp[dst + 2] = blue[src];
                        byte sourceAlpha = alpha != null ? alpha[src] : (byte)255;
                        image.RgbaBottomUp[dst + 3] = MultiplyAlpha(sourceAlpha, opacity);
                    }
                }

                return image;
            }

            private static int ReadIntProperty(object layer, string propertyName)
            {
                object value = s_LayerType.GetProperty(propertyName)?.GetValue(layer);
                return value != null ? Convert.ToInt32(value) : 0;
            }

            private static float ReadDocumentDimension(object document, string propertyName, float fallbackValue)
            {
                object value = s_DocumentType.GetProperty(propertyName)?.GetValue(document);
                return value != null ? Convert.ToSingle(value) : fallbackValue;
            }

            private static float ReadLayerOpacity(object layer, bool includeAncestors = false)
            {
                float opacity = ReadNormalizedLayerOpacity(layer);
                if (!includeAncestors)
                {
                    return opacity;
                }

                object parent = layer?.GetType().GetProperty("Parent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(layer);
                while (parent != null)
                {
                    opacity *= ReadNormalizedLayerOpacity(parent);

                    parent = parent.GetType().GetProperty("Parent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(parent);
                }

                return opacity;
            }

            private static float ReadNormalizedLayerOpacity(object layer)
            {
                if (layer == null)
                {
                    return 1f;
                }

                Type runtimeType = layer.GetType();
                object records = runtimeType.GetProperty("Records", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(layer);
                float? opacity = records != null
                    ? NormalizeOpacityValue(records.GetType().GetProperty("Opacity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(records))
                    : null;
                if (opacity.HasValue)
                {
                    return opacity.Value;
                }

                return NormalizeOpacityValue(runtimeType.GetProperty("Opacity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(layer)) ?? 1f;
            }

            private static float? NormalizeOpacityValue(object value)
            {
                if (value == null)
                {
                    return null;
                }

                float opacity = Convert.ToSingle(value);
                if (opacity > 1f)
                {
                    opacity /= 255f;
                }

                return Mathf.Clamp01(opacity);
            }

            private static PsdLayerHierarchyNode BuildHierarchyNode(object layer, Dictionary<string, int> siblingNameCounts)
            {
                string desiredName = SanitizeUnityObjectName((string)s_LayerType.GetProperty("Name")?.GetValue(layer));
                PsdLayerHierarchyNode node = new PsdLayerHierarchyNode
                {
                    Name = siblingNameCounts != null ? MakeUniqueSiblingName(desiredName, siblingNameCounts) : desiredName,
                    Bounds = new Rect(
                        ReadIntProperty(layer, "Left"),
                        ReadIntProperty(layer, "Top"),
                        ReadIntProperty(layer, "Width"),
                        ReadIntProperty(layer, "Height"))
                };

                IEnumerable children = (IEnumerable)s_LayerType.GetProperty("Childs")?.GetValue(layer);
                if (children != null)
                {
                    Dictionary<string, int> childNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (object child in children)
                    {
                        node.Children.Add(BuildHierarchyNode(child, childNameCounts));
                    }
                }

                return node;
            }

            private static byte MultiplyAlpha(byte alpha, float opacity)
            {
                return (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * opacity), 0, 255);
            }

            private static RawImage RenderWithLayerRenderer(object layer, bool isGroup)
            {
                if (s_RenderedImageType == null)
                {
                    return RawImage.Empty;
                }

                MethodInfo renderMethod = isGroup ? s_RenderGroupMethod : s_RenderLeafMethod;
                if (renderMethod == null)
                {
                    return RawImage.Empty;
                }

                object renderedImage;
                try
                {
                    renderedImage = isGroup
                        ? renderMethod.Invoke(null, new[] { layer, (object)false, true })
                        : renderMethod.Invoke(null, new[] { layer });
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogWarning($"PSD2UIForm renderer fallback failed: {exception.InnerException?.Message ?? exception.Message}");
                    return RawImage.Empty;
                }

                if (renderedImage == null)
                {
                    return RawImage.Empty;
                }

                bool isEmpty = (bool)(s_RenderedImageType.GetProperty("IsEmpty")?.GetValue(renderedImage) ?? true);
                int width = (int)(s_RenderedImageType.GetProperty("Width")?.GetValue(renderedImage) ?? 0);
                int height = (int)(s_RenderedImageType.GetProperty("Height")?.GetValue(renderedImage) ?? 0);
                byte[] rgbaTopDown = (byte[])s_RenderedImageType.GetProperty("Rgba32")?.GetValue(renderedImage);
                if (isEmpty || width <= 0 || height <= 0 || rgbaTopDown == null || rgbaTopDown.Length < width * height * 4)
                {
                    return RawImage.Empty;
                }

                int left = (int)(s_RenderedImageType.GetProperty("Left")?.GetValue(renderedImage) ?? 0);
                int top = (int)(s_RenderedImageType.GetProperty("Top")?.GetValue(renderedImage) ?? 0);
                RawImage image = new RawImage(left, top, width, height);
                for (int y = 0; y < height; y++)
                {
                    int src = y * width * 4;
                    int dst = (height - 1 - y) * width * 4;
                    Buffer.BlockCopy(rgbaTopDown, src, image.RgbaBottomUp, dst, width * 4);
                }

                return image;
            }
        }

        private readonly struct RawImage
        {
            public static readonly RawImage Empty = new RawImage(0, 0, 0, 0);

            public readonly int Left;
            public readonly int Top;
            public readonly int Width;
            public readonly int Height;
            public readonly byte[] RgbaBottomUp;

            public bool IsEmpty => Width <= 0 || Height <= 0 || RgbaBottomUp == null || RgbaBottomUp.Length == 0;

            public RawImage(int left, int top, int width, int height)
            {
                Left = left;
                Top = top;
                Width = Mathf.Max(0, width);
                Height = Mathf.Max(0, height);
                RgbaBottomUp = Width > 0 && Height > 0 ? new byte[Width * Height * 4] : Array.Empty<byte>();
            }

            public void Composite(RawImage source)
            {
                for (int y = 0; y < source.Height; y++)
                {
                    int dstY = Height - 1 - ((source.Top - Top) + (source.Height - 1 - y));
                    if (dstY < 0 || dstY >= Height)
                    {
                        continue;
                    }

                    for (int x = 0; x < source.Width; x++)
                    {
                        int dstX = source.Left - Left + x;
                        if (dstX < 0 || dstX >= Width)
                        {
                            continue;
                        }

                        int src = (y * source.Width + x) * 4;
                        int dst = (dstY * Width + dstX) * 4;
                        BlendPixel(source.RgbaBottomUp, src, RgbaBottomUp, dst);
                    }
                }
            }

            public void MultiplyAlpha(float opacity)
            {
                if (Mathf.Approximately(opacity, 1f))
                {
                    return;
                }

                for (int i = 3; i < RgbaBottomUp.Length; i += 4)
                {
                    RgbaBottomUp[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(RgbaBottomUp[i] * opacity), 0, 255);
                }
            }

            private static void BlendPixel(byte[] srcBytes, int src, byte[] dstBytes, int dst)
            {
                float srcA = srcBytes[src + 3] / 255f;
                if (srcA <= 0f)
                {
                    return;
                }

                float dstA = dstBytes[dst + 3] / 255f;
                float outA = srcA + dstA * (1f - srcA);
                if (outA <= 0f)
                {
                    dstBytes[dst] = 0;
                    dstBytes[dst + 1] = 0;
                    dstBytes[dst + 2] = 0;
                    dstBytes[dst + 3] = 0;
                    return;
                }

                dstBytes[dst] = BlendChannel(srcBytes[src], srcA, dstBytes[dst], dstA, outA);
                dstBytes[dst + 1] = BlendChannel(srcBytes[src + 1], srcA, dstBytes[dst + 1], dstA, outA);
                dstBytes[dst + 2] = BlendChannel(srcBytes[src + 2], srcA, dstBytes[dst + 2], dstA, outA);
                dstBytes[dst + 3] = (byte)Mathf.RoundToInt(outA * 255f);
            }

            private static byte BlendChannel(byte src, float srcA, byte dst, float dstA, float outA)
            {
                float value = (src / 255f * srcA + dst / 255f * dstA * (1f - srcA)) / outA;
                return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            }
        }
    }
}
