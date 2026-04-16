using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using TMPro;
using GiantGrey.TileWorldCreator;

namespace AAAGame.MiniMap
{
    public class MinimapUI : UIFormBase
    {
        [Header("UI 组件")]
        [SerializeField] private RectTransform minimapContainer;
        [SerializeField] private GameObject soldierDotPrefab;
        [SerializeField] private RectTransform cameraViewFrame; // 旧的方式（向后兼容）
        [SerializeField] private MinimapCameraFrame cameraFrame; // 新的组件（推荐）

        [Header("比例尺")]
        [SerializeField] private TextMeshProUGUI scaleTextHorizontal;
        [SerializeField] private TextMeshProUGUI scaleTextVertical;

        [Header("小地图尺寸")]
        [SerializeField] private float minimapSize = 200f;

        [Header("摄像机")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private float cameraFrameBorderWidth = 1.5f;

        [Header("建筑图标预制体字典")]
        [SerializeField] private List<BuildingIconMapping> buildingIconMappings = new List<BuildingIconMapping>();

        [Header("地形显示")]
        [SerializeField] private bool showTerrainMap = true;
        [SerializeField, Range(64, 512)] private int terrainTextureMaxSize = 256;
        [SerializeField] private string groundLayerKeyword = "Plane";
        [SerializeField] private string waterLayerKeyword = "Water";
        [SerializeField] private Color groundLayerColor = new Color(0.28f, 0.28f, 0.28f, 1f);
        [SerializeField] private Color waterLayerColor = new Color(0.08f, 0.2f, 0.35f, 1f);

        private MinimapManager minimapManager;
        private Dictionary<int, RectTransform> unitVisuals = new Dictionary<int, RectTransform>();
        private Dictionary<string, GameObject> buildingIconPrefabs = new Dictionary<string, GameObject>();
        private HashSet<int> currentUnitIds = new HashSet<int>();
        private RawImage terrainMapImage;
        private Texture2D terrainMapTexture;
        private int terrainMapLevelEntityId;
        private bool cameraFrameStyleApplied;
        private TileWorldCreatorManager terrainMapTileWorldCreatorManager;
        private bool terrainMapBuildEventSubscribed;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            minimapManager = GameEntry.GetComponent<MinimapManager>();

            if (minimapManager == null)
            {
                Log.Error("[MinimapUI] MinimapManager not found!");
                return;
            }

            if (!EnsureCameraFrame())
            {
                Log.Warning("[MinimapUI] No camera frame configured!");
            }
            else
            {
                ApplyCameraFrameStyle();
            }

            foreach (var mapping in buildingIconMappings)
            {
                if (!string.IsNullOrEmpty(mapping.iconName) && mapping.prefab != null)
                {
                    buildingIconPrefabs[mapping.iconName] = mapping.prefab;
                }
            }

            TryBuildTerrainMap(true);
            UpdateScaleText();
            Log.Info("[MinimapUI] MinimapUI initialized");
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (minimapManager != null)
            {
                minimapManager.OnUnitsUpdated += HandleUnitsUpdated;
                Log.Info("[MinimapUI] Subscribed to event");
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);

            if (minimapManager != null)
            {
                minimapManager.OnUnitsUpdated -= HandleUnitsUpdated;
            }

            foreach (var visual in unitVisuals.Values)
            {
                if (visual != null) Destroy(visual.gameObject);
            }
            unitVisuals.Clear();

            if (terrainMapTexture != null)
            {
                Destroy(terrainMapTexture);
                terrainMapTexture = null;
            }

            UnsubscribeTerrainBuildEvent();
            terrainMapLevelEntityId = 0;
            cameraFrameStyleApplied = false;
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            // 每秒打印一次调试信息（避免日志过多）
            if (Time.frameCount % 60 == 0)
            {
                //Log.Info($"[MinimapUI] OnUpdate called, cameraViewFrame={(cameraViewFrame != null ? "exists" : "NULL")}");
            }

            UpdateCameraViewFrame();

            if (Time.frameCount % 30 == 0)
            {
                TryBuildTerrainMap(false);
                UpdateScaleText();
            }
        }

