using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using TMPro;
using GiantGrey.TileWorldCreator;
using AAAGame.MiniMap.FOG3;

namespace AAAGame.MiniMap
{
    public class MinimapUI : UIFormBase
    {
        private const float CameraFrameAspect = 16f / 9f;
        private const float CameraFrameAdditionalScale = 1f / 2f;
        private const float TeleportationMarkerSmallSize = 8f;
        private const float TeleportationMarkerLargeSize = 18f;
        private static readonly Color TeleportationMarkerColor = new Color(0.1f, 0.52f, 1f, 1f);

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
        [SerializeField, Range(0.1f, 1f)] private float cameraFrameSizeScale = 0.5f;
        [SerializeField] private bool useHeroYForFrameGround = true;
        [SerializeField] private float cameraFrameGroundYOffset = 0f;

        [Header("建筑图标预制体字典")]
        [SerializeField] private List<BuildingIconMapping> buildingIconMappings = new List<BuildingIconMapping>();
        [SerializeField] private Texture2D targetLocationIconTexture;

        [Header("地形显示")]
        [SerializeField] private bool showTerrainMap = true;
        [SerializeField, Range(64, 512)] private int terrainTextureMaxSize = 256;
        [SerializeField] private string groundLayerKeyword = "Plane";
        [SerializeField] private string waterLayerKeyword = "Water";

        [Header("小地图迷雾同步")]
        [SerializeField] private bool showMinimapFogSync = true;
        [SerializeField] private float fogOverlayRefreshInterval = 0.08f;
        [SerializeField] private Color minimapHiddenFogColor = new Color(0f, 0f, 0f, 0.98f);
        [SerializeField] private Color minimapExploredFogColor = new Color(0f, 0f, 0f, 0.3f);
        [SerializeField] private Color minimapOutsideFogColor = new Color(0f, 0f, 0f, 1f);

        private MinimapManager minimapManager;
        private Dictionary<int, RectTransform> unitVisuals = new Dictionary<int, RectTransform>();
        private Dictionary<string, GameObject> buildingIconPrefabs = new Dictionary<string, GameObject>();
        private HashSet<int> currentUnitIds = new HashSet<int>();
        private HashSet<int> snapshotUnitIds = new HashSet<int>();
        private readonly List<int> staleUnitVisualIds = new List<int>();
        private readonly List<int> staleBuildingUnitIds = new List<int>();
        private readonly List<int> staleTargetLocationUnitIds = new List<int>();
        private HashSet<int> knownBuildingUnitIds = new HashSet<int>();
        private HashSet<int> targetLocationUnitIds = new HashSet<int>();
        private RawImage terrainMapImage;
        private Texture2D terrainMapTexture;
        private RawImage fogMapImage;
        private Texture2D fogMapTexture;
        private Color32[] fogMapPixels;
        private Fog3Manager fog3Manager;
        private float fogOverlayRefreshTimer;
        private bool fogSyncReadyLogged;
        private bool fogSyncMissingLogged;
        private int terrainMapLevelEntityId;
        private bool cameraFrameStyleApplied;
        private TileWorldCreatorManager terrainMapTileWorldCreatorManager;
        private bool terrainMapBuildEventSubscribed;
        private bool terrainMapUseCenteredGrid;
        private int terrainGridWidth;
        private int terrainGridHeight;
        private float terrainCellSize = 1f;
        private RectTransform minimapContent;
        private readonly List<TeleportationMarker> teleportationMarkers = new List<TeleportationMarker>();
        private int teleportationMarkerLevelEntityId;
        private bool teleportationMarkersInitialized;
        private bool isLargeMap;
        private Action<EntityPresetPoint> teleportationPointClicked;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            minimapManager = GameEntry.GetComponent<MinimapManager>();

            if (minimapManager == null)
            {
                Log.Error("[MinimapUI] MinimapManager not found!");
                return;
            }

            HideScaleText();
            EnsureMinimapContent();
            UpdateMinimapContentLayout();

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
            RefreshMinimapFogOverlay(true, 0f);
            RefreshTeleportationMarkers();
            Log.Info("[MinimapUI] MinimapUI initialized");
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            UpdateMinimapContentLayout();

            if (minimapManager != null)
            {
                minimapManager.OnUnitsUpdated += HandleUnitsUpdated;
                Log.Info("[MinimapUI] Subscribed to event");
            }

            RefreshTeleportationMarkers();
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
            knownBuildingUnitIds.Clear();
            targetLocationUnitIds.Clear();
            currentUnitIds.Clear();
            snapshotUnitIds.Clear();

            if (terrainMapTexture != null)
            {
                Destroy(terrainMapTexture);
                terrainMapTexture = null;
            }

            if (fogMapTexture != null)
            {
                Destroy(fogMapTexture);
                fogMapTexture = null;
            }
            fogMapPixels = null;
            fogOverlayRefreshTimer = 0f;
            fogSyncReadyLogged = false;
            fogSyncMissingLogged = false;

