using UnityEngine;
using UnityEditor;

/// <summary>
/// CardUIManager 自定义编辑器
/// 在 Inspector 中添加测试按钮
/// </summary>
[CustomEditor(typeof(CardUIManager))]
public class CardUIManagerEditor : Editor
{
    private int addCardCount = 1; // 添加卡牌数量

    public override void OnInspectorGUI()
    {
        // 绘制默认 Inspector
        DrawDefaultInspector();

        CardUIManager manager = (CardUIManager)target;

        // 添加分隔线
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("测试功能", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("运行时可用的测试按钮", MessageType.Info);

        // 只在运行时启用按钮
        GUI.enabled = Application.isPlaying;

        // 测试按钮：重新抽取手牌
        if (GUILayout.Button("🎴 重新抽取手牌（播放动画）", GUILayout.Height(30)))
        {
            manager.TestDrawCards();
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
            manager.TestAddCards(addCardCount);
        }

        EditorGUILayout.Space(5);

        // 快捷按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加1张", GUILayout.Height(25)))
        {
            manager.TestAddCards(1);
        }
        if (GUILayout.Button("添加2张", GUILayout.Height(25)))
        {
            manager.TestAddCards(2);
        }
        if (GUILayout.Button("添加3张", GUILayout.Height(25)))
        {
            manager.TestAddCards(3);
        }
        EditorGUILayout.EndHorizontal();

        GUI.enabled = true;

        // 提示信息
        if (!Application.isPlaying)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("请先运行游戏才能使用测试按钮", MessageType.Warning);
        }
    }
}
