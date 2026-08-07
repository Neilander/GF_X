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
        private SerializedProperty enableEnemyStrongholdHiddenVisionBlock;
        private SerializedProperty createWorldOverlay;
        private SerializedProperty viewSettings;
        private SerializedProperty persistAcrossSceneLoads;
        private SerializedProperty waitForGameplayScene;
        private SerializedProperty gameplaySceneName;
        private SerializedProperty rebuildOnSceneLoaded;
        private SerializedProperty fastRebuildOnGameplaySceneAvailable;
        private SerializedProperty sceneRebuildFrameDelay;
        private SerializedProperty sceneRebuildDelay;

        private static readonly string[] TerrainSourceNames =
        {
            "自动检测",
            "手动配置",
            "TileWorld 地形",
            "A* 网格地形",
            "碰撞体边界"
        };

        private static readonly string[] SurfaceModeNames =
        {
            "云层迷雾",
            "贴合地形",
            "固定世界平面"
        };

        private static readonly string[] TextureFilterNames =
        {
            "点过滤",
            "双线性",
            "三线性"
        };

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
            enableEnemyStrongholdHiddenVisionBlock = serializedObject.FindProperty("enableEnemyStrongholdHiddenVisionBlock");
            createWorldOverlay = serializedObject.FindProperty("createWorldOverlay");
            viewSettings = serializedObject.FindProperty("viewSettings");
            persistAcrossSceneLoads = serializedObject.FindProperty("persistAcrossSceneLoads");
            waitForGameplayScene = serializedObject.FindProperty("waitForGameplayScene");
            gameplaySceneName = serializedObject.FindProperty("gameplaySceneName");
            rebuildOnSceneLoaded = serializedObject.FindProperty("rebuildOnSceneLoaded");
            fastRebuildOnGameplaySceneAvailable = serializedObject.FindProperty("fastRebuildOnGameplaySceneAvailable");
            sceneRebuildFrameDelay = serializedObject.FindProperty("sceneRebuildFrameDelay");
            sceneRebuildDelay = serializedObject.FindProperty("sceneRebuildDelay");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox("FOG3 使用 IMGUI 面板和下拉控件，避免 Unity UIElements 下拉框异常。", MessageType.Info);
            DrawTerrain();
            DrawVision();
            DrawView();
            DrawSceneFlow();
            DrawRuntimeButtons();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTerrain()
        {
            terrainFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(terrainFoldout, "地形检测");
            if (terrainFoldout)
            {
                EditorGUI.indentLevel++;
                DrawSubHeader("地形来源");
                DrawEnumDropdown(terrainSettings.FindPropertyRelative("SourceMode"), "来源模式", TerrainSourceNames);
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("RequireTileWorldCreatorManager"), new GUIContent("必须存在 TileWorld 地形"));
                if (terrainSettings.FindPropertyRelative("RequireTileWorldCreatorManager").boolValue)
                    EditorGUILayout.HelpBox("开启后，只有玩法场景里找到 TileWorldCreatorManager/GridBased 地形时才会生成迷雾，避免影响 Launch 或无关场景。", MessageType.None);
                DrawSubHeader("手动兜底");
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualWidth"), new GUIContent("手动宽度"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualHeight"), new GUIContent("手动高度"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualCellSize"), new GUIContent("格子大小"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("ManualOrigin"), new GUIContent("地图原点"));
                DrawSubHeader("TileWorld 可行走区域");
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("UseTileWorldBlueprintLayerAsWalkable"), new GUIContent("使用蓝图层作为可行走区域"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("TileWorldWalkableLayerName"), new GUIContent("可行走蓝图层名称"));
                DrawSubHeader("物理采样");
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("SampleWalkableWithPhysics"), new GUIContent("使用物理采样可行走区域"));
                DrawLayerMaskDropdown(terrainSettings.FindPropertyRelative("GroundMask"), "地面层级");
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("PhysicsSampleHeight"), new GUIContent("采样起始高度"));
                EditorGUILayout.PropertyField(terrainSettings.FindPropertyRelative("PhysicsSampleDistance"), new GUIContent("采样距离"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawVision()
        {
            visionFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(visionFoldout, "视野参数");
            if (visionFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(currentPlayerVisionRadius, new GUIContent("当前玩家可视半径"));
                EditorGUILayout.PropertyField(playerSideUnitVisionRadius, new GUIContent("我方单位可视半径"));
                EditorGUILayout.PropertyField(buildingVisionRadius, new GUIContent("我方建筑可视半径"));
                EditorGUILayout.PropertyField(autoRegisterPlayerSideEntities, new GUIContent("自动注册我方实体"));
                EditorGUILayout.PropertyField(enableEnemyStrongholdHiddenVisionBlock, new GUIContent("启用敌方据点遮挡 hidden 扩散"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawView()
        {
            viewFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(viewFoldout, "显示效果");
            if (viewFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(createWorldOverlay, new GUIContent("创建世界迷雾遮罩"));
                SerializedProperty surfaceMode = viewSettings.FindPropertyRelative("SurfaceMode");
                DrawSubHeader("表面模式");
                DrawEnumDropdown(surfaceMode, "表面模式", SurfaceModeNames);
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("OverlayHeight"), new GUIContent("遮罩高度"));
                if (surfaceMode.enumValueIndex == (int)Fog3OverlaySurfaceMode.CloudLayer)
                {
                    DrawSubHeader("云层设置");
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("CloudLayerMinimumHeight"), new GUIContent("云层最低高度"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("ClampCloudLayerBelowCamera"), new GUIContent("限制在摄像机下方"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("CloudCameraClearance"), new GUIContent("离摄像机安全距离"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("CloudHeightRefreshInterval"), new GUIContent("云层刷新间隔"));
                    DrawSubHeader("摄像机角度补偿");
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("CloudLayerWorldOffset"), new GUIContent("手动世界偏移"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("UseCameraAngleOffset"), new GUIContent("启用自动角度补偿"));
                    if (viewSettings.FindPropertyRelative("UseCameraAngleOffset").boolValue)
                    {
                        EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("CameraProjectionTargetWorldY"), new GUIContent("对齐目标世界 Y"));
                        EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("CameraAngleOffsetScale"), new GUIContent("角度补偿倍率"));
                    }
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("AutoHeightPadding"), new GUIContent("云层高度余量"));
                    EditorGUILayout.HelpBox("推荐模式。遮罩固定在地图上，摄像机角度补偿会沿运行摄像机方向整体偏移显示层，用来修正斜视角透视偏移。", MessageType.None);
                }
                else if (surfaceMode.enumValueIndex == (int)Fog3OverlaySurfaceMode.TerrainConforming)
                {
                    DrawSubHeader("贴合地形");
                    DrawLayerMaskDropdown(viewSettings.FindPropertyRelative("HeightSampleMask"), "高度采样层级");
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("HeightSampleStartHeight"), new GUIContent("高度采样起点"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("HeightSampleMaxDistance"), new GUIContent("高度采样距离"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("SurfaceOffset"), new GUIContent("表面偏移"));
                }
                DrawSubHeader("渲染");
                SerializedProperty drawOverSceneGeometry = viewSettings.FindPropertyRelative("DrawOverSceneGeometry");
                EditorGUILayout.PropertyField(drawOverSceneGeometry, new GUIContent("覆盖场景物体"));
                if (drawOverSceneGeometry.boolValue)
                {
                    EditorGUILayout.HelpBox("建议开启。迷雾会使用始终在上层的着色器覆盖场景物体。", MessageType.None);
                }
                else if (surfaceMode.enumValueIndex == (int)Fog3OverlaySurfaceMode.FlatWorldPlane)
                {
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("AutoHeightAboveScene"), new GUIContent("自动高于场景"));
                    EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("AutoHeightPadding"), new GUIContent("高度余量"));
                }
                DrawSubHeader("外侧黑色遮罩");
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("OutsideMaskPadding"), new GUIContent("外侧延伸距离"));
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("OutsideMaskInnerOverlap"), new GUIContent("向内压边距离"));
                DrawLayerDropdown(viewSettings.FindPropertyRelative("OverlayLayer"), "迷雾对象层级");
                DrawSubHeader("颜色");
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("HiddenColor"), new GUIContent("未探索颜色"));
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("ExploredColor"), new GUIContent("已探索颜色"));
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("VisibleColor"), new GUIContent("当前可见颜色"));
                EditorGUILayout.PropertyField(viewSettings.FindPropertyRelative("OutsideColor"), new GUIContent("地图外颜色"));
                DrawEnumDropdown(viewSettings.FindPropertyRelative("TextureFilterMode"), "贴图过滤模式", TextureFilterNames);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSceneFlow()
        {
            sceneFlowFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(sceneFlowFoldout, "GF_X 场景流程");
            if (sceneFlowFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("GF_X 默认先加载 Launch，再进入 Game。FOG3 会等待玩法场景和 TileWorld/GridBased 地形可用后初始化。", MessageType.None);
                EditorGUILayout.PropertyField(persistAcrossSceneLoads, new GUIContent("跨场景保留"));
                EditorGUILayout.PropertyField(waitForGameplayScene, new GUIContent("等待玩法场景"));
                EditorGUILayout.PropertyField(gameplaySceneName, new GUIContent("玩法场景名"));
                EditorGUILayout.PropertyField(rebuildOnSceneLoaded, new GUIContent("场景加载后重建"));
                EditorGUILayout.PropertyField(fastRebuildOnGameplaySceneAvailable, new GUIContent("场景可用立即重建"));
                EditorGUILayout.PropertyField(sceneRebuildFrameDelay, new GUIContent("兜底重建延迟帧"));
                EditorGUILayout.PropertyField(sceneRebuildDelay, new GUIContent("兜底重建延迟秒"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRuntimeButtons()
        {
            runtimeFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(runtimeFoldout, "运行时");
            if (runtimeFoldout)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    Fog3Manager manager = (Fog3Manager)target;
                    if (GUILayout.Button("重建地形迷雾"))
                        manager.RebuildTerrain();
                    if (GUILayout.Button("重置迷雾探索"))
                        manager.ResetFog();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawSubHeader(string label)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private static void DrawEnumDropdown(SerializedProperty property, string label, string[] displayNames = null)
        {
            if (displayNames == null || displayNames.Length != property.enumDisplayNames.Length)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                return;
            }

            property.enumValueIndex = EditorGUILayout.Popup(new GUIContent(label), property.enumValueIndex, displayNames);
        }

        private static void DrawLayerMaskDropdown(SerializedProperty property, string label)
        {
            EditorGUILayout.PropertyField(property, new GUIContent(label));
        }

        private static void DrawLayerDropdown(SerializedProperty property, string label)
        {
            bool useDefaultLayer = property.intValue < 0;
            bool nextUseDefault = EditorGUILayout.Toggle("使用对象原层级", useDefaultLayer);
            if (nextUseDefault)
            {
                property.intValue = -1;
                return;
            }

            int currentLayer = Mathf.Clamp(property.intValue, 0, 31);
            property.intValue = EditorGUILayout.LayerField(label, currentLayer);
        }
    }

    [CustomEditor(typeof(Fog3RevealerComponent))]
    public sealed class Fog3RevealerComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("visionRadius"), new GUIContent("可视半径"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRegister"), new GUIContent("自动注册"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useLineOfSight"), new GUIContent("启用视线遮挡"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
