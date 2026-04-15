using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Card;

public partial class CardSetup : GameFrameworkComponent
{
    private CardSystemController m_CardSystemController;
    private int m_CardUIFormId = -1;

    public void CardSystemSetup()
    {
        InitializeCardSystem();
    }

    public void CardSystemUpdate()
    {
        // 更新卡牌放置逻辑
        if (m_CardSystemController != null)
        {
            m_CardSystemController.UpdatePlacement();
        }
    }

    public void CardSystemShutdown()
    {
        // 关闭卡牌 UI
        if (m_CardUIFormId != -1)
        {
            GF.UI.CloseUIForm(m_CardUIFormId);
            m_CardUIFormId = -1;
        }

        // 清理卡牌系统
        if (m_CardSystemController != null)
        {
            m_CardSystemController.Shutdown();
            m_CardSystemController = null;
        }
    }

    /// <summary>
    /// 初始化卡牌系统
    /// </summary>
    private void InitializeCardSystem()
    {
        m_CardSystemController = new CardSystemController();
        m_CardSystemController.Initialize();

        // 设置最大人口
        m_CardSystemController.SetMaxPopulation(10);

        // 设置卡牌池（从 DataTable 或 ScriptableObject 加载）
        List<ICardDataProvider> cardPool = LoadCardPool();
        m_CardSystemController.SetCardPool(cardPool);

        // 设置区域对象（可放置区域和禁止区域）
        SetupAreaObjects();
        m_CardSystemController.DrawCard();
        m_CardSystemController.DrawCard();
        m_CardSystemController.DrawCard();
        m_CardSystemController.DrawCard();
        m_CardSystemController.DrawCard();

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
        // 使用 GF.UI 打开 CardUIForm
        UIParams uiParams = UIParams.Create();
        uiParams.Set("CardSystemController", m_CardSystemController);
        
        m_CardUIFormId = GF.UI.OpenUIForm(UIViews.CardUIForm, uiParams);
        GF.UI.OpenUIForm(UIViews.MinimapUI);
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
    /// 生成卡牌
    /// </summary>
    public void GenerateCard()
    {
        if (m_CardSystemController != null)
        {
            m_CardSystemController.DrawCard();
            Log.Info("[CardGame] 生成卡牌");
        }
    }
} 
