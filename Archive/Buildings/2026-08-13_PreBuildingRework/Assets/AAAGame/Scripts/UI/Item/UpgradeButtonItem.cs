using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class UpgradeButtonItem : UIItemBase, IPointerEnterHandler
{
    private int m_OptionIndex;
    private CanvasGroup m_CanvasGroup;
    private Action m_OnHover;

    public RectTransform HoldRoot => (varUpgradeButtonItem != null ? varUpgradeButtonItem.transform : transform) as RectTransform;

    protected override void OnInit()
    {
        base.OnInit();
        if (varUpgradeButtonItem != null)
            varUpgradeButtonItem.onClick.RemoveAllListeners();
    }

    public void SetData(int optionIndex, string keyText, bool selected, Action onClick, Action onHover = null)
    {
        SetOptionIndex(optionIndex);
        m_OnHover = onHover;

        if (varKey != null)
        {
            varKey.text = keyText ?? string.Empty;
            varKey.gameObject.SetActive(!string.IsNullOrWhiteSpace(keyText));
        }

        SetSelected(selected);

        if (varUpgradeButtonItem != null)
        {
            varUpgradeButtonItem.onClick.RemoveAllListeners();
            if (onClick != null)
                varUpgradeButtonItem.onClick.AddListener(() => onClick());
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        m_OnHover?.Invoke();
    }

    public void SetSelected(bool selected)
    {
        if (varChooseEffect != null)
            varChooseEffect.SetActive(selected);

        ApplyGlyphState(selected);
    }

    public void SetExecutable(bool executable)
    {
        if (m_CanvasGroup == null)
            m_CanvasGroup = (varUpgradeButtonItem != null ? varUpgradeButtonItem.gameObject : gameObject).GetComponent<CanvasGroup>();
        if (m_CanvasGroup == null)
            m_CanvasGroup = (varUpgradeButtonItem != null ? varUpgradeButtonItem.gameObject : gameObject).AddComponent<CanvasGroup>();

        m_CanvasGroup.alpha = executable ? 1f : 0.65f;
    }

    private void SetOptionIndex(int optionIndex)
    {
        m_OptionIndex = Mathf.Clamp(optionIndex, 0, 3);

        GameObject[] normals = { varA_Normal, varB_Normal, varΓ_Normal, varΔ_Normal };
        GameObject[] highlights = { varA_Highlight, varB_Highlight, varΓ_Highlight, varΔ_Highlight };

        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i] != null)
                normals[i].SetActive(i == m_OptionIndex);

            if (highlights[i] != null)
                highlights[i].SetActive(false);
        }
    }

    private void ApplyGlyphState(bool selected)
    {
        GameObject[] normals = { varA_Normal, varB_Normal, varΓ_Normal, varΔ_Normal };
        GameObject[] highlights = { varA_Highlight, varB_Highlight, varΓ_Highlight, varΔ_Highlight };

        for (int i = 0; i < normals.Length; i++)
        {
            bool activeIndex = i == m_OptionIndex;
            if (normals[i] != null)
                normals[i].SetActive(activeIndex && !selected);

            if (highlights[i] != null)
                highlights[i].SetActive(activeIndex && selected);
        }
    }
}
