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
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            GF.Event.Subscribe(CardDrawnEventArgs.EventId, OnCardDrawn);
            GF.Event.Subscribe(CardPlayedEventArgs.EventId, OnCardPlayed);
            GF.Event.Subscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
            GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
            GF.Event.Subscribe(ArmyBuildingCardPropertyChangedEventArgs.EventId, OnArmyBuildingCardPropertyChanged);
          
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
            
            // 初始化手牌显示
            RefreshHandCards();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (GF.Event != null)
            {
                try
                {
                    GF.Event.Unsubscribe(CardDrawnEventArgs.EventId, OnCardDrawn);
                    GF.Event.Unsubscribe(CardPlayedEventArgs.EventId, OnCardPlayed);
                    GF.Event.Unsubscribe(CardDiscardedEventArgs.EventId, OnCardDiscarded);
                    GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
                    GF.Event.Unsubscribe(ArmyBuildingCardPropertyChangedEventArgs.EventId, OnArmyBuildingCardPropertyChanged);
                }
                catch (System.Exception ex)
                {
                    Log.Info("CardUIForm: Error unsubscribing events: " + ex.Message);
                }
            }

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
            
            Log.Info($"[CardUI] ========== OnCardEndDrag START ==========");
            Log.Info($"[CardUI] Screen Position: {screenPosition}");
            
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
            
            // 优先级 1：检查是否在垃圾桶区域（最高优先级！）
            bool isInTrash = IsInTrashBin(screenPosition);
            Log.Info($"[CardUI] ✅ Is in trash bin: {isInTrash}");
            
            if (isInTrash)
            {
                Log.Info($"[CardUI] ✅✅✅ Card in trash bin, DISCARDING");
                
                // 取消放置（防止生成对象）
                m_CardSystemController.CancelPlacement();
                
                // 先通知 HandCardItem 停止拖拽状态并播放消失动画
                cardItem.OnDiscardSuccess();
                
                // 先移除 UI（避免事件重复移除）
                RemoveHandCardItemDirect(cardItem.GetCardModel());
                
                // 调用 Controller 丢弃卡牌（更新数据模型 + 触发事件）
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
            
            // 优先级 2：检查是否拖回手牌区域
            bool isOverHand = IsOverHandCardArea(screenPosition);
            Log.Info($"[CardUI] Is over hand area: {isOverHand}");
            
            if (isOverHand)
            {
                Log.Info("[CardUI] Card dragged back to hand, CANCELING PLACEMENT");
                m_CardSystemController.CancelPlacement();
                return false;
            }
            
            // 优先级 3：尝试确认放置到场景
            Log.Info("[CardUI] Attempting to place card in scene");
            bool placed = m_CardSystemController.ConfirmPlacement(cardItem.GetCardModel(), screenPosition);
            Log.Info($"[CardUI] Card placement result: {placed}");
            
            if (placed)
            {
                Log.Info($"[CardUI] ✅ Card placed successfully: {cardItem.GetCardModel().GetCardName()}");
                cardItem.OnPlaySuccess();
            }
            else
            {
                Log.Info($"[CardUI] ❌ Placement failed, canceling");
                m_CardSystemController.CancelPlacement();
            }

            Log.Info($"[CardUI] ========== OnCardEndDrag END (returned {placed}) ==========");
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
            RectTransform targetRect = null;
            
            // 优先使用 handCardArea
            if (handCardArea != null)
            {
                targetRect = handCardArea;
            }
            // 如果没有配置 handCardArea，使用 handCardContainer 的父级
            else if (handCardContainer != null && handCardContainer.parent != null)
            {
                targetRect = handCardContainer.parent.GetComponent<RectTransform>();
            }
            // 最后尝试使用 handCardContainer 本身
            else if (handCardContainer != null)
            {
                targetRect = handCardContainer.GetComponent<RectTransform>();
            }
            
            if (targetRect == null)
            {
                Log.Warning("[CardUI] No valid hand card area RectTransform found");
                return false;
            }
            
            // 尝试两种方式：使用 UICamera 和使用 null（Overlay 模式）
            Camera uiCamera = GFBuiltin.UICamera;
            
            // 方法 1：使用 UICamera
            bool result1 = RectTransformUtility.RectangleContainsScreenPoint(
                targetRect, screenPosition, uiCamera);
            
            // 方法 2：使用 null（适用于 Overlay 模式的 Canvas）
            bool result2 = RectTransformUtility.RectangleContainsScreenPoint(
                targetRect, screenPosition, null);
            
            Log.Info($"[CardUI] IsOverHandCardArea - With Camera: {result1}, Without Camera (null): {result2}");
            Log.Info($"[CardUI] Target rect: {targetRect.name}, Camera: {(uiCamera != null ? uiCamera.name : "null")}");
            Log.Info($"[CardUI] Screen Position: {screenPosition}");
            
            // 如果任何一个方法返回 true，就认为在手牌区域
            return result1 || result2;
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
            
            // 获取 Canvas 信息
            Canvas canvas = m_TrashBinRect.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Log.Warning("[CardUI] Cannot find Canvas for trash bin.");
                return false;
            }
            
            Camera uiCamera = null;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // Overlay 模式不需要相机
                uiCamera = null;
            }
            else
            {
                // Camera 模式使用 Canvas 的相机或 UICamera
                uiCamera = canvas.worldCamera ?? GFBuiltin.UICamera;
            }
            
            // 使用 RectTransformUtility.RectangleContainsScreenPoint（最可靠的方法）
            bool result = RectTransformUtility.RectangleContainsScreenPoint(
                m_TrashBinRect, screenPosition, uiCamera);
            
            Log.Info($"[CardUI] IsInTrashBin check: result={result}, Canvas mode={canvas.renderMode}");
            Log.Info($"[CardUI] Screen pos: {screenPosition}, TrashBin: {m_TrashBinRect.name}");
            
            return result;
        }

        #region 事件处理

        private void OnCardDrawn(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
                return;

            CardDrawnEventArgs ne = (CardDrawnEventArgs)e;
            if (ContainsCardItem(ne.CardModel))
            {
                Log.Info($"[CardUI] Skip duplicated draw event for card: {ne.CardModel?.GetCardName()}");
                return;
            }

            //Debug.Log($"[Card] Card drawn: {ne.CardModel.GetCardName()}");
            CreateHandCardItem(ne.CardModel, playAnimation: true);
        }

        private void OnCardPlayed(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
                return;

            CardPlayedEventArgs ne = (CardPlayedEventArgs)e;
            RemoveHandCardItem(ne.CardModel);
        }

        private void OnCardDiscarded(object sender, GameEventArgs e)
        {
            if (!ReferenceEquals(sender, m_CardSystemController))
                return;

            CardDiscardedEventArgs ne = (CardDiscardedEventArgs)e;
            RemoveHandCardItem(ne.CardModel);
        }

        private void OnIngameValueChanged(object sender, GameEventArgs e)
        {
            var ne = (IngameValueChangedEventArgs)e;
            if (ne.DataType != IngameValueType.CurrentSupply && ne.DataType != IngameValueType.MaxSupply)
                return;

            // 更新所有手牌的可用状态
            foreach (var itemObj in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObj.gameObject.GetComponent<HandCardItem>();
                if (cardItem != null)
                {
                    cardItem.RefreshView();
                }
            }
        }

        private void OnArmyBuildingCardPropertyChanged(object sender, GameEventArgs e)
        {
            var ne = (ArmyBuildingCardPropertyChangedEventArgs)e;
            if (string.IsNullOrWhiteSpace(ne.BuildingInstanceId))
                return;

            foreach (var itemObj in m_HandCardItemObjects)
            {
                HandCardItem cardItem = itemObj.gameObject.GetComponent<HandCardItem>();
                if (cardItem == null)
                    continue;

                CardModel cardModel = cardItem.GetCardModel();
                if (cardModel == null)
                    continue;

                if (!string.Equals(cardModel.GetSourceBuildingInstanceId(), ne.BuildingInstanceId, StringComparison.Ordinal))
                    continue;

                cardItem.RefreshView();
            }
        }

        private bool ContainsCardItem(CardModel cardModel)
        {
            if (cardModel == null)
                return false;

            for (int i = 0; i < m_HandCardItemObjects.Count; i++)
            {
                var itemObj = m_HandCardItemObjects[i];
                if (itemObj == null || itemObj.gameObject == null)
                    continue;

                HandCardItem cardItem = itemObj.gameObject.GetComponent<HandCardItem>();
                if (cardItem == null)
                    continue;

                if (ReferenceEquals(cardItem.GetCardModel(), cardModel))
                    return true;
            }

            return false;
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
        }
    }
}
