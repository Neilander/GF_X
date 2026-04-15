using UnityEditor;
using UnityEngine;

namespace AAAGame.MiniMap.FOG3.Editor
{
    [CustomEditor(typeof(Fog3Manager))]
    public sealed class Fog3ManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty terrainSettings;
        private SerializedProperty currentPlayerVisionRadius;
        private SerializedProperty playerSideUnitVisionRadius;
        private SerializedProperty buildingVisionRadius;
        private SerializedProperty autoRegisterPlayerSideEntities;
        private SerializedProperty updateInterval;
        private SerializedProperty softEdgeWidth;
        private SerializedProperty useLineOfSight;
        private SerializedProperty lineOfSightOccluderMask;
        private SerializedProperty lineOfSightEyeHeight;
        private SerializedProperty createWorldOverlay;
        private SerializedProperty viewSettings;
        private SerializedProperty persistAcrossSceneLoads;
        private SerializedProperty waitForGameplayScene;
        private SerializedProperty gameplaySceneName;
        private SerializedProperty rebuildOnSceneLoaded;
        private SerializedProperty sceneRebuildFrameDelay;
        private SerializedProperty sceneRebuildDelay;

        private bool terrainFoldout = true;
        private bool visionFoldout = true;
        private bool viewFoldout = true;
        private bool sceneFlowFoldout = true;
        private bool runtimeFoldout = true;

