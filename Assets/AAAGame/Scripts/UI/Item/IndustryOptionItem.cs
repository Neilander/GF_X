using System;
using UnityEngine;
using UnityEngine.UI;

public partial class IndustryOptionItem : UIItemBase
{
    protected override void OnInit()
    {
        base.OnInit();
        if (varBg != null)
            varBg.onClick.RemoveAllListeners();
    }

    public void SetData(string keyText, string displayName, bool selected, Action onClick)
    {
        if (varKey != null)
        {
            varKey.text = keyText ?? string.Empty;
            varKey.gameObject.SetActive(!string.IsNullOrWhiteSpace(keyText));
        }

        if (varName != null)
            varName.text = displayName ?? string.Empty;

        SetSelected(selected);

        if (varBg != null)
        {
            varBg.onClick.RemoveAllListeners();
            if (onClick != null)
                varBg.onClick.AddListener(() => onClick());
        }
    }

    public void SetSelected(bool selected)
    {
        if (varChooseEffect != null)
            varChooseEffect.SetActive(selected);
    }
}
