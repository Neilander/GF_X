using System;
using DG.Tweening;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public partial class GoalConditionItem : UIItemBase
{
    private GameObject m_TutorialCheckbox;
    private Image[] m_CheckboxBorders;
    private TextMeshProUGUI m_Checkmark;

    public void SetText(string text)
    {
        if (varIcon != null)
            varIcon.enabled = true;
        if (m_TutorialCheckbox != null)
            m_TutorialCheckbox.SetActive(false);
        if (varGoalConditionItem != null)
        {
            varGoalConditionItem.text = text;
            varGoalConditionItem.color = Color.white;
            varGoalConditionItem.fontStyle = FontStyles.Normal;
        }
        if (m_Checkmark != null)
            m_Checkmark.text = string.Empty;
        if (varIcon != null)
            varIcon.color = Color.white;
    }

    public void SetTutorialObjective(string text, TutorialObjectiveStatus status)
    {
        SetObjectiveState(
            text,
            status == TutorialObjectiveStatus.Completed,
            status == TutorialObjectiveStatus.Failed);
    }

    public void SetLevelObjective(string text, LevelObjectiveStatus status)
    {
        SetObjectiveState(
            text,
            status == LevelObjectiveStatus.Completed,
            status == LevelObjectiveStatus.Failed);
    }

    public void SetSectionTitle(string text)
    {
        if (varGoalConditionItem == null || varIcon == null)
            throw new InvalidOperationException("GoalConditionItem requires both text and icon references.");

        varIcon.enabled = false;
        if (m_TutorialCheckbox != null)
            m_TutorialCheckbox.SetActive(false);
        varGoalConditionItem.text = text;
        varGoalConditionItem.color = Color.white;
        varGoalConditionItem.fontStyle = FontStyles.Bold;
    }

    private void SetObjectiveState(string text, bool completed, bool failed)
    {
        if (varGoalConditionItem == null || varIcon == null)
            throw new InvalidOperationException("GoalConditionItem requires both text and icon references.");

        EnsureTutorialCheckbox();
        Color rowColor = completed || failed ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;

        varIcon.enabled = false;
        m_TutorialCheckbox.SetActive(true);
        for (int i = 0; i < m_CheckboxBorders.Length; i++)
            m_CheckboxBorders[i].color = rowColor;
        m_Checkmark.text = completed ? "\u2713" : string.Empty;
        m_Checkmark.color = rowColor;
        varGoalConditionItem.color = rowColor;
        varGoalConditionItem.fontStyle = FontStyles.Normal;
        varGoalConditionItem.text = failed ? $"<s>{text}</s>" : text;
    }

    private void EnsureTutorialCheckbox()
    {
        if (m_TutorialCheckbox != null)
            return;

        m_TutorialCheckbox = new GameObject("TutorialCheckbox", typeof(RectTransform));
        RectTransform checkboxRect = m_TutorialCheckbox.GetComponent<RectTransform>();
        checkboxRect.SetParent(varIcon.rectTransform, false);
        checkboxRect.anchorMin = Vector2.zero;
        checkboxRect.anchorMax = Vector2.one;
        checkboxRect.offsetMin = Vector2.zero;
        checkboxRect.offsetMax = Vector2.zero;

        m_CheckboxBorders = new Image[4];
        m_CheckboxBorders[0] = CreateBorder("Top", checkboxRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -0.6f), new Vector2(0f, 1.2f));
        m_CheckboxBorders[1] = CreateBorder("Bottom", checkboxRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0.6f), new Vector2(0f, 1.2f));
        m_CheckboxBorders[2] = CreateBorder("Left", checkboxRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.6f, 0f), new Vector2(1.2f, 0f));
        m_CheckboxBorders[3] = CreateBorder("Right", checkboxRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-0.6f, 0f), new Vector2(1.2f, 0f));

        var checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rect = checkmarkObject.GetComponent<RectTransform>();
        rect.SetParent(checkboxRect, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        m_Checkmark = checkmarkObject.GetComponent<TextMeshProUGUI>();
        m_Checkmark.alignment = TextAlignmentOptions.Center;
        m_Checkmark.fontSize = Mathf.Max(12f, varIcon.rectTransform.rect.height * 0.9f);
        m_Checkmark.raycastTarget = false;
    }

    private static Image CreateBorder(
        string name,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        var borderObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = borderObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Image border = borderObject.GetComponent<Image>();
        border.raycastTarget = false;
        return border;
    }

    public void SetState(bool satisfied, Color iconActiveColor, Color iconInactiveColor, Color textSatisfiedColor, Color textUnsatisfiedColor)
    {
        if (varIcon != null)
            varIcon.color = satisfied ? iconActiveColor : iconInactiveColor;

        if (varGoalConditionItem != null)
            varGoalConditionItem.color = satisfied ? textSatisfiedColor : textUnsatisfiedColor;
    }
}