            UnsubscribeTerrainBuildEvent();
            terrainMapLevelEntityId = 0;
            cameraFrameStyleApplied = false;
            terrainMapUseCenteredGrid = false;
            terrainGridWidth = 0;
            terrainGridHeight = 0;
            terrainCellSize = 1f;
            ClearTeleportationMarkers();
            isLargeMap = false;
            teleportationPointClicked = null;
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
            }

            RefreshMinimapFogOverlay(false, realElapseSeconds);
            RefreshTeleportationMarkers();
        }

        public void RefreshLayout()
        {
            UpdateMinimapContentLayout();
            RefreshTeleportationMarkerStates();
            UpdateCameraViewFrame();
        }

        public void SetLargeMapState(bool largeMap, Action<EntityPresetPoint> pointClicked)
        {
            if (largeMap && pointClicked == null)
                throw new ArgumentNullException(nameof(pointClicked));
            if (!largeMap && pointClicked != null)
                throw new ArgumentException("Closed minimap state cannot retain a teleportation callback.", nameof(pointClicked));

            isLargeMap = largeMap;
            teleportationPointClicked = pointClicked;
            RefreshTeleportationMarkers();
        }

        private RectTransform GetMinimapContent()
        {
            if (minimapContent == null)
            {
                EnsureMinimapContent();
            }

            return minimapContent != null ? minimapContent : minimapContainer;
        }

        private void EnsureMinimapContent()
        {
            if (minimapContainer == null)
                return;

            if (Params != null && Params.IsSubUIForm)
            {
                minimapContainer.anchorMin = Vector2.zero;
                minimapContainer.anchorMax = Vector2.one;
                minimapContainer.offsetMin = Vector2.zero;
                minimapContainer.offsetMax = Vector2.zero;
                minimapContainer.pivot = new Vector2(0.5f, 0.5f);
                minimapContainer.anchoredPosition = Vector2.zero;
                minimapContainer.localScale = Vector3.one;
            }

            Transform contentTransform = minimapContainer.Find("MinimapContent");
            if (contentTransform == null)
            {
                GameObject contentObject = new GameObject("MinimapContent", typeof(RectTransform));
                contentObject.transform.SetParent(minimapContainer, false);
                minimapContent = contentObject.GetComponent<RectTransform>();
            }
            else
            {
                minimapContent = contentTransform as RectTransform;
            }

            minimapContent.anchorMin = new Vector2(0.5f, 0.5f);
            minimapContent.anchorMax = new Vector2(0.5f, 0.5f);
            minimapContent.pivot = new Vector2(0.5f, 0.5f);
            minimapContent.anchoredPosition = Vector2.zero;
            minimapContent.localScale = Vector3.one;

            MoveChildToMinimapContent("CameraViewFrame");
            MoveChildToMinimapContent("TerrainMap");
            MoveChildToMinimapContent("FogMap");

            if (cameraViewFrame != null && cameraViewFrame.parent == minimapContainer)
                cameraViewFrame.SetParent(minimapContent, false);

            if (cameraFrame != null && cameraFrame.transform.parent == minimapContainer)
                cameraFrame.transform.SetParent(minimapContent, false);

            if (cameraFrame != null)
                cameraFrame.SetBounds(minimapContent);
        }

        private void MoveChildToMinimapContent(string childName)
        {
            if (minimapContainer == null || minimapContent == null || string.IsNullOrEmpty(childName))
                return;

            Transform child = minimapContainer.Find(childName);
            if (child != null)
                child.SetParent(minimapContent, false);
        }

        private void UpdateMinimapContentLayout()
        {
            EnsureMinimapContent();
            if (minimapContainer == null || minimapContent == null || minimapManager == null)
                return;

            Rect boundsRect = minimapContainer.rect;
            float boundsWidth = Mathf.Abs(boundsRect.width);
            float boundsHeight = Mathf.Abs(boundsRect.height);
            if (boundsWidth <= 0.01f || boundsHeight <= 0.01f)
                return;

            MinimapConfig config = minimapManager.Config;
            float worldWidth = terrainGridWidth > 0 ? terrainGridWidth : Mathf.Max(0.01f, config.WorldMaxX - config.WorldMinX);
            float worldHeight = terrainGridHeight > 0 ? terrainGridHeight : Mathf.Max(0.01f, config.WorldMaxZ - config.WorldMinZ);
            float mapAspect = worldWidth / worldHeight;
            float boundsAspect = boundsWidth / boundsHeight;

            Vector2 contentSize = boundsAspect >= mapAspect
                ? new Vector2(boundsHeight * mapAspect, boundsHeight)
                : new Vector2(boundsWidth, boundsWidth / mapAspect);

            minimapContent.sizeDelta = contentSize;
            minimapContent.anchoredPosition = Vector2.zero;
            UpdateOverlaySiblingOrder();
        }

        private void HideScaleText()
        {
            if (scaleTextHorizontal != null)
                scaleTextHorizontal.gameObject.SetActive(false);

            if (scaleTextVertical != null)
                scaleTextVertical.gameObject.SetActive(false);
        }

        private void HandleUnitsUpdated(List<MinimapUnitData> units)
        {
            //Log.Info($"[MinimapUI] HandleUnitsUpdated called with {units?.Count ?? 0} units");

            RectTransform content = GetMinimapContent();
            if (units == null || content == null)
            {
                Log.Warning($"[MinimapUI] HandleUnitsUpdated early return: units={units != null}, content={content != null}");
                return;
            }

            currentUnitIds.Clear();
            snapshotUnitIds.Clear();

            Fog3MapData fogMapData = GetFogMapData();
            bool hasFogMap = showMinimapFogSync && fogMapData != null;

            foreach (var unit in units)
            {
                snapshotUnitIds.Add(unit.UnitId);

                if (!unit.IsVisible)
                {
                    RemoveUnitVisual(unit.UnitId);
                    continue;
                }

                if (IsPriorityMarker(unit))
                {
                    currentUnitIds.Add(unit.UnitId);
                    targetLocationUnitIds.Add(unit.UnitId);
                    knownBuildingUnitIds.Add(unit.UnitId);

                    if (unitVisuals.ContainsKey(unit.UnitId))
                        UpdateUnitVisual(unit);
                    else
                        CreateUnitVisual(unit);

                    continue;
                }

                Fog3CellState fogCellState = hasFogMap
                    ? ResolveFogCellState(fogMapData, unit.WorldPosition)
                    : Fog3CellState.Visible;

                if (fogCellState == Fog3CellState.Visible)
                {
                    currentUnitIds.Add(unit.UnitId);
                    if (unit.UnitType == MinimapUnitType.Building)
                        knownBuildingUnitIds.Add(unit.UnitId);

                    if (unitVisuals.ContainsKey(unit.UnitId))
                        UpdateUnitVisual(unit);
                    else
                        CreateUnitVisual(unit);

                    continue;
                }

                // 已探索区域：建筑保留最后一次可见信息，不更新；单位不显示。
                if (fogCellState == Fog3CellState.Explored
                    && unit.UnitType == MinimapUnitType.Building
                    && knownBuildingUnitIds.Contains(unit.UnitId)
                    && unitVisuals.ContainsKey(unit.UnitId))
                {
                    currentUnitIds.Add(unit.UnitId);
                    continue;
                }

                RemoveUnitVisual(unit.UnitId);
            }

            staleUnitVisualIds.Clear();
            foreach (var id in unitVisuals.Keys)
            {
                if (!currentUnitIds.Contains(id)) staleUnitVisualIds.Add(id);
            }
            for (int i = 0; i < staleUnitVisualIds.Count; i++)
                RemoveUnitVisual(staleUnitVisualIds[i]);

            if (knownBuildingUnitIds.Count > 0)
            {
                staleBuildingUnitIds.Clear();
                foreach (int buildingId in knownBuildingUnitIds)
                {
                    if (snapshotUnitIds.Contains(buildingId))
                        continue;

                    staleBuildingUnitIds.Add(buildingId);
                }

                for (int i = 0; i < staleBuildingUnitIds.Count; i++)
                    knownBuildingUnitIds.Remove(staleBuildingUnitIds[i]);
            }

            if (targetLocationUnitIds.Count > 0)
            {
                staleTargetLocationUnitIds.Clear();
                foreach (int targetId in targetLocationUnitIds)
                {
                    if (snapshotUnitIds.Contains(targetId))
                        continue;

                    staleTargetLocationUnitIds.Add(targetId);
                }

                for (int i = 0; i < staleTargetLocationUnitIds.Count; i++)
                    targetLocationUnitIds.Remove(staleTargetLocationUnitIds[i]);
            }

            RefreshMinimapFogOverlay(false, 0f);
            SetTargetLocationIconsAsLastSibling();
        }

        private static bool IsPriorityMarker(MinimapUnitData unit)
        {
            return unit.UnitType == MinimapUnitType.Objective
                || (unit.UnitType == MinimapUnitType.Building
                    && unit.IconPrefabName == MinimapUnitData.TargetLocationIconName);
        }

        private void CreateUnitVisual(MinimapUnitData unit)
        {
            GameObject visualObj = null;
            RectTransform content = GetMinimapContent();
            if (content == null)
                return;

            if (unit.UnitType == MinimapUnitType.Soldier)
            {
                visualObj = soldierDotPrefab != null ?
                    Instantiate(soldierDotPrefab, content) :
                    new GameObject($"Soldier_{unit.UnitId}");

                if (soldierDotPrefab == null)
                    visualObj.transform.SetParent(content, false);

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
                visualObj = CreateBuildingVisual(unit, content);
            }

            if (visualObj == null) return;

            RectTransform rectTransform = visualObj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = WorldToMinimapPosition(unit.WorldPosition);
                unitVisuals[unit.UnitId] = rectTransform;
            }

            if (IsPriorityMarker(unit))
                SetTargetLocationIconsAsLastSibling();
            else
                SetCameraFrameAsLastSibling();
        }

        private GameObject CreateBuildingVisual(MinimapUnitData unit, RectTransform content)
        {
            bool isTargetLocationIcon = unit.IconPrefabName == MinimapUnitData.TargetLocationIconName;
            bool isObjectiveIcon = unit.UnitType == MinimapUnitType.Objective;
            GameObject visualObj = null;

            if (!isTargetLocationIcon
                && !isObjectiveIcon
                && !string.IsNullOrWhiteSpace(unit.IconPrefabName)
                && buildingIconPrefabs.TryGetValue(unit.IconPrefabName, out GameObject iconPrefab)
                && iconPrefab != null)
            {
                visualObj = Instantiate(iconPrefab, content);
            }
            else
            {
                visualObj = new GameObject($"Building_{unit.UnitId}");
                visualObj.transform.SetParent(content, false);
            }

            RectTransform rt = visualObj.GetComponent<RectTransform>();
            if (rt == null) rt = visualObj.AddComponent<RectTransform>();
            float iconSize = minimapManager.Config.BuildingIconSize * (isTargetLocationIcon || isObjectiveIcon ? 2f : 1f);
            rt.sizeDelta = new Vector2(iconSize, iconSize);

            if (isTargetLocationIcon || isObjectiveIcon)
            {
                RawImage img = visualObj.GetComponent<RawImage>();
                if (img == null) img = visualObj.AddComponent<RawImage>();

                img.texture = targetLocationIconTexture;
                img.color = isObjectiveIcon ? Color.green : Color.red;
                img.raycastTarget = false;
                return visualObj;
            }

            Graphic markerGraphic = visualObj.GetComponent<Graphic>();
            if (markerGraphic == null) markerGraphic = visualObj.AddComponent<RawImage>();
            markerGraphic.color = minimapManager.Config.GetSoldierColor(unit.Side);
            markerGraphic.raycastTarget = false;

            return visualObj;
        }

        private void UpdateUnitVisual(MinimapUnitData unit)
        {
            if (!unitVisuals.ContainsKey(unit.UnitId)) return;
            RectTransform rt = unitVisuals[unit.UnitId];
            if (rt == null) return;

            rt.anchoredPosition = WorldToMinimapPosition(unit.WorldPosition);

            Graphic markerGraphic = rt.GetComponent<Graphic>();
            if (markerGraphic != null)
            {
                markerGraphic.color = unit.UnitType == MinimapUnitType.Objective
                    ? Color.green
                    : unit.IconPrefabName == MinimapUnitData.TargetLocationIconName
                        ? Color.red
                        : minimapManager.Config.GetSoldierColor(unit.Side);
            }
        }

        private void RemoveUnitVisual(int unitId)
        {
            if (!unitVisuals.ContainsKey(unitId)) return;
            if (unitVisuals[unitId] != null) Destroy(unitVisuals[unitId].gameObject);
            unitVisuals.Remove(unitId);
            targetLocationUnitIds.Remove(unitId);
        }

        private Fog3MapData GetFogMapData()
        {
            if (!showMinimapFogSync)
                return null;

            if (fog3Manager == null)
                fog3Manager = Fog3Manager.Instance != null ? Fog3Manager.Instance : GameEntry.GetComponent<Fog3Manager>();

            Fog3MapData fogMapData = fog3Manager != null ? fog3Manager.MapData : null;
            if (fogMapData != null)
            {
                if (!fogSyncReadyLogged)
                {
                    fogSyncReadyLogged = true;
                    fogSyncMissingLogged = false;
                    Log.Info("[MinimapUI] FOG3 minimap sync enabled.");
                }
            }
            else if (!fogSyncMissingLogged)
            {
                fogSyncMissingLogged = true;
                fogSyncReadyLogged = false;
                Log.Warning("[MinimapUI] FOG3 map data not ready, minimap fog sync is waiting.");
            }

            return fogMapData;
        }

        private static Fog3CellState ResolveFogCellState(Fog3MapData fogMapData, Vector3 worldPosition)
        {
            if (fogMapData == null)
                return Fog3CellState.Visible;

            if (!fogMapData.WorldToGrid(worldPosition, out int gridX, out int gridY))
                return Fog3CellState.Outside;

            return fogMapData.GetCellState(gridX, gridY);
        }

        private void RefreshMinimapFogOverlay(bool force, float deltaTime)
        {
            if (!showMinimapFogSync || GetMinimapContent() == null)
            {
                DisableFogOverlay();
                return;
            }

            if (!force)
            {
                fogOverlayRefreshTimer += Mathf.Max(0f, deltaTime);
                if (fogOverlayRefreshTimer < Mathf.Max(0.02f, fogOverlayRefreshInterval))
                    return;
            }

            fogOverlayRefreshTimer = 0f;

            Fog3MapData fogMapData = GetFogMapData();
            if (fogMapData == null)
            {
                DisableFogOverlay();
                return;
            }

            EnsureFogMapImage();
            EnsureFogMapTexture(fogMapData.Width, fogMapData.Height);

            Color32 hiddenColor = minimapHiddenFogColor;
            Color32 exploredColor = minimapExploredFogColor;
            Color32 outsideColor = minimapOutsideFogColor;
            Color32 visibleColor = new Color32(0, 0, 0, 0);

            int width = fogMapData.Width;
            int height = fogMapData.Height;
            for (int y = 0; y < height; y++)
            {
                int rowIndex = y * width;
                for (int x = 0; x < width; x++)
                {
                    Fog3CellState cellState = fogMapData.GetCellState(x, y);
                    switch (cellState)
                    {
                        case Fog3CellState.Visible:
                            fogMapPixels[rowIndex + x] = visibleColor;
                            break;
                        case Fog3CellState.Explored:
                            fogMapPixels[rowIndex + x] = exploredColor;
                            break;
                        case Fog3CellState.Outside:
                            fogMapPixels[rowIndex + x] = outsideColor;
                            break;
                        default:
                            fogMapPixels[rowIndex + x] = hiddenColor;
                            break;
                    }
                }
            }

            fogMapTexture.SetPixels32(fogMapPixels);
            fogMapTexture.Apply(false, false);
            fogMapImage.texture = fogMapTexture;
            fogMapImage.color = Color.white;
            fogMapImage.enabled = true;
            UpdateOverlaySiblingOrder();
        }

        private void EnsureFogMapImage()
        {
            RectTransform content = GetMinimapContent();
            if (content == null)
                return;

            if (fogMapImage == null)
            {
                Transform fogMapTransform = content.Find("FogMap");
                if (fogMapTransform != null)
                    fogMapImage = fogMapTransform.GetComponent<RawImage>();

                if (fogMapImage == null)
                {
                    GameObject fogMapObject = new GameObject("FogMap", typeof(RectTransform), typeof(RawImage));
                    fogMapObject.transform.SetParent(content, false);
                    fogMapImage = fogMapObject.GetComponent<RawImage>();
                }
            }

            RectTransform rt = fogMapImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            fogMapImage.raycastTarget = false;
            UpdateOverlaySiblingOrder();
        }

        private void EnsureFogMapTexture(int width, int height)
        {
            if (fogMapTexture != null && fogMapTexture.width == width && fogMapTexture.height == height && fogMapPixels != null)
                return;

            if (fogMapTexture != null)
                Destroy(fogMapTexture);

            fogMapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            fogMapTexture.filterMode = FilterMode.Point;
            fogMapTexture.wrapMode = TextureWrapMode.Clamp;
            fogMapPixels = new Color32[width * height];
        }

        private void DisableFogOverlay()
        {
            if (fogMapImage != null)
                fogMapImage.enabled = false;
        }

        private void UpdateOverlaySiblingOrder()
        {
            RectTransform content = GetMinimapContent();
            if (content == null)
                return;

            if (terrainMapImage != null)
                terrainMapImage.rectTransform.SetSiblingIndex(0);

            if (fogMapImage != null)
            {
                int fogSiblingIndex = content.childCount > 1 ? 1 : 0;
                fogMapImage.rectTransform.SetSiblingIndex(fogSiblingIndex);
            }

            SetCameraFrameAsLastSibling();
            SetTargetLocationIconsAsLastSibling();
            SetTeleportationMarkersAsLastSibling();
        }

        private void SetCameraFrameAsLastSibling()
        {
            if (cameraFrame != null)
            {
                cameraFrame.transform.SetAsLastSibling();
                return;
            }

            if (cameraViewFrame != null)
                cameraViewFrame.SetAsLastSibling();
        }

        private void SetTargetLocationIconsAsLastSibling()
        {
            foreach (int unitId in targetLocationUnitIds)
            {
                if (unitVisuals.TryGetValue(unitId, out RectTransform targetIcon) && targetIcon != null)
                    targetIcon.SetAsLastSibling();
            }
        }

        private void RefreshTeleportationMarkers()
        {
            LevelEntity level = LevelEntity.ActiveLevelEntity;
            if (level == null)
            {
                ClearTeleportationMarkers();
                return;
            }

            int levelEntityId = level.GetInstanceID();
            if (teleportationMarkerLevelEntityId != levelEntityId)
            {
                ClearTeleportationMarkers();
                teleportationMarkerLevelEntityId = levelEntityId;
            }

            if (!level.IsRuntimeInitializationCompleted)
                return;
            if (!LogicStrongholdMap.IsInitialized)
                throw new InvalidOperationException("MinimapUI cannot display teleportation points without an initialized LogicStrongholdMap.");

            if (!teleportationMarkersInitialized)
                CreateTeleportationMarkers(level);

            RefreshTeleportationMarkerStates();
        }

        private void CreateTeleportationMarkers(LevelEntity level)
        {
            RectTransform content = GetMinimapContent()
                ?? throw new InvalidOperationException("MinimapUI cannot create teleportation markers without minimap content.");
            EntityPresetPoint[] points = level.GetComponentsInChildren<EntityPresetPoint>(true);
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                if (point.PointType != EntityPresetPointType.Teleportation)
                    continue;

                string strongholdId = TeleportationPointService.GetStrongholdIdRequired(point);
                var markerObject = new GameObject(
                    $"TeleportationMarker_{point.name}",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Button));
                markerObject.transform.SetParent(content, false);

                RectTransform rect = markerObject.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                Image image = markerObject.GetComponent<Image>();
                image.color = TeleportationMarkerColor;

                Button button = markerObject.GetComponent<Button>();
                button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                EntityPresetPoint clickedPoint = point;
                button.onClick.AddListener(() => HandleTeleportationMarkerClicked(clickedPoint));

                teleportationMarkers.Add(new TeleportationMarker(point, strongholdId, rect, image, button));
            }

            Log.Info("[MinimapUI] Teleportation markers created. level={0}, count={1}.", level.name, teleportationMarkers.Count);
            teleportationMarkersInitialized = true;
        }

        private void RefreshTeleportationMarkerStates()
        {
            if (teleportationMarkers.Count == 0 || !LogicStrongholdMap.IsInitialized)
                return;

            float size = isLargeMap ? TeleportationMarkerLargeSize : TeleportationMarkerSmallSize;
            for (int i = 0; i < teleportationMarkers.Count; i++)
            {
                TeleportationMarker marker = teleportationMarkers[i];
                bool isPlayerOwned = LogicStrongholdMap.GetOwnerFactionIdRequired(marker.StrongholdId)
                                     == EntitySideHelper.PlayerFactionId;
                bool isClickable = isLargeMap
                                   && isPlayerOwned
                                   && !TeleportationPointService.IsStrongholdTeleportBlocked(marker.StrongholdId);
                marker.Rect.gameObject.SetActive(isPlayerOwned);
                marker.Button.interactable = isClickable;
                marker.Image.raycastTarget = isClickable;
                if (!isPlayerOwned)
                    continue;

                marker.Rect.sizeDelta = new Vector2(size, size);
                marker.Rect.anchoredPosition = WorldToMinimapPosition(marker.Point.Position);
            }

            SetTeleportationMarkersAsLastSibling();
        }

        private void HandleTeleportationMarkerClicked(EntityPresetPoint point)
        {
            if (!isLargeMap || teleportationPointClicked == null)
                throw new InvalidOperationException("Teleportation marker click requires an open large map.");
            if (!TeleportationPointService.IsPlayerOwned(point))
                throw new InvalidOperationException($"Teleportation marker '{point.name}' is no longer player-owned.");

            teleportationPointClicked(point);
        }

        private void SetTeleportationMarkersAsLastSibling()
        {
            for (int i = 0; i < teleportationMarkers.Count; i++)
            {
                RectTransform marker = teleportationMarkers[i].Rect;
                if (marker != null)
                    marker.SetAsLastSibling();
            }
        }

        private void ClearTeleportationMarkers()
        {
            for (int i = 0; i < teleportationMarkers.Count; i++)
            {
                RectTransform marker = teleportationMarkers[i].Rect;
                if (marker != null)
                    Destroy(marker.gameObject);
            }

            teleportationMarkers.Clear();
            teleportationMarkerLevelEntityId = 0;
            teleportationMarkersInitialized = false;
        }

        private Vector2 WorldToMinimapPosition(Vector3 worldPos)
        {
            if (TryWorldToMinimapByTileGrid(worldPos, out Vector2 minimapPosByGrid))
            {
                return minimapPosByGrid;
            }

            if (minimapManager == null) return Vector2.zero;
            MinimapConfig cfg = minimapManager.Config;
            Vector2 mapSize = GetMinimapRenderSize();
            float nx = Mathf.InverseLerp(cfg.WorldMinX, cfg.WorldMaxX, worldPos.x);
            float nz = Mathf.InverseLerp(cfg.WorldMinZ, cfg.WorldMaxZ, worldPos.z);
            return new Vector2((nx - 0.5f) * mapSize.x, (nz - 0.5f) * mapSize.y);
        }

        private bool TryWorldToMinimapByTileGrid(Vector3 worldPos, out Vector2 minimapPos)
        {
            minimapPos = Vector2.zero;

            TileWorldCreatorManager twcManager = terrainMapTileWorldCreatorManager;
            if (twcManager == null)
            {
                LevelEntity levelEntity = LevelEntity.ActiveLevelEntity;
                if (levelEntity != null)
                {
                    twcManager = levelEntity.GetComponentInChildren<TileWorldCreatorManager>();
                }
            }

            if (twcManager == null || twcManager.configuration == null)
            {
                return false;
            }

            int width = terrainGridWidth > 0 ? terrainGridWidth : Mathf.Max(1, twcManager.configuration.width);
            int height = terrainGridHeight > 0 ? terrainGridHeight : Mathf.Max(1, twcManager.configuration.height);
            float cellSize = terrainCellSize > 0f ? terrainCellSize : Mathf.Max(0.01f, twcManager.configuration.cellSize);

            Vector3 localPos = twcManager.transform.InverseTransformPoint(worldPos);
            float gridX = localPos.x / cellSize;
            float gridY = localPos.z / cellSize;

            if (terrainMapUseCenteredGrid)
            {
                gridX += width * 0.5f;
                gridY += height * 0.5f;
            }

            float nx = gridX / Mathf.Max(1f, width);
            float ny = gridY / Mathf.Max(1f, height);
            Vector2 mapSize = GetMinimapRenderSize();
            minimapPos = new Vector2((nx - 0.5f) * mapSize.x, (ny - 0.5f) * mapSize.y);
            return true;
        }

        private Vector2 GetMinimapRenderSize()
        {
            RectTransform content = GetMinimapContent();
            if (content != null)
            {
                Rect rect = content.rect;
                float width = Mathf.Abs(rect.width);
                float height = Mathf.Abs(rect.height);
                if (width > 0.01f && height > 0.01f)
                {
                    return new Vector2(width, height);
                }
            }

            return new Vector2(minimapSize, minimapSize);
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
            float groundY = ResolveCameraFrameGroundY();

            Plane ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));

            if (mainCamera.orthographic)
            {
                Vector3 centerWorld;
                if (!TryGetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), ground, out centerWorld))
                {
                    centerWorld = mainCamera.transform.position;
                    centerWorld.y = groundY;
                }

                float halfWidth = mainCamera.orthographicSize * mainCamera.aspect;
                float halfHeight = mainCamera.orthographicSize;

                Vector3 rightXZ = Vector3.ProjectOnPlane(mainCamera.transform.right, Vector3.up);
                if (rightXZ.sqrMagnitude <= 0.0001f)
                {
                    rightXZ = Vector3.right;
                }
                else
                {
                    rightXZ.Normalize();
                }

                Vector3 forwardXZ = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
                if (forwardXZ.sqrMagnitude <= 0.0001f)
                {
                    forwardXZ = Vector3.forward;
                }
                else
                {
                    forwardXZ.Normalize();
                }

                Vector3 orthoBL = centerWorld - rightXZ * halfWidth - forwardXZ * halfHeight;
                Vector3 orthoBR = centerWorld + rightXZ * halfWidth - forwardXZ * halfHeight;
                Vector3 orthoTL = centerWorld - rightXZ * halfWidth + forwardXZ * halfHeight;
                Vector3 orthoTR = centerWorld + rightXZ * halfWidth + forwardXZ * halfHeight;

                Vector2 orthoMbl = WorldToMinimapPosition(orthoBL);
                Vector2 orthoMbr = WorldToMinimapPosition(orthoBR);
                Vector2 orthoMtl = WorldToMinimapPosition(orthoTL);
                Vector2 orthoMtr = WorldToMinimapPosition(orthoTR);

                Vector2 orthoPosition = (orthoMbl + orthoMbr + orthoMtl + orthoMtr) * 0.25f;
                float orthoMinX = Mathf.Min(Mathf.Min(orthoMbl.x, orthoMbr.x), Mathf.Min(orthoMtl.x, orthoMtr.x));
                float orthoMaxX = Mathf.Max(Mathf.Max(orthoMbl.x, orthoMbr.x), Mathf.Max(orthoMtl.x, orthoMtr.x));
                float orthoMinY = Mathf.Min(Mathf.Min(orthoMbl.y, orthoMbr.y), Mathf.Min(orthoMtl.y, orthoMtr.y));
                float orthoMaxY = Mathf.Max(Mathf.Max(orthoMbl.y, orthoMbr.y), Mathf.Max(orthoMtl.y, orthoMtr.y));
                Vector2 orthoSize = new Vector2(Mathf.Abs(orthoMaxX - orthoMinX), Mathf.Abs(orthoMaxY - orthoMinY));
                orthoSize *= cameraFrameSizeScale * CameraFrameAdditionalScale;
                orthoSize = ApplyCameraFrameAspect(orthoSize);

                cameraFrame.UpdateFrame(orthoPosition, orthoSize);
                return;
            }

            Vector3 bl = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(0, 0, 0)), ground);
            Vector3 br = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(1, 0, 0)), ground);
            Vector3 tl = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(0, 1, 0)), ground);
            Vector3 tr = GetGroundIntersection(mainCamera.ViewportPointToRay(new Vector3(1, 1, 0)), ground);

            Vector2 mbl = WorldToMinimapPosition(bl);
            Vector2 mbr = WorldToMinimapPosition(br);
            Vector2 mtl = WorldToMinimapPosition(tl);
            Vector2 mtr = WorldToMinimapPosition(tr);

            Vector2 position = (mbl + mbr + mtl + mtr) * 0.25f;
            float minX = Mathf.Min(Mathf.Min(mbl.x, mbr.x), Mathf.Min(mtl.x, mtr.x));
            float maxX = Mathf.Max(Mathf.Max(mbl.x, mbr.x), Mathf.Max(mtl.x, mtr.x));
            float minY = Mathf.Min(Mathf.Min(mbl.y, mbr.y), Mathf.Min(mtl.y, mtr.y));
            float maxY = Mathf.Max(Mathf.Max(mbl.y, mbr.y), Mathf.Max(mtl.y, mtr.y));
            Vector2 size = new Vector2(Mathf.Abs(maxX - minX), Mathf.Abs(maxY - minY));
            size *= cameraFrameSizeScale * CameraFrameAdditionalScale;
            size = ApplyCameraFrameAspect(size);

            cameraFrame.UpdateFrame(position, size);
        }

        private static Vector2 ApplyCameraFrameAspect(Vector2 size)
        {
            float height = Mathf.Max(0f, size.y);
            if (height <= 0.01f)
            {
                return size;
            }

            size.x = height * CameraFrameAspect;
            return size;
        }

        private bool EnsureCameraFrame()
        {
            if (cameraFrame != null)
            {
                cameraFrame.SetBounds(GetMinimapContent());
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
                cameraFrame.SetBounds(GetMinimapContent());
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

        private bool TryGetGroundIntersection(Ray ray, Plane plane, out Vector3 point)
        {
            float enter;
            if (plane.Raycast(ray, out enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        private float ResolveCameraFrameGroundY()
        {
            if (useHeroYForFrameGround && EntityRegistry.Player != null)
            {
                return EntityRegistry.Player.Position.y + cameraFrameGroundYOffset;
            }

            float groundY = 0f;
            if (terrainMapTileWorldCreatorManager != null)
            {
                groundY = terrainMapTileWorldCreatorManager.transform.position.y;
            }

            return groundY + cameraFrameGroundYOffset;
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
            if (!showTerrainMap || GetMinimapContent() == null || minimapManager == null)
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

            MinimapTerrainMapBuildResult terrainMapBuildResult = MinimapTerrainMapBuilder.Build(
                tileWorldCreatorManager.configuration,
                terrainTextureMaxSize,
                groundLayerKeyword,
                waterLayerKeyword,
                minimapManager.PlaneLayerColor,
                minimapManager.WaterLayerColor,
                new Color32(0, 0, 0, 255));
            if (terrainMapBuildResult == null)
            {
                return;
            }

            terrainGridWidth = terrainMapBuildResult.GridWidth;
            terrainGridHeight = terrainMapBuildResult.GridHeight;
            terrainCellSize = terrainMapBuildResult.CellSize;
            UpdateMinimapContentLayout();

            EnsureTerrainMapImage();

            if (terrainMapTexture != null)
            {
                Destroy(terrainMapTexture);
                terrainMapTexture = null;
            }
            terrainMapTexture = terrainMapBuildResult.Texture;
            terrainMapUseCenteredGrid = terrainMapBuildResult.UseCenteredGrid;

            terrainMapImage.texture = terrainMapTexture;
            terrainMapImage.color = Color.white;
            terrainMapImage.raycastTarget = false;
            UpdateOverlaySiblingOrder();

            terrainMapLevelEntityId = terrainMapBuildResult.IsTerrainReady ? levelEntityId : 0;
        }

        private void EnsureTerrainMapImage()
        {
            RectTransform content = GetMinimapContent();
            if (content == null)
                return;

            if (terrainMapImage == null)
            {
                Transform terrainMapTransform = content.Find("TerrainMap");
                if (terrainMapTransform != null)
                {
                    terrainMapImage = terrainMapTransform.GetComponent<RawImage>();
                }

                if (terrainMapImage == null)
                {
                    GameObject terrainMapObject = new GameObject("TerrainMap", typeof(RectTransform), typeof(RawImage));
                    terrainMapObject.transform.SetParent(content, false);
                    terrainMapImage = terrainMapObject.GetComponent<RawImage>();
                }
            }

            RectTransform rt = terrainMapImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            UpdateOverlaySiblingOrder();
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

        private sealed class TeleportationMarker
        {
            public TeleportationMarker(
                EntityPresetPoint point,
                string strongholdId,
                RectTransform rect,
                Image image,
                Button button)
            {
                Point = point;
                StrongholdId = strongholdId;
                Rect = rect;
                Image = image;
                Button = button;
            }

            public EntityPresetPoint Point { get; }
            public string StrongholdId { get; }
            public RectTransform Rect { get; }
            public Image Image { get; }
            public Button Button { get; }
        }

    }

    [System.Serializable]
    public class BuildingIconMapping
    {
        public string iconName;
        public GameObject prefab;
    }
}
