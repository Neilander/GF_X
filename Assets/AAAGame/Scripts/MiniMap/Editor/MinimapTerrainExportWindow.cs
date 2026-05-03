using System;
using System.IO;
using GiantGrey.TileWorldCreator;
using UnityEditor;
using UnityEngine;

namespace AAAGame.MiniMap.Editor
{
    public sealed class MinimapTerrainExportWindow : EditorWindow
    {
        private const string DefaultOutputFolder = "Assets/AAAGame/Art/MinimapExports";

        private TileWorldCreatorManager tileWorldCreatorManager;
        private int textureMaxSize = 1024;
        private string groundLayerKeyword = "Plane";
        private string waterLayerKeyword = "Water";
        private Color groundLayerColor = new Color(0.42f, 0.45f, 0.33f, 1f);
        private Color waterLayerColor = new Color(0.14f, 0.36f, 0.52f, 1f);
        private Color backgroundColor = new Color(0f, 0f, 0f, 1f);
        private bool flipVerticalForPaintTools = true;
        private bool exportInfoText = true;

        [MenuItem("Tools/Minimap/导出地形平面图")]
        private static void Open()
        {
            MinimapTerrainExportWindow window = GetWindow<MinimapTerrainExportWindow>("Minimap 地形导出");
            window.minSize = new Vector2(420f, 330f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("用于导出给美术手绘的 minimap 地形底图。建议在运行时从 ActiveLevelEntity 导出，确保与 procedure 加载后的关卡一致。", MessageType.Info);

            DrawSourceSection();
            DrawExportSettingsSection();

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("导出 PNG", GUILayout.Height(30f)))
            {
                ExportPng();
            }
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("地形来源", EditorStyles.boldLabel);
            tileWorldCreatorManager = (TileWorldCreatorManager)EditorGUILayout.ObjectField("TileWorldCreatorManager", tileWorldCreatorManager, typeof(TileWorldCreatorManager), true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("从运行中关卡读取"))
            {
                TryAssignFromActiveLevelEntity();
            }

            if (GUILayout.Button("场景自动查找"))
            {
                tileWorldCreatorManager = FindObjectOfType<TileWorldCreatorManager>();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawExportSettingsSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("导出设置", EditorStyles.boldLabel);

            textureMaxSize = EditorGUILayout.IntSlider("最大尺寸", textureMaxSize, 64, 4096);
            groundLayerKeyword = EditorGUILayout.TextField("地面层关键字", groundLayerKeyword);
            waterLayerKeyword = EditorGUILayout.TextField("水域层关键字", waterLayerKeyword);
            groundLayerColor = EditorGUILayout.ColorField("地面颜色", groundLayerColor);
            waterLayerColor = EditorGUILayout.ColorField("水域颜色", waterLayerColor);
            backgroundColor = EditorGUILayout.ColorField("背景颜色", backgroundColor);
            flipVerticalForPaintTools = EditorGUILayout.Toggle("垂直翻转(美术软件)", flipVerticalForPaintTools);
            exportInfoText = EditorGUILayout.Toggle("导出说明文本", exportInfoText);
        }

        private void TryAssignFromActiveLevelEntity()
        {
            LevelEntity levelEntity = LevelEntity.ActiveLevelEntity;
            if (levelEntity == null)
            {
                EditorUtility.DisplayDialog("读取失败", "当前没有 ActiveLevelEntity。请先从 launch/procedure 进入玩法关卡再导出。", "确定");
                return;
            }

            tileWorldCreatorManager = levelEntity.GetComponentInChildren<TileWorldCreatorManager>();
            if (tileWorldCreatorManager == null)
            {
                EditorUtility.DisplayDialog("读取失败", "ActiveLevelEntity 下未找到 TileWorldCreatorManager。", "确定");
            }
        }

        private void ExportPng()
        {
            if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
            {
                EditorUtility.DisplayDialog("导出失败", "TileWorldCreatorManager 或其 Configuration 为空。", "确定");
                return;
            }

            MinimapTerrainMapBuildResult result = MinimapTerrainMapBuilder.Build(
                tileWorldCreatorManager.configuration,
                textureMaxSize,
                groundLayerKeyword,
                waterLayerKeyword,
                groundLayerColor,
                waterLayerColor,
                (Color32)backgroundColor);
            if (result == null || result.Texture == null)
            {
                EditorUtility.DisplayDialog("导出失败", "地形纹理构建失败。", "确定");
                return;
            }

            Texture2D exportTexture = result.Texture;
            Texture2D flippedTexture = null;
            if (flipVerticalForPaintTools)
            {
                flippedTexture = CreateVerticallyFlippedCopy(result.Texture);
                exportTexture = flippedTexture;
            }

            string sceneName = tileWorldCreatorManager.gameObject.scene.IsValid()
                ? tileWorldCreatorManager.gameObject.scene.name
                : "Scene";
            string defaultName = $"{sceneName}_MinimapTerrain_{DateTime.Now:yyyyMMdd_HHmmss}";

            string savePath = EditorUtility.SaveFilePanelInProject(
                "导出 minimap 地形图",
                defaultName,
                "png",
                "请选择导出路径",
                DefaultOutputFolder);
            if (string.IsNullOrEmpty(savePath))
            {
                DestroyImmediate(result.Texture);
                if (flippedTexture != null)
                {
                    DestroyImmediate(flippedTexture);
                }
                return;
            }

            string fullSavePath = Path.GetFullPath(savePath);
            File.WriteAllBytes(fullSavePath, exportTexture.EncodeToPNG());
            if (exportInfoText)
            {
                WriteInfoFile(fullSavePath, result, tileWorldCreatorManager);
            }

            DestroyImmediate(result.Texture);
            if (flippedTexture != null)
            {
                DestroyImmediate(flippedTexture);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[MinimapTerrainExport] 导出成功: {savePath}");
        }

        private static Texture2D CreateVerticallyFlippedCopy(Texture2D source)
        {
            Texture2D target = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            Color32[] sourcePixels = source.GetPixels32();
            Color32[] targetPixels = new Color32[sourcePixels.Length];

            int width = source.width;
            int height = source.height;
            for (int y = 0; y < height; y++)
            {
                int srcRowStart = y * width;
                int dstRowStart = (height - 1 - y) * width;
                Array.Copy(sourcePixels, srcRowStart, targetPixels, dstRowStart, width);
            }

            target.SetPixels32(targetPixels);
            target.Apply(false, false);
            target.filterMode = source.filterMode;
            target.wrapMode = source.wrapMode;
            return target;
        }

        private static void WriteInfoFile(string pngFullPath, MinimapTerrainMapBuildResult result, TileWorldCreatorManager manager)
        {
            Vector3 origin = manager.transform.position;
            float worldWidth = result.GridWidth * result.CellSize;
            float worldHeight = result.GridHeight * result.CellSize;
            string infoPath = Path.ChangeExtension(pngFullPath, ".txt");

            string infoText =
                "Minimap Terrain Export Info\n" +
                $"GridWidth: {result.GridWidth}\n" +
                $"GridHeight: {result.GridHeight}\n" +
                $"CellSize: {result.CellSize:F4}\n" +
                $"WorldMinX: {origin.x:F4}\n" +
                $"WorldMinZ: {origin.z:F4}\n" +
                $"WorldMaxX: {origin.x + worldWidth:F4}\n" +
                $"WorldMaxZ: {origin.z + worldHeight:F4}\n" +
                $"UseCenteredGrid: {result.UseCenteredGrid}\n" +
                $"PaintedCount: {result.PaintedCount}\n" +
                $"GroundPaintedCount: {result.GroundPaintedCount}\n" +
                $"WaterPaintedCount: {result.WaterPaintedCount}\n";

            File.WriteAllText(infoPath, infoText);
        }
    }
}
