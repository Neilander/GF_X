using System.Collections.Generic;
using System;
using System.Collections;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3Manager : GameFrameworkComponent
    {
        [Header("Terrain")]
        [SerializeField] private Fog3TerrainSettings terrainSettings = new Fog3TerrainSettings();

        [Header("Vision")]
        [SerializeField] private float currentPlayerVisionRadius = 14f;
        [SerializeField] private float playerSideUnitVisionRadius = 10f;
        [SerializeField] private float buildingVisionRadius = 12f;
        [SerializeField] private bool autoRegisterPlayerSideEntities = true;
        [SerializeField] private float updateInterval = 0.08f;
        [SerializeField] private float softEdgeWidth = 2f;
        [SerializeField] private bool useLineOfSight;
        [SerializeField] private LayerMask lineOfSightOccluderMask;
        [SerializeField] private float lineOfSightEyeHeight = 1f;

        [Header("View")]
        [SerializeField] private bool createWorldOverlay = true;
        [SerializeField] private Fog3ViewSettings viewSettings = new Fog3ViewSettings();

        [Header("GF_X Scene Flow")]
        [SerializeField] private bool persistAcrossSceneLoads = true;
        [SerializeField] private bool waitForGameplayScene = true;
        [SerializeField] private string gameplaySceneName = "Game";
        [SerializeField] private bool rebuildOnSceneLoaded = true;
        [SerializeField] private int sceneRebuildFrameDelay = 2;
        [SerializeField] private float sceneRebuildDelay = 0.25f;

        private readonly Dictionary<int, int> entityRevealers = new Dictionary<int, int>();
        private readonly Dictionary<Transform, int> transformRevealers = new Dictionary<Transform, int>();
        private Fog3Controller controller;
        private Fog3TerrainInfo currentTerrainInfo;
        private Fog3WorldOverlayView overlayView;
        private Coroutine sceneRebuildCoroutine;
        private bool gfEventsSubscribed;
        private bool unitySceneEventsSubscribed;
        private bool isInitialized;
        private float currentOverlayHeight;
        private float updateTimer;

        public static Fog3Manager Instance { get; private set; }
        public Fog3Controller Controller => controller;
        public Fog3MapData MapData => controller?.MapData;
        public bool IsInitialized => isInitialized;

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
                ScheduleSceneRebuild("FOG3 manager started");
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
                Initialize();
                RegisterExistingRevealers();
                if (!isInitialized || controller == null || controller.MapData == null)
                    return;
            }

            updateTimer += Time.deltaTime;
            if (updateInterval <= 0f || updateTimer >= updateInterval)
            {
                updateTimer = 0f;
                controller.UpdateVisibility(lineOfSightOccluderMask, lineOfSightEyeHeight, softEdgeWidth, useLineOfSight);
            }
        }

        public void Initialize()
        {
            EnsureController();
            if (isInitialized && controller.MapData != null)
                return;

            Fog3TerrainInfo terrainInfo = Fog3TerrainDetector.Detect(terrainSettings);
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
            Initialize();
            RegisterExistingRevealers();
        }

        public int RegisterRevealer(Transform target, float visionRadius, int entityId = 0, bool revealerUsesLineOfSight = false)
        {
            if (!isInitialized || controller == null || target == null)
                return -1;

            if (transformRevealers.TryGetValue(target, out int existingId))
            {
                SetRevealerVisionRadius(existingId, visionRadius);
                if (entityId != 0)
                    entityRevealers[entityId] = existingId;
                return existingId;
            }

            int id = controller.RegisterRevealer(target, visionRadius, entityId, revealerUsesLineOfSight);
            if (id > 0)
            {
                transformRevealers[target] = id;
                if (entityId != 0)
                    entityRevealers[entityId] = id;
            }

            return id;
        }

        public int RegisterStaticRevealer(Vector3 position, float visionRadius, bool revealerUsesLineOfSight = false)
        {
            if (!isInitialized || controller == null)
                return -1;

            return controller.RegisterRevealer(position, visionRadius, revealerUsesLineOfSight);
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
            Log.Info($"[FOG3] Rebuilt after scene became ready: {reason}.");
        }

        private void ClearRuntimeState(bool destroyOverlay)
        {
            isInitialized = false;
            entityRevealers.Clear();
            transformRevealers.Clear();

            if (controller != null)
                controller.VisibilityUpdated -= OnVisibilityUpdated;

            controller = new Fog3Controller();
            currentTerrainInfo = null;
            currentOverlayHeight = 0f;

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
            overlayView.Build(terrainInfo, viewSettings, currentOverlayHeight);
            overlayView.Render(controller.MapData);
        }

        private float ResolveOverlayHeight(Fog3TerrainInfo terrainInfo)
        {
            float minimumLocalHeight = Mathf.Max(0.01f, viewSettings.OverlayHeight);
            if (viewSettings.DrawOverSceneGeometry || !viewSettings.AutoHeightAboveScene)
                return minimumLocalHeight;

            float terrainMinX = terrainInfo.Origin.x;
            float terrainMaxX = terrainInfo.Origin.x + terrainInfo.Width * terrainInfo.CellSize;
            float terrainMinZ = terrainInfo.Origin.z;
            float terrainMaxZ = terrainInfo.Origin.z + terrainInfo.Height * terrainInfo.CellSize;
            float maxWorldY = terrainInfo.Origin.y + minimumLocalHeight;

            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!CanUseRendererForOverlayHeight(renderer, terrainMinX, terrainMaxX, terrainMinZ, terrainMaxZ))
                    continue;

                maxWorldY = Mathf.Max(maxWorldY, renderer.bounds.max.y);
            }

            float resolvedWorldY = maxWorldY + Mathf.Max(0.01f, viewSettings.AutoHeightPadding);
            return Mathf.Max(minimumLocalHeight, resolvedWorldY - terrainInfo.Origin.y);
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

        private void RefreshOverlayHeightIfNeeded()
        {
            if (!createWorldOverlay || currentTerrainInfo == null || overlayView == null || controller?.MapData == null)
                return;

            if (viewSettings.DrawOverSceneGeometry)
                return;

            float nextOverlayHeight = ResolveOverlayHeight(currentTerrainInfo);
            if (nextOverlayHeight <= currentOverlayHeight + 0.05f)
                return;

            currentOverlayHeight = nextOverlayHeight;
            overlayView.Build(currentTerrainInfo, viewSettings, currentOverlayHeight);
            overlayView.Render(controller.MapData);
            Log.Info($"[FOG3] Raised overlay height to {currentOverlayHeight:F2} to stay above scene renderers.");
        }

        private void OnVisibilityUpdated(Fog3MapData mapData)
        {
            if (overlayView != null)
                overlayView.Render(mapData);
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

            ScheduleSceneRebuild($"{reason}: {sceneName}");
        }

        private void OnShowEntitySuccess(object sender, GameEventArgs e)
        {
            if (!autoRegisterPlayerSideEntities || !isInitialized)
                return;

            ShowEntitySuccessEventArgs args = (ShowEntitySuccessEventArgs)e;
            if (args.Entity == null || args.Entity.Logic == null)
                return;

            TryRegisterEntity(args.Entity.Id, args.Entity.Logic);
            RefreshOverlayHeightIfNeeded();
        }

        private void OnHideEntityComplete(object sender, GameEventArgs e)
        {
            HideEntityCompleteEventArgs args = (HideEntityCompleteEventArgs)e;
            if (entityRevealers.TryGetValue(args.EntityId, out int revealerId))
                UnregisterRevealer(revealerId);
        }

        private void RegisterExistingRevealers()
        {
            if (!isInitialized)
                return;

            Fog3RevealerComponent[] manualRevealers = UnityEngine.Object.FindObjectsOfType<Fog3RevealerComponent>();
            for (int i = 0; i < manualRevealers.Length; i++)
            {
                if (manualRevealers[i] != null && manualRevealers[i].AutoRegister && manualRevealers[i].isActiveAndEnabled)
                    manualRevealers[i].RegisterRevealer();
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

            if (TryRegisterEntityComponentRevealer(entityId, logic))
                return;

            RegisterRevealer(logic.transform, radius, entityId, false);
        }

        private bool TryRegisterEntityComponentRevealer(int entityId, EntityLogic logic)
        {
            Fog3RevealerComponent revealer = logic.GetComponent<Fog3RevealerComponent>();
            if (revealer == null)
                revealer = logic.GetComponentInChildren<Fog3RevealerComponent>();

            if (revealer == null || !revealer.isActiveAndEnabled)
                return false;

            int revealerId = revealer.RegisterRevealer(entityId);
            if (revealerId <= 0)
                return false;

            entityRevealers[entityId] = revealerId;
            return true;
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
                radius = buildingVisionRadius;
            else if (brainType == BrainType.Player || logic is PlayerEntity)
                radius = currentPlayerVisionRadius;
            else
                radius = playerSideUnitVisionRadius;

            return radius > 0f;
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
