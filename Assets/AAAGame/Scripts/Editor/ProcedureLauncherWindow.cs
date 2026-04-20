using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameFramework.Procedure;

[InitializeOnLoad]
public class ProcedureLauncherWindow : EditorWindow
{
    // 运行时动态发现的 Procedure 列表
    private static string[] s_ProcedureNames = { "CharacterTestProcedure" };
    private const string SceneRootFolder = "Assets/AAAGame/Scene";

    private const string PrefKey_Selected = "Procedure_Selected";
    private const string PrefKey_SceneName = "Procedure_SceneName";
    private const string PrefKey_LevelIdentifier = "Procedure_LevelIdentifier";
    private const string PrefKey_FriendlyCount = "Test_FriendlyCount";
    private const string PrefKey_EnemyCount = "Test_EnemyCount";
    private const string PrefKey_FriendlySpacing = "Test_FriendlySpacing";
    private const string PrefKey_EnemySpacing = "Test_EnemySpacing";
    private const string PrefKey_PlayerX = "Test_PlayerX";
    private const string PrefKey_PlayerY = "Test_PlayerY";
    private const string PrefKey_PlayerZ = "Test_PlayerZ";
    private const string PrefKey_EnemyX = "Test_EnemyX";
    private const string PrefKey_EnemyY = "Test_EnemyY";
    private const string PrefKey_EnemyZ = "Test_EnemyZ";

    // 运行时动态发现的场景列表（仅来自 Assets/AAAGame/Scene）
    private static string[] s_SceneNames = { "Game" };

    // 运行时动态发现的关卡标识（来自 LevelTable）
    private static string[] s_LevelIdentifiers = { "Lv_2" };

    private int _selectedIndex;
    private int _sceneIndex;
    private int _levelIndex;

    static ProcedureLauncherWindow()
    {
        EditorApplication.delayCall += LoadToStatic;
    }

    private static void RefreshDynamicOptions()
    {
        s_ProcedureNames = TypeCache.GetTypesDerivedFrom<ProcedureBase>()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericType)
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        var appConfigs = AppConfigs.GetInstanceEditor();
        if (appConfigs != null && appConfigs.Procedures != null && appConfigs.Procedures.Length > 0)
        {
            s_ProcedureNames = s_ProcedureNames
                .Concat(appConfigs.Procedures.Where(name => !string.IsNullOrEmpty(name)))
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
        }

        if (s_ProcedureNames.Length == 0)
        {
            s_ProcedureNames = new[] { "CharacterTestProcedure" };
        }

        var projectSceneNames = AssetDatabase.FindAssets("t:Scene", new[] { SceneRootFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name));

        s_SceneNames = projectSceneNames
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        if (s_SceneNames.Length == 0)
        {
            s_SceneNames = new[] { "Game" };
        }

