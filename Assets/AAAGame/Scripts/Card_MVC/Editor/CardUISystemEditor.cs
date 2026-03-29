using UnityEngine;
using UnityEditor;
using AAAGame.Card.UI;

namespace AAAGame.Card.Editor
{
    /// <summary>
    /// CardUISystem 自定义编辑器 - 完全独立版本
    /// 在 Inspector 中添加测试按钮
    /// </summary>
    [CustomEditor(typeof(CardUISystem))]
    public class CardUISystemEditor : UnityEditor.Editor
    {
        private int addCardCount = 1;

        public override void OnInspectorGUI()
        {
            // 绘制默认 Inspector
            DrawDefaultInspector();

            CardUISystem uiSystem = (CardUISystem)target;

            // 添加分隔线
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("测试功能", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("运行时可用的测试按钮", MessageType.Info);

            // 只在运行时启用按钮
            GUI.enabled = Application.isPlaying;

            // 测试按钮：重新抽取手牌
            if (GUILayout.Button("🎴 重新抽取手牌（播放动画）", GUILayout.Height(30)))
            {
                if (PlayerHandManager.Instance != null)
                {
                    PlayerHandManager.Instance.RedrawHand();
                    Debug.Log("[Card] 测试：重新抽取手牌");
                }
                else
                {
                    Debug.LogWarning("[Card] PlayerHandManager 不存在");
                }
            }

            EditorGUILayout.Space(5);

            // 添加卡牌数量输入
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("添加卡牌数量：", GUILayout.Width(100));
            addCardCount = EditorGUILayout.IntSlider(addCardCount, 1, 4);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 测试按钮：添加指定数量卡牌
            if (GUILayout.Button($"➕ 添加 {addCardCount} 张卡牌（播放动画）", GUILayout.Height(30)))
            {
                if (PlayerHandManager.Instance != null)
                {
                    PlayerHandManager.Instance.DrawRandomCards(addCardCount);
                    Debug.Log($"[Card] 测试：添加 {addCardCount} 张卡牌");
                }
                else
                {
                    Debug.LogWarning("[Card] PlayerHandManager 不存在");
                }
            }

            EditorGUILayout.Space(5);

            // 快捷按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加1张", GUILayout.Height(25)))
            {
                if (PlayerHandManager.Instance != null)
                {
                    PlayerHandManager.Instance.DrawRandomCards(1);
                }
            }
            if (GUILayout.Button("添加2张", GUILayout.Height(25)))
            {
                if (PlayerHandManager.Instance != null)
                {
                    PlayerHandManager.Instance.DrawRandomCards(2);
                }
            }
            if (GUILayout.Button("添加3张", GUILayout.Height(25)))
            {
                if (PlayerHandManager.Instance != null)
                {
                    PlayerHandManager.Instance.DrawRandomCards(3);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 人口测试按钮
            EditorGUILayout.LabelField("人口测试", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重置人口", GUILayout.Height(25)))
            {
                if (PopulationManager.Instance != null)
                {
                    PopulationManager.Instance.ResetPopulation();
                    Debug.Log("[Card] 测试：重置人口");
                }
            }
            if (GUILayout.Button("占用5人口", GUILayout.Height(25)))
            {
                if (PopulationManager.Instance != null)
                {
                    PopulationManager.Instance.OccupyPopulation(5);
                    Debug.Log("[Card] 测试：占用5人口");
                }
            }
            if (GUILayout.Button("释放5人口", GUILayout.Height(25)))
            {
                if (PopulationManager.Instance != null)
                {
                    PopulationManager.Instance.ReleasePopulation(5);
                    Debug.Log("[Card] 测试：释放5人口");
                }
            }
            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;

            // 提示信息
            if (!Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("请先运行游戏才能使用测试按钮", MessageType.Warning);
            }
            else
            {
                // 显示当前状态
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
                
                if (PlayerHandManager.Instance != null)
                {
                    EditorGUILayout.LabelField($"手牌数量：{PlayerHandManager.Instance.CurrentHandSize}/{PlayerHandManager.Instance.MaxHandSize}");
                }
                
                if (PopulationManager.Instance != null)
                {
                    EditorGUILayout.LabelField($"人口：{PopulationManager.Instance.CurrentPopulation}/{PopulationManager.Instance.MaxPopulation}");
                }
            }
        }
    }
}
