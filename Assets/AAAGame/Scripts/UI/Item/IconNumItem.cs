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
        // 固定图标尺寸，避免原图尺寸突破属性网格单元。
        varIcon.SetSprite(iconSpritePath, resize: false);
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

    internal void FitParentRect()
    {
        RectTransform rect = transform as RectTransform
                             ?? throw new System.InvalidOperationException("Icon number item requires RectTransform.");
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    internal void AlignContentRight()
    {
        RectTransform root = transform as RectTransform
                             ?? throw new System.InvalidOperationException("Icon number item requires RectTransform.");
        float textWidth = Mathf.Clamp(
            Mathf.Ceil(varNum.GetPreferredValues(varNum.text, 0f, root.rect.height).x),
            12f,
            Mathf.Max(12f, root.rect.width - 22f));

        RectTransform numberRect = varNum.rectTransform;
        numberRect.anchorMin = new Vector2(1f, 0.5f);
        numberRect.anchorMax = new Vector2(1f, 0.5f);
        numberRect.pivot = new Vector2(1f, 0.5f);
        numberRect.anchoredPosition = new Vector2(-1f, 0f);
        numberRect.sizeDelta = new Vector2(textWidth, root.rect.height);
        varNum.alignment = TMPro.TextAlignmentOptions.Right;

        RectTransform iconRect = varIcon.rectTransform;
        iconRect.anchorMin = new Vector2(1f, 0.5f);
        iconRect.anchorMax = new Vector2(1f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(-textWidth - 11f, 0f);
        iconRect.sizeDelta = new Vector2(16f, 16f);
    }

    private void CacheDefaultNumColorIfNeeded()
    {
        if (_defaultNumColorCached || varNum == null)
            return;

        _defaultNumColor = varNum.color;
        _defaultNumColorCached = true;
    }

    internal void ConfigureNumberLayout(bool hasIcon)
    {
        RectTransform iconRect = varIcon.rectTransform;
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(10f, 0f);
        iconRect.sizeDelta = new Vector2(16f, 16f);

        RectTransform rect = varNum.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(hasIcon ? 26f : 1f, 0f);
        rect.offsetMax = new Vector2(-1f, 0f);
        varNum.enableAutoSizing = true;
        varNum.fontSizeMin = 8f;
        varNum.fontSizeMax = 16f;
        varNum.enableWordWrapping = false;
        varNum.characterSpacing = 0f;
        varNum.alignment = TMPro.TextAlignmentOptions.Left;
    }
}
