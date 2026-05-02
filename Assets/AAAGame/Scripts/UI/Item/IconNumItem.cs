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
        // SetSprite 是异步加载，native size 需要在 sprite 赋值后执行。
        varIcon.SetSprite(iconSpritePath, resize: true);
        // 数值文本默认走富文本规则（例如 +10 / -3 的颜色规则）。
        varNum.text = LocalizationTextManager.ProcessText(numberText ?? string.Empty);
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
}
