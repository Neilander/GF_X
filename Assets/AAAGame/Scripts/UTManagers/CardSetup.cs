using System.Collections;
using System.Collections.Generic;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Card;

public partial class CardSetup : GameFrameworkComponent
{
    private CardSystemController m_CardSystemController;
    private int m_CardUIFormId = -1;
    private float m_NextAutoDrawTime;
    private const float AutoDrawInterval = 0.15f;

    public void CardSystemSetup()
    {
        m_NextAutoDrawTime = 0f;
        InitializeCardSystem();
    }

    public void CardSystemUpdate()
    {
        if (m_CardSystemController != null)
        {
            if (CanAutoDrawNextCard())
            {
                if (m_CardSystemController.TryAutoDrawOneCardFromDeck())
                {
                    m_NextAutoDrawTime = Time.unscaledTime + AutoDrawInterval;
                }
            }

            m_CardSystemController.UpdatePlacement();
        }
    }

    private bool CanAutoDrawNextCard()
    {
        if (Time.unscaledTime < m_NextAutoDrawTime)
        {
            return false;
        }

        if (m_CardUIFormId <= 0 || GF.UI == null)
        {
            return false;
        }

        var uiForm = GF.UI.GetUIForm(m_CardUIFormId) as UIForm;
        var cardUIForm = uiForm != null ? uiForm.Logic as CardUIForm : null;
        return cardUIForm != null && cardUIForm.IsReadyForAutoDraw;
    }

    public void CardSystemShutdown()
    {
        // 关闭卡牌 UI（按序列号关闭，但优先播放面板关闭动画）
        if (GF.UI.IsLoadingUIForm(UIViews.CardUIForm) || GF.UI.HasUIForm(UIViews.CardUIForm))
        {
            var ui = GF.UI;
            if (ui != null && ui.HasUIForm(m_CardUIFormId))
            {
                try
                {
                    var uiForm = ui.GetUIForm(m_CardUIFormId) as UIForm;
                    var cardUIForm = uiForm != null ? uiForm.Logic as CardUIForm : null;
                    if (cardUIForm != null)
                    {
                        cardUIForm.CloseCardPanelWithAnimation();
                    }
                    else
                    {
                        ui.CloseUIForm(m_CardUIFormId);
                    }
                }
                catch (GameFrameworkException ex)
                {
                    Log.Warning("[CardGame] Close Card UI ignored: {0}", ex.Message);
                }
            }
            m_CardUIFormId = -1;
        }

        m_CardUIFormId = -1;

        // 清理卡牌系统
        if (m_CardSystemController != null)
        {
            m_CardSystemController.Shutdown();
            m_CardSystemController = null;
        }

        m_NextAutoDrawTime = 0f;
    }

    /// <summary>
    /// 初始化卡牌系统
    /// </summary>
    private void InitializeCardSystem()
    {
        m_CardSystemController = new CardSystemController();
        m_CardSystemController.Initialize();


        // 设置卡牌池（从 DataTable 或 ScriptableObject 加载）
        List<ICardDataProvider> cardPool = LoadCardPool();
        m_CardSystemController.SetCardPool(cardPool);

        // 每次进入战斗阶段先创建空卡组和空手牌
        m_CardSystemController.ResetDeckAndHand();

        // 设置区域对象（可放置区域和禁止区域）
        SetupAreaObjects();

        Log.Info("[CardGame] 卡牌系统初始化完成");
    }

    /// <summary>
    /// 加载卡牌池
    /// </summary>
    private List<ICardDataProvider> LoadCardPool()
    {
        List<ICardDataProvider> cardPool = new List<ICardDataProvider>();

        // TODO: 从 DataTable 或 Resources 加载卡牌数据
        // 示例：从 Resources 加载 CardData ScriptableObject
        CardData[] cardDataArray = Resources.LoadAll<CardData>("CardData");
        foreach (var cardData in cardDataArray)
        {
            // 使用 CardDataAdapter 包装 CardData
            cardPool.Add(new CardDataAdapter(cardData));
        }

        if (cardPool.Count == 0)
        {
            Log.Warning("[CardGame] 卡牌池为空，请检查卡牌数据配置");
        }

        return cardPool;
    }

    /// <summary>
    /// 设置区域对象
    /// </summary>
    private void SetupAreaObjects()
    {
        // TODO: 从场景中查找或创建可放置区域和禁止区域
        GameObject validArea = GameObject.Find("ValidArea");
        GameObject invalidArea = GameObject.Find("InvalidArea");

        if (validArea != null && invalidArea != null)
        {
            m_CardSystemController.SetAreaObjects(validArea, invalidArea);
        }
        else
        {
            Log.Warning("[CardGame] 未找到区域对象，卡牌放置功能可能无法正常工作");
        }
    }

    /// <summary>
    /// 打开卡牌 UI
    /// </summary>
    public void OpenCardUI()
    {
        if (GF.UI.IsLoadingUIForm(UIViews.CardUIForm) || GF.UI.HasUIForm(UIViews.CardUIForm))
        {
            Log.Info("[CardGame] 卡牌 UI 已在打开或加载中，跳过重复打开");
            return;
        }

        // 使用 GF.UI 打开 CardUIForm
        UIParams uiParams = UIParams.Create();
        uiParams.Set("CardSystemController", m_CardSystemController);

        m_CardUIFormId = GF.UI.OpenUIForm(UIViews.CardUIForm, uiParams);
        if (m_CardUIFormId == -1)
        {
            Log.Error("[CardGame] 打开卡牌 UI 失败");
        }
        else
        {
            Log.Info("[CardGame] 卡牌 UI 已打开");
        }
    }

    /// <summary>
    /// 在卡组中生成卡牌
    /// </summary>
    public void GenerateCardToDeck(BuildingEntity sourceBuilding)
    {
        m_CardSystemController.AddCardToDeck(sourceBuilding);
    }

    public bool AddCardToDeck(CardData cardData)
    {
        return m_CardSystemController != null && m_CardSystemController.AddCardToDeck(cardData);
    }

    public bool DrawCard()
    {
        return m_CardSystemController != null && m_CardSystemController.DrawCard();
    }
}
