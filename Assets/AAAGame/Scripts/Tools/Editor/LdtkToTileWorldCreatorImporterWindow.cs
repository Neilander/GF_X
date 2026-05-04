using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GiantGrey.TileWorldCreator;
using UnityEditor;
using UnityEngine;

namespace AAAGame.Tools.Editor
{
    public sealed class LdtkToTileWorldCreatorImporterWindow : EditorWindow
    {
        private const string DefaultLdtkPath = "Assets/AAAGame/Tilemap/Ldtk/City/Lv3.ldtkl";
        private const string TemplateConfigurationPath = "Assets/AAAGame/Tilemap/Lv2.asset";
        private const string PlaneLayerName = "Plane";
        private const string WaterLayerName = "Water";
        private const string StrongholdLayerName = "SH";

        private UnityEngine.Object ldtkLevelAsset;
        private Configuration configuration;
        private TileWorldCreatorManager manager;
        private bool resizeConfiguration = true;
        private bool clearBlueprintModifiers = true;
        private Vector2 scrollPosition;
        private string lastReport;

        [MenuItem("Tools/LDtk/Import To TileWorldCreator")]
        private static void Open()
        {
            var window = GetWindow<LdtkToTileWorldCreatorImporterWindow>("LDtk To TWC");
            window.minSize = new Vector2(460f, 360f);
        }

        private void OnEnable()
        {
            if (ldtkLevelAsset == null)
            {
                ldtkLevelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefaultLdtkPath);
            }

            if (configuration == null)
            {
                configuration = AssetDatabase.LoadAssetAtPath<Configuration>(GetDefaultTargetPath());
            }

            if (manager == null)
            {
                manager = FindObjectsByType<TileWorldCreatorManager>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                    .FirstOrDefault(x => x != null && x.configuration == configuration);
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            ldtkLevelAsset = EditorGUILayout.ObjectField("LDtk level json", ldtkLevelAsset, typeof(UnityEngine.Object), false);
            configuration = (Configuration)EditorGUILayout.ObjectField("TWC configuration", configuration, typeof(Configuration), false);
            manager = (TileWorldCreatorManager)EditorGUILayout.ObjectField("TWC manager", manager, typeof(TileWorldCreatorManager), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create/Select Target From Lv2 Template"))
                {
                    CreateOrSelectTargetFromTemplate();
                }

                if (GUILayout.Button("Find Manager For Target"))
                {
                    manager = FindObjectsByType<TileWorldCreatorManager>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                        .FirstOrDefault(x => x != null && x.configuration == configuration);
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Import Rules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Plane -> Plane, Water -> Water. SH is split by 4-neighbor connected components and assigned to SH_* blueprint layers. Lv2.asset is used only as a clone template when you click the template button.", MessageType.Info);
            resizeConfiguration = EditorGUILayout.Toggle("Resize configuration", resizeConfiguration);
            clearBlueprintModifiers = EditorGUILayout.Toggle("Clear blueprint modifiers", clearBlueprintModifiers);

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Blueprint Only", GUILayout.Height(30f)))
                {
                    Import(generateBuildLayers: false);
                }

                if (GUILayout.Button("Import And Generate Build", GUILayout.Height(30f)))
                {
                    Import(generateBuildLayers: true);
                }
            }

            if (!string.IsNullOrEmpty(lastReport))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.TextArea(lastReport, GUILayout.MinHeight(120f));
            }

            EditorGUILayout.EndScrollView();
        }

        private void Import(bool generateBuildLayers)
        {
            lastReport = string.Empty;

            if (!TryGetLdtkPath(out string ldtkPath))
            {
                return;
            }

            if (configuration == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "TWC configuration is not assigned.", "OK");
                return;
            }

            if (generateBuildLayers && (manager == null || manager.configuration != configuration))
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Import and build requires a TileWorldCreatorManager using the selected configuration.", "OK");
                return;
            }

