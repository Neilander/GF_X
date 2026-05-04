using System.Collections.Generic;
using System;
using System.Collections;
using System.Globalization;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3Manager : GameFrameworkComponent
    {
        private const string HeroVisionRadiusConfigKey = "HeroVisionRadius";
        private const string UnitVisionRadiusConfigKey = "UnitVisionRadius";
        private const string BuildingVisionRadiusConfigKey = "BuildingVisionRadius";

        [Header("地形检测")]
        [Tooltip("地形检测和可行走区域配置。")]
        [SerializeField] private Fog3TerrainSettings terrainSettings = new Fog3TerrainSettings();

        [Header("视野参数")]
        [Tooltip("当前玩家默认可视半径。")]
        [SerializeField] private float currentPlayerVisionRadius = 28f;
        [Tooltip("我方普通单位默认可视半径（仅配置缺失时使用，正常读取 GameConfig.UnitVisionRadius）。")]
        [SerializeField] private float playerSideUnitVisionRadius = 21f;
        [Tooltip("我方建筑默认可视半径（仅配置缺失时使用，正常读取 GameConfig.BuildingVisionRadius）。")]
        [SerializeField] private float buildingVisionRadius = 21f;
        [Tooltip("自动把 GF_X 里的我方实体注册为迷雾可视源。")]
        [SerializeField] private bool autoRegisterPlayerSideEntities = true;
        [Tooltip("迷雾可视区域刷新间隔，单位秒。")]
        [SerializeField] private float updateInterval = 0.08f;
        [Tooltip("可视区域边缘柔化宽度。")]
        [SerializeField] private float softEdgeWidth = 2f;
        [Tooltip("是否启用射线遮挡视野。")]
        [SerializeField] private bool useLineOfSight;
        [Tooltip("视野射线遮挡检测使用的 Layer。")]
        [SerializeField] private LayerMask lineOfSightOccluderMask;
        [Tooltip("视野射线起点高度。")]
        [SerializeField] private float lineOfSightEyeHeight = 1f;
        [Tooltip("是否启用敌方据点遮挡 hidden 区视野扩散。")]
        [SerializeField] private bool enableEnemyStrongholdHiddenVisionBlock = true;

        [Header("显示效果")]
        [Tooltip("是否创建场景中的世界空间迷雾遮罩。")]
        [SerializeField] private bool createWorldOverlay = true;
        [Tooltip("迷雾显示、云层、摄像机角度补偿、外侧遮罩等配置。")]
        [SerializeField] private Fog3ViewSettings viewSettings = new Fog3ViewSettings();

        [Header("GF_X 场景流程")]
        [Tooltip("从 Launch 切换到玩法场景时保留 FOG3System。")]
        [SerializeField] private bool persistAcrossSceneLoads = true;
        [Tooltip("是否等待指定玩法场景加载后再检测 TileWorld/GridBased 地形。")]
        [SerializeField] private bool waitForGameplayScene = true;
        [Tooltip("包含 TileWorld/GridBased 关卡的玩法场景名。")]
        [SerializeField] private string gameplaySceneName = "Game";
        [Tooltip("玩法场景或关卡实体加载后是否重建迷雾。")]
        [SerializeField] private bool rebuildOnSceneLoaded = true;
        [Tooltip("玩法场景可用时立即尝试初始化迷雾，减少先看到完整场景的空窗。")]
        [SerializeField] private bool fastRebuildOnGameplaySceneAvailable = true;
        [Tooltip("玩法场景已进入但 TileWorld/GridBased 地形还没生成出来时，重新检测地形的间隔。数值越小，迷雾越快跟上关卡生成。")]
        [SerializeField] private float terrainRetryInterval = 0.05f;
        [Tooltip("兜底重建前等待的帧数。立即初始化失败时才主要依赖它。")]
        [SerializeField] private int sceneRebuildFrameDelay;
        [Tooltip("兜底重建前等待的真实时间。立即初始化失败时才主要依赖它。")]
        [SerializeField] private float sceneRebuildDelay;

        private readonly Dictionary<int, int> entityRevealers = new Dictionary<int, int>();
        private readonly Dictionary<Transform, int> transformRevealers = new Dictionary<Transform, int>();
        private readonly Dictionary<int, Fog3EntityVisibilityState> enemyVisibilityStates = new Dictionary<int, Fog3EntityVisibilityState>();
        private readonly HashSet<int> updatedEnemyVisibilityIds = new HashSet<int>();
        private readonly List<int> staleEnemyVisibilityIds = new List<int>();
        private Fog3Controller controller;
        private Fog3TerrainInfo currentTerrainInfo;
        private Fog3WorldOverlayView overlayView;
        private Coroutine sceneRebuildCoroutine;
        private bool gfEventsSubscribed;
        private bool unitySceneEventsSubscribed;
        private bool isInitialized;
        private float currentOverlayHeight;
        private float nextInitializeRetryTime;
        private bool missingTerrainLogged;
        private float updateTimer;
        private float cloudHeightRefreshTimer;
        private bool cloudReferenceHeightResolved;
        private float cloudReferenceWorldY;
        private Vector3 currentOverlayWorldOffset;

        public static Fog3Manager Instance { get; private set; }
        public Fog3Controller Controller => controller;
        public Fog3MapData MapData => controller?.MapData;
        public bool IsInitialized => isInitialized;

        public void LogDiagnostics(string phase)
        {
            Fog3MapData mapData = controller?.MapData;
            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[FOG3Diag] Header phase={0}, initialized={1}, active={2}, controller={3}, map={4}, overlay={5}, createWorldOverlay={6}, surfaceMode={7}, drawOverScene={8}, overlayHeight={9:F3}, overlayOffset={10}, terrain={11}",
                phase,
                isInitialized,
                isActiveAndEnabled,
                controller != null ? "present" : "null",
                mapData != null ? "present" : "null",
                overlayView != null ? "present" : "null",
                createWorldOverlay,
                viewSettings.SurfaceMode,
                viewSettings.DrawOverSceneGeometry,
                currentOverlayHeight,
                FormatVector3(currentOverlayWorldOffset),
                currentTerrainInfo != null
                    ? string.Format(CultureInfo.InvariantCulture, "{0}x{1},cell={2:F3},origin={3},source={4}", currentTerrainInfo.Width, currentTerrainInfo.Height, currentTerrainInfo.CellSize, FormatVector3(currentTerrainInfo.Origin), currentTerrainInfo.SourceName)
                    : "null"));

            if (controller != null)
            {
                controller.GetRevealerDiagnostics(
                    out int revealerTotal,
                    out int revealerActive,
                    out int revealerTargets,
                    out int revealerStatic,
                    out float maxRadius,
                    out int firstActiveId,
                    out int firstActiveEntityId,
                    out Vector3 firstActivePosition);
                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "[FOG3Diag] Revealers total={0}, active={1}, target={2}, static={3}, maxRadius={4:F3}, firstActiveId={5}, firstActiveEntityId={6}, firstActivePos={7}",
                    revealerTotal,
                    revealerActive,
                    revealerTargets,
                    revealerStatic,
                    maxRadius,
                    firstActiveId,
                    firstActiveEntityId,
                    FormatVector3(firstActivePosition)));
            }

            if (mapData != null)
            {
                mapData.GetDiagnostics(
                    out int walkableCount,
                    out int hiddenCount,
                    out int exploredCount,
                    out int visibleCount,
                    out int outsideCount,
                    out float averageVisibility);
                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "[FOG3Diag] Map width={0}, height={1}, cell={2:F3}, origin={3}, walkable={4}, hidden={5}, explored={6}, visible={7}, outside={8}, avgVisibility={9:F4}, dirty={10}",
                    mapData.Width,
                    mapData.Height,
                    mapData.CellSize,
                    FormatVector3(mapData.WorldOrigin),
                    walkableCount,
                    hiddenCount,
                    exploredCount,
                    visibleCount,
                    outsideCount,
                    averageVisibility,
                    mapData.IsDirty));

                if (Camera.main != null)
                    LogMapCell("MainCamera", mapData, Camera.main.transform.position);
            }

            if (overlayView == null)
                return;

            overlayView.GetTextureDiagnostics(
                out int textureWidth,
                out int textureHeight,
                out float averageR,
                out float averageG,
                out float averageB,
                out float averageA,
                out int alphaZero,
                out int alphaLow,
                out int alphaMid,
                out int alphaHigh,
                out int alphaFull);
            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[FOG3Diag] Overlay texture={0}x{1}, avg=({2:F4},{3:F4},{4:F4},{5:F4}), alphaZero={6}, alphaLow={7}, alphaMid={8}, alphaHigh={9}, alphaFull={10}, meshBoundsCenter={11}, meshBoundsSize={12}, outsideQuads={13}, cameraProjectionGrid={14}",
                textureWidth,
                textureHeight,
                averageR,
                averageG,
                averageB,
                averageA,
                alphaZero,
                alphaLow,
                alphaMid,
                alphaHigh,
                alphaFull,
                FormatVector3(overlayView.FogMeshBounds.center),
                FormatVector3(overlayView.FogMeshBounds.size),
                overlayView.OutsideMaskQuadCount,
                overlayView.FogMeshUsesCameraProjectionGrid));

            LogMaterialDiagnostics("FogMaterial", overlayView.FogMaterial);
            LogMaterialDiagnostics("OutsideMaterial", overlayView.OutsideMaterial);
        }

        private static void LogMapCell(string label, Fog3MapData mapData, Vector3 worldPosition)
        {
            mapData.TryGetCellDiagnostics(worldPosition, out int gridX, out int gridY, out Fog3CellState state, out float visibility);
            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[FOG3Diag] Cell label={0}, world={1}, grid=({2},{3}), state={4}, visibility={5:F4}",
                label,
                FormatVector3(worldPosition),
                gridX,
                gridY,
                state,
                visibility));
        }

        private static void LogMaterialDiagnostics(string label, Material material)
        {
            if (material == null)
            {
                Log.Info("[FOG3Diag] Material label={0}, material=null", label);
                return;
            }

            Texture mainTexture = null;
            if (material.HasProperty("_MainTex"))
                mainTexture = material.GetTexture("_MainTex");
            else if (material.HasProperty("_BaseMap"))
                mainTexture = material.GetTexture("_BaseMap");

            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[FOG3Diag] Material label={0}, name={1}, shader={2}, shaderSupported={3}, renderQueue={4}, color={5}, srcBlend={6}, dstBlend={7}, zWrite={8}, zTest={9}, mainTex={10}",
                label,
                material.name,
                material.shader != null ? material.shader.name : "null",
                material.shader != null && material.shader.isSupported,
                material.renderQueue,
                FormatColor(ReadMaterialColor(material)),
                ReadMaterialFloat(material, "_SrcBlend"),
                ReadMaterialFloat(material, "_DstBlend"),
                ReadMaterialFloat(material, "_ZWrite"),
                ReadMaterialFloat(material, "_ZTest"),
                mainTexture != null ? string.Format(CultureInfo.InvariantCulture, "{0},{1}x{2}", mainTexture.name, mainTexture.width, mainTexture.height) : "null"));
        }

        private static Color ReadMaterialColor(Material material)
        {
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            return Color.clear;
        }

        private static string ReadMaterialFloat(Material material, string propertyName)
        {
            return material.HasProperty(propertyName)
                ? material.GetFloat(propertyName).ToString("F3", CultureInfo.InvariantCulture)
                : "n/a";
        }

        private static string FormatVector3(Vector3 value)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3},{2:F3})", value.x, value.y, value.z);
        }

        private static string FormatColor(Color value)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4},{2:F4},{3:F4})", value.r, value.g, value.b, value.a);
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != null && Instance != this)
            {
                Log.Warning("[FOG3] Multiple Fog3Manager instances detected. Destroying the duplicate instance.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            controller = new Fog3Controller();

            if (persistAcrossSceneLoads)
                DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            TrySubscribeEvents();
            if (CanInitializeForCurrentScene())
                TryRebuildOrSchedule("FOG3 manager started");
            else
                Log.Info($"[FOG3] Waiting for gameplay scene '{gameplaySceneName}' before terrain detection.");
        }

        private void OnEnable()
        {
            TrySubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            if (sceneRebuildCoroutine != null)
            {
                StopCoroutine(sceneRebuildCoroutine);
                sceneRebuildCoroutine = null;
            }

            ResetEnemyVisibilityStates();
            UnsubscribeEvents();
            if (overlayView != null)
                Destroy(overlayView.gameObject);

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            TrySubscribeEvents();

            if (isInitialized && (controller == null || controller.MapData == null))
                isInitialized = false;

            if (!CanInitializeForCurrentScene())
                return;

            if (!isInitialized && sceneRebuildCoroutine != null)
                return;

            if (!isInitialized)
            {
                if (Time.unscaledTime < nextInitializeRetryTime)
                    return;

                Initialize();
                RegisterExistingRevealers();
                if (!isInitialized || controller == null || controller.MapData == null)
                    return;

                UpdateVisibilityImmediately();
            }

            updateTimer += Time.deltaTime;
            if (updateInterval <= 0f || updateTimer >= updateInterval)
            {
                updateTimer = 0f;
                controller.UpdateVisibility(lineOfSightOccluderMask, lineOfSightEyeHeight, softEdgeWidth, useLineOfSight, enableEnemyStrongholdHiddenVisionBlock);
            }

            RefreshCloudOverlayForCameraIfNeeded();
        }

        public void Initialize()
        {
            EnsureController();
            if (isInitialized && controller.MapData != null)
                return;

            Fog3TerrainInfo terrainInfo = Fog3TerrainDetector.Detect(terrainSettings);
            if (terrainInfo == null)
            {
                ClearRuntimeState(true);
                nextInitializeRetryTime = Time.unscaledTime + Mathf.Max(0.02f, terrainRetryInterval);

                if (!missingTerrainLogged)
                {
                    missingTerrainLogged = true;
                    Log.Info("[FOG3] No TileWorldCreatorManager/GridBased terrain found. Fog overlay is disabled for this scene.");
                }

                return;
            }

            missingTerrainLogged = false;
            nextInitializeRetryTime = 0f;
            currentTerrainInfo = terrainInfo;
            controller.VisibilityUpdated -= OnVisibilityUpdated;
            controller.Initialize(terrainInfo);
            controller.VisibilityUpdated += OnVisibilityUpdated;

            if (createWorldOverlay)
                EnsureOverlayView(terrainInfo);

            isInitialized = true;
            Log.Info($"[FOG3] Initialized from {terrainInfo.SourceName}: {terrainInfo.Width}x{terrainInfo.Height}, cell={terrainInfo.CellSize}");
        }

        public void RebuildTerrain()
        {
            isInitialized = false;
            entityRevealers.Clear();
            transformRevealers.Clear();
            if (controller != null)
                controller.VisibilityUpdated -= OnVisibilityUpdated;
            currentTerrainInfo = null;
            currentOverlayHeight = 0f;
            cloudHeightRefreshTimer = 0f;
            cloudReferenceHeightResolved = false;
            currentOverlayWorldOffset = Vector3.zero;
            Initialize();
            if (!isInitialized)
                return;

            RegisterExistingRevealers();
            UpdateVisibilityImmediately();
        }

        public int RegisterRevealer(Transform target, float visionRadius, int entityId = 0, bool revealerUsesLineOfSight = false, bool allowRevealHidden = true)
        {
            if (!isInitialized || controller == null || target == null)
                return -1;

            if (transformRevealers.TryGetValue(target, out int existingId))
            {
                SetRevealerVisionRadius(existingId, visionRadius);
                SetRevealerAllowRevealHidden(existingId, allowRevealHidden);
                if (entityId != 0)
                    entityRevealers[entityId] = existingId;
                return existingId;
            }

            int id = controller.RegisterRevealer(target, visionRadius, entityId, revealerUsesLineOfSight, allowRevealHidden);
            if (id > 0)
            {
                transformRevealers[target] = id;
                if (entityId != 0)
                    entityRevealers[entityId] = id;
            }

            return id;
        }

        public int RegisterStaticRevealer(Vector3 position, float visionRadius, bool revealerUsesLineOfSight = false, bool allowRevealHidden = true)
        {
            if (!isInitialized || controller == null)
                return -1;

            return controller.RegisterRevealer(position, visionRadius, revealerUsesLineOfSight, allowRevealHidden);
        }

        public void UnregisterRevealer(int revealerId)
        {
            if (revealerId <= 0 || controller == null)
                return;

            controller.UnregisterRevealer(revealerId);
            RemoveRevealerReferences(revealerId);
        }

        public void SetRevealerVisionRadius(int revealerId, float visionRadius)
        {
            if (controller != null && controller.TryGetRevealer(revealerId, out Fog3RevealerData revealer))
                revealer.SetVisionRadius(visionRadius);
        }

        public void SetRevealerAllowRevealHidden(int revealerId, bool allowRevealHidden)
        {
            if (controller == null || revealerId <= 0)
                return;

            if (!controller.TryGetRevealer(revealerId, out Fog3RevealerData revealer))
                return;

            if (revealer.AllowRevealHidden == allowRevealHidden)
                return;

            revealer.SetAllowRevealHidden(allowRevealHidden);
            Log.Info("[FOG3] Revealer hidden-visibility updated. revealerId={0}, allowHidden={1}", revealerId, allowRevealHidden);
        }

        public void SetEntityRevealerAllowRevealHidden(int entityId, bool allowRevealHidden)
        {
            if (entityId == 0)
                return;

            if (!entityRevealers.TryGetValue(entityId, out int revealerId))
                return;

            SetRevealerAllowRevealHidden(revealerId, allowRevealHidden);
        }

        public bool IsPositionVisible(Vector3 worldPos)
        {
            return controller != null && controller.IsPositionVisible(worldPos);
        }

        public bool IsPositionExplored(Vector3 worldPos)
        {
            return controller != null && controller.IsPositionExplored(worldPos);
        }

        public void ResetFog()
        {
            controller?.ResetExploration();
        }

        private void EnsureController()
        {
            if (controller == null)
                controller = new Fog3Controller();
        }

        private bool CanInitializeForCurrentScene()
        {
            if (!waitForGameplayScene)
                return true;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && IsGameplaySceneName(scene.name))
                    return true;
            }

            return false;
        }

        private bool IsGameplaySceneName(string sceneName)
        {
            if (!waitForGameplayScene || string.IsNullOrEmpty(gameplaySceneName))
                return true;

            return string.Equals(sceneName, gameplaySceneName, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSceneNameFromAssetName(string sceneAssetName)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
                return string.Empty;

            string sceneName = SceneComponent.GetSceneName(sceneAssetName);
            if (!string.IsNullOrEmpty(sceneName))
                return sceneName;

            return System.IO.Path.GetFileNameWithoutExtension(sceneAssetName);
        }

        private void ScheduleSceneRebuild(string reason)
        {
            if (!isActiveAndEnabled)
                return;

            if (sceneRebuildCoroutine != null)
                StopCoroutine(sceneRebuildCoroutine);

            sceneRebuildCoroutine = StartCoroutine(RebuildWhenSceneReady(reason));
        }

        private void TryRebuildOrSchedule(string reason)
        {
            if (fastRebuildOnGameplaySceneAvailable && CanInitializeForCurrentScene())
            {
                RebuildTerrain();
                if (isInitialized)
                {
                    Log.Info($"[FOG3] Fast rebuilt after scene became ready: {reason}.");
                    return;
                }
            }

            ScheduleSceneRebuild(reason);
        }

        private IEnumerator RebuildWhenSceneReady(string reason)
        {
            int frameDelay = Mathf.Max(0, sceneRebuildFrameDelay);
            for (int i = 0; i < frameDelay; i++)
                yield return null;

            if (sceneRebuildDelay > 0f)
                yield return new WaitForSecondsRealtime(sceneRebuildDelay);

            sceneRebuildCoroutine = null;
            if (!CanInitializeForCurrentScene())
                yield break;

            RebuildTerrain();
            if (isInitialized)
                Log.Info($"[FOG3] Rebuilt after scene became ready: {reason}.");
            else
                Log.Info($"[FOG3] Scene became ready but no TileWorldCreatorManager/GridBased terrain was found: {reason}.");
        }

        private void ClearRuntimeState(bool destroyOverlay)
        {
            isInitialized = false;
            entityRevealers.Clear();
            transformRevealers.Clear();
            ResetEnemyVisibilityStates();

            if (controller != null)
                controller.VisibilityUpdated -= OnVisibilityUpdated;

            controller = new Fog3Controller();
            currentTerrainInfo = null;
            currentOverlayHeight = 0f;
            cloudHeightRefreshTimer = 0f;
            cloudReferenceHeightResolved = false;
            currentOverlayWorldOffset = Vector3.zero;

            if (destroyOverlay && overlayView != null)
            {
                Destroy(overlayView.gameObject);
                overlayView = null;
            }
        }

        private void EnsureOverlayView(Fog3TerrainInfo terrainInfo)
        {
            if (overlayView == null)
            {
                GameObject viewObject = new GameObject("FOG3_WorldOverlayView");
                viewObject.transform.SetParent(null, false);
                overlayView = viewObject.AddComponent<Fog3WorldOverlayView>();
            }
            else if (overlayView.transform.parent != null)
            {
                overlayView.transform.SetParent(null, true);
            }

            currentOverlayHeight = ResolveOverlayHeight(terrainInfo);
            currentOverlayWorldOffset = ResolveOverlayWorldOffset(terrainInfo, currentOverlayHeight);
            overlayView.Build(terrainInfo, viewSettings, currentOverlayHeight, ResolveHeightSampleMask(), currentOverlayWorldOffset);
            overlayView.Render(controller.MapData);
        }

        private LayerMask ResolveHeightSampleMask()
        {
            if (viewSettings.HeightSampleMask.value != 0)
                return viewSettings.HeightSampleMask;

            if (terrainSettings.GroundMask.value != 0)
                return terrainSettings.GroundMask;

            return Physics.DefaultRaycastLayers;
        }

        private Vector3 ResolveOverlayWorldOffset(Fog3TerrainInfo terrainInfo, float overlayLocalHeight)
        {
            Vector3 offset = viewSettings.CloudLayerWorldOffset;
            if (viewSettings.SurfaceMode != Fog3OverlaySurfaceMode.CloudLayer || !viewSettings.UseCameraAngleOffset)
                return offset;

            if (!TryGetReferenceCamera(out Camera referenceCamera))
                return offset;

            Vector3 cameraForward = referenceCamera.transform.forward;
            float verticalDown = -cameraForward.y;
            if (verticalDown <= 0.0001f)
                return offset;

            float overlayWorldY = terrainInfo.Origin.y + overlayLocalHeight;
            float heightDelta = viewSettings.CameraProjectionTargetWorldY - overlayWorldY;
            if (Mathf.Abs(heightDelta) <= 0.0001f)
                return offset;

            Vector3 cameraProjectedDirection = new Vector3(cameraForward.x, 0f, cameraForward.z);
            Vector3 cameraOffset = cameraProjectedDirection * (heightDelta / verticalDown) * viewSettings.CameraAngleOffsetScale;
            cameraOffset.y = 0f;
            return offset + cameraOffset;
        }

        private float ResolveOverlayHeight(Fog3TerrainInfo terrainInfo)
        {
            float minimumLocalHeight = Mathf.Max(0.01f, viewSettings.OverlayHeight);
            if (viewSettings.SurfaceMode == Fog3OverlaySurfaceMode.CloudLayer)
                return ResolveCloudLayerHeight(terrainInfo, minimumLocalHeight);

            if (viewSettings.SurfaceMode == Fog3OverlaySurfaceMode.TerrainConforming)
                return minimumLocalHeight;

            if (viewSettings.DrawOverSceneGeometry || !viewSettings.AutoHeightAboveScene)
                return minimumLocalHeight;

            return ResolveAutoHeightAboveScene(terrainInfo, minimumLocalHeight);
        }

        private float ResolveCloudLayerHeight(Fog3TerrainInfo terrainInfo, float minimumLocalHeight)
        {
            float terrainWorldY = terrainInfo.Origin.y;
            float minimumWorldY = terrainWorldY + minimumLocalHeight;
            float fixedCloudWorldY = ResolveCloudReferenceWorldY(terrainInfo) + minimumLocalHeight;
            float resolvedWorldY = Mathf.Max(minimumWorldY, fixedCloudWorldY);

            if (!viewSettings.DrawOverSceneGeometry || viewSettings.AutoHeightAboveScene)
            {
                float cloudMinimumWorldY = terrainWorldY + Mathf.Max(minimumLocalHeight, viewSettings.CloudLayerMinimumHeight);
                float sceneMaxWorldY = ResolveSceneMaxWorldY(terrainInfo, cloudMinimumWorldY);
                resolvedWorldY = Mathf.Max(resolvedWorldY, sceneMaxWorldY + Mathf.Max(0.01f, viewSettings.AutoHeightPadding));
            }

            if (viewSettings.ClampCloudLayerBelowCamera && TryGetReferenceCamera(out Camera referenceCamera))
                resolvedWorldY = ClampCloudLayerBelowCamera(resolvedWorldY, minimumWorldY, referenceCamera);

            return resolvedWorldY - terrainInfo.Origin.y;
        }

        private float ResolveCloudReferenceWorldY(Fog3TerrainInfo terrainInfo)
        {
            if (cloudReferenceHeightResolved)
                return cloudReferenceWorldY;

            cloudReferenceWorldY = SampleCloudReferenceWorldY(terrainInfo);
            cloudReferenceHeightResolved = true;
            return cloudReferenceWorldY;
        }

        private float SampleCloudReferenceWorldY(Fog3TerrainInfo terrainInfo)
        {
            const int maxSamplesPerAxis = 16;
            List<float> samples = new List<float>(maxSamplesPerAxis * maxSamplesPerAxis);
            int stepX = Mathf.Max(1, Mathf.CeilToInt((float)terrainInfo.Width / maxSamplesPerAxis));
            int stepY = Mathf.Max(1, Mathf.CeilToInt((float)terrainInfo.Height / maxSamplesPerAxis));
            LayerMask sampleMask = ResolveHeightSampleMask();
            float startHeight = Mathf.Max(1f, viewSettings.HeightSampleStartHeight);
            float maxDistance = Mathf.Max(startHeight + 1f, viewSettings.HeightSampleMaxDistance);

            for (int y = 0; y < terrainInfo.Height; y += stepY)
            {
                for (int x = 0; x < terrainInfo.Width; x += stepX)
                {
                    if (!terrainInfo.IsWalkable(x, y))
                        continue;

                    float worldX = terrainInfo.Origin.x + (x + 0.5f) * terrainInfo.CellSize;
                    float worldZ = terrainInfo.Origin.z + (y + 0.5f) * terrainInfo.CellSize;
                    if (TrySampleLowestHeight(worldX, worldZ, terrainInfo.Origin.y, startHeight, maxDistance, sampleMask, out float sampledY))
                        samples.Add(sampledY);
                }
            }

            if (samples.Count == 0)
                return terrainInfo.Origin.y;

            samples.Sort();
            int medianIndex = Mathf.Clamp(samples.Count / 2, 0, samples.Count - 1);
            return samples[medianIndex];
        }

        private static bool TrySampleLowestHeight(float worldX, float worldZ, float originY, float startHeight, float maxDistance, LayerMask sampleMask, out float height)
        {
            Vector3 rayOrigin = new Vector3(worldX, originY + startHeight, worldZ);
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, maxDistance, sampleMask, QueryTriggerInteraction.Ignore);
            height = 0f;
            if (hits == null || hits.Length == 0)
                return false;

            height = hits[0].point.y;
            for (int i = 1; i < hits.Length; i++)
                height = Mathf.Min(height, hits[i].point.y);

            return true;
        }

        private float ClampCloudLayerBelowCamera(float desiredWorldY, float minimumWorldY, Camera referenceCamera)
        {
            float cameraCeiling = referenceCamera.transform.position.y - Mathf.Max(0.1f, viewSettings.CloudCameraClearance);
            if (cameraCeiling <= minimumWorldY)
                return minimumWorldY;

            return Mathf.Min(desiredWorldY, cameraCeiling);
        }

        private float ResolveAutoHeightAboveScene(Fog3TerrainInfo terrainInfo, float minimumLocalHeight)
        {
            float sceneMaxWorldY = ResolveSceneMaxWorldY(terrainInfo, terrainInfo.Origin.y + minimumLocalHeight);
            float resolvedWorldY = sceneMaxWorldY + Mathf.Max(0.01f, viewSettings.AutoHeightPadding);
            return Mathf.Max(minimumLocalHeight, resolvedWorldY - terrainInfo.Origin.y);
        }

        private float ResolveSceneMaxWorldY(Fog3TerrainInfo terrainInfo, float fallbackWorldY)
        {
            float terrainMinX = terrainInfo.Origin.x;
            float terrainMaxX = terrainInfo.Origin.x + terrainInfo.Width * terrainInfo.CellSize;
            float terrainMinZ = terrainInfo.Origin.z;
            float terrainMaxZ = terrainInfo.Origin.z + terrainInfo.Height * terrainInfo.CellSize;
            float maxWorldY = fallbackWorldY;

            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!CanUseRendererForOverlayHeight(renderer, terrainMinX, terrainMaxX, terrainMinZ, terrainMaxZ))
                    continue;

                maxWorldY = Mathf.Max(maxWorldY, renderer.bounds.max.y);
            }

            Collider[] colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (!CanUseColliderForOverlayHeight(collider, terrainMinX, terrainMaxX, terrainMinZ, terrainMaxZ))
                    continue;

                maxWorldY = Mathf.Max(maxWorldY, collider.bounds.max.y);
            }

            return maxWorldY;
        }

        private bool CanUseRendererForOverlayHeight(Renderer renderer, float terrainMinX, float terrainMaxX, float terrainMinZ, float terrainMaxZ)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (overlayView != null && renderer.transform.IsChildOf(overlayView.transform))
                return false;

            if (renderer.GetComponentInParent<Fog3WorldOverlayView>() != null)
                return false;

            Scene scene = renderer.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            if (waitForGameplayScene && !IsGameplaySceneName(scene.name))
                return false;

            Bounds bounds = renderer.bounds;
            return bounds.max.x >= terrainMinX
                   && bounds.min.x <= terrainMaxX
                   && bounds.max.z >= terrainMinZ
                   && bounds.min.z <= terrainMaxZ;
        }

        private bool CanUseColliderForOverlayHeight(Collider collider, float terrainMinX, float terrainMaxX, float terrainMinZ, float terrainMaxZ)
        {
            if (collider == null || collider.isTrigger || !collider.enabled || !collider.gameObject.activeInHierarchy)
                return false;

            if (overlayView != null && collider.transform.IsChildOf(overlayView.transform))
                return false;

            Scene scene = collider.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            if (waitForGameplayScene && !IsGameplaySceneName(scene.name))
                return false;

            Bounds bounds = collider.bounds;
            return bounds.max.x >= terrainMinX
                   && bounds.min.x <= terrainMaxX
                   && bounds.max.z >= terrainMinZ
                   && bounds.min.z <= terrainMaxZ;
        }

        private void RefreshOverlayHeightIfNeeded()
        {
            if (!createWorldOverlay || currentTerrainInfo == null || overlayView == null || controller?.MapData == null)
                return;

            if (viewSettings.SurfaceMode != Fog3OverlaySurfaceMode.CloudLayer && viewSettings.DrawOverSceneGeometry)
                return;

            float nextOverlayHeight = ResolveOverlayHeight(currentTerrainInfo);
            Vector3 nextOverlayWorldOffset = ResolveOverlayWorldOffset(currentTerrainInfo, nextOverlayHeight);
            bool isCloudLayer = viewSettings.SurfaceMode == Fog3OverlaySurfaceMode.CloudLayer;
            if (isCloudLayer)
            {
                bool heightChanged = Mathf.Abs(nextOverlayHeight - currentOverlayHeight) > 0.05f;
                bool offsetChanged = (nextOverlayWorldOffset - currentOverlayWorldOffset).sqrMagnitude > 0.0001f;
                if (!heightChanged && !offsetChanged)
                {
                    overlayView.RefreshCameraProjection();
                    return;
                }

                if (!heightChanged)
                {
                    currentOverlayWorldOffset = nextOverlayWorldOffset;
                    overlayView.SetWorldOffset(currentTerrainInfo, currentOverlayWorldOffset);
                    return;
                }
            }
            else if (nextOverlayHeight <= currentOverlayHeight + 0.05f)
            {
                return;
            }

            currentOverlayHeight = nextOverlayHeight;
            currentOverlayWorldOffset = nextOverlayWorldOffset;
            overlayView.Build(currentTerrainInfo, viewSettings, currentOverlayHeight, ResolveHeightSampleMask(), currentOverlayWorldOffset);
            overlayView.Render(controller.MapData);

            if (isCloudLayer)
                Log.Info($"[FOG3] Adjusted cloud overlay height to {currentOverlayHeight:F2} for the active camera.");
            else
                Log.Info($"[FOG3] Raised overlay height to {currentOverlayHeight:F2} to stay above scene renderers.");
        }

        private void RefreshCloudOverlayForCameraIfNeeded()
        {
            if (!isInitialized || viewSettings.SurfaceMode != Fog3OverlaySurfaceMode.CloudLayer)
                return;

            if (!viewSettings.ClampCloudLayerBelowCamera && !viewSettings.UseCameraAngleOffset)
                return;

            cloudHeightRefreshTimer += Time.deltaTime;
            float refreshInterval = Mathf.Max(0.02f, viewSettings.CloudHeightRefreshInterval);
            if (cloudHeightRefreshTimer < refreshInterval)
                return;

            cloudHeightRefreshTimer = 0f;
            RefreshOverlayHeightIfNeeded();
        }

        private static bool TryGetReferenceCamera(out Camera referenceCamera)
        {
            referenceCamera = Camera.main;
            if (IsUsableCamera(referenceCamera))
                return true;

            Camera[] cameras = Camera.allCameras;
            float bestDepth = float.NegativeInfinity;
            referenceCamera = null;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (!IsUsableCamera(camera) || camera.depth < bestDepth)
                    continue;

                bestDepth = camera.depth;
                referenceCamera = camera;
            }

            return referenceCamera != null;
        }

        private static bool IsUsableCamera(Camera camera)
        {
            return camera != null && camera.isActiveAndEnabled && camera.gameObject.activeInHierarchy;
        }

        private void OnVisibilityUpdated(Fog3MapData mapData)
        {
            if (overlayView != null)
                overlayView.Render(mapData);

            UpdateEnemyVisibilityByFog(mapData);
        }

        private void UpdateVisibilityImmediately()
        {
            if (!isInitialized || controller == null || controller.MapData == null)
                return;

            updateTimer = 0f;
            controller.UpdateVisibility(lineOfSightOccluderMask, lineOfSightEyeHeight, softEdgeWidth, useLineOfSight, enableEnemyStrongholdHiddenVisionBlock);
        }

        private void TrySubscribeEvents()
        {
            if (!unitySceneEventsSubscribed)
            {
                SceneManager.sceneLoaded += OnUnitySceneLoaded;
                SceneManager.activeSceneChanged += OnUnityActiveSceneChanged;
                unitySceneEventsSubscribed = true;
            }

            if (gfEventsSubscribed || GF.Event == null)
                return;

            GF.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            GF.Event.Subscribe(UnloadSceneSuccessEventArgs.EventId, OnUnloadSceneSuccess);
            GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
            GF.Event.Subscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete);
            gfEventsSubscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (unitySceneEventsSubscribed)
            {
                SceneManager.sceneLoaded -= OnUnitySceneLoaded;
                SceneManager.activeSceneChanged -= OnUnityActiveSceneChanged;
                unitySceneEventsSubscribed = false;
            }

            if (gfEventsSubscribed && GF.Event != null)
            {
                GF.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
                GF.Event.Unsubscribe(UnloadSceneSuccessEventArgs.EventId, OnUnloadSceneSuccess);
                GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
                GF.Event.Unsubscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete);
            }

            gfEventsSubscribed = false;
        }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            LoadSceneSuccessEventArgs args = (LoadSceneSuccessEventArgs)e;
            HandleSceneBecameAvailable(GetSceneNameFromAssetName(args.SceneAssetName), "GF_X LoadSceneSuccess");
        }

        private void OnUnloadSceneSuccess(object sender, GameEventArgs e)
        {
            UnloadSceneSuccessEventArgs args = (UnloadSceneSuccessEventArgs)e;
            string sceneName = GetSceneNameFromAssetName(args.SceneAssetName);
            if (IsGameplaySceneName(sceneName))
                ClearRuntimeState(true);
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            HandleSceneBecameAvailable(scene.name, "Unity sceneLoaded");
        }

        private void OnUnityActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            HandleSceneBecameAvailable(newScene.name, "Unity activeSceneChanged");
        }

        private void HandleSceneBecameAvailable(string sceneName, string reason)
        {
            if (!IsGameplaySceneName(sceneName))
                return;

            if (isInitialized && !rebuildOnSceneLoaded)
                return;

            TryRebuildOrSchedule($"{reason}: {sceneName}");
        }

        private void OnShowEntitySuccess(object sender, GameEventArgs e)
        {
            ShowEntitySuccessEventArgs args = (ShowEntitySuccessEventArgs)e;
            if (args.Entity == null || args.Entity.Logic == null)
                return;

            if (IsLevelEntityLogic(args.Entity.Logic))
            {
                TryRebuildOrSchedule("GF_X level entity loaded");
                return;
            }

            if (!autoRegisterPlayerSideEntities || !isInitialized)
                return;

            TryRegisterEntity(args.Entity.Id, args.Entity.Logic);
            RefreshOverlayHeightIfNeeded();
            UpdateVisibilityImmediately();
        }

        private static bool IsLevelEntityLogic(EntityLogic logic)
        {
            return logic != null && string.Equals(logic.GetType().Name, "LevelEntity", StringComparison.Ordinal);
        }

        private void OnHideEntityComplete(object sender, GameEventArgs e)
        {
            HideEntityCompleteEventArgs args = (HideEntityCompleteEventArgs)e;
            if (entityRevealers.TryGetValue(args.EntityId, out int revealerId))
                UnregisterRevealer(revealerId);

            HealthBarComp.Remove(args.EntityId);

            if (enemyVisibilityStates.TryGetValue(args.EntityId, out Fog3EntityVisibilityState hiddenState))
            {
                RestoreEntityVisibilityState(hiddenState);
                enemyVisibilityStates.Remove(args.EntityId);
            }
        }

        private void RegisterExistingRevealers()
        {
            if (!isInitialized)
                return;

            Fog3RevealerComponent[] manualRevealers = UnityEngine.Object.FindObjectsOfType<Fog3RevealerComponent>();
            for (int i = 0; i < manualRevealers.Length; i++)
            {
                Fog3RevealerComponent manualRevealer = manualRevealers[i];
                if (manualRevealer == null || !manualRevealer.AutoRegister || !manualRevealer.isActiveAndEnabled)
                    continue;

                if (manualRevealer.GetComponentInParent<EntityLogic>() != null)
                    continue;

                manualRevealer.RegisterRevealer();
            }

            if (!autoRegisterPlayerSideEntities)
                return;

            EntityLogic[] entityLogics = UnityEngine.Object.FindObjectsOfType<EntityLogic>();
            for (int i = 0; i < entityLogics.Length; i++)
            {
                EntityLogic logic = entityLogics[i];
                if (logic == null || logic.Entity == null)
                    continue;

                TryRegisterEntity(logic.Entity.Id, logic);
            }
        }

        private void TryRegisterEntity(int entityId, EntityLogic logic)
        {
            if (logic == null || entityId == 0 || entityRevealers.ContainsKey(entityId))
                return;

            if (!TryReadEntityVision(logic, out float radius))
                return;

            int revealerId = RegisterRevealer(logic.transform, radius, entityId, false);
            if (revealerId <= 0)
                return;

            SetEntityRevealerAllowRevealHidden(entityId, !IsGhostSoldier(logic));
        }

        private static bool IsGhostSoldier(EntityLogic logic)
        {
            return logic is SoldierEntity soldier && soldier.IsGhostState;
        }

        private bool TryReadEntityVision(EntityLogic logic, out float radius)
        {
            radius = playerSideUnitVisionRadius;
            SideType side = SideType.NoSide;
            BrainType brainType = BrainType.SoldierAI;

            if (logic is EntityBase entityBase && entityBase.Params != null)
            {
                side = entityBase.Params.Side;
                brainType = entityBase.Params.BrainType;
            }

            if (logic is IEntityContext context)
            {
                side = context.Side;
                if (!context.Alive)
                    return false;
            }

            if (logic is SoldierEntity soldier)
                brainType = soldier.BrainType;

            if (logic is PlayerEntity)
            {
                side = SideType.PlayerSide;
                brainType = BrainType.Player;
            }

            if (side != SideType.PlayerSide)
                return false;

            if (logic is BuildingEntity)
                return TryReadVisionRadiusFromConfig(BuildingVisionRadiusConfigKey, buildingVisionRadius, out radius);

            else if (brainType == BrainType.Player || logic is PlayerEntity)
                return TryReadVisionRadiusFromConfig(HeroVisionRadiusConfigKey, currentPlayerVisionRadius, out radius);

            return TryReadVisionRadiusFromConfig(UnitVisionRadiusConfigKey, playerSideUnitVisionRadius, out radius);
        }

        private static Fog3CellState ResolveEntityFogCellState(Fog3MapData mapData, Vector3 worldPosition)
        {
            if (mapData == null)
                return Fog3CellState.Visible;

            if (!mapData.WorldToGrid(worldPosition, out int gridX, out int gridY))
                return Fog3CellState.Outside;

            return mapData.GetCellState(gridX, gridY);
        }

        private void UpdateEnemyVisibilityByFog(Fog3MapData mapData)
        {
            if (mapData == null)
            {
                ResetEnemyVisibilityStates();
                return;
            }

            IList<IEntityContext> allEntities = EntityRegistry.AllEntities;
            if (allEntities == null || allEntities.Count == 0)
            {
                ResetEnemyVisibilityStates();
                return;
            }

            updatedEnemyVisibilityIds.Clear();

            for (int i = 0; i < allEntities.Count; i++)
            {
                if (allEntities[i] is not MAEntity entity)
                    continue;

                if (!entity.Alive || entity.Side == SideType.PlayerSide)
                    continue;

                int entityId = entity.Id;
                if (!enemyVisibilityStates.TryGetValue(entityId, out Fog3EntityVisibilityState visibilityState) || visibilityState.Entity != entity)
                {
                    visibilityState = new Fog3EntityVisibilityState(entity, entity is BuildingEntity);
                    enemyVisibilityStates[entityId] = visibilityState;
                }

                updatedEnemyVisibilityIds.Add(entityId);

                Fog3CellState cellState = ResolveEntityFogCellState(mapData, entity.transform.position);
                Fog3CellState previousCellState = visibilityState.LastCellState;
                if (previousCellState != cellState)
                {
                    visibilityState.LastCellState = cellState;
                    FireEnemyVisibilityChanged(visibilityState, previousCellState, cellState);
                }

                ApplyEntityVisibilityState(visibilityState, cellState);
            }

            if (enemyVisibilityStates.Count == updatedEnemyVisibilityIds.Count)
                return;

            staleEnemyVisibilityIds.Clear();
            foreach (KeyValuePair<int, Fog3EntityVisibilityState> pair in enemyVisibilityStates)
            {
                if (!updatedEnemyVisibilityIds.Contains(pair.Key))
                    staleEnemyVisibilityIds.Add(pair.Key);
            }

            for (int i = 0; i < staleEnemyVisibilityIds.Count; i++)
            {
                int staleEntityId = staleEnemyVisibilityIds[i];
                if (!enemyVisibilityStates.TryGetValue(staleEntityId, out Fog3EntityVisibilityState staleState))
                    continue;

                if (staleState.LastCellState != Fog3CellState.Outside)
                {
                    FireEnemyVisibilityChanged(staleState, staleState.LastCellState, Fog3CellState.Outside);
                    staleState.LastCellState = Fog3CellState.Outside;
                }

                RestoreEntityVisibilityState(staleState);
                enemyVisibilityStates.Remove(staleEntityId);
            }
        }

        private static void SetRenderersEnabled(Renderer[] renderers, bool enabled)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.enabled != enabled)
                    renderer.enabled = enabled;
            }
        }

        private static void SetAnimatorsEnabled(Animator[] animators, bool enabled)
        {
            if (animators == null)
                return;

            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (animator != null && animator.enabled != enabled)
                    animator.enabled = enabled;
            }
        }

        private void ApplyEntityVisibilityState(Fog3EntityVisibilityState state, Fog3CellState cellState)
        {
            bool shouldRender;
            bool freezeAnimator = false;
            bool shouldShowHealthBar = cellState == Fog3CellState.Visible;

            if (state.IsBuilding)
            {
                if (cellState == Fog3CellState.Visible)
                {
                    state.HasBeenVisible = true;
                    shouldRender = true;
                }
                else if (cellState == Fog3CellState.Explored && state.HasBeenVisible)
                {
                    shouldRender = true;
                    freezeAnimator = true;
                }
                else
                {
                    shouldRender = false;
                }
            }
            else
            {
                shouldRender = cellState == Fog3CellState.Visible;
            }

            SetRenderersEnabled(state.Renderers, shouldRender);
            if (state.IsBuilding)
                SetAnimatorsEnabled(state.Animators, shouldRender && !freezeAnimator);

            HealthBarComp.SetFogVisible(state.EntityId, shouldShowHealthBar);
        }

        private void FireEnemyVisibilityChanged(Fog3EntityVisibilityState state, Fog3CellState oldCellState, Fog3CellState newCellState)
        {
            if (oldCellState == newCellState || state?.Entity == null || GF.Event == null)
                return;

            if (newCellState == Fog3CellState.Visible)
            {
                Log.Info("[FOG3] Enemy became visible. entityId={0}, old={1}, new={2}, characterKey={3}.",
                    state.EntityId, oldCellState, newCellState, state.Entity.CharacterKey);
            }

            GF.Event.Fire(this, EnemyUnitVisibilityChangedEventArgs.Create(state.Entity, oldCellState, newCellState));
        }

        private static void RestoreEntityVisibilityState(Fog3EntityVisibilityState state)
        {
            if (state == null)
                return;

            SetRenderersEnabled(state.Renderers, true);
            if (state.IsBuilding)
                SetAnimatorsEnabled(state.Animators, true);

            HealthBarComp.SetFogVisible(state.EntityId, true);
        }

        private void ResetEnemyVisibilityStates()
        {
            foreach (KeyValuePair<int, Fog3EntityVisibilityState> pair in enemyVisibilityStates)
            {
                if (pair.Value != null && pair.Value.LastCellState != Fog3CellState.Outside)
                {
                    FireEnemyVisibilityChanged(pair.Value, pair.Value.LastCellState, Fog3CellState.Outside);
                    pair.Value.LastCellState = Fog3CellState.Outside;
                }

                RestoreEntityVisibilityState(pair.Value);
            }

            enemyVisibilityStates.Clear();
            updatedEnemyVisibilityIds.Clear();
            staleEnemyVisibilityIds.Clear();
        }

        private bool TryReadVisionRadiusFromConfig(string configKey, float fallbackRadius, out float radius)
        {
            radius = fallbackRadius;

            if (GF.Config == null)
            {
                Log.Error($"[FOG3] Config component not ready, cannot read '{configKey}'.");
                return false;
            }

            float configDistance = GF.Config.GetFloat(configKey, -1f);
            if (configDistance <= 0f)
            {
                Log.Error($"[FOG3] Config '{configKey}' is invalid: {configDistance}.");
                return false;
            }

            radius = configDistance * DistanceUnitConverter.DistanceConversionRate;
            if (radius <= 0f)
            {
                Log.Error($"[FOG3] Config '{configKey}' converted radius is invalid: {radius}.");
                return false;
            }

            return radius > 0f;
        }

        private sealed class Fog3EntityVisibilityState
        {
            public Fog3EntityVisibilityState(MAEntity entity, bool isBuilding)
            {
                Entity = entity;
                EntityId = entity != null ? entity.Id : 0;
                IsBuilding = isBuilding;
                Renderers = entity != null ? entity.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
                Animators = entity != null ? entity.GetComponentsInChildren<Animator>(true) : Array.Empty<Animator>();
                HasBeenVisible = false;
                LastCellState = Fog3CellState.Outside;
            }

            public MAEntity Entity { get; }
            public int EntityId { get; }
            public Renderer[] Renderers { get; }
            public Animator[] Animators { get; }
            public bool IsBuilding { get; }
            public bool HasBeenVisible { get; set; }
            public Fog3CellState LastCellState { get; set; }
        }

        private void RemoveRevealerReferences(int revealerId)
        {
            int entityToRemove = 0;
            foreach (KeyValuePair<int, int> pair in entityRevealers)
            {
                if (pair.Value == revealerId)
                {
                    entityToRemove = pair.Key;
                    break;
                }
            }

            if (entityToRemove != 0)
                entityRevealers.Remove(entityToRemove);

            Transform transformToRemove = null;
            foreach (KeyValuePair<Transform, int> pair in transformRevealers)
            {
                if (pair.Value == revealerId)
                {
                    transformToRemove = pair.Key;
                    break;
                }
            }

            if (transformToRemove != null)
                transformRevealers.Remove(transformToRemove);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (controller?.MapData == null)
                return;

            Gizmos.color = Color.cyan;
            Bounds bounds = controller.MapData.Bounds;
            Gizmos.DrawWireCube(bounds.center, new Vector3(bounds.size.x, 0.1f, bounds.size.z));
        }
#endif
    }
}
