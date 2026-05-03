using System;
using DG.Tweening;
using UnityEngine;

public partial class StarItem : UIItemBase
{
    public Color32 NormalColor = Color.white;
    public Color32 HighlightColor = new Color32(250, 112, 36, 255);
    public void SetHighlight(bool highlight)
    {
        varStarItem.color = highlight ? HighlightColor : NormalColor;
    }
}
