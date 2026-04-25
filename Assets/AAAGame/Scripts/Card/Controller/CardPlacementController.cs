using System;
using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;
using UnityEngine;

namespace AAAGame.Card
{
    public enum CardPlacementInvalidReason
    {
        None = 0,
        NotInVisibleArea = 1,
        StaticForbiddenArea = 2,
        DynamicForbiddenArea = 3,
        NotOnGround = 4,
        SpawnFailed = 5
    }

    /// <summary>
    /// 卡牌放置控制器。
    /// 负责放置判定、预览以及最终士兵生成。
    /// </summary>
    public class CardPlacementController
    {
        private const float PreviewReuseInterval = 0.08f;
        private const int OverlapBufferSize = 32;

        private Camera m_MainCamera;
        private LayerMask m_GroundLayer;
        private LayerMask m_ForbiddenLayer;

        private Vector3 m_CurrentPlacementPosition;
        private bool m_IsValidPlacement;
        private bool m_IsPlacing;
        private CardModel m_CurrentCardModel;

        private readonly List<Vector3> m_CachedPreviewSpawnPositions = new List<Vector3>();
        private Vector3 m_CachedPreviewCenterPosition;
        private bool m_CachedPreviewResult;
        private float m_CachedPreviewTime;
        private CardModel m_CachedPreviewCard;
        private bool m_HasPreviewCache;

        private float m_DetectionRadius = 0.5f;
        private Func<Vector3, float, bool> m_AdditionalForbiddenChecker;
        private readonly Collider[] m_ForbiddenOverlapBuffer = new Collider[OverlapBufferSize];
        private readonly Collider[] m_GroundOverlapBuffer = new Collider[OverlapBufferSize];

        public event Action<CardModel> OnPlacementStarted;
        public event Action<Vector3, bool> OnPositionUpdated;
        public event Action<bool> OnValidityChanged;
        public event Action<CardModel, Vector3> OnPlacementConfirmed;
        public event Action OnPlacementCancelled;
        public event Action<CardModel, Vector3, int> OnSoldiersSpawned;

        public CardPlacementController()
        {
            m_MainCamera = Camera.main;
            m_GroundLayer = LayerMask.GetMask("Ground");
            m_ForbiddenLayer = LayerMask.GetMask("ForbiddenArea");
        }

        /// <summary>
        /// 设置检测半径。
        /// </summary>
        public void SetDetectionRadius(float radius)
        {
            m_DetectionRadius = Mathf.Max(0.1f, radius);
        }

        /// <summary>
        /// 当前放置检测半径。
        /// </summary>
        public float GetDetectionRadius()
        {
            return m_DetectionRadius;
        }

        /// <summary>
        /// 设置附加禁止区域检测。
        /// </summary>
        public void SetAdditionalForbiddenChecker(Func<Vector3, float, bool> checker)
        {
            m_AdditionalForbiddenChecker = checker;
        }

        /// <summary>
        /// 开始放置卡牌。
        /// </summary>
        public void StartPlacement(CardModel cardModel)
        {
            if (cardModel == null)
            {
                Debug.LogError("[Card] CardModel is null.");
                return;
            }

            m_IsPlacing = true;
            m_CurrentCardModel = cardModel;
            ResetPreviewCache();
            Debug.Log("[Card] Card placement started.");

            OnPlacementStarted?.Invoke(cardModel);
        }

        /// <summary>
        /// 获取指定屏幕点的放置预览结果。
        /// </summary>
        public bool TryGetPlacementPreview(Vector2 screenPosition, out Vector3 groundPosition, out bool isValid)
        {
            return TryGetPlacementPreview(screenPosition, out groundPosition, out isValid, null);
        }

        /// <summary>
        /// 获取指定屏幕点的放置预览结果，并返回与真实生成一致的预览士兵点位。
        /// </summary>
        public bool TryGetPlacementPreview(
            Vector2 screenPosition,
            out Vector3 groundPosition,
            out bool isValid,
            List<Vector3> previewSpawnPositions)
        {
            previewSpawnPositions?.Clear();
            isValid = false;

            if (!TryGetGroundPositionAtScreenPoint(screenPosition, out groundPosition))
            {
                return false;
            }

            isValid = EvaluatePlacementPreview(groundPosition, previewSpawnPositions);
            return true;
        }

