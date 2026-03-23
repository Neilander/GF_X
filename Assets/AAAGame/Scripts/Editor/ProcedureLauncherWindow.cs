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
        "LevelTestProcedure"
    };

    // 显示用的中文标签
    private static readonly string[] ProcedureLabels =
    {
        "CharacterTestProcedure (角色测试)",
        "MenuProcedure (主菜单)",
        "GameProcedure (正式游戏)",
        "LevelTestProcedure (关卡测试)"
    };

    private const string PrefKey_Selected = "Procedure_Selected";

    private int _selectedIndex;

    static ProcedureLauncherWindow()
    {
        EditorApplication.delayCall += LoadToStatic;
    }

    /// <summary>
    /// 编辑器启动 / 脚本重编译后，从 EditorPrefs 恢复设置到静态字段
    /// </summary>
    static void LoadToStatic()
    {
        // 恢复选中的 Procedure
        string saved = EditorPrefs.GetString(PrefKey_Selected, "CharacterTestProcedure");
        ChangeSceneProcedure.SelectedProcedureForGame = saved;

        // 如果选中的是 CharacterTestProcedure，加载其专属设置
        if (saved == "CharacterTestProcedure")
        {
            LoadCharacterTestSettings();
        }
    }

    static void LoadCharacterTestSettings()
    {
        CharacterTestProcedure.FriendlyCount = EditorPrefs.GetInt("Test_FriendlyCount", 5);
        CharacterTestProcedure.EnemyCount = EditorPrefs.GetInt("Test_EnemyCount", 1);
        CharacterTestProcedure.FriendlySpacing = EditorPrefs.GetFloat("Test_FriendlySpacing", 3f);
        CharacterTestProcedure.EnemySpacing = EditorPrefs.GetFloat("Test_EnemySpacing", 3f);
        CharacterTestProcedure.PlayerSpawn = new Vector3(
            EditorPrefs.GetFloat("Test_PlayerX", 0),
            EditorPrefs.GetFloat("Test_PlayerY", 1),
            EditorPrefs.GetFloat("Test_PlayerZ", 0));
        CharacterTestProcedure.EnemySpawnCenter = new Vector3(
            EditorPrefs.GetFloat("Test_EnemyX", 20),
            EditorPrefs.GetFloat("Test_EnemyY", 1),
            EditorPrefs.GetFloat("Test_EnemyZ", 0));
    }

    [MenuItem("Tools/Procedure 启动配置")]
    static void Open() => GetWindow<ProcedureLauncherWindow>("Procedure 启动配置");

    private void OnEnable()
    {
        // 从 EditorPrefs 恢复选中索引
        string saved = EditorPrefs.GetString(PrefKey_Selected, "CharacterTestProcedure");
        _selectedIndex = System.Array.IndexOf(ProcedureNames, saved);
        if (_selectedIndex < 0) _selectedIndex = 0;

        // 同步加载 CharacterTest 设置
        LoadCharacterTestSettings();
    }

    private void OnGUI()
    {
        #region Procedure 选择

        GUILayout.Label("启动 Procedure", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _selectedIndex = EditorGUILayout.Popup("选择 Procedure", _selectedIndex, ProcedureLabels);
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
        EditorPrefs.SetInt("Test_FriendlyCount", CharacterTestProcedure.FriendlyCount);
        EditorPrefs.SetInt("Test_EnemyCount", CharacterTestProcedure.EnemyCount);
        EditorPrefs.SetFloat("Test_FriendlySpacing", CharacterTestProcedure.FriendlySpacing);
        EditorPrefs.SetFloat("Test_EnemySpacing", CharacterTestProcedure.EnemySpacing);
        EditorPrefs.SetFloat("Test_PlayerX", CharacterTestProcedure.PlayerSpawn.x);
        EditorPrefs.SetFloat("Test_PlayerY", CharacterTestProcedure.PlayerSpawn.y);
        EditorPrefs.SetFloat("Test_PlayerZ", CharacterTestProcedure.PlayerSpawn.z);
        EditorPrefs.SetFloat("Test_EnemyX", CharacterTestProcedure.EnemySpawnCenter.x);
        EditorPrefs.SetFloat("Test_EnemyY", CharacterTestProcedure.EnemySpawnCenter.y);
        EditorPrefs.SetFloat("Test_EnemyZ", CharacterTestProcedure.EnemySpawnCenter.z);
    }

    #endregion
}
