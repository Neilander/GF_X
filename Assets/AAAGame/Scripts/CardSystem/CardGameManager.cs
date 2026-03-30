using UnityEngine;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌系统管理器 - 系统入口
    /// 创建 Controller，转发 Unity 生命周期，提供公共接口
    /// </summary>
    public class CardGameManager : GameFrameworkComponent
    {
        [Header("卡牌系统配置")]
        [SerializeField] private int initialHandSize = 4;
        [SerializeField] private int maxPopulation = 20;

        [Header("区域配置")]
        [SerializeField] private GameObject validAreaObject;
        [SerializeField] private GameObject invalidAreaObject;

        [Header("卡牌池配置")]
        [SerializeField] private List<CardData> cardPool = new List<CardData>();

        private CardSystemController cardSystemController;
        private bool isInitialized = false;

        public CardSystemController CardSystem => cardSystemController;

        protected override void Awake()
        {
            base.Awake();
        }

        private void Start()
        {
            InitializeCardSystem();
        }

        private void InitializeCardSystem()
        {
            if (isInitialized) return;

            cardSystemController = new CardSystemController();
            cardSystemController.Initialize();

            cardSystemController.SetMaxPopulation(maxPopulation);

            if (validAreaObject != null && invalidAreaObject != null)
            {
                cardSystemController.SetAreaObjects(validAreaObject, invalidAreaObject);
            }

            // 设置卡牌池
            if (cardPool.Count > 0)
            {
                List<ICardDataProvider> providers = new List<ICardDataProvider>();
                foreach (CardData card in cardPool)
                {
                    if (card != null)
                        providers.Add(new CardDataAdapter(card));
                }
                cardSystemController.SetCardPool(providers);
            }

            isInitialized = true;
            Debug.Log("[Card] CardGameManager initialized.");
        }

        private void Update()
        {
            if (!isInitialized || cardSystemController == null) return;
            cardSystemController.UpdatePlacement();
        }

        #region 公共接口

        public void DrawCards(int count)
        {
            cardSystemController?.DrawCards(count);
        }

        public bool PlayCard(CardModel cardModel)
        {
            return cardSystemController != null && cardSystemController.PlayCard(cardModel);
        }

        public void StartPlacement(CardModel cardModel)
        {
            cardSystemController?.StartPlacement(cardModel);
        }

        public bool ConfirmPlacement(CardModel cardModel)
        {
            return cardSystemController != null && cardSystemController.ConfirmPlacement(cardModel);
        }

        public void CancelPlacement()
        {
            cardSystemController?.CancelPlacement();
        }

        public bool DiscardCard(CardModel cardModel)
        {
            return cardSystemController != null && cardSystemController.DiscardCard(cardModel);
        }

        public PopulationModel GetPopulationModel()
        {
            return cardSystemController?.GetPopulationModel();
        }

        public PlayerHandModel GetHandModel()
        {
            return cardSystemController?.GetHandModel();
        }

        #endregion

        private void OnDestroy()
        {
            cardSystemController?.Shutdown();
        }
    }
}
