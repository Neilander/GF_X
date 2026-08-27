using UnityEditor;
using UnityEngine;

public sealed class EditorRuntimeLevelEntryWindow : EditorWindow
{
    private const string LevelIdentifierEditorPrefKey = "Avenge.EditorRuntimeLevelEntry.LevelIdentifier";
    private string levelIdentifier;

    [MenuItem("Tools/Diagnostics/Enter Runtime Level...")]
    public static void Open()
    {
        GetWindow<EditorRuntimeLevelEntryWindow>(true, "Enter Runtime Level");
    }

    private void OnEnable()
    {
        levelIdentifier = EditorPrefs.GetString(
            LevelIdentifierEditorPrefKey,
            LevelSelectionService.TestLevelIdentifier);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Runtime level identifier", EditorStyles.boldLabel);
        levelIdentifier = EditorGUILayout.TextField(levelIdentifier);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("LvTest"))
            levelIdentifier = LevelSelectionService.TestLevelIdentifier;
        if (GUILayout.Button("Lv_1"))
            levelIdentifier = "Lv_1";
        if (GUILayout.Button("Lv_2"))
            levelIdentifier = "Lv_2";
        if (GUILayout.Button("Lv_3"))
            levelIdentifier = "Lv_3";
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(EditorRuntimeLevelEntryLauncher.Status, MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorRuntimeLevelEntryLauncher.IsEntryPending))
        {
            if (GUILayout.Button("Enter From Launch"))
            {
                EditorPrefs.SetString(LevelIdentifierEditorPrefKey, levelIdentifier.Trim());
                EditorRuntimeLevelEntryLauncher.Enter(levelIdentifier);
            }
        }
    }

    private void Update()
    {
        Repaint();
    }
}
