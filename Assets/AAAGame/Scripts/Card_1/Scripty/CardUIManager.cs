using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityGameFramework.Runtime;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 卡牌UI管理器 - 管理整个卡牌UI系统
/// </summary>
public class CardUIManager : MonoBehaviour
{
    private static CardUIManager instance;
    public static CardUIManager Instance => instance;

    [Header("UI引用")]
    [SerializeField] private GameObject cardUIPrefab;
    [SerializeField] private Transform handCardContainer;
    [SerializeField] private RectTransform handCardArea; // 手牌区域（用于检测拖回）
    [SerializeField] private TextMeshProUGUI populationText;
    [SerializeField] private GameObject trashBin;
    [SerializeField] private TextMeshProUGUI trashBinHintText;
    [SerializeField] private GameObject cardUIRoot; // 整个卡牌UI根节点

    [Header("场景引用")]
    [SerializeField] private CardPlacementIndicator placementIndicator;
    [SerializeField] private AreaIndicator areaIndicator; // 区域指示器（菲涅尔光效果）
    [SerializeField] private AreaMaterialOverlay areaMaterialOverlay; // 区域材质叠加器（新）
    [SerializeField] private Transform forbiddenArea; // 禁止部署区域
    [SerializeField] private Transform validArea; // 可放置区域（新增）

    [Header("快捷键")]
    [SerializeField] private KeyCode toggleUIKey = KeyCode.Tab;
    [SerializeField] private KeyCode[] cardHotkeys = new KeyCode[] 
    { 
        KeyCode.Alpha1, 
        KeyCode.Alpha2, 
        KeyCode.Alpha3, 
        KeyCode.Alpha4 
    };

    [Header("抽卡动画")]
    [Tooltip("卡牌堆位置（卡牌从这里生成）")]
    [SerializeField] private RectTransform cardDeckTransform; // 卡牌堆的 RectTransform
    [SerializeField] private float cardMoveToHandDuration = 0.5f; // 移动到手牌的时间

    [Header("测试功能")]
    [SerializeField] private bool showTestButtons = true; // 是否显示测试按钮

    private List<HandCardUI> handCardUIs = new List<HandCardUI>();
    private HandCardUI currentDraggingCard;
    private HandCardUI selectedCardByHotkey; // 快捷键选中的卡牌
    private int selectedCardIndex = -1; // 选中的卡牌索引
    private RectTransform trashBinRect;
    private bool isUIVisible = true;

    void Awake()
    {
        instance = this;
        if (trashBin != null)
        {
            trashBinRect = trashBin.GetComponent<RectTransform>();
            trashBin.SetActive(false);
        }
    }

    void Start()
    {
        // 订阅事件
        if (PlayerHandManager.Instance != null)
        {
            PlayerHandManager.Instance.OnHandChanged += OnHandChanged;
        }

        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.OnPopulationChanged += OnPopulationChanged;
        }

