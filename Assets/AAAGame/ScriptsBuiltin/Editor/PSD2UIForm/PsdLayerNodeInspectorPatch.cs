using UnityEditor;
using UGF.EditorTools.Psd2UGUI;

namespace AAAGame.EditorTools.Psd2UIForm
{
    [CustomEditor(typeof(PsdLayerNode))]
    internal sealed class PsdLayerNodeInspectorPatch : Editor
    {
        private static readonly string[] VisibleProperties =
        {
            "BindPsdLayerIndex",
            "mLayerType",
            "sourceLayerName",
            "markToExport",
            "UIType",
            "RoleUIType"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((PsdLayerNode)target), typeof(MonoScript), false);
            }

            foreach (string propertyName in VisibleProperties)
            {
                SerializedProperty property = serializedObject.FindProperty(propertyName);
                if (property != null)
                {
                    EditorGUILayout.PropertyField(property);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
