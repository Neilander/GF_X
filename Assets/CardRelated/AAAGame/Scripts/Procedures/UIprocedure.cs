// using GameFramework.Fsm;
// using GameFramework.Procedure;
// using UnityGameFramework.Runtime;
// using AAAGame.Card;
// using UnityEngine;
//
// /// <summary>
// /// 卡牌系统测试流程（新版）
// /// </summary>
// [Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
// public class UIprocedure : ProcedureBase
// {
//     //private CardSystemController m_CardSystem;
//
//     protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
//     {
//         base.OnEnter(procedureOwner);
//         
//         GFBuiltin.Log("=== 进入卡牌系统测试流程 ===");
//         
//         // 延迟初始化，确保所有系统都准备好
//        // GFBuiltin.StartCoroutine(InitializeAfterDelay());
//     }
//
//     private System.Collections.IEnumerator InitializeAfterDelay()
//     {
//         // 等待 1 秒，确保所有系统初始化完成
//         yield return new WaitForSeconds(1f);
//         
//         InitializeCardSystem();
//     }
//
//     private void InitializeCardSystem()
//     {
//         GFBuiltin.Log("开始初始化卡牌系统...");
//         
//         // 1. 创建卡牌系统控制器
//         m_CardSystem = new CardSystemController();
//         m_CardSystem.Initialize();
//         GFBuiltin.Log("✓ 卡牌系统控制器已创建");
//         
//         // 2. 设置最大人口
//         m_CardSystem.SetMaxPopulation(10);
//         GFBuiltin.Log("✓ 最大人口设置为: 10");
//         
//         // 3. 创建测试区域
//         CreateTestAreas();
//         
//         // 4. 打开 UI
//         GFBuiltin.UI.OpenUIForm(UIViews.CardUIForm);
//         GFBuiltin.Log("✓ UI 已打开");
//         
//         // 5. 抽初始手牌（如果有卡牌数据）
//         // m_CardSystem.DrawCards(4);
//         
//         GFBuiltin.Log("=== 卡牌系统初始化完成 ===");
//         GFBuiltin.Log("提示：需要先创建 CardUIForm 预制体和配置 DataTable");
//     }
//
//     private void CreateTestAreas()
//     {
//         // 创建可放置区域
//         var validArea = GameObject.CreatePrimitive(PrimitiveType.Plane);
//         validArea.name = "ValidArea";
//         validArea.transform.position = Vector3.zero;
//         validArea.transform.localScale = new Vector3(2, 1, 2);
//         validArea.layer = LayerMask.NameToLayer("Ground");
//         
//         // 创建禁止区域
//         var invalidArea = GameObject.CreatePrimitive(PrimitiveType.Cube);
//         invalidArea.name = "InvalidArea";
//         invalidArea.transform.position = new Vector3(5, 0.5f, 0);
//         invalidArea.transform.localScale = new Vector3(2, 1, 2);
//         invalidArea.layer = LayerMask.NameToLayer("ForbiddenArea");
//         
//         // 设置区域
//         m_CardSystem.SetAreaObjects(validArea, invalidArea);
//         GFBuiltin.Log("✓ 测试区域已创建");
//     }
//
//     protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
//     {
//         base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
//         
//         if (m_CardSystem != null)
//         {
//             m_CardSystem.UpdatePlacement();
//         }
//         
//         // 测试快捷键
//         if (Input.GetKeyDown(KeyCode.Escape))
//         {
//             GFBuiltin.Log("退出测试流程");
//             Application.Quit();
// #if UNITY_EDITOR
//             UnityEditor.EditorApplication.isPlaying = false;
// #endif
//         }
//     }
//
//     protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
//     {
//         if (m_CardSystem != null)
//         {
//             m_CardSystem.Shutdown();
//         }
//         
//         GFBuiltin.Log("离开卡牌系统测试流程");
//         base.OnLeave(procedureOwner, isShutdown);
//     }
// }