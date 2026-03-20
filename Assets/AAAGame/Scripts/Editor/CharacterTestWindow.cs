using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class CharacterTestWindow : EditorWindow
{
    static CharacterTestWindow()
    {
        // EditorPrefs 不能在静态构造函数直接调用，延迟到下一帧
        EditorApplication.delayCall += LoadToStatic;
    }

    static void LoadToStatic()
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

    [MenuItem("Tools/测试配置")]
    static void Open() => GetWindow<CharacterTestWindow>("测试配置");

    private void OnEnable()
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

    private void Save()
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

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        GUILayout.Label("单位数量", EditorStyles.boldLabel);
        CharacterTestProcedure.FriendlyCount = EditorGUILayout.IntSlider("友方数量", CharacterTestProcedure.FriendlyCount, 0, 30);
        CharacterTestProcedure.EnemyCount = EditorGUILayout.IntSlider("敌方数量", CharacterTestProcedure.EnemyCount, 0, 30);

        GUILayout.Space(10);
        GUILayout.Label("生成位置", EditorStyles.boldLabel);
        CharacterTestProcedure.PlayerSpawn = EditorGUILayout.Vector3Field("玩家出生点", CharacterTestProcedure.PlayerSpawn);
        CharacterTestProcedure.FriendlySpacing = EditorGUILayout.FloatField("友方间距", CharacterTestProcedure.FriendlySpacing);
        CharacterTestProcedure.EnemySpawnCenter = EditorGUILayout.Vector3Field("敌方出生中心", CharacterTestProcedure.EnemySpawnCenter);
        CharacterTestProcedure.EnemySpacing = EditorGUILayout.FloatField("敌方间距", CharacterTestProcedure.EnemySpacing);

        if (EditorGUI.EndChangeCheck())
            Save();

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("改完后重新进 Play 生效", MessageType.Info);
    }
}
