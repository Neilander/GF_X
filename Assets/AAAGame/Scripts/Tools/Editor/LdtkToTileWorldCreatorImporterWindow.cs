using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GiantGrey.TileWorldCreator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace AAAGame.Tools.Editor
{
    public sealed class LdtkToTileWorldCreatorImporterWindow : EditorWindow
    {
        private const string DefaultLdtkPath = "Assets/AAAGame/Tilemap/Ldtk/City/Lv3.ldtkl";
        private const string TemplateConfigurationPath = "Assets/AAAGame/Tilemap/Lv3.asset";
        private const string PlaneLayerName = "Plane";
        private const string WaterLayerName = "Water";
        private const string StrongholdLayerName = "SH";
        private const string EntityLayerName = "Entities";
        private const string LevelPrefabTemplatePath = "Assets/AAAGame/Prefabs/Entity/Level/Level_2.prefab";
        private const string LevelPrefabFolderPath = "Assets/AAAGame/Prefabs/Entity/Level";
        private const string TemplateTerrainPrefabPath = "Assets/AAAGame/Tilemap/Lv2.prefab";
        private const string TerrainPrefabFolderPath = "Assets/AAAGame/Tilemap";
        private const string EntityPresetPointPrefabPath = "Assets/AAAGame/Prefabs/Meiyou/EntityPresetPoint.prefab";
        private const string PresetUnitsRootName = "\u5173\u5361\u9884\u8BBE\u5355\u4F4D";
        private const string PresetBuildingsRootName = "\u5173\u5361\u9884\u8BBE\u5EFA\u7B51";
        private const string DefaultHeroIdentifier = "Unit_Hero";
        private const int EnemyStrongholdValue = 1;
        private const int PlayerStrongholdValue = 2;
        private const int PlayerStrongholdFaction = 0;
        private const int EnemyStrongholdFaction = 1;

        private UnityEngine.Object ldtkLevelAsset;
        private Configuration configuration;
        private TileWorldCreatorManager manager;
        private GameObject levelPrefabTemplate;
        private GameObject levelPrefabTarget;
        private GameObject entityPresetPointPrefab;
        private bool resizeConfiguration = true;
        private bool clearBlueprintModifiers = true;
        private bool importEntityPresetPoints = true;
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

            if (levelPrefabTemplate == null)
            {
                levelPrefabTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPrefabTemplatePath);
            }

            if (levelPrefabTarget == null)
            {
                levelPrefabTarget = AssetDatabase.LoadAssetAtPath<GameObject>(GetDefaultLevelPrefabPath());
            }

            if (entityPresetPointPrefab == null)
            {
                entityPresetPointPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EntityPresetPointPrefabPath);
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
                if (GUILayout.Button("Create/Select Target From Lv3 Template"))
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
            EditorGUILayout.HelpBox("Plane -> Plane, Water -> Water. SH Player(value 2) -> SH_0_x, Enemy(value 1) -> SH_1_x, split by 4-neighbor connected components. Lv3.asset is used as the clone template when you click the template button.", MessageType.Info);
            resizeConfiguration = EditorGUILayout.Toggle("Resize configuration", resizeConfiguration);
            clearBlueprintModifiers = EditorGUILayout.Toggle("Clear blueprint modifiers", clearBlueprintModifiers);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Entity Preset Points", EditorStyles.boldLabel);
            importEntityPresetPoints = EditorGUILayout.Toggle("Import entity points", importEntityPresetPoints);
            using (new EditorGUI.DisabledScope(!importEntityPresetPoints))
            {
                levelPrefabTarget = (GameObject)EditorGUILayout.ObjectField("Level prefab target", levelPrefabTarget, typeof(GameObject), false);
                levelPrefabTemplate = (GameObject)EditorGUILayout.ObjectField("Level prefab template", levelPrefabTemplate, typeof(GameObject), false);
                entityPresetPointPrefab = (GameObject)EditorGUILayout.ObjectField("Point prefab", entityPresetPointPrefab, typeof(GameObject), false);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create/Select Level Prefab From Level_2 Template"))
                    {
                        CreateOrSelectLevelPrefabFromTemplate();
                    }

                    if (GUILayout.Button("Generate Level Prefab Only"))
                    {
                        ImportEntityPresetPointsOnly();
                    }
                }
            }

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

            if (!EnsureStrongholdLayers(plan.strongholdComponentsByFaction, out string ensureReport))
            {
                string message = "Failed to prepare SH layers.";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                lastReport = message;
                return;
            }

            List<BlueprintLayer> strongholdLayers = GetStrongholdBlueprintLayers();
            if (!HasRequiredStrongholdLayers(plan.strongholdComponentsByFaction, strongholdLayers, out string missingStrongholdLayer))
            {
                string message = $"SH component still has no matching blueprint layer: {missingStrongholdLayer}";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                lastReport = message + "\n\n" + BuildStrongholdReport(plan.strongholdComponentsByFaction, strongholdLayers);
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
                HashSet<Vector2> cells = GetStrongholdCellsForLayer(plan.strongholdComponentsByFaction, strongholdLayers[i]);
                clearedModifierCount += ImportCells(strongholdLayers[i], cells, clearBlueprintModifiers);
            }

            if (generateBuildLayers)
            {
                manager.ExecuteBuildLayers(ExecutionMode.FromScratch);
                ScheduleSaveAfterBuild(
                    configuration,
                    () =>
                    {
                        TerrainPrefabResult delayedTerrainResult = SaveTerrainPrefabFromManager();
                        EntityImportResult delayedEntityImportResult = ImportEntityPresetPointsIfRequested(plan, delayedTerrainResult.targetPath);
                        delayedEntityImportResult.terrainResult = delayedTerrainResult;
                        AssetDatabase.SaveAssets();

                        lastReport = BuildImportReport(ldtkPath, plan, strongholdLayers, true, clearedModifierCount, delayedEntityImportResult);
                        if (!string.IsNullOrEmpty(ensureReport))
                        {
                            lastReport = ensureReport + "\n\n" + lastReport;
                        }

                        Debug.Log(lastReport);
                    });

                lastReport = "[LDtk Import] TileWorldCreator build layers are generating. Terrain prefab and level prefab will be saved after the editor build pass finishes.";
                Debug.Log(lastReport);
                return;
            }

            TerrainPrefabResult terrainPrefabResult = TerrainPrefabResult.Skipped("Build layers were not generated.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GetDefaultTerrainPrefabPath()) != null)
            {
                terrainPrefabResult = TerrainPrefabResult.Existing(GetDefaultTerrainPrefabPath());
            }

            EntityImportResult entityImportResult = ImportEntityPresetPointsIfRequested(plan, terrainPrefabResult.targetPath);
            entityImportResult.terrainResult = terrainPrefabResult;

            if (!generateBuildLayers)
            {
                MarkImportedAssetsDirty(configuration, planeLayer, waterLayer, strongholdLayers);
                AssetDatabase.SaveAssets();
            }
            else
            {
                AssetDatabase.SaveAssets();
            }

            lastReport = BuildImportReport(ldtkPath, plan, strongholdLayers, generateBuildLayers, clearedModifierCount, entityImportResult);
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

        private string GetDefaultLevelPrefabPath()
        {
            string ldtkPath = GetSelectedLdtkPath();
            if (string.IsNullOrEmpty(ldtkPath))
            {
                return string.Empty;
            }

            string levelName = Path.GetFileNameWithoutExtension(ldtkPath);
            Match lvMatch = Regex.Match(levelName, @"^Lv(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (lvMatch.Success)
            {
                levelName = "Level_" + lvMatch.Groups[1].Value;
            }

            return $"{LevelPrefabFolderPath}/{levelName}.prefab";
        }

        private string GetDefaultTerrainPrefabPath()
        {
            string ldtkPath = GetSelectedLdtkPath();
            if (string.IsNullOrEmpty(ldtkPath))
            {
                return string.Empty;
            }

            string terrainName = Path.GetFileNameWithoutExtension(ldtkPath);
            return $"{TerrainPrefabFolderPath}/{terrainName}.prefab";
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
            lastReport = $"Created target from Lv3 template: {targetPath}";
        }

        private void CreateOrSelectLevelPrefabFromTemplate()
        {
            if (!TryCreateOrSelectLevelPrefabFromTemplate(out string targetPath))
            {
                return;
            }

            lastReport = $"Selected level prefab target: {targetPath}";
        }

        private bool TryCreateOrSelectLevelPrefabFromTemplate(out string targetPath)
        {
            targetPath = GetDefaultLevelPrefabPath();
            if (string.IsNullOrEmpty(targetPath))
            {
                EditorUtility.DisplayDialog("LDtk import failed", "LDtk level json path is invalid.", "OK");
                return false;
            }

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (existing != null)
            {
                levelPrefabTarget = existing;
                return true;
            }

            string templatePath = levelPrefabTemplate != null ? AssetDatabase.GetAssetPath(levelPrefabTemplate) : LevelPrefabTemplatePath;
            if (string.IsNullOrEmpty(templatePath) || AssetDatabase.LoadAssetAtPath<GameObject>(templatePath) == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Level prefab template is invalid:\n{templatePath}", "OK");
                return false;
            }

            if (!AssetDatabase.IsValidFolder(LevelPrefabFolderPath))
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Missing level prefab folder:\n{LevelPrefabFolderPath}", "OK");
                return false;
            }

            if (!AssetDatabase.CopyAsset(templatePath, targetPath))
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Failed to clone level prefab template:\n{templatePath}\n-> {targetPath}", "OK");
                return false;
            }

            AssetDatabase.ImportAsset(targetPath);
            levelPrefabTarget = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (levelPrefabTarget == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Cloned level prefab could not be loaded:\n{targetPath}", "OK");
                return false;
            }

            RenamePrefabRoot(targetPath, Path.GetFileNameWithoutExtension(targetPath));
            levelPrefabTarget = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            return levelPrefabTarget != null;
        }

        private static void RenamePrefabRoot(string prefabPath, string rootName)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                prefabRoot.name = rootName;
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
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
                level = JsonConvert.DeserializeObject<LdtkLevelJson>(json);
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
            int gridSize = plane.__gridSize;
            float cellSize = GetTileWorldCellSize();
            if (!ValidateSameGrid(plane, water, stronghold))
            {
                return false;
            }

            if (!TryReadStrongholdComponents(stronghold, width, height, out var strongholdComponentsByFaction))
            {
                return false;
            }

            plan = new ImportPlan
            {
                width = width,
                height = height,
                gridSize = gridSize,
                cellSize = cellSize,
                pixelHeight = level.pxHei > 0 ? level.pxHei : height * gridSize,
                planeCells = ReadIntGridCells(plane, width, height),
                waterCells = ReadIntGridCells(water, width, height),
                strongholdComponentsByFaction = strongholdComponentsByFaction,
                entityPoints = ReadEntityPresetPoints(level, gridSize, cellSize)
            };

            return true;
        }

        private float GetTileWorldCellSize()
        {
            Configuration sourceConfiguration = configuration;
            if (sourceConfiguration == null)
            {
                sourceConfiguration = AssetDatabase.LoadAssetAtPath<Configuration>(GetDefaultTargetPath());
            }

            return sourceConfiguration != null && sourceConfiguration.cellSize > 0f
                ? sourceConfiguration.cellSize
                : 1f;
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

        private bool EnsureStrongholdLayers(Dictionary<int, List<StrongholdComponent>> requiredByFaction, out string report)
        {
            report = string.Empty;

            List<BlueprintLayer> existingBlueprintLayers = GetStrongholdBlueprintLayers();
            if (existingBlueprintLayers.Count == 0)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "No SH_* blueprint layer exists in the selected configuration.", "OK");
                return false;
            }

            var created = new List<string>();
            foreach (var required in requiredByFaction.OrderBy(x => x.Key))
            {
                int factionId = required.Key;
                int requiredCount = required.Value.Count;
                for (int index = 0; index < requiredCount; index++)
                {
                    string layerName = BuildStrongholdLayerName(factionId, index);
                    BlueprintLayer blueprintLayer = existingBlueprintLayers.FirstOrDefault(x => string.Equals(x.layerName, layerName, StringComparison.OrdinalIgnoreCase));
                    TilesBuildLayer buildLayerTemplate = null;
                    if (blueprintLayer == null)
                    {
                        if (!TryFindStrongholdTemplate(existingBlueprintLayers, factionId, out BlueprintLayer templateBlueprint, out TilesBuildLayer templateBuildLayer))
                        {
                            return false;
                        }

                        buildLayerTemplate = templateBuildLayer;
                        if (!TryCloneBlueprintLayer(templateBlueprint, layerName, out blueprintLayer))
                        {
                            EditorUtility.DisplayDialog("LDtk import failed", $"Failed to create blueprint layer: {layerName}", "OK");
                            return false;
                        }

                        existingBlueprintLayers.Add(blueprintLayer);
                        created.Add(layerName);
                    }

                    if (GetStrongholdBuildLayer(layerName) == null)
                    {
                        if (buildLayerTemplate == null && !TryFindStrongholdTemplate(existingBlueprintLayers, factionId, out _, out buildLayerTemplate))
                        {
                            return false;
                        }

                        string buildLayerName = "Build " + layerName;
                        if (!TryCloneBuildLayer(buildLayerTemplate, blueprintLayer, buildLayerName, out _))
                        {
                            EditorUtility.DisplayDialog("LDtk import failed", $"Failed to create build layer: {buildLayerName}", "OK");
                            return false;
                        }

                        created.Add(buildLayerName);
                    }
                }
            }

            if (created.Count > 0)
            {
                report = "Created missing stronghold layers: " + string.Join(", ", created);
            }
            else
            {
                var extraLayerNames = GetExtraStrongholdLayerNames(requiredByFaction, existingBlueprintLayers);
                if (extraLayerNames.Count > 0)
                {
                    report = "Cleared extra stronghold layers: " + string.Join(", ", extraLayerNames);
                }
            }

            return true;
        }

        private bool TryFindStrongholdTemplate(List<BlueprintLayer> existingBlueprintLayers, int factionId, out BlueprintLayer templateBlueprint, out TilesBuildLayer templateBuildLayer)
        {
            templateBlueprint = existingBlueprintLayers
                .Where(x => ParseStrongholdName(x.layerName, out int layerFaction, out _) && layerFaction == factionId && GetStrongholdBuildLayer(x.layerName) != null)
                .OrderBy(x =>
                {
                    ParseStrongholdName(x.layerName, out _, out int index);
                    return index;
                })
                .LastOrDefault();

            templateBlueprint ??= existingBlueprintLayers.LastOrDefault(x => GetStrongholdBuildLayer(x.layerName) != null);
            templateBuildLayer = templateBlueprint != null ? GetStrongholdBuildLayer(templateBlueprint.layerName) : null;
            if (templateBlueprint == null || templateBuildLayer == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "No usable SH_* blueprint/build layer template exists in the selected configuration.", "OK");
                return false;
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

        private static string BuildStrongholdLayerName(int factionId, int index)
        {
            return $"SH_{factionId}_{index}";
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

        private static bool TryReadStrongholdComponents(LdtkLayerInstance layer, int width, int height, out Dictionary<int, List<StrongholdComponent>> componentsByFaction)
        {
            componentsByFaction = new Dictionary<int, List<StrongholdComponent>>();
            var cellsByFaction = new Dictionary<int, HashSet<Vector2>>();
            int offsetX = Mathf.RoundToInt((layer.__pxTotalOffsetX + layer.pxOffsetX) / (float)layer.__gridSize);
            int offsetY = Mathf.RoundToInt((layer.__pxTotalOffsetY + layer.pxOffsetY) / (float)layer.__gridSize);

            for (int i = 0; i < layer.intGridCsv.Length; i++)
            {
                int value = layer.intGridCsv[i];
                if (value == 0)
                {
                    continue;
                }

                if (!TryMapStrongholdValueToFaction(value, out int factionId))
                {
                    EditorUtility.DisplayDialog("LDtk import failed", $"Unsupported SH IntGrid value: {value}. Expected {PlayerStrongholdValue}=player and {EnemyStrongholdValue}=enemy.", "OK");
                    return false;
                }

                int ldtkX = i % width + offsetX;
                int ldtkY = i / width + offsetY;
                int twcY = height - 1 - ldtkY;
                if (!cellsByFaction.TryGetValue(factionId, out var cells))
                {
                    cells = new HashSet<Vector2>();
                    cellsByFaction.Add(factionId, cells);
                }

                cells.Add(new Vector2(ldtkX, twcY));
            }

            foreach (var pair in cellsByFaction)
            {
                componentsByFaction[pair.Key] = SplitConnectedComponents(pair.Value);
            }

            return true;
        }

        private static bool TryMapStrongholdValueToFaction(int value, out int factionId)
        {
            switch (value)
            {
                case PlayerStrongholdValue:
                    factionId = PlayerStrongholdFaction;
                    return true;

                case EnemyStrongholdValue:
                    factionId = EnemyStrongholdFaction;
                    return true;

                default:
                    factionId = -1;
                    return false;
            }
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

        private TerrainPrefabResult SaveTerrainPrefabFromManager()
        {
            if (manager == null)
            {
                return TerrainPrefabResult.Skipped("TWC manager is not assigned.");
            }

            string terrainPrefabPath = GetDefaultTerrainPrefabPath();
            if (string.IsNullOrEmpty(terrainPrefabPath))
            {
                return TerrainPrefabResult.Skipped("Terrain prefab path is invalid.");
            }

            if (!AssetDatabase.IsValidFolder(TerrainPrefabFolderPath))
            {
                return TerrainPrefabResult.Skipped($"Missing terrain prefab folder: {TerrainPrefabFolderPath}");
            }

            string terrainName = Path.GetFileNameWithoutExtension(terrainPrefabPath);
            GameObject terrainClone = Instantiate(manager.gameObject);
            terrainClone.name = terrainName;
            terrainClone.transform.position = manager.transform.position;
            terrainClone.transform.rotation = manager.transform.rotation;
            terrainClone.transform.localScale = manager.transform.localScale;

            try
            {
                SaveGeneratedMeshes(terrainClone, terrainPrefabPath);
                PrefabUtility.SaveAsPrefabAsset(terrainClone, terrainPrefabPath);
                AssetDatabase.ImportAsset(terrainPrefabPath);
                int navMeshSurfaceCount = EnsureTerrainNavMeshSurfacesAndData(terrainPrefabPath);
                return TerrainPrefabResult.Saved(terrainPrefabPath, navMeshSurfaceCount);
            }
            finally
            {
                DestroyImmediate(terrainClone);
            }
        }

        private static int EnsureTerrainNavMeshSurfacesAndData(string terrainPrefabPath)
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplateTerrainPrefabPath);
            if (template == null)
            {
                Debug.LogWarning($"[LDtk Import] Missing terrain navmesh template: {TemplateTerrainPrefabPath}");
                return 0;
            }

            NavMeshSurface[] templateSurfaces = template.GetComponents<NavMeshSurface>();
            if (templateSurfaces == null || templateSurfaces.Length == 0)
            {
                Debug.LogWarning($"[LDtk Import] No NavMeshSurface found on terrain template: {TemplateTerrainPrefabPath}");
                return 0;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(terrainPrefabPath);
            try
            {
                foreach (NavMeshSurface existingSurface in prefabRoot.GetComponents<NavMeshSurface>())
                {
                    DestroyImmediate(existingSurface);
                }

                for (int i = 0; i < templateSurfaces.Length; i++)
                {
                    NavMeshSurface surface = prefabRoot.AddComponent<NavMeshSurface>();
                    CopyNavMeshSurfaceSettings(templateSurfaces[i], surface);
                    BuildAndSaveNavMeshData(surface, terrainPrefabPath, i);
                    EditorUtility.SetDirty(surface);
                }

                EditorUtility.SetDirty(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, terrainPrefabPath);
                AssetDatabase.ImportAsset(terrainPrefabPath);
                return templateSurfaces.Length;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static int CountTerrainNavMeshSurfaces(string terrainPrefabPath)
        {
            GameObject terrainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath);
            return terrainPrefab != null ? terrainPrefab.GetComponents<NavMeshSurface>().Length : 0;
        }

        private static void CopyNavMeshSurfaceSettings(NavMeshSurface source, NavMeshSurface target)
        {
            target.agentTypeID = source.agentTypeID;
            target.collectObjects = source.collectObjects;
            target.size = source.size;
            target.center = source.center;
            target.layerMask = source.layerMask;
            target.useGeometry = source.useGeometry;
            target.defaultArea = source.defaultArea;
            target.ignoreNavMeshAgent = source.ignoreNavMeshAgent;
            target.ignoreNavMeshObstacle = source.ignoreNavMeshObstacle;
            target.overrideTileSize = source.overrideTileSize;
            target.tileSize = source.tileSize;
            target.overrideVoxelSize = source.overrideVoxelSize;
            target.voxelSize = source.voxelSize;
            target.minRegionArea = source.minRegionArea;
            target.buildHeightMesh = source.buildHeightMesh;
            target.navMeshData = null;

            var sourceObject = new SerializedObject(source);
            var targetObject = new SerializedObject(target);
            SerializedProperty sourceGenerateLinks = sourceObject.FindProperty("m_GenerateLinks");
            SerializedProperty targetGenerateLinks = targetObject.FindProperty("m_GenerateLinks");
            if (sourceGenerateLinks != null && targetGenerateLinks != null)
            {
                targetGenerateLinks.boolValue = sourceGenerateLinks.boolValue;
                targetObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildAndSaveNavMeshData(NavMeshSurface surface, string terrainPrefabPath, int surfaceIndex)
        {
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                Debug.LogWarning($"[LDtk Import] NavMesh build produced no data for agent type {surface.agentTypeID}.");
                return;
            }

            string navMeshPath = GetTerrainNavMeshDataPath(terrainPrefabPath, surfaceIndex, surface.agentTypeID);
            NavMeshData data = surface.navMeshData;
            data.name = Path.GetFileNameWithoutExtension(navMeshPath);

            NavMeshData existingData = AssetDatabase.LoadAssetAtPath<NavMeshData>(navMeshPath);
            if (existingData != null)
            {
                AssetDatabase.DeleteAsset(navMeshPath);
            }

            AssetDatabase.CreateAsset(data, navMeshPath);
            AssetDatabase.ImportAsset(navMeshPath);
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(navMeshPath);
        }

        private static string GetTerrainNavMeshDataPath(string terrainPrefabPath, int surfaceIndex, int agentTypeId)
        {
            string prefabFolder = Path.GetDirectoryName(terrainPrefabPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(prefabFolder))
            {
                prefabFolder = TerrainPrefabFolderPath;
            }

            string terrainName = Path.GetFileNameWithoutExtension(terrainPrefabPath);
            string agentSuffix = agentTypeId == 0 ? "Default" : agentTypeId.ToString();
            return $"{prefabFolder}/NavMesh-{terrainName}-{surfaceIndex}-{agentSuffix}.asset";
        }

        private static void SaveGeneratedMeshes(GameObject root, string terrainPrefabPath)
        {
            string prefabFolder = Path.GetDirectoryName(terrainPrefabPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(prefabFolder))
            {
                return;
            }

            string meshFolderName = root.name + "_Meshes";
            string meshFolder = (prefabFolder + "/" + meshFolderName).Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(meshFolder))
            {
                AssetDatabase.CreateFolder(prefabFolder, meshFolderName);
            }

            var components = new List<(UnityEngine.Object component, Mesh mesh, bool isCollider)>();
            foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh != null && meshFilter.sharedMesh.vertexCount > 0)
                {
                    components.Add((meshFilter, meshFilter.sharedMesh, false));
                }
            }

            foreach (MeshCollider meshCollider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (meshCollider.sharedMesh != null && meshCollider.sharedMesh.vertexCount > 0)
                {
                    components.Add((meshCollider, meshCollider.sharedMesh, true));
                }
            }

            var meshMap = new Dictionary<Mesh, Mesh>();
            foreach (var item in components)
            {
                if (!meshMap.TryGetValue(item.mesh, out Mesh savedMesh))
                {
                    savedMesh = SaveOrReuseMesh(item.component, item.mesh, item.isCollider, meshFolder);
                    meshMap.Add(item.mesh, savedMesh);
                }

                if (item.isCollider)
                {
                    ((MeshCollider)item.component).sharedMesh = savedMesh;
                }
                else
                {
                    ((MeshFilter)item.component).sharedMesh = savedMesh;
                }
            }
        }

        private static Mesh SaveOrReuseMesh(UnityEngine.Object component, Mesh sourceMesh, bool isCollider, string meshFolder)
        {
            string baseName = string.IsNullOrEmpty(sourceMesh.name) ? component.name : sourceMesh.name;
            string meshName = isCollider && !baseName.EndsWith("_Collider", StringComparison.Ordinal)
                ? baseName + "_Collider"
                : baseName;
            string meshPath = Path.Combine(meshFolder, meshName + ".asset").Replace("\\", "/");

            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh != null && !MeshChanged(sourceMesh, existingMesh))
            {
                return existingMesh;
            }

            if (existingMesh != null)
            {
                AssetDatabase.DeleteAsset(meshPath);
            }

            Mesh savedMesh = Instantiate(sourceMesh);
            savedMesh.name = meshName;
            AssetDatabase.CreateAsset(savedMesh, meshPath);
            return savedMesh;
        }

        private static bool MeshChanged(Mesh a, Mesh b)
        {
            if (a == null || b == null)
            {
                return true;
            }

            return a.vertexCount != b.vertexCount ||
                   a.subMeshCount != b.subMeshCount ||
                   a.bounds != b.bounds;
        }

        private void ImportEntityPresetPointsOnly()
        {
            lastReport = string.Empty;

            if (!TryGetLdtkPath(out string ldtkPath))
            {
                return;
            }

            if (!TryLoadLevel(ldtkPath, out LdtkLevelJson level))
            {
                return;
            }

            int gridSize = GetDefaultEntityGridSize(level);
            float cellSize = GetTileWorldCellSize();
            var plan = new ImportPlan
            {
                gridSize = gridSize,
                cellSize = cellSize,
                pixelHeight = level.pxHei,
                entityPoints = ReadEntityPresetPoints(level, gridSize, cellSize)
            };

            string terrainPrefabPath = GetDefaultTerrainPrefabPath();
            TerrainPrefabResult terrainPrefabResult = AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath) != null
                ? TerrainPrefabResult.Existing(terrainPrefabPath)
                : TerrainPrefabResult.Skipped("Terrain prefab has not been generated.");
            EntityImportResult result = ImportEntityPresetPointsIfRequested(plan, terrainPrefabResult.targetPath);
            result.terrainResult = terrainPrefabResult;

            lastReport = BuildEntityImportReport(ldtkPath, result);
            Debug.Log(lastReport);
        }

        private EntityImportResult ImportEntityPresetPointsIfRequested(ImportPlan plan, string terrainPrefabPath)
        {
            if (!importEntityPresetPoints)
            {
                return EntityImportResult.Skipped("Entity preset point import disabled.");
            }

            if (levelPrefabTarget == null)
            {
                if (!TryCreateOrSelectLevelPrefabFromTemplate(out _))
                {
                    return EntityImportResult.Skipped("Failed to create or select level prefab target.");
                }
            }

            if (entityPresetPointPrefab == null)
            {
                const string message = "EntityPresetPoint prefab is not assigned.";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                return EntityImportResult.Skipped(message);
            }

            if (entityPresetPointPrefab.GetComponent<EntityPresetPoint>() == null)
            {
                const string message = "Selected point prefab has no EntityPresetPoint component.";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                return EntityImportResult.Skipped(message);
            }

            string targetPath = AssetDatabase.GetAssetPath(levelPrefabTarget);
            bool isPrefabAsset = !string.IsNullOrEmpty(targetPath) && PrefabUtility.GetPrefabAssetType(levelPrefabTarget) != PrefabAssetType.NotAPrefab;
            if (!isPrefabAsset)
            {
                const string message = "Level prefab target must be a prefab asset.";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                return EntityImportResult.Skipped(message);
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(targetPath);
            try
            {
                prefabRoot.name = Path.GetFileNameWithoutExtension(targetPath);
                EntityImportResult result = ImportEntityPresetPointsIntoRoot(prefabRoot.transform, plan.entityPoints, terrainPrefabPath, useUndo: false);
                result.targetPath = targetPath;
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, targetPath);
                AssetDatabase.ImportAsset(targetPath);
                levelPrefabTarget = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                return result;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private EntityImportResult ImportEntityPresetPointsIntoRoot(Transform root, List<EntityPresetPointData> points, string terrainPrefabPath, bool useUndo)
        {
            int removedCount = ClearExistingEntityPresetPoints(root, useUndo);
            int removedTerrainCount = ReplaceTerrainPrefab(root, terrainPrefabPath, useUndo);
            Transform unitsRoot = FindOrCreateChild(root, PresetUnitsRootName, useUndo);
            Transform buildingsRoot = FindOrCreateChild(root, PresetBuildingsRootName, useUndo);

            var result = new EntityImportResult
            {
                removedCount = removedCount,
                removedTerrainCount = removedTerrainCount,
                terrainPrefabPath = terrainPrefabPath
            };

            foreach (var pointData in points)
            {
                Transform parent = pointData.pointType == EntityPresetPointType.Building ? buildingsRoot : unitsRoot;
                GameObject pointObject = (GameObject)PrefabUtility.InstantiatePrefab(entityPresetPointPrefab, parent);
                if (pointObject == null)
                {
                    pointObject = Instantiate(entityPresetPointPrefab, parent);
                }

                pointObject.name = BuildEntityPresetPointName(pointData);
                pointObject.transform.localPosition = pointData.localPosition;
                pointObject.transform.localRotation = Quaternion.identity;
                pointObject.transform.localScale = Vector3.one;

                EntityPresetPoint point = pointObject.GetComponent<EntityPresetPoint>();
                point.Identifier = pointData.identifier;
                point.PointType = pointData.pointType;
                point.UnitSpawnCount = pointData.unitSpawnCount;
                point.IsGameEndConditionBuilding = pointData.isGameEndConditionBuilding;
                point.IsTestSlot = false;
                point.TestSlotIndex = 0;

                result.Add(pointData.pointType);
                EditorUtility.SetDirty(pointObject);
                EditorUtility.SetDirty(point);
            }

            EditorUtility.SetDirty(root.gameObject);
            return result;
        }

        private static int ClearExistingEntityPresetPoints(Transform root, bool useUndo)
        {
            var points = root.GetComponentsInChildren<EntityPresetPoint>(true)
                .Where(x => x != null && x.transform != root)
                .Select(x => x.gameObject)
                .Distinct()
                .ToArray();

            foreach (GameObject pointObject in points)
            {
                DestroyImmediateObject(pointObject, useUndo);
            }

            return points.Length;
        }

        private int ReplaceTerrainPrefab(Transform root, string terrainPrefabPath, bool useUndo)
        {
            var terrainPrefab = !string.IsNullOrEmpty(terrainPrefabPath)
                ? AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath)
                : null;
            if (terrainPrefab == null)
            {
                return 0;
            }

            var terrainRoots = GetTerrainRoots(root).ToArray();
            Vector3 localPosition = terrainRoots.Length > 0 ? terrainRoots[0].localPosition : Vector3.zero;
            Quaternion localRotation = terrainRoots.Length > 0 ? terrainRoots[0].localRotation : Quaternion.identity;
            Vector3 localScale = terrainRoots.Length > 0 ? terrainRoots[0].localScale : Vector3.one;

            foreach (Transform terrainRoot in terrainRoots)
            {
                DestroyImmediateObject(terrainRoot.gameObject, useUndo);
            }

            GameObject terrainObject = (GameObject)PrefabUtility.InstantiatePrefab(terrainPrefab, root);
            if (terrainObject == null)
            {
                terrainObject = Instantiate(terrainPrefab, root);
            }

            terrainObject.name = Path.GetFileNameWithoutExtension(terrainPrefabPath);
            terrainObject.transform.localPosition = localPosition;
            terrainObject.transform.localRotation = localRotation;
            terrainObject.transform.localScale = localScale;
            EditorUtility.SetDirty(terrainObject);
            return terrainRoots.Length;
        }

        private static IEnumerable<Transform> GetTerrainRoots(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponentInChildren<TileWorldCreatorManager>(true) != null)
                {
                    yield return child;
                    continue;
                }

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
                if (sourcePath.StartsWith(TerrainPrefabFolderPath + "/", StringComparison.OrdinalIgnoreCase) &&
                    sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    yield return child;
                }
            }
        }

        private static Transform FindOrCreateChild(Transform root, string childName, bool useUndo)
        {
            Transform child = root.Find(childName);
            if (child != null)
            {
                return child;
            }

            var childObject = new GameObject(childName);
            if (useUndo)
            {
                Undo.RegisterCreatedObjectUndo(childObject, "Create LDtk Entity Preset Root");
            }

            childObject.transform.SetParent(root, false);
            return childObject.transform;
        }

        private static void DestroyImmediateObject(UnityEngine.Object target, bool useUndo)
        {
            if (useUndo)
            {
                Undo.DestroyObjectImmediate(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static string BuildEntityPresetPointName(EntityPresetPointData pointData)
        {
            string typeName = pointData.pointType.ToString();
            return string.IsNullOrEmpty(pointData.identifier) ? typeName : $"{typeName}_{pointData.identifier}";
        }

        private static int GetDefaultEntityGridSize(LdtkLevelJson level)
        {
            LdtkLayerInstance gridLayer = level.layerInstances?.FirstOrDefault(x => x != null && x.__gridSize > 0);
            return gridLayer != null ? gridLayer.__gridSize : 16;
        }

        private static List<EntityPresetPointData> ReadEntityPresetPoints(LdtkLevelJson level, int gridSize, float cellSize)
        {
            var result = new List<EntityPresetPointData>();
            LdtkLayerInstance entityLayer = level.layerInstances?.FirstOrDefault(x =>
                x != null &&
                string.Equals(x.__identifier, EntityLayerName, StringComparison.OrdinalIgnoreCase));

            if (entityLayer?.entityInstances == null)
            {
                return result;
            }

            int pixelHeight = level.pxHei > 0 ? level.pxHei : entityLayer.__cHei * gridSize;
            foreach (var entity in entityLayer.entityInstances)
            {
                if (entity == null || entity.px == null || entity.px.Length < 2)
                {
                    continue;
                }

                if (!TryConvertEntity(entity, gridSize, pixelHeight, cellSize, out EntityPresetPointData pointData))
                {
                    continue;
                }

                result.Add(pointData);
            }

            return result;
        }

        private static bool TryConvertEntity(LdtkEntityInstance entity, int gridSize, int pixelHeight, float cellSize, out EntityPresetPointData pointData)
        {
            pointData = default;
            string entityType = entity.__identifier;
            EntityPresetPointType pointType;
            string identifier;
            int unitSpawnCount = 0;
            bool isGameEndConditionBuilding = false;

            if (string.Equals(entityType, "Soldier", StringComparison.OrdinalIgnoreCase))
            {
                pointType = EntityPresetPointType.Unit;
                identifier = GetFieldString(entity, "Identifier");
                unitSpawnCount = GetFieldInt(entity, "Count", 0);
            }
            else if (string.Equals(entityType, "Hero", StringComparison.OrdinalIgnoreCase))
            {
                pointType = EntityPresetPointType.Hero;
                identifier = GetFieldString(entity, "Identifier");
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    identifier = DefaultHeroIdentifier;
                }
            }
            else if (string.Equals(entityType, "Building", StringComparison.OrdinalIgnoreCase))
            {
                pointType = EntityPresetPointType.Building;
                identifier = NormalizeBuildingIdentifier(GetFieldString(entity, "Identifier"));
                isGameEndConditionBuilding = GetFieldBool(entity, "IsGameEndCondition", false);
            }
            else
            {
                return false;
            }

            pointData = new EntityPresetPointData
            {
                pointType = pointType,
                identifier = identifier,
                unitSpawnCount = unitSpawnCount,
                isGameEndConditionBuilding = isGameEndConditionBuilding,
                localPosition = new Vector3(
                    entity.px[0] / (float)gridSize * cellSize,
                    0f,
                    (pixelHeight - entity.px[1]) / (float)gridSize * cellSize)
            };
            return true;
        }

        private static string NormalizeBuildingIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return identifier;
            }

            string trimmed = identifier.Trim();
            return Regex.IsMatch(trimmed, @"_Lv\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? trimmed : trimmed + "_Lv1";
        }

        private static string GetFieldString(LdtkEntityInstance entity, string fieldName)
        {
            JToken value = GetFieldValue(entity, fieldName);
            if (value == null || value.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            return value.Type == JTokenType.String ? value.Value<string>() : value.ToString(Formatting.None);
        }

        private static int GetFieldInt(LdtkEntityInstance entity, string fieldName, int defaultValue)
        {
            JToken value = GetFieldValue(entity, fieldName);
            if (value == null || value.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return value.Type == JTokenType.Integer || value.Type == JTokenType.Float
                ? value.Value<int>()
                : int.TryParse(value.ToString(Formatting.None), out int parsed) ? parsed : defaultValue;
        }

        private static bool GetFieldBool(LdtkEntityInstance entity, string fieldName, bool defaultValue)
        {
            JToken value = GetFieldValue(entity, fieldName);
            if (value == null || value.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return value.Type == JTokenType.Boolean
                ? value.Value<bool>()
                : bool.TryParse(value.ToString(Formatting.None), out bool parsed) ? parsed : defaultValue;
        }

        private static JToken GetFieldValue(LdtkEntityInstance entity, string fieldName)
        {
            return entity.fieldInstances?
                .FirstOrDefault(x => x != null && string.Equals(x.__identifier, fieldName, StringComparison.OrdinalIgnoreCase))
                ?.__value;
        }

        private static HashSet<Vector2> GetStrongholdCellsForLayer(Dictionary<int, List<StrongholdComponent>> componentsByFaction, BlueprintLayer layer)
        {
            if (layer == null || !ParseStrongholdName(layer.layerName, out int factionId, out int index))
            {
                return new HashSet<Vector2>();
            }

            return componentsByFaction.TryGetValue(factionId, out var components) && index >= 0 && index < components.Count
                ? components[index].cells
                : new HashSet<Vector2>();
        }

        private static bool HasRequiredStrongholdLayers(Dictionary<int, List<StrongholdComponent>> componentsByFaction, List<BlueprintLayer> layers, out string missingLayer)
        {
            foreach (var pair in componentsByFaction)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    string layerName = BuildStrongholdLayerName(pair.Key, i);
                    if (layers.Any(x => string.Equals(x.layerName, layerName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    missingLayer = layerName;
                    return false;
                }
            }

            missingLayer = string.Empty;
            return true;
        }

        private static List<string> GetExtraStrongholdLayerNames(Dictionary<int, List<StrongholdComponent>> componentsByFaction, List<BlueprintLayer> layers)
        {
            var result = new List<string>();
            foreach (var layer in layers)
            {
                if (!ParseStrongholdName(layer.layerName, out int factionId, out int index))
                {
                    continue;
                }

                int requiredCount = componentsByFaction.TryGetValue(factionId, out var components) ? components.Count : 0;
                if (index >= requiredCount)
                {
                    result.Add(layer.layerName);
                }
            }

            return result;
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

        private static void ScheduleSaveAfterBuild(Configuration configuration, Action afterSave = null)
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
                afterSave?.Invoke();
            };

            EditorApplication.update += update;
        }

        private static string BuildImportReport(string path, ImportPlan plan, List<BlueprintLayer> strongholdLayers, bool generatedBuildLayers, int clearedModifierCount, EntityImportResult entityImportResult)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[LDtk Import] Completed");
            builder.AppendLine($"Source: {path}");
            builder.AppendLine($"Size: {plan.width} x {plan.height}");
            builder.AppendLine($"TWC cell size: {plan.cellSize}");
            builder.AppendLine($"Plane cells: {plan.planeCells.Count}");
            builder.AppendLine($"Water cells: {plan.waterCells.Count}");
            builder.AppendLine($"SH components: {GetStrongholdComponentCount(plan.strongholdComponentsByFaction)}");
            builder.AppendLine($"Cleared blueprint modifiers: {clearedModifierCount}");
            builder.AppendLine($"Generated build layers: {generatedBuildLayers}");
            builder.Append(BuildEntityImportReport(path, entityImportResult));
            builder.Append(BuildStrongholdReport(plan.strongholdComponentsByFaction, strongholdLayers));
            return builder.ToString();
        }

        private static string BuildEntityImportReport(string path, EntityImportResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Entity source: {path}");
            builder.AppendLine($"Terrain prefab: {result.terrainResult.targetPath}");
            builder.AppendLine($"Terrain prefab import: {(result.terrainResult.skipped ? "Skipped" : result.terrainResult.saved ? "Saved" : "Existing")}");
            builder.AppendLine($"Terrain navmesh surfaces: {result.terrainResult.navMeshSurfaceCount}");
            if (result.terrainResult.skipped)
            {
                builder.AppendLine($"Terrain skipped reason: {result.terrainResult.skippedReason}");
            }

            builder.AppendLine($"Entity import: {(result.skipped ? "Skipped" : "Completed")}");
            if (result.skipped)
            {
                builder.AppendLine($"Entity skipped reason: {result.skippedReason}");
                return builder.ToString();
            }

            builder.AppendLine($"Entity prefab: {result.targetPath}");
            builder.AppendLine($"Entity removed old terrains: {result.removedTerrainCount}");
            builder.AppendLine($"Entity terrain instance: {result.terrainPrefabPath}");
            builder.AppendLine($"Entity removed old points: {result.removedCount}");
            builder.AppendLine($"Entity heroes: {result.heroCount}");
            builder.AppendLine($"Entity buildings: {result.buildingCount}");
            builder.AppendLine($"Entity units: {result.unitCount}");
            return builder.ToString();
        }

        private static string BuildStrongholdReport(Dictionary<int, List<StrongholdComponent>> componentsByFaction, List<BlueprintLayer> layers)
        {
            var builder = new StringBuilder();
            foreach (var layer in layers)
            {
                if (!ParseStrongholdName(layer.layerName, out int factionId, out int index))
                {
                    continue;
                }

                string component = componentsByFaction.TryGetValue(factionId, out var components) && index < components.Count
                    ? components[index].ToString()
                    : "<empty>";
                builder.AppendLine($"SH_{factionId}[{index}] {component} -> {layer.layerName}");
            }

            return builder.ToString();
        }

        private static int GetStrongholdComponentCount(Dictionary<int, List<StrongholdComponent>> componentsByFaction)
        {
            return componentsByFaction.Sum(x => x.Value.Count);
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
            public LdtkEntityInstance[] entityInstances;
        }

        private sealed class LdtkEntityInstance
        {
            public string __identifier;
            public int[] px;
            public LdtkFieldInstance[] fieldInstances;
        }

        private sealed class LdtkFieldInstance
        {
            public string __identifier;
            public JToken __value;
        }

        private sealed class ImportPlan
        {
            public int width;
            public int height;
            public int gridSize;
            public float cellSize;
            public int pixelHeight;
            public HashSet<Vector2> planeCells;
            public HashSet<Vector2> waterCells;
            public Dictionary<int, List<StrongholdComponent>> strongholdComponentsByFaction;
            public List<EntityPresetPointData> entityPoints;
        }

        private struct EntityPresetPointData
        {
            public EntityPresetPointType pointType;
            public string identifier;
            public int unitSpawnCount;
            public bool isGameEndConditionBuilding;
            public Vector3 localPosition;
        }

        private struct EntityImportResult
        {
            public bool skipped;
            public string skippedReason;
            public string targetPath;
            public string terrainPrefabPath;
            public TerrainPrefabResult terrainResult;
            public int removedTerrainCount;
            public int removedCount;
            public int heroCount;
            public int buildingCount;
            public int unitCount;

            public static EntityImportResult Skipped(string reason)
            {
                return new EntityImportResult
                {
                    skipped = true,
                    skippedReason = reason
                };
            }

            public void Add(EntityPresetPointType pointType)
            {
                switch (pointType)
                {
                    case EntityPresetPointType.Hero:
                        heroCount++;
                        break;

                    case EntityPresetPointType.Building:
                        buildingCount++;
                        break;

                    case EntityPresetPointType.Unit:
                        unitCount++;
                        break;
                }
            }
        }

        private struct TerrainPrefabResult
        {
            public bool skipped;
            public bool saved;
            public string skippedReason;
            public string targetPath;
            public int navMeshSurfaceCount;

            public static TerrainPrefabResult Skipped(string reason)
            {
                return new TerrainPrefabResult
                {
                    skipped = true,
                    skippedReason = reason
                };
            }

            public static TerrainPrefabResult Existing(string targetPath)
            {
                return new TerrainPrefabResult
                {
                    targetPath = targetPath,
                    navMeshSurfaceCount = CountTerrainNavMeshSurfaces(targetPath)
                };
            }

            public static TerrainPrefabResult Saved(string targetPath, int navMeshSurfaceCount)
            {
                return new TerrainPrefabResult
                {
                    saved = true,
                    targetPath = targetPath,
                    navMeshSurfaceCount = navMeshSurfaceCount
                };
            }
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
