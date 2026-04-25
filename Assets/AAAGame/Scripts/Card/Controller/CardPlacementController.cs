using System;
using AAAGame.MiniMap.FOG3;
using UnityEngine;
using Random = UnityEngine.Random;


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
    /// 卡牌放置控制器
    /// 负责卡牌放置逻辑、区域检测、士兵生成
    /// </summary>
    public class CardPlacementController
    {
        private Camera m_MainCamera;
        private LayerMask m_GroundLayer;
        private LayerMask m_ForbiddenLayer;

        private Vector3 m_CurrentPlacementPosition;
        private bool m_IsValidPlacement;
        private bool m_IsPlacing;

        // 区域检测配置
        private float m_DetectionRadius = 0.5f;
        private Func<Vector3, float, bool> m_AdditionalForbiddenChecker;

        public CardPlacementInvalidReason LastInvalidReason { get; private set; }

        // 事件回调
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
        /// 设置检测半径
        /// </summary>
        public void SetDetectionRadius(float radius)
        {
            m_DetectionRadius = radius;
        }

        /// <summary>
        /// 设置附加禁止区域检测（用于动态禁区）。
        /// </summary>
        public void SetAdditionalForbiddenChecker(Func<Vector3, float, bool> checker)
        {
            m_AdditionalForbiddenChecker = checker;
        }

        /// <summary>
        /// 开始放置卡牌
        /// </summary>
        public void StartPlacement(CardModel cardModel)
        {
            if (cardModel == null)
            {
                Debug.LogError("[Card] CardModel is null.");
                return;
            }

            m_IsPlacing = true;
            LastInvalidReason = CardPlacementInvalidReason.None;
            Debug.Log("[Card] Card placement started.");

            // 触发开始放置事件
            OnPlacementStarted?.Invoke(cardModel);
        }

        /// <summary>
        /// 更新放置位置（每帧调用）
        /// </summary>
        public void UpdatePlacement()
        {
            if (!m_IsPlacing) return;

            // 射线检测地面
            if (TryGetGroundPosition(out Vector3 groundPosition))
            {
                m_CurrentPlacementPosition = groundPosition;
                //Debug.Log($"[Card] Placement position updated: {groundPosition}");
                // 检测区域合法性
                bool wasValid = m_IsValidPlacement;
                CardPlacementInvalidReason invalidReason = GetPlacementInvalidReason(groundPosition);
                m_IsValidPlacement = invalidReason == CardPlacementInvalidReason.None;

                // 触发位置更新事件
                OnPositionUpdated?.Invoke(groundPosition, m_IsValidPlacement);

                // 如果合法性改变，触发事件
                if (wasValid != m_IsValidPlacement)
                {
                    OnValidityChanged?.Invoke(m_IsValidPlacement);
                }
            }
            else
            {
                bool wasValid = m_IsValidPlacement;
                m_IsValidPlacement = false;
                LastInvalidReason = CardPlacementInvalidReason.NotOnGround;
                if (wasValid)
                {
                    OnValidityChanged?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// 确认放置卡牌
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

            m_CurrentPlacementPosition = releaseGroundPosition;
            m_IsValidPlacement = true;

            // 生成士兵
            int soldierCount = SpawnSoldiers(cardModel, m_CurrentPlacementPosition);
            if (soldierCount <= 0)
            {
                Debug.Log("[Card] Cannot confirm placement: 生成点不合法或无法生成单位.");
                LastInvalidReason = CardPlacementInvalidReason.SpawnFailed;
                return false;
            }

            LastInvalidReason = CardPlacementInvalidReason.None;

            // 触发放置成功事件
            OnPlacementConfirmed?.Invoke(cardModel, m_CurrentPlacementPosition);

            EndPlacement();
            return true;
        }

        /// <summary>
        /// 取消放置
        /// </summary>
        public void CancelPlacement()
        {
            if (!m_IsPlacing) return;

            // 触发取消事件
            OnPlacementCancelled?.Invoke();

            EndPlacement();
        }

        /// <summary>
        /// 结束放置
        /// </summary>
        private void EndPlacement()
        {
            m_IsPlacing = false;
            m_IsValidPlacement = false;
        }

        /// <summary>
        /// 尝试获取地面位置
        /// </summary>
        private bool TryGetGroundPosition(out Vector3 groundPosition)
        {
            return TryGetGroundPositionAtScreenPoint(Input.mousePosition, out groundPosition);
        }

        private bool TryGetGroundPositionAtScreenPoint(Vector2 screenPosition, out Vector3 groundPosition)
        {
            groundPosition = Vector3.zero;

            if (Camera.main != null)
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

        /// <summary>
        /// 检查放置位置合法性
        /// </summary>
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

            // 检测是否在禁止区域
            Collider[] forbiddenColliders = Physics.OverlapSphere(
                position, m_DetectionRadius, m_ForbiddenLayer);

            if (forbiddenColliders.Length > 0)
            {
                return CardPlacementInvalidReason.StaticForbiddenArea;
            }

            if (m_AdditionalForbiddenChecker != null
                && m_AdditionalForbiddenChecker(position, m_DetectionRadius))
            {
                return CardPlacementInvalidReason.DynamicForbiddenArea;
            }

            // 检测是否在地面上
            Collider[] groundColliders = Physics.OverlapSphere(
                position, m_DetectionRadius, m_GroundLayer);

            return groundColliders.Length > 0
                ? CardPlacementInvalidReason.None
                : CardPlacementInvalidReason.NotOnGround;
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

            if (invalidReason == CardPlacementInvalidReason.StaticForbiddenArea)
            {
                Collider[] forbiddenColliders = Physics.OverlapSphere(
                    position, m_DetectionRadius, m_ForbiddenLayer);
                Debug.Log($"[Card] Cannot confirm placement: 命中静态禁区. pos={position}, forbiddenHits={forbiddenColliders.Length}");
                return;
            }

            if (invalidReason == CardPlacementInvalidReason.DynamicForbiddenArea)
            {
                Debug.Log($"[Card] Cannot confirm placement: 命中动态禁区. pos={position}, radius={m_DetectionRadius:F2}");
                return;
            }

            if (invalidReason == CardPlacementInvalidReason.NotOnGround)
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

        /// <summary>
        /// 生成士兵
        /// </summary>
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
                sourceBuildingInstanceId = null;

            bool spawnSuccess = ClusterSpawnSystem.SpawnCluster(centerPosition, soldierCount, spawnRadius, 2f, soldierIndex, SideType.PlayerSide, BrainType.SoldierAI, sourceBuildingInstanceId);
            if (!spawnSuccess)
            {
                Debug.LogWarning($"[Card] SpawnCluster failed. center={centerPosition}, count={soldierCount}, radius={spawnRadius:F2}, minDistance=2.00, unit={soldierIndex}");
                return 0;
            }
            // 在圆形区域内随机生成士兵
            /*
            for (int i = 0; i < soldierCount; i++)
            {
                Vector2 num=Random.insideUnitCircle * spawnRadius;

                Vector3 randomOffset = new Vector3(num.x,num.y,0);
                Vector3 spawnPosition = centerPosition + new Vector3(randomOffset.x, 0, randomOffset.y);
                
                // 直接实例化
                //GameObject soldier = UnityEngine.Object.Instantiate(soldierPrefab, spawnPosition, Quaternion.identity);
                //soldier.name = $"{dataProvider.SoldierName}_{i}";
                
                //Debug.Log($"[Card] Spawned soldier: {soldier.name} at {spawnPosition}");
                Debug.LogError("这里不该用到");
            }*/

            // 触发士兵生成事件
            OnSoldiersSpawned?.Invoke(cardModel, centerPosition, soldierCount);

            return soldierCount;
        }

        /// <summary>
        /// 获取当前放置位置
        /// </summary>
        public Vector3 GetCurrentPlacementPosition()
        {
            return m_CurrentPlacementPosition;
        }

        /// <summary>
        /// 是否正在放置
        /// </summary>
        public bool IsPlacing()
        {
            return m_IsPlacing;
        }

        /// <summary>
        /// 当前位置是否合法
        /// </summary>
        public bool IsValidPlacement()
        {
            return m_IsValidPlacement;
        }

        /// <summary>
        /// 清理
        /// </summary>
        public void Shutdown()
        {
            m_IsPlacing = false;
            m_IsValidPlacement = false;
        }
    }
}
