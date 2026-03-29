using System.Collections.Generic;
using UnityEngine;

namespace AAAGame.Card.Standalone
{
    /// <summary>
    /// 最小化卡牌系统 - 完全独立，无需任何预制体
    /// 使用 OnGUI 显示所有 UI，可以直接运行
    /// </summary>
    public class MinimalCardSystem : MonoBehaviour
    {
        [Header("游戏配置")]
        [SerializeField] private int maxPopulation = 10;
        [SerializeField] private int maxHandCards = 8;
        [SerializeField] private int initialCardCount = 4;
        
        [Header("区域配置（可选）")]
        [SerializeField] private GameObject validArea;
        [SerializeField] private GameObject invalidArea;
        
        // 数据
        private int currentPopulation = 0;
        private List<SimpleCard> handCards = new List<SimpleCard>();
        private List<SimpleCard> cardPool = new List<SimpleCard>();
        
        // 放置
        private SimpleCard placingCard;
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
            
            // 创建默认卡牌池
            CreateDefaultCardPool();
            
            // 抽初始手牌
            for (int i = 0; i < initialCardCount; i++)
            {
                DrawCard();
            }
            
            Debug.Log("✓ 最小化卡牌系统初始化完成");
        }

        private void CreateDefaultCardPool()
        {
            // 创建一些默认卡牌
            cardPool.Add(new SimpleCard("步兵", 1, 10));
            cardPool.Add(new SimpleCard("弓箭手", 2, 8));
            cardPool.Add(new SimpleCard("骑兵", 3, 5));
            cardPool.Add(new SimpleCard("法师", 4, 3));
            cardPool.Add(new SimpleCard("坦克", 5, 2));
            
            Debug.Log($"✓ 创建默认卡牌池，共 {cardPool.Count} 种卡牌");
        }

        #region 卡牌操作

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
            
            // 随机抽取
            SimpleCard template = DrawRandomCard();
            if (template == null) return false;
            
            // 创建新卡牌实例
            SimpleCard card = new SimpleCard(template.name, template.populationCost, template.dropWeight);
            handCards.Add(card);
            
            Debug.Log($"抽取卡牌: {card.name}");
            return true;
        }

        private SimpleCard DrawRandomCard()
        {
            if (cardPool.Count == 0) return null;
            
            int totalWeight = 0;
            foreach (var card in cardPool)
            {
                totalWeight += card.dropWeight;
            }
            
            if (totalWeight <= 0)
            {
                return cardPool[random.Next(cardPool.Count)];
            }
            
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

        public bool PlayCard(int index)
        {
            if (index < 0 || index >= handCards.Count)
            {
                return false;
            }
            
            SimpleCard card = handCards[index];
            
            if (!CanPlayCard(card))
            {
                Debug.LogWarning($"人口不足，无法打出 {card.name}");
                return false;
            }
            
            StartPlacement(card);
            return true;
        }

        private bool CanPlayCard(SimpleCard card)
        {
            return (currentPopulation + card.populationCost) <= maxPopulation;
        }

        public void DiscardCard(int index)
        {
            if (index < 0 || index >= handCards.Count)
            {
                return;
            }
            
            SimpleCard card = handCards[index];
            handCards.RemoveAt(index);
            Debug.Log($"丢弃卡牌: {card.name}");
        }

        #endregion

        #region 放置系统

        private void StartPlacement(SimpleCard card)
        {
            placingCard = card;
            
            // 创建预览
            if (placementPreview == null)
            {
                placementPreview = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                placementPreview.name = "PlacementPreview";
                Destroy(placementPreview.GetComponent<Collider>());
                
                // 设置半透明材质
                Renderer renderer = placementPreview.GetComponent<Renderer>();
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0, 1, 0, 0.5f);
                renderer.material = mat;
            }
            
            placementPreview.SetActive(true);
            Debug.Log($"开始放置: {card.name}");
        }

        private void UpdatePlacement()
        {
            if (placingCard == null || placementPreview == null) return;
            
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit))
            {
                placementPreview.transform.position = hit.point;
                
                // 检测区域
                isValidPlacement = IsInValidArea(hit.point);
                
                // 更新颜色
                Renderer renderer = placementPreview.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Color color = isValidPlacement ? new Color(0, 1, 0, 0.5f) : new Color(1, 0, 0, 0.5f);
                    renderer.material.color = color;
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
            
            // 生成士兵
            GameObject soldier = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            soldier.name = placingCard.name;
            soldier.transform.position = placementPreview.transform.position;
            
            // 添加颜色区分
            Renderer renderer = soldier.GetComponent<Renderer>();
            renderer.material.color = Random.ColorHSV();
            
            // 从手牌移除
            handCards.Remove(placingCard);
            
            // 清理
            CancelPlacement();
            
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

        #region 输入处理

        private void HandleInput()
        {
            // 放置确认/取消
            if (placingCard != null)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    ConfirmPlacement();
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    CancelPlacement();
                }
                return;
            }
            
            // 快捷键
            if (Input.GetKeyDown(KeyCode.F1))
            {
                DrawCard();
            }
            
            if (Input.GetKeyDown(KeyCode.F2))
            {
                maxPopulation += 5;
                Debug.Log($"人口上限增加到: {maxPopulation}");
            }
            
            // 数字键打出卡牌
            for (int i = 0; i < 8; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    PlayCard(i);
                }
            }
        }

        #endregion

        #region GUI 显示

        void OnGUI()
        {
            // 设置样式
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 20;
            titleStyle.fontStyle = FontStyle.Bold;
            
            GUIStyle cardStyle = new GUIStyle(GUI.skin.button);
            cardStyle.fontSize = 14;
            
            // 左上角信息
            GUILayout.BeginArea(new Rect(10, 10, 300, 400));
            
            GUILayout.Label("=== 最小化卡牌系统 ===", titleStyle);
            GUILayout.Space(10);
            
            GUILayout.Label($"人口: {currentPopulation}/{maxPopulation}");
            GUILayout.Label($"手牌: {handCards.Count}/{maxHandCards}");
            
            GUILayout.Space(20);
            GUILayout.Label("=== 操作说明 ===");
            GUILayout.Label("1-8: 打出对应位置的卡牌");
            GUILayout.Label("左键: 确认放置");
            GUILayout.Label("右键: 取消放置");
            GUILayout.Label("F1: 抽一张卡");
            GUILayout.Label("F2: 增加人口上限");
            
            GUILayout.EndArea();
            
            // 底部手牌
            float cardWidth = 120;
            float cardHeight = 80;
            float spacing = 10;
            float startX = (Screen.width - (cardWidth + spacing) * handCards.Count) / 2;
            float startY = Screen.height - cardHeight - 20;
            
            for (int i = 0; i < handCards.Count; i++)
            {
                SimpleCard card = handCards[i];
                Rect cardRect = new Rect(startX + i * (cardWidth + spacing), startY, cardWidth, cardHeight);
                
                // 检查是否可以打出
                bool canPlay = CanPlayCard(card);
                GUI.enabled = canPlay;
                
                string buttonText = $"{i + 1}. {card.name}\n人口: {card.populationCost}";
                
                if (GUI.Button(cardRect, buttonText, cardStyle))
                {
                    PlayCard(i);
                }
                
                GUI.enabled = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// 简单卡牌数据
    /// </summary>
    [System.Serializable]
    public class SimpleCard
    {
        public string name;
        public int populationCost;
        public int dropWeight;
        
        public SimpleCard(string name, int populationCost, int dropWeight)
        {
            this.name = name;
            this.populationCost = populationCost;
            this.dropWeight = dropWeight;
        }
    }
}