            if (!TryLoadLevel(ldtkPath, out LdtkLevelJson level))
            {
                return;
            }

            if (!TryBuildImportPlan(level, out ImportPlan plan))
            {
                return;
            }

            if (!TryFindBlueprintLayer(PlaneLayerName, out BlueprintLayer planeLayer) ||
                !TryFindBlueprintLayer(WaterLayerName, out BlueprintLayer waterLayer))
            {
                return;
            }

            if (!EnsureStrongholdLayers(plan.strongholdComponents.Count, out string ensureReport))
            {
                string message = "Failed to prepare SH layers.";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                lastReport = message;
                return;
            }

            List<BlueprintLayer> strongholdLayers = GetStrongholdBlueprintLayers();
            if (strongholdLayers.Count < plan.strongholdComponents.Count)
            {
                string message = $"SH component count still exceeds SH_* blueprint layers.\nComponents: {plan.strongholdComponents.Count}\nSH layers: {strongholdLayers.Count}";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                lastReport = message + "\n\n" + BuildStrongholdReport(plan.strongholdComponents, strongholdLayers);
                return;
            }

            Undo.RegisterCompleteObjectUndo(configuration, "Import LDtk To TWC");

            if (resizeConfiguration)
            {
                configuration.width = plan.width;
                configuration.height = plan.height;
            }

            int clearedModifierCount = 0;
            clearedModifierCount += ImportCells(planeLayer, plan.planeCells, clearBlueprintModifiers);
            clearedModifierCount += ImportCells(waterLayer, plan.waterCells, clearBlueprintModifiers);

            for (int i = 0; i < strongholdLayers.Count; i++)
            {
                HashSet<Vector2> cells = i < plan.strongholdComponents.Count
                    ? plan.strongholdComponents[i].cells
                    : new HashSet<Vector2>();
                clearedModifierCount += ImportCells(strongholdLayers[i], cells, clearBlueprintModifiers);
            }

            if (generateBuildLayers)
            {
                manager.ExecuteBuildLayers(ExecutionMode.FromScratch);
                ScheduleSaveAfterBuild(configuration);
            }
            else
            {
                MarkImportedAssetsDirty(configuration, planeLayer, waterLayer, strongholdLayers);
                AssetDatabase.SaveAssets();
            }

