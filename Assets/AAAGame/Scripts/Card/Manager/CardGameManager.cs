using UnityEngine;
using AAAGame.Card;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌游戏管理器 - 核心适配器
    /// 连接重构后的 Controller 和 Temp_script 的 Manager
    /// 作为整个卡牌系统的入口点
    /// </summary>
    public class CardGameManager : MonoBehaviour
    {
        private static CardGameManager instance;
        public static CardGameManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<CardGameManager>();
                }
                return instance;
            }
        }

        private CardSystemController cardSystemController;
        private bool isInitialized = false;

        public CardSystemController CardSystem => cardSystemController;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        void Start()
        {
            InitializeCardSystem();
        }

        /// <summary>
        /// 初始化卡牌系统
        /// </summary>
        private void InitializeCardSystem()
        {
            if (isInitialized)
            {
                Debug.LogWarning("[Card] CardGameManager already initialized.");
                return;
            }

            // 创建并初始化 Controller
            cardSystemController = new CardSystemController();
            cardSystemController.Initialize();

            // 设置人口上限

            // 连接到 Manager
            ConnectToManagers();

            // 订阅 Controller 事件
            SubscribeToControllerEvents();

            isInitialized = true;
            Debug.Log("[Card] CardGameManager initialized successfully.");
        }

        /// <summary>
        /// 连接到 Temp_script 的 Manager
        /// </summary>
        private void ConnectToManagers()
        {
            // 连接 PopulationManager
            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.SetCardSystemController(cardSystemController);
                Debug.Log("[Card] Connected to PopulationManager");
            }
            else
            {
                Debug.LogWarning("[Card] PopulationManager not found");
            }

        }

        /// <summary>
        /// 订阅 Controller 事件
        /// </summary>
        private void SubscribeToControllerEvents()
        {
            cardSystemController.OnHandChanged += OnHandChanged;
            cardSystemController.OnCardDrawn += OnCardDrawn;
            cardSystemController.OnCardPlayed += OnCardPlayed;
            cardSystemController.OnCardDiscarded += OnCardDiscarded;
        }

        void Update()
        {
            if (!isInitialized || cardSystemController == null)
            {
                return;
            }

            // 更新放置逻辑
            cardSystemController.UpdatePlacement();
        }

        #region Controller 事件回调

        private void OnHandChanged(int cardCount, int maxCards)
        {
            Debug.Log($"[Card] Hand changed: {cardCount}/{maxCards}");
        }

        private void OnCardDrawn(CardModel card)
        {
            Debug.Log($"[Card] Card drawn: {card.GetCardName()}");
        }

        private void OnCardPlayed(CardModel card)
        {
            Debug.Log($"[Card] Card played: {card.GetCardName()}");
        }

        private void OnCardDiscarded(CardModel card)
        {
            Debug.Log($"[Card] Card discarded: {card.GetCardName()}");
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 抽取指定数量的卡牌
        /// </summary>
        public void DrawCards(int count)
        {
            if (cardSystemController != null)
            {
                cardSystemController.DrawCards(count);
            }
        }

        /// <summary>
        /// 打出卡牌
        /// </summary>
        public bool PlayCard(CardModel cardModel)
        {
            if (cardSystemController != null)
            {
                return cardSystemController.PlayCard(cardModel);
            }
            return false;
        }

        /// <summary>
        /// 开始放置卡牌
        /// </summary>
        public void StartPlacement(CardModel cardModel)
        {
            if (cardSystemController != null)
            {
                cardSystemController.StartPlacement(cardModel);
            }
        }

        /// <summary>
        /// 确认放置
        /// </summary>
        public bool ConfirmPlacement(CardModel cardModel)
        {
            if (cardSystemController != null)
            {
                return cardSystemController.ConfirmPlacement(cardModel);
            }
            return false;
        }

        /// <summary>
        /// 取消放置
        /// </summary>
        public void CancelPlacement()
        {
            if (cardSystemController != null)
            {
                cardSystemController.CancelPlacement();
            }
        }

        /// <summary>
        /// 丢弃卡牌
        /// </summary>
        public bool DiscardCard(CardModel cardModel)
        {
            if (cardSystemController != null)
            {
                return cardSystemController.DiscardCard(cardModel);
            }
            return false;
        }

        #endregion

        void OnDestroy()
        {
            if (cardSystemController != null)
            {
                // 取消订阅事件
                cardSystemController.OnHandChanged -= OnHandChanged;
                cardSystemController.OnCardDrawn -= OnCardDrawn;
                cardSystemController.OnCardPlayed -= OnCardPlayed;
                cardSystemController.OnCardDiscarded -= OnCardDiscarded;

                // 清理
                cardSystemController.Shutdown();
            }

            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