        private void HandleUnitsUpdated(List<MinimapUnitData> units)
        {
            //Log.Info($"[MinimapUI] HandleUnitsUpdated called with {units?.Count ?? 0} units");

            if (units == null || minimapContainer == null)
            {
                Log.Warning($"[MinimapUI] HandleUnitsUpdated early return: units={units != null}, container={minimapContainer != null}");
                return;
            }

            currentUnitIds.Clear();

            foreach (var unit in units)
            {
                if (!unit.IsVisible) continue;
                currentUnitIds.Add(unit.UnitId);

                if (unitVisuals.ContainsKey(unit.UnitId))
                    UpdateUnitVisual(unit);
                else
                    CreateUnitVisual(unit);
            }

            List<int> toRemove = new List<int>();
            foreach (var id in unitVisuals.Keys)
            {
                if (!currentUnitIds.Contains(id)) toRemove.Add(id);
            }
            foreach (var id in toRemove) RemoveUnitVisual(id);
        }

        private void CreateUnitVisual(MinimapUnitData unit)
        {
            GameObject visualObj = null;

            if (unit.UnitType == MinimapUnitType.Soldier)
            {
                visualObj = soldierDotPrefab != null ?
                    Instantiate(soldierDotPrefab, minimapContainer) :
                    new GameObject($"Soldier_{unit.UnitId}");

                if (soldierDotPrefab == null)
                    visualObj.transform.SetParent(minimapContainer, false);

                Graphic markerGraphic = visualObj.GetComponent<Graphic>();
                if (markerGraphic == null) markerGraphic = visualObj.AddComponent<RawImage>();

                Color soldierColor = minimapManager.Config.GetSoldierColor(unit.Side);
                markerGraphic.color = soldierColor;
                markerGraphic.raycastTarget = false;

                RectTransform rt = visualObj.GetComponent<RectTransform>();
                if (rt == null) rt = visualObj.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(minimapManager.Config.SoldierDotSize, minimapManager.Config.SoldierDotSize);
            }
            else
            {
                visualObj = new GameObject($"Building_{unit.UnitId}");
                visualObj.transform.SetParent(minimapContainer, false);

                RawImage img = visualObj.AddComponent<RawImage>();
                img.color = minimapManager.Config.GetSoldierColor(unit.Side);
                img.raycastTarget = false;

                RectTransform rt = visualObj.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(minimapManager.Config.BuildingIconSize, minimapManager.Config.BuildingIconSize);
            }

            if (visualObj == null) return;

            RectTransform rectTransform = visualObj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = WorldToMinimapPosition(unit.WorldPosition);
                unitVisuals[unit.UnitId] = rectTransform;
            }
        }

        private void UpdateUnitVisual(MinimapUnitData unit)
        {
            if (!unitVisuals.ContainsKey(unit.UnitId)) return;
            RectTransform rt = unitVisuals[unit.UnitId];
            if (rt == null) return;

            rt.anchoredPosition = WorldToMinimapPosition(unit.WorldPosition);

            Graphic markerGraphic = rt.GetComponent<Graphic>();
            if (markerGraphic != null) markerGraphic.color = minimapManager.Config.GetSoldierColor(unit.Side);
        }

        private void RemoveUnitVisual(int unitId)
        {
            if (!unitVisuals.ContainsKey(unitId)) return;
            if (unitVisuals[unitId] != null) Destroy(unitVisuals[unitId].gameObject);
            unitVisuals.Remove(unitId);
        }

        private Vector2 WorldToMinimapPosition(Vector3 worldPos)
        {
            if (minimapManager == null) return Vector2.zero;
            MinimapConfig cfg = minimapManager.Config;
            float nx = Mathf.InverseLerp(cfg.WorldMinX, cfg.WorldMaxX, worldPos.x);
            float nz = Mathf.InverseLerp(cfg.WorldMinZ, cfg.WorldMaxZ, worldPos.z);
            return new Vector2((nx - 0.5f) * minimapSize, (nz - 0.5f) * minimapSize);
        }

