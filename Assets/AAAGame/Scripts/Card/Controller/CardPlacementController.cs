using System;
using System.Collections.Generic;
using UnityEngine;

namespace AAAGame.Card
{
    public enum CardPlacementInvalidReason
    {
        None = 0,
        NotInExploredArea = 1,
        StaticForbiddenArea = 2,
        EnemyBuildingForbiddenArea = 3,
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

        private Vector3 m_CurrentPlacementPosition;
        private bool m_IsValidPlacement;
        private bool m_IsPlacing;
        private CardModel m_CurrentCardModel;

        private readonly List<Vector3> m_CachedPreviewSpawnPositions = new List<Vector3>();
        private Vector3 m_CachedPreviewCenterPosition;
        private Vector3 m_CachedPreviewResolvedPosition;
        private bool m_CachedPreviewResult;
        private float m_CachedPreviewTime;
        private CardModel m_CachedPreviewCard;
        private bool m_HasPreviewCache;

        private float m_DetectionRadius = 0.5f;
        private readonly Collider[] m_GroundOverlapBuffer = new Collider[OverlapBufferSize];
        private readonly RaycastHit[] m_GroundRaycastBuffer = new RaycastHit[OverlapBufferSize];

        public event Action<CardModel> OnPlacementStarted;
        public event Action<Vector3, bool> OnPositionUpdated;
        public event Action<bool> OnValidityChanged;
        public event Action<CardModel, Vector3> OnPlacementConfirmed;
        public event Action OnPlacementCancelled;

        public CardPlacementInvalidReason LastInvalidReason { get; private set; } = CardPlacementInvalidReason.None;

        public CardPlacementController()
        {
            m_MainCamera = Camera.main;
            m_GroundLayer = LayerMask.GetMask("Ground");
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

            Vector3 requestedPosition = groundPosition;
            isValid = EvaluatePlacementPreview(requestedPosition, previewSpawnPositions, out Vector3 resolvedPosition);
            if (isValid)
            {
                groundPosition = resolvedPosition;
            }

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

            Vector3 requestedPosition = groundPosition;
            if (!hasPreviewListeners)
            {
                return;
            }

            bool wasPlacementValid = m_IsValidPlacement;
            m_IsValidPlacement = EvaluatePlacementPreview(requestedPosition, null, out Vector3 resolvedPosition);
            m_CurrentPlacementPosition = m_IsValidPlacement ? resolvedPosition : requestedPosition;

            OnPositionUpdated?.Invoke(m_CurrentPlacementPosition, m_IsValidPlacement);
            if (wasPlacementValid != m_IsValidPlacement)
            {
                OnValidityChanged?.Invoke(m_IsValidPlacement);
            }
        }

        /// <summary>
        /// 确认放置卡牌。
        /// </summary>
        public bool TryCreatePlayCommandPayload(
            CardModel cardModel,
            Vector2? releaseScreenPosition,
            out FixVector2 selectedPosition)
        {
            selectedPosition = FixVector2.Zero;
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
                PlayCancelCreateSound();
                return false;
            }

            if (!TryResolveCardPlacement(
                    cardModel,
                    releaseGroundPosition,
                    null,
                    out Vector3 resolvedGroundPosition,
                    out _,
                    out CardPlacementInvalidReason invalidReason))
            {
                LastInvalidReason = invalidReason;
                LogInvalidPlacementReason(releaseGroundPosition, invalidReason);
                PlayCancelCreateSound();
                return false;
            }

            if (!CanSpawnCardAtPosition(cardModel, resolvedGroundPosition))
            {
                Debug.Log($"[Card] Cannot confirm placement: 预检测生成失败. requested={releaseGroundPosition}, resolved={resolvedGroundPosition}");
                PlayCancelCreateSound();
                return false;
            }

            m_CurrentPlacementPosition = resolvedGroundPosition;
            m_IsValidPlacement = true;

            selectedPosition = new FixVector2(
                (Fix64)m_CurrentPlacementPosition.x,
                (Fix64)m_CurrentPlacementPosition.z);
            EndPlacement();
            return true;
        }