        // 初始化UI（播放抽卡动画）
        RefreshHandUI(playAnimation: true);
        UpdatePopulationUI();
    }

    void Update()
    {
        // 快捷键控制UI开关
        if (Input.GetKeyDown(toggleUIKey))
        {
            ToggleUI();
        }

        // 快捷键选择卡牌（1/2/3/4）
        for (int i = 0; i < cardHotkeys.Length; i++)
        {
            if (Input.GetKeyDown(cardHotkeys[i]))
            {
                SelectCardByHotkey(i);
            }
        }

        // 如果有选中的卡牌，鼠标左键点击场景使用卡牌
        if (selectedCardByHotkey != null && Input.GetMouseButtonDown(0))
        {
            TryUseSelectedCard();
        }

        // ESC 取消选中
        if (Input.GetKeyDown(KeyCode.Escape) && selectedCardByHotkey != null)
        {
            DeselectCard();
        }
    }

    #region UI显示控制

    /// <summary>
    /// 切换UI显示/隐藏
    /// </summary>
    public void ToggleUI()
    {
        isUIVisible = !isUIVisible;
        if (cardUIRoot != null)
        {
            cardUIRoot.SetActive(isUIVisible);
        }
        GF.Log($"卡牌UI {(isUIVisible ? "显示" : "隐藏")}");
    }

    /// <summary>
    /// 刷新手牌UI
    /// </summary>
    /// <param name="playAnimation">是否播放抽卡动画</param>
    private void RefreshHandUI(bool playAnimation = false)
    {
        // 清空现有UI
        foreach (var ui in handCardUIs)
        {
            if (ui != null) Destroy(ui.gameObject);
        }
        handCardUIs.Clear();

        // 创建新UI
        if (PlayerHandManager.Instance == null) return;

        var cards = PlayerHandManager.Instance.HandCards;
        foreach (var card in cards)
        {
            CreateCardUI(card, playAnimation);
        }
    }

    /// <summary>
    /// 创建单张卡牌UI
    /// </summary>
    /// <param name="cardData">卡牌数据</param>
    /// <param name="playAnimation">是否播放移动动画</param>
    private void CreateCardUI(CardData cardData, bool playAnimation = true)
    {
        if (cardUIPrefab == null || handCardContainer == null) return;

        var cardObj = Instantiate(cardUIPrefab, handCardContainer);
        var cardUI = cardObj.GetComponent<HandCardUI>();
        if (cardUI != null)
        {
            cardUI.SetCardData(cardData);
            handCardUIs.Add(cardUI);

            // 播放抽卡动画
            if (playAnimation)
            {
                // 等待一帧让布局系统计算好位置
                StartCoroutine(PlayDrawCardAnimation(cardUI));
            }
        }
    }

    /// <summary>
    /// 播放抽卡动画
    /// </summary>
    private System.Collections.IEnumerator PlayDrawCardAnimation(HandCardUI cardUI)
    {
        // 等待一帧，让布局系统计算好目标位置
        yield return null;

        // 获取卡牌堆的屏幕位置
        Vector2 spawnPosition;
        if (cardDeckTransform != null)
        {
            // 使用卡牌堆的位置
            spawnPosition = cardDeckTransform.position;
        }
        else
        {
            // 如果没有配置卡牌堆，使用屏幕中心作为默认位置
            spawnPosition = new Vector2(Screen.width / 2f, Screen.height / 2f);
            GF.LogWarning("未配置卡牌堆位置，使用屏幕中心作为默认位置");
        }

        // 从卡牌堆位置移动到手牌位置
        cardUI.MoveToHandFromScreenPosition(spawnPosition, cardMoveToHandDuration);
    }

    /// <summary>
    /// 更新人口UI
    /// </summary>
    private void UpdatePopulationUI()
    {
        if (populationText == null || PopulationManager.Instance == null) return;

        int current = PopulationManager.Instance.CurrentPopulation;
        int max = PopulationManager.Instance.MaxPopulation;
        populationText.text = $"{current}/{max}";

        // 人口不足时变红
        populationText.color = current >= max ? Color.red : Color.white;
    }

    #endregion

    #region 拖拽处理

    /// <summary>
    /// 卡牌开始拖拽
    /// </summary>
    public void OnCardBeginDrag(HandCardUI card)
    {
        currentDraggingCard = card;

        // 显示垃圾桶
        if (trashBin != null)
        {
            trashBin.SetActive(true);
        }

        // 显示放置指示器
        if (placementIndicator != null)
        {
            placementIndicator.Show();
        }
        
        // 显示区域指示器（旧方式，可选）
        if (areaIndicator != null)
        {
            areaIndicator.Show(true);
        }
    }

    /// <summary>
    /// 卡牌拖拽中
    /// </summary>
    public void OnCardDragging(HandCardUI card, Vector2 screenPosition)
    {
        // 更新垃圾桶提示
        UpdateTrashBinHint(screenPosition);

        // 更新放置指示器和区域指示器
        UpdatePlacementIndicator(screenPosition);
    }

    /// <summary>
    /// 卡牌结束拖拽
    /// </summary>
    public bool OnCardEndDrag(HandCardUI card, Vector2 screenPosition)
    {
        currentDraggingCard = null;

        // 隐藏垃圾桶
        if (trashBin != null)
        {
            trashBin.SetActive(false);
        }

        // 隐藏放置指示器
        if (placementIndicator != null)
        {
            placementIndicator.Hide();
        }
        
        // 隐藏区域指示器（旧方式）
        if (areaIndicator != null)
        {
            areaIndicator.Hide();
        }
        
        // 隐藏区域材质效果（新方式）
        if (areaMaterialOverlay != null)
        {
            areaMaterialOverlay.HideAreaEffect();
        }

        // 优先检查是否在手牌区域内（拖回手牌区域）
        if (IsOverHandCardArea(screenPosition))
        {
            GF.Log($"卡牌拖回手牌区域：{card.CardData.cardName}");
            return false; // 返回false，让卡牌返回原位
        }

        // 检查是否在垃圾桶上
        if (IsOverTrashBin(screenPosition))
        {
            DiscardCard(card);
            return true;
        }

        // 检查是否在场景中放置
        if (TryPlaceCardInScene(card, screenPosition))
        {
            return true;
        }

        return false;
    }

    #endregion

    #region 手牌区域检测

    /// <summary>
    /// 检查是否在手牌区域上
    /// </summary>
    private bool IsOverHandCardArea(Vector2 screenPosition)
    {
        if (handCardArea == null)
        {
            // 如果没有配置handCardArea，尝试使用handCardContainer的父级
            if (handCardContainer != null && handCardContainer.parent != null)
            {
                var parentRect = handCardContainer.parent.GetComponent<RectTransform>();
                if (parentRect != null)
                {
                    return RectTransformUtility.RectangleContainsScreenPoint(parentRect, screenPosition);
                }
            }
            return false;
        }
        
        return RectTransformUtility.RectangleContainsScreenPoint(handCardArea, screenPosition);
    }

    #endregion

    #region 垃圾桶逻辑

    /// <summary>
    /// 更新垃圾桶提示
    /// </summary>
    private void UpdateTrashBinHint(Vector2 screenPosition)
    {
        if (trashBinRect == null) return;

        bool isOver = RectTransformUtility.RectangleContainsScreenPoint(trashBinRect, screenPosition);

        if (trashBinHintText != null)
        {
            trashBinHintText.gameObject.SetActive(isOver);
        }
    }

    /// <summary>
    /// 检查是否在垃圾桶上
    /// </summary>
    private bool IsOverTrashBin(Vector2 screenPosition)
    {
        if (trashBinRect == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(trashBinRect, screenPosition);
    }

    /// <summary>
    /// 丢弃卡牌
    /// </summary>
    private void DiscardCard(HandCardUI card)
    {
        if (card == null || card.CardData == null) return;

        string cardName = card.CardData.cardName;

        // 从手牌管理器移除
        PlayerHandManager.Instance?.RemoveCard(card.CardData);

        Debug.Log($"丢弃了[{cardName}]");
        GF.Log($"丢弃了[{cardName}]");
    }

    #endregion

    #region 快捷键功能

    /// <summary>
    /// 通过快捷键选择卡牌
    /// </summary>
    private void SelectCardByHotkey(int index)
    {
        // 取消之前的选中
        if (selectedCardByHotkey != null)
        {
            selectedCardByHotkey.SetSelected(false);
        }

        // 检查索引是否有效
        if (index < 0 || index >= handCardUIs.Count)
        {
            GF.LogWarning($"卡牌索引 {index + 1} 超出范围，当前手牌数量：{handCardUIs.Count}");
            selectedCardByHotkey = null;
            selectedCardIndex = -1;
            return;
        }

        // 选中新卡牌
        selectedCardByHotkey = handCardUIs[index];
        selectedCardIndex = index;
        
        if (selectedCardByHotkey != null && selectedCardByHotkey.CardData != null)
        {
            // 检查人口是否足够
            if (PopulationManager.Instance != null && 
                !PopulationManager.Instance.HasEnoughPopulation(selectedCardByHotkey.CardData.populationCost))
            {
                GF.LogWarning($"人口不足，无法使用 {selectedCardByHotkey.CardData.cardName}");
                selectedCardByHotkey = null;
                selectedCardIndex = -1;
                return;
            }

            selectedCardByHotkey.SetSelected(true);
            GF.Log($"选中卡牌 {index + 1}：{selectedCardByHotkey.CardData.cardName}");
        }
    }

    /// <summary>
    /// 取消选中卡牌
    /// </summary>
    private void DeselectCard()
    {
        if (selectedCardByHotkey != null)
        {
            selectedCardByHotkey.SetSelected(false);
            GF.Log($"取消选中：{selectedCardByHotkey.CardData?.cardName}");
        }
        selectedCardByHotkey = null;
        selectedCardIndex = -1;
    }

    /// <summary>
    /// 尝试使用选中的卡牌
    /// </summary>
    private void TryUseSelectedCard()
    {
        if (selectedCardByHotkey == null || selectedCardByHotkey.CardData == null)
        {
            return;
        }

        // 获取鼠标点击的世界坐标
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            GF.LogWarning("未点击到有效位置");
            return;
        }

        Vector3 worldPos = hit.point;
        
        // 检查是否在禁止区域
        bool isForbidden = IsInForbiddenArea(worldPos);
        GF.Log($"快捷键使用卡牌：位置({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2})，是否禁止区域：{isForbidden}");
        
        if (isForbidden)
        {
            GF.LogWarning("禁止在此区域部署！");
            return;
        }

        // 扣除人口
        if (!PopulationManager.Instance.OccupyPopulation(selectedCardByHotkey.CardData.populationCost))
        {
            GF.LogWarning("人口不足！");
            return;
        }

        // 生成士兵
        SpawnSoldiers(selectedCardByHotkey.CardData, worldPos);

        // 输出日志
        string logMsg = $"在坐标({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2})召唤了[{selectedCardByHotkey.CardData.cardName}]x{selectedCardByHotkey.CardData.soldierCount}";
        Debug.Log(logMsg);
        GF.Log(logMsg);

        // 从手牌移除
        PlayerHandManager.Instance?.RemoveCard(selectedCardByHotkey.CardData);

        // 销毁UI
        Destroy(selectedCardByHotkey.gameObject);
        handCardUIs.Remove(selectedCardByHotkey);

        // 清除选中状态
        selectedCardByHotkey = null;
        selectedCardIndex = -1;
    }

    #endregion

    #region 场景放置逻辑

    /// <summary>
    /// 更新放置指示器
    /// </summary>
    private void UpdatePlacementIndicator(Vector2 screenPosition)
    {
        // 屏幕坐标转世界坐标
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            Vector3 worldPos = hit.point;
            
            // 检查是否在禁止区域
            bool isForbidden = IsInForbiddenArea(worldPos);
            
            // 检查是否在可放置区域
            bool isInValidArea = IsInValidArea(worldPos);
            
            GF.Log($"拖拽更新：位置({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}), 禁止区域:{isForbidden}, 可放置区域:{isInValidArea}, 碰撞:{hit.collider.name}");
            
            // 更新旧的指示器（如果存在）
            if (placementIndicator != null)
            {
                placementIndicator.UpdatePosition(worldPos);
                placementIndicator.SetValid(!isForbidden);
            }
            
            // 更新区域指示器（旧方式，可选）
            if (areaIndicator != null)
            {
                areaIndicator.UpdatePosition(worldPos);
                areaIndicator.SetValid(!isForbidden);
            }
            
            // 更新区域材质叠加效果（新方式）
            if (areaMaterialOverlay != null)
            {
                // 传递 isValid 参数：如果不在禁止区域则为 true
                bool isValid = !isForbidden;
                GF.Log($"调用 ShowAreaEffect: isValid={isValid}, isInValidArea={isInValidArea}");
                areaMaterialOverlay.ShowAreaEffect(worldPos, isValid);
            }
            else
            {
                GF.LogWarning("⚠️ areaMaterialOverlay 为 null，无法显示区域效果");
            }
        }
    }

    /// <summary>
    /// 尝试在场景中放置卡牌
    /// </summary>
    private bool TryPlaceCardInScene(HandCardUI card, Vector2 screenPosition)
    {
        if (card == null || card.CardData == null) return false;

        // 屏幕坐标转世界坐标
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            return false;
        }

        Vector3 worldPos = hit.point;

        // 检查是否在禁止区域
        if (IsInForbiddenArea(worldPos))
        {
            GF.LogWarning("禁止在此区域部署！");
            return false;
        }

        // 扣除人口
        if (!PopulationManager.Instance.OccupyPopulation(card.CardData.populationCost))
        {
            return false;
        }

        // 从手牌移除
        PlayerHandManager.Instance?.RemoveCard(card.CardData);

        // 生成士兵
        SpawnSoldiers(card.CardData, worldPos);

        // 输出日志
        string logMsg = $"在坐标({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2})召唤了[{card.CardData.cardName}]x{card.CardData.soldierCount}";
        Debug.Log(logMsg);
        GF.Log(logMsg);

        return true;
    }

    /// <summary>
    /// 检查是否在禁止区域（支持不规则形状）
    /// </summary>
    private bool IsInForbiddenArea(Vector3 worldPos)
    {
        if (forbiddenArea == null)
        {
            GF.LogWarning("⚠️ 禁止区域未配置（forbiddenArea为null）");
            return false;
        }

        // 使用 Collider 检测（支持不规则形状）
        Collider[] colliders = forbiddenArea.GetComponentsInChildren<Collider>();
        
        if (colliders.Length == 0)
        {
            GF.LogWarning("⚠️ 禁止区域没有 Collider 组件，无法检测");
            return false;
        }
        
        bool isInside = false;
        string hitColliderName = "";
        
        foreach (var collider in colliders)
        {
            // 使用 ClosestPoint 检测点是否在 Collider 内
            Vector3 closestPoint = collider.ClosestPoint(worldPos);
            float distance = Vector3.Distance(worldPos, closestPoint);
            
            // 如果距离非常小（小于0.01），说明点在 Collider 内部或表面
            if (distance < 0.01f)
            {
                isInside = true;
                hitColliderName = collider.name;
                break;
            }
        }
        
        // 详细日志
        GF.Log($"禁止区域检测（不规则形状）：");
        GF.Log($"  - 检测位置: ({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2})");
        GF.Log($"  - Collider 数量: {colliders.Length}");
        if (isInside)
        {
            GF.Log($"  - 碰撞对象: {hitColliderName}");
        }
        GF.Log($"  - 检测结果: {(isInside ? "在禁止区域内 ❌" : "不在禁止区域内 ✅")}");
        
        return isInside;
    }

    /// <summary>
    /// 检查是否在可放置区域（支持不规则形状）
    /// </summary>
    private bool IsInValidArea(Vector3 worldPos)
    {
        if (validArea == null)
        {
            GF.LogWarning("⚠️ 可放置区域未配置（validArea为null）");
            return false;
        }

        // 使用 Collider 检测（支持不规则形状）
        Collider[] colliders = validArea.GetComponentsInChildren<Collider>();
        
        if (colliders.Length == 0)
        {
            GF.LogWarning("⚠️ 可放置区域没有 Collider 组件，无法检测");
            return false;
        }
        
        bool isInside = false;
        string hitColliderName = "";
        
        foreach (var collider in colliders)
        {
            // 使用 ClosestPoint 检测点是否在 Collider 内
            Vector3 closestPoint = collider.ClosestPoint(worldPos);
            float distance = Vector3.Distance(worldPos, closestPoint);
            
            // 如果距离非常小（小于0.01），说明点在 Collider 内部或表面
            if (distance < 0.01f)
            {
                isInside = true;
                hitColliderName = collider.name;
                break;
            }
        }
        
        GF.Log($"可放置区域检测（不规则形状）：");
        GF.Log($"  - 检测位置: ({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2})");
        GF.Log($"  - Collider 数量: {colliders.Length}");
        if (isInside)
        {
            GF.Log($"  - 碰撞对象: {hitColliderName}");
        }
        GF.Log($"  - 检测结果: {(isInside ? "在可放置区域内 ✅" : "不在可放置区域内")}");
        
        return isInside;
    }

    /// <summary>
    /// 生成士兵
    /// </summary>
    /// <param name="cardData">卡牌数据</param>
    /// <param name="centerPosition">中心位置</param>
    private void SpawnSoldiers(CardData cardData, Vector3 centerPosition)
    {
        if (cardData == null)
        {
            GF.LogError("卡牌数据为空，无法生成士兵");
            return;
        }

        // 检查是否配置了士兵预制体
        if (cardData.soldierPrefab == null)
        {
            GF.LogWarning($"卡牌 {cardData.cardName} 未配置士兵预制体，跳过生成");
            return;
        }

        int soldierCount = cardData.soldierCount;
        float spawnRadius = cardData.spawnRadius;

        GF.Log($"开始生成 {soldierCount} 个 {cardData.soldierName}");

        // 根据士兵数量选择生成模式
        if (soldierCount == 1)
        {
            // 单个士兵：直接在中心点生成
            SpawnSingleSoldier(cardData, centerPosition);
        }
        else if (soldierCount == 2)
        {
            // 2个士兵：左右排列
            SpawnSoldiersInLine(cardData, centerPosition, soldierCount, spawnRadius);
        }
        else if (soldierCount <= 5)
        {
            // 3-5个士兵：圆形排列
            SpawnSoldiersInCircle(cardData, centerPosition, soldierCount, spawnRadius);
        }
        else
        {
            // 6个以上：网格排列
            SpawnSoldiersInGrid(cardData, centerPosition, soldierCount, spawnRadius);
        }
    }

    /// <summary>
    /// 生成单个士兵
    /// </summary>
    private void SpawnSingleSoldier(CardData cardData, Vector3 position)
    {
        GameObject soldier = Instantiate(cardData.soldierPrefab, position, Quaternion.identity);
        soldier.name = $"{cardData.soldierName}_1";
        GF.Log($"生成士兵：{soldier.name} 在位置 ({position.x:F2}, {position.y:F2}, {position.z:F2})");
    }

    /// <summary>
    /// 直线排列生成士兵
    /// </summary>
    private void SpawnSoldiersInLine(CardData cardData, Vector3 centerPosition, int count, float spacing)
    {
        float totalWidth = (count - 1) * spacing;
        float startX = centerPosition.x - totalWidth / 2f;

        for (int i = 0; i < count; i++)
        {
            Vector3 position = new Vector3(
                startX + i * spacing,
                centerPosition.y,
                centerPosition.z
            );

            GameObject soldier = Instantiate(cardData.soldierPrefab, position, Quaternion.identity);
            soldier.name = $"{cardData.soldierName}_{i + 1}";
            GF.Log($"生成士兵：{soldier.name} 在位置 ({position.x:F2}, {position.y:F2}, {position.z:F2})");
        }
    }

    /// <summary>
    /// 圆形排列生成士兵
    /// </summary>
    private void SpawnSoldiersInCircle(CardData cardData, Vector3 centerPosition, int count, float radius)
    {
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * radius,
                0,
                Mathf.Sin(angle) * radius
            );

            Vector3 position = centerPosition + offset;

            // 士兵朝向中心点
            Quaternion rotation = Quaternion.LookRotation(centerPosition - position);

            GameObject soldier = Instantiate(cardData.soldierPrefab, position, rotation);
            soldier.name = $"{cardData.soldierName}_{i + 1}";
            GF.Log($"生成士兵：{soldier.name} 在位置 ({position.x:F2}, {position.y:F2}, {position.z:F2})");
        }
    }

    /// <summary>
    /// 网格排列生成士兵
    /// </summary>
    private void SpawnSoldiersInGrid(CardData cardData, Vector3 centerPosition, int count, float spacing)
    {
        // 计算网格大小（尽量接近正方形）
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / cols);

        float gridWidth = (cols - 1) * spacing;
        float gridHeight = (rows - 1) * spacing;

        Vector3 startPos = centerPosition - new Vector3(gridWidth / 2f, 0, gridHeight / 2f);

        int soldierIndex = 0;
        for (int row = 0; row < rows && soldierIndex < count; row++)
        {
            for (int col = 0; col < cols && soldierIndex < count; col++)
            {
                Vector3 position = startPos + new Vector3(
                    col * spacing,
                    0,
                    row * spacing
                );

                GameObject soldier = Instantiate(cardData.soldierPrefab, position, Quaternion.identity);
                soldier.name = $"{cardData.soldierName}_{soldierIndex + 1}";
                GF.Log($"生成士兵：{soldier.name} 在位置 ({position.x:F2}, {position.y:F2}, {position.z:F2})");

                soldierIndex++;
            }
        }
    }

    #endregion

    #region 测试功能

    /// <summary>
    /// 测试：重新抽取手牌（带动画）
    /// </summary>
    [ContextMenu("测试：重新抽取手牌")]
    public void TestDrawCards()
    {
        if (PlayerHandManager.Instance == null)
        {
            GF.LogWarning("PlayerHandManager 不存在");
            return;
        }

        // 调用 PlayerHandManager 的重新抽卡方法
        PlayerHandManager.Instance.RedrawHand();
        GF.Log("测试：重新抽取手牌（播放动画）");
    }

    /// <summary>
    /// 测试：添加指定数量的卡牌（带动画）
    /// </summary>
    /// <param name="count">添加的卡牌数量</param>
    public void TestAddCards(int count = 1)
    {
        if (PlayerHandManager.Instance == null)
        {
            GF.LogWarning("PlayerHandManager 不存在");
            return;
        }

        // 使用随机抽卡系统添加指定数量的卡牌
        AddCardsWithAnimation(count);
        GF.Log($"测试：添加 {count} 张卡牌（播放动画）");
    }

    /// <summary>
    /// 测试：添加单张卡牌（带动画）
    /// </summary>
    [ContextMenu("测试：添加1张卡牌")]
    public void TestAddCard()
    {
        TestAddCards(1);
    }

    /// <summary>
    /// 测试：添加2张卡牌（带动画）
    /// </summary>
    [ContextMenu("测试：添加2张卡牌")]
    public void TestAdd2Cards()
    {
        TestAddCards(2);
    }

    /// <summary>
    /// 测试：添加3张卡牌（带动画）
    /// </summary>
    [ContextMenu("测试：添加3张卡牌")]
    public void TestAdd3Cards()
    {
        TestAddCards(3);
    }

    /// <summary>
    /// 添加指定数量的卡牌（带动画）
    /// 预留接口：可根据不同效果获取不同数量的随机卡牌
    /// </summary>
    /// <param name="count">添加的卡牌数量</param>
    /// <param name="effectType">效果类型（预留，可用于特殊抽卡效果）</param>
    public void AddCardsWithAnimation(int count, string effectType = "normal")
    {
        if (PlayerHandManager.Instance == null)
        {
            GF.LogWarning("PlayerHandManager 不存在");
            return;
        }

        // 记录添加前的手牌数量
        int beforeCount = PlayerHandManager.Instance.CurrentHandSize;

        // 根据效果类型调整抽卡逻辑（预留扩展）
        switch (effectType)
        {
            case "normal":
                // 普通抽卡
                PlayerHandManager.Instance.DrawRandomCards(count);
                break;
            
            case "rare":
                // 预留：稀有卡抽取（可以调整权重或从特定卡池抽取）
                // TODO: 实现稀有卡抽取逻辑
                PlayerHandManager.Instance.DrawRandomCards(count);
                GF.Log("稀有卡抽取效果（待实现）");
                break;
            
            case "epic":
                // 预留：史诗卡抽取
                // TODO: 实现史诗卡抽取逻辑
                PlayerHandManager.Instance.DrawRandomCards(count);
                GF.Log("史诗卡抽取效果（待实现）");
                break;
            
            default:
                PlayerHandManager.Instance.DrawRandomCards(count);
                break;
        }

        // 记录添加后的手牌数量
        int afterCount = PlayerHandManager.Instance.CurrentHandSize;
        int actualAdded = afterCount - beforeCount;

        GF.Log($"添加卡牌：请求 {count} 张，实际添加 {actualAdded} 张");
    }

    #endregion

    #region 事件回调

    private int lastHandCount = 0; // 记录上次手牌数量
    private bool isAddingCards = false; // 是否正在添加卡牌

    private void OnHandChanged(List<CardData> cards)
    {
        int currentCount = cards.Count;

        // 如果手牌为空，说明是清空操作，直接刷新不播放动画
        if (currentCount == 0)
        {
            RefreshHandUI(playAnimation: false);
            lastHandCount = 0;
        }
        // 如果手牌数量等于最大值且上次为0，说明是重新抽取，播放动画
        else if (currentCount == PlayerHandManager.Instance.MaxHandSize && lastHandCount == 0)
        {
            RefreshHandUI(playAnimation: true);
            lastHandCount = currentCount;
        }
        // 如果手牌数量增加，说明是添加卡牌，增量添加
        else if (currentCount > lastHandCount && !isAddingCards)
        {
            isAddingCards = true;
            // 使用协程增量添加，确保布局正确
            StartCoroutine(AddCardsIncremental(cards, lastHandCount, currentCount));
        }
        // 如果手牌数量减少，说明是移除卡牌，刷新UI
        else if (currentCount < lastHandCount)
        {
            RefreshHandUI(playAnimation: false);
            lastHandCount = currentCount;
        }
        else if (!isAddingCards)
        {
            // 数量相同，可能是替换，刷新UI
            RefreshHandUI(playAnimation: false);
            lastHandCount = currentCount;
        }
    }

    /// <summary>
    /// 增量添加卡牌（协程）
    /// </summary>
    private IEnumerator AddCardsIncremental(List<CardData> cards, int startIndex, int endCount)
    {
        // 等待一帧，确保之前的操作完成
        yield return null;

        // 增量添加新卡牌
        int addCount = endCount - startIndex;
        for (int i = startIndex; i < endCount; i++)
        {
            if (i < cards.Count)
            {
                CreateCardUI(cards[i], playAnimation: true);
            }
        }
        
        lastHandCount = endCount;
        isAddingCards = false;
        GF.Log($"增量添加 {addCount} 张卡牌完成");
    }

    private void OnPopulationChanged(int current, int max)
    {
        UpdatePopulationUI();

        // 更新所有卡牌的可拖拽状态
        foreach (var cardUI in handCardUIs)
        {
            cardUI?.UpdateDragability();
        }
    }

    #endregion

    void OnDestroy()
    {
        if (PlayerHandManager.Instance != null)
        {
            PlayerHandManager.Instance.OnHandChanged -= OnHandChanged;
        }

        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.OnPopulationChanged -= OnPopulationChanged;
        }

        if (instance == this)
        {
            instance = null;
        }
    }
}
