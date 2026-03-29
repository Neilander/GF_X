using AAAGame.Card;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 卡牌系统测试脚本
/// 用于测试卡牌系统的所有功能
/// </summary>
public class CardSystemTest : MonoBehaviour
{
    [Header("测试配置")]
    [SerializeField] private int maxPopulation = 10;
    [SerializeField] private int initialCardCount = 4;
    
    [Header("区域对象")]
    [SerializeField] private GameObject validArea;
    [SerializeField] private GameObject invalidArea;
    
    [Header("卡牌数据")]
    [SerializeField] private List<CardData> testCardDataList;
    
    private CardSystemController m_CardSystem;
    private bool m_IsInitialized = false;

    void Start()
    {
        // 使用协程等待 GFBuiltin 初始化
        StartCoroutine(WaitAndInitialize());
    }

    private System.Collections.IEnumerator WaitAndInitialize()
    {
        // 等待 GFBuiltin 初始化完成
        while (GFBuiltin.Event == null || GFBuiltin.UI == null)
        {
            if (GFBuiltin.Event == null)
            {
                print(1111);
            }
             if(GFBuiltin.UI == null)
            {
                print(2222);
            }
            
            GF.Log("为空");
            yield return null;
        }
        
        // 额外等待 0.5 秒，确保所有系统都准备好
        yield return new WaitForSeconds(0.5f);
        
        InitializeCardSystem();
    }

    private void InitializeCardSystem()
    {
        GFBuiltin.Log("=== 开始初始化卡牌系统 ===");
        
        // 检查 GFBuiltin 是否初始化
        if (GFBuiltin.Event == null)
        {
            GFBuiltin.LogError("❌ GFBuiltin.Event 未初始化！");
            return;
        }
        
        if (GFBuiltin.UI == null)
        {
            GFBuiltin.LogError("❌ GFBuiltin.UI 未初始化！");
            return;
        }
        
        // 1. 创建卡牌系统控制器
        try
        {
            m_CardSystem = new CardSystemController();
            m_CardSystem.Initialize();
            GFBuiltin.Log("✓ 卡牌系统控制器已创建");
        }
        catch (System.Exception e)
        {
            GFBuiltin.LogError($"❌ 创建卡牌系统控制器失败: {e.Message}");
            return;
        }
        
        // 2. 设置最大人口
        try
        {
            m_CardSystem.SetMaxPopulation(maxPopulation);
            GFBuiltin.Log($"✓ 最大人口设置为: {maxPopulation}");
        }
        catch (System.Exception e)
        {
            GFBuiltin.LogError($"❌ 设置最大人口失败: {e.Message}");
            return;
        }
        
        // 3. 加载卡牌数据
        List<ICardDataProvider> providers = LoadCardData();
        if (providers.Count == 0)
        {
            GFBuiltin.LogWarning("⚠️ 没有可用的卡牌数据，将无法抽卡");
        }
        else
        {
            m_CardSystem.SetCardPool(providers);
            GFBuiltin.Log($"✓ 卡牌池已设置，共 {providers.Count} 张卡牌");
        }
        
        // 4. 设置区域对象
        SetupAreas();
        
        // 5. 打开 UI（使用 UIViews 枚举）
        try
        {
            GFBuiltin.UI.OpenUIForm(UIViews.CardUIForm);
            GFBuiltin.Log("✓ UI 已打开");
        }
        catch (System.Exception e)
        {
            GFBuiltin.LogError($"❌ 打开 UI 失败: {e.Message}");
            GFBuiltin.LogError("请确保已完成以下步骤：");
            GFBuiltin.LogError("1. 重新生成 DataTable (AAAGame → Generate DataTables)");
            GFBuiltin.LogError("2. 创建 CardUIForm 预制体");
            GFBuiltin.LogError("3. 配置 AssetBundle 标签");
            return;
        }
        
        // 6. 抽初始手牌
        if (providers.Count > 0)
        {
            m_CardSystem.DrawCards(initialCardCount);
            GFBuiltin.Log($"✓ 已抽取 {initialCardCount} 张初始手牌");
        }
        
        m_IsInitialized = true;
        GFBuiltin.Log("=== 卡牌系统初始化完成 ===");
    }

