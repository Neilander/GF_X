using UnityEngine;

public partial class IconNumItem : UIItemBase
{
    private Color _defaultNumColor;
    private bool _defaultNumColorCached;

    protected override void OnInit()
    {
        base.OnInit();
        CacheDefaultNumColorIfNeeded();
    }

    public void SetData(string iconSpritePath, int number)
    {
        SetData(iconSpritePath, number.ToString());
    }

    public void SetData(string iconSpritePath, string numberText)
    {
        CacheDefaultNumColorIfNeeded();
        varIcon.gameObject.SetActive(true);
        ConfigureNumberLayout(hasIcon: true);
        // SetSprite 是异步加载，native size 需要在 sprite 赋值后执行。
        varIcon.SetSprite(iconSpritePath, resize: true);
        // 数值文本默认走富文本规则（例如 +10 / -3 的颜色规则）。
        varNum.text = LocalizationTextManager.ProcessText(numberText ?? string.Empty);
        ResetNumberColor();
    }

    public void SetGlyphData(string glyph, string numberText)
    {
        CacheDefaultNumColorIfNeeded();
        varIcon.gameObject.SetActive(false);
        ConfigureNumberLayout(hasIcon: false);
        varNum.text = LocalizationTextManager.ProcessText(glyph + " " + (numberText ?? string.Empty));
        ResetNumberColor();
    }

    public void SetNumberColor(Color color)
    {
        if (varNum != null)
            varNum.color = color;
    }

    public void ResetNumberColor()
    {
        if (varNum != null && _defaultNumColorCached)
            varNum.color = _defaultNumColor;
    }

    private void CacheDefaultNumColorIfNeeded()
    {
        if (_defaultNumColorCached || varNum == null)
            return;

        _defaultNumColor = varNum.color;
        _defaultNumColorCached = true;
    }

    private void ConfigureNumberLayout(bool hasIcon)
    {
        RectTransform rect = varNum.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(hasIcon ? 22f : 1f, 0f);
        rect.offsetMax = new Vector2(-1f, 0f);
        varNum.enableAutoSizing = true;
        varNum.fontSizeMin = 8f;
        varNum.fontSizeMax = 16f;
        varNum.enableWordWrapping = false;
    }
}
