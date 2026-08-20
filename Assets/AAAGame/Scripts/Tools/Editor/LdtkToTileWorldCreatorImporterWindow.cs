using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AAAGame.Tilemap;
using GiantGrey.TileWorldCreator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace AAAGame.Tools.Editor
{
    public sealed class LdtkToTileWorldCreatorImporterWindow : EditorWindow
    {
        private const string DefaultLdtkPath = "Assets/AAAGame/Tilemap/Ldtk/City/Lv3.ldtkl";
        private const string TemplateConfigurationPath = "Assets/AAAGame/Tilemap/Lv3.asset";
        private const string PlaneLayerName = "Plane_H0";
        private const string LegacyPlaneLayerName = "Plane";
        private const string PlatformLayerPrefix = "Plane_H";
        private const string PlatformDualControlLayerPrefix = "__LDtkDualControl_Plane_H";
        private const string SlopeLayerName = "Slope";
        private const string WaterLayerName = "Water";
        private const string StrongholdLayerName = "SH";
        private const string WallLayerName = "Wall";
        private const string WallForbiddenLayerName = "NoWall";
        private const string EntityLayerName = "Entities";
        private const string LevelPrefabTemplatePath = "Assets/AAAGame/Prefabs/Entity/Level/Level_2.prefab";
        private const string LevelPrefabFolderPath = "Assets/AAAGame/Prefabs/Entity/Level";
        private const string TerrainPrefabFolderPath = "Assets/AAAGame/Tilemap";
        private const string EntityPresetPointPrefabPath = "Assets/AAAGame/Prefabs/Meiyou/EntityPresetPoint.prefab";
        private const string GameConfigPath = "Assets/AAAGame/Config/GameConfig.txt";
        private const string PresetUnitsRootName = "\u5173\u5361\u9884\u8BBE\u5355\u4F4D";
        private const string PresetBuildingsRootName = "\u5173\u5361\u9884\u8BBE\u5EFA\u7B51";
        private const string DefaultHeroIdentifier = "Unit_Hero";
        private const int EnemyStrongholdValue = 1;
        private const int PlayerStrongholdValue = 2;
        private const int PlayerStrongholdFaction = 0;
        private const int EnemyStrongholdFaction = 1;
        private const int MinimumPlatformHeight = 0;
        private const int MaximumPlatformHeight = 5;
        private const string RampPrefabPath = "Assets/AAAGame/Models/SlopePlaceholder/Ramp.prefab";
        private const string LastLdtkPathPreference = "AAAGame.LdtkToTileWorldCreator.LastLdtkPath";
        private const float SourceActionButtonWidth = 220f;

        [SerializeField]
        private UnityEngine.Object ldtkLevelAsset;
        private Configuration templateConfiguration;
        private Configuration configuration;
        private TileWorldCreatorManager manager;
        private GameObject levelPrefabTemplate;
        private GameObject levelPrefabTarget;
        private GameObject entityPresetPointPrefab;
        private bool resizeConfiguration = true;
        private bool clearBlueprintModifiers = true;
        private bool importEntityPresetPoints = true;
        private bool syncBuildSettingsFromTemplate = true;
        private bool autoResolveForSelectedLdtk = true;
        private bool autoCreateMissingAssets = true;
        private bool autoCreateSceneManager = true;
        private Vector2 scrollPosition;
        private string lastReport;
        private GameObject temporaryManagerObject;
        private GUIStyle reportTextAreaStyle;

        [MenuItem("Tools/LDtk To TileWorldCreator")]
        private static void Open()
        {
            var window = GetWindow<LdtkToTileWorldCreatorImporterWindow>("LDtk To TWC");
            window.minSize = new Vector2(460f, 360f);
        }

        [MenuItem("Tools/Buildings/Sync Wall Authoring From LDtk")]
        private static void SyncWallAuthoringFromLdtk()
        {
            const string levelFolder = "Assets/AAAGame/Tilemap/Ldtk/City";
            string[] sourcePaths = Directory.GetFiles(levelFolder, "*.ldtkl", SearchOption.TopDirectoryOnly)
                .Select(path => path.Replace('\\', '/'))
                .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("_Backup", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (sourcePaths.Length == 0)
                throw new InvalidOperationException($"No LDtk level files were found in {levelFolder}.");

            var importer = CreateInstance<LdtkToTileWorldCreatorImporterWindow>();
            try
            {
                for (int i = 0; i < sourcePaths.Length; i++)
                    importer.SyncWallAuthoringForLevel(sourcePaths[i]);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                DestroyImmediate(importer);
            }
        }

        private void SyncWallAuthoringForLevel(string ldtkPath)
        {
            ldtkLevelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ldtkPath)
                             ?? throw new InvalidOperationException($"LDtk level asset is missing: {ldtkPath}.");
            string levelName = Path.GetFileNameWithoutExtension(ldtkPath);
            configuration = AssetDatabase.LoadAssetAtPath<Configuration>($"Assets/AAAGame/Tilemap/{levelName}.asset")
                            ?? throw new InvalidOperationException($"TWC configuration is missing for {ldtkPath}.");
            if (!TryLoadLevel(ldtkPath, out LdtkLevelJson level) || !TryBuildImportPlan(level, out ImportPlan plan))
                throw new InvalidOperationException($"Failed to build the wall import plan for {ldtkPath}.");

            string prefabPath = GetDefaultLevelPrefabPath();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                throw new InvalidOperationException($"Level prefab is missing for {ldtkPath}: {prefabPath}.");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                ConfigureWallGridAuthoring(prefabRoot, plan);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            Debug.Log(
                $"[Wall Authoring Sync] source={ldtkPath} prefab={prefabPath} " +
                $"walkable={plan.walkableWallCells.Count} preview={plan.previewWallCells.Count} preset={plan.presetWallCells.Count}");
        }

        private void OnEnable()
        {
            string savedLdtkPath = EditorPrefs.GetString(LastLdtkPathPreference, string.Empty);
            if (!string.IsNullOrEmpty(savedLdtkPath))
            {
                ldtkLevelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(savedLdtkPath);
                if (ldtkLevelAsset == null)
                {
                    Debug.LogWarning("[LDtk Import] Saved LDtk path is no longer available: " + savedLdtkPath);
                    EditorPrefs.DeleteKey(LastLdtkPathPreference);
                }
            }

            if (ldtkLevelAsset == null)
            {
                ldtkLevelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefaultLdtkPath);
            }

            SaveSelectedLdtkPath();

            if (templateConfiguration == null)
            {
                templateConfiguration = AssetDatabase.LoadAssetAtPath<Configuration>(TemplateConfigurationPath);
            }

            if (levelPrefabTemplate == null)
            {
                levelPrefabTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPrefabTemplatePath);
            }

            AutoResolveReferences(createMissingAssets: false, createSceneManager: false, out _);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            float scrollContentWidth = Mathf.Max(1f, position.width - 24f);
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(scrollContentWidth)))
            {
                EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                ldtkLevelAsset = EditorGUILayout.ObjectField("LDtk level json", ldtkLevelAsset, typeof(UnityEngine.Object), false);
                if (EditorGUI.EndChangeCheck())
                {
                    SaveSelectedLdtkPath();
                    if (autoResolveForSelectedLdtk)
                    {
                        AutoResolveReferences(createMissingAssets: false, createSceneManager: false, out _);
                    }
                }

                configuration = (Configuration)EditorGUILayout.ObjectField("TWC configuration", configuration, typeof(Configuration), false);
                templateConfiguration = (Configuration)EditorGUILayout.ObjectField("TWC template", templateConfiguration, typeof(Configuration), false);
                manager = (TileWorldCreatorManager)EditorGUILayout.ObjectField("TWC manager", manager, typeof(TileWorldCreatorManager), true);

                if (position.width >= 680f)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawSourceActionButtons(scrollContentWidth);
                    }
                }
                else
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        DrawSourceActionButtons(scrollContentWidth);
                    }
                }

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Import Rules", EditorStyles.boldLabel);
                autoResolveForSelectedLdtk = EditorGUILayout.Toggle("Auto select by LDtk", autoResolveForSelectedLdtk);
                autoCreateMissingAssets = EditorGUILayout.Toggle("Auto create assets", autoCreateMissingAssets);
                autoCreateSceneManager = EditorGUILayout.Toggle("Auto create manager", autoCreateSceneManager);
                syncBuildSettingsFromTemplate = EditorGUILayout.Toggle("Sync build settings", syncBuildSettingsFromTemplate);
                EditorGUILayout.HelpBox("Plane_H0..Plane_H5 define platform heights; overlapping cells use the highest layer for slope inference. Each straight rectangular Slope component becomes one continuous ramp at the angle inferred from its run and platform height difference. Ramps steeper than the project's walkable NavMesh slope are rejected. Slope support cells are added to the inferred platform layers automatically.", MessageType.Info);
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

                    if (GUILayout.Button(
                            "Create/Select Level Prefab From Level_2 Template",
                            GUILayout.Width(Mathf.Min(360f, scrollContentWidth))))
                    {
                        CreateOrSelectLevelPrefabFromTemplate();
                    }
                }

                if (!string.IsNullOrEmpty(lastReport))
                {
                    EditorGUILayout.Space(8f);
                    reportTextAreaStyle = reportTextAreaStyle ?? new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                    EditorGUILayout.TextArea(lastReport, reportTextAreaStyle, GUILayout.Height(120f));
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(!importEntityPresetPoints))
            {
                if (GUILayout.Button("Generate Level Prefab Only", GUILayout.Height(26f)))
                {
                    ImportEntityPresetPointsOnly();
                }
            }

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
        }

        private void DrawSourceActionButtons(float availableWidth)
        {
            GUILayoutOption width = GUILayout.Width(Mathf.Min(SourceActionButtonWidth, availableWidth));
            if (GUILayout.Button("Auto Setup Selected LDtk", width))
            {
                AutoResolveReferences(autoCreateMissingAssets, autoCreateSceneManager, out string report);
                lastReport = report;
            }

            if (GUILayout.Button("Create/Select TWC Asset", width))
            {
                CreateOrSelectTargetFromTemplate();
            }

            if (GUILayout.Button("Find/Create Manager", width))
            {
                manager = FindOrCreateManager(createSceneManager: true, out string managerReport, out _);
                lastReport = managerReport;
            }
        }

        private void SaveSelectedLdtkPath()
        {
            if (ldtkLevelAsset == null)
            {
                EditorPrefs.DeleteKey(LastLdtkPathPreference);
                return;
            }

            string path = AssetDatabase.GetAssetPath(ldtkLevelAsset);
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException("Selected LDtk asset has no project path.");
            }

            EditorPrefs.SetString(LastLdtkPathPreference, path);
        }

        private void Import(bool generateBuildLayers)
        {
            bool cleanupDeferred = false;
            try
            {
                ImportCore(generateBuildLayers, ref cleanupDeferred);
            }
            finally
            {
                if (!cleanupDeferred)
                {
                    CleanupTemporaryManager();
                }
            }
        }

        private void ImportCore(bool generateBuildLayers, ref bool cleanupDeferred)
        {
            lastReport = string.Empty;

            if (!TryGetLdtkPath(out string ldtkPath))
            {
                return;
            }

            if (autoResolveForSelectedLdtk)
            {
                bool autoResolved = AutoResolveReferences(autoCreateMissingAssets, generateBuildLayers && autoCreateSceneManager, out string autoReport);
                if (!autoResolved)
                {
                    lastReport = autoReport;
                    return;
                }
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

            string templateSyncReport = string.Empty;
            if (!TrySyncBuildSettingsFromTemplate(out templateSyncReport))
            {
                return;
            }

            if (!TryFindBlueprintLayer(WaterLayerName, out BlueprintLayer waterLayer))
            {
                return;
            }

            if (!EnsurePlatformAndSlopeLayers(
                    plan,
                    out Dictionary<int, BlueprintLayer> platformLayers,
                    out Dictionary<int, BlueprintLayer> platformDualControlLayers,
                    out BlueprintLayer slopeLayer,
                    out string terrainLayerReport))
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
            foreach (PlatformImport platform in plan.platforms)
            {
                clearedModifierCount += ImportCells(platformLayers[platform.height], platform.cells, clearBlueprintModifiers);
                clearedModifierCount += ImportCells(
                    platformDualControlLayers[platform.height],
                    platform.dualControlCells,
                    clearBlueprintModifiers);
            }

            clearedModifierCount += ImportCells(slopeLayer, plan.slopeCells, clearBlueprintModifiers);
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
                        try
                        {
                            TerrainPrefabResult delayedTerrainResult = SaveTerrainPrefabFromManager();
                            FlowNavigationGridImportResult delayedFlowGridResult = GenerateFlowNavigationGrid(plan, delayedTerrainResult.targetPath);
                            EntityImportResult delayedEntityImportResult = ImportEntityPresetPointsIfRequested(plan, delayedTerrainResult.targetPath, delayedFlowGridResult.assets);
                            delayedEntityImportResult.terrainResult = delayedTerrainResult;
                            delayedEntityImportResult.flowNavigationGridResult = delayedFlowGridResult;
                            AssetDatabase.SaveAssets();

                            lastReport = BuildImportReport(ldtkPath, plan, strongholdLayers, true, clearedModifierCount, delayedEntityImportResult);
                            if (!string.IsNullOrEmpty(ensureReport))
                            {
                                lastReport = ensureReport + "\n\n" + lastReport;
                            }

                            if (!string.IsNullOrEmpty(terrainLayerReport))
                            {
                                lastReport = terrainLayerReport + "\n\n" + lastReport;
                            }

                            if (!string.IsNullOrEmpty(templateSyncReport))
                            {
                                lastReport = templateSyncReport + "\n\n" + lastReport;
                            }

                            Debug.Log(lastReport);
                        }
                        finally
                        {
                            CleanupTemporaryManager();
                        }
                    });
                cleanupDeferred = true;

                lastReport = "[LDtk Import] TileWorldCreator build layers are generating. Terrain prefab and level prefab will be saved after the editor build pass finishes.";
                Debug.Log(lastReport);
                return;
            }

            TerrainPrefabResult terrainPrefabResult = TerrainPrefabResult.Skipped("Build layers were not generated.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GetDefaultTerrainPrefabPath()) != null)
            {
                terrainPrefabResult = TerrainPrefabResult.Existing(GetDefaultTerrainPrefabPath());
            }

            FlowNavigationGridImportResult flowGridResult = LoadExistingFlowNavigationGrids();
            EntityImportResult entityImportResult = ImportEntityPresetPointsIfRequested(plan, terrainPrefabResult.targetPath, flowGridResult.assets);
            entityImportResult.terrainResult = terrainPrefabResult;
            entityImportResult.flowNavigationGridResult = flowGridResult;

            if (!generateBuildLayers)
            {
                MarkImportedAssetsDirty(
                    configuration,
                    platformLayers.Values.Concat(platformDualControlLayers.Values),
                    slopeLayer,
                    waterLayer,
                    strongholdLayers);
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

            if (!string.IsNullOrEmpty(terrainLayerReport))
            {
                lastReport = terrainLayerReport + "\n\n" + lastReport;
            }

            if (!string.IsNullOrEmpty(templateSyncReport))
            {
                lastReport = templateSyncReport + "\n\n" + lastReport;
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

        private string GetDefaultFlowNavigationGridAssetPath()
        {
            string terrainPrefabPath = GetDefaultTerrainPrefabPath();
            if (string.IsNullOrEmpty(terrainPrefabPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(terrainPrefabPath)?.Replace("\\", "/");
            string name = Path.GetFileNameWithoutExtension(terrainPrefabPath);
            return $"{folder}/{name}_FlowNavigationGrid_Medium.asset";
        }

        private Configuration GetTemplateConfiguration()
        {
            if (templateConfiguration == null)
            {
                templateConfiguration = AssetDatabase.LoadAssetAtPath<Configuration>(TemplateConfigurationPath);
            }

            return templateConfiguration;
        }

        private string GetTemplateConfigurationPath()
        {
            Configuration template = GetTemplateConfiguration();
            string path = template != null ? AssetDatabase.GetAssetPath(template) : string.Empty;
            return !string.IsNullOrEmpty(path) ? path : TemplateConfigurationPath;
        }

        private bool AutoResolveReferences(bool createMissingAssets, bool createSceneManager, out string report)
        {
            var builder = new StringBuilder();
            bool success = true;

            if (ldtkLevelAsset == null)
            {
                ldtkLevelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefaultLdtkPath);
            }

            string ldtkPath = GetSelectedLdtkPath();
            if (string.IsNullOrEmpty(ldtkPath))
            {
                report = "No LDtk level json selected.";
                return false;
            }

            if (levelPrefabTemplate == null)
            {
                levelPrefabTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPrefabTemplatePath);
            }

            if (GetTemplateConfiguration() != null)
            {
                builder.AppendLine($"Selected TWC template: {GetTemplateConfigurationPath()}");
            }

            if (entityPresetPointPrefab == null)
            {
                entityPresetPointPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EntityPresetPointPrefabPath);
            }

            if (autoResolveForSelectedLdtk || configuration == null)
            {
                string targetPath = GetDefaultTargetPath();
                Configuration targetConfiguration = !string.IsNullOrEmpty(targetPath)
                    ? AssetDatabase.LoadAssetAtPath<Configuration>(targetPath)
                    : null;

                if (targetConfiguration == null && createMissingAssets)
                {
                    success &= TryCreateOrSelectTargetFromTemplate(out targetPath, out bool createdConfiguration);
                    targetConfiguration = configuration;
                    if (createdConfiguration)
                    {
                        builder.AppendLine($"Created TWC asset: {targetPath}");
                    }
                }
                else if (targetConfiguration == null)
                {
                    configuration = null;
                    manager = null;
                    builder.AppendLine($"TWC asset not found: {targetPath}");
                }
                else if (targetConfiguration != null)
                {
                    configuration = targetConfiguration;
                    builder.AppendLine($"Selected TWC asset: {targetPath}");
                }
            }

            if (importEntityPresetPoints && (autoResolveForSelectedLdtk || levelPrefabTarget == null))
            {
                string levelPrefabPath = GetDefaultLevelPrefabPath();
                GameObject targetLevelPrefab = !string.IsNullOrEmpty(levelPrefabPath)
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(levelPrefabPath)
                    : null;

                if (targetLevelPrefab == null && createMissingAssets)
                {
                    success &= TryCreateOrSelectLevelPrefabFromTemplate(out levelPrefabPath);
                    targetLevelPrefab = levelPrefabTarget;
                    if (targetLevelPrefab != null)
                    {
                        builder.AppendLine($"Created level prefab: {levelPrefabPath}");
                    }
                }
                else if (targetLevelPrefab == null)
                {
                    levelPrefabTarget = null;
                    builder.AppendLine($"Level prefab not found: {levelPrefabPath}");
                }
                else if (targetLevelPrefab != null)
                {
                    levelPrefabTarget = targetLevelPrefab;
                    builder.AppendLine($"Selected level prefab: {levelPrefabPath}");
                }
            }

            if (configuration != null)
            {
                manager = FindOrCreateManager(
                    createSceneManager && autoCreateSceneManager,
                    out string managerReport,
                    out bool createdManager);
                if (createdManager)
                {
                    temporaryManagerObject = manager.gameObject;
                }

                if (!string.IsNullOrEmpty(managerReport))
                {
                    builder.AppendLine(managerReport);
                }
            }

            if (entityPresetPointPrefab != null)
            {
                builder.AppendLine($"Selected point prefab: {EntityPresetPointPrefabPath}");
            }

            report = builder.Length > 0 ? builder.ToString().TrimEnd() : "Auto setup finished.";
            return success;
        }

        private TileWorldCreatorManager FindOrCreateManager(bool createSceneManager, out string report, out bool created)
        {
            report = string.Empty;
            created = false;
            if (configuration == null)
            {
                report = "TWC manager skipped: no configuration selected.";
                return null;
            }

            TileWorldCreatorManager found = FindObjectsByType<TileWorldCreatorManager>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                .FirstOrDefault(x => x != null && x.configuration == configuration);
            if (found != null)
            {
                report = $"Selected TWC manager: {found.name}";
                return found;
            }

            if (!createSceneManager)
            {
                report = "No TWC manager found for selected configuration.";
                return null;
            }

            string managerName = $"{configuration.name}_TWC";
            var managerObject = new GameObject(managerName);
            Undo.RegisterCreatedObjectUndo(managerObject, "Create LDtk TWC Manager");
            found = managerObject.AddComponent<TileWorldCreatorManager>();
            found.configuration = configuration;
            created = true;
            EditorSceneManager.MarkSceneDirty(managerObject.scene);
            report = $"Created TWC manager: {managerName}";
            return found;
        }

        private void CleanupTemporaryManager()
        {
            if (temporaryManagerObject == null)
            {
                return;
            }

            GameObject target = temporaryManagerObject;
            temporaryManagerObject = null;
            if (manager != null && manager.gameObject == target)
            {
                manager = null;
            }

            string targetName = target.name;
            DestroyImmediate(target);
            Debug.Log("[LDtk Import] Removed temporary TWC manager: " + targetName);
        }

        private void CreateOrSelectTargetFromTemplate()
        {
            if (!TryCreateOrSelectTargetFromTemplate(out string targetPath, out bool created))
            {
                return;
            }

            lastReport = created ? $"Created target from template: {targetPath}" : $"Selected existing target: {targetPath}";
        }

        private bool TryCreateOrSelectTargetFromTemplate(out string targetPath, out bool created)
        {
            created = false;
            targetPath = string.Empty;
            if (!TryGetLdtkPath(out string ldtkPath))
            {
                return false;
            }

            targetPath = GetDefaultTargetPath();
            if (string.IsNullOrEmpty(targetPath))
            {
                return false;
            }

            Configuration existing = AssetDatabase.LoadAssetAtPath<Configuration>(targetPath);
            if (existing != null)
            {
                configuration = existing;
                manager = FindOrCreateManager(createSceneManager: false, out _, out _);
                return true;
            }

            string templatePath = GetTemplateConfigurationPath();
            if (AssetDatabase.LoadAssetAtPath<Configuration>(templatePath) == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"TWC template is invalid:\n{templatePath}", "OK");
                return false;
            }

            if (!AssetDatabase.CopyAsset(templatePath, targetPath))
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Failed to clone template:\n{templatePath}\n-> {targetPath}", "OK");
                return false;
            }

            AssetDatabase.ImportAsset(targetPath);
            configuration = AssetDatabase.LoadAssetAtPath<Configuration>(targetPath);
            if (configuration == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", $"Cloned asset could not be loaded:\n{targetPath}", "OK");
                return false;
            }

            configuration.name = Path.GetFileNameWithoutExtension(ldtkPath);
            EditorUtility.SetDirty(configuration);
            AssetDatabase.SaveAssets();
            manager = null;
            created = true;
            return true;
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

            if (!TryFindPlatformLdtkLayers(level, out List<PlatformLdtkLayer> platformLayers) ||
                !TryFindLdtkLayer(level, WaterLayerName, out LdtkLayerInstance water) ||
                !TryFindLdtkLayer(level, StrongholdLayerName, out LdtkLayerInstance stronghold))
            {
                return false;
            }

            LdtkLayerInstance referenceLayer = platformLayers[0].layer;
            int width = referenceLayer.__cWid;
            int height = referenceLayer.__cHei;
            int gridSize = referenceLayer.__gridSize;
            LdtkLayerInstance wall = FindOptionalIntGridLayer(
                level,
                WallLayerName,
                referenceLayer);
            LdtkLayerInstance wallForbidden = FindOptionalIntGridLayer(
                level,
                WallForbiddenLayerName,
                referenceLayer);
            float cellSize = GetTileWorldCellSize();
            LdtkLayerInstance slope = level.layerInstances.FirstOrDefault(x =>
                x != null &&
                string.Equals(x.__identifier, SlopeLayerName, StringComparison.OrdinalIgnoreCase));
            if (slope != null && (!string.Equals(slope.__type, "IntGrid", StringComparison.OrdinalIgnoreCase) || slope.intGridCsv == null))
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Slope must be an IntGrid layer.", "OK");
                return false;
            }
            var validatedLayers = platformLayers.Select(x => x.layer)
                .Concat(new[] { water, stronghold, wall, wallForbidden })
                .ToList();
            if (slope != null)
            {
                validatedLayers.Add(slope);
            }

            if (!ValidateSameGrid(validatedLayers.ToArray()))
            {
                return false;
            }

            if (!TryReadStrongholdComponents(stronghold, width, height, out var strongholdComponentsByFaction))
            {
                return false;
            }

            var platformImports = platformLayers
                .Select(x => new PlatformImport
                {
                    height = x.height,
                    cells = ReadIntGridCells(x.layer, width, height)
                })
                .OrderBy(x => x.height)
                .ToList();

            Dictionary<Vector2, int> topPlatformHeights = LdtkSlopeLayoutResolver.GetTopPlatformHeights(
                platformImports.Select(platform =>
                    new KeyValuePair<int, IEnumerable<Vector2>>(platform.height, platform.cells)));

            foreach (PlatformImport platform in platformImports)
            {
                if (!LdtkDualGridControlBuilder.TryBuildExactControls(
                        platform.cells,
                        out platform.dualControlCells,
                        out HashSet<Vector2> missingCells,
                        out HashSet<Vector2> extraCells,
                        out string controlError))
                {
                    string message = "Platform layer " + BuildPlatformLayerName(platform.height) +
                                     " cannot be represented exactly by TWC dual grid.";
                    if (!string.IsNullOrEmpty(controlError))
                    {
                        message += " " + controlError;
                    }

                    if (missingCells.Count > 0)
                    {
                        message += " Missing output cells: " + FormatCellsForError(missingCells) + ".";
                    }

                    if (extraCells.Count > 0)
                    {
                        message += " Extra output cells: " + FormatCellsForError(extraCells) + ".";
                    }

                    Debug.LogError("[LDtk Import] " + message);
                    EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                    return false;
                }
            }

            HashSet<Vector2> slopeCells = slope != null
                ? ReadIntGridCells(slope, width, height)
                : new HashSet<Vector2>();
            if (!TryApplySlopeSupports(platformImports, slopeCells))
            {
                return false;
            }

            topPlatformHeights = LdtkSlopeLayoutResolver.GetTopPlatformHeights(
                platformImports.Select(platform =>
                    new KeyValuePair<int, IEnumerable<Vector2>>(platform.height, platform.cells)));
            HashSet<Vector2> presetWallCells = ReadIntGridCells(wall, width, height);
            HashSet<Vector2> forbiddenWallCells = ReadIntGridCells(wallForbidden, width, height);
            if (!TryResolveWallCells(
                    strongholdComponentsByFaction,
                    topPlatformHeights,
                    slopeCells,
                    presetWallCells,
                    forbiddenWallCells,
                    out HashSet<Vector2> previewWallCells,
                    out string wallError))
            {
                EditorUtility.DisplayDialog("LDtk import failed", wallError, "OK");
                return false;
            }

            plan = new ImportPlan
            {
                width = width,
                height = height,
                gridSize = gridSize,
                cellSize = cellSize,
                pixelHeight = level.pxHei > 0 ? level.pxHei : height * gridSize,
                platforms = platformImports,
                slopeCells = slopeCells,
                waterCells = ReadIntGridCells(water, width, height),
                walkableWallCells = topPlatformHeights,
                previewWallCells = previewWallCells,
                presetWallCells = presetWallCells,
                strongholdComponentsByFaction = strongholdComponentsByFaction,
                entityPoints = ReadEntityPresetPoints(level, gridSize, cellSize)
            };

            return true;
        }

        private static bool TryFindPlatformLdtkLayers(LdtkLevelJson level, out List<PlatformLdtkLayer> platformLayers)
        {
            platformLayers = new List<PlatformLdtkLayer>();
            if (level.layerInstances.Any(layer =>
                    layer != null &&
                    string.Equals(layer.__identifier, LegacyPlaneLayerName, StringComparison.OrdinalIgnoreCase)))
            {
                EditorUtility.DisplayDialog(
                    "LDtk import failed",
                    "Legacy layer Plane is no longer supported. Rename it to Plane_H0.",
                    "OK");
                return false;
            }

            foreach (LdtkLayerInstance layer in level.layerInstances)
            {
                if (layer == null)
                {
                    continue;
                }

                Match match = Regex.Match(layer.__identifier ?? string.Empty, @"^Plane_H(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                int height = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (height < MinimumPlatformHeight || height > MaximumPlatformHeight)
                {
                    EditorUtility.DisplayDialog("LDtk import failed", $"Unsupported platform height: {layer.__identifier}. Expected Plane_H0..Plane_H5.", "OK");
                    return false;
                }

                if (!string.Equals(layer.__type, "IntGrid", StringComparison.OrdinalIgnoreCase) || layer.intGridCsv == null)
                {
                    EditorUtility.DisplayDialog("LDtk import failed", $"{layer.__identifier} must be an IntGrid layer.", "OK");
                    return false;
                }

                platformLayers.Add(new PlatformLdtkLayer
                {
                    height = height,
                    layer = layer
                });
            }

            if (platformLayers.Count == 0)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Missing IntGrid platform layers. Expected Plane_H0..Plane_H5.", "OK");
                return false;
            }

            if (platformLayers.GroupBy(x => x.height).Any(group => group.Count() > 1))
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Duplicate platform height layer found. Each Plane_H0..Plane_H5 height may appear only once.", "OK");
                return false;
            }

            platformLayers.Sort((a, b) => a.height.CompareTo(b.height));
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
                    EditorUtility.DisplayDialog("LDtk import failed", "Plane_H*, Slope, Water and SH must use the same LDtk grid.", "OK");
                    return false;
                }

                if (layer.intGridCsv == null || layer.intGridCsv.Length != width * height)
                {
                    EditorUtility.DisplayDialog("LDtk import failed", $"Layer {layer.__identifier} has invalid IntGrid data length.", "OK");
                    return false;
                }
            }

            return true;
        }

        private static bool TryApplySlopeSupports(List<PlatformImport> platforms, HashSet<Vector2> slopeCells)
        {
            var levels = platforms.Select(platform =>
                new KeyValuePair<int, IEnumerable<Vector2>>(platform.height, platform.cells));
            Dictionary<Vector2, int> topHeights = LdtkSlopeLayoutResolver.GetTopPlatformHeights(levels);
            if (!LdtkSlopeLayoutResolver.TryResolve(
                    slopeCells,
                    topHeights,
                    0f,
                    90f,
                    out LdtkSlopeLayout layout,
                    out string error))
            {
                EditorUtility.DisplayDialog("LDtk import failed", error, "OK");
                return false;
            }

            foreach (KeyValuePair<Vector2, int> support in layout.requiredPlatformHeights)
            {
                if (support.Value < MinimumPlatformHeight || support.Value > MaximumPlatformHeight)
                {
                    EditorUtility.DisplayDialog(
                        "LDtk import failed",
                        "Slope support at " + FormatCell(support.Key) + " resolves outside Plane_H0..Plane_H5.",
                        "OK");
                    return false;
                }

                PlatformImport platform = platforms.FirstOrDefault(item => item.height == support.Value);
                if (platform == null)
                {
                    platform = new PlatformImport
                    {
                        height = support.Value,
                        cells = new HashSet<Vector2>()
                    };
                    platforms.Add(platform);
                }

                platform.cells.Add(support.Key);
            }

            platforms.Sort((left, right) => left.height.CompareTo(right.height));
            return true;
        }

        private static string FormatCell(Vector2 cell)
        {
            return $"({Mathf.RoundToInt(cell.x)},{Mathf.RoundToInt(cell.y)})";
        }

        private bool TrySyncBuildSettingsFromTemplate(out string report)
        {
            report = string.Empty;
            if (!syncBuildSettingsFromTemplate || configuration == null)
            {
                return true;
            }

            Configuration template = GetTemplateConfiguration();
            if (template == null)
            {
                const string message = "TWC template is not assigned.";
                EditorUtility.DisplayDialog("LDtk import failed", message, "OK");
                return false;
            }

            if (template == configuration)
            {
                return true;
            }

            var targetBuildLayers = GetBuildLayers(configuration).OfType<TilesBuildLayer>().ToList();
            var templateBuildLayers = GetBuildLayers(template).OfType<TilesBuildLayer>().ToList();
            if (targetBuildLayers.Count == 0 || templateBuildLayers.Count == 0)
            {
                report = "Build settings sync skipped: no Tiles build layer found.";
                return true;
            }

            Dictionary<string, string> templateBlueprintNamesByGuid = GetBlueprintLayers(template)
                .Where(x => x != null && !string.IsNullOrEmpty(x.guid))
                .GroupBy(x => x.guid)
                .ToDictionary(x => x.Key, x => x.First().layerName);
            Dictionary<string, BlueprintLayer> targetBlueprintsByName = GetBlueprintLayers(configuration)
                .Where(x => x != null && !string.IsNullOrEmpty(x.layerName))
                .GroupBy(x => x.layerName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            int syncedCount = 0;
            foreach (TilesBuildLayer targetLayer in targetBuildLayers)
            {
                TilesBuildLayer templateLayer = FindTemplateBuildLayer(templateBuildLayers, targetLayer.layerName);
                if (templateLayer == null)
                {
                    continue;
                }

                SyncTilesBuildLayerFromTemplate(templateLayer, targetLayer, templateBlueprintNamesByGuid, targetBlueprintsByName);
                syncedCount++;
            }

            if (syncedCount > 0)
            {
                EditorUtility.SetDirty(configuration);
                report = $"Synced build settings from template: {GetTemplateConfigurationPath()} ({syncedCount} layers)";
            }

            return true;
        }

        private static TilesBuildLayer FindTemplateBuildLayer(
            List<TilesBuildLayer> templateBuildLayers,
            string targetLayerName)
        {
            return templateBuildLayers.FirstOrDefault(x => string.Equals(x.layerName, targetLayerName, StringComparison.OrdinalIgnoreCase));
        }

        private void SyncTilesBuildLayerFromTemplate(
            TilesBuildLayer templateLayer,
            TilesBuildLayer targetLayer,
            Dictionary<string, string> templateBlueprintNamesByGuid,
            Dictionary<string, BlueprintLayer> targetBlueprintsByName)
        {
            string layerName = targetLayer.layerName;
            string guid = targetLayer.guid;
            string hierarchyLayerId = targetLayer.hierarchyLayerID;
            string assignedBlueprintGuid = targetLayer.assignedBlueprintLayerGuid;
            BlueprintLayer currentBlueprintLayer = targetLayer.currentBlueprintLayer != null
                ? targetLayer.currentBlueprintLayer
                : configuration.GetBlueprintLayerByGuid(assignedBlueprintGuid);
            HideFlags hideFlags = targetLayer.hideFlags;

            Undo.RegisterCompleteObjectUndo(targetLayer, "Sync TWC Build Layer From Template");
            EditorUtility.CopySerialized(templateLayer, targetLayer);

            targetLayer.hideFlags = hideFlags;
            targetLayer.layerName = layerName;
            targetLayer.guid = guid;
            targetLayer.hierarchyLayerID = string.IsNullOrEmpty(hierarchyLayerId) ? guid : hierarchyLayerId;
            targetLayer.configuration = configuration;

            BlueprintLayer assignedBlueprintLayer = ResolveTargetBlueprintLayer(
                templateLayer.assignedBlueprintLayerGuid,
                currentBlueprintLayer,
                templateBlueprintNamesByGuid,
                targetBlueprintsByName);
            if (assignedBlueprintLayer != null)
            {
                targetLayer.assignedBlueprintLayerGuid = assignedBlueprintLayer.guid;
                targetLayer.currentBlueprintLayer = assignedBlueprintLayer;
            }
            else
            {
                targetLayer.assignedBlueprintLayerGuid = assignedBlueprintGuid;
                targetLayer.currentBlueprintLayer = currentBlueprintLayer;
            }

            RemapCopiedBuildLayerBlueprintReferences(targetLayer, templateBlueprintNamesByGuid, targetBlueprintsByName);
            targetLayer.ResetLayer(manager != null && manager.configuration == configuration ? manager : null);
            EditorUtility.SetDirty(targetLayer);
        }

        private static BlueprintLayer ResolveTargetBlueprintLayer(
            string templateBlueprintGuid,
            BlueprintLayer fallback,
            Dictionary<string, string> templateBlueprintNamesByGuid,
            Dictionary<string, BlueprintLayer> targetBlueprintsByName)
        {
            if (templateBlueprintNamesByGuid.TryGetValue(templateBlueprintGuid, out string templateBlueprintName) &&
                targetBlueprintsByName.TryGetValue(templateBlueprintName, out BlueprintLayer targetBlueprint))
            {
                return targetBlueprint;
            }

            return fallback;
        }

        private static void RemapCopiedBuildLayerBlueprintReferences(
            TilesBuildLayer targetLayer,
            Dictionary<string, string> templateBlueprintNamesByGuid,
            Dictionary<string, BlueprintLayer> targetBlueprintsByName)
        {
            if (targetLayer.masks != null)
            {
                foreach (BuildLayerMask mask in targetLayer.masks)
                {
                    if (mask == null)
                    {
                        continue;
                    }

                    mask.assignedBlueprintLayerGuid = RemapTemplateBlueprintGuid(mask.assignedBlueprintLayerGuid, templateBlueprintNamesByGuid, targetBlueprintsByName);
                }
            }

            if (targetLayer.tileLayers == null)
            {
                return;
            }

            foreach (TilesBuildLayer.TileLayers tileLayer in targetLayer.tileLayers)
            {
                if (tileLayer?.layerOverrides == null)
                {
                    continue;
                }

                foreach (TilesBuildLayer.TilePresetOverride layerOverride in tileLayer.layerOverrides)
                {
                    if (layerOverride == null)
                    {
                        continue;
                    }

                    layerOverride.blueprintOverrideLayer = RemapTemplateBlueprintGuid(layerOverride.blueprintOverrideLayer, templateBlueprintNamesByGuid, targetBlueprintsByName);
                }
            }
        }

        private static string RemapTemplateBlueprintGuid(
            string templateBlueprintGuid,
            Dictionary<string, string> templateBlueprintNamesByGuid,
            Dictionary<string, BlueprintLayer> targetBlueprintsByName)
        {
            if (string.IsNullOrEmpty(templateBlueprintGuid))
            {
                return templateBlueprintGuid;
            }

            return templateBlueprintNamesByGuid.TryGetValue(templateBlueprintGuid, out string templateBlueprintName) &&
                   targetBlueprintsByName.TryGetValue(templateBlueprintName, out BlueprintLayer targetBlueprint)
                ? targetBlueprint.guid
                : templateBlueprintGuid;
        }

        private static IEnumerable<BuildLayer> GetBuildLayers(Configuration asset)
        {
            if (asset?.buildLayerFolders == null)
            {
                yield break;
            }

            foreach (BuildLayerFolder folder in asset.buildLayerFolders)
            {
                if (folder?.buildLayers == null)
                {
                    continue;
                }

                foreach (BuildLayer layer in folder.buildLayers)
                {
                    if (layer != null)
                    {
                        yield return layer;
                    }
                }
            }
        }

        private static IEnumerable<BlueprintLayer> GetBlueprintLayers(Configuration asset)
        {
            if (asset?.blueprintLayerFolders == null)
            {
                yield break;
            }

            foreach (BlueprintLayerFolder folder in asset.blueprintLayerFolders)
            {
                if (folder?.blueprintLayers == null)
                {
                    continue;
                }

                foreach (BlueprintLayer layer in folder.blueprintLayers)
                {
                    if (layer != null)
                    {
                        yield return layer;
                    }
                }
            }
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

        private bool EnsurePlatformAndSlopeLayers(
            ImportPlan plan,
            out Dictionary<int, BlueprintLayer> platformLayers,
            out Dictionary<int, BlueprintLayer> platformDualControlLayers,
            out BlueprintLayer slopeLayer,
            out string report)
        {
            platformLayers = new Dictionary<int, BlueprintLayer>();
            platformDualControlLayers = new Dictionary<int, BlueprintLayer>();
            slopeLayer = null;
            report = string.Empty;
            if (!TryFindBlueprintLayer(PlaneLayerName, out BlueprintLayer planeTemplate))
            {
                return false;
            }

            string planeBuildLayerName = "Build " + PlaneLayerName;
            TilesBuildLayer buildTemplate = GetBuildLayers(configuration)
                .OfType<TilesBuildLayer>()
                .FirstOrDefault(layer => string.Equals(layer.layerName, planeBuildLayerName, StringComparison.OrdinalIgnoreCase));
            if (buildTemplate == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Missing TWC build layer: " + planeBuildLayerName, "OK");
                return false;
            }

            var changes = new List<string>();
            foreach (PlatformImport platform in plan.platforms)
            {
                string blueprintName = BuildPlatformLayerName(platform.height);
                BlueprintLayer blueprint = GetBlueprintLayers(configuration)
                    .FirstOrDefault(layer => string.Equals(layer.layerName, blueprintName, StringComparison.OrdinalIgnoreCase));
                if (blueprint == null)
                {
                    if (!TryCloneBlueprintLayer(planeTemplate, blueprintName, out blueprint))
                    {
                        EditorUtility.DisplayDialog("LDtk import failed", "Failed to create TWC blueprint layer: " + blueprintName, "OK");
                        return false;
                    }

                    changes.Add("Created " + blueprintName);
                }

                blueprint.defaultLayerHeight = platform.height * plan.cellSize;
                blueprint.isEnabled = true;
                platformLayers.Add(platform.height, blueprint);

                string dualControlName = BuildPlatformDualControlLayerName(platform.height);
                BlueprintLayer dualControlLayer = GetBlueprintLayers(configuration)
                    .FirstOrDefault(layer => string.Equals(layer.layerName, dualControlName, StringComparison.OrdinalIgnoreCase));
                if (dualControlLayer == null)
                {
                    if (!TryCloneBlueprintLayer(blueprint, dualControlName, out dualControlLayer))
                    {
                        EditorUtility.DisplayDialog("LDtk import failed", "Failed to create TWC blueprint layer: " + dualControlName, "OK");
                        return false;
                    }

                    changes.Add("Created " + dualControlName);
                }

                dualControlLayer.defaultLayerHeight = blueprint.defaultLayerHeight;
                dualControlLayer.isEnabled = true;
                platformDualControlLayers.Add(platform.height, dualControlLayer);

                string buildLayerName = "Build " + blueprintName;
                TilesBuildLayer buildLayer = GetBuildLayers(configuration)
                    .OfType<TilesBuildLayer>()
                    .FirstOrDefault(layer => string.Equals(layer.layerName, buildLayerName, StringComparison.OrdinalIgnoreCase));
                if (buildLayer == null)
                {
                    if (!TryCloneTilesBuildLayer(buildTemplate, dualControlLayer, buildLayerName, out buildLayer))
                    {
                        EditorUtility.DisplayDialog("LDtk import failed", "Failed to create TWC build layer: " + buildLayerName, "OK");
                        return false;
                    }

                    changes.Add("Created " + buildLayerName);
                }

                buildLayer.assignedBlueprintLayerGuid = dualControlLayer.guid;
                buildLayer.currentBlueprintLayer = dualControlLayer;
                buildLayer.useDualGrid = true;
                buildLayer.isEnabled = true;
                EditorUtility.SetDirty(blueprint);
                EditorUtility.SetDirty(dualControlLayer);
                EditorUtility.SetDirty(buildLayer);
            }

            ClearUnusedPlatformLayers(platformLayers.Keys.ToHashSet());

            slopeLayer = GetBlueprintLayers(configuration)
                .FirstOrDefault(layer => string.Equals(layer.layerName, SlopeLayerName, StringComparison.OrdinalIgnoreCase));
            if (slopeLayer == null)
            {
                if (!TryCloneBlueprintLayer(planeTemplate, SlopeLayerName, out slopeLayer))
                {
                    EditorUtility.DisplayDialog("LDtk import failed", "Failed to create TWC blueprint layer: " + SlopeLayerName, "OK");
                    return false;
                }

                changes.Add("Created " + SlopeLayerName);
            }

            slopeLayer.defaultLayerHeight = 0f;
            slopeLayer.isEnabled = true;

            LdtkSlopeBuildLayer slopeBuildLayer = GetBuildLayers(configuration)
                .OfType<LdtkSlopeBuildLayer>()
                .FirstOrDefault(layer => string.Equals(layer.layerName, "Build Slope", StringComparison.OrdinalIgnoreCase));
            if (slopeBuildLayer == null)
            {
                slopeBuildLayer = ScriptableObject.CreateInstance<LdtkSlopeBuildLayer>();
                slopeBuildLayer.hideFlags = HideFlags.HideInHierarchy;
                slopeBuildLayer.layerName = "Build Slope";
                slopeBuildLayer.guid = Guid.NewGuid().ToString();
                slopeBuildLayer.hierarchyLayerID = slopeBuildLayer.guid;
                AssetDatabase.AddObjectToAsset(slopeBuildLayer, configuration);
                GetOrCreateBuildLayerFolder().buildLayers.Add(slopeBuildLayer);
                changes.Add("Created Build Slope");
            }

            GameObject ramp = AssetDatabase.LoadAssetAtPath<GameObject>(RampPrefabPath);
            if (ramp == null)
            {
                EditorUtility.DisplayDialog(
                    "LDtk import failed",
                    "Slope placeholder prefab is missing. Run Tools/TileWorldCreator/Generate Slope Placeholder Prefabs first.",
                    "OK");
                return false;
            }

            if (!TryCalculatePlatformSurfaceBase(buildTemplate, planeTemplate, plan.cellSize, out float surfaceBaseHeight) ||
                !TryCalculateSlopePlatformEndExtension(buildTemplate, plan.cellSize, out float platformEndExtension) ||
                !TryCalculateMaximumWalkableSlopeAngle(out float maximumSlopeAngle))
            {
                return false;
            }

            Dictionary<Vector2, int> importedTopHeights = LdtkSlopeLayoutResolver.GetTopPlatformHeights(
                plan.platforms.Select(platform =>
                    new KeyValuePair<int, IEnumerable<Vector2>>(platform.height, platform.cells)));
            if (!LdtkSlopeLayoutResolver.TryResolve(
                    plan.slopeCells,
                    importedTopHeights,
                    platformEndExtension,
                    maximumSlopeAngle,
                    out _,
                    out string slopeError))
            {
                EditorUtility.DisplayDialog("LDtk import failed", slopeError, "OK");
                return false;
            }

            slopeBuildLayer.assignedBlueprintLayerGuid = slopeLayer.guid;
            slopeBuildLayer.currentBlueprintLayer = slopeLayer;
            slopeBuildLayer.slopeLayer = slopeLayer;
            slopeBuildLayer.platformLevels = platformLayers
                .OrderBy(pair => pair.Key)
                .Select(pair => new LdtkSlopeBuildLayer.PlatformLevel { height = pair.Key, layer = pair.Value })
                .ToList();
            slopeBuildLayer.rampPrefab = ramp;
            slopeBuildLayer.platformEndExtension = platformEndExtension;
            slopeBuildLayer.maximumSlopeAngle = maximumSlopeAngle;
            slopeBuildLayer.surfaceBaseHeight = surfaceBaseHeight;
            slopeBuildLayer.surfaceHeightStep = plan.cellSize;
            slopeBuildLayer.objectLayer = buildTemplate.meshGenerationOverride
                ? buildTemplate.objectLayer
                : configuration.objectLayer;
            slopeBuildLayer.isEnabled = true;
            Debug.Log(
                "[LDtk Slope] Configured continuous ramp: platformEndExtension=" + platformEndExtension.ToString("0.###") +
                " cells, maximumSlopeAngle=" + maximumSlopeAngle.ToString("0.##") + " degrees.");

            EditorUtility.SetDirty(slopeLayer);
            EditorUtility.SetDirty(slopeBuildLayer);
            EditorUtility.SetDirty(configuration);
            report = changes.Count > 0 ? "Updated height/slope layers: " + string.Join(", ", changes) : string.Empty;
            return true;
        }

        private static string BuildPlatformLayerName(int height)
        {
            return PlatformLayerPrefix + height.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildPlatformDualControlLayerName(int height)
        {
            return PlatformDualControlLayerPrefix + height.ToString(CultureInfo.InvariantCulture);
        }

        private bool TryCloneTilesBuildLayer(
            TilesBuildLayer template,
            BlueprintLayer blueprint,
            string layerName,
            out TilesBuildLayer newLayer)
        {
            newLayer = ScriptableObject.CreateInstance<TilesBuildLayer>();
            newLayer.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(newLayer, configuration);
            EditorUtility.CopySerialized(template, newLayer);
            newLayer.hideFlags = HideFlags.HideInHierarchy;
            newLayer.layerName = layerName;
            newLayer.guid = Guid.NewGuid().ToString();
            newLayer.hierarchyLayerID = newLayer.guid;
            newLayer.configuration = configuration;
            newLayer.assignedBlueprintLayerGuid = blueprint.guid;
            newLayer.currentBlueprintLayer = blueprint;
            newLayer.isEnabled = true;
            GetOrCreateBuildLayerFolder().buildLayers.Add(newLayer);
            EditorUtility.SetDirty(newLayer);
            EditorUtility.SetDirty(configuration);
            return true;
        }

        private BuildLayerFolder GetOrCreateBuildLayerFolder()
        {
            if (configuration.buildLayerFolders == null)
            {
                configuration.buildLayerFolders = new List<BuildLayerFolder>();
            }

            BuildLayerFolder folder = configuration.buildLayerFolders.FirstOrDefault();
            if (folder == null)
            {
                folder = new BuildLayerFolder("Root");
                configuration.buildLayerFolders.Add(folder);
            }

            return folder;
        }

        private void ClearUnusedPlatformLayers(HashSet<int> importedHeights)
        {
            for (int height = MinimumPlatformHeight; height <= MaximumPlatformHeight; height++)
            {
                if (importedHeights.Contains(height))
                {
                    continue;
                }

                string blueprintName = BuildPlatformLayerName(height);
                BlueprintLayer blueprint = GetBlueprintLayers(configuration)
                    .FirstOrDefault(layer => string.Equals(layer.layerName, blueprintName, StringComparison.OrdinalIgnoreCase));
                if (blueprint != null)
                {
                    blueprint.ClearLayer(false);
                    EditorUtility.SetDirty(blueprint);
                }

                string dualControlName = BuildPlatformDualControlLayerName(height);
                BlueprintLayer dualControlLayer = GetBlueprintLayers(configuration)
                    .FirstOrDefault(layer => string.Equals(layer.layerName, dualControlName, StringComparison.OrdinalIgnoreCase));
                if (dualControlLayer != null)
                {
                    dualControlLayer.ClearLayer(false);
                    EditorUtility.SetDirty(dualControlLayer);
                }

            }
        }

        private static string FormatCellsForError(IEnumerable<Vector2> cells)
        {
            return string.Join(
                ", ",
                cells.OrderBy(cell => cell.y)
                    .ThenBy(cell => cell.x)
                    .Take(16)
                    .Select(FormatCell));
        }

        private static bool TryCalculatePlatformSurfaceBase(
            TilesBuildLayer buildLayer,
            BlueprintLayer blueprintLayer,
            float cellSize,
            out float surfaceBaseHeight)
        {
            surfaceBaseHeight = 0f;
            TilesBuildLayer.TilePresetSelection selection = buildLayer.tilePresetsTop?.FirstOrDefault(item => item?.preset != null);
            if (selection?.preset == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Build Plane_H0 has no top tile preset.", "OK");
                return false;
            }

            GameObject fillPrefab = selection.preset.GetTile(
                buildLayer.useDualGrid ? TilePreset.TileType.DUALGRD_fill : TilePreset.TileType.NRMGRD_fill,
                out _);
            if (fillPrefab == null || !TryGetPrefabLocalBounds(fillPrefab, out Bounds bounds))
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Build Plane_H0 top preset has no measurable fill mesh.", "OK");
                return false;
            }

            float topLayerOffset = buildLayer.tileLayers != null && buildLayer.tileLayers.Count > 0
                ? buildLayer.tileLayers.Max(layer => layer.heightOffset)
                : 0f;
            float meshScale = buildLayer.scaleTileToCellSize ? cellSize : 1f;
            surfaceBaseHeight = blueprintLayer.defaultLayerHeight + buildLayer.layerYOffset + topLayerOffset +
                                bounds.max.y * buildLayer.scaleOffset.y * meshScale;
            return true;
        }

        private static bool TryCalculateSlopePlatformEndExtension(
            TilesBuildLayer buildLayer,
            float cellSize,
            out float platformEndExtension)
        {
            platformEndExtension = 0f;
            if (cellSize <= 0f)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "TWC cell size must be positive to measure the slope connection.", "OK");
                return false;
            }

            TilesBuildLayer.TilePresetSelection selection = buildLayer.tilePresetsTop?.FirstOrDefault(item => item?.preset != null);
            if (selection?.preset == null)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "Build Plane_H0 has no top tile preset for slope connection measurement.", "OK");
                return false;
            }

            GameObject fillPrefab = selection.preset.GetTile(TilePreset.TileType.DUALGRD_fill, out _);
            GameObject edgePrefab = selection.preset.GetTile(TilePreset.TileType.DUALGRD_edge, out _);
            if (fillPrefab == null || edgePrefab == null ||
                !TryGetPrefabLocalBounds(fillPrefab, out Bounds fillBounds) ||
                !TryGetPrefabLocalBounds(edgePrefab, out Bounds edgeBounds))
            {
                EditorUtility.DisplayDialog(
                    "LDtk import failed",
                    "Build Plane_H0 dual-grid fill/edge prefabs are missing or have no measurable mesh.",
                    "OK");
                return false;
            }

            float scaleX = Mathf.Abs(buildLayer.scaleOffset.x) * (buildLayer.scaleTileToCellSize ? 1f : 1f / cellSize);
            float scaleZ = Mathf.Abs(buildLayer.scaleOffset.z) * (buildLayer.scaleTileToCellSize ? 1f : 1f / cellSize);
            platformEndExtension = Mathf.Max(
                (fillBounds.max.x - edgeBounds.max.x) * scaleX,
                (edgeBounds.min.x - fillBounds.min.x) * scaleX,
                (fillBounds.max.z - edgeBounds.max.z) * scaleZ,
                (edgeBounds.min.z - fillBounds.min.z) * scaleZ);
            if (platformEndExtension <= 0.0001f)
            {
                EditorUtility.DisplayDialog(
                    "LDtk import failed",
                    "Build Plane_H0 dual-grid edge prefab has no measurable inward offset relative to its fill prefab.",
                    "OK");
                return false;
            }

            return true;
        }

        private static bool TryCalculateMaximumWalkableSlopeAngle(out float maximumSlopeAngle)
        {
            maximumSlopeAngle = float.PositiveInfinity;
            int settingsCount = NavMesh.GetSettingsCount();
            if (settingsCount == 0)
            {
                EditorUtility.DisplayDialog("LDtk import failed", "The project has no NavMesh agent settings.", "OK");
                return false;
            }

            for (int i = 0; i < settingsCount; i++)
            {
                NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(i);
                if (settings.agentSlope <= 0f)
                {
                    EditorUtility.DisplayDialog(
                        "LDtk import failed",
                        "NavMesh agent type " + settings.agentTypeID + " has an invalid walkable slope: " + settings.agentSlope + ".",
                        "OK");
                    return false;
                }

                maximumSlopeAngle = Mathf.Min(maximumSlopeAngle, settings.agentSlope);
            }

            return true;
        }

        private static bool TryGetPrefabLocalBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Matrix4x4 rootWorldToLocal = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Matrix4x4 matrix = rootWorldToLocal * filter.transform.localToWorldMatrix;
                Bounds meshBounds = filter.sharedMesh.bounds;
                foreach (Vector3 corner in GetBoundsCorners(meshBounds))
                {
                    Vector3 point = matrix.MultiplyPoint3x4(corner);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return hasBounds;
        }

        private static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds)
        {
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    }
                }
            }
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

            var changed = new List<string>();
            foreach (var required in requiredByFaction.OrderBy(x => x.Key))
            {
                int factionId = required.Key;
                int requiredCount = required.Value.Count;
                for (int index = 0; index < requiredCount; index++)
                {
                    string layerName = BuildStrongholdLayerName(factionId, index);
                    BlueprintLayer blueprintLayer = existingBlueprintLayers.FirstOrDefault(x => string.Equals(x.layerName, layerName, StringComparison.OrdinalIgnoreCase));
                    if (blueprintLayer == null)
                    {
                        BlueprintLayer templateBlueprint = FindStrongholdBlueprintTemplate(existingBlueprintLayers, factionId);
                        if (!TryCloneBlueprintLayer(templateBlueprint, layerName, out blueprintLayer))
                        {
                            EditorUtility.DisplayDialog("LDtk import failed", $"Failed to create blueprint layer: {layerName}", "OK");
                            return false;
                        }

                        existingBlueprintLayers.Add(blueprintLayer);
                        changed.Add("Created " + layerName);
                    }
                }
            }

            var extraLayerNames = GetExtraStrongholdLayerNames(requiredByFaction, existingBlueprintLayers);
            if (extraLayerNames.Count > 0)
            {
                changed.Add("Cleared " + string.Join(", ", extraLayerNames));
            }

            if (changed.Count > 0)
            {
                report = "Updated logical stronghold layers: " + string.Join(", ", changed);
            }

            return true;
        }

        private static BlueprintLayer FindStrongholdBlueprintTemplate(List<BlueprintLayer> existingBlueprintLayers, int factionId)
        {
            BlueprintLayer templateBlueprint = existingBlueprintLayers
                .Where(x => ParseStrongholdName(x.layerName, out int layerFaction, out _) && layerFaction == factionId)
                .OrderBy(x =>
                {
                    ParseStrongholdName(x.layerName, out _, out int index);
                    return index;
                })
                .LastOrDefault();

            return templateBlueprint ?? existingBlueprintLayers[existingBlueprintLayers.Count - 1];
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
                RemoveTerrainNavMeshSurfaces(terrainPrefabPath);
                return TerrainPrefabResult.Saved(terrainPrefabPath, CountTerrainNavMeshSurfaces(terrainPrefabPath));
            }
            finally
            {
                DestroyImmediate(terrainClone);
            }
        }

        private static void RemoveTerrainNavMeshSurfaces(string terrainPrefabPath)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(terrainPrefabPath);
            try
            {
                NavMeshSurface[] surfaces = prefabRoot.GetComponents<NavMeshSurface>();
                for (int i = 0; i < surfaces.Length; i++)
                {
                    DestroyImmediate(surfaces[i]);
                }

                EditorUtility.SetDirty(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, terrainPrefabPath);
                AssetDatabase.ImportAsset(terrainPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int CountTerrainNavMeshSurfaces(string terrainPrefabPath)
        {
            GameObject terrainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath);
            return terrainPrefab != null ? terrainPrefab.GetComponents<NavMeshSurface>().Length : 0;
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

            if (autoResolveForSelectedLdtk)
            {
                AutoResolveReferences(autoCreateMissingAssets, createSceneManager: false, out _);
            }

            if (!TryLoadLevel(ldtkPath, out LdtkLevelJson level))
            {
                return;
            }

            int gridSize = GetDefaultEntityGridSize(level);
            float cellSize = GetTileWorldCellSize();
            var plan = new ImportPlan
            {
                width = configuration != null ? configuration.width : 0,
                height = configuration != null ? configuration.height : 0,
                gridSize = gridSize,
                cellSize = cellSize,
                pixelHeight = level.pxHei,
                entityPoints = ReadEntityPresetPoints(level, gridSize, cellSize)
            };

            string terrainPrefabPath = GetDefaultTerrainPrefabPath();
            TerrainPrefabResult terrainPrefabResult = AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath) != null
                ? TerrainPrefabResult.Existing(terrainPrefabPath)
                : TerrainPrefabResult.Skipped("Terrain prefab has not been generated.");
            FlowNavigationGridImportResult flowGridResult = LoadExistingFlowNavigationGrids();
            EntityImportResult result = ImportEntityPresetPointsIfRequested(plan, terrainPrefabResult.targetPath, flowGridResult.assets);
            result.terrainResult = terrainPrefabResult;
            result.flowNavigationGridResult = flowGridResult;

            lastReport = BuildEntityImportReport(ldtkPath, result);
            Debug.Log(lastReport);
        }

        private EntityImportResult ImportEntityPresetPointsIfRequested(ImportPlan plan, string terrainPrefabPath, IReadOnlyList<FlowNavigationGridAsset> flowNavigationGrids)
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
                if (plan.walkableWallCells != null)
                    ConfigureWallGridAuthoring(prefabRoot, plan);
                if (flowNavigationGrids != null && flowNavigationGrids.Count > 0)
                {
                    FlowNavigationGridAsset primaryGrid = flowNavigationGrids[0];
                    FlowNavigationGridAsset[] movementTypeGrids = flowNavigationGrids.Skip(1).Where(x => x != null).ToArray();
                    FlowNavigationGridPrefabBaker.AttachSourceToLevelPrefab(prefabRoot, primaryGrid, movementTypeGrids);
                    result.flowNavigationSourceAttached = true;
                }

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
                point.TeleportationId = pointData.teleportationId;
                point.SetDefendSpawnWeight(pointData.defendSpawnWeight);
                point.DestinationId = pointData.destinationId;
                point.DestinationRadius = pointData.destinationRadius;
                point.IsGameEndConditionBuilding = pointData.isGameEndConditionBuilding;
                point.UseCustomCoinReserves = pointData.useCustomCoinReserves;
                point.CustomCoinReserves = pointData.customCoinReserves;
                point.IsTestSlot = false;
                point.TestSlotIndex = 0;

                result.Add(pointData.pointType);
                EditorUtility.SetDirty(pointObject);
                EditorUtility.SetDirty(point);
            }

            EditorUtility.SetDirty(root.gameObject);
            return result;
        }

        private static void ConfigureWallGridAuthoring(GameObject prefabRoot, ImportPlan plan)
        {
            if (prefabRoot == null)
                throw new ArgumentNullException(nameof(prefabRoot));
            if (plan == null || plan.walkableWallCells == null || plan.previewWallCells == null || plan.presetWallCells == null)
                throw new InvalidOperationException("Wall grid import data is incomplete.");

            WallGridAuthoring[] existing = prefabRoot.GetComponentsInChildren<WallGridAuthoring>(true);
            WallGridAuthoring authoring;
            if (existing.Length == 0)
                authoring = prefabRoot.AddComponent<WallGridAuthoring>();
            else if (existing.Length == 1)
                authoring = existing[0];
            else
                throw new InvalidOperationException($"Level prefab contains {existing.Length} WallGridAuthoring components.");

            WallGridHeightCell[] walkable = plan.walkableWallCells
                .OrderBy(pair => Mathf.RoundToInt(pair.Key.y))
                .ThenBy(pair => Mathf.RoundToInt(pair.Key.x))
                .Select(pair => new WallGridHeightCell(
                    Mathf.RoundToInt(pair.Key.x),
                    Mathf.RoundToInt(pair.Key.y),
                    pair.Value))
                .ToArray();
            WallGridCell[] previews = ConvertWallCells(plan.previewWallCells);
            WallGridCell[] presets = ConvertWallCells(plan.presetWallCells);
            authoring.SetData(plan.width, plan.height, plan.cellSize, walkable, previews, presets);
            EditorUtility.SetDirty(authoring);
        }

        private static WallGridCell[] ConvertWallCells(IEnumerable<Vector2> cells)
        {
            return cells
                .Select(cell => new WallGridCell(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.y)))
                .OrderBy(cell => cell)
                .ToArray();
        }

        private static bool TryResolveWallCells(
            Dictionary<int, List<StrongholdComponent>> strongholdComponentsByFaction,
            Dictionary<Vector2, int> topPlatformHeights,
            HashSet<Vector2> slopeCells,
            HashSet<Vector2> presetWallCells,
            HashSet<Vector2> forbiddenWallCells,
            out HashSet<Vector2> previewWallCells,
            out string error)
        {
            previewWallCells = new HashSet<Vector2>();
            error = string.Empty;
            if (strongholdComponentsByFaction == null || topPlatformHeights == null
                || slopeCells == null || presetWallCells == null || forbiddenWallCells == null)
                throw new ArgumentNullException(nameof(strongholdComponentsByFaction));

            var allStrongholdCells = new HashSet<Vector2>();
            foreach (List<StrongholdComponent> components in strongholdComponentsByFaction.Values)
            {
                for (int i = 0; i < components.Count; i++)
                    allStrongholdCells.UnionWith(components[i].cells);
            }
            var groundOrSlope = new HashSet<Vector2>(topPlatformHeights.Keys);
            groundOrSlope.UnionWith(slopeCells);
            Vector2[] directions =
            {
                Vector2.right,
                Vector2.left,
                Vector2.up,
                Vector2.down,
            };

            foreach (Vector2 cell in allStrongholdCells)
            {
                bool outer = false;
                bool surroundedByTerrain = true;
                for (int i = 0; i < directions.Length; i++)
                {
                    Vector2 neighbor = cell + directions[i];
                    outer |= !allStrongholdCells.Contains(neighbor);
                    surroundedByTerrain &= groundOrSlope.Contains(neighbor);
                }
                if (outer && surroundedByTerrain && topPlatformHeights.ContainsKey(cell))
                    previewWallCells.Add(cell);
            }

            foreach (Vector2 cell in presetWallCells)
            {
                if (!allStrongholdCells.Contains(cell) || !topPlatformHeights.ContainsKey(cell))
                {
                    error = $"Preset wall cell {FormatCell(cell)} must be walkable and inside a stronghold.";
                    return false;
                }
            }
            foreach (Vector2 cell in forbiddenWallCells)
            {
                if (!allStrongholdCells.Contains(cell))
                {
                    error = $"NoWall cell {FormatCell(cell)} is outside every stronghold.";
                    return false;
                }
            }

            previewWallCells.ExceptWith(forbiddenWallCells);
            previewWallCells.ExceptWith(presetWallCells);
            return true;
        }

        private static LdtkLayerInstance FindOptionalIntGridLayer(
            LdtkLevelJson level,
            string layerName,
            LdtkLayerInstance referenceLayer)
        {
            if (level == null)
                throw new ArgumentNullException(nameof(level));
            if (referenceLayer == null)
                throw new ArgumentNullException(nameof(referenceLayer));
            LdtkLayerInstance layer = level.layerInstances.FirstOrDefault(x =>
                x != null && string.Equals(x.__identifier, layerName, StringComparison.OrdinalIgnoreCase));
            if (layer == null)
            {
                return new LdtkLayerInstance
                {
                    __identifier = layerName,
                    __type = "IntGrid",
                    __cWid = referenceLayer.__cWid,
                    __cHei = referenceLayer.__cHei,
                    __gridSize = referenceLayer.__gridSize,
                    intGridCsv = new int[checked(referenceLayer.__cWid * referenceLayer.__cHei)],
                };
            }
            if (!string.Equals(layer.__type, "IntGrid", StringComparison.OrdinalIgnoreCase)
                || layer.intGridCsv == null)
            {
                throw new InvalidOperationException($"Optional LDtk layer {layerName} must be an IntGrid layer.");
            }
            return layer;
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
            TransformData transformData = ResolveTerrainInstanceTransform(terrainRoots, terrainPrefabPath);

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
            terrainObject.transform.localPosition = transformData.localPosition;
            terrainObject.transform.localRotation = transformData.localRotation;
            terrainObject.transform.localScale = transformData.localScale;
            EditorUtility.SetDirty(terrainObject);
            return terrainRoots.Length;
        }

        private static TransformData ResolveTerrainInstanceTransform(Transform[] terrainRoots, string terrainPrefabPath)
        {
            Transform matchingTerrainRoot = terrainRoots.FirstOrDefault(x => PathsEqual(GetPrefabSourcePath(x.gameObject), terrainPrefabPath));
            if (matchingTerrainRoot != null)
            {
                return TransformData.FromTransform(matchingTerrainRoot);
            }

            if (TryReadTemplateTerrainTransform(out TransformData templateTransform))
            {
                return templateTransform;
            }

            if (terrainRoots.Length > 0)
            {
                return TransformData.FromTransform(terrainRoots[0]);
            }

            return TransformData.Identity;
        }

        private static bool TryReadTemplateTerrainTransform(out TransformData transformData)
        {
            transformData = TransformData.Identity;
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPrefabTemplatePath);
            if (prefabRoot == null)
            {
                return false;
            }

            GameObject loadedRoot = PrefabUtility.LoadPrefabContents(LevelPrefabTemplatePath);
            try
            {
                Transform terrainRoot = GetTerrainRoots(loadedRoot.transform).FirstOrDefault();
                if (terrainRoot == null)
                {
                    return false;
                }

                transformData = TransformData.FromTransform(terrainRoot);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(loadedRoot);
            }
        }

        private FlowNavigationGridImportResult GenerateFlowNavigationGrid(ImportPlan plan, string terrainPrefabPath)
        {
            if (plan == null || plan.width <= 0 || plan.height <= 0 || plan.cellSize <= 0.0001f)
            {
                return FlowNavigationGridImportResult.Skipped("Import plan has no valid grid size.");
            }

            if (string.IsNullOrEmpty(terrainPrefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(terrainPrefabPath) == null)
            {
                return FlowNavigationGridImportResult.Skipped("Terrain prefab has not been generated.");
            }

            string assetPath = GetDefaultFlowNavigationGridAssetPath();
            TransformData terrainTransform = ResolveFlowNavigationTerrainTransform(terrainPrefabPath);
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log(
                $"[FlowNavigationGridImport] stage=start terrain={terrainPrefabPath} asset={assetPath} " +
                $"plan={plan.width}x{plan.height} cellSize={plan.cellSize:F4} " +
                $"terrainPosition={terrainTransform.localPosition} terrainRotation={terrainTransform.localRotation.eulerAngles} terrainScale={terrainTransform.localScale}");
            FlowNavigationGridPrefabBaker.MovementTypeBakeRequest[] requests = BuildDefaultFlowNavigationGridBakeRequests(assetPath);
            Debug.Log(
                $"[FlowNavigationGridImport] stage=requests-ready elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"requests={FormatFlowNavigationBakeRequests(requests)}");
            float navigationCellSize = ResolveFlowNavigationCellSize(requests);
            FlowNavigationGridPrefabBaker.Result[] results = FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab(
                terrainPrefabPath,
                requests,
                navigationCellSize,
                Array.Empty<FlowNavigationGridPrefabBaker.StaticObstacleBakeInstance>(),
                new FlowNavigationGridPrefabBaker.TerrainBakeTransform(
                    terrainTransform.localPosition,
                    terrainTransform.localRotation,
                    terrainTransform.localScale));
            Debug.Log(
                $"[FlowNavigationGridImport] stage=complete elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"results={FormatFlowNavigationBakeResults(results)}");

            return FlowNavigationGridImportResult.From(results);
        }

        private FlowNavigationGridImportResult LoadExistingFlowNavigationGrids()
        {
            string primaryAssetPath = GetDefaultFlowNavigationGridAssetPath();
            if (string.IsNullOrWhiteSpace(primaryAssetPath))
                return FlowNavigationGridImportResult.Skipped("Flow navigation grid asset path is invalid.");

            FlowNavigationGridPrefabBaker.MovementTypeBakeRequest[] requests = BuildDefaultFlowNavigationGridBakeRequests(primaryAssetPath);
            var assets = new FlowNavigationGridAsset[requests.Length];
            for (int i = 0; i < requests.Length; i++)
            {
                assets[i] = AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>(requests[i].AssetPath);
                if (assets[i] == null)
                {
                    return FlowNavigationGridImportResult.Skipped(
                        $"Existing flow navigation grid is missing: {requests[i].AssetPath}. Generate build layers once before importing entity points only.");
                }
            }

            return FlowNavigationGridImportResult.Existing(requests, assets);
        }

        private static string FormatFlowNavigationBakeRequests(IReadOnlyList<FlowNavigationGridPrefabBaker.MovementTypeBakeRequest> requests)
        {
            if (requests == null)
                return "<null>";

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < requests.Count; i++)
            {
                if (i > 0)
                    builder.Append("; ");
                FlowNavigationGridPrefabBaker.MovementTypeBakeRequest request = requests[i];
                builder.Append("agentType=");
                builder.Append(request.AgentTypeId);
                builder.Append(",radius=");
                builder.Append(request.HardClearanceRadius.ToString("F4", CultureInfo.InvariantCulture));
                builder.Append(",asset=");
                builder.Append(request.AssetPath);
            }

            return builder.ToString();
        }

        private static string FormatFlowNavigationBakeResults(IReadOnlyList<FlowNavigationGridPrefabBaker.Result> results)
        {
            if (results == null)
                return "<null>";

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0)
                    builder.Append("; ");
                FlowNavigationGridPrefabBaker.Result result = results[i];
                builder.Append("agentType=");
                builder.Append(result.AgentTypeId);
                builder.Append(",size=");
                builder.Append(result.Width);
                builder.Append('x');
                builder.Append(result.Height);
                builder.Append(",walkable=");
                builder.Append(result.WalkableCount);
                builder.Append(",blocked=");
                builder.Append(result.BlockedCount);
                builder.Append(",groundColliders=");
                builder.Append(result.GroundColliderCount);
                builder.Append(",obstacleColliders=");
                builder.Append(result.ObstacleColliderCount);
            }

            return builder.ToString();
        }

        private static float ResolveFlowNavigationCellSize(IReadOnlyList<FlowNavigationGridPrefabBaker.MovementTypeBakeRequest> requests)
        {
            if (requests == null || requests.Count == 0)
                throw new InvalidOperationException("ResolveFlowNavigationCellSize failed: movement type requests are missing.");

            float smallestRadius = float.PositiveInfinity;
            for (int i = 0; i < requests.Count; i++)
            {
                float radius = requests[i].HardClearanceRadius;
                if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
                    throw new InvalidOperationException($"ResolveFlowNavigationCellSize failed: invalid radius={radius:F4} agentType={requests[i].AgentTypeId}.");
                if (radius < smallestRadius)
                    smallestRadius = radius;
            }

            return Mathf.Max(0.05f, smallestRadius * 0.5f);
        }

        private static FlowNavigationGridPrefabBaker.MovementTypeBakeRequest[] BuildDefaultFlowNavigationGridBakeRequests(string primaryAssetPath)
        {
            if (string.IsNullOrWhiteSpace(primaryAssetPath))
                throw new InvalidOperationException("BuildDefaultFlowNavigationGridBakeRequests failed: primaryAssetPath is empty.");

            string folder = Path.GetDirectoryName(primaryAssetPath)?.Replace("\\", "/");
            string name = Path.GetFileNameWithoutExtension(primaryAssetPath);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"BuildDefaultFlowNavigationGridBakeRequests failed: invalid primaryAssetPath={primaryAssetPath}.");
            const string mediumSuffix = "_Medium";
            if (!name.EndsWith(mediumSuffix, StringComparison.Ordinal))
                throw new InvalidOperationException($"BuildDefaultFlowNavigationGridBakeRequests failed: medium asset path must end with '{mediumSuffix}'. path={primaryAssetPath}.");
            string familyName = name.Substring(0, name.Length - mediumSuffix.Length);

            return new[]
            {
                new FlowNavigationGridPrefabBaker.MovementTypeBakeRequest(AgentTypeHelper.MediumMovementTypeId, primaryAssetPath, ResolveDefaultMovementTypeRadius("MediumUnitCollisionRadius")),
                new FlowNavigationGridPrefabBaker.MovementTypeBakeRequest(AgentTypeHelper.SmallMovementTypeId, $"{folder}/{familyName}_Small.asset", ResolveDefaultMovementTypeRadius("SmallUnitCollisionRadius")),
                new FlowNavigationGridPrefabBaker.MovementTypeBakeRequest(AgentTypeHelper.LargeMovementTypeId, $"{folder}/{familyName}_Large.asset", ResolveDefaultMovementTypeRadius("LargeUnitCollisionRadius"))
            };
        }

        private static float ResolveDefaultMovementTypeRadius(string configKey)
        {
            float tableRadius = ResolveGameConfigFloat(configKey);
            float conversionRate = ResolveGameConfigFloat(DistanceUnitConverter.DistanceConversionRateKey);
            return tableRadius * conversionRate;
        }

        private static float ResolveGameConfigFloat(string configKey)
        {
            if (string.IsNullOrWhiteSpace(configKey))
                throw new InvalidOperationException("ResolveGameConfigFloat failed: configKey is empty.");
            if (!File.Exists(GameConfigPath))
                throw new InvalidOperationException($"ResolveGameConfigFloat failed: config file not found at {GameConfigPath}.");

            foreach (string line in File.ReadLines(GameConfigPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split('\t');
                bool keyMatched = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.Equals(parts[i].Trim(), configKey, StringComparison.Ordinal))
                    {
                        keyMatched = true;
                        break;
                    }
                }

                if (!keyMatched)
                    continue;

                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    string value = parts[i].Trim();
                    if (value.Length == 0)
                        continue;
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                        return parsed;
                }

                throw new InvalidOperationException($"ResolveGameConfigFloat failed: key {configKey} has no parseable float value in {GameConfigPath}.");
            }

            throw new InvalidOperationException($"ResolveGameConfigFloat failed: key {configKey} was not found in {GameConfigPath}.");
        }

        private TransformData ResolveFlowNavigationTerrainTransform(string terrainPrefabPath)
        {
            TransformData transformData = TransformData.Identity;
            if (levelPrefabTarget != null)
            {
                string targetPath = AssetDatabase.GetAssetPath(levelPrefabTarget);
                bool isPrefabAsset = !string.IsNullOrEmpty(targetPath) && PrefabUtility.GetPrefabAssetType(levelPrefabTarget) != PrefabAssetType.NotAPrefab;
                if (isPrefabAsset)
                {
                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(targetPath);
                    try
                    {
                        transformData = ResolveTerrainInstanceTransform(GetTerrainRoots(prefabRoot.transform).ToArray(), terrainPrefabPath);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }
            else if (TryReadTemplateTerrainTransform(out TransformData templateTransform))
            {
                transformData = templateTransform;
            }

            return transformData;
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

                string sourcePath = GetPrefabSourcePath(child.gameObject);
                if (sourcePath.StartsWith(TerrainPrefabFolderPath + "/", StringComparison.OrdinalIgnoreCase) &&
                    sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    yield return child;
                }
            }
        }

        private static string GetPrefabSourcePath(GameObject instance)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            return source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
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
            var teleportationIds = new HashSet<int>();
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

                if (pointData.pointType == EntityPresetPointType.Teleportation && !teleportationIds.Add(pointData.teleportationId))
                {
                    throw new InvalidOperationException($"LDtk level contains duplicate Teleportation ID {pointData.teleportationId}.");
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
            int teleportationId = 0;
            Fix64 defendSpawnWeight = Fix64.Zero;
            int destinationId = 0;
            float destinationRadius = 0f;
            bool isGameEndConditionBuilding = false;
            bool useCustomCoinReserves = false;
            int customCoinReserves = 0;

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
            else if (TryGetBuildingEntityGridSize(entityType, out int buildingEntityGridSize))
            {
                int expectedPixelSize = buildingEntityGridSize * gridSize;
                if (entity.width != expectedPixelSize || entity.height != expectedPixelSize)
                {
                    throw new InvalidOperationException(
                        $"LDtk {entityType} must be {expectedPixelSize}x{expectedPixelSize} pixels at grid size {gridSize}. " +
                        $"actual={entity.width}x{entity.height}.");
                }
                pointType = EntityPresetPointType.Building;
                identifier = NormalizeBuildingIdentifier(GetFieldString(entity, "Identifier"));
                if (string.IsNullOrWhiteSpace(identifier))
                    throw new InvalidOperationException($"LDtk {entityType} requires a non-empty Identifier field.");
                isGameEndConditionBuilding = GetFieldBool(entity, "IsGameEndCondition", false);
                useCustomCoinReserves = TryGetFieldInt(entity, "CoinReserves", out customCoinReserves);
                if (useCustomCoinReserves)
                {
                    customCoinReserves = Mathf.Max(0, customCoinReserves);
                }
            }
            else if (string.Equals(entityType, "DefendSpawn", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("LDtk DefendSpawn is no longer supported. Use one Teleportation entity per stronghold with ID and Weight fields.");
            }
            else if (string.Equals(entityType, "Destination", StringComparison.OrdinalIgnoreCase))
            {
                pointType = EntityPresetPointType.Destination;
                float authoredId = GetFieldFloat(entity, "ID", float.NaN);
                if (!float.IsFinite(authoredId) || authoredId < 0f || !Mathf.Approximately(authoredId, Mathf.Round(authoredId)))
                    throw new InvalidOperationException($"Destination ID must be a non-negative integer, actual={authoredId}.");

                destinationId = Mathf.RoundToInt(authoredId);
                destinationRadius = GetFieldFloat(entity, "Radius", float.NaN);
                if (!float.IsFinite(destinationRadius) || destinationRadius <= 0f)
                    throw new InvalidOperationException($"Destination {destinationId} radius must be positive, actual={destinationRadius}.");

                identifier = destinationId.ToString(CultureInfo.InvariantCulture);
            }
            else if (string.Equals(entityType, "Teleportation", StringComparison.OrdinalIgnoreCase))
            {
                pointType = EntityPresetPointType.Teleportation;
                if (!TryGetStrictFieldInt(entity, "ID", out teleportationId) || teleportationId < 0)
                    throw new InvalidOperationException($"Teleportation ID must be a non-negative integer, actual={GetFieldValue(entity, "ID")}.");
                if (!TryGetNonNegativeFixedField(entity, "Weight", out defendSpawnWeight))
                    throw new InvalidOperationException($"Teleportation {teleportationId} Weight must be a finite non-negative number, actual={GetFieldValue(entity, "Weight")}.");
                identifier = teleportationId.ToString(CultureInfo.InvariantCulture);
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
                teleportationId = teleportationId,
                defendSpawnWeight = defendSpawnWeight,
                destinationId = destinationId,
                destinationRadius = destinationRadius,
                isGameEndConditionBuilding = isGameEndConditionBuilding,
                useCustomCoinReserves = useCustomCoinReserves,
                customCoinReserves = customCoinReserves,
                localPosition = pointType == EntityPresetPointType.Building
                    ? ConvertLdtkBuildingPivotToLocalPosition(entity, gridSize, pixelHeight, cellSize)
                    : ConvertLdtkPivotToLocalPosition(
                        entity.px[0],
                        entity.px[1],
                        gridSize,
                        pixelHeight,
                        cellSize)
            };
            return true;
        }

        private static bool TryGetBuildingEntityGridSize(string entityType, out int footprintGridSize)
        {
            if (string.Equals(entityType, "Building22", StringComparison.OrdinalIgnoreCase))
            {
                footprintGridSize = 2;
                return true;
            }
            if (string.Equals(entityType, "Building33", StringComparison.OrdinalIgnoreCase))
            {
                footprintGridSize = 3;
                return true;
            }
            if (string.Equals(entityType, "Building44", StringComparison.OrdinalIgnoreCase))
            {
                footprintGridSize = 4;
                return true;
            }

            footprintGridSize = 0;
            return false;
        }

        private static Vector3 ConvertLdtkBuildingPivotToLocalPosition(
            LdtkEntityInstance entity,
            int gridSize,
            int pixelHeight,
            float cellSize)
        {
            if (entity.__pivot == null || entity.__pivot.Length < 2)
                throw new InvalidOperationException($"LDtk {entity.__identifier} is missing its pivot.");
            if (!Mathf.Approximately(entity.__pivot[0], 0.5f) || !Mathf.Approximately(entity.__pivot[1], 0.5f))
            {
                throw new InvalidOperationException(
                    $"LDtk {entity.__identifier} pivot must be centered at (0.5, 0.5), " +
                    $"actual=({entity.__pivot[0]}, {entity.__pivot[1]}).");
            }

            return ConvertLdtkPivotToLocalPosition(
                entity.px[0],
                entity.px[1],
                gridSize,
                pixelHeight,
                cellSize);
        }

        private static Vector3 ConvertLdtkPivotToLocalPosition(
            int pixelX,
            int pixelY,
            int gridSize,
            int pixelHeight,
            float cellSize)
        {
            float halfCellSize = cellSize * 0.5f;
            return new Vector3(
                pixelX / (float)gridSize * cellSize - halfCellSize,
                0f,
                (pixelHeight - pixelY) / (float)gridSize * cellSize - halfCellSize);
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

        private static bool TryGetFieldInt(LdtkEntityInstance entity, string fieldName, out int value)
        {
            value = default;
            JToken token = GetFieldValue(entity, fieldName);
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<int>();
                return true;
            }

            return int.TryParse(token.ToString(Formatting.None), out value);
        }

        private static bool TryGetStrictFieldInt(LdtkEntityInstance entity, string fieldName, out int value)
        {
            value = default;
            JToken token = GetFieldValue(entity, fieldName);
            if (token == null || token.Type != JTokenType.Integer)
            {
                return false;
            }

            value = token.Value<int>();
            return true;
        }

        private static bool TryGetNonNegativeFixedField(LdtkEntityInstance entity, string fieldName, out Fix64 value)
        {
            value = Fix64.Zero;
            JToken token = GetFieldValue(entity, fieldName);
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                return false;
            }

            try
            {
                decimal decimalValue = token.Value<decimal>();
                value = (Fix64)decimalValue;
                return decimalValue >= decimal.Zero && value >= Fix64.Zero;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static float GetFieldFloat(LdtkEntityInstance entity, string fieldName, float defaultValue)
        {
            JToken value = GetFieldValue(entity, fieldName);
            if (value == null || value.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return value.Type == JTokenType.Integer || value.Type == JTokenType.Float
                ? value.Value<float>()
                : float.TryParse(value.ToString(Formatting.None), out float parsed) ? parsed : defaultValue;
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

        private static void MarkImportedAssetsDirty(
            Configuration configuration,
            IEnumerable<BlueprintLayer> platformLayers,
            BlueprintLayer slopeLayer,
            BlueprintLayer waterLayer,
            List<BlueprintLayer> strongholdLayers)
        {
            EditorUtility.SetDirty(configuration);
            foreach (BlueprintLayer platformLayer in platformLayers)
            {
                platformLayer.OnBeforeSerialize();
                EditorUtility.SetDirty(platformLayer);
            }

            slopeLayer.OnBeforeSerialize();
            waterLayer.OnBeforeSerialize();
            EditorUtility.SetDirty(slopeLayer);
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
            foreach (PlatformImport platform in plan.platforms)
            {
                builder.AppendLine($"Plane_H{platform.height} cells: {platform.cells.Count}");
            }

            builder.AppendLine($"Slope cells: {plan.slopeCells.Count}");
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

            builder.AppendLine($"Flow navigation grid: {(result.flowNavigationGridResult.skipped ? "Skipped" : result.flowNavigationGridResult.reused ? "Existing" : "Generated")}");
            if (result.flowNavigationGridResult.skipped)
            {
                builder.AppendLine($"Flow navigation skipped reason: {result.flowNavigationGridResult.skippedReason}");
            }
            else
            {
                builder.AppendLine($"Flow navigation asset: {result.flowNavigationGridResult.assetPath}");
                builder.AppendLine($"Flow navigation movement type grids: {result.flowNavigationGridResult.assetCount}");
                if (!string.IsNullOrEmpty(result.flowNavigationGridResult.assetPathsSummary))
                {
                    builder.AppendLine($"Flow navigation assets: {result.flowNavigationGridResult.assetPathsSummary}");
                }

                builder.AppendLine($"Flow navigation size: {result.flowNavigationGridResult.width} x {result.flowNavigationGridResult.height}");
                builder.AppendLine($"Flow navigation walkable: {result.flowNavigationGridResult.walkableCount}");
                builder.AppendLine($"Flow navigation blocked: {result.flowNavigationGridResult.blockedCount}");
                builder.AppendLine($"Flow navigation ground colliders: {result.flowNavigationGridResult.groundColliderCount}");
                builder.AppendLine($"Flow navigation obstacle colliders: {result.flowNavigationGridResult.obstacleColliderCount}");
            }

            builder.AppendLine($"Entity import: {(result.skipped ? "Skipped" : "Completed")}");
            if (result.skipped)
            {
                builder.AppendLine($"Entity skipped reason: {result.skippedReason}");
                return builder.ToString();
            }

            builder.AppendLine($"Entity prefab: {result.targetPath}");
            builder.AppendLine($"Entity flow source attached: {result.flowNavigationSourceAttached}");
            builder.AppendLine($"Entity removed old terrains: {result.removedTerrainCount}");
            builder.AppendLine($"Entity terrain instance: {result.terrainPrefabPath}");
            builder.AppendLine($"Entity removed old points: {result.removedCount}");
            builder.AppendLine($"Entity heroes: {result.heroCount}");
            builder.AppendLine($"Entity buildings: {result.buildingCount}");
            builder.AppendLine($"Entity units: {result.unitCount}");
            builder.AppendLine($"Entity defend spawns: {result.defendSpawnCount}");
            builder.AppendLine($"Entity destinations: {result.destinationCount}");
            builder.AppendLine($"Entity teleportations: {result.teleportationCount}");
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
            public float[] __pivot;
            public int width;
            public int height;
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
            public List<PlatformImport> platforms;
            public HashSet<Vector2> slopeCells;
            public HashSet<Vector2> waterCells;
            public Dictionary<Vector2, int> walkableWallCells;
            public HashSet<Vector2> previewWallCells;
            public HashSet<Vector2> presetWallCells;
            public Dictionary<int, List<StrongholdComponent>> strongholdComponentsByFaction;
            public List<EntityPresetPointData> entityPoints;
        }

        private sealed class PlatformLdtkLayer
        {
            public int height;
            public LdtkLayerInstance layer;
        }

        private sealed class PlatformImport
        {
            public int height;
            public HashSet<Vector2> cells;
            public HashSet<Vector2> dualControlCells;
        }

        private struct EntityPresetPointData
        {
            public EntityPresetPointType pointType;
            public string identifier;
            public int unitSpawnCount;
            public int teleportationId;
            public Fix64 defendSpawnWeight;
            public int destinationId;
            public float destinationRadius;
            public bool isGameEndConditionBuilding;
            public bool useCustomCoinReserves;
            public int customCoinReserves;
            public Vector3 localPosition;
        }

        private struct TransformData
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;

            public static TransformData Identity => new TransformData
            {
                localPosition = Vector3.zero,
                localRotation = Quaternion.identity,
                localScale = Vector3.one
            };

            public static TransformData FromTransform(Transform transform)
            {
                return new TransformData
                {
                    localPosition = transform.localPosition,
                    localRotation = transform.localRotation,
                    localScale = transform.localScale
                };
            }
        }

        private struct FlowNavigationGridImportResult
        {
            public bool skipped;
            public bool reused;
            public string skippedReason;
            public FlowNavigationGridAsset asset;
            public FlowNavigationGridAsset[] assets;
            public string assetPath;
            public string assetPathsSummary;
            public int assetCount;
            public int width;
            public int height;
            public int walkableCount;
            public int blockedCount;
            public int groundColliderCount;
            public int obstacleColliderCount;

            public static FlowNavigationGridImportResult Skipped(string reason)
            {
                return new FlowNavigationGridImportResult
                {
                    skipped = true,
                    skippedReason = reason
                };
            }

            public static FlowNavigationGridImportResult From(FlowNavigationGridPrefabBaker.Result[] results)
            {
                if (results == null || results.Length == 0)
                    return Skipped("No flow navigation grids were generated.");

                FlowNavigationGridPrefabBaker.Result primary = results[0];
                FlowNavigationGridAsset[] assets = new FlowNavigationGridAsset[results.Length];
                string[] paths = new string[results.Length];
                int walkableCount = 0;
                int blockedCount = 0;
                for (int i = 0; i < results.Length; i++)
                {
                    assets[i] = results[i].Asset;
                    paths[i] = $"{results[i].AgentTypeId}:{results[i].AssetPath}";
                    walkableCount += results[i].WalkableCount;
                    blockedCount += results[i].BlockedCount;
                }

                return new FlowNavigationGridImportResult
                {
                    asset = primary.Asset,
                    assets = assets,
                    assetPath = primary.AssetPath,
                    assetPathsSummary = string.Join(", ", paths),
                    assetCount = assets.Length,
                    width = primary.Width,
                    height = primary.Height,
                    walkableCount = walkableCount,
                    blockedCount = blockedCount,
                    groundColliderCount = primary.GroundColliderCount,
                    obstacleColliderCount = primary.ObstacleColliderCount
                };
            }

            public static FlowNavigationGridImportResult Existing(
                IReadOnlyList<FlowNavigationGridPrefabBaker.MovementTypeBakeRequest> requests,
                FlowNavigationGridAsset[] assets)
            {
                if (requests == null || assets == null || requests.Count == 0 || requests.Count != assets.Length)
                    return Skipped("Existing flow navigation grid set is invalid.");

                int walkableCount = 0;
                int blockedCount = 0;
                string[] paths = new string[assets.Length];
                for (int i = 0; i < assets.Length; i++)
                {
                    FlowNavigationGridAsset asset = assets[i];
                    if (asset == null)
                        return Skipped($"Existing flow navigation grid is null at index {i}.");

                    bool[] walkable = asset.GetWalkableMaskRuntimeReadOnlyReference();
                    for (int cellIndex = 0; cellIndex < walkable.Length; cellIndex++)
                    {
                        if (walkable[cellIndex])
                            walkableCount++;
                        else
                            blockedCount++;
                    }
                    paths[i] = $"{requests[i].AgentTypeId}:{requests[i].AssetPath}";
                }

                return new FlowNavigationGridImportResult
                {
                    reused = true,
                    asset = assets[0],
                    assets = assets,
                    assetPath = requests[0].AssetPath,
                    assetPathsSummary = string.Join(", ", paths),
                    assetCount = assets.Length,
                    width = assets[0].Width,
                    height = assets[0].Height,
                    walkableCount = walkableCount,
                    blockedCount = blockedCount,
                    groundColliderCount = -1,
                    obstacleColliderCount = -1
                };
            }
        }

        private struct EntityImportResult
        {
            public bool skipped;
            public string skippedReason;
            public string targetPath;
            public string terrainPrefabPath;
            public TerrainPrefabResult terrainResult;
            public FlowNavigationGridImportResult flowNavigationGridResult;
            public bool flowNavigationSourceAttached;
            public int removedTerrainCount;
            public int removedCount;
            public int heroCount;
            public int buildingCount;
            public int unitCount;
            public int defendSpawnCount;
            public int destinationCount;
            public int teleportationCount;

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

                    case EntityPresetPointType.DefendSpawn:
                        defendSpawnCount++;
                        break;

                    case EntityPresetPointType.Destination:
                        destinationCount++;
                        break;

                    case EntityPresetPointType.Teleportation:
                        teleportationCount++;
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
