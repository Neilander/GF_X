using AAAGame.Card;
using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
using System.Collections.Generic;

/// <summary>
/// 卡牌游戏流程
/// 在 Game 场景加载完成后进入此流程，显示卡牌 UI
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class CardGameProcedure : ProcedureBase
{
    private CardSystemController m_CardSystemController;
    private int m_CardUIFormId = -1;

    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        
        Log.Info("[CardGame] 进入卡牌游戏流程");

        // 初始化数据模型
        InitDataModels();

        // 初始化卡牌系统
        InitializeCardSystem();

        // 打开卡牌 UI
        OpenCardUI();
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

        // 更新卡牌放置逻辑
        if (m_CardSystemController != null)
        {
            m_CardSystemController.UpdatePlacement();
        }
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
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

        base.OnLeave(procedureOwner, isShutdown);
    }

    /// <summary>
    /// 初始化数据模型
    /// </summary>
    private void InitDataModels()
    {
        // 根据需要初始化数据模型
        // GF.DataModel.CreateDataModel<YourDataModel>();
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
    private void OpenCardUI()
    {
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
}
