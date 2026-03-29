using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AAAGame.Card.Standalone
{
    /// <summary>
    /// 独立卡牌系统 - 不依赖 GameFramework
    /// 可以直接在任何场景中使用
    /// </summary>
    public class StandaloneCardSystem : MonoBehaviour
    {
        [Header("UI 配置")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private Transform handCardContainer;
        [SerializeField] private GameObject handCardItemPrefab;
        [SerializeField] private Text populationText;
        [SerializeField] private Text handCountText;
        
        [Header("游戏配置")]
        [SerializeField] private int maxPopulation = 10;
        [SerializeField] private int maxHandCards = 8;
        [SerializeField] private int initialCardCount = 4;
        
        [Header("区域配置")]
        [SerializeField] private GameObject validArea;
        [SerializeField] private GameObject invalidArea;
        
        [Header("卡牌数据")]
        [SerializeField] private List<CardData> cardDataList;
        
        // 数据模型
        private int currentPopulation = 0;
        private List<StandaloneCardModel> handCards = new List<StandaloneCardModel>();
        private List<CardData> cardPool = new List<CardData>();
        
        // 放置相关
        private StandaloneCardModel placingCard;
        private GameObject placementPreview;
        private bool isValidPlacement;
        
        private System.Random random;

        void Start()
        {
            Initialize();
        }

        void Update()
        {
            UpdatePlacement();
            HandleInput();
        }

        private void Initialize()
        {
            random = new System.Random();
            
            // 初始化卡牌池
            if (cardDataList != null && cardDataList.Count > 0)
            {
                cardPool.AddRange(cardDataList);
                Debug.Log($"✓ 卡牌池初始化完成，共 {cardPool.Count} 张卡牌");
            }
            else
            {
                Debug.LogWarning("⚠️ 卡牌数据列表为空");
            }
            
            // 抽初始手牌
            for (int i = 0; i < initialCardCount; i++)
            {
                DrawCard();
            }
            
            UpdateUI();
            Debug.Log("✓ 独立卡牌系统初始化完成");
        }

        #region 卡牌抽取

        public bool DrawCard()
        {
            if (handCards.Count >= maxHandCards)
            {
                Debug.LogWarning("手牌已满");
                return false;
            }
            
            if (cardPool.Count == 0)
            {
                Debug.LogWarning("卡牌池为空");
                return false;
            }
            
            // 随机抽取卡牌
            CardData cardData = DrawRandomCard();
            if (cardData == null) return false;
            
            // 创建卡牌模型
            StandaloneCardModel card = new StandaloneCardModel(cardData);
            handCards.Add(card);
            
            // 创建 UI
            CreateHandCardUI(card);
            
            UpdateUI();
            Debug.Log($"抽取卡牌: {cardData.cardName}");
            return true;
        }

        private CardData DrawRandomCard()
        {
            if (cardPool.Count == 0) return null;
            
            // 计算总权重
            int totalWeight = 0;
            foreach (var card in cardPool)
            {
                totalWeight += card.dropWeight;
            }
            
            if (totalWeight <= 0)
            {
                return cardPool[random.Next(cardPool.Count)];
            }
            
            // 根据权重随机
            int randomValue = random.Next(totalWeight);
            int currentWeight = 0;
            
            foreach (var card in cardPool)
            {
                currentWeight += card.dropWeight;
                if (randomValue < currentWeight)
                {
                    return card;
                }
            }
            
            return cardPool[0];
        }

        #endregion

        #region UI 管理

        private void CreateHandCardUI(StandaloneCardModel card)
        {
            if (handCardItemPrefab == null || handCardContainer == null)
            {
                Debug.LogWarning("⚠️ 手牌预制体或容器未配置");
                return;
            }
            
            GameObject cardObj = Instantiate(handCardItemPrefab, handCardContainer);
            card.uiObject = cardObj;
            
            // 配置 UI（简化版）
            Text nameText = cardObj.GetComponentInChildren<Text>();
            if (nameText != null)
            {
                nameText.text = $"{card.cardName}\n人口:{card.populationCost}";
            }
            
            // 添加点击事件
            Button button = cardObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnCardClicked(card));
            }
        }

        private void OnCardClicked(StandaloneCardModel card)
        {
            if (!CanPlayCard(card))
            {
                Debug.LogWarning($"无法打出卡牌 {card.cardName}: 人口不足");
                return;
            }
            
            StartPlacement(card);
        }

        private void UpdateUI()
        {
            // 更新人口显示
            if (populationText != null)
            {
                populationText.text = $"人口: {currentPopulation}/{maxPopulation}";
            }
            
            // 更新手牌数量
            if (handCountText != null)
            {
                handCountText.text = $"手牌: {handCards.Count}/{maxHandCards}";
            }
        }

        #endregion

        #region 卡牌放置

        private void StartPlacement(StandaloneCardModel card)
        {
            placingCard = card;
            
            // 创建预览对象
            if (placementPreview == null)
            {
                placementPreview = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                placementPreview.name = "PlacementPreview";
                Destroy(placementPreview.GetComponent<Collider>());
            }
            
            placementPreview.SetActive(true);
            Debug.Log($"开始放置: {card.cardName}");
        }

        private void UpdatePlacement()
        {
            if (placingCard == null || placementPreview == null) return;
            
            // 获取鼠标位置
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit))
            {
                placementPreview.transform.position = hit.point;
                
                // 检测是否在有效区域
                isValidPlacement = IsInValidArea(hit.point);
                
                // 更新预览颜色
                Renderer renderer = placementPreview.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = isValidPlacement ? Color.green : Color.red;
                }
            }
        }

        private bool IsInValidArea(Vector3 position)
        {
            if (validArea == null) return true;
            
            Collider validCollider = validArea.GetComponent<Collider>();
            if (validCollider != null)
            {
                return validCollider.bounds.Contains(position);
            }
            
            return true;
        }

        private void ConfirmPlacement()
        {
            if (placingCard == null) return;
            
            if (!isValidPlacement)
            {
                Debug.LogWarning("无效的放置位置");
                return;
            }
            
            // 消耗人口
            currentPopulation += placingCard.populationCost;
            
            // 生成士兵（简化版）
            GameObject soldier = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            soldier.name = placingCard.cardName;
            soldier.transform.position = placementPreview.transform.position;
            
            // 从手牌移除
            RemoveCard(placingCard);
            
            // 清理放置状态
            CancelPlacement();
            
            UpdateUI();
            Debug.Log($"放置成功: {soldier.name}");
        }

        private void CancelPlacement()
        {
            placingCard = null;
            
            if (placementPreview != null)
            {
                placementPreview.SetActive(false);
            }
        }

        #endregion

        #region 卡牌操作

        private bool CanPlayCard(StandaloneCardModel card)
        {
            return (currentPopulation + card.populationCost) <= maxPopulation;
        }

        private void RemoveCard(StandaloneCardModel card)
        {
            handCards.Remove(card);
            
            if (card.uiObject != null)
            {
                Destroy(card.uiObject);
            }
        }

        public void DiscardCard(StandaloneCardModel card)
        {
            RemoveCard(card);
            UpdateUI();
            Debug.Log($"丢弃卡牌: {card.cardName}");
        }

        #endregion

        #region 输入处理

        private void HandleInput()
        {
            // 鼠标左键确认放置
            if (Input.GetMouseButtonDown(0) && placingCard != null)
            {
                ConfirmPlacement();
            }
            
            // 鼠标右键取消放置
            if (Input.GetMouseButtonDown(1) && placingCard != null)
            {
                CancelPlacement();
            }
            
            // F1 抽卡
            if (Input.GetKeyDown(KeyCode.F1))
            {
                DrawCard();
            }
            
            // F2 增加人口上限
            if (Input.GetKeyDown(KeyCode.F2))
            {
                maxPopulation += 5;
                UpdateUI();
                Debug.Log($"人口上限增加到: {maxPopulation}");
            }
        }

        #endregion

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("=== 独立卡牌系统 ===");
            GUILayout.Label("左键: 确认放置");
            GUILayout.Label("右键: 取消放置");
            GUILayout.Label("F1: 抽一张卡");
            GUILayout.Label("F2: 增加人口上限");
            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// 独立卡牌模型
    /// </summary>
    [System.Serializable]
    public class StandaloneCardModel
    {
        public string cardName;
        public int populationCost;
        public int dropWeight;
        public GameObject uiObject;
        
        public StandaloneCardModel(CardData data)
        {
            cardName = data.cardName;
            populationCost = data.populationCost;
            dropWeight = data.dropWeight;
        }
    }
}
