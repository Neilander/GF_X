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
        "RangedWeaponTestProcedure", // 新增：远程武器测试流程,
        "BuffTestProcedure"
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

    // Buff 配置 PrefKeys
    private const string PrefKey_StartWithBuff = "Test_StartWithBuff";
    private const string PrefKey_FriendlyBuff_Dot = "Test_FBuff_Dot";
    private const string PrefKey_FriendlyBuff_Regen = "Test_FBuff_Regen";
    private const string PrefKey_FriendlyBuff_Speed = "Test_FBuff_Speed";
    private const string PrefKey_FriendlyBuff_Shield = "Test_FBuff_Shield";
    private const string PrefKey_EnemyBuff_Dot = "Test_EBuff_Dot";
    private const string PrefKey_EnemyBuff_Regen = "Test_EBuff_Regen";
    private const string PrefKey_EnemyBuff_Speed = "Test_EBuff_Speed";
    private const string PrefKey_EnemyBuff_Shield = "Test_EBuff_Shield";

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

        // Buff 配置
        CharacterTestProcedure.StartWithBuff = EditorPrefs.GetBool(PrefKey_StartWithBuff, false);
        CharacterTestProcedure.FriendlyBuff_Dot = EditorPrefs.GetBool(PrefKey_FriendlyBuff_Dot, false);
        CharacterTestProcedure.FriendlyBuff_Regen = EditorPrefs.GetBool(PrefKey_FriendlyBuff_Regen, false);
        CharacterTestProcedure.FriendlyBuff_Speed = EditorPrefs.GetBool(PrefKey_FriendlyBuff_Speed, false);
        CharacterTestProcedure.FriendlyBuff_Shield = EditorPrefs.GetBool(PrefKey_FriendlyBuff_Shield, false);
        CharacterTestProcedure.EnemyBuff_Dot = EditorPrefs.GetBool(PrefKey_EnemyBuff_Dot, false);
        CharacterTestProcedure.EnemyBuff_Regen = EditorPrefs.GetBool(PrefKey_EnemyBuff_Regen, false);
        CharacterTestProcedure.EnemyBuff_Speed = EditorPrefs.GetBool(PrefKey_EnemyBuff_Speed, false);
        CharacterTestProcedure.EnemyBuff_Shield = EditorPrefs.GetBool(PrefKey_EnemyBuff_Shield, false);
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

        GUILayout.Space(10);
        GUILayout.Label("出生 Buff", EditorStyles.boldLabel);
        CharacterTestProcedure.StartWithBuff =
            EditorGUILayout.Toggle("启用 Start With Buff", CharacterTestProcedure.StartWithBuff);

        if (CharacterTestProcedure.StartWithBuff)
        {
            EditorGUI.indentLevel++;

            GUILayout.Label("友方 Buff", EditorStyles.miniBoldLabel);
            CharacterTestProcedure.FriendlyBuff_Dot =
                EditorGUILayout.Toggle("Dot (持续伤害)", CharacterTestProcedure.FriendlyBuff_Dot);
            CharacterTestProcedure.FriendlyBuff_Regen =
                EditorGUILayout.Toggle("Regen (持续回血)", CharacterTestProcedure.FriendlyBuff_Regen);
            CharacterTestProcedure.FriendlyBuff_Speed =
                EditorGUILayout.Toggle("Speed (移速)", CharacterTestProcedure.FriendlyBuff_Speed);
            CharacterTestProcedure.FriendlyBuff_Shield =
                EditorGUILayout.Toggle("Shield (护盾)", CharacterTestProcedure.FriendlyBuff_Shield);

            GUILayout.Space(5);
            GUILayout.Label("敌方 Buff", EditorStyles.miniBoldLabel);
            CharacterTestProcedure.EnemyBuff_Dot =
                EditorGUILayout.Toggle("Dot (持续伤害)", CharacterTestProcedure.EnemyBuff_Dot);
            CharacterTestProcedure.EnemyBuff_Regen =
                EditorGUILayout.Toggle("Regen (持续回血)", CharacterTestProcedure.EnemyBuff_Regen);
            CharacterTestProcedure.EnemyBuff_Speed =
                EditorGUILayout.Toggle("Speed (移速)", CharacterTestProcedure.EnemyBuff_Speed);
            CharacterTestProcedure.EnemyBuff_Shield =
                EditorGUILayout.Toggle("Shield (护盾)", CharacterTestProcedure.EnemyBuff_Shield);

            EditorGUI.indentLevel--;
        }

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

        // Buff 配置
        EditorPrefs.SetBool(PrefKey_StartWithBuff, CharacterTestProcedure.StartWithBuff);
        EditorPrefs.SetBool(PrefKey_FriendlyBuff_Dot, CharacterTestProcedure.FriendlyBuff_Dot);
        EditorPrefs.SetBool(PrefKey_FriendlyBuff_Regen, CharacterTestProcedure.FriendlyBuff_Regen);
        EditorPrefs.SetBool(PrefKey_FriendlyBuff_Speed, CharacterTestProcedure.FriendlyBuff_Speed);
        EditorPrefs.SetBool(PrefKey_FriendlyBuff_Shield, CharacterTestProcedure.FriendlyBuff_Shield);
        EditorPrefs.SetBool(PrefKey_EnemyBuff_Dot, CharacterTestProcedure.EnemyBuff_Dot);
        EditorPrefs.SetBool(PrefKey_EnemyBuff_Regen, CharacterTestProcedure.EnemyBuff_Regen);
        EditorPrefs.SetBool(PrefKey_EnemyBuff_Speed, CharacterTestProcedure.EnemyBuff_Speed);
        EditorPrefs.SetBool(PrefKey_EnemyBuff_Shield, CharacterTestProcedure.EnemyBuff_Shield);
    }

    #endregion
}
