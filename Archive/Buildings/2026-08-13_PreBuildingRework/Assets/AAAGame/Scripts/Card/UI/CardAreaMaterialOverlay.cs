using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGameFramework.Runtime;

namespace AAAGame.Card.UI
{
    /// <summary>
    /// 场景放置预览层。
    /// 负责区域高亮、生成范围圈以及预生成士兵预览。
    /// </summary>
    public class CardAreaMaterialOverlay : MonoBehaviour
    {
        private const string PreviewShaderAssetPath = "Assets/AAAGame/Scripts/Card/Material/CardForbiddenZoneOverlay.shader";

        [Header("材质设置")]
        [SerializeField] private Material validOverlayMaterial;
        [SerializeField] private Material invalidOverlayMaterial;

        [Header("区域设置")]
        [SerializeField] private GameObject validAreaObject;
        [SerializeField] private GameObject invalidAreaObject;

        [Header("生成范围预览")]
        [SerializeField] private Color validPreviewColor = new Color(0.45f, 1f, 0.55f, 0.95f);
        [SerializeField] private Color invalidPreviewColor = new Color(1f, 0.35f, 0.35f, 0.95f);
        [SerializeField] [Range(0.02f, 1f)] private float previewLineWidth = 0.18f;
        [SerializeField] [Range(16, 128)] private int previewSegmentCount = 48;
        [SerializeField] [Range(0.01f, 5f)] private float previewHeightOffset = 0.12f;

        [Header("预生成士兵")]
        [SerializeField] [Range(0.01f, 2f)] private float previewUnitHeightOffset = 0.05f;
        [SerializeField] [Range(0.2f, 2f)] private float previewUnitScaleMultiplier = 1f;
        [SerializeField] private Color previewFallbackColor = new Color(0.65f, 1f, 0.72f, 0.7f);
        [SerializeField] [Range(0.2f, 2f)] private float previewFallbackHeight = 1.4f;
        [SerializeField] [Range(0.05f, 1f)] private float previewFallbackWidth = 0.3f;

        private sealed class PreviewSoldierInstance
        {
            public GameObject GameObject;
            public string SourceAssetPath;
            public bool IsFallback;
        }

        private readonly Dictionary<Renderer, Material[]> m_OriginalMaterials = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<GameObject, Collider[]> m_AreaColliderCache = new Dictionary<GameObject, Collider[]>();
        private readonly Dictionary<int, string> m_PreviewAssetPathCache = new Dictionary<int, string>();
        private readonly Dictionary<string, GameObject> m_PreviewPrefabCache = new Dictionary<string, GameObject>();
        private readonly HashSet<string> m_PendingPreviewPrefabLoads = new HashSet<string>();
        private readonly List<PreviewSoldierInstance> m_PreviewSoldierInstances = new List<PreviewSoldierInstance>();
        private readonly List<Vector3> m_LastPreviewPositions = new List<Vector3>();
        private readonly List<Vector3> m_LastRenderedPreviewPositions = new List<Vector3>();

        private GameObject m_CurrentActiveArea;
        private AreaType m_CurrentAreaType = AreaType.None;
        private LineRenderer m_PreviewRingRenderer;
        private Material m_PreviewRingMaterial;
        private GameObject m_PreviewRingObject;
        private Transform m_PreviewSoldierRoot;
        private Material m_PreviewFallbackMaterial;
        private CardModel m_LastPreviewCardModel;
        private bool m_LastPreviewValid;
        private CardModel m_LastRenderedPreviewCardModel;
        private string m_LastRenderedPreviewAssetPath;
        private bool m_LastRenderedPreviewUsedFallback;
        private bool m_PreviewSoldiersVisible;
        private bool m_HasPreviewRingState;
        private Vector3 m_LastPreviewRingCenter;
        private float m_LastPreviewRingRadius;
        private float m_LastPreviewRingLineWidth;
        private int m_LastPreviewRingSegmentCount;
        private bool m_LastPreviewRingValid;

        public enum AreaType
        {
            None,
            Valid,
            Invalid
        }

