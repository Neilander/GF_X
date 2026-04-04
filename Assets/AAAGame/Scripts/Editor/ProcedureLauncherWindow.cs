using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class ProcedureLauncherWindow : EditorWindow
{
    // 硬编码的 Procedure 选项列表
    private static readonly string[] ProcedureNames =
    {
        "CharacterTestProcedure",
        "MenuProcedure",
        "GameProcedure",
        "LevelTestProcedure",
        "SampleProcedure",
        "RangedWeaponTestProcedure", // 新增：远程武器测试流程
        "BuffTestProcedure", // 新增：Buff测试流程
        "CardGameProcedure"
    };

    private const string PrefKey_Selected = "Procedure_Selected";
    private const string PrefKey_SceneName = "Procedure_SceneName";
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

    // 可选场景列表（硬编码项目中的游戏场景）
    private static readonly string[] SceneNames =
    {
        "Game",
        "LevelTestScene",
        "CharacterAndSkillTestScene"
    };

    private int _selectedIndex;
    private int _sceneIndex;

    static ProcedureLauncherWindow()
    {
        EditorApplication.delayCall += LoadToStatic;
    }

    /// <summary>
    /// 编辑器启动 / 脚本重编译后，从 EditorPrefs 恢复设置到静态字段
    /// </summary>
    static void LoadToStatic()
    {
        // 恢复选中的 Procedure，校验有效性
        string saved = EditorPrefs.GetString(PrefKey_Selected, "CharacterTestProcedure");
        if (!ChangeSceneProcedure.ValidProcedureNames.Contains(saved))
        {
            Debug.LogWarning($"[ProcedureLauncher] EditorPrefs 中的 Procedure '{saved}' 无效，回退到 CharacterTestProcedure");
            saved = "CharacterTestProcedure";
            EditorPrefs.SetString(PrefKey_Selected, saved);
        }
        ChangeSceneProcedure.SelectedProcedureForGame = saved;

        // 恢复选中的场景，校验有效性
        string savedScene = EditorPrefs.GetString(PrefKey_SceneName, "Game");
        if (System.Array.IndexOf(SceneNames, savedScene) < 0)
        {
            Debug.LogWarning($"[ProcedureLauncher] EditorPrefs 中的场景 '{savedScene}' 无效，回退到 Game");
            savedScene = "Game";
            EditorPrefs.SetString(PrefKey_SceneName, savedScene);
        }
        ChangeSceneProcedure.SelectedSceneForGame = savedScene;

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
        // 从 EditorPrefs 恢复选中索引
        string saved = EditorPrefs.GetString(PrefKey_Selected, "CharacterTestProcedure");
        _selectedIndex = System.Array.IndexOf(ProcedureNames, saved);
        if (_selectedIndex < 0) _selectedIndex = 0;

        // 恢复场景选择索引
        string savedScene = EditorPrefs.GetString(PrefKey_SceneName, "Game");
        _sceneIndex = System.Array.IndexOf(SceneNames, savedScene);
        if (_sceneIndex < 0) _sceneIndex = 0;

        // 同步加载 CharacterTest 设置
        LoadCharacterTestSettings();
    }

    private void OnGUI()
    {
        #region Procedure 选择

        GUILayout.Label("启动 Procedure", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _selectedIndex = EditorGUILayout.Popup("选择 Procedure", _selectedIndex, ProcedureNames);
        if (EditorGUI.EndChangeCheck())
        {
            string selected = ProcedureNames[_selectedIndex];
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
        _sceneIndex = EditorGUILayout.Popup("加载场景", _sceneIndex, SceneNames);
        if (EditorGUI.EndChangeCheck())
        {
            string selectedScene = SceneNames[_sceneIndex];
            EditorPrefs.SetString(PrefKey_SceneName, selectedScene);
            ChangeSceneProcedure.SelectedSceneForGame = selectedScene;
        }

        #endregion

        GUILayout.Space(10);

        #region 根据选中 Procedure 显示对应面板

        if (ProcedureNames[_selectedIndex] == "CharacterTestProcedure")
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