        s_LevelIdentifiers = LoadLevelIdentifiers();
        if (s_LevelIdentifiers.Length == 0)
        {
            s_LevelIdentifiers = new[] { "Lv_2" };
        }
    }

    private static string[] LoadLevelIdentifiers()
    {
        string levelTablePath = UtilityBuiltin.AssetsPath.GetDataTablePath("LevelTable", false);
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(levelTablePath) == null)
        {
            string[] guids = AssetDatabase.FindAssets("LevelTable t:TextAsset", new[] { "Assets/AAAGame/DataTable" });
            levelTablePath = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => !string.IsNullOrEmpty(path) && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrEmpty(levelTablePath))
        {
            return Array.Empty<string>();
        }

        var levelTableAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(levelTablePath);
        if (levelTableAsset == null || string.IsNullOrWhiteSpace(levelTableAsset.text))
        {
            return Array.Empty<string>();
        }

        var levelIds = new List<string>();
        var dedup = new HashSet<string>(StringComparer.Ordinal);

        using (var reader = new StringReader(levelTableAsset.text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                {
                    continue;
                }

                var row = new LevelTable();
                if (!row.ParseDataRow(line, null))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(row.Identifier) && dedup.Add(row.Identifier))
                {
                    levelIds.Add(row.Identifier);
                }
            }
        }

        return levelIds.ToArray();
    }

    /// <summary>
    /// 编辑器启动 / 脚本重编译后，从 EditorPrefs 恢复设置到静态字段
    /// </summary>
    static void LoadToStatic()
    {
        RefreshDynamicOptions();

        // 恢复选中的 Procedure，校验有效性
        string saved = EditorPrefs.GetString(PrefKey_Selected, "CharacterTestProcedure");
        if (Array.IndexOf(s_ProcedureNames, saved) < 0)
        {
            Debug.LogWarning($"[ProcedureLauncher] EditorPrefs 中的 Procedure '{saved}' 无效，回退到 CharacterTestProcedure");
            saved = s_ProcedureNames[0];
            EditorPrefs.SetString(PrefKey_Selected, saved);
        }
        ChangeSceneProcedure.SelectedProcedureForGame = saved;

        // 恢复选中的场景，校验有效性
        string savedScene = EditorPrefs.GetString(PrefKey_SceneName, "Game");
        if (Array.IndexOf(s_SceneNames, savedScene) < 0)
        {
            Debug.LogWarning($"[ProcedureLauncher] EditorPrefs 中的场景 '{savedScene}' 无效，回退到 Game");
            savedScene = s_SceneNames[0];
            EditorPrefs.SetString(PrefKey_SceneName, savedScene);
        }
        ChangeSceneProcedure.SelectedSceneForGame = savedScene;

        // 恢复选中的关卡标识，校验有效性
        string savedLevel = EditorPrefs.GetString(PrefKey_LevelIdentifier, "Lv_2");
        if (Array.IndexOf(s_LevelIdentifiers, savedLevel) < 0)
        {
            Debug.LogWarning($"[ProcedureLauncher] EditorPrefs 中的关卡标识 '{savedLevel}' 无效，回退到 {s_LevelIdentifiers[0]}");
            savedLevel = s_LevelIdentifiers[0];
            EditorPrefs.SetString(PrefKey_LevelIdentifier, savedLevel);
        }
        ChangeSceneProcedure.SelectedLevelIdentifier = savedLevel;

        // 如果选中的是 CharacterTestProcedure，加载其专属设置
        if (saved == "CharacterTestProcedure")
        {
            LoadCharacterTestSettings();
        }
    }

    static void LoadCharacterTestSettings()
    {
        CharacterTestProcedure.FriendlyCount = EditorPrefs.GetInt(PrefKey_FriendlyCount, 5);
        CharacterTestProcedure.EnemyCount = EditorPrefs.GetInt(PrefKey_EnemyCount, 1);
        CharacterTestProcedure.FriendlySpacing = EditorPrefs.GetFloat(PrefKey_FriendlySpacing, 3f);
        CharacterTestProcedure.EnemySpacing = EditorPrefs.GetFloat(PrefKey_EnemySpacing, 3f);
        CharacterTestProcedure.PlayerSpawn = new Vector3(
            EditorPrefs.GetFloat(PrefKey_PlayerX, 0),
            EditorPrefs.GetFloat(PrefKey_PlayerY, 1),
            EditorPrefs.GetFloat(PrefKey_PlayerZ, 0));
        CharacterTestProcedure.EnemySpawnCenter = new Vector3(
            EditorPrefs.GetFloat(PrefKey_EnemyX, 20),
            EditorPrefs.GetFloat(PrefKey_EnemyY, 1),
            EditorPrefs.GetFloat(PrefKey_EnemyZ, 0));
    }

    [MenuItem("Tools/Procedure 启动配置")]
    static void Open() => GetWindow<ProcedureLauncherWindow>("Procedure 启动配置");

    private void OnEnable()
    {
        RefreshDynamicOptions();
        EditorBuildSettings.sceneListChanged += OnSceneListChanged;
        EditorApplication.projectChanged += OnProjectChanged;

        // 从 EditorPrefs 恢复选中索引
        string saved = EditorPrefs.GetString(PrefKey_Selected, "CharacterTestProcedure");
        _selectedIndex = Array.IndexOf(s_ProcedureNames, saved);
        if (_selectedIndex < 0) _selectedIndex = 0;

        // 恢复场景选择索引
        string savedScene = EditorPrefs.GetString(PrefKey_SceneName, "Game");
        _sceneIndex = Array.IndexOf(s_SceneNames, savedScene);
        if (_sceneIndex < 0) _sceneIndex = 0;

        // 恢复关卡标识索引
        string savedLevel = EditorPrefs.GetString(PrefKey_LevelIdentifier, "Lv_2");
        _levelIndex = Array.IndexOf(s_LevelIdentifiers, savedLevel);
        if (_levelIndex < 0) _levelIndex = 0;

        // 同步加载 CharacterTest 设置
        LoadCharacterTestSettings();
    }

    private void OnDisable()
    {
        EditorBuildSettings.sceneListChanged -= OnSceneListChanged;
        EditorApplication.projectChanged -= OnProjectChanged;
    }

    private void OnSceneListChanged()
    {
        RefreshDynamicOptions();
        ClampIndexes();
        Repaint();
    }

    private void OnProjectChanged()
    {
        RefreshDynamicOptions();
        ClampIndexes();
        Repaint();
    }

    private void ClampIndexes()
    {
        _selectedIndex = Mathf.Clamp(_selectedIndex, 0, s_ProcedureNames.Length - 1);
        _sceneIndex = Mathf.Clamp(_sceneIndex, 0, s_SceneNames.Length - 1);
        _levelIndex = Mathf.Clamp(_levelIndex, 0, s_LevelIdentifiers.Length - 1);
    }

    private void OnGUI()
    {
        RefreshDynamicOptions();
        ClampIndexes();

        #region Procedure 选择

        GUILayout.Label("启动 Procedure", EditorStyles.boldLabel);

        if (GUILayout.Button("刷新 Procedure / Scene 列表"))
        {
            RefreshDynamicOptions();
            ClampIndexes();
        }

        if (s_ProcedureNames.Length == 0)
        {
            EditorGUILayout.HelpBox("未发现可用 Procedure", MessageType.Warning);
            return;
        }

        if (s_SceneNames.Length == 0)
        {
            EditorGUILayout.HelpBox("未发现可用 Scene，请先把场景加入 Build Settings。", MessageType.Warning);
            return;
        }

        EditorGUI.BeginChangeCheck();
        _selectedIndex = EditorGUILayout.Popup("选择 Procedure", _selectedIndex, s_ProcedureNames);
        if (EditorGUI.EndChangeCheck())
        {
            string selected = s_ProcedureNames[_selectedIndex];
            EditorPrefs.SetString(PrefKey_Selected, selected);
            ChangeSceneProcedure.SelectedProcedureForGame = selected;

            if (selected == "CharacterTestProcedure")
            {
                LoadCharacterTestSettings();
            }
        }

        #endregion

        GUILayout.Space(10);

        #region 场景选择

        GUILayout.Label("目标场景", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _sceneIndex = EditorGUILayout.Popup("加载场景", _sceneIndex, s_SceneNames);
        if (EditorGUI.EndChangeCheck())
        {
            string selectedScene = s_SceneNames[_sceneIndex];
            EditorPrefs.SetString(PrefKey_SceneName, selectedScene);
            ChangeSceneProcedure.SelectedSceneForGame = selectedScene;
        }

        #endregion

        GUILayout.Space(10);

        #region 关卡选择

        GUILayout.Label("初始关卡（RuntimeProcedure）", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _levelIndex = EditorGUILayout.Popup("关卡标识", _levelIndex, s_LevelIdentifiers);
        if (EditorGUI.EndChangeCheck())
        {
            string selectedLevel = s_LevelIdentifiers[_levelIndex];
            EditorPrefs.SetString(PrefKey_LevelIdentifier, selectedLevel);
            ChangeSceneProcedure.SelectedLevelIdentifier = selectedLevel;
        }

        #endregion

        GUILayout.Space(10);

        #region 根据选中 Procedure 显示对应面板

        if (s_ProcedureNames[_selectedIndex] == "CharacterTestProcedure")
        {
            DrawCharacterTestPanel();
        }
        else
        {
            EditorGUILayout.HelpBox("暂无可配置项", MessageType.Info);
        }

        #endregion

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("改完后重新进 Play 生效", MessageType.Info);
    }

    #region CharacterTestProcedure 设置面板

    private void DrawCharacterTestPanel()
    {
        EditorGUI.BeginChangeCheck();

        GUILayout.Label("单位数量", EditorStyles.boldLabel);
        CharacterTestProcedure.FriendlyCount =
            EditorGUILayout.IntSlider("友方数量", CharacterTestProcedure.FriendlyCount, 0, 30);
        CharacterTestProcedure.EnemyCount =
            EditorGUILayout.IntSlider("敌方数量", CharacterTestProcedure.EnemyCount, 0, 30);

        GUILayout.Space(10);
        GUILayout.Label("生成位置", EditorStyles.boldLabel);
        CharacterTestProcedure.PlayerSpawn =
            EditorGUILayout.Vector3Field("玩家出生点", CharacterTestProcedure.PlayerSpawn);
        CharacterTestProcedure.FriendlySpacing =
            EditorGUILayout.FloatField("友方间距", CharacterTestProcedure.FriendlySpacing);
        CharacterTestProcedure.EnemySpawnCenter =
            EditorGUILayout.Vector3Field("敌方出生中心", CharacterTestProcedure.EnemySpawnCenter);
        CharacterTestProcedure.EnemySpacing =
            EditorGUILayout.FloatField("敌方间距", CharacterTestProcedure.EnemySpacing);

        if (EditorGUI.EndChangeCheck())
        {
            SaveCharacterTestSettings();
        }
    }

    private void SaveCharacterTestSettings()
    {
        EditorPrefs.SetInt(PrefKey_FriendlyCount, CharacterTestProcedure.FriendlyCount);
        EditorPrefs.SetInt(PrefKey_EnemyCount, CharacterTestProcedure.EnemyCount);
        EditorPrefs.SetFloat(PrefKey_FriendlySpacing, CharacterTestProcedure.FriendlySpacing);
        EditorPrefs.SetFloat(PrefKey_EnemySpacing, CharacterTestProcedure.EnemySpacing);
        EditorPrefs.SetFloat(PrefKey_PlayerX, CharacterTestProcedure.PlayerSpawn.x);
        EditorPrefs.SetFloat(PrefKey_PlayerY, CharacterTestProcedure.PlayerSpawn.y);
        EditorPrefs.SetFloat(PrefKey_PlayerZ, CharacterTestProcedure.PlayerSpawn.z);
        EditorPrefs.SetFloat(PrefKey_EnemyX, CharacterTestProcedure.EnemySpawnCenter.x);
        EditorPrefs.SetFloat(PrefKey_EnemyY, CharacterTestProcedure.EnemySpawnCenter.y);
        EditorPrefs.SetFloat(PrefKey_EnemyZ, CharacterTestProcedure.EnemySpawnCenter.z);
    }

    #endregion
}