        private void Awake()
        {
            if (validAreaObject != null)
            {
                StoreOriginalMaterials(validAreaObject);
                CacheAreaColliders(validAreaObject);
            }

            if (invalidAreaObject != null)
            {
                StoreOriginalMaterials(invalidAreaObject);
                CacheAreaColliders(invalidAreaObject);
            }
        }

        public void ShowAreaEffect(Vector3 worldPosition, bool isValid)
        {
            AreaType areaType = DetermineAreaType(worldPosition, isValid);
            switch (areaType)
            {
                case AreaType.Valid:
                    ShowValidAreaEffect();
                    break;

                case AreaType.Invalid:
                    ShowInvalidAreaEffect();
                    break;

                default:
                    HideMaterialOverlay();
                    break;
            }
        }

        public void ShowPlacementPreview(
            Vector3 worldPosition,
            float radius,
            bool isValid,
            CardModel cardModel = null,
            IReadOnlyList<Vector3> previewSpawnPositions = null)
        {
            CacheLastPreviewRequest(cardModel, previewSpawnPositions, isValid);
            ShowAreaEffect(worldPosition, isValid);
            ShowPreviewRing(worldPosition, radius, isValid);
            UpdatePreviewSoldiers(cardModel, previewSpawnPositions, isValid);
        }

        public void HideAreaEffect()
        {
            HideMaterialOverlay();
            HidePreviewRing();
            HidePreviewSoldiers();
            ClearLastPreviewRequest();
        }

        public bool IsPositionInArea(Vector3 worldPosition, GameObject areaObject)
        {
            if (areaObject == null)
            {
                return false;
            }

            Collider[] colliders = GetAreaColliders(areaObject);
            if (colliders.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                Vector3 closestPoint = collider.ClosestPoint(worldPosition);
                if ((worldPosition - closestPoint).sqrMagnitude < 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private Collider[] GetAreaColliders(GameObject areaObject)
        {
            if (areaObject == null)
            {
                return System.Array.Empty<Collider>();
            }

            if (!m_AreaColliderCache.TryGetValue(areaObject, out Collider[] colliders) || colliders == null)
            {
                colliders = CacheAreaColliders(areaObject);
            }

            return colliders;
        }

        private Collider[] CacheAreaColliders(GameObject areaObject)
        {
            if (areaObject == null)
            {
                return System.Array.Empty<Collider>();
            }

            Collider[] colliders = areaObject.GetComponentsInChildren<Collider>();
            m_AreaColliderCache[areaObject] = colliders;
            return colliders;
        }

        private void StoreOriginalMaterials(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!m_OriginalMaterials.ContainsKey(renderer))
                {
                    m_OriginalMaterials[renderer] = renderer.sharedMaterials;
                }
            }
        }

        private AreaType DetermineAreaType(Vector3 worldPosition, bool isValid)
        {
            if (!isValid)
            {
                if (invalidAreaObject != null && IsPositionInArea(worldPosition, invalidAreaObject))
                {
                    return AreaType.Invalid;
                }

                return AreaType.None;
            }

            if (validAreaObject != null && IsPositionInArea(worldPosition, validAreaObject))
            {
                return AreaType.Valid;
            }

            return AreaType.None;
        }

        private void ShowValidAreaEffect()
        {
            ShowOverlay(validAreaObject, validOverlayMaterial, AreaType.Valid);
        }

        private void ShowInvalidAreaEffect()
        {
            ShowOverlay(invalidAreaObject, invalidOverlayMaterial, AreaType.Invalid);
        }

        private void ShowOverlay(GameObject targetArea, Material overlayMaterial, AreaType areaType)
        {
            if (targetArea == null || overlayMaterial == null)
            {
                return;
            }

            if (m_CurrentActiveArea != targetArea && m_CurrentActiveArea != null)
            {
                RemoveOverlayMaterial(m_CurrentActiveArea);
            }

            if (m_CurrentActiveArea == targetArea && m_CurrentAreaType == areaType)
            {
                return;
            }

            ApplyOverlayMaterial(targetArea, overlayMaterial);
            m_CurrentActiveArea = targetArea;
            m_CurrentAreaType = areaType;
        }

        private void HideMaterialOverlay()
        {
            if (m_CurrentActiveArea == null)
            {
                m_CurrentAreaType = AreaType.None;
                return;
            }

            RemoveOverlayMaterial(m_CurrentActiveArea);
            m_CurrentActiveArea = null;
            m_CurrentAreaType = AreaType.None;
        }

        private void ShowPreviewRing(Vector3 worldPosition, float radius, bool isValid)
        {
            EnsurePreviewRingRenderer();
            if (m_PreviewRingRenderer == null)
            {
                return;
            }

            int pointCount = Mathf.Max(16, previewSegmentCount);
            Vector3 center = worldPosition + Vector3.up * previewHeightOffset;
            float clampedRadius = Mathf.Max(0.1f, radius);
            if (CanReusePreviewRing(center, clampedRadius, isValid, pointCount))
            {
                m_PreviewRingRenderer.enabled = true;
                return;
            }

            m_PreviewRingRenderer.positionCount = pointCount + 1;
            m_PreviewRingRenderer.startWidth = previewLineWidth;
            m_PreviewRingRenderer.endWidth = previewLineWidth;

            Color previewColor = isValid ? validPreviewColor : invalidPreviewColor;
            m_PreviewRingRenderer.startColor = previewColor;
            m_PreviewRingRenderer.endColor = previewColor;

            for (int i = 0; i < pointCount; i++)
            {
                float angle = Mathf.PI * 2f * i / pointCount;
                Vector3 point = center + new Vector3(Mathf.Cos(angle) * clampedRadius, 0f, Mathf.Sin(angle) * clampedRadius);
                m_PreviewRingRenderer.SetPosition(i, point);
            }

            m_PreviewRingRenderer.SetPosition(pointCount, m_PreviewRingRenderer.GetPosition(0));
            m_PreviewRingRenderer.enabled = true;
            CachePreviewRingState(center, clampedRadius, isValid, pointCount);
        }

        private void HidePreviewRing()
        {
            if (m_PreviewRingRenderer != null)
            {
                m_PreviewRingRenderer.enabled = false;
            }

            m_HasPreviewRingState = false;
        }

        private bool CanReusePreviewRing(Vector3 center, float radius, bool isValid, int pointCount)
        {
            return m_HasPreviewRingState
                && m_PreviewRingRenderer.enabled
                && m_LastPreviewRingValid == isValid
                && m_LastPreviewRingSegmentCount == pointCount
                && Mathf.Abs(m_LastPreviewRingRadius - radius) <= 0.01f
                && Mathf.Abs(m_LastPreviewRingLineWidth - previewLineWidth) <= 0.001f
                && (m_LastPreviewRingCenter - center).sqrMagnitude <= 0.0004f;
        }

        private void CachePreviewRingState(Vector3 center, float radius, bool isValid, int pointCount)
        {
            m_HasPreviewRingState = true;
            m_LastPreviewRingCenter = center;
            m_LastPreviewRingRadius = radius;
            m_LastPreviewRingLineWidth = previewLineWidth;
            m_LastPreviewRingSegmentCount = pointCount;
            m_LastPreviewRingValid = isValid;
        }

        private void EnsurePreviewRingRenderer()
        {
            if (m_PreviewRingRenderer != null)
            {
                return;
            }

            m_PreviewRingObject = new GameObject("CardPlacementPreviewRing");

            Transform previewParent = ResolvePreviewWorldParent();
            if (previewParent != null)
            {
                m_PreviewRingObject.transform.SetParent(previewParent, false);
            }

            m_PreviewRingObject.layer = ResolvePreviewLayer();

            m_PreviewRingRenderer = m_PreviewRingObject.AddComponent<LineRenderer>();
            m_PreviewRingRenderer.loop = false;
            m_PreviewRingRenderer.useWorldSpace = true;
            m_PreviewRingRenderer.textureMode = LineTextureMode.Stretch;
            m_PreviewRingRenderer.alignment = LineAlignment.View;
            m_PreviewRingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_PreviewRingRenderer.receiveShadows = false;
            m_PreviewRingRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            m_PreviewRingRenderer.numCornerVertices = 4;
            m_PreviewRingRenderer.numCapVertices = 2;
            m_PreviewRingRenderer.sortingOrder = 200;

            Shader shader = AAAGame.Effect.EffectShaderAssetLoader.TryGet(PreviewShaderAssetPath);

            if (shader != null)
            {
                m_PreviewRingMaterial = new Material(shader)
                {
                    name = "CardPlacementPreviewRing_Material"
                };
                m_PreviewRingRenderer.material = m_PreviewRingMaterial;
            }
            else
            {
                Log.Error("[CardAreaMaterialOverlay] Shader is not ready: {0}", PreviewShaderAssetPath);
            }

            m_PreviewRingRenderer.enabled = false;
        }

        private void UpdatePreviewSoldiers(CardModel cardModel, IReadOnlyList<Vector3> previewSpawnPositions, bool isValid)
        {
            if (!isValid || cardModel == null || previewSpawnPositions == null || previewSpawnPositions.Count == 0)
            {
                HidePreviewSoldiers();
                return;
            }

            EnsurePreviewSoldierRoot();

            if (!TryGetPreviewPrefabAssetPath(cardModel, out string assetPath))
            {
                if (CanReusePreviewSoldiers(cardModel, previewSpawnPositions, string.Empty, true))
                {
                    return;
                }

                CacheRenderedPreviewSoldiers(cardModel, previewSpawnPositions, string.Empty, true);
                ShowFallbackPreviewSoldiers(previewSpawnPositions);
                return;
            }

            GameObject previewPrefab = GetOrLoadPreviewPrefab(assetPath);
            if (previewPrefab == null)
            {
                if (CanReusePreviewSoldiers(cardModel, previewSpawnPositions, assetPath, true))
                {
                    return;
                }

                CacheRenderedPreviewSoldiers(cardModel, previewSpawnPositions, assetPath, true);
                ShowFallbackPreviewSoldiers(previewSpawnPositions);
                return;
            }

            if (CanReusePreviewSoldiers(cardModel, previewSpawnPositions, assetPath, false))
            {
                return;
            }

            CacheRenderedPreviewSoldiers(cardModel, previewSpawnPositions, assetPath, false);
            EnsurePreviewSoldierInstances(previewSpawnPositions.Count, assetPath, previewPrefab);
            for (int i = 0; i < previewSpawnPositions.Count; i++)
            {
                PreviewSoldierInstance instance = m_PreviewSoldierInstances[i];
                if (instance?.GameObject == null)
                {
                    continue;
                }

                if (!instance.GameObject.activeSelf)
                {
                    instance.GameObject.SetActive(true);
                }

                instance.GameObject.transform.position = previewSpawnPositions[i] + Vector3.up * previewUnitHeightOffset;
            }

            for (int i = previewSpawnPositions.Count; i < m_PreviewSoldierInstances.Count; i++)
            {
                if (m_PreviewSoldierInstances[i]?.GameObject != null)
                {
                    GameObject instanceObject = m_PreviewSoldierInstances[i].GameObject;
                    if (instanceObject.activeSelf)
                    {
                        instanceObject.SetActive(false);
                    }
                }
            }

            m_PreviewSoldiersVisible = true;
        }

        private bool CanReusePreviewSoldiers(
            CardModel cardModel,
            IReadOnlyList<Vector3> previewSpawnPositions,
            string assetPath,
            bool usesFallback)
        {
            if (!m_PreviewSoldiersVisible
                || !ReferenceEquals(m_LastRenderedPreviewCardModel, cardModel)
                || m_LastRenderedPreviewUsedFallback != usesFallback
                || !string.Equals(m_LastRenderedPreviewAssetPath, assetPath, System.StringComparison.Ordinal)
                || previewSpawnPositions == null
                || m_LastRenderedPreviewPositions.Count != previewSpawnPositions.Count)
            {
                return false;
            }

            for (int i = 0; i < previewSpawnPositions.Count; i++)
            {
                if ((m_LastRenderedPreviewPositions[i] - previewSpawnPositions[i]).sqrMagnitude > 0.0004f)
                {
                    return false;
                }
            }

            return true;
        }

        private void CacheRenderedPreviewSoldiers(
            CardModel cardModel,
            IReadOnlyList<Vector3> previewSpawnPositions,
            string assetPath,
            bool usesFallback)
        {
            m_LastRenderedPreviewCardModel = cardModel;
            m_LastRenderedPreviewAssetPath = assetPath;
            m_LastRenderedPreviewUsedFallback = usesFallback;
            m_LastRenderedPreviewPositions.Clear();

            if (previewSpawnPositions == null)
            {
                return;
            }

            for (int i = 0; i < previewSpawnPositions.Count; i++)
            {
                m_LastRenderedPreviewPositions.Add(previewSpawnPositions[i]);
            }
        }

        private void EnsurePreviewSoldierRoot()
        {
            if (m_PreviewSoldierRoot != null)
            {
                return;
            }

            GameObject rootObject = new GameObject("CardPlacementPreviewUnits");
            Transform previewParent = ResolvePreviewWorldParent();
            if (previewParent != null)
            {
                rootObject.transform.SetParent(previewParent, false);
            }

            int previewLayer = ResolvePreviewLayer();
            rootObject.layer = previewLayer;
            m_PreviewSoldierRoot = rootObject.transform;
        }

        private void EnsurePreviewSoldierInstances(int requiredCount, string assetPath, GameObject previewPrefab)
        {
            while (m_PreviewSoldierInstances.Count < requiredCount)
            {
                m_PreviewSoldierInstances.Add(null);
            }

            for (int i = 0; i < requiredCount; i++)
            {
                PreviewSoldierInstance existingInstance = m_PreviewSoldierInstances[i];
                bool needsRebuild = existingInstance == null
                    || existingInstance.GameObject == null
                    || existingInstance.IsFallback
                    || !string.Equals(existingInstance.SourceAssetPath, assetPath, System.StringComparison.Ordinal);

                if (!needsRebuild)
                {
                    continue;
                }

                if (existingInstance?.GameObject != null)
                {
                    Destroy(existingInstance.GameObject);
                }

                m_PreviewSoldierInstances[i] = CreatePrefabPreviewInstance(assetPath, previewPrefab);
            }
        }

        private PreviewSoldierInstance CreatePrefabPreviewInstance(string assetPath, GameObject previewPrefab)
        {
            GameObject instanceObject = Instantiate(previewPrefab, m_PreviewSoldierRoot);
            instanceObject.name = $"Preview_{previewPrefab.name}";
            instanceObject.transform.localScale *= previewUnitScaleMultiplier;

            PreparePreviewInstance(instanceObject);

            return new PreviewSoldierInstance
            {
                GameObject = instanceObject,
                SourceAssetPath = assetPath,
                IsFallback = false
            };
        }

        private void PreparePreviewInstance(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            int previewLayer = ResolvePreviewLayer();
            SetLayerRecursive(root, previewLayer);

            Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].detectCollisions = false;
            }

            ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.enabled = true;
            }
        }

        private void ShowFallbackPreviewSoldiers(IReadOnlyList<Vector3> previewSpawnPositions)
        {
            EnsurePreviewSoldierRoot();
            EnsureFallbackMaterial();

            while (m_PreviewSoldierInstances.Count < previewSpawnPositions.Count)
            {
                m_PreviewSoldierInstances.Add(null);
            }

            for (int i = 0; i < previewSpawnPositions.Count; i++)
            {
                PreviewSoldierInstance existingInstance = m_PreviewSoldierInstances[i];
                bool needsRebuild = existingInstance == null
                    || existingInstance.GameObject == null
                    || !existingInstance.IsFallback;

                if (needsRebuild)
                {
                    if (existingInstance?.GameObject != null)
                    {
                        Destroy(existingInstance.GameObject);
                    }

                    m_PreviewSoldierInstances[i] = CreateFallbackPreviewInstance();
                }

                PreviewSoldierInstance instance = m_PreviewSoldierInstances[i];
                if (!instance.GameObject.activeSelf)
                {
                    instance.GameObject.SetActive(true);
                }

                instance.GameObject.transform.position = previewSpawnPositions[i] + Vector3.up * previewUnitHeightOffset;
            }

            for (int i = previewSpawnPositions.Count; i < m_PreviewSoldierInstances.Count; i++)
            {
                if (m_PreviewSoldierInstances[i]?.GameObject != null)
                {
                    GameObject instanceObject = m_PreviewSoldierInstances[i].GameObject;
                    if (instanceObject.activeSelf)
                    {
                        instanceObject.SetActive(false);
                    }
                }
            }

            m_PreviewSoldiersVisible = true;
        }

        private PreviewSoldierInstance CreateFallbackPreviewInstance()
        {
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Preview_FallbackSoldier";
            capsule.transform.SetParent(m_PreviewSoldierRoot, false);
            capsule.transform.localScale = new Vector3(previewFallbackWidth, previewFallbackHeight * 0.5f, previewFallbackWidth);
            capsule.layer = ResolvePreviewLayer();

            Collider collider = capsule.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = capsule.GetComponent<Renderer>();
            if (renderer != null && m_PreviewFallbackMaterial != null)
            {
                renderer.sharedMaterial = m_PreviewFallbackMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return new PreviewSoldierInstance
            {
                GameObject = capsule,
                SourceAssetPath = string.Empty,
                IsFallback = true
            };
        }

        private void EnsureFallbackMaterial()
        {
            if (m_PreviewFallbackMaterial != null)
            {
                return;
            }

            Shader shader = AAAGame.Effect.EffectShaderAssetLoader.TryGet(PreviewShaderAssetPath);

            if (shader == null)
            {
                Log.Error("[CardAreaMaterialOverlay] Shader is not ready: {0}", PreviewShaderAssetPath);
                return;
            }

            m_PreviewFallbackMaterial = new Material(shader)
            {
                name = "CardPlacementPreviewFallback_Material"
            };

            if (m_PreviewFallbackMaterial.HasProperty("_Color"))
            {
                m_PreviewFallbackMaterial.color = previewFallbackColor;
            }
        }

        private void HidePreviewSoldiers()
        {
            if (!m_PreviewSoldiersVisible)
            {
                return;
            }

            for (int i = 0; i < m_PreviewSoldierInstances.Count; i++)
            {
                PreviewSoldierInstance instance = m_PreviewSoldierInstances[i];
                if (instance?.GameObject != null && instance.GameObject.activeSelf)
                {
                    instance.GameObject.SetActive(false);
                }
            }

            m_PreviewSoldiersVisible = false;
            m_LastRenderedPreviewCardModel = null;
            m_LastRenderedPreviewAssetPath = null;
            m_LastRenderedPreviewUsedFallback = false;
            m_LastRenderedPreviewPositions.Clear();
        }

        private bool TryGetPreviewPrefabAssetPath(CardModel cardModel, out string assetPath)
        {
            assetPath = null;
            if (cardModel == null || cardModel.DataProvider == null)
            {
                return false;
            }

            int soldierKey = (int)cardModel.DataProvider.SoldierIndex;
            if (m_PreviewAssetPathCache.TryGetValue(soldierKey, out assetPath))
            {
                return !string.IsNullOrWhiteSpace(assetPath);
            }

            if (!LogicRuntimeDataTableCache.IsPrepared)
            {
                return false;
            }

            string characterKey = cardModel.DataProvider.SoldierIndex.ToString();
            LogicRuntimeDataTableCache.TryGetCharacter(characterKey, out CharacterDataDetail tableRow);

            if (tableRow == null || string.IsNullOrWhiteSpace(tableRow.PrefabPath))
            {
                m_PreviewAssetPathCache[soldierKey] = string.Empty;
                return false;
            }

            assetPath = UtilityBuiltin.AssetsPath.GetEntityPath(tableRow.PrefabPath);
            m_PreviewAssetPathCache[soldierKey] = assetPath;
            return !string.IsNullOrWhiteSpace(assetPath);
        }

        private GameObject GetOrLoadPreviewPrefab(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            if (m_PreviewPrefabCache.TryGetValue(assetPath, out GameObject cachedPrefab) && cachedPrefab != null)
            {
                return cachedPrefab;
            }

            if (m_PendingPreviewPrefabLoads.Contains(assetPath))
            {
                return null;
            }

            if (GF.Resource == null || GF.Resource.HasAsset(assetPath) == HasAssetResult.NotExist)
            {
                return null;
            }

            m_PendingPreviewPrefabLoads.Add(assetPath);
            GF.Resource.LoadAsset(
                assetPath,
                typeof(GameObject),
                new LoadAssetCallbacks(OnPreviewPrefabLoadSuccess, OnPreviewPrefabLoadFailure));

            return null;
        }

        private void OnPreviewPrefabLoadSuccess(string assetName, object asset, float duration, object userData)
        {
            m_PendingPreviewPrefabLoads.Remove(assetName);
            if (asset is GameObject prefab)
            {
                m_PreviewPrefabCache[assetName] = prefab;
                RefreshLastPreviewSoldiers();
            }
        }

        private void OnPreviewPrefabLoadFailure(string assetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            m_PendingPreviewPrefabLoads.Remove(assetName);
            Log.Warning("[CardAreaMaterialOverlay] 预览士兵资源加载失败: {0}, status={1}, error={2}", assetName, status, errorMessage);
        }

        private void CacheLastPreviewRequest(CardModel cardModel, IReadOnlyList<Vector3> previewSpawnPositions, bool isValid)
        {
            m_LastPreviewCardModel = cardModel;
            m_LastPreviewValid = isValid;
            m_LastPreviewPositions.Clear();

            if (previewSpawnPositions == null)
            {
                return;
            }

            for (int i = 0; i < previewSpawnPositions.Count; i++)
            {
                m_LastPreviewPositions.Add(previewSpawnPositions[i]);
            }
        }

        private void ClearLastPreviewRequest()
        {
            m_LastPreviewCardModel = null;
            m_LastPreviewValid = false;
            m_LastPreviewPositions.Clear();
        }

        private void RefreshLastPreviewSoldiers()
        {
            if (!m_LastPreviewValid || m_LastPreviewCardModel == null || m_LastPreviewPositions.Count == 0)
            {
                return;
            }

            UpdatePreviewSoldiers(m_LastPreviewCardModel, m_LastPreviewPositions, true);
        }

        private Transform ResolvePreviewWorldParent()
        {
            if (validAreaObject != null && validAreaObject.transform.parent != null)
            {
                return validAreaObject.transform.parent;
            }

            if (invalidAreaObject != null && invalidAreaObject.transform.parent != null)
            {
                return invalidAreaObject.transform.parent;
            }

            return null;
        }

        private int ResolvePreviewLayer()
        {
            if (validAreaObject != null)
            {
                return validAreaObject.layer;
            }

            if (invalidAreaObject != null)
            {
                return invalidAreaObject.layer;
            }

            return 0;
        }

        private void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.layer = layer;
            }
        }

        private void ApplyOverlayMaterial(GameObject obj, Material overlayMaterial)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!m_OriginalMaterials.ContainsKey(renderer))
                {
                    m_OriginalMaterials[renderer] = renderer.sharedMaterials;
                }

                Material[] originalMats = m_OriginalMaterials[renderer];
                Material[] newMaterials = new Material[originalMats.Length + 1];
                for (int j = 0; j < originalMats.Length; j++)
                {
                    newMaterials[j] = originalMats[j];
                }

                newMaterials[originalMats.Length] = overlayMaterial;
                renderer.sharedMaterials = newMaterials;
            }
        }

        private void RemoveOverlayMaterial(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (m_OriginalMaterials.TryGetValue(renderer, out Material[] materials))
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<Renderer, Material[]> kvp in m_OriginalMaterials)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.sharedMaterials = kvp.Value;
                }
            }

            m_OriginalMaterials.Clear();

            if (m_PreviewRingMaterial != null)
            {
                Destroy(m_PreviewRingMaterial);
                m_PreviewRingMaterial = null;
            }

            if (m_PreviewFallbackMaterial != null)
            {
                Destroy(m_PreviewFallbackMaterial);
                m_PreviewFallbackMaterial = null;
            }

            if (m_PreviewRingObject != null)
            {
                Destroy(m_PreviewRingObject);
                m_PreviewRingObject = null;
            }

            if (m_PreviewSoldierRoot != null)
            {
                Destroy(m_PreviewSoldierRoot.gameObject);
                m_PreviewSoldierRoot = null;
            }
        }
    }
}
