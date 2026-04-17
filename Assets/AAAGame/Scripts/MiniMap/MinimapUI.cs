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

        [Header("地形显示")]
        [SerializeField] private bool showTerrainMap = true;
        [SerializeField, Range(64, 512)] private int terrainTextureMaxSize = 256;
        [SerializeField] private string groundLayerKeyword = "Plane";
        [SerializeField] private string waterLayerKeyword = "Water";
        [SerializeField] private Color groundLayerColor = new Color(0.42f, 0.45f, 0.33f, 1f);
        [SerializeField] private Color waterLayerColor = new Color(0.14f, 0.36f, 0.52f, 1f);

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
        private HashSet<int> knownBuildingUnitIds = new HashSet<int>();
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
            RefreshMinimapFogOverlay(true, 0f);
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
            knownBuildingUnitIds.Clear();
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

            RefreshMinimapFogOverlay(false, realElapseSeconds);
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

            List<int> toRemove = new List<int>();
            foreach (var id in unitVisuals.Keys)
            {
                if (!currentUnitIds.Contains(id)) toRemove.Add(id);
            }
            foreach (var id in toRemove) RemoveUnitVisual(id);

            if (knownBuildingUnitIds.Count > 0)
            {
                List<int> staleBuildingIds = null;
                foreach (int buildingId in knownBuildingUnitIds)
                {
                    if (snapshotUnitIds.Contains(buildingId))
                        continue;

                    staleBuildingIds ??= new List<int>();
                    staleBuildingIds.Add(buildingId);
                }

                if (staleBuildingIds != null)
                {
                    for (int i = 0; i < staleBuildingIds.Count; i++)
                        knownBuildingUnitIds.Remove(staleBuildingIds[i]);
                }
            }

            RefreshMinimapFogOverlay(false, 0f);
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
            if (!showMinimapFogSync || minimapContainer == null)
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
            if (fogMapImage == null)
            {
                Transform fogMapTransform = minimapContainer.Find("FogMap");
                if (fogMapTransform != null)
                    fogMapImage = fogMapTransform.GetComponent<RawImage>();

                if (fogMapImage == null)
                {
                    GameObject fogMapObject = new GameObject("FogMap", typeof(RectTransform), typeof(RawImage));
                    fogMapObject.transform.SetParent(minimapContainer, false);
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
            if (minimapContainer == null)
                return;

            if (terrainMapImage != null)
                terrainMapImage.rectTransform.SetSiblingIndex(0);

            if (fogMapImage != null)
            {
                int fogSiblingIndex = minimapContainer.childCount > 1 ? 1 : 0;
                fogMapImage.rectTransform.SetSiblingIndex(fogSiblingIndex);
            }
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
            if (minimapContainer != null)
            {
                Rect rect = minimapContainer.rect;
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
                float orthoMinX = Mathf.Min(orthoMbl.x, orthoMbr.x, orthoMtl.x, orthoMtr.x);
                float orthoMaxX = Mathf.Max(orthoMbl.x, orthoMbr.x, orthoMtl.x, orthoMtr.x);
                float orthoMinY = Mathf.Min(orthoMbl.y, orthoMbr.y, orthoMtl.y, orthoMtr.y);
                float orthoMaxY = Mathf.Max(orthoMbl.y, orthoMbr.y, orthoMtl.y, orthoMtr.y);
                Vector2 orthoSize = new Vector2(Mathf.Abs(orthoMaxX - orthoMinX), Mathf.Abs(orthoMaxY - orthoMinY));
                orthoSize *= cameraFrameSizeScale;

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
            float minX = Mathf.Min(mbl.x, mbr.x, mtl.x, mtr.x);
            float maxX = Mathf.Max(mbl.x, mbr.x, mtl.x, mtr.x);
            float minY = Mathf.Min(mbl.y, mbr.y, mtl.y, mtr.y);
            float maxY = Mathf.Max(mbl.y, mbr.y, mtl.y, mtr.y);
            Vector2 size = new Vector2(Mathf.Abs(maxX - minX), Mathf.Abs(maxY - minY));
            size *= cameraFrameSizeScale;

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
            float cellSize = Mathf.Max(0.01f, tileWorldCreatorManager.configuration.cellSize);
            terrainGridWidth = gridWidth;
            terrainGridHeight = gridHeight;
            terrainCellSize = cellSize;
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

            HashSet<Vector2> coordinateSamplePositions = new HashSet<Vector2>();
            coordinateSamplePositions.UnionWith(groundPositions);
            coordinateSamplePositions.UnionWith(waterPositions);
            if (coordinateSamplePositions.Count == 0)
            {
                coordinateSamplePositions = CollectAllBlueprintCells(tileWorldCreatorManager.configuration);
            }
            terrainMapUseCenteredGrid = DetermineGridCoordinateMode(coordinateSamplePositions, gridWidth, gridHeight, texWidth, texHeight);

            int paintedCount = 0;
            int waterPaintedCount = PaintLayerCells(pixels, texWidth, texHeight, gridWidth, gridHeight, waterPositions, waterLayerColor, terrainMapUseCenteredGrid);
            int groundPaintedCount = PaintLayerCells(pixels, texWidth, texHeight, gridWidth, gridHeight, groundPositions, groundLayerColor, terrainMapUseCenteredGrid);
            paintedCount += waterPaintedCount + groundPaintedCount;

            if (paintedCount == 0)
            {
                HashSet<Vector2> fallbackPositions = CollectAllBlueprintCells(tileWorldCreatorManager.configuration);
                paintedCount += PaintLayerCells(pixels, texWidth, texHeight, gridWidth, gridHeight, fallbackPositions, groundLayerColor, terrainMapUseCenteredGrid);
            }

            terrainMapTexture.SetPixels32(pixels);
            terrainMapTexture.Apply(false, false);

            terrainMapImage.texture = terrainMapTexture;
            terrainMapImage.color = Color.white;
            terrainMapImage.raycastTarget = false;
            UpdateOverlaySiblingOrder();

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
            UpdateOverlaySiblingOrder();
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

        private int PaintLayerCells(Color32[] pixels, int texWidth, int texHeight, int gridWidth, int gridHeight, HashSet<Vector2> positions, Color color, bool centeredGrid)
        {
            if (positions == null || positions.Count == 0)
            {
                return 0;
            }

            Color32 pixelColor = color;
            int painted = 0;

            foreach (Vector2 cellPos in positions)
            {
                if (TryConvertCellToTexture(cellPos, gridWidth, gridHeight, texWidth, texHeight, centeredGrid, out int texX, out int texY))
                {
                    int idx = texX + texY * texWidth;
                    pixels[idx] = pixelColor;
                    painted++;
                }
            }

            return painted;
        }

        private bool DetermineGridCoordinateMode(HashSet<Vector2> samplePositions, int gridWidth, int gridHeight, int texWidth, int texHeight)
        {
            if (samplePositions == null || samplePositions.Count == 0)
            {
                return terrainMapUseCenteredGrid;
            }

            int normalPaintable = CountPaintableCells(samplePositions, gridWidth, gridHeight, texWidth, texHeight, false);
            int centeredPaintable = CountPaintableCells(samplePositions, gridWidth, gridHeight, texWidth, texHeight, true);
            return centeredPaintable > normalPaintable;
        }

        private int CountPaintableCells(HashSet<Vector2> positions, int gridWidth, int gridHeight, int texWidth, int texHeight, bool centeredGrid)
        {
            int count = 0;
            foreach (Vector2 cellPos in positions)
            {
                if (TryConvertCellToTexture(cellPos, gridWidth, gridHeight, texWidth, texHeight, centeredGrid, out _, out _))
                {
                    count++;
                }
            }

            return count;
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
