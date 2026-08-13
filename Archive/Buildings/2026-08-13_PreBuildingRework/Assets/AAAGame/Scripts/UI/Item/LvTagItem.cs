using UnityEngine;
using UnityGameFramework.Runtime;
using System;

public partial class LvTagItem : UIItemBase
{
    private LevelTagTable m_Data;
    private Action<LvTagItem> m_OnClick;
    private bool m_IsSelected;
    private bool m_IsGrayed;

    public int TagId => m_Data.Id;
    public int GroupID => m_Data.GroupID;
    public bool IsPositive => m_Data.IsPositiveTag;
    public int TagLevel => m_Data.Score;

    public void Setup(LevelTagTable data, Action<LvTagItem> onClick)
    {
        m_Data = data;
        m_OnClick = onClick;
        varName.text = LocalizationTextManager.GetLocalizedText(data.NameKey, false);
        varTagLv.gameObject.SetActive(!data.IsPositiveTag);
        if (!data.IsPositiveTag) varTagLv.text = data.Score.ToString();
        varLvTagItem.onClick.RemoveAllListeners();
        varLvTagItem.onClick.AddListener(() => m_OnClick?.Invoke(this));
        SetSelected(false);
        SetGrayed(false);
    }

    public void SetSelected(bool selected)
    {
        m_IsSelected = selected;
        RefreshColor();
    }

    public void SetGrayed(bool grayed)
    {
        m_IsGrayed = grayed;
        RefreshColor();
    }

    private static readonly Color s_Orange = new Color(1f, 0.65f, 0f);
    private static readonly Color s_Gray   = new Color(0.4f, 0.4f, 0.4f);

    private void RefreshColor()
    {
        Color c = m_IsGrayed ? s_Gray : m_IsSelected ? s_Orange : Color.white;
        var cb = varLvTagItem.colors;
        cb.normalColor      = c;
        cb.highlightedColor = c;
        cb.selectedColor    = c;
        cb.pressedColor     = c * 0.85f;
        cb.disabledColor    = c;
        varLvTagItem.colors = cb;
    }
}