        private void OnEnable()
        {
            terrainSettings = serializedObject.FindProperty("terrainSettings");
            currentPlayerVisionRadius = serializedObject.FindProperty("currentPlayerVisionRadius");
            playerSideUnitVisionRadius = serializedObject.FindProperty("playerSideUnitVisionRadius");
            buildingVisionRadius = serializedObject.FindProperty("buildingVisionRadius");
            autoRegisterPlayerSideEntities = serializedObject.FindProperty("autoRegisterPlayerSideEntities");
            updateInterval = serializedObject.FindProperty("updateInterval");
            softEdgeWidth = serializedObject.FindProperty("softEdgeWidth");
            useLineOfSight = serializedObject.FindProperty("useLineOfSight");
            lineOfSightOccluderMask = serializedObject.FindProperty("lineOfSightOccluderMask");
            lineOfSightEyeHeight = serializedObject.FindProperty("lineOfSightEyeHeight");
            createWorldOverlay = serializedObject.FindProperty("createWorldOverlay");
            viewSettings = serializedObject.FindProperty("viewSettings");
            persistAcrossSceneLoads = serializedObject.FindProperty("persistAcrossSceneLoads");
            waitForGameplayScene = serializedObject.FindProperty("waitForGameplayScene");
            gameplaySceneName = serializedObject.FindProperty("gameplaySceneName");
            rebuildOnSceneLoaded = serializedObject.FindProperty("rebuildOnSceneLoaded");
            sceneRebuildFrameDelay = serializedObject.FindProperty("sceneRebuildFrameDelay");
            sceneRebuildDelay = serializedObject.FindProperty("sceneRebuildDelay");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox("FOG3 uses this IMGUI inspector to avoid Unity UIElements dropdown issues.", MessageType.Info);
            DrawTerrain();
            DrawVision();
            DrawView();
            DrawSceneFlow();
            DrawRuntimeButtons();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTerrain()
        {
            terrainFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(terrainFoldout, "Terrain");
            if (terrainFoldout)
            {
                EditorGUI.indentLevel++;
                DrawEnumButtons(terrainSettings.FindPropertyRelative("SourceMode"), "Source Mode");
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualWidth"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualHeight"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualCellSize"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualOrigin"));
                EditorGUILayout.Space(4f);
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("UseTileWorldBlueprintLayerAsWalkable"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("TileWorldWalkableLayerName"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("SampleWalkableWithPhysics"));
                DrawLayerMaskChecklist(terrainSettings.FindPropertyRelative("GroundMask"), "Ground Mask");
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("PhysicsSampleHeight"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("PhysicsSampleDistance"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawVision()
        {
            visionFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(visionFoldout, "Vision");
            if (visionFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(currentPlayerVisionRadius);
                EditorGUILayout.PropertyField(playerSideUnitVisionRadius);
                EditorGUILayout.PropertyField(buildingVisionRadius);
                EditorGUILayout.PropertyField(autoRegisterPlayerSideEntities);
                EditorGUILayout.PropertyField(updateInterval);
                EditorGUILayout.PropertyField(softEdgeWidth);
                EditorGUILayout.PropertyField(useLineOfSight);
                DrawLayerMaskChecklist(lineOfSightOccluderMask, "Line Of Sight Occluder Mask");
                EditorGUILayout.PropertyField(lineOfSightEyeHeight);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawView()
        {
            viewFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(viewFoldout, "View");
            if (viewFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(createWorldOverlay);
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("OverlayHeight"));
                SerializedProperty drawOverSceneGeometry = viewSettings.FindPropertyRelative("DrawOverSceneGeometry");
                EditorGUILayout.PropertyField(drawOverSceneGeometry);
                if (drawOverSceneGeometry.boolValue)
                {
                    EditorGUILayout.HelpBox("Recommended for movable perspective or orthographic cameras. The overlay stays aligned to terrain coordinates and draws over scene geometry by depth test.", MessageType.None);
                }
                else
                {
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("AutoHeightAboveScene"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("AutoHeightPadding"));
                }
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("OutsideMaskPadding"));
                DrawLayerIndexButtons(viewSettings.FindPropertyRelative("OverlayLayer"), "Overlay Layer");
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("HiddenColor"));
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("ExploredColor"));
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("VisibleColor"));
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("OutsideColor"));
                DrawEnumButtons(viewSettings.FindPropertyRelative("TextureFilterMode"), "Texture Filter Mode");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSceneFlow()
        {
            sceneFlowFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(sceneFlowFoldout, "GF_X Scene Flow");
            if (sceneFlowFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Default GF_X flow loads Launch first, then Game. FOG3 waits for the gameplay scene before detecting GridBased terrain.", MessageType.None);
                EditorGUILayout.PropertyField(persistAcrossSceneLoads);
                EditorGUILayout.PropertyField(waitForGameplayScene);
                EditorGUILayout.PropertyField(gameplaySceneName);
                EditorGUILayout.PropertyField(rebuildOnSceneLoaded);
                EditorGUILayout.PropertyField(sceneRebuildFrameDelay);
                EditorGUILayout.PropertyField(sceneRebuildDelay);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRuntimeButtons()
        {
            runtimeFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(runtimeFoldout, "Runtime");
            if (runtimeFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    Fog3Manager manager = (Fog3Manager)target;
                    if (GUILayout.Button("Rebuild Terrain"))
                        manager.RebuildTerrain();
                    if (GUILayout.Button("Reset Fog"))
                        manager.ResetFog();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawEnumButtons(SerializedProperty property, string label)
        {
            EditorGUILayout.LabelField(label);
            EditorGUI.indentLevel++;
            string[] displayNames = property.enumDisplayNames;
            for (int i = 0; i < displayNames.Length; i++)
            {
                bool selected = property.enumValueIndex == i;
                bool next = GUILayout.Toggle(selected, displayNames[i], EditorStyles.miniButton);
                if (next && !selected)
                    property.enumValueIndex = i;
            }
            EditorGUI.indentLevel--;
        }

        private static void DrawLayerMaskChecklist(SerializedProperty property, string label)
        {
            EditorGUILayout.LabelField(label);
            EditorGUI.indentLevel++;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Nothing", EditorStyles.miniButtonLeft))
                    property.intValue = 0;
                if (GUILayout.Button("Everything", EditorStyles.miniButtonRight))
                    property.intValue = -1;
            }

            int mask = property.intValue;
            for (int layer = 0; layer < 32; layer++)
            {
                string layerName = LayerMask.LayerToName(layer);
                if (string.IsNullOrEmpty(layerName))
                    continue;

                int bit = 1 << layer;
                bool enabled = (mask & bit) != 0;
                bool next = EditorGUILayout.ToggleLeft(layer + ": " + layerName, enabled);
                if (next)
                    mask |= bit;
                else
                    mask &= ~bit;
            }

            property.intValue = mask;
            EditorGUI.indentLevel--;
        }

        private static void DrawLayerIndexButtons(SerializedProperty property, string label)
        {
            EditorGUILayout.LabelField(label);
            EditorGUI.indentLevel++;
            property.intValue = EditorGUILayout.IntSlider(property.intValue, -1, 31);
            if (GUILayout.Toggle(property.intValue == -1, "-1: Use GameObject Layer", EditorStyles.miniButton))
                property.intValue = -1;

            for (int layer = 0; layer < 32; layer++)
            {
                string layerName = LayerMask.LayerToName(layer);
                if (string.IsNullOrEmpty(layerName))
                    continue;

                if (GUILayout.Toggle(property.intValue == layer, layer + ": " + layerName, EditorStyles.miniButton))
                    property.intValue = layer;
            }
            EditorGUI.indentLevel--;
        }
    }

    [CustomEditor(typeof(Fog3RevealerComponent))]
    public sealed class Fog3RevealerComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("visionRadius"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRegister"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useLineOfSight"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
