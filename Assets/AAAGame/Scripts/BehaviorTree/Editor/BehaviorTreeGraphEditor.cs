using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BehaviorTreeGraph))]
public class BehaviorTreeGraphEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        if (GUILayout.Button("Open Behavior Tree Editor", GUILayout.Height(30)))
        {
            BehaviorTreeGraph graph = (BehaviorTreeGraph)target;

            BehaviorTreeEditorWindow window =
                EditorWindow.GetWindow<BehaviorTreeEditorWindow>();

            window.titleContent = new GUIContent("BT Editor");
            window.OpenGraph(graph);
            window.Show();
        }
    }
}
