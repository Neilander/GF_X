using UnityEngine;
using UnityEngine.UI;
using GameFramework;
using GameFramework.Event;
using UnityGameFramework.Runtime;
using System.Collections.Generic;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌UI界面
    /// 继承自 UIFormLogic，符合 GameFramework UI 规范
    /// </summary>
    public class CardUIForm : UIFormBase
    {
        [Header("UI容器")]
        [SerializeField] private Transform handCardContainer;
        [SerializeField] private Transform trashBinArea;
        
        [Header("预制体")]
        [SerializeField] private GameObject handCardItemPrefab;
        
        [Header("人口显示")]
        [SerializeField] private PopulationView populationView;
        
        private CardSystemController m_CardSystemController;
        private List<HandCardItem> m_HandCardItems = new List<HandCardItem>();
        private HandCardItem m_DraggingCard;
        
        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            
            // 订阅事件
            GFBuiltin.Event.Subscribe(CardDrawnEventArgs.EventId, OnCardDrawn);
            GFBuiltin.Event.Subscribe(CardPlayedEventArgs.EventId, OnCardPlayed);
            GFBuiltin.Event.Subscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
            GFBuiltin.Event.Subscribe(PopulationChangedEventArgs.EventId, OnPopulationChanged);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            
            m_CardSystemController = userData as CardSystemController;
            if (m_CardSystemController == null)
            {
                Log.Error("CardSystemController is null.");
                return;
            }
            
            // 初始化人口显示
            if (populationView != null)
            {
                PopulationModel populationModel = m_CardSystemController.GetPopulationModel();
                populationView.Initialize(populationModel);
            }
            
            // 初始化手牌显示
            RefreshHandCards();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
            
            // 清理手牌
            ClearHandCards();
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);
            
            // 处理快捷键
            HandleHotkeys();
        }

        /// <summary>
        /// 处理快捷键
        /// </summary>
        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                PlayCardByIndex(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                PlayCardByIndex(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                PlayCardByIndex(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                PlayCardByIndex(3);
            }
        }

        /// <summary>
        /// 通过索引打出卡牌
        /// </summary>
        private void PlayCardByIndex(int index)
        {
            if (index < 0 || index >= m_HandCardItems.Count) return;
            
            HandCardItem cardItem = m_HandCardItems[index];
            if (cardItem != null && cardItem.CanPlay())
            {
                cardItem.SetSelected(true);
                m_CardSystemController.PlayCard(cardItem.GetCardModel());
            }
        }

        /// <summary>
        /// 刷新手牌显示
        /// </summary>
        private void RefreshHandCards()
        {
            ClearHandCards();
            
            PlayerHandModel handModel = m_CardSystemController.GetHandModel();
            if (handModel == null) return;
            
            List<CardModel> cards = handModel.GetAllCards();
            foreach (var cardModel in cards)
            {
                CreateHandCardItem(cardModel);
            }
        }

        /// <summary>
        /// 创建手牌UI项
        /// </summary>
        private void CreateHandCardItem(CardModel cardModel)
        {
            if (handCardItemPrefab == null || handCardContainer == null)
            {
                Log.Error("HandCardItemPrefab or HandCardContainer is null.");
                return;
            }
            
            GameObject itemObj = Instantiate(handCardItemPrefab, handCardContainer);
            HandCardItem cardItem = itemObj.GetComponent<HandCardItem>();
            
            if (cardItem == null)
            {
                cardItem = itemObj.AddComponent<HandCardItem>();
            }
            
            cardItem.Initialize(cardModel, this);
            m_HandCardItems.Add(cardItem);
        }

        /// <summary>
        /// 移除手牌UI项
        /// </summary>
        private void RemoveHandCardItem(CardModel cardModel)
        {
            HandCardItem itemToRemove = m_HandCardItems.Find(item => item.GetCardModel() == cardModel);
            if (itemToRemove != null)
            {
                m_HandCardItems.Remove(itemToRemove);
                Destroy(itemToRemove.gameObject);
            }
        }

        /// <summary>
        /// 清理所有手牌
        /// </summary>
        private void ClearHandCards()
        {
            foreach (var cardItem in m_HandCardItems)
            {
                if (cardItem != null)
                {
                    Destroy(cardItem.gameObject);
                }
            }
            m_HandCardItems.Clear();
        }

        /// <summary>
        /// 卡牌开始拖拽回调
        /// </summary>
        public void OnCardBeginDrag(HandCardItem cardItem)
        {
            m_DraggingCard = cardItem;
            m_CardSystemController.StartPlacement(cardItem.GetCardModel());
        }

        /// <summary>
        /// 卡牌拖拽中回调
        /// </summary>
        public void OnCardDragging(HandCardItem cardItem, Vector2 screenPosition)
        {
            // 更新放置位置（由 CardPlacementController 处理）
        }

        /// <summary>
        /// 卡牌结束拖拽回调
        /// </summary>
        public bool OnCardEndDrag(HandCardItem cardItem, Vector2 screenPosition)
        {
            m_DraggingCard = null;
            
            // 检查是否在垃圾桶区域
            if (IsInTrashBin(screenPosition))
            {
                m_CardSystemController.DiscardCard(cardItem.GetCardModel());
                return true;
            }
            
            // 尝试确认放置
            return m_CardSystemController.ConfirmPlacement(cardItem.GetCardModel());
        }

        /// <summary>
        /// 检查是否在垃圾桶区域
        /// </summary>
        private bool IsInTrashBin(Vector2 screenPosition)
        {
            if (trashBinArea == null) return false;
            
            RectTransform trashBinRect = trashBinArea as RectTransform;
            if (trashBinRect == null) return false;
            
            return RectTransformUtility.RectangleContainsScreenPoint(
                trashBinRect, screenPosition, GFBuiltin.UICamera);
        }

        #region 事件处理

        private void OnCardDrawn(object sender, GameEventArgs e)
        {
            CardDrawnEventArgs ne = (CardDrawnEventArgs)e;
            CreateHandCardItem(ne.CardModel);
        }

        private void OnCardPlayed(object sender, GameEventArgs e)
        {
            CardPlayedEventArgs ne = (CardPlayedEventArgs)e;
            RemoveHandCardItem(ne.CardModel);
        }

        private void OnCardDiscarded(object sender, GameEventArgs e)
        {
            CardDiscardedEventArgs ne = (CardDiscardedEventArgs)e;
            RemoveHandCardItem(ne.CardModel);
        }

        private void OnPopulationChanged(object sender, GameEventArgs e)
        {
            // 更新所有手牌的可用状态
            foreach (var cardItem in m_HandCardItems)
            {
                cardItem.UpdatePlayability();
            }
        }

        #endregion

        protected override void OnRecycle()
        {
            base.OnRecycle();
            
            // 取消订阅事件（检查 GFBuiltin.Event 是否存在）
            if (GFBuiltin.Event != null)
            {
                GFBuiltin.Event.Unsubscribe(CardDrawnEventArgs.EventId, OnCardDrawn);
                GFBuiltin.Event.Unsubscribe(CardPlayedEventArgs.EventId, OnCardPlayed);
                GFBuiltin.Event.Unsubscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
                GFBuiltin.Event.Unsubscribe(PopulationChangedEventArgs.EventId, OnPopulationChanged);
            }
        }
    }
}