    private List<ICardDataProvider> LoadCardData()
    {
        List<ICardDataProvider> providers = new List<ICardDataProvider>();
        
        // 方式1：使用 Inspector 配置的测试数据
        if (testCardDataList != null && testCardDataList.Count > 0)
        {
            providers = CardDataProviderFactory.CreateFromScriptableObjects(testCardDataList);
            GFBuiltin.Log($"从 Inspector 加载了 {providers.Count} 张卡牌");
            return providers;
        }
        
        // 方式2：从 Resources 加载
        CardData[] cardDataArray = Resources.LoadAll<CardData>("CardData");
        if (cardDataArray.Length > 0)
        {
            providers = CardDataProviderFactory.CreateFromScriptableObjects(
                new List<CardData>(cardDataArray));
            GFBuiltin.Log($"从 Resources 加载了 {providers.Count} 张卡牌");
            return providers;
        }
        
        GFBuiltin.LogWarning("未找到卡牌数据，将创建测试数据");
        return providers;
    }

    private void SetupAreas()
    {
        // 自动查找区域对象（如果没有手动配置）
        if (validArea == null)
        {
            validArea = GameObject.Find("ValidArea");
        }
        
        if (invalidArea == null)
        {
            invalidArea = GameObject.Find("InvalidArea");
        }
        
        if (validArea == null || invalidArea == null)
        {
            GFBuiltin.LogWarning("⚠️ 区域对象未配置，请在 Inspector 中设置或确保场景中有 ValidArea 和 InvalidArea 对象");
            return;
        }
        
        m_CardSystem.SetAreaObjects(validArea, invalidArea);
        GFBuiltin.Log($"✓ 区域对象已设置: Valid={validArea.name}, Invalid={invalidArea.name}");
    }

    void Update()
    {
        if (!m_IsInitialized || m_CardSystem == null) return;
        
        // 更新放置逻辑
        m_CardSystem.UpdatePlacement();
        
        // 测试快捷键
        HandleTestHotkeys();
    }

    private void HandleTestHotkeys()
    {
        // F1: 抽一张卡
        if (Input.GetKeyDown(KeyCode.F1))
        {
            m_CardSystem.DrawCard();
            GFBuiltin.Log("抽了一张卡");
        }
        
        // F2: 增加人口上限
        if (Input.GetKeyDown(KeyCode.F2))
        {
            m_CardSystem.SetMaxPopulation(maxPopulation + 5);
            maxPopulation += 5;
            GFBuiltin.Log($"人口上限增加到: {maxPopulation}");
        }
        
        // F3: 打印当前状态
        if (Input.GetKeyDown(KeyCode.F3))
        {
            PrintSystemStatus();
        }
    }

    private void PrintSystemStatus()
    {
        GFBuiltin.Log("=== 卡牌系统状态 ===");
        
        var populationModel = m_CardSystem.GetPopulationModel();
        GFBuiltin.Log($"人口: {populationModel.CurrentPopulation}/{populationModel.MaxPopulation}");
        
        var handModel = m_CardSystem.GetHandModel();
        GFBuiltin.Log($"手牌: {handModel.CardCount}/{handModel.MaxCards}");
        
        GFBuiltin.Log("==================");
    }

    void OnDestroy()
    {
        if (m_CardSystem != null)
        {
            m_CardSystem.Shutdown();
            GFBuiltin.Log("卡牌系统已清理");
        }
    }

    // 在 Inspector 中显示帮助信息
    void OnGUI()
    {
        if (!m_IsInitialized) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("=== 测试快捷键 ===");
        GUILayout.Label("1-4: 打出对应位置的卡牌");
        GUILayout.Label("F1: 抽一张卡");
        GUILayout.Label("F2: 增加人口上限");
        GUILayout.Label("F3: 打印系统状态");
        GUILayout.Label("拖拽卡牌到场景放置士兵");
        GUILayout.Label("拖拽卡牌到垃圾桶丢弃");
        GUILayout.EndArea();
    }
}