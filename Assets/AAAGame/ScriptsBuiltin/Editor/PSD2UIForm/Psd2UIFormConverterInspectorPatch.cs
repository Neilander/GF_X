using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UGF.EditorTools.Psd2UGUI;

namespace AAAGame.EditorTools.Psd2UIForm
{
    [CustomEditor(typeof(Psd2UIFormConverter))]
    internal sealed class Psd2UIFormConverterInspectorPatch : Editor
    {
        private Editor innerInspector;
        private bool sanitizedOnEnable;

        private void OnEnable()
        {
            Type innerType = Type.GetType("UGF.EditorTools.Psd2UGUI.Psd2UIFormConverterInspector, cn.efunstudio.psd2ugui");
            if (innerType != null)
            {
                innerInspector = CreateEditor(targets, innerType);
            }

            SanitizeCurrentTargetsOnce();
        }

        private void OnDisable()
        {
            if (innerInspector != null)
            {
                DestroyImmediate(innerInspector);
                innerInspector = null;
            }
        }

        public override void OnInspectorGUI()
        {
            if (innerInspector != null)
            {
                innerInspector.OnInspectorGUI();
            }
            else
            {
                DrawDefaultInspector();
            }
        }

        public override bool HasPreviewGUI()
        {
            return innerInspector != null && innerInspector.HasPreviewGUI();
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (innerInspector != null)
            {
                innerInspector.OnPreviewGUI(r, background);
            }
        }

        private void OnInspectorUpdate()
        {
            if (innerInspector == null)
            {
                return;
            }

            MethodInfo method = innerInspector.GetType().GetMethod("OnInspectorUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(innerInspector, null);
        }

        private void SanitizeCurrentTargetsOnce()
        {
            if (sanitizedOnEnable)
            {
                return;
            }

            sanitizedOnEnable = true;
            bool changed = false;
            foreach (UnityEngine.Object targetObject in targets)
            {
                if (targetObject is Psd2UIFormConverter converter)
                {
                    changed |= Psd2UIFormGeneratedPrefabPostprocessor.SanitizePsdLayerNodes(converter.gameObject);
                    if (changed)
                    {
                        EditorUtility.SetDirty(converter);
                    }
                }
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
            }
        }
    }
}
