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
        private const string ScaleUserDataPrefix = "Psd2UIFormScaledHash:";
        private const string PsdLayerNodeTypeName = "UGF.EditorTools.Psd2UGUI.PsdLayerNode";
        private const string PsdConverterTypeName = "UGF.EditorTools.Psd2UGUI.Psd2UIFormConverter";
        private const string PsdDocumentTypeName = "cn.efunstudio.psdreader.PsdParser.PsdDocument";
        private const string PsdLayerTypeName = "cn.efunstudio.psdreader.PsdParser.PsdLayer";
        private const string ImageSourceTypeName = "cn.efunstudio.psdreader.PsdParser.IImageSource";
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
        private static bool s_InitialScanRegistered;

        [InitializeOnLoadMethod]
        private static void RegisterInitialScan()
        {
            if (s_InitialScanRegistered)
            {
                return;
            }

            s_InitialScanRegistered = true;
            EditorApplication.delayCall += ProcessExistingGeneratedPrefabs;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string assetPath in importedAssets)
            {
                if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    if (assetPath.EndsWith("_psd_layers_parsed.prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        PendingParsedPrefabPaths.Add(assetPath);
                    }
                    else
                    {
                        PendingPrefabPaths.Add(assetPath);
                    }
                }
                else if (assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    PendingImagePaths.Add(assetPath);
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

            bool changed = false;
            foreach (string path in parsedPrefabPaths)
            {
                changed |= SanitizeParsedPrefab(path);
            }

            Dictionary<string, ParsedPrefabInfo> parsedPrefabs = BuildParsedPrefabMap();
            foreach (string path in prefabPaths)
            {
                changed |= ScalePrefabIfNeeded(path, parsedPrefabs, parsedPrefabPaths.Length > 0);
            }

            ExportMap exportMap = BuildExportMap();
            HashSet<string> imagePathsToReplace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in imagePaths)
            {
                imagePathsToReplace.Add(NormalizeAssetPath(path));
            }

            if (prefabPaths.Length > 0 || parsedPrefabPaths.Length > 0)
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
                AssetDatabase.Refresh();
            }
        }

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
            if (scaleX <= 0f || scaleY <= 0f || (Mathf.Approximately(scaleX, 1f) && Mathf.Approximately(scaleY, 1f)))
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

                RectTransform[] rectTransforms = root.GetComponentsInChildren<RectTransform>(true);
                if (rectTransforms.Length <= 1)
                {
                    return false;
                }

                saved |= NormalizeRootRect(root.transform as RectTransform);
                saved |= EnsureRootCanvasScaler(root);
                saved |= RenameGeneratedPrefabObjects(root, info);
                bool shouldScale = !sameScaledContent && (fromGenerationSignal || IsInPsdCoordinateSpace(rectTransforms, root.transform, info.PsdSize));

                if (shouldScale)
                {
                    foreach (RectTransform rectTransform in rectTransforms)
                    {
                        if (rectTransform.transform == root.transform)
                        {
                            continue;
                        }

                        ScaleRectTransform(rectTransform, scaleX, scaleY);
                    }

                    float textScale = Mathf.Min(scaleX, scaleY);
                    ScaleText(root, textScale);
                    ScaleLayouts(root, scaleX, scaleY, textScale);

                    saved = true;
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

            saved |= RenameGeneratedPrefabAssetObjectNames(prefabPath, info);
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

            Debug.Log($"PSD2UIForm generated prefab scaled by PSD size: {prefabPath} ({info.PsdSize.x:0.#}x{info.PsdSize.y:0.#} -> {TargetWidth:0.#}x{TargetHeight:0.#}, scale {scaleX:0.####},{scaleY:0.####})");
            return true;
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

        private static void ScaleRectTransform(RectTransform rectTransform, float scaleX, float scaleY)
        {
            Vector2 anchoredPosition = rectTransform.anchoredPosition;
            Vector2 sizeDelta = rectTransform.sizeDelta;
            rectTransform.anchoredPosition = new Vector2(anchoredPosition.x * scaleX, anchoredPosition.y * scaleY);
            rectTransform.sizeDelta = new Vector2(sizeDelta.x * scaleX, sizeDelta.y * scaleY);
        }

        private static void ScaleText(GameObject root, float scale)
        {
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                text.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * scale));
                text.lineSpacing *= scale;
            }

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                text.fontSize *= scale;
                text.fontSizeMin *= scale;
                text.fontSizeMax *= scale;
                text.lineSpacing *= scale;
                text.paragraphSpacing *= scale;
                text.characterSpacing *= scale;
                text.wordSpacing *= scale;
                text.margin *= scale;
            }
        }

        private static void ScaleLayouts(GameObject root, float scaleX, float scaleY, float uniformScale)
        {
            foreach (LayoutElement layout in root.GetComponentsInChildren<LayoutElement>(true))
            {
                layout.minWidth = ScaleIfNonNegative(layout.minWidth, scaleX);
                layout.minHeight = ScaleIfNonNegative(layout.minHeight, scaleY);
                layout.preferredWidth = ScaleIfNonNegative(layout.preferredWidth, scaleX);
                layout.preferredHeight = ScaleIfNonNegative(layout.preferredHeight, scaleY);
            }

            foreach (GridLayoutGroup grid in root.GetComponentsInChildren<GridLayoutGroup>(true))
            {
                grid.cellSize = new Vector2(grid.cellSize.x * scaleX, grid.cellSize.y * scaleY);
                grid.spacing = new Vector2(grid.spacing.x * scaleX, grid.spacing.y * scaleY);
                ScalePadding(grid.padding, scaleX, scaleY);
            }

            foreach (HorizontalOrVerticalLayoutGroup layoutGroup in root.GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true))
            {
                layoutGroup.spacing *= uniformScale;
                ScalePadding(layoutGroup.padding, scaleX, scaleY);
            }
        }

        private static float ScaleIfNonNegative(float value, float scale)
        {
            return value >= 0f ? value * scale : value;
        }

        private static void ScalePadding(RectOffset padding, float scaleX, float scaleY)
        {
            padding.left = Mathf.RoundToInt(padding.left * scaleX);
            padding.right = Mathf.RoundToInt(padding.right * scaleX);
            padding.top = Mathf.RoundToInt(padding.top * scaleY);
            padding.bottom = Mathf.RoundToInt(padding.bottom * scaleY);
        }

        private static bool ReplaceExportedImageIfNeeded(string pngPath, ExportMap exportMap)
        {
            if (!exportMap.EntriesByPngPath.TryGetValue(NormalizeAssetPath(pngPath), out ExportEntry entry))
            {
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

        private static bool RenameGeneratedPrefabObjects(GameObject root, ParsedPrefabInfo info)
        {
            if (info.GeneratedNamesByLocalId.Count == 0)
            {
                return false;
            }

            bool changed = false;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!TryGetLocalFileIdentifier(transform.gameObject, out long localId))
                {
                    continue;
                }

                if (!info.GeneratedNamesByLocalId.TryGetValue(localId, out string desiredName) ||
                    string.IsNullOrWhiteSpace(desiredName) ||
                    transform.gameObject.name == desiredName)
                {
                    continue;
                }

                transform.gameObject.name = desiredName;
                EditorUtility.SetDirty(transform.gameObject);
                changed = true;
            }

            return changed;
        }

        private static bool RenameGeneratedPrefabAssetObjectNames(string prefabPath, ParsedPrefabInfo info)
        {
            if (info.GeneratedNamesByLocalId.Count == 0)
            {
                return false;
            }

            string fullPath = Path.GetFullPath(prefabPath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            string[] lines = File.ReadAllLines(fullPath);
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!TryParseGameObjectYamlHeader(lines[i], out long localId) ||
                    !info.GeneratedNamesByLocalId.TryGetValue(localId, out string desiredName))
                {
                    continue;
                }

                for (int j = i + 1; j < lines.Length && !lines[j].StartsWith("--- ", StringComparison.Ordinal); j++)
                {
                    if (!lines[j].StartsWith("  m_Name:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string newLine = $"  m_Name: {FormatYamlString(desiredName)}";
                    if (lines[j] != newLine)
                    {
                        lines[j] = newLine;
                        changed = true;
                    }

                    break;
                }
            }

            if (!changed)
            {
                return false;
            }

            File.WriteAllLines(fullPath, lines);
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"PSD2UIForm generated prefab object names restored from metadata: {prefabPath}");
            return true;
        }

        private static bool TryParseGameObjectYamlHeader(string line, out long localId)
        {
            localId = 0;
            const string prefix = "--- !u!1 &";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return long.TryParse(line.Substring(prefix.Length), out localId);
        }

        private static string FormatYamlString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool quote = false;
            foreach (char c in value)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                {
                    quote = true;
                    break;
                }

                if (c > 127)
                {
                    quote = true;
                    break;
                }
            }

            if (!quote)
            {
                return value;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                if (c == '\\' || c == '"')
                {
                    builder.Append('\\').Append(c);
                }
                else if (c == '\n')
                {
                    builder.Append("\\n");
                }
                else if (c == '\r')
                {
                    builder.Append("\\r");
                }
                else if (c < 32 || c > 127)
                {
                    builder.Append("\\u").Append(((int)c).ToString("X4"));
                }
                else
                {
                    builder.Append(c);
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static bool TryGetLocalFileIdentifier(UnityEngine.Object assetObject, out long localId)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetObject, out string _, out localId))
            {
                return true;
            }

            Type unsupportedType = typeof(Editor).Assembly.GetType("UnityEditor.Unsupported");
            MethodInfo method = unsupportedType?.GetMethod(
                "GetLocalIdentifierInFile",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(UnityEngine.Object) },
                null);
            if (method == null)
            {
                localId = 0;
                return false;
            }

            localId = Convert.ToInt64(method.Invoke(null, new object[] { assetObject }));
            return localId != 0;
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
            Dictionary<string, ParsedPrefabInfo> parsedPrefabs = BuildParsedPrefabMap();
            foreach (ParsedPrefabInfo info in parsedPrefabs.Values)
            {
                string imageOutputDir = $"Assets/AAAGame/Sprites/{info.UiFormName}";
                foreach (ExportEntry entry in info.Entries)
                {
                    entry.PsdAssetPath = info.PsdAssetPath;
                    foreach (string exportName in GetExportFileNameCandidates(entry.ObjectName))
                    {
                        string pngPath = NormalizeAssetPath($"{imageOutputDir}/{exportName}.png");
                        map.EntriesByPngPath[pngPath] = entry;
                    }
                }
            }

            return map;
        }

        private static IEnumerable<string> GetExportFileNameCandidates(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                yield break;
            }

            yield return objectName;

            string normalizedName = NormalizePsd2UIFormFileName(objectName);
            if (!string.Equals(normalizedName, objectName, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(normalizedName))
            {
                yield return normalizedName;
            }

            string suffixBaseName = !string.IsNullOrWhiteSpace(normalizedName) ? normalizedName : objectName;
            for (int i = 1; i <= 32; i++)
            {
                yield return $"{suffixBaseName}_{i}";
            }
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

        private static Dictionary<string, ParsedPrefabInfo> BuildParsedPrefabMap()
        {
            Dictionary<string, ParsedPrefabInfo> map = new Dictionary<string, ParsedPrefabInfo>(StringComparer.OrdinalIgnoreCase);
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
                string uiFormPath = NormalizeAssetPath($"Assets/AAAGame/Prefabs/UI/{info.UiFormName}.prefab");
                map[uiFormPath] = info;
            }

            return map;
        }

        private static ParsedPrefabInfo ReadParsedPrefabInfo(GameObject prefab)
        {
            ParsedPrefabInfo info = new ParsedPrefabInfo();
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
                    ReadGeneratedMetadata(serializedObject, info);
                }
                else if (type.FullName == PsdLayerNodeTypeName)
                {
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
                        ObjectName = behaviour.gameObject.name,
                        SourceLayerName = sourceLayerName.stringValue
                    });
                }
            }

            return info;
        }

        private static void ReadGeneratedMetadata(SerializedObject converterObject, ParsedPrefabInfo info)
        {
            SerializedProperty metadataEntries = converterObject.FindProperty("generatedMetadataEntries");
            if (metadataEntries == null || !metadataEntries.isArray)
            {
                return;
            }

            for (int metadataIndex = 0; metadataIndex < metadataEntries.arraySize; metadataIndex++)
            {
                SerializedProperty metadata = metadataEntries.GetArrayElementAtIndex(metadataIndex);
                SerializedProperty entries = metadata.FindPropertyRelative("Entries");
                if (entries == null || !entries.isArray)
                {
                    continue;
                }

                for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
                    string globalObjectId = entry.FindPropertyRelative("GlobalObjectId")?.stringValue;
                    string key = entry.FindPropertyRelative("Key")?.stringValue;
                    if (string.IsNullOrWhiteSpace(globalObjectId) ||
                        string.IsNullOrWhiteSpace(key) ||
                        !TryParseGlobalObjectLocalId(globalObjectId, out long localId))
                    {
                        continue;
                    }

                    string generatedName = GetGeneratedObjectNameFromKey(key);
                    if (!string.IsNullOrWhiteSpace(generatedName))
                    {
                        info.GeneratedNamesByLocalId[localId] = generatedName;
                    }
                }
            }
        }

        private static bool TryParseGlobalObjectLocalId(string globalObjectId, out long localId)
        {
            localId = 0;
            int lastDash = globalObjectId.LastIndexOf('-');
            if (lastDash <= 0)
            {
                return false;
            }

            int previousDash = globalObjectId.LastIndexOf('-', lastDash - 1);
            if (previousDash < 0 || previousDash + 1 >= lastDash)
            {
                return false;
            }

            string localIdText = globalObjectId.Substring(previousDash + 1, lastDash - previousDash - 1);
            return long.TryParse(localIdText, out localId);
        }

        private static string GetGeneratedObjectNameFromKey(string key)
        {
            string name = key;
            int slashIndex = key.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex + 1 < key.Length)
            {
                name = key.Substring(slashIndex + 1);
            }

            name = name.Replace('[', '_').Replace("]", string.Empty);
            return SanitizeUnityObjectName(name);
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
            public readonly List<ExportEntry> Entries = new List<ExportEntry>();
            public readonly Dictionary<long, string> GeneratedNamesByLocalId = new Dictionary<long, string>();
        }

        private sealed class ExportEntry
        {
            public string ObjectName;
            public string SourceLayerName;
            public string PsdAssetPath;
        }

        private static class PsdRawPngExporter
        {
            private static Assembly s_PsdAssembly;
            private static Type s_DocumentType;
            private static Type s_LayerType;
            private static Type s_ImageSourceType;

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

                    RawImage image = RenderLayer(layer);
                    if (image.Width <= 0 || image.Height <= 0)
                    {
                        throw new InvalidOperationException($"PSD layer has no pixels: {sourceLayerName}");
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

            private static RawImage RenderLayer(object layer)
            {
                bool isVisible = (bool)(s_LayerType.GetProperty("IsVisible")?.GetValue(layer) ?? true);
                if (!isVisible)
                {
                    return RawImage.Empty;
                }

                IEnumerable childEnumerable = (IEnumerable)s_LayerType.GetProperty("Childs")?.GetValue(layer);
                object[] children = childEnumerable != null ? childEnumerable.Cast<object>().ToArray() : Array.Empty<object>();
                if (children.Length > 0)
                {
                    return RenderGroup(children);
                }

                return RenderLeaf(layer);
            }

            private static RawImage RenderGroup(object[] children)
            {
                List<RawImage> renderedChildren = new List<RawImage>();
                foreach (object child in children.Reverse())
                {
                    RawImage image = RenderLayer(child);
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

            private static RawImage RenderLeaf(object layer)
            {
                int left = (int)s_LayerType.GetProperty("Left")?.GetValue(layer);
                int top = (int)s_LayerType.GetProperty("Top")?.GetValue(layer);
                int width = (int)s_ImageSourceType.GetProperty("Width")?.GetValue(layer);
                int height = (int)s_ImageSourceType.GetProperty("Height")?.GetValue(layer);
                if (width <= 0 || height <= 0)
                {
                    return RawImage.Empty;
                }

                IEnumerable channels = (IEnumerable)s_ImageSourceType.GetProperty("Channels")?.GetValue(layer);
                Dictionary<int, byte[]> channelData = new Dictionary<int, byte[]>();
                foreach (object channel in channels)
                {
                    int channelType = Convert.ToInt32(channel.GetType().GetProperty("Type")?.GetValue(channel));
                    byte[] data = (byte[])channel.GetType().GetProperty("Data")?.GetValue(channel);
                    if (data != null && data.Length >= width * height)
                    {
                        channelData[channelType] = data;
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
                        image.RgbaBottomUp[dst + 3] = alpha != null ? alpha[src] : (byte)255;
                    }
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