        /// <summary>
        /// 更新放置位置。
        /// </summary>
        public void UpdatePlacement()
        {
            if (!m_IsPlacing)
            {
                return;
            }

            bool hasPreviewListeners = OnPositionUpdated != null || OnValidityChanged != null;
            if (!TryGetGroundPosition(out Vector3 groundPosition))
            {
                if (!hasPreviewListeners)
                {
                    return;
                }

                bool wasValid = m_IsValidPlacement;
                m_IsValidPlacement = false;
                LastInvalidReason = CardPlacementInvalidReason.NotOnGround;
                if (wasValid)
                {
                    OnValidityChanged?.Invoke(false);
                }

                return;
            }

            m_CurrentPlacementPosition = groundPosition;
            if (!hasPreviewListeners)
            {
                return;
            }

            bool wasPlacementValid = m_IsValidPlacement;
            m_IsValidPlacement = EvaluatePlacementPreview(groundPosition, null);

            OnPositionUpdated?.Invoke(groundPosition, m_IsValidPlacement);
            if (wasPlacementValid != m_IsValidPlacement)
            {
                OnValidityChanged?.Invoke(m_IsValidPlacement);
            }
        }

        /// <summary>
        /// 确认放置卡牌。
        /// </summary>
        public bool ConfirmPlacement(CardModel cardModel, Vector2? releaseScreenPosition = null)
        {
            if (!m_IsPlacing)
            {
                Debug.Log("[Card] Cannot confirm placement: 放置流程未开启.");
                LastInvalidReason = CardPlacementInvalidReason.None;
                return false;
            }

            Vector2 screenPos = releaseScreenPosition ?? (Vector2)Input.mousePosition;
            if (!TryGetGroundPositionAtScreenPoint(screenPos, out Vector3 releaseGroundPosition))
            {
                Debug.Log("[Card] Cannot confirm placement: 松手时未命中 Ground.");
                LastInvalidReason = CardPlacementInvalidReason.NotOnGround;
                return false;
            }

            CardPlacementInvalidReason invalidReason = GetPlacementInvalidReason(releaseGroundPosition);
            if (invalidReason != CardPlacementInvalidReason.None)
            {
                LastInvalidReason = invalidReason;
                LogInvalidPlacementReason(releaseGroundPosition, invalidReason);
                return false;
            }

            if (!CanSpawnCardAtPosition(cardModel, releaseGroundPosition))
            {
                Debug.Log($"[Card] Cannot confirm placement: 预检测生成失败. pos={releaseGroundPosition}");
                return false;
            }

            m_CurrentPlacementPosition = releaseGroundPosition;
            m_IsValidPlacement = true;

            int soldierCount = SpawnSoldiers(cardModel, m_CurrentPlacementPosition);
            if (soldierCount <= 0)
            {
                Debug.Log("[Card] Cannot confirm placement: 生成点不合法或无法生成单位.");
                LastInvalidReason = CardPlacementInvalidReason.SpawnFailed;
                return false;
            }

            OnPlacementConfirmed?.Invoke(cardModel, m_CurrentPlacementPosition);

            EndPlacement();
            return true;
        }

        /// <summary>
        /// 取消放置。
        /// </summary>
        public void CancelPlacement()
        {
            if (!m_IsPlacing)
            {
                return;
            }

            OnPlacementCancelled?.Invoke();
            EndPlacement();
        }

        /// <summary>
        /// 获取当前放置位置。
        /// </summary>
        public Vector3 GetCurrentPlacementPosition()
        {
            return m_CurrentPlacementPosition;
        }

        /// <summary>
        /// 是否正在放置。
        /// </summary>
        public bool IsPlacing()
        {
            return m_IsPlacing;
        }

        /// <summary>
        /// 当前位置是否合法。
        /// </summary>
        public bool IsValidPlacement()
        {
            return m_IsValidPlacement;
        }

        /// <summary>
        /// 清理。
        /// </summary>
        public void Shutdown()
        {
            m_IsPlacing = false;
            m_IsValidPlacement = false;
            m_CurrentCardModel = null;
            ResetPreviewCache();
        }

        private void EndPlacement()
        {
            m_IsPlacing = false;
            m_IsValidPlacement = false;
            m_CurrentCardModel = null;
            ResetPreviewCache();
        }

        private bool EvaluatePlacementPreview(Vector3 position, List<Vector3> previewSpawnPositions)
        {
            if (!CheckPlacementValidity(position))
            {
                previewSpawnPositions?.Clear();
                return false;
            }

            return TryGetCurrentCardPreviewSpawnPositionsCached(position, previewSpawnPositions);
        }

