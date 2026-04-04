using AAAGame.Card;
using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityEngine;
using UnityGameFramework.Runtime;
using System.Collections.Generic;
using AAAGame.Scripts.Entity;

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
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        SoldierFactory.ShowSoldier("knight", new Vector3(0, 1, -8), SideType.PlayerSide, BrainType.Player);
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
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);

        base.OnLeave(procedureOwner, isShutdown);
    }

    /// <summary>
    /// 初始化数据模型
    /// </summary>
    private void InitDataModels()
    {
        // 初始化数据模型（参考CharacterTestProcedure）
        GF.DataModel.CreateDataModel<ItemDataModel>();
        GF.DataModel.CreateDataModel<DeviceDataModel>();
        GF.DataModel.CreateDataModel<LocalizationTextDataModel>();
        GF.DataModel.CreateDataModel<CraftingDeviceDataModel>();
        GF.DataModel.CreateDataModel<InputModel>(); // 必须初始化输入模型，否则PlayerBrain会出现空引用
        GF.DataModel.CreateDataModel<TechNodeDataModel>();

        GF.DataModel.GetOrCreate<ItemCollectionDataModel>();
        GF.DataModel.GetOrCreate<CapabilityProgressDataModel>();
        GF.DataModel.GetOrCreate<ProfileDataModel>();
        GF.DataModel.GetOrCreate<TechProgressDataModel>();
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
    
    private void OnShowEntitySuccess(object sender, GameEventArgs e)
    {
        var args = (ShowEntitySuccessEventArgs)e;
        if (args.Entity.Logic is MAEntity ma)
        {
            // 玩家注册为 Player
            if (ma.Brain is PlayerBrain)
            {
                EntityRegistry.RegisterAsPlayer(ma);
                
                // 设置摄像机跟随玩家
                CameraController cameraController = Camera.main.GetComponent<CameraController>();
                if (cameraController != null)
                {
                    cameraController.SetFollowTarget(ma.transform);
                }
            }

            // 给所有生物挂血条
            if (ma is GeneralCreature creature)
            {
                float originalMax = (float)creature.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
                float max = originalMax;
                
                // 根据单位类型调整血量
                if (ma is SoldierEntity soldier && soldier.UnitIndex == "coder")
                {
                    // 码农单位：血量减少10倍
                    float newMax = originalMax / 10f;
                    Fix64 subtractValue = (Fix64)(originalMax - newMax);
                    var modifier = PropertyDirectAdditiveModifier.Create(-subtractValue);
                    creature.CreaturePropertyManager.ModifyMainPropertyValueBuff(CreatureMainProperty.Health, modifier, true);
                    max = newMax;
                }
                else
                {
                    // 敌方单位：保持原血量不变
                }
                
                // 更新当前生命值，确保单位满血
                float currentHealth = creature.health;
                float healAmount = max - currentHealth;
                if (healAmount > 0)
                {
                    creature.CreaturePropertyManager.ModifyCurrentProperty(
                        CreatureCurrentProperty.HealthCurrent,
                        PropertyIrreversibleAdditiveModifier.Create((Fix64)healAmount), true);
                }
                
                HealthBarComp.Create(creature.Id, creature.transform, creature.health, max);
            }

            // SoldierAIBrain 需要重新 Inject（玩家可能在它之后创建）
            if (ma.Brain is SoldierAIBrain soldierBrain)
            {
                soldierBrain.Inject();
            }
        }
    }
    
}


