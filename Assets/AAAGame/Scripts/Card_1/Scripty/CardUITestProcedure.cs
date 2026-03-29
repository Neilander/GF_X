using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

/// <summary>
/// 卡牌UI测试流程
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public class CardUITestProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);

        GF.Log("进入卡牌UI测试流程");

        // 初始化测试环境
        SetupTestEnvironment();
    }

    protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

        // 测试用快捷键
        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.R))
        {
            // 重置测试
            ResetTest();
        }

        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
        {
            // 返回主菜单
            ChangeState<MenuProcedure>(procedureOwner);
        }
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        GF.Log("离开卡牌UI测试流程");
        base.OnLeave(procedureOwner, isShutdown);
    }

    /// <summary>
    /// 设置测试环境
    /// </summary>
    private void SetupTestEnvironment()
    {
        // 确保管理器存在
        EnsureManagers();

        // 创建测试用禁止区域
        CreateForbiddenArea();

        // 创建放置指示器
        CreatePlacementIndicator();

        GF.Log("卡牌UI测试环境设置完成");
        GF.Log("按 Tab 键切换UI显示/隐藏");
        GF.Log("按 R 键重置测试");
        GF.Log("按 ESC 键返回主菜单");
    }

    /// <summary>
    /// 确保管理器存在
    /// </summary>
    private void EnsureManagers()
    {
        // 人口管理器
        if (PopulationManager.Instance == null)
        {
            var popObj = new UnityEngine.GameObject("PopulationManager");
            popObj.AddComponent<PopulationManager>();
        }

        // 手牌管理器
        if (PlayerHandManager.Instance == null)
        {
            var handObj = new UnityEngine.GameObject("PlayerHandManager");
            handObj.AddComponent<PlayerHandManager>();
        }

        // UI管理器
        if (CardUIManager.Instance == null)
        {
            GF.LogWarning("CardUIManager 未找到，请在场景中手动添加");
        }
    }

    /// <summary>
    /// 创建禁止区域
    /// </summary>
    private void CreateForbiddenArea()
    {
        var forbiddenObj = UnityEngine.GameObject.Find("ForbiddenArea");
        if (forbiddenObj == null)
        {
            forbiddenObj = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
            forbiddenObj.name = "ForbiddenArea";
            forbiddenObj.transform.position = new UnityEngine.Vector3(5, 0.5f, 0);
            forbiddenObj.transform.localScale = new UnityEngine.Vector3(3, 1, 3);

            // 设置为红色半透明
            var renderer = forbiddenObj.GetComponent<UnityEngine.Renderer>();
            if (renderer != null)
            {
                var mat = new UnityEngine.Material(UnityEngine.Shader.Find("Standard"));
                mat.color = new UnityEngine.Color(1, 0, 0, 0.3f);
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
                renderer.material = mat;
            }

            GF.Log("创建禁止部署区域");
        }
    }

    /// <summary>
    /// 创建放置指示器
    /// </summary>
    private void CreatePlacementIndicator()
    {
        var indicatorObj = UnityEngine.GameObject.Find("PlacementIndicator");
        if (indicatorObj == null)
        {
            indicatorObj = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cylinder);
            indicatorObj.name = "PlacementIndicator";
            indicatorObj.transform.localScale = new UnityEngine.Vector3(1, 0.1f, 1);

            // 移除碰撞体
            var collider = indicatorObj.GetComponent<UnityEngine.Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            // 添加指示器组件
            var indicator = indicatorObj.AddComponent<CardPlacementIndicator>();

            // 关联到UI管理器
            if (CardUIManager.Instance != null)
            {
                // 通过反射设置（因为是SerializeField）
                var field = typeof(CardUIManager).GetField("placementIndicator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(CardUIManager.Instance, indicator);
            }

            GF.Log("创建放置指示器");
        }
    }

    /// <summary>
    /// 重置测试
    /// </summary>
    private void ResetTest()
    {
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.ResetPopulation();
        }

        if (PlayerHandManager.Instance != null)
        {
            PlayerHandManager.Instance.ReloadInitialHand();
        }

        GF.Log("测试已重置");
    }
}
