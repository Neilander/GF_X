using System;
using UnityEngine;
using Random = UnityEngine.Random;


namespace AAAGame.Card
{
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
                
                // 检测区域合法性
                bool wasValid = m_IsValidPlacement;
                m_IsValidPlacement = CheckPlacementValidity(groundPosition);
                
                // 触发位置更新事件
                OnPositionUpdated?.Invoke(groundPosition, m_IsValidPlacement);
                
                // 如果合法性改变，触发事件
                if (wasValid != m_IsValidPlacement)
                {
                    OnValidityChanged?.Invoke(m_IsValidPlacement);
                }
            }
        }

        /// <summary>
        /// 确认放置卡牌
        /// </summary>
        public bool ConfirmPlacement(CardModel cardModel)
        {
            if (!m_IsPlacing || !m_IsValidPlacement)
            {
                return false;
            }

            // 生成士兵
            int soldierCount = SpawnSoldiers(cardModel, m_CurrentPlacementPosition);
            
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
            groundPosition = Vector3.zero;
            
            if (m_MainCamera == null)
            {
                m_MainCamera = Camera.main;
                if (m_MainCamera == null) return false;
            }

            Ray ray = m_MainCamera.ScreenPointToRay(Input.mousePosition);
            
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
            // 检测是否在禁止区域
            Collider[] forbiddenColliders = Physics.OverlapSphere(
                position, m_DetectionRadius, m_ForbiddenLayer);
            
            if (forbiddenColliders.Length > 0)
            {
                return false;
            }

            // 检测是否在地面上
            Collider[] groundColliders = Physics.OverlapSphere(
                position, m_DetectionRadius, m_GroundLayer);
            
            return groundColliders.Length > 0;
        }

        /// <summary>
        /// 生成士兵
        /// </summary>
        private int SpawnSoldiers(CardModel cardModel, Vector3 centerPosition)
        {
            ICardDataProvider dataProvider = cardModel.DataProvider;
            if (dataProvider == null || dataProvider.SoldierPrefab == null)
            {
                Debug.LogError("[Card] Cannot spawn soldiers: DataProvider or SoldierPrefab is null.");
                return 0;
            }

            int soldierCount = dataProvider.SoldierCount;
            float spawnRadius = dataProvider.SpawnRadius;
            GameObject soldierPrefab = dataProvider.SoldierPrefab;

            // 在圆形区域内随机生成士兵
            for (int i = 0; i < soldierCount; i++)
            {
                Vector2 num=Random.insideUnitCircle * spawnRadius;

                Vector3 randomOffset = new Vector3(num.x,num.y,0);
                Vector3 spawnPosition = centerPosition + new Vector3(randomOffset.x, 0, randomOffset.y);
                
                // 直接实例化
                GameObject soldier = UnityEngine.Object.Instantiate(soldierPrefab, spawnPosition, Quaternion.identity);
                soldier.name = $"{dataProvider.SoldierName}_{i}";
                
                Debug.Log($"[Card] Spawned soldier: {soldier.name} at {spawnPosition}");
            }

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