        private bool TryGetCurrentCardPreviewSpawnPositionsCached(Vector3 centerPosition, List<Vector3> previewSpawnPositions)
        {
            previewSpawnPositions?.Clear();
            if (m_CurrentCardModel == null)
            {
                return false;
            }

            ICardDataProvider dataProvider = m_CurrentCardModel.DataProvider;
            float reuseDistance = dataProvider != null
                ? Mathf.Max(0.25f, dataProvider.SpawnRadius * 0.2f)
                : 0.25f;

            if (m_HasPreviewCache
                && ReferenceEquals(m_CachedPreviewCard, m_CurrentCardModel)
                && (centerPosition - m_CachedPreviewCenterPosition).sqrMagnitude <= reuseDistance * reuseDistance
                && Time.unscaledTime - m_CachedPreviewTime <= PreviewReuseInterval)
            {
                CopyCachedPreviewSpawnPositions(previewSpawnPositions);
                return m_CachedPreviewResult;
            }

            bool result = TryGetCardPreviewSpawnPositions(m_CurrentCardModel, centerPosition, m_CachedPreviewSpawnPositions);
            m_CachedPreviewCenterPosition = centerPosition;
            m_CachedPreviewResult = result;
            m_CachedPreviewTime = Time.unscaledTime;
            m_CachedPreviewCard = m_CurrentCardModel;
            m_HasPreviewCache = true;

            CopyCachedPreviewSpawnPositions(previewSpawnPositions);
            return result;
        }

        private void CopyCachedPreviewSpawnPositions(List<Vector3> previewSpawnPositions)
        {
            if (previewSpawnPositions == null)
            {
                return;
            }

            previewSpawnPositions.Clear();
            previewSpawnPositions.AddRange(m_CachedPreviewSpawnPositions);
        }

        private bool TryGetCardPreviewSpawnPositions(CardModel cardModel, Vector3 centerPosition, List<Vector3> previewSpawnPositions)
        {
            if (cardModel == null || previewSpawnPositions == null)
            {
                return false;
            }

            previewSpawnPositions.Clear();

            ICardDataProvider dataProvider = cardModel.DataProvider;
            if (dataProvider == null)
            {
                return false;
            }

            int soldierCount = cardModel.GetTroopCount();
            if (soldierCount <= 0)
            {
                return false;
            }

            return ClusterSpawnSystem.TryGetPreviewSpawnPositions(centerPosition, soldierCount, dataProvider.SpawnRadius, 2f, previewSpawnPositions);
        }

        private bool TryGetGroundPosition(out Vector3 groundPosition)
        {
            return TryGetGroundPositionAtScreenPoint(Input.mousePosition, out groundPosition);
        }

        private bool TryGetGroundPositionAtScreenPoint(Vector2 screenPosition, out Vector3 groundPosition)
        {
            groundPosition = Vector3.zero;

            if (m_MainCamera == null || !m_MainCamera.isActiveAndEnabled)
            {
                m_MainCamera = Camera.main;
            }

            if (m_MainCamera == null)
            {
                return false;
            }

            Ray ray = m_MainCamera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, m_GroundLayer))
            {
                groundPosition = hit.point;
                return true;
            }

