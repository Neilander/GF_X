using System;
using DG.Tweening;
using UnityEngine;
using TMPro;

public partial class GoalConditionItem : UIItemBase
{
    private TextMeshProUGUI m_Checkmark;

    public void SetText(string text)
    {
        if (varGoalConditionItem != null)
        {
            varGoalConditionItem.text = text;
            varGoalConditionItem.color = Color.white;
        }
        if (m_Checkmark != null)
            m_Checkmark.text = string.Empty;
        if (varIcon != null)
            varIcon.color = Color.white;
    }

    public void SetTutorialObjective(string text, TutorialObjectiveStatus status)
    {
        if (varGoalConditionItem == null || varIcon == null)
            throw new InvalidOperationException("GoalConditionItem requires both text and icon references.");

        EnsureCheckmark();
        bool completed = status == TutorialObjectiveStatus.Completed;
        bool failed = status == TutorialObjectiveStatus.Failed;
        Color rowColor = status == TutorialObjectiveStatus.Active
            ? Color.white
            : new Color(0.55f, 0.55f, 0.55f, 1f);

        varIcon.color = rowColor;
        m_Checkmark.text = completed ? "\u2713" : string.Empty;
        m_Checkmark.color = rowColor;
        varGoalConditionItem.color = rowColor;
        varGoalConditionItem.text = failed ? $"<s>{text}</s>" : text;
    }

    private void EnsureCheckmark()
    {
        if (m_Checkmark != null)
            return;

        var checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rect = checkmarkObject.GetComponent<RectTransform>();
        rect.SetParent(varIcon.rectTransform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        m_Checkmark = checkmarkObject.GetComponent<TextMeshProUGUI>();
        m_Checkmark.alignment = TextAlignmentOptions.Center;
        m_Checkmark.fontSize = Mathf.Max(12f, varIcon.rectTransform.rect.height * 0.9f);
        m_Checkmark.raycastTarget = false;
    }

    public void SetState(bool satisfied, Color iconActiveColor, Color iconInactiveColor, Color textSatisfiedColor, Color textUnsatisfiedColor)
    {
        if (varIcon != null)
            varIcon.color = satisfied ? iconActiveColor : iconInactiveColor;

        if (varGoalConditionItem != null)
            varGoalConditionItem.color = satisfied ? textSatisfiedColor : textUnsatisfiedColor;
    }
}
