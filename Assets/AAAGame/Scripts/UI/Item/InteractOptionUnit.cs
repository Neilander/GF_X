using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class InteractOptionUnit : UIItemBase
{
    public RectTransform OptionCostRoot => varOptionCost;

    public void SetData(string displayName, string key = null, bool enabled = true, Action onHoldFull = null)
    {
        varOptionText.text = displayName;
        if (varOptionKey != null)
        {
            varOptionKey.text = key ?? string.Empty;
            varOptionKey.gameObject.SetActive(!string.IsNullOrWhiteSpace(key));
        }

        // “置灰”：不引入新颜色，仅通过透明度降低实现。
        float alpha = enabled ? 1f : 0.35f;
        if (varOptionText != null)
        {
            var c = varOptionText.color;
            c.a = alpha;
            varOptionText.color = c;
        }

        if (varOptionKey != null)
        {
            var c = varOptionKey.color;
            c.a = alpha;
            varOptionKey.color = c;
        }

        if (varFillProgress != null)
        {
            varFillProgress.onFull = onHoldFull;
            varFillProgress.AllowHold = enabled && onHoldFull != null;
            varFillProgress.SetExternalHolding(false);
            varFillProgress.ResetProgress();
            varFillProgress.gameObject.SetActive(onHoldFull != null);
        }
    }

    public void SetHoldState(bool allowHold, bool keyHolding)
    {
        if (varFillProgress == null)
            return;

        varFillProgress.AllowHold = allowHold;
        varFillProgress.SetExternalHolding(allowHold && keyHolding);
        if (!allowHold)
            varFillProgress.ResetProgress();
    }
}