        private void UpdateCameraViewFrame()
        {
            if (cameraFrame == null && !EnsureCameraFrame())
            {
                return;
            }

            if (!cameraFrameStyleApplied && cameraFrame != null)
            {
                ApplyCameraFrameStyle();
            }

            if (minimapManager == null)
            {
                return;
            }

            // 每帧动态查找主摄像机（解决场景切换问题）
            if (mainCamera == null || !mainCamera.gameObject.activeInHierarchy)
            {
                mainCamera = Camera.main;
            }

            if (mainCamera == null)
            {
                return;
            }

            // 计算摄像机视野在地面的投影
            MinimapConfig cfg = minimapManager.Config;
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            Vector3 bl = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(0, 0, 0)), ground);
            Vector3 br = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(1, 0, 0)), ground);
            Vector3 tl = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(0, 1, 0)), ground);
            Vector3 tr = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(1, 1, 0)), ground);

            Vector3 center = (bl + br + tl + tr) / 4f;
            float w = Mathf.Max(Vector3.Distance(bl, br), Vector3.Distance(tl, tr));
            float h = Mathf.Max(Vector3.Distance(bl, tl), Vector3.Distance(br, tr));

            float mw = (w / (cfg.WorldMaxX - cfg.WorldMinX)) * minimapSize;
            float mh = (h / (cfg.WorldMaxZ - cfg.WorldMinZ)) * minimapSize;

            Vector2 position = WorldToMinimapPosition(center);
            Vector2 size = new Vector2(mw, mh);

            cameraFrame.UpdateFrame(position, size);
        }

        private bool EnsureCameraFrame()
        {
            if (cameraFrame != null)
            {
                ApplyCameraFrameStyle();
                return true;
            }

            if (cameraViewFrame != null)
            {
                cameraFrame = cameraViewFrame.GetComponent<MinimapCameraFrame>();
                if (cameraFrame == null)
                {
                    cameraFrame = cameraViewFrame.gameObject.AddComponent<MinimapCameraFrame>();
                }

                ApplyCameraFrameStyle();
                Log.Info("[MinimapUI] Migrated legacy cameraViewFrame to MinimapCameraFrame");
                return true;
            }

            return false;
        }

        private Vector3 GetGroundIntersection(Ray ray, Plane plane)
        {
            float enter;
            return plane.Raycast(ray, out enter) ? ray.GetPoint(enter) : Vector3.zero;
        }

        private void UpdateScaleText()
        {
            if (minimapManager == null) return;
            MinimapConfig cfg = minimapManager.Config;
            if (scaleTextHorizontal != null) scaleTextHorizontal.text = $"{cfg.WorldMaxX - cfg.WorldMinX:F0}m";
            if (scaleTextVertical != null) scaleTextVertical.text = $"{cfg.WorldMaxZ - cfg.WorldMinZ:F0}m";
        }

        private void ApplyCameraFrameStyle()
        {
            if (cameraFrame == null)
            {
                return;
            }

            cameraFrame.SetBorderColor(Color.white);
            cameraFrame.SetBorderAlpha(1f);
            cameraFrame.SetBorderWidth(cameraFrameBorderWidth);
            cameraFrame.SetHollow(true);
            cameraFrame.ForceRefresh();
            cameraFrameStyleApplied = true;
        }

        private void TryBuildTerrainMap(bool force)
        {
            if (!showTerrainMap || minimapContainer == null || minimapManager == null)
            {
                return;
            }

            LevelEntity levelEntity = LevelEntity.ActiveLevelEntity;
            if (levelEntity == null)
            {
                return;
            }

            int levelEntityId = levelEntity.GetInstanceID();
            if (!force && terrainMapLevelEntityId == levelEntityId)
            {
                return;
            }

            TileWorldCreatorManager tileWorldCreatorManager = levelEntity.GetComponentInChildren<TileWorldCreatorManager>();
            if (tileWorldCreatorManager == null || tileWorldCreatorManager.configuration == null)
            {
                UnsubscribeTerrainBuildEvent();
                return;
            }

            EnsureTerrainBuildEventSubscription(tileWorldCreatorManager);

            int gridWidth = Mathf.Max(1, tileWorldCreatorManager.configuration.width);
            int gridHeight = Mathf.Max(1, tileWorldCreatorManager.configuration.height);
            int texWidth = gridWidth;
            int texHeight = gridHeight;

            int maxDimension = Mathf.Max(texWidth, texHeight);
            if (maxDimension > terrainTextureMaxSize)
            {
                float scale = terrainTextureMaxSize / (float)maxDimension;
                texWidth = Mathf.Max(1, Mathf.RoundToInt(texWidth * scale));
                texHeight = Mathf.Max(1, Mathf.RoundToInt(texHeight * scale));
            }

            EnsureTerrainMapImage();

            if (terrainMapTexture != null)
            {
                Destroy(terrainMapTexture);
                terrainMapTexture = null;
            }

            terrainMapTexture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
            terrainMapTexture.filterMode = FilterMode.Point;
            terrainMapTexture.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[texWidth * texHeight];
            Color32 backgroundColor = new Color32(0, 0, 0, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }

            HashSet<Vector2> groundPositions = CollectBlueprintLayerCells(tileWorldCreatorManager.configuration, groundLayerKeyword);
            HashSet<Vector2> waterPositions = CollectBlueprintLayerCells(tileWorldCreatorManager.configuration, waterLayerKeyword);

            int paintedCount = 0;
            int waterPaintedCount = PaintLayerCells(pixels, texWidth, texHeight, gridWidth, gridHeight, waterPositions, waterLayerColor);
            int groundPaintedCount = PaintLayerCells(pixels, texWidth, texHeight, gridWidth, gridHeight, groundPositions, groundLayerColor);
            paintedCount += waterPaintedCount + groundPaintedCount;

            if (paintedCount == 0)
            {
                HashSet<Vector2> fallbackPositions = CollectAllBlueprintCells(tileWorldCreatorManager.configuration);
                paintedCount += PaintLayerCells(pixels, texWidth, texHeight, gridWidth, gridHeight, fallbackPositions, groundLayerColor);
            }

            terrainMapTexture.SetPixels32(pixels);
            terrainMapTexture.Apply(false, false);

            terrainMapImage.texture = terrainMapTexture;
            terrainMapImage.color = Color.white;
            terrainMapImage.raycastTarget = false;
            terrainMapImage.rectTransform.SetAsFirstSibling();

            bool expectsGroundLayer = HasBlueprintLayerMatch(tileWorldCreatorManager.configuration, groundLayerKeyword);
            bool expectsWaterLayer = HasBlueprintLayerMatch(tileWorldCreatorManager.configuration, waterLayerKeyword);
            bool groundReady = !expectsGroundLayer || groundPaintedCount > 0;
            bool waterReady = !expectsWaterLayer || waterPaintedCount > 0;
            bool terrainReady = paintedCount > 0 && groundReady && waterReady;

            terrainMapLevelEntityId = terrainReady ? levelEntityId : 0;
        }

        private void EnsureTerrainMapImage()
        {
            if (terrainMapImage == null)
            {
                Transform terrainMapTransform = minimapContainer.Find("TerrainMap");
                if (terrainMapTransform != null)
                {
                    terrainMapImage = terrainMapTransform.GetComponent<RawImage>();
                }

                if (terrainMapImage == null)
                {
                    GameObject terrainMapObject = new GameObject("TerrainMap", typeof(RectTransform), typeof(RawImage));
                    terrainMapObject.transform.SetParent(minimapContainer, false);
                    terrainMapImage = terrainMapObject.GetComponent<RawImage>();
                }
            }

            RectTransform rt = terrainMapImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private HashSet<Vector2> CollectBlueprintLayerCells(Configuration configuration, string layerKeyword)
        {
            HashSet<Vector2> positions = new HashSet<Vector2>();
            if (configuration == null || string.IsNullOrWhiteSpace(layerKeyword))
            {
                return positions;
            }

            string keyword = layerKeyword.Trim();
            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                var folder = configuration.blueprintLayerFolders[i];
                if (folder == null || folder.blueprintLayers == null)
                {
                    continue;
                }

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    var layer = folder.blueprintLayers[j];
                    if (layer == null || string.IsNullOrEmpty(layer.layerName))
                    {
                        continue;
                    }

                    if (layer.layerName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    layer.GetAllCellPositions(positions);
                }
            }

            return positions;
        }

        private HashSet<Vector2> CollectAllBlueprintCells(Configuration configuration)
        {
            HashSet<Vector2> positions = new HashSet<Vector2>();
            if (configuration == null)
            {
                return positions;
            }

            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                var folder = configuration.blueprintLayerFolders[i];
                if (folder == null || folder.blueprintLayers == null)
                {
                    continue;
                }

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    var layer = folder.blueprintLayers[j];
                    if (layer == null)
                    {
                        continue;
                    }

                    layer.GetAllCellPositions(positions);
                }
            }

            return positions;
        }

        private int PaintLayerCells(Color32[] pixels, int texWidth, int texHeight, int gridWidth, int gridHeight, HashSet<Vector2> positions, Color color)
        {
            if (positions == null || positions.Count == 0)
            {
                return 0;
            }

            Color32 pixelColor = color;
            int painted = 0;

            foreach (Vector2 cellPos in positions)
            {
                if (TryConvertCellToTexture(cellPos, gridWidth, gridHeight, texWidth, texHeight, false, out int texX, out int texY))
                {
                    int idx = texX + texY * texWidth;
                    pixels[idx] = pixelColor;
                    painted++;
                }
            }

            if (painted > 0)
            {
                return painted;
            }

            foreach (Vector2 cellPos in positions)
            {
                if (TryConvertCellToTexture(cellPos, gridWidth, gridHeight, texWidth, texHeight, true, out int texX, out int texY))
                {
                    int idx = texX + texY * texWidth;
                    pixels[idx] = pixelColor;
                    painted++;
                }
            }

            return painted;
        }

        private bool TryConvertCellToTexture(Vector2 cellPos, int gridWidth, int gridHeight, int texWidth, int texHeight, bool centeredGrid, out int texX, out int texY)
        {
            float rawX = cellPos.x;
            float rawY = cellPos.y;

            if (centeredGrid)
            {
                rawX += gridWidth * 0.5f;
                rawY += gridHeight * 0.5f;
            }

            int cellX = Mathf.RoundToInt(rawX);
            int cellY = Mathf.RoundToInt(rawY);
            if (cellX < 0 || cellX >= gridWidth || cellY < 0 || cellY >= gridHeight)
            {
                texX = 0;
                texY = 0;
                return false;
            }

            texX = Mathf.Clamp(Mathf.FloorToInt((cellX / (float)gridWidth) * texWidth), 0, texWidth - 1);
            texY = Mathf.Clamp(Mathf.FloorToInt((cellY / (float)gridHeight) * texHeight), 0, texHeight - 1);
            return true;
        }

        private bool HasBlueprintLayerMatch(Configuration configuration, string layerKeyword)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(layerKeyword))
            {
                return false;
            }

            string keyword = layerKeyword.Trim();
            for (int i = 0; i < configuration.blueprintLayerFolders.Count; i++)
            {
                var folder = configuration.blueprintLayerFolders[i];
                if (folder == null || folder.blueprintLayers == null)
                {
                    continue;
                }

                for (int j = 0; j < folder.blueprintLayers.Count; j++)
                {
                    var layer = folder.blueprintLayers[j];
                    if (layer == null || string.IsNullOrEmpty(layer.layerName))
                    {
                        continue;
                    }

                    if (layer.layerName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureTerrainBuildEventSubscription(TileWorldCreatorManager manager)
        {
            if (manager == null)
            {
                UnsubscribeTerrainBuildEvent();
                return;
            }

            if (terrainMapBuildEventSubscribed && terrainMapTileWorldCreatorManager == manager)
            {
                return;
            }

            UnsubscribeTerrainBuildEvent();
            terrainMapTileWorldCreatorManager = manager;
            terrainMapTileWorldCreatorManager.OnBuildLayersReady += HandleTerrainBuildLayersReady;
            terrainMapBuildEventSubscribed = true;
        }

        private void UnsubscribeTerrainBuildEvent()
        {
            if (terrainMapTileWorldCreatorManager != null && terrainMapBuildEventSubscribed)
            {
                terrainMapTileWorldCreatorManager.OnBuildLayersReady -= HandleTerrainBuildLayersReady;
            }

            terrainMapTileWorldCreatorManager = null;
            terrainMapBuildEventSubscribed = false;
        }

        private void HandleTerrainBuildLayersReady()
        {
            terrainMapLevelEntityId = 0;
            TryBuildTerrainMap(true);
        }

    }

    [System.Serializable]
    public class BuildingIconMapping
    {
        public string iconName;
        public GameObject prefab;
    }
}
