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
        private enum EntityRegistryPresentationKind
        {
            Registered = 0,
            Unregistered = 1,
        }

        private readonly struct EntityRegistryPresentationRequest
        {
            public EntityRegistryPresentationRequest(
                EntityRegistryPresentationKind kind,
                LogicEntityId logicEntityId,
                int retiringViewEntityId)
            {
                Kind = kind;
                LogicEntityId = logicEntityId;
                RetiringViewEntityId = retiringViewEntityId;
            }

            public EntityRegistryPresentationKind Kind { get; }
            public LogicEntityId LogicEntityId { get; }
            public int RetiringViewEntityId { get; }
        }

        private const string HeroVisionRadiusConfigKey = "HeroVisionRadius";
        private const string UnitVisionRadiusConfigKey = "UnitVisionRadius";
        private const string BuildingVisionRadiusConfigKey = "BuildingVisionRadius";
        private const string FogLogicCellSizeConfigKey = "FogLogicCellSize";
        private const string VisionFadeSpeedConfigKey = "VisionFadeSpeed";
        private const string VisionBoundaryFadeDistanceConfigKey = "VisionBoundaryFadeDistance";

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
        [Tooltip("是否启用敌方据点遮挡 hidden 区视野扩散。")]
        [SerializeField] private bool enableEnemyStrongholdHiddenVisionBlock;

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
        [Tooltip("玩法场景可用时立即尝试初始化迷雾，减少先看到完整场景的空窗。")]
        [SerializeField] private bool fastRebuildOnGameplaySceneAvailable = true;
        [Tooltip("玩法场景已进入但 TileWorld/GridBased 地形还没生成出来时，重新检测地形的间隔。数值越小，迷雾越快跟上关卡生成。")]
        [SerializeField] private float terrainRetryInterval = 0.05f;
        [Tooltip("兜底重建前等待的帧数。立即初始化失败时才主要依赖它。")]
        [SerializeField] private int sceneRebuildFrameDelay;
        [Tooltip("兜底重建前等待的真实时间。立即初始化失败时才主要依赖它。")]
        [SerializeField] private float sceneRebuildDelay;

        [Header("性能诊断")]
        [Tooltip("输出 FOG3 热路径性能日志。默认关闭，仅用于临时诊断。")]
        [SerializeField] private bool logPerformanceDiagnostics;

        private readonly Dictionary<int, int> entityRevealers = new Dictionary<int, int>();
        private readonly Dictionary<Transform, int> transformRevealers = new Dictionary<Transform, int>();
        private readonly Queue<EntityRegistryPresentationRequest> entityRegistryPresentationRequests =
            new Queue<EntityRegistryPresentationRequest>();
        private readonly Dictionary<int, Fog3EntityVisibilityState> enemyVisibilityStates = new Dictionary<int, Fog3EntityVisibilityState>();
        private readonly HashSet<int> updatedEnemyVisibilityIds = new HashSet<int>();
        private readonly List<int> staleEnemyVisibilityIds = new List<int>();
        private Fog3Controller controller;
        private Fog3TerrainInfo currentTerrainInfo;
        private Fog3WorldOverlayView overlayView;
        private Coroutine sceneRebuildCoroutine;
        private bool gfEventsSubscribed;
        private bool unitySceneEventsSubscribed;
        private bool entityRegistryEventsSubscribed;
        private bool isInitialized;
        private float currentOverlayHeight;
        private float nextInitializeRetryTime;
        private bool missingTerrainLogged;
        private bool visibilityRefreshPending;
        private int pendingVisibilityRefreshRequestCount;
        private float cloudHeightRefreshTimer;
        private bool cloudReferenceHeightResolved;
        private float cloudReferenceWorldY;
        private Vector3 currentOverlayWorldOffset;
        private int gameplaySceneIndex = -1;
        private int gameplaySceneHandle;

        public static Fog3Manager Instance { get; private set; }
        public Fog3Controller Controller => controller;
        public Fog3MapData MapData => controller?.MapData;
        public bool IsInitialized => isInitialized;
        public bool HasPresentedLogicFrameVisibility { get; private set; }
        public bool BlocksHiddenRevealByEnemyStronghold => enableEnemyStrongholdHiddenVisionBlock;

        public void GetEnemyUnitVisibilityDiagnostics(
            out int aliveLogicCount,
            out int boundViewCount,
            out int trackedViewCount,
            out int renderableViewCount,
            out int fogVisibleViewCount,
            out int rendererMismatchViewCount)
        {
            aliveLogicCount = 0;
            boundViewCount = 0;
            trackedViewCount = 0;
            renderableViewCount = 0;
            fogVisibleViewCount = 0;
            rendererMismatchViewCount = 0;

            IList<IEntityContext> allEntities = EntityRegistry.AllEntities;
            if (allEntities == null)
                throw new InvalidOperationException("Fog3Manager enemy visibility diagnostics require EntityRegistry.AllEntities.");

            for (int i = 0; i < allEntities.Count; i++)
            {
                IEntityContext logicEntity = allEntities[i]
                                             ?? throw new InvalidOperationException(
                                                 $"Fog3Manager enemy visibility diagnostics found a null logic entity at index {i}.");
                if (!logicEntity.Alive || logicEntity.Side != SideType.EnemySide)
                    continue;
                if (logicEntity.TryGetLogicBuilding(out IBuildingLogicContext building))
                    continue;

                aliveLogicCount++;
                if (!TryResolveBoundView(logicEntity, out MAEntity view))
                    continue;

                boundViewCount++;
                if (enemyVisibilityStates.TryGetValue(view.Id, out Fog3EntityVisibilityState state)
                    && state.Entity == view)
                {
                    trackedViewCount++;
                    if (HasAnyRenderer(state.Renderers))
                        renderableViewCount++;
                    if (state.LastCellState == Fog3CellState.Visible)
                        fogVisibleViewCount++;
                    if (!state.HasAppliedState || HasRendererStateMismatch(state.Renderers, state.LastShouldRender))
                        rendererMismatchViewCount++;
                }
            }
        }

        private static bool HasAnyRenderer(Renderer[] renderers)
        {
            if (renderers == null)
                return false;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    return true;
            }

            return false;
        }

        private static bool HasRendererStateMismatch(Renderer[] renderers, bool expectedEnabled)
        {
            if (renderers == null)
                return false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.enabled != expectedEnabled)
                    return true;
            }

            return false;
        }

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
                TryInitializeOrSchedule("FOG3 manager started");
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
            long updateStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            long updateStartAllocatedBytes = System.GC.GetAllocatedBytesForCurrentThread();
            try
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

                UpdateEntityRegistryPresentation();

                bool visibilityUpdatedThisFrame = false;
                if (visibilityRefreshPending)
                {
                    int batchedRequestCount = pendingVisibilityRefreshRequestCount;
                    visibilityRefreshPending = false;
                    pendingVisibilityRefreshRequestCount = 0;
                    UpdateVisibilityImmediately();
                    visibilityUpdatedThisFrame = true;

                    if (batchedRequestCount > 1)
                    {
                        Log.Info(
                            "[FOG3] Batched {0} entity visibility refresh requests into one update.",
                            batchedRequestCount);
                    }
                }

                if (!visibilityUpdatedThisFrame && controller.MapData.IsDirty)
                {
                    long visibilityStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        controller.PublishAuthoritativeVisibility(logPerformanceDiagnostics);
                    }
                    finally
                    {
                        MainThreadFrameProfiler.Record(
                            MainThreadPerfScope.Fog3Visibility,
                            System.Diagnostics.Stopwatch.GetTimestamp() - visibilityStartTicks);
                    }
                }

                RefreshCloudOverlayForCameraIfNeeded();
                RefreshOverlayPresentationForCamera();
            }
            finally
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.Fog3Update,
                    System.Diagnostics.Stopwatch.GetTimestamp() - updateStartTicks,
                    System.Math.Max(0L, System.GC.GetAllocatedBytesForCurrentThread() - updateStartAllocatedBytes));
            }
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
            HasPresentedLogicFrameVisibility = false;
            controller.VisibilityUpdated -= OnVisibilityUpdated;
            controller.Initialize(terrainInfo, ResolveFogLogicCellSize());
            controller.VisibilityUpdated += OnVisibilityUpdated;

            if (createWorldOverlay)
                EnsureOverlayView(terrainInfo);

            isInitialized = true;
            Log.Info(
                $"[FOG3] Initialized from {terrainInfo.SourceName}: " +
                $"terrain={terrainInfo.Width}x{terrainInfo.Height}, terrainCell={terrainInfo.CellSize}, " +
                $"fog={controller.MapData.Width}x{controller.MapData.Height}, fogCell=({controller.MapData.CellSizeX},{controller.MapData.CellSizeY})");
        }

        public void RebuildTerrain()
        {
            isInitialized = false;
            visibilityRefreshPending = false;
            pendingVisibilityRefreshRequestCount = 0;
            entityRegistryPresentationRequests.Clear();
            entityRevealers.Clear();
            transformRevealers.Clear();
            if (controller != null)
                controller.VisibilityUpdated -= OnVisibilityUpdated;
            currentTerrainInfo = null;
            HasPresentedLogicFrameVisibility = false;
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

        public void ResetForWorldTransition()
        {
            if (sceneRebuildCoroutine != null)
            {
                StopCoroutine(sceneRebuildCoroutine);
                sceneRebuildCoroutine = null;
            }

            ClearRuntimeState(true);
        }

        public int RegisterRevealer(
            Transform target,
            float visionRadius,
            int viewEntityId = 0,
            bool revealerUsesLineOfSight = false,
            bool allowRevealHidden = true,
            int logicEntityId = 0)
        {
            if (!isInitialized || controller == null || target == null)
                return -1;
            if ((viewEntityId == 0) != (logicEntityId == 0))
            {
                throw new InvalidOperationException(
                    $"FOG3 entity revealer requires both view and logic entity ids. view={viewEntityId}, logic={logicEntityId}.");
            }

            if (transformRevealers.TryGetValue(target, out int existingId))
            {
                if (!controller.TryGetRevealer(existingId, out Fog3RevealerData existingRevealer))
                    throw new InvalidOperationException($"FOG3 revealer mapping references missing revealer {existingId}.");
                if (existingRevealer.LogicEntityId != logicEntityId)
                {
                    throw new InvalidOperationException(
                        $"FOG3 revealer logic identity mismatch. revealer={existingId}, existing={existingRevealer.LogicEntityId}, requested={logicEntityId}.");
                }

                SetRevealerVisionRadius(existingId, visionRadius);
                SetRevealerAllowRevealHidden(existingId, allowRevealHidden);
                if (viewEntityId != 0)
                    entityRevealers[viewEntityId] = existingId;
                return existingId;
            }

            int id = controller.RegisterRevealer(
                target,
                visionRadius,
                logicEntityId,
                revealerUsesLineOfSight,
                allowRevealHidden);
            if (id > 0)
            {
                transformRevealers[target] = id;
                if (viewEntityId != 0)
                    entityRevealers[viewEntityId] = id;
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

        public void RefreshRevealHiddenByHeroGhostState()
        {
            if (controller == null)
                return;

            bool hasPlayerSideGhostHero = HasPlayerSideGhostHero();
            IList<IEntityContext> allEntities = EntityRegistry.AllEntities;
            if (allEntities == null)
                return;

            for (int i = 0; i < allEntities.Count; i++)
            {
                IEntityContext logicEntity = allEntities[i]
                                             ?? throw new InvalidOperationException(
                                                 $"Fog3Manager.RefreshRevealHiddenByHeroGhostState found a null logic entity at index {i}.");
                if (!TryResolveBoundView(logicEntity, out MAEntity entityView))
                    continue;

                if (!entityRevealers.ContainsKey(entityView.Id))
                    continue;

                SetEntityRevealerAllowRevealHidden(
                    entityView.Id,
                    ShouldEntityRevealerAllowRevealHidden(entityView, hasPlayerSideGhostHero));
            }
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

            if (gameplaySceneIndex >= 0 && gameplaySceneIndex < SceneManager.sceneCount)
            {
                Scene cachedScene = SceneManager.GetSceneAt(gameplaySceneIndex);
                if (cachedScene.handle == gameplaySceneHandle && cachedScene.isLoaded)
                    return true;

                gameplaySceneIndex = -1;
                gameplaySceneHandle = 0;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && IsGameplaySceneName(scene.name))
                {
                    gameplaySceneIndex = i;
                    gameplaySceneHandle = scene.handle;
                    return true;
                }
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

        private void TryInitializeOrSchedule(string reason)
        {
            if (isInitialized)
            {
                if (sceneRebuildCoroutine != null)
                {
                    StopCoroutine(sceneRebuildCoroutine);
                    sceneRebuildCoroutine = null;
                }

                return;
            }

            if (fastRebuildOnGameplaySceneAvailable && CanInitializeForCurrentScene())
            {
                RebuildTerrain();
                if (isInitialized)
                {
                    if (sceneRebuildCoroutine != null)
                    {
                        StopCoroutine(sceneRebuildCoroutine);
                        sceneRebuildCoroutine = null;
                    }

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
            if (isInitialized || !CanInitializeForCurrentScene())
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
            visibilityRefreshPending = false;
            pendingVisibilityRefreshRequestCount = 0;
            entityRegistryPresentationRequests.Clear();
            entityRevealers.Clear();
            transformRevealers.Clear();
            ResetEnemyVisibilityStates();

            if (controller != null)
                controller.VisibilityUpdated -= OnVisibilityUpdated;

            controller = new Fog3Controller();
            currentTerrainInfo = null;
            HasPresentedLogicFrameVisibility = false;
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
            overlayView.Build(
                terrainInfo,
                controller.MapData,
                viewSettings,
                currentOverlayHeight,
                ResolveHeightSampleMask(),
                currentOverlayWorldOffset,
                ResolveVisibilityFadeSpeed(),
                ResolveVisibilityBoundaryFadeDistance());
            overlayView.Render(controller.MapData, logPerformanceDiagnostics);
        }

        private static float ResolveVisibilityFadeSpeed()
        {
            float speed = (float)FixedConfigReader.ReadRequiredPositiveFixedConfig(VisionFadeSpeedConfigKey);
            if (speed <= 0f || float.IsNaN(speed) || float.IsInfinity(speed))
                throw new InvalidOperationException($"FOG3 visibility fade speed must be finite and positive. value={speed}.");
            return speed;
        }

        private static Fix64 ResolveFogLogicCellSize()
        {
            return FixedConfigReader.ReadRequiredPositiveFixedConfig(FogLogicCellSizeConfigKey);
        }

        private static float ResolveVisibilityBoundaryFadeDistance()
        {

            float distance = (float)FixedConfigReader.ReadRequiredPositiveFixedConfig(VisionBoundaryFadeDistanceConfigKey);
            if (distance <= 0f || float.IsNaN(distance) || float.IsInfinity(distance))
                throw new InvalidOperationException($"FOG3 visibility boundary fade distance must be finite and positive. value={distance}.");
            if (distance > Fog3WorldOverlayView.MaximumBoundaryFadeCellRatio)
            {
                throw new InvalidOperationException(
                    $"FOG3 visibility boundary fade distance cannot exceed {Fog3WorldOverlayView.MaximumBoundaryFadeCellRatio} fog texels. distance={distance}.");
            }

            return distance;
        }

        private LayerMask ResolveHeightSampleMask()
        {
            if (viewSettings.HeightSampleMask.value != 0)
                return viewSettings.HeightSampleMask;

            if (terrainSettings.GroundMask.value != 0)
                return terrainSettings.GroundMask;

            int groundMask = LayerMask.GetMask("Ground");
            if (groundMask == 0)
                throw new InvalidOperationException("FOG3 height sampling requires the Ground layer.");
            return groundMask;
        }

        private Vector3 ResolveOverlayWorldOffset(Fog3TerrainInfo terrainInfo, float overlayLocalHeight)
        {
            Vector3 offset = viewSettings.CloudLayerWorldOffset;
            if (viewSettings.SurfaceMode != Fog3OverlaySurfaceMode.CloudLayer
                || viewSettings.ProjectCloudLayerToCameraView
                || !viewSettings.UseCameraAngleOffset)
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
            overlayView.Build(
                currentTerrainInfo,
                controller.MapData,
                viewSettings,
                currentOverlayHeight,
                ResolveHeightSampleMask(),
                currentOverlayWorldOffset,
                ResolveVisibilityFadeSpeed(),
                ResolveVisibilityBoundaryFadeDistance());
            overlayView.Render(controller.MapData, logPerformanceDiagnostics);

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

        private void RefreshOverlayPresentationForCamera()
        {
            if (!isInitialized || overlayView == null)
                return;
            if (!TryGetReferenceCamera(out Camera referenceCamera))
                return;

            overlayView.RefreshCameraPresentation(referenceCamera);
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
            long overlayStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (overlayView != null)
                    overlayView.Render(mapData, logPerformanceDiagnostics);
            }
            finally
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.Fog3OverlayRender,
                    System.Diagnostics.Stopwatch.GetTimestamp() - overlayStartTicks);
            }

            UpdateEnemyVisibilityByFog(mapData);
            if (LogicTimeControlService.IsActive && LogicTimeControlService.CurrentFrame > 0)
                HasPresentedLogicFrameVisibility = true;
        }

        private void UpdateVisibilityImmediately()
        {
            if (!isInitialized || controller == null || controller.MapData == null)
                return;

            long visibilityStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                controller.PublishAuthoritativeVisibility(logPerformanceDiagnostics);
            }
            finally
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.Fog3Visibility,
                    System.Diagnostics.Stopwatch.GetTimestamp() - visibilityStartTicks);
            }
        }

        private void RequestVisibilityRefresh()
        {
            visibilityRefreshPending = true;
            pendingVisibilityRefreshRequestCount++;
        }

        private void TrySubscribeEvents()
        {
            if (!entityRegistryEventsSubscribed)
            {
                EntityRegistry.Registered += OnLogicEntityRegistered;
                EntityRegistry.Unregistered += OnLogicEntityUnregistered;
                entityRegistryEventsSubscribed = true;
            }

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
            GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
            gfEventsSubscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (entityRegistryEventsSubscribed)
            {
                EntityRegistry.Registered -= OnLogicEntityRegistered;
                EntityRegistry.Unregistered -= OnLogicEntityUnregistered;
                entityRegistryEventsSubscribed = false;
            }

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
                GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
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

            TryInitializeOrSchedule($"{reason}: {sceneName}");
        }

        private void OnShowEntitySuccess(object sender, GameEventArgs e)
        {
            ShowEntitySuccessEventArgs args = (ShowEntitySuccessEventArgs)e;
            if (args.Entity == null || args.Entity.Logic == null)
                return;

            if (IsLevelEntityLogic(args.Entity.Logic))
            {
                TryInitializeOrSchedule("GF_X level entity loaded");
                return;
            }

            if (!autoRegisterPlayerSideEntities || !isInitialized)
                return;

            if (args.Entity.Logic is not IEntityContext context)
                return;
            if (!context.LogicEntityId.IsValid)
            {
                throw new InvalidOperationException(
                    $"FOG3 cannot process shown entity {args.Entity.Id} without a valid logic identity.");
            }
            if (!EntityRegistry.TryGet(context.LogicEntityId, out IEntityContext registeredEntity))
                return;
            bool identityMatches = context is MAEntity view
                ? ReferenceEquals(registeredEntity, view.LogicState)
                : ReferenceEquals(registeredEntity, context);
            if (!identityMatches)
            {
                throw new InvalidOperationException(
                    $"FOG3 shown entity identity mismatch. view={args.Entity.Id}, logic={context.LogicEntityId.Value}.");
            }

            TryRegisterEntity(args.Entity.Id, args.Entity.Logic);
            RefreshOverlayHeightIfNeeded();
            RequestVisibilityRefresh();
        }

        private void OnEntityFactionChanged(object sender, GameEventArgs e)
        {
            if (!autoRegisterPlayerSideEntities || !isInitialized)
                return;

            EntityFactionChangedEventArgs args = (EntityFactionChangedEventArgs)e;
            SideType oldSide = EntitySideHelper.ToSide(args.OldFactionId);
            SideType newSide = EntitySideHelper.ToSide(args.NewFactionId);
            if (oldSide != SideType.PlayerSide && newSide != SideType.PlayerSide)
                return;

            if (newSide != SideType.PlayerSide)
            {
                if (entityRevealers.TryGetValue(args.EntityId, out int revealerId))
                {
                    UnregisterRevealer(revealerId);
                    RequestVisibilityRefresh();
                }

                return;
            }

            Entity entity = GF.Entity != null ? GF.Entity.GetEntity(args.EntityId) : null;
            if (entity == null || entity.Logic == null)
            {
                Log.Error("[FOG3] Entity faction changed but entity logic is missing. entityId={0}, oldFaction={1}, newFaction={2}.",
                    args.EntityId, args.OldFactionId, args.NewFactionId);
                return;
            }

            if (enemyVisibilityStates.TryGetValue(args.EntityId, out Fog3EntityVisibilityState visibilityState))
            {
                RestoreEntityVisibilityState(visibilityState);
                enemyVisibilityStates.Remove(args.EntityId);
            }

            if (entity.Logic is BuildingEntity building && !building.Alive)
            {
                StartCoroutine(RegisterEntityRevealerAfterAliveRefresh(args.EntityId));
                return;
            }

            if (TryRegisterEntity(args.EntityId, entity.Logic))
            {
                RefreshOverlayHeightIfNeeded();
                RequestVisibilityRefresh();
            }
        }

        private static bool IsLevelEntityLogic(EntityLogic logic)
        {
            return logic != null && string.Equals(logic.GetType().Name, "LevelEntity", StringComparison.Ordinal);
        }

        private void OnHideEntityComplete(object sender, GameEventArgs e)
        {
            HideEntityCompleteEventArgs args = (HideEntityCompleteEventArgs)e;
            if (entityRevealers.TryGetValue(args.EntityId, out int revealerId))
            {
                UnregisterRevealer(revealerId);
                RequestVisibilityRefresh();
            }

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

            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext logicEntity = entities[i]
                                             ?? throw new InvalidOperationException(
                                                 $"FOG3 found a null registered logic entity at index {i}.");
                if (!TryResolveBoundView(logicEntity, out MAEntity view))
                    continue;

                TryRegisterEntity(view.Id, view);
            }
        }

        private void OnLogicEntityRegistered(IEntityContext logicEntity)
        {
            EnqueueEntityRegistryPresentation(EntityRegistryPresentationKind.Registered, logicEntity);
        }

        private void OnLogicEntityUnregistered(IEntityContext logicEntity)
        {
            EnqueueEntityRegistryPresentation(EntityRegistryPresentationKind.Unregistered, logicEntity);
        }

        private void EnqueueEntityRegistryPresentation(
            EntityRegistryPresentationKind kind,
            IEntityContext logicEntity)
        {
            if (logicEntity == null)
                throw new ArgumentNullException(nameof(logicEntity));
            if (!logicEntity.LogicEntityId.IsValid)
                throw new InvalidOperationException($"FOG3 received {kind} for an invalid logic entity id.");

            int retiringViewEntityId = kind == EntityRegistryPresentationKind.Unregistered
                                       && logicEntity is LogicEntityState state
                ? state.BoundViewEntityId
                : 0;
            entityRegistryPresentationRequests.Enqueue(
                new EntityRegistryPresentationRequest(
                    kind,
                    logicEntity.LogicEntityId,
                    retiringViewEntityId));
        }

        private void UpdateEntityRegistryPresentation()
        {
            while (entityRegistryPresentationRequests.Count > 0)
            {
                EntityRegistryPresentationRequest request = entityRegistryPresentationRequests.Dequeue();
                switch (request.Kind)
                {
                    case EntityRegistryPresentationKind.Registered:
                        if (!autoRegisterPlayerSideEntities
                            || !EntityRegistry.TryGet(request.LogicEntityId, out IEntityContext registered))
                        {
                            continue;
                        }
                        if (!LogicEntityLifecycleService.TryGetBoundView(
                                request.LogicEntityId,
                                out MAEntity view))
                        {
                            continue;
                        }
                        if (!ReferenceEquals(registered, view.LogicState))
                        {
                            throw new InvalidOperationException(
                                $"FOG3 registered entity identity mismatch. view={view.Id}, logic={request.LogicEntityId.Value}.");
                        }
                        if (TryRegisterEntity(view.Id, view))
                        {
                            RefreshOverlayHeightIfNeeded();
                            RequestVisibilityRefresh();
                        }
                        break;
                    case EntityRegistryPresentationKind.Unregistered:
                        if (request.RetiringViewEntityId > 0
                            && entityRevealers.TryGetValue(
                                request.RetiringViewEntityId,
                                out int revealerId))
                        {
                            UnregisterRevealer(revealerId);
                            RequestVisibilityRefresh();
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(request.Kind),
                            request.Kind,
                            "Unknown FOG3 entity registry presentation kind.");
                }
            }
        }

        private IEnumerator RegisterEntityRevealerAfterAliveRefresh(int entityId)
        {
            yield return null;

            if (!autoRegisterPlayerSideEntities || !isInitialized || entityRevealers.ContainsKey(entityId))
                yield break;

            Entity entity = GF.Entity != null ? GF.Entity.GetEntity(entityId) : null;
            if (entity == null || entity.Logic == null)
            {
                Log.Error("[FOG3] Deferred player-side revealer registration failed, entity logic is missing. entityId={0}.", entityId);
                yield break;
            }

            if (!TryRegisterEntity(entityId, entity.Logic))
            {
                SideType side = entity.Logic is IEntityContext context ? context.Side : SideType.NoSide;
                bool alive = entity.Logic is IEntityContext aliveContext && aliveContext.Alive;
                Log.Error("[FOG3] Deferred player-side revealer registration failed. entityId={0}, logic={1}, side={2}, alive={3}.",
                    entityId, entity.Logic.GetType().Name, side, alive);
                yield break;
            }

            RefreshOverlayHeightIfNeeded();
            RequestVisibilityRefresh();
        }

        private bool TryRegisterEntity(int entityId, EntityLogic logic)
        {
            if (logic == null || entityId == 0 || entityRevealers.ContainsKey(entityId))
                return false;

            if (!TryReadEntityVision(logic, out float radius))
                return false;
            if (logic is not IEntityContext context || !context.LogicEntityId.IsValid)
            {
                throw new InvalidOperationException(
                    $"FOG3 cannot register entity revealer without a valid logic identity. framework={entityId}, logic={logic.GetType().FullName}.");
            }

            int revealerId = RegisterRevealer(
                logic.transform,
                radius,
                entityId,
                false,
                ShouldEntityRevealerAllowRevealHidden(logic),
                context.LogicEntityId.Value);
            return revealerId > 0;
        }

        private static bool IsGhostSoldier(EntityLogic logic)
        {
            return logic is HeroEntity soldier && soldier.IsGhostState;
        }

        private bool ShouldEntityRevealerAllowRevealHidden(EntityLogic logic)
        {
            return ShouldEntityRevealerAllowRevealHidden(logic, HasPlayerSideGhostHero());
        }

        private static bool ShouldEntityRevealerAllowRevealHidden(EntityLogic logic, bool hasPlayerSideGhostHero)
        {
            if (IsGhostSoldier(logic))
                return false;

            if (hasPlayerSideGhostHero && IsPlayerSideNonHeroSoldier(logic))
                return false;

            return true;
        }

        private static bool HasPlayerSideGhostHero()
        {
            IList<IEntityContext> allEntities = EntityRegistry.AllEntities;
            if (allEntities == null)
                return false;

            for (int i = 0; i < allEntities.Count; i++)
            {
                if (allEntities[i].TryGetLogicHero(out IHeroLogicContext hero)
                    && hero.Side == SideType.PlayerSide
                    && hero.IsGhostState)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPlayerSideNonHeroSoldier(EntityLogic logic)
        {
            return logic is SoldierEntity soldier
                && soldier.Side == SideType.PlayerSide
                && soldier is not HeroEntity;
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

            if (logic is BuildingEntity building)
            {
                if (building.buildingData == null)
                    throw new InvalidOperationException(
                        $"Fog3Manager found a building view without BuildingData. entity={building.Id}.");
                if (building.buildingData.Lv == 0)
                    return false;

                return TryReadVisionRadiusFromConfig(BuildingVisionRadiusConfigKey, buildingVisionRadius, out radius);
            }

            else if (brainType == BrainType.Player || logic is PlayerEntity)
                return TryReadVisionRadiusFromConfig(HeroVisionRadiusConfigKey, currentPlayerVisionRadius, out radius);

            return TryReadVisionRadiusFromConfig(UnitVisionRadiusConfigKey, playerSideUnitVisionRadius, out radius);
        }

        private void UpdateEnemyVisibilityByFog(Fog3MapData mapData)
        {
            long enemyStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            int allEntityCount = 0;
            int enemyEntityCount = 0;
            int lv0BuildingCount = 0;
            int stateCreatedCount = 0;
            int cellChangedCount = 0;
            int applyCallCount = 0;
            int applySkippedCount = 0;
            int rendererWriteCount = 0;
            int animatorWriteCount = 0;
            int staleCount = 0;
            long bindTicks = 0L;
            long stateCreateTicks = 0L;
            long resolveTicks = 0L;
            long eventTicks = 0L;
            long applyTicks = 0L;
            long staleTicks = 0L;
            HealthBarComp.ResetFogVisibilityDiagnostics();
            try
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
                    allEntityCount++;
                    IEntityContext logicEntity = allEntities[i]
                                                 ?? throw new InvalidOperationException(
                                                     $"Fog3Manager.UpdateEnemyVisibilityByFog found a null logic entity at index {i}.");
                    if (!logicEntity.Alive || logicEntity.Side == SideType.PlayerSide)
                        continue;

                    enemyEntityCount++;
                    long bindStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    bool hasBoundView = TryResolveBoundView(logicEntity, out MAEntity entity);
                    bindTicks += System.Diagnostics.Stopwatch.GetTimestamp() - bindStartTicks;
                    if (!hasBoundView)
                        continue;

                    int entityId = entity.Id;
                    if (entity is BuildingEntity building && building.buildingData != null && building.buildingData.Lv == 0)
                    {
                        lv0BuildingCount++;
                        if (enemyVisibilityStates.ContainsKey(entityId))
                            enemyVisibilityStates.Remove(entityId);
                        continue;
                    }

                    if (!enemyVisibilityStates.TryGetValue(entityId, out Fog3EntityVisibilityState visibilityState) || visibilityState.Entity != entity)
                    {
                        long stateCreateStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                        visibilityState = new Fog3EntityVisibilityState(entity, entity is BuildingEntity);
                        enemyVisibilityStates[entityId] = visibilityState;
                        stateCreateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stateCreateStartTicks;
                        stateCreatedCount++;
                    }

                    updatedEnemyVisibilityIds.Add(entityId);

                    long resolveStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    Fog3CellState cellState = entity is BuildingEntity stealthBuilding
                                              && stealthBuilding.IsHiddenFromPlayerByStealth
                        ? Fog3CellState.Hidden
                        : mapData.GetCellState(logicEntity.PositionFixed);
                    resolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - resolveStartTicks;
                    Fog3CellState previousCellState = visibilityState.LastCellState;
                    if (previousCellState != cellState)
                    {
                        visibilityState.LastCellState = cellState;
                        cellChangedCount++;
                        long eventStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                        FireEnemyVisibilityChanged(visibilityState, previousCellState, cellState);
                        eventTicks += System.Diagnostics.Stopwatch.GetTimestamp() - eventStartTicks;
                    }

                    long applyStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    applyCallCount++;
                    if (ApplyEntityVisibilityState(visibilityState, cellState, out int rendererWrites, out int animatorWrites))
                    {
                        rendererWriteCount += rendererWrites;
                        animatorWriteCount += animatorWrites;
                    }
                    else
                    {
                        applySkippedCount++;
                    }

                    applyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - applyStartTicks;
                }

                if (enemyVisibilityStates.Count == updatedEnemyVisibilityIds.Count)
                    return;

                long staleStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
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
                    staleCount++;
                }
                staleTicks += System.Diagnostics.Stopwatch.GetTimestamp() - staleStartTicks;
            }
            finally
            {
                long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - enemyStartTicks;
                MainThreadFrameProfiler.Record(MainThreadPerfScope.Fog3EnemyVisibility, elapsedTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.Fog3EnemyBind, bindTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.Fog3EnemyStateCreate, stateCreateTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.Fog3EnemyResolve, resolveTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.Fog3EnemyEvent, eventTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.Fog3EnemyApply, applyTicks);
                MainThreadFrameProfiler.Record(MainThreadPerfScope.Fog3EnemyStale, staleTicks);
                long healthDiagnosticsStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                HealthBarComp.ConsumeFogVisibilityDiagnostics(
                    out int healthCalls,
                    out int healthCacheHits,
                    out int healthMissing,
                    out int healthNoOps,
                    out int healthCanvasWrites,
                    out long healthInternalTicks);
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.Fog3EnemyHealth,
                    System.Diagnostics.Stopwatch.GetTimestamp() - healthDiagnosticsStartTicks);

                double elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (logPerformanceDiagnostics && elapsedMs >= 2.0)
                {
                    UnityEngine.Debug.LogFormat(
                        LogType.Log,
                        LogOption.NoStacktrace,
                        null,
                        "[FOG3Perf] enemy total={0:F3}ms all={1} enemies={2} lv0={3} states={4} created={5} cellChanged={6} applyCalls={7} applySkipped={8} rendererWrites={9} animatorWrites={10} stale={11} resolve={12:F3}ms event={13:F3}ms apply={14:F3}ms staleMs={15:F3} health(calls={16},hits={17},missing={18},noOps={19},canvasWrites={20},internal={21:F3}ms)",
                        elapsedMs,
                        allEntityCount,
                        enemyEntityCount,
                        lv0BuildingCount,
                        enemyVisibilityStates.Count,
                        stateCreatedCount,
                        cellChangedCount,
                        applyCallCount,
                        applySkippedCount,
                        rendererWriteCount,
                        animatorWriteCount,
                        staleCount,
                        resolveTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                        eventTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                        applyTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                        staleTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                        healthCalls,
                        healthCacheHits,
                        healthMissing,
                        healthNoOps,
                        healthCanvasWrites,
                        healthInternalTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                }
            }
        }

        private static bool TryResolveBoundView(IEntityContext logicEntity, out MAEntity view)
        {
            if (logicEntity == null)
                throw new ArgumentNullException(nameof(logicEntity));
            if (!logicEntity.LogicEntityId.IsValid)
            {
                throw new InvalidOperationException(
                    "Fog3Manager cannot resolve a View for a logic entity with an invalid logic entity id.");
            }

            return LogicEntityLifecycleService.TryGetBoundView(logicEntity.LogicEntityId, out view);
        }

        private static int SetRenderersEnabled(Renderer[] renderers, bool enabled)
        {
            if (renderers == null)
                return 0;

            int changedCount = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.enabled != enabled)
                {
                    renderer.enabled = enabled;
                    changedCount++;
                }
            }

            return changedCount;
        }

        private static int SetAnimatorsEnabled(Animator[] animators, bool enabled)
        {
            if (animators == null)
                return 0;

            int changedCount = 0;
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (animator != null && animator.enabled != enabled)
                {
                    animator.enabled = enabled;
                    changedCount++;
                }
            }

            return changedCount;
        }

        private bool ApplyEntityVisibilityState(Fog3EntityVisibilityState state, Fog3CellState cellState, out int rendererWrites, out int animatorWrites)
        {
            long applyStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            long rendererTicks = 0L;
            long animatorTicks = 0L;
            long healthBarTicks = 0L;
            rendererWrites = 0;
            animatorWrites = 0;

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

            bool shouldAnimate = state.IsBuilding && shouldRender && !freezeAnimator;
            if (state.HasAppliedState
                && state.LastShouldRender == shouldRender
                && state.LastShouldAnimate == shouldAnimate
                && state.LastShouldShowHealthBar == shouldShowHealthBar
                && HealthBarComp.IsFogVisibilityApplied(state.EntityId, shouldShowHealthBar))
            {
                return false;
            }

            long stepStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            rendererWrites = SetRenderersEnabled(state.Renderers, shouldRender);
            rendererTicks = System.Diagnostics.Stopwatch.GetTimestamp() - stepStartTicks;
            if (state.IsBuilding)
            {
                stepStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                animatorWrites = SetAnimatorsEnabled(state.Animators, shouldAnimate);
                animatorTicks = System.Diagnostics.Stopwatch.GetTimestamp() - stepStartTicks;
            }

            stepStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            HealthBarComp.SetFogVisible(state.EntityId, shouldShowHealthBar);
            healthBarTicks = System.Diagnostics.Stopwatch.GetTimestamp() - stepStartTicks;
            state.HasAppliedState = true;
            state.LastShouldRender = shouldRender;
            state.LastShouldAnimate = shouldAnimate;
            state.LastShouldShowHealthBar = shouldShowHealthBar;

            long totalTicks = System.Diagnostics.Stopwatch.GetTimestamp() - applyStartTicks;
            double totalMs = totalTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (totalMs >= 10.0)
            {
                UnityEngine.Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    null,
                    "[FOG3VisibilityStep] entityId={0} characterKey={1} cell={2} total={3:F3}ms renderer={4:F3}ms/{5} animator={6:F3}ms/{7} health={8:F3}ms render={9} animate={10} healthVisible={11}.",
                    state.EntityId,
                    state.Entity != null ? state.Entity.CharacterKey : "<null>",
                    cellState,
                    totalMs,
                    rendererTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    rendererWrites,
                    animatorTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    animatorWrites,
                    healthBarTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    shouldRender,
                    shouldAnimate,
                    shouldShowHealthBar);
            }
            return true;
        }

        private void FireEnemyVisibilityChanged(Fog3EntityVisibilityState state, Fog3CellState oldCellState, Fog3CellState newCellState)
        {
            if (oldCellState == newCellState || state?.Entity == null || GF.Event == null)
                return;

            if (newCellState == Fog3CellState.Visible)
            {
                UnityEngine.Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    null,
                    "[FOG3] Enemy became visible. entityId={0}, old={1}, new={2}, characterKey={3}.",
                    state.EntityId,
                    oldCellState,
                    newCellState,
                    state.Entity.CharacterKey);
            }

            GF.Event.Fire(this, EnemyUnitVisibilityChangedEventArgs.Create(state.Entity, oldCellState, newCellState));
        }

        private static void RestoreEntityVisibilityState(Fog3EntityVisibilityState state)
        {
            if (state == null)
                return;

            if (state.Entity is BuildingEntity building)
            {
                if (building.buildingData != null && building.buildingData.Lv == 0)
                {
                    building.RefreshLv0PhaseVisibility();
                    HealthBarComp.SetFogVisible(state.EntityId, false);
                    return;
                }

                if (building.IsHiddenFromPlayerByStealth)
                {
                    SetRenderersEnabled(state.Renderers, false);
                    SetAnimatorsEnabled(state.Animators, false);
                    HealthBarComp.SetFogVisible(state.EntityId, false);
                    return;
                }
            }

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

            radius = configDistance;
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
            public bool HasAppliedState { get; set; }
            public bool LastShouldRender { get; set; }
            public bool LastShouldAnimate { get; set; }
            public bool LastShouldShowHealthBar { get; set; }
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