        public void NotifyPlacementApplied(CardModel cardModel, FixVector2 selectedPosition)
        {
            if (cardModel == null)
                throw new ArgumentNullException(nameof(cardModel));
            OnPlacementConfirmed?.Invoke(
                cardModel,
                new Vector3((float)selectedPosition.x, 0f, (float)selectedPosition.y));
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

        private bool EvaluatePlacementPreview(Vector3 position, List<Vector3> previewSpawnPositions, out Vector3 resolvedPosition)
        {
            return TryGetCurrentCardPreviewSpawnPositionsCached(position, previewSpawnPositions, out resolvedPosition);
        }

        private bool TryGetCurrentCardPreviewSpawnPositionsCached(Vector3 centerPosition, List<Vector3> previewSpawnPositions, out Vector3 resolvedPosition)
        {
            previewSpawnPositions?.Clear();
            resolvedPosition = centerPosition;
            if (m_CurrentCardModel == null)
            {
                return false;
            }

            float formationRadius = GetCardFormationRadius(m_CurrentCardModel);
            float reuseDistance = Mathf.Max(0.25f, formationRadius * 0.2f);

            if (m_HasPreviewCache
                && ReferenceEquals(m_CachedPreviewCard, m_CurrentCardModel)
                && (centerPosition - m_CachedPreviewCenterPosition).sqrMagnitude <= reuseDistance * reuseDistance
                && Time.unscaledTime - m_CachedPreviewTime <= PreviewReuseInterval)
            {
                CopyCachedPreviewSpawnPositions(previewSpawnPositions);
                resolvedPosition = m_CachedPreviewResult ? m_CachedPreviewResolvedPosition : centerPosition;
                return m_CachedPreviewResult;
            }

            bool result = TryGetCardPreviewSpawnPositions(m_CurrentCardModel, centerPosition, m_CachedPreviewSpawnPositions, out resolvedPosition);
            if (result)
            {
                m_CurrentPlacementPosition = resolvedPosition;
            }

            m_CachedPreviewCenterPosition = centerPosition;
            m_CachedPreviewResolvedPosition = resolvedPosition;
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

        private bool TryGetCardPreviewSpawnPositions(CardModel cardModel, Vector3 centerPosition, List<Vector3> previewSpawnPositions, out Vector3 resolvedPosition)
        {
            resolvedPosition = centerPosition;
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

            return TryResolveCardPlacement(
                cardModel,
                centerPosition,
                previewSpawnPositions,
                out resolvedPosition,
                out _,
                out _);
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
            int hitCount = Physics.RaycastNonAlloc(ray, m_GroundRaycastBuffer, 1000f, m_GroundLayer);
            if (hitCount >= m_GroundRaycastBuffer.Length)
            {
                throw new InvalidOperationException("Card placement ground raycast buffer is full.");
            }

            float nearestGroundDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = m_GroundRaycastBuffer[i];
                if (hit.collider.GetComponentInParent<MAEntity>() != null)
                {
                    continue;
                }

                if (hit.distance < nearestGroundDistance)
                {
                    nearestGroundDistance = hit.distance;
                    groundPosition = hit.point;
                }
            }

            return !float.IsPositiveInfinity(nearestGroundDistance);
        }

        private bool CheckPlacementValidity(Vector3 position)
        {
            return GetPlacementInvalidReason(position, m_DetectionRadius) == CardPlacementInvalidReason.None;
        }

        private CardPlacementInvalidReason GetPlacementInvalidReason(Vector3 position)
        {
            return GetPlacementInvalidReason(position, m_DetectionRadius);
        }

        private CardPlacementInvalidReason GetPlacementInvalidReason(Vector3 position, float radius)
        {
            float checkRadius = Mathf.Max(0.1f, radius);
            Fix64 logicRadius = m_CurrentCardModel != null
                ? ClusterSpawnSystem.CalculateAutoSpawnRadiusFixed(m_CurrentCardModel.GetTroopCount())
                : (Fix64)checkRadius;
            LogicCardPlacementInvalidReason logicReason = LogicCardPlacementAuthority.Evaluate(
                new FixVector2((Fix64)position.x, (Fix64)position.z),
                logicRadius);
            if (logicReason != LogicCardPlacementInvalidReason.None)
                return ConvertInvalidReason(logicReason);

            int groundCount = Physics.OverlapSphereNonAlloc(
                position,
                checkRadius,
                m_GroundOverlapBuffer,
                m_GroundLayer);
            return groundCount > 0
                ? CardPlacementInvalidReason.None
                : CardPlacementInvalidReason.NotOnGround;
        }

        private static CardPlacementInvalidReason ConvertInvalidReason(LogicCardPlacementInvalidReason reason)
        {
            return reason switch
            {
                LogicCardPlacementInvalidReason.None => CardPlacementInvalidReason.None,
                LogicCardPlacementInvalidReason.Unexplored => CardPlacementInvalidReason.NotInExploredArea,
                LogicCardPlacementInvalidReason.StaticForbiddenArea => CardPlacementInvalidReason.StaticForbiddenArea,
                LogicCardPlacementInvalidReason.EnemyBuildingForbiddenArea => CardPlacementInvalidReason.EnemyBuildingForbiddenArea,
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown logic card-placement reason."),
            };
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

            int agentTypeId = ClusterSpawnSystem.ResolveAgentTypeId(dataProvider.SoldierIndex);
            return ClusterSpawnSystem.CanSpawnCluster(centerPosition, soldierCount, GetCardFormationRadius(cardModel), 2f, true, agentTypeId);
        }

        private bool TryResolveCardPlacement(
            CardModel cardModel,
            Vector3 requestedCenter,
            List<Vector3> spawnPositions,
            out Vector3 resolvedCenter,
            out float formationRadius,
            out CardPlacementInvalidReason invalidReason)
        {
            resolvedCenter = requestedCenter;
            formationRadius = GetCardFormationRadius(cardModel);
            invalidReason = CardPlacementInvalidReason.SpawnFailed;
            spawnPositions?.Clear();

            if (cardModel == null || cardModel.DataProvider == null)
            {
                return false;
            }

            int soldierCount = cardModel.GetTroopCount();
            if (soldierCount <= 0)
            {
                return false;
            }

            List<Vector3> targetPositions = spawnPositions ?? m_CachedPreviewSpawnPositions;
            int agentTypeId = ClusterSpawnSystem.ResolveAgentTypeId(cardModel.DataProvider.SoldierIndex);
            bool resolved = ClusterSpawnSystem.TryResolvePreviewSpawnPositions(
                requestedCenter,
                soldierCount,
                formationRadius,
                2f,
                IsPlacementCenterAllowed,
                targetPositions,
                out resolvedCenter,
                agentTypeId);

            if (resolved)
            {
                invalidReason = CardPlacementInvalidReason.None;
                return true;
            }

            invalidReason = GetPlacementInvalidReason(requestedCenter, formationRadius);
            if (invalidReason == CardPlacementInvalidReason.None)
            {
                invalidReason = CardPlacementInvalidReason.SpawnFailed;
            }

            targetPositions.Clear();
            return false;
        }

        private bool IsPlacementCenterAllowed(Vector3 centerPosition, float radius)
        {
            return GetPlacementInvalidReason(centerPosition, radius) == CardPlacementInvalidReason.None;
        }

        private static float GetCardFormationRadius(CardModel cardModel)
        {
            int soldierCount = cardModel != null ? cardModel.GetTroopCount() : 0;
            return ClusterSpawnSystem.CalculateAutoSpawnRadius(soldierCount);
        }

        private void ResetPreviewCache()
        {
            m_HasPreviewCache = false;
            m_CachedPreviewCard = null;
            m_CachedPreviewCenterPosition = Vector3.zero;
            m_CachedPreviewResolvedPosition = Vector3.zero;
            m_CachedPreviewResult = false;
            m_CachedPreviewTime = 0f;
            m_CachedPreviewSpawnPositions.Clear();
        }

        private void LogInvalidPlacementReason(Vector3 position, CardPlacementInvalidReason invalidReason)
        {
            if (invalidReason == CardPlacementInvalidReason.SpawnFailed)
            {
                LogSpawnFailureDiagnostics(position);
                return;
            }

            if (invalidReason == CardPlacementInvalidReason.NotInExploredArea)
            {
                Debug.Log($"[Card] Cannot confirm placement: 松手位置尚未探索. pos={position}");
                return;
            }

            if (invalidReason == CardPlacementInvalidReason.StaticForbiddenArea)
            {
                Debug.Log($"[Card] Cannot confirm placement: 命中逻辑静态禁区. pos={position}");
                return;
            }

            if (invalidReason == CardPlacementInvalidReason.EnemyBuildingForbiddenArea)
            {
                Debug.Log($"[Card] Cannot confirm placement: 命中敌方逻辑建筑禁区. pos={position}, radius={m_DetectionRadius:F2}");
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

        private void LogSpawnFailureDiagnostics(Vector3 position)
        {
            if (m_CurrentCardModel == null || m_CurrentCardModel.DataProvider == null)
            {
                throw new InvalidOperationException("Cannot diagnose card spawn failure: current card data is missing.");
            }

            UnitType unitType = m_CurrentCardModel.DataProvider.SoldierIndex;
            int soldierCount = m_CurrentCardModel.GetTroopCount();
            float formationRadius = GetCardFormationRadius(m_CurrentCardModel);
            int agentTypeId = ClusterSpawnSystem.ResolveAgentTypeId(unitType);
            bool canSpawnIgnoringAgents = ClusterSpawnSystem.CanSpawnCluster(
                position,
                soldierCount,
                formationRadius,
                2f,
                false,
                agentTypeId);
            bool canSpawnAvoidingAgents = ClusterSpawnSystem.CanSpawnCluster(
                position,
                soldierCount,
                formationRadius,
                2f,
                true,
                agentTypeId);
            bool mediumGridCanSpawnIgnoringAgents = ClusterSpawnSystem.CanSpawnCluster(
                position,
                soldierCount,
                formationRadius,
                2f,
                false,
                AgentTypeHelper.MediumMovementTypeId);
            CardPlacementInvalidReason centerReason = GetPlacementInvalidReason(position, formationRadius);

            Debug.LogWarning(
                $"[CardPlacementDiagnostics] result=SpawnFailed unit={unitType} count={soldierCount} " +
                $"position={position} formationRadius={formationRadius:F2} agentType={agentTypeId} " +
                $"centerReason={centerReason} canSpawnIgnoringAgents={canSpawnIgnoringAgents} " +
                $"canSpawnAvoidingAgents={canSpawnAvoidingAgents} " +
                $"mediumGridCanSpawnIgnoringAgents={mediumGridCanSpawnIgnoringAgents}");
        }

        /// <summary>放置失败统一播 cancelCreate（程序状态错误"放置流程未开启"不算用户失败，不播）。</summary>
        private static void PlayCancelCreateSound()
        {
            if (AudioManager.Instance != null) AudioManager.Instance.Play("cancelCreate");
        }

    }
}
