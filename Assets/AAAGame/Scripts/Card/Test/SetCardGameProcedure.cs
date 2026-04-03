using UnityEngine;
using UnityEditor;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌系统启动流程设置工具
    /// 用于快速设置 CardGameProcedure 为启动流程
    /// </summary>
    public class SetCardGameProcedure
    {
        [MenuItem("AAAGame/Card System/Set Card Game Procedure")]
        public static void SetProcedure()
        {
            // 设置启动流程为 CardGameProcedure
            ChangeSceneProcedure.SelectedProcedureForGame = "CardGameProcedure";
            ChangeSceneProcedure.SelectedSceneForGame = "Game";
            
            Debug.Log("✓ 已设置启动流程为 CardGameProcedure");
            Debug.Log("✓ 已设置场景为 Game");
            Debug.Log("✓ 现在可以运行 Launch 场景测试卡牌系统");
            Debug.Log("");
            Debug.Log("操作步骤：");
            Debug.Log("1. 打开 Launch 场景");
            Debug.Log("2. 点击 Play 按钮");
            Debug.Log("3. 等待自动进入 Game 场景");
            Debug.Log("4. CardUIForm 应该会自动打开");
        }
        
        [MenuItem("AAAGame/Card System/Reset To Default Procedure")]
        public static void ResetProcedure()
        {
            // 重置为默认流程
            ChangeSceneProcedure.SelectedProcedureForGame = "CharacterTestProcedure";
            ChangeSceneProcedure.SelectedSceneForGame = "Game";
            
            Debug.Log("✓ 已重置为默认流程 CharacterTestProcedure");
        }
        
        [MenuItem("AAAGame/Card System/Check Current Procedure")]
        public static void CheckProcedure()
        {
            Debug.Log("=== 当前启动配置 ===");
            Debug.Log($"流程: {ChangeSceneProcedure.SelectedProcedureForGame}");
            Debug.Log($"场景: {ChangeSceneProcedure.SelectedSceneForGame}");
            Debug.Log("==================");
        }
    }
}