            return false;
        }

        private bool CheckPlacementValidity(Vector3 position)
        {
            return GetPlacementInvalidReason(position) == CardPlacementInvalidReason.None;
        }

        private CardPlacementInvalidReason GetPlacementInvalidReason(Vector3 position)
        {
            if (!IsPositionInVisibleArea(position))
            {
                return CardPlacementInvalidReason.NotInVisibleArea;
            }

            int forbiddenCount = Physics.OverlapSphereNonAlloc(
                position,
                m_DetectionRadius,
                m_ForbiddenOverlapBuffer,
                m_ForbiddenLayer);
            if (forbiddenCount > 0)
            {
                return CardPlacementInvalidReason.StaticForbiddenArea;
            }

            if (m_AdditionalForbiddenChecker != null
                && m_AdditionalForbiddenChecker(position, m_DetectionRadius))
            {
                return CardPlacementInvalidReason.DynamicForbiddenArea;
            }

            int groundCount = Physics.OverlapSphereNonAlloc(
                position,
                m_DetectionRadius,
                m_GroundOverlapBuffer,
                m_GroundLayer);
            return groundCount > 0;
        }

        private bool CanSpawnCardAtPosition(CardModel cardModel, Vector3 centerPosition)
        {
            if (cardModel == null)
            {
                return false;
            }

            ICardDataProvider dataProvider = cardModel.DataProvider;
            if (dataProvider == null)
            {
                return false;
            }

            int soldierCount = cardModel.GetTroopCount();
            if (soldierCount <= 0)
            {
                return false;
            }

            return ClusterSpawnSystem.CanSpawnCluster(centerPosition, soldierCount, dataProvider.SpawnRadius, 2f);
        }

        private void ResetPreviewCache()
        {
            m_HasPreviewCache = false;
            m_CachedPreviewCard = null;
            m_CachedPreviewCenterPosition = Vector3.zero;
            m_CachedPreviewResult = false;
            m_CachedPreviewTime = 0f;
            m_CachedPreviewSpawnPositions.Clear();
        }

        private bool IsPositionInVisibleArea(Vector3 position)
        {
            Fog3Manager fogManager = Fog3Manager.Instance;
            if (fogManager == null || !fogManager.IsInitialized || fogManager.MapData == null)
            {
                return false;
            }

            return fogManager.IsPositionVisible(position);
        }

        private void LogInvalidPlacementReason(Vector3 position, CardPlacementInvalidReason invalidReason)
        {
            if (invalidReason == CardPlacementInvalidReason.NotInVisibleArea)
            {
                Fog3CellState fogState = ResolveFogCellState(position);
                Debug.Log($"[Card] Cannot confirm placement: 松手位置不在 Visible 区域. pos={position}, fogState={fogState}");
                return;
            }

            int forbiddenCount = Physics.OverlapSphereNonAlloc(
                position,
                m_DetectionRadius,
                m_ForbiddenOverlapBuffer,
                m_ForbiddenLayer);
            if (forbiddenCount > 0)
            {
                Debug.Log($"[Card] Cannot confirm placement: 命中静态禁区. pos={position}, forbiddenHits={forbiddenCount}");
                return;
            }

            if (invalidReason == CardPlacementInvalidReason.DynamicForbiddenArea)
            {
                Debug.Log($"[Card] Cannot confirm placement: 命中动态禁区. pos={position}, radius={m_DetectionRadius:F2}");
                return;
            }

            int groundCount = Physics.OverlapSphereNonAlloc(
                position,
                m_DetectionRadius,
                m_GroundOverlapBuffer,
                m_GroundLayer);
            if (groundCount == 0)
            {
                Debug.Log($"[Card] Cannot confirm placement: Ground 检测失败. pos={position}, radius={m_DetectionRadius:F2}");
            }
        }

        private Fog3CellState ResolveFogCellState(Vector3 position)
        {
            Fog3Manager fogManager = Fog3Manager.Instance;
            Fog3MapData mapData = fogManager != null ? fogManager.MapData : null;
            if (mapData == null)
            {
                return Fog3CellState.Outside;
            }

            if (!mapData.WorldToGrid(position, out int gridX, out int gridY))
            {
                return Fog3CellState.Outside;
            }

            return mapData.GetCellState(gridX, gridY);
        }

        private int SpawnSoldiers(CardModel cardModel, Vector3 centerPosition)
        {
            ICardDataProvider dataProvider = cardModel.DataProvider;
            if (dataProvider == null)
            {
                Debug.LogError("[Card] Cannot spawn soldiers: DataProvider is null.");
                return 0;
            }

            int soldierCount = cardModel.GetTroopCount();
            if (soldierCount <= 0)
            {
                Debug.LogWarning("[Card] Cannot spawn soldiers: soldier count is not positive.");
                return 0;
            }

            float spawnRadius = dataProvider.SpawnRadius;
            UnitType soldierIndex = dataProvider.SoldierIndex;

            string sourceBuildingInstanceId = cardModel.GetSourceBuildingInstanceId();
            if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
            {
                sourceBuildingInstanceId = null;
            }

            bool spawnSuccess = ClusterSpawnSystem.SpawnCluster(
                centerPosition,
                soldierCount,
                spawnRadius,
                2f,
                soldierIndex,
                SideType.PlayerSide,
                BrainType.SoldierAI,
                sourceBuildingInstanceId);

            if (!spawnSuccess)
            {
                Debug.LogWarning(
                    $"[Card] SpawnCluster failed. center={centerPosition}, count={soldierCount}, radius={spawnRadius:F2}, minDistance=2.00, unit={soldierIndex}");
                return 0;
            }

            OnSoldiersSpawned?.Invoke(cardModel, centerPosition, soldierCount);
            return soldierCount;
        }
    }
}
