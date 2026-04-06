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
                //Debug.Log($"[Card] 初始化成功啦");
            }
            
            // 初始化手牌显示
            RefreshHandCards();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
            
            // 清理手牌
            ClearHandCards();
            populationView.Deinitialize();
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
                //Debug.Log($"[Card] Card drawn: {cardModel.GetCardName()}");
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
        /// 移除手牌UI项 - 回收到对象池（直接移除，不检查是否已存在）
        /// </summary>
        private void RemoveHandCardItemDirect(CardModel cardModel)
        {
            Log.Info($"[CardUI] RemoveHandCardItemDirect called for: {cardModel?.GetCardName()}");
            
            UIItemObject itemToRemove = null;
            foreach (var itemObj in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObj.gameObject.GetComponent<HandCardItem>();
                if (cardItem != null && cardItem.GetCardModel() == cardModel)
                {
                    itemToRemove = itemObj;
                    Log.Info($"[CardUI] Found matching card item: {cardItem.GetCardModel().GetCardName()}");
                    break;
                }
            }
            
            if (itemToRemove != null)
            {
                Log.Info($"[CardUI] Removing item from list and unspawning...");
                m_HandCardItemObjects.Remove(itemToRemove);
                UnspawnItem<UIItemObject>(handCardItemPrefab, itemToRemove);
                Log.Info($"[CardUI] ✅ Item removed. Remaining count: {m_HandCardItemObjects.Count}");
            }
            else
            {
                Log.Warning($"[CardUI] Could not find card item to remove for: {cardModel?.GetCardName()}");
            }
        }

        /// <summary>
        /// 移除手牌UI项 - 回收到对象池（通过事件调用，检查是否已移除）
        /// </summary>
        private void RemoveHandCardItem(CardModel cardModel)
        {
            Log.Info($"[CardUI] RemoveHandCardItem called for: {cardModel?.GetCardName()}");
            
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
                Log.Info($"[CardUI] Item removed. Remaining count: {m_HandCardItemObjects.Count}");
            }
            else
            {
                // 不报错，因为可能已经被直接移除了
                Log.Info($"[CardUI] Card item already removed or not found: {cardModel?.GetCardName()}");
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
            
            Log.Info($"[CardUI] OnCardEndDrag - Screen Position: {screenPosition}");
            
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
            
            // 检查是否在垃圾桶区域（优先检查）
            bool isInTrash = IsInTrashBin(screenPosition);
            Log.Info($"[CardUI] Is in trash bin: {isInTrash}");
            
            if (isInTrash)
            {
                Log.Info($"[CardUI] ✅ Discarding card: {cardItem.GetCardModel().GetCardName()}");
                
                // 先通知 HandCardItem 停止拖拽状态并播放消失动画
                cardItem.OnDiscardSuccess();
                
                // 先移除 UI（避免事件重复移除）
                RemoveHandCardItemDirect(cardItem.GetCardModel());
                
                // 调用 Controller 丢弃卡牌（更新数据模型 + 触发事件）
                // 注意：这会触发 OnCardDiscarded 事件，但 UI 已经移除了
                bool discarded = m_CardSystemController.DiscardCard(cardItem.GetCardModel());
                
                if (discarded)
                {
                    Log.Info("[CardUI] ✅ Card discarded and removed successfully");
                }
                else
                {
                    Log.Error("[CardUI] ❌ Failed to discard card in controller");
                }
                
                return true;
            }
            
            // 检查是否拖回手牌区域
            bool isOverHand = IsOverHandCardArea(screenPosition);
            Log.Info($"[CardUI] Is over hand area: {isOverHand}");
            
            if (isOverHand)
            {
                Log.Info("[CardUI] Card dragged back to hand");
                return false;
            }
            
            // 尝试确认放置到场景
            Log.Info("[CardUI] Attempting to place card in scene");
            bool placed = m_CardSystemController.ConfirmPlacement(cardItem.GetCardModel());
            Log.Info($"[CardUI] Card placement result: {placed}");
            
            if (placed)
            {
                // 卡牌成功打出到场景，播放消失动画并移除
                Log.Info($"[CardUI] ✅ Card placed successfully: {cardItem.GetCardModel().GetCardName()}");
                cardItem.OnPlaySuccess();
                
                // 注意：ConfirmPlacement 内部会触发 OnCardPlayed 事件
                // OnCardPlayed 事件会调用 RemoveHandCardItem
                // 所以这里不需要手动移除
            }
            else
            {
                Log.Info($"[CardUI] 放置失败");
                m_CardSystemController.CancelPlacement();
            }

            return placed;
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
            if (m_TrashBinRect == null)
            {
                Log.Warning("[CardUI] TrashBin RectTransform is null. Cannot detect trash bin area.");
                return false;
            }
            
            // 方法 1：使用 RectTransformUtility（在某些 Canvas 设置下可能不准确）
            Camera uiCamera = GFBuiltin.UICamera;
            bool result1 = RectTransformUtility.RectangleContainsScreenPoint(
                m_TrashBinRect, screenPosition, uiCamera);
            
            // 方法 2：手动计算屏幕坐标范围（更可靠）
            // 获取垃圾桶的 4 个角的世界坐标
            Vector3[] corners = new Vector3[4];
            m_TrashBinRect.GetWorldCorners(corners);
            
            // 转换为屏幕坐标
            Vector2[] screenCorners = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                if (uiCamera != null)
                {
                    screenCorners[i] = uiCamera.WorldToScreenPoint(corners[i]);
                }
                else
                {
                    // 如果没有 Camera，直接使用世界坐标（Overlay 模式）
                    screenCorners[i] = corners[i];
                }
            }
            
            // 计算屏幕坐标的边界
            float minX = Mathf.Min(screenCorners[0].x, screenCorners[1].x, screenCorners[2].x, screenCorners[3].x)/100;
            float maxX = Mathf.Max(screenCorners[0].x, screenCorners[1].x, screenCorners[2].x, screenCorners[3].x)/100;
            float minY = Mathf.Min(screenCorners[0].y, screenCorners[1].y, screenCorners[2].y, screenCorners[3].y)/100;
            float maxY = Mathf.Max(screenCorners[0].y, screenCorners[1].y, screenCorners[2].y, screenCorners[3].y)/100;
            
            // 检查鼠标位置是否在边界内
            bool result2 = screenPosition.x >= minX && screenPosition.x <= maxX &&
                          screenPosition.y >= minY && screenPosition.y <= maxY;
            
            // 使用方法 2 的结果（更可靠）
            return result2;
        }

        #region 事件处理

        private void OnCardDrawn(object sender, GameEventArgs e)
        {
            CardDrawnEventArgs ne = (CardDrawnEventArgs)e;
            //Debug.Log($"[Card] Card drawn: {ne.CardModel.GetCardName()}");
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
