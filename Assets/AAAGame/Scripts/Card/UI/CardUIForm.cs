using System;
using UnityEngine;
using UnityEngine.UI;
using GameFramework;
using GameFramework.Event;
using UnityGameFramework.Runtime;
using System.Collections.Generic;
using TMPro;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌UI界面
    /// 继承自 UIFormBase，符合 GF_X UI 规范
    /// 使用 GF_X 对象池管理 HandCardItem
    /// </summary>
    public partial class CardUIForm : UIFormBase
    {
        [Header("UI容器")]
        [SerializeField] private Transform handCardContainer;
        [SerializeField] private RectTransform handCardArea;
        [SerializeField] private GameObject trashBin;
        [SerializeField] private TMPro.TextMeshProUGUI trashBinHintText;
        
        [Header("预制体")]
        [SerializeField] private GameObject handCardItemPrefab;
        
        [Header("人口显示")]
        [SerializeField] private PopulationView populationView;
        
        [Header("区域材质效果")]
        [SerializeField] private UI.CardAreaMaterialOverlay areaMaterialOverlay;
        
        [Header("抽卡动画")]
        [SerializeField] private RectTransform cardDeckTransform;
        [SerializeField] private float cardMoveToHandDuration = 0.5f;
        
        [Header("快捷键")]
        [SerializeField] private KeyCode toggleUIKey = KeyCode.Tab;
        
        private CardSystemController m_CardSystemController;
        private List<UIItemObject> m_HandCardItemObjects = new List<UIItemObject>();
        private HandCardItem m_DraggingCard;
        private RectTransform m_TrashBinRect;
        private bool m_IsUIVisible = true;
        

        
        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            
            // 初始化垃圾桶
            if (trashBin != null)
            {
                m_TrashBinRect = trashBin.GetComponent<RectTransform>();
                trashBin.SetActive(false);
            }
            
            // 订阅事件
            GFBuiltin.Event.Subscribe(CardDrawnEventArgs.EventId, OnCardDrawn);
            GFBuiltin.Event.Subscribe(CardPlayedEventArgs.EventId, OnCardPlayed);
            GFBuiltin.Event.Subscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
            GFBuiltin.Event.Subscribe(PopulationChangedEventArgs.EventId, OnPopulationChanged);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
          
            // 从 UIParams 获取 CardSystemController
            UIParams uiParams = userData as UIParams;
            if (uiParams != null)
            {
                m_CardSystemController = uiParams.Get("CardSystemController") as CardSystemController;
            }
            
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
            
            // Tab键切换UI显示/隐藏
            if (Input.GetKeyDown(toggleUIKey))
            {
                ToggleUIVisibility();
            }
            
            // 处理快捷键
            HandleHotkeys();
        }
        
        /// <summary>
        /// 切换UI显示/隐藏
        /// </summary>
        private void ToggleUIVisibility()
        {
            m_IsUIVisible = !m_IsUIVisible;
            gameObject.SetActive(m_IsUIVisible);
            Log.Info(Utility.Text.Format("Card UI {0}", m_IsUIVisible ? "显示" : "隐藏"));
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
            if (index < 0 || index >= m_HandCardItemObjects.Count) return;
            
            var itemObj = m_HandCardItemObjects[index];
            HandCardItem cardItem = itemObj.gameObject.GetComponent<HandCardItem>();
            
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
                CreateHandCardItem(cardModel, playAnimation: false);
            }
        }

        /// <summary>
        /// 创建手牌UI项 - 使用 GF_X 对象池
        /// </summary>
        private void CreateHandCardItem(CardModel cardModel, bool playAnimation = true)
        {
            if (handCardItemPrefab == null || handCardContainer == null)
            {
                Log.Error("HandCardItemPrefab or HandCardContainer is null.");
                return;
            }
            
            // 使用 GF_X 对象池创建 HandCardItem
            var itemObject = SpawnItem<UIItemObject>(handCardItemPrefab, handCardContainer);
            if (itemObject == null)
            {
                Log.Error("Failed to spawn HandCardItem from object pool.");
                return;
            }
            
            // 使用 gameObject 属性获取 GameObject
            HandCardItem cardItem = itemObject.gameObject.GetComponent<HandCardItem>();
            if (cardItem == null)
            {
                Log.Error("HandCardItem component not found on spawned object.");
                return;
            }
            
            cardItem.Initialize(cardModel, this);
            m_HandCardItemObjects.Add(itemObject);
            
            // 播放抽卡动画
            if (playAnimation && cardDeckTransform != null)
            {
                cardItem.MoveToHandFromScreenPosition(cardDeckTransform.position, cardMoveToHandDuration);
            }
        }

        /// <summary>
        /// 移除手牌UI项 - 回收到对象池
        /// </summary>
        private void RemoveHandCardItem(CardModel cardModel)
        {
            UIItemObject itemToRemove = null;
            foreach (var itemObj in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObj.gameObject.GetComponent<HandCardItem>();
                if (cardItem != null && cardItem.GetCardModel() == cardModel)
                {
                    itemToRemove = itemObj;
                    break;
                }
            }
            
            if (itemToRemove != null)
            {
                m_HandCardItemObjects.Remove(itemToRemove);
                UnspawnItem<UIItemObject>(handCardItemPrefab, itemToRemove);
            }
        }

        /// <summary>
        /// 清理所有手牌 - 回收到对象池
        /// </summary>
        private void ClearHandCards()
        {
            // 使用 GF_X 对象池回收所有 HandCardItem
            UnspawnAllItem<UIItemObject>(handCardItemPrefab);
            m_HandCardItemObjects.Clear();
        }

        /// <summary>
        /// 卡牌开始拖拽回调
        /// </summary>
        public void OnCardBeginDrag(HandCardItem cardItem)
        {
            m_DraggingCard = cardItem;
            
            // 显示垃圾桶
            if (trashBin != null)
            {
                trashBin.SetActive(true);
            }
            
            m_CardSystemController.StartPlacement(cardItem.GetCardModel());
        }

        /// <summary>
        /// 卡牌拖拽中回调
        /// </summary>
        public void OnCardDragging(HandCardItem cardItem, Vector2 screenPosition)
        {
            // 更新垃圾桶提示
            UpdateTrashBinHint(screenPosition);
            
            // 更新区域材质效果
            UpdateAreaMaterialEffect(screenPosition);
        }

        /// <summary>
        /// 卡牌结束拖拽回调
        /// </summary>
        public bool OnCardEndDrag(HandCardItem cardItem, Vector2 screenPosition)
        {
            m_DraggingCard = null;
            
            // 隐藏垃圾桶
            if (trashBin != null)
            {
                trashBin.SetActive(false);
            }
            
            // 隐藏区域材质效果
            if (areaMaterialOverlay != null)
            {
                areaMaterialOverlay.HideAreaEffect();
            }
            
            // 检查是否拖回手牌区域
            if (IsOverHandCardArea(screenPosition))
            {
                Log.Info("Card dragged back to hand");
                return false;
            }
            
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
        /// 更新垃圾桶提示
        /// </summary>
        private void UpdateTrashBinHint(Vector2 screenPosition)
        {
            if (m_TrashBinRect == null) return;
            
            bool isOver = RectTransformUtility.RectangleContainsScreenPoint(
                m_TrashBinRect, screenPosition, GFBuiltin.UICamera);
            
            if (trashBinHintText != null)
            {
                trashBinHintText.gameObject.SetActive(isOver);
            }
        }
        
        /// <summary>
        /// 更新区域材质效果
        /// </summary>
        private void UpdateAreaMaterialEffect(Vector2 screenPosition)
        {
            if (areaMaterialOverlay == null || Camera.main == null) return;
            
            // 发射射线
            Ray ray = Camera.main.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                areaMaterialOverlay.HideAreaEffect();
                return;
            }
            
            Vector3 worldPos = hit.point;
            
            // 检查是否在禁止区域
            bool isForbidden = m_CardSystemController.IsInForbiddenArea(worldPos);
            
            // 显示区域效果 (isValid = !isForbidden)
            areaMaterialOverlay.ShowAreaEffect(worldPos, !isForbidden);
        }
        
        /// <summary>
        /// 检查是否在手牌区域
        /// </summary>
        private bool IsOverHandCardArea(Vector2 screenPosition)
        {
            if (handCardArea == null)
            {
                if (handCardContainer != null && handCardContainer.parent != null)
                {
                    var parentRect = handCardContainer.parent.GetComponent<RectTransform>();
                    if (parentRect != null)
                    {
                        return RectTransformUtility.RectangleContainsScreenPoint(
                            parentRect, screenPosition, GFBuiltin.UICamera);
                    }
                }
                return false;
            }
            
            return RectTransformUtility.RectangleContainsScreenPoint(
                handCardArea, screenPosition, GFBuiltin.UICamera);
        }

        /// <summary>
        /// 检查是否在垃圾桶区域
        /// </summary>
        private bool IsInTrashBin(Vector2 screenPosition)
        {
            if (m_TrashBinRect == null) return false;
            
            return RectTransformUtility.RectangleContainsScreenPoint(
                m_TrashBinRect, screenPosition, GFBuiltin.UICamera);
        }

        #region 事件处理

        private void OnCardDrawn(object sender, GameEventArgs e)
        {
            CardDrawnEventArgs ne = (CardDrawnEventArgs)e;
            CreateHandCardItem(ne.CardModel, playAnimation: true);
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
            foreach (var itemObj in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObj.gameObject.GetComponent<HandCardItem>();
                if (cardItem != null)
                {
                    cardItem.UpdatePlayability();
                }
            }
        }

        #endregion
        
        #region 公共接口
        
        /// <summary>
        /// 重新抽卡 - 清空手牌并重新抽取
        /// </summary>
        public void RedrawCards()
        {
            if (m_CardSystemController == null) return;
            
            // 清空当前手牌
            PlayerHandModel handModel = m_CardSystemController.GetHandModel();
            if (handModel != null)
            {
                int cardCount = handModel.CardCount;
                handModel.Clear();
                
                // 重新抽卡
                m_CardSystemController.DrawCards(cardCount);
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
