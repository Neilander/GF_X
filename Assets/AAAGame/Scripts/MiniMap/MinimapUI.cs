using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using TMPro;

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

        [Header("建筑图标预制体字典")]
        [SerializeField] private List<BuildingIconMapping> buildingIconMappings = new List<BuildingIconMapping>();

        private MinimapManager minimapManager;
        private Dictionary<int, RectTransform> unitVisuals = new Dictionary<int, RectTransform>();
        private Dictionary<string, GameObject> buildingIconPrefabs = new Dictionary<string, GameObject>();
        private HashSet<int> currentUnitIds = new HashSet<int>();
        private bool useNewCameraFrame; // 是否使用新组件

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            minimapManager = GameEntry.GetComponent<MinimapManager>();

            if (minimapManager == null)
            {
                Log.Error("[MinimapUI] MinimapManager not found!");
                return;
            }

            // 检测使用哪种摄像机视野框方式
            useNewCameraFrame = (cameraFrame != null);

            if (useNewCameraFrame)
            {
                Log.Info("[MinimapUI] Using new MinimapCameraFrame component");
                cameraFrame.ForceRefresh();
            }
            else if (cameraViewFrame != null)
            {
                Log.Info("[MinimapUI] Using legacy RectTransform camera frame");
            }
            else
            {
                Log.Warning("[MinimapUI] No camera frame configured!");
            }

            foreach (var mapping in buildingIconMappings)
            {
                if (!string.IsNullOrEmpty(mapping.iconName) && mapping.prefab != null)
                {
                    buildingIconPrefabs[mapping.iconName] = mapping.prefab;
                }
            }

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
            Log.Info($"[MinimapUI] Creating visual for unit {unit.UnitId}, Side={unit.Side}, Type={unit.UnitType}");

            GameObject visualObj = null;

            if (unit.UnitType == MinimapUnitType.Soldier)
            {
                visualObj = soldierDotPrefab != null ?
                    Instantiate(soldierDotPrefab, minimapContainer) :
                    new GameObject($"Soldier_{unit.UnitId}");

                if (soldierDotPrefab == null)
                    visualObj.transform.SetParent(minimapContainer, false);

                Image img = visualObj.GetComponent<Image>();
                if (img == null) img = visualObj.AddComponent<Image>();

                Color soldierColor = minimapManager.Config.GetSoldierColor(unit.Side);
                img.color = soldierColor;
                Log.Info($"[MinimapUI] Soldier dot created with color: {soldierColor} for Side={unit.Side}");

                RectTransform rt = visualObj.GetComponent<RectTransform>();
                if (rt == null) rt = visualObj.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(minimapManager.Config.SoldierDotSize, minimapManager.Config.SoldierDotSize);
            }
            else
            {
                if (!string.IsNullOrEmpty(unit.IconPrefabName) && buildingIconPrefabs.ContainsKey(unit.IconPrefabName))
                    visualObj = Instantiate(buildingIconPrefabs[unit.IconPrefabName], minimapContainer);
                else
                {
                    visualObj = new GameObject($"Building_{unit.UnitId}");
                    visualObj.transform.SetParent(minimapContainer, false);
                    visualObj.AddComponent<Image>().color = minimapManager.Config.GetSoldierColor(unit.Side);
                }

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

            if (unit.UnitType == MinimapUnitType.Soldier)
            {
                Image img = rt.GetComponent<Image>();
                if (img != null) img.color = minimapManager.Config.GetSoldierColor(unit.Side);
            }
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
            // 每秒打印一次调试信息（避免日志过多）
            bool shouldLog = Time.frameCount % 60 == 0;

            // 检查是否有任何视野框配置
            if (!useNewCameraFrame && cameraViewFrame == null)
            {
                if (shouldLog) Log.Warning("[MinimapUI] No camera frame configured!");
                return;
            }

            if (minimapManager == null)
            {
                if (shouldLog) Log.Warning("[MinimapUI] minimapManager is null!");
                return;
            }

            // 每帧动态查找主摄像机（解决场景切换问题）
            if (mainCamera == null || !mainCamera.gameObject.activeInHierarchy)
            {
                mainCamera = Camera.main;
                if (mainCamera != null && shouldLog)
                {
                    Log.Info($"[MinimapUI] Found main camera: {mainCamera.name}");
                }
            }

            if (mainCamera == null)
            {
                if (shouldLog) Log.Warning("[MinimapUI] mainCamera not found!");
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

            // 使用新组件或旧方式
            if (useNewCameraFrame)
            {
                cameraFrame.UpdateFrame(position, size);

                if (shouldLog)
                {
                    Log.Info($"[MinimapUI] Camera frame (new) updated: pos={position}, size={size}");
                }
            }
            else
            {
                cameraViewFrame.anchoredPosition = position;
                cameraViewFrame.sizeDelta = size;

                // 确保视野框可见
                if (!cameraViewFrame.gameObject.activeSelf)
                {
                    cameraViewFrame.gameObject.SetActive(true);
                    Log.Info($"[MinimapUI] Camera view frame activated at pos={position}, size={size}");
                }

                if (shouldLog)
                {
                    //Log.Info($"[MinimapUI] Camera view frame (legacy) updated: pos={position}, size={size}, active={cameraViewFrame.gameObject.activeSelf}");
                }
            }
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
    }

    [System.Serializable]
    public class BuildingIconMapping
    {
        public string iconName;
        public GameObject prefab;
    }
}