            lastReport = BuildImportReport(ldtkPath, plan, strongholdLayers, generateBuildLayers, clearedModifierCount);
            if (!string.IsNullOrEmpty(ensureReport))
            {
                lastReport = ensureReport + "\n\n" + lastReport;
            }
            Debug.Log(lastReport);
        }

        private string GetDefaultTargetPath()
        {
            string ldtkPath = GetSelectedLdtkPath();
            if (string.IsNullOrEmpty(ldtkPath))
            {
                return string.Empty;
            }

            string levelName = Path.GetFileNameWithoutExtension(ldtkPath);
            return $"Assets/AAAGame/Tilemap/{levelName}.asset";
        }

        private void CreateOrSelectTargetFromTemplate()
        {
            if (!TryGetLdtkPath(out string ldtkPath))
            {
                return;
            }

            string targetPath = GetDefaultTargetPath();
            if (string.IsNullOrEmpty(targetPath))
            {
                return;
            }

            Configuration existing = AssetDatabase.LoadAssetAtPath<Configuration>(targetPath);
            if (existing != null)
            {
                configuration = existing;
                manager = FindObjectsByType<TileWorldCreatorManager>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                    .FirstOrDefault(x => x != null && x.configuration == configuration);
                lastReport = $"Selected existing target: {targetPath}";
                return;
            }

            if (!AssetDatabase.CopyAsset(TemplateConfigurationPath, targetPath))
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Failed to clone template:\n{TemplateConfigurationPath}\n-> {targetPath}", "OK");
                return;
            }

            AssetDatabase.ImportAsset(targetPath);
            configuration = AssetDatabase.LoadAssetAtPath<Configuration>(targetPath);
            if (configuration == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Cloned asset could not be loaded:\n{targetPath}", "OK");
                return;
            }

            configuration.name = Path.GetFileNameWithoutExtension(ldtkPath);
            EditorUtility.SetDirty(configuration);
            AssetDatabase.SaveAssets();
            manager = null;
            lastReport = $"Created target from Lv2 template: {targetPath}";
        }

        private string GetSelectedLdtkPath()
        {
            return ldtkLevelAsset != null ? AssetDatabase.GetAssetPath(ldtkLevelAsset) : string.Empty;
        }

        private bool TryGetLdtkPath(out string path)
        {
            path = ldtkLevelAsset != null ? AssetDatabase.GetAssetPath(ldtkLevelAsset) : string.Empty;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                EditorUtility.DisplayDialog("LDtk import failed", "LDtk level json path is invalid.", "OK");
                return false;
            }

            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".ldtkl", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Please select a .ldtkl or .json file.", "OK");
                return false;
            }

            return true;
        }

        private static bool TryLoadLevel(string path, out LdtkLevelJson level)
        {
            level = null;

            try
            {
                string json = File.ReadAllText(path);
                level = JsonUtility.FromJson<LdtkLevelJson>(json);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("LDtk import failed", ex.Message, "OK");
                return false;
            }

            if (level == null || level.layerInstances == null || level.layerInstances.Length == 0)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "The selected file does not look like an LDtk level json.", "OK");
                return false;
            }

            return true;
        }

        private bool TryBuildImportPlan(LdtkLevelJson level, out ImportPlan plan)
        {
            plan = null;

            if (!TryFindLdtkLayer(level, PlaneLayerName, out LdtkLayerInstance plane) ||
                !TryFindLdtkLayer(level, WaterLayerName, out LdtkLayerInstance water) ||
                !TryFindLdtkLayer(level, StrongholdLayerName, out LdtkLayerInstance stronghold))
            {
                return false;
            }

            int width = plane.__cWid;
            int height = plane.__cHei;
            if (!ValidateSameGrid(plane, water, stronghold))
            {
                return false;
            }

            var strongholdCells = ReadIntGridCells(stronghold, width, height);
            plan = new ImportPlan
            {
                width = width,
                height = height,
                planeCells = ReadIntGridCells(plane, width, height),
                waterCells = ReadIntGridCells(water, width, height),
                strongholdComponents = SplitConnectedComponents(strongholdCells)
            };

            return true;
        }

        private static bool TryFindLdtkLayer(LdtkLevelJson level, string layerName, out LdtkLayerInstance layer)
        {
            layer = level.layerInstances.FirstOrDefault(x =>
                x != null &&
                string.Equals(x.__identifier, layerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.__type, "IntGrid", StringComparison.OrdinalIgnoreCase));

            if (layer == null || layer.intGridCsv == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Missing IntGrid layer: {layerName}", "OK");
                return false;
            }

            return true;
        }

        private static bool ValidateSameGrid(params LdtkLayerInstance[] layers)
        {
            int width = layers[0].__cWid;
            int height = layers[0].__cHei;
            int gridSize = layers[0].__gridSize;

            foreach (var layer in layers)
            {
                if (layer.__cWid != width || layer.__cHei != height || layer.__gridSize != gridSize)
                {
                    EditorUtility.DisplayDialog("LDtk import failed", "Plane, Water and SH must use the same LDtk grid.", "OK");
                    return false;
                }

                if (layer.intGridCsv.Length != width * height)
                {
                    EditorUtility.DisplayDialog("LDtk import failed", $"Layer {layer.__identifier} has invalid IntGrid data length.", "OK");
                    return false;
                }
            }

            return true;
        }

        private bool TryFindBlueprintLayer(string layerName, out BlueprintLayer layer)
        {
            layer = configuration.GetBlueprintLayerByGuid(configuration.GetBlueprintLayerGuid(layerName));
            if (layer == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Missing TWC blueprint layer: {layerName}", "OK");
                return false;
            }

            return true;
        }

        private List<BlueprintLayer> GetStrongholdBlueprintLayers()
        {
            var layers = new List<BlueprintLayer>();
            foreach (var folder in configuration.blueprintLayerFolders)
            {
                if (folder?.blueprintLayers == null)
                {
                    continue;
                }

                foreach (var layer in folder.blueprintLayers)
                {
                    if (layer != null && IsStrongholdLayerName(layer.layerName))
                    {
                        layers.Add(layer);
                    }
                }
            }

            layers.Sort(CompareStrongholdLayers);
            return layers;
        }

        private bool EnsureStrongholdLayers(int requiredCount, out string report)
        {
            report = string.Empty;

            List<BlueprintLayer> existingBlueprintLayers = GetStrongholdBlueprintLayers();
            if (existingBlueprintLayers.Count == 0)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "No SH_* blueprint layer exists in the selected configuration.", "OK");
                return false;
            }

            BlueprintLayer templateBlueprint = existingBlueprintLayers[existingBlueprintLayers.Count - 1];
            TilesBuildLayer templateBuildLayer = GetStrongholdBuildLayer(templateBlueprint.layerName);
            if (templateBuildLayer == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Missing build layer for template stronghold layer: {templateBlueprint.layerName}", "OK");
                return false;
            }

            var created = new List<string>();
            while (existingBlueprintLayers.Count < requiredCount)
            {
                string nextLayerName = BuildNextStrongholdLayerName(existingBlueprintLayers[existingBlueprintLayers.Count - 1].layerName);
                string nextBuildLayerName = "Build " + nextLayerName;

                if (!TryCloneBlueprintLayer(templateBlueprint, nextLayerName, out BlueprintLayer newBlueprint))
                {
                    EditorUtility.DisplayDialog("LDtk import failed", $"Failed to create blueprint layer: {nextLayerName}", "OK");
                    return false;
                }

                if (!TryCloneBuildLayer(templateBuildLayer, newBlueprint, nextBuildLayerName, out TilesBuildLayer newBuildLayer))
                {
                    EditorUtility.DisplayDialog("LDtk import failed", $"Failed to create build layer: {nextBuildLayerName}", "OK");
                    return false;
                }

                existingBlueprintLayers.Add(newBlueprint);
                created.Add(nextLayerName);
            }

            if (created.Count > 0)
            {
                report = "Created missing stronghold layers: " + string.Join(", ", created);
            }
            else if (existingBlueprintLayers.Count > requiredCount)
            {
                var extraLayerNames = existingBlueprintLayers.Skip(requiredCount).Select(x => x.layerName).ToArray();
                report = "Cleared extra stronghold layers: " + string.Join(", ", extraLayerNames);
            }

            return true;
        }

        private bool TryCloneBlueprintLayer(BlueprintLayer template, string layerName, out BlueprintLayer newLayer)
        {
            newLayer = ScriptableObject.CreateInstance<BlueprintLayer>();
            newLayer.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(newLayer, configuration);

            EditorUtility.CopySerialized(template, newLayer);
            newLayer.layerName = layerName;
            newLayer.guid = Guid.NewGuid().ToString();
            newLayer.tileMapModifiers = new List<BlueprintModifier>();
            newLayer.ClearLayer(false);

            BlueprintLayerFolder folder = FindBlueprintFolder(template);
            if (folder == null)
            {
                if (configuration.blueprintLayerFolders == null)
                {
                    configuration.blueprintLayerFolders = new List<BlueprintLayerFolder>();
                }

                folder = configuration.blueprintLayerFolders.FirstOrDefault();
                if (folder == null)
                {
                    folder = new BlueprintLayerFolder("Root");
                    configuration.blueprintLayerFolders.Add(folder);
                }
            }

            folder.blueprintLayers.Add(newLayer);
            EditorUtility.SetDirty(newLayer);
            EditorUtility.SetDirty(configuration);
            return true;
        }

        private bool TryCloneBuildLayer(TilesBuildLayer template, BlueprintLayer blueprintLayer, string layerName, out TilesBuildLayer newLayer)
        {
            newLayer = ScriptableObject.CreateInstance<TilesBuildLayer>();
            newLayer.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(newLayer, configuration);

            EditorUtility.CopySerialized(template, newLayer);
            newLayer.layerName = layerName;
            newLayer.guid = Guid.NewGuid().ToString();
            newLayer.assignedBlueprintLayerGuid = blueprintLayer.guid;
            newLayer.currentBlueprintLayer = blueprintLayer;
            newLayer.hierarchyLayerID = newLayer.guid;
            newLayer.configuration = configuration;
            newLayer.ResetLayer(manager != null && manager.configuration == configuration ? manager : null);

            BuildLayerFolder folder = FindBuildFolder(template);
            if (folder == null)
            {
                if (configuration.buildLayerFolders == null)
                {
                    configuration.buildLayerFolders = new List<BuildLayerFolder>();
                }

                folder = configuration.buildLayerFolders.FirstOrDefault();
                if (folder == null)
                {
                    folder = new BuildLayerFolder("Root");
                    configuration.buildLayerFolders.Add(folder);
                }
            }

            folder.buildLayers.Add(newLayer);
            EditorUtility.SetDirty(newLayer);
            EditorUtility.SetDirty(configuration);
            return true;
        }

        private BlueprintLayerFolder FindBlueprintFolder(BlueprintLayer layer)
        {
            foreach (var folder in configuration.blueprintLayerFolders)
            {
                if (folder?.blueprintLayers != null && folder.blueprintLayers.Contains(layer))
                {
                    return folder;
                }
            }

            return null;
        }

        private BuildLayerFolder FindBuildFolder(BuildLayer layer)
        {
            foreach (var folder in configuration.buildLayerFolders)
            {
                if (folder?.buildLayers != null && folder.buildLayers.Contains(layer))
                {
                    return folder;
                }
            }

            return null;
        }

        private TilesBuildLayer GetStrongholdBuildLayer(string blueprintLayerName)
        {
            string buildLayerName = "Build " + blueprintLayerName;
            foreach (var folder in configuration.buildLayerFolders)
            {
                if (folder?.buildLayers == null)
                {
                    continue;
                }

                foreach (var layer in folder.buildLayers)
                {
                    if (layer is TilesBuildLayer tilesBuildLayer && string.Equals(tilesBuildLayer.layerName, buildLayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return tilesBuildLayer;
                    }
                }
            }

            return null;
        }

        private static string BuildNextStrongholdLayerName(string currentLayerName)
        {
            if (!ParseStrongholdName(currentLayerName, out int factionId, out int index))
            {
                return currentLayerName + "_1";
            }

            return $"SH_{factionId}_{index + 1}";
        }

        private static bool IsStrongholdLayerName(string layerName)
        {
            return Regex.IsMatch(layerName ?? string.Empty, @"^SH_\d+_\d+$", RegexOptions.IgnoreCase);
        }

        private static int CompareStrongholdLayers(BlueprintLayer a, BlueprintLayer b)
        {
            ParseStrongholdName(a.layerName, out int factionA, out int indexA);
            ParseStrongholdName(b.layerName, out int factionB, out int indexB);

            int factionCompare = factionA.CompareTo(factionB);
            return factionCompare != 0 ? factionCompare : indexA.CompareTo(indexB);
        }

        private static bool ParseStrongholdName(string layerName, out int factionId, out int index)
        {
            factionId = int.MaxValue;
            index = int.MaxValue;

            string[] parts = (layerName ?? string.Empty).Split('_');
            if (parts.Length == 3)
            {
                bool parsedFaction = int.TryParse(parts[1], out factionId);
                bool parsedIndex = int.TryParse(parts[2], out index);
                return parsedFaction && parsedIndex;
            }

            return false;
        }

        private static HashSet<Vector2> ReadIntGridCells(LdtkLayerInstance layer, int width, int height)
        {
            var cells = new HashSet<Vector2>();
            int offsetX = Mathf.RoundToInt((layer.__pxTotalOffsetX + layer.pxOffsetX) / (float)layer.__gridSize);
            int offsetY = Mathf.RoundToInt((layer.__pxTotalOffsetY + layer.pxOffsetY) / (float)layer.__gridSize);

            for (int i = 0; i < layer.intGridCsv.Length; i++)
            {
                if (layer.intGridCsv[i] == 0)
                {
                    continue;
                }

                int ldtkX = i % width + offsetX;
                int ldtkY = i / width + offsetY;
                int twcY = height - 1 - ldtkY;
                cells.Add(new Vector2(ldtkX, twcY));
            }

            return cells;
        }

        private static List<StrongholdComponent> SplitConnectedComponents(HashSet<Vector2> cells)
        {
            var result = new List<StrongholdComponent>();
            var visited = new HashSet<Vector2>();
            var directions = new[]
            {
                new Vector2(1, 0),
                new Vector2(-1, 0),
                new Vector2(0, 1),
                new Vector2(0, -1)
            };

            foreach (var start in cells)
            {
                if (visited.Contains(start))
                {
                    continue;
                }

                var componentCells = new HashSet<Vector2>();
                var queue = new Queue<Vector2>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    Vector2 current = queue.Dequeue();
                    componentCells.Add(current);

                    foreach (var direction in directions)
                    {
                        Vector2 next = current + direction;
                        if (cells.Contains(next) && visited.Add(next))
                        {
                            queue.Enqueue(next);
                        }
                    }
                }

                result.Add(new StrongholdComponent(componentCells));
            }

            result.Sort(CompareComponentsByLdtkReadingOrder);
            return result;
        }

        private static int CompareComponentsByLdtkReadingOrder(StrongholdComponent a, StrongholdComponent b)
        {
            int topCompare = b.MaxY.CompareTo(a.MaxY);
            return topCompare != 0 ? topCompare : a.MinX.CompareTo(b.MinX);
        }

        private static int ImportCells(BlueprintLayer layer, HashSet<Vector2> cells, bool clearModifiers)
        {
            Undo.RegisterCompleteObjectUndo(layer, "Import LDtk Layer");
            int clearedModifierCount = clearModifiers ? ClearBlueprintModifiers(layer) : 0;
            layer.ClearLayer(false);
            layer.AddCells(cells);
            EditorUtility.SetDirty(layer);
            return clearedModifierCount;
        }

        private static int ClearBlueprintModifiers(BlueprintLayer layer)
        {
            if (layer.tileMapModifiers == null || layer.tileMapModifiers.Count == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = layer.tileMapModifiers.Count - 1; i >= 0; i--)
            {
                BlueprintModifier modifier = layer.tileMapModifiers[i];
                if (modifier == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(modifier);
                count++;
            }

            layer.tileMapModifiers.Clear();
            return count;
        }

        private static void MarkImportedAssetsDirty(Configuration configuration, BlueprintLayer planeLayer, BlueprintLayer waterLayer, List<BlueprintLayer> strongholdLayers)
        {
            EditorUtility.SetDirty(configuration);
            planeLayer.OnBeforeSerialize();
            waterLayer.OnBeforeSerialize();
            EditorUtility.SetDirty(planeLayer);
            EditorUtility.SetDirty(waterLayer);
            foreach (var layer in strongholdLayers)
            {
                layer.OnBeforeSerialize();
                EditorUtility.SetDirty(layer);
            }
        }

        private static void ScheduleSaveAfterBuild(Configuration configuration)
        {
            int framesLeft = 90;
            EditorApplication.CallbackFunction update = null;
            update = () =>
            {
                framesLeft--;
                if (framesLeft > 0)
                {
                    return;
                }

                EditorApplication.update -= update;
                EditorUtility.SetDirty(configuration);
                foreach (var folder in configuration.blueprintLayerFolders)
                {
                    if (folder?.blueprintLayers == null)
                    {
                        continue;
                    }

                    foreach (var blueprintLayer in folder.blueprintLayers)
                    {
                        if (blueprintLayer == null)
                        {
                            continue;
                        }

                        blueprintLayer.OnBeforeSerialize();
                        EditorUtility.SetDirty(blueprintLayer);
                    }
                }

                foreach (var folder in configuration.buildLayerFolders)
                {
                    if (folder?.buildLayers == null)
                    {
                        continue;
                    }

                    foreach (var buildLayer in folder.buildLayers)
                    {
                        if (buildLayer == null)
                        {
                            continue;
                        }

                        if (buildLayer is ISerializationCallbackReceiver receiver)
                        {
                            receiver.OnBeforeSerialize();
                        }

                        EditorUtility.SetDirty(buildLayer);
                    }
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[LDtk Import] Build layers saved after TileWorldCreator editor generation.");
            };

            EditorApplication.update += update;
        }

        private static string BuildImportReport(string path, ImportPlan plan, List<BlueprintLayer> strongholdLayers, bool generatedBuildLayers, int clearedModifierCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[LDtk Import] Completed");
            builder.AppendLine($"Source: {path}");
            builder.AppendLine($"Size: {plan.width} x {plan.height}");
            builder.AppendLine($"Plane cells: {plan.planeCells.Count}");
            builder.AppendLine($"Water cells: {plan.waterCells.Count}");
            builder.AppendLine($"SH components: {plan.strongholdComponents.Count}");
            builder.AppendLine($"Cleared blueprint modifiers: {clearedModifierCount}");
            builder.AppendLine($"Generated build layers: {generatedBuildLayers}");
            builder.Append(BuildStrongholdReport(plan.strongholdComponents, strongholdLayers));
            return builder.ToString();
        }

        private static string BuildStrongholdReport(List<StrongholdComponent> components, List<BlueprintLayer> layers)
        {
            var builder = new StringBuilder();
            int count = Mathf.Max(components.Count, layers.Count);
            for (int i = 0; i < count; i++)
            {
                string layerName = i < layers.Count ? layers[i].layerName : "<missing layer>";
                string component = i < components.Count ? components[i].ToString() : "<missing component>";
                builder.AppendLine($"SH[{i}] {component} -> {layerName}");
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class LdtkLevelJson
        {
            public string identifier;
            public int pxWid;
            public int pxHei;
            public LdtkLayerInstance[] layerInstances;
        }

        [Serializable]
        private sealed class LdtkLayerInstance
        {
            public string __identifier;
            public string __type;
            public int __cWid;
            public int __cHei;
            public int __gridSize;
            public int __pxTotalOffsetX;
            public int __pxTotalOffsetY;
            public int pxOffsetX;
            public int pxOffsetY;
            public int[] intGridCsv;
        }

        private sealed class ImportPlan
        {
            public int width;
            public int height;
            public HashSet<Vector2> planeCells;
            public HashSet<Vector2> waterCells;
            public List<StrongholdComponent> strongholdComponents;
        }

        private readonly struct StrongholdComponent
        {
            public readonly HashSet<Vector2> cells;
            public readonly float MinX;
            public readonly float MaxX;
            public readonly float MinY;
            public readonly float MaxY;

            public StrongholdComponent(HashSet<Vector2> cells)
            {
                this.cells = cells;
                MinX = cells.Min(x => x.x);
                MaxX = cells.Max(x => x.x);
                MinY = cells.Min(x => x.y);
                MaxY = cells.Max(x => x.y);
            }

            public override string ToString()
            {
                return $"count={cells.Count}, twcBounds=({MinX},{MinY})-({MaxX},{MaxY})";
            }
        }
    }
}
