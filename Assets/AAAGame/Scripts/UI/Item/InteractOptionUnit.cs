using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class InteractOptionUnit : UIItemBase
{
    public RectTransform OptionCostRoot => varOptionCost;

    public void SetData(
        string displayName,
        string displayDesc,
        string key = null,
        bool enabled = true,
        Action onHoldFull = null,
        bool logicTimedHold = false)
    {
        if (varOptionName != null)
            varOptionName.text = displayName ?? string.Empty;

        if (varOptionDesc != null)
        {
            varOptionDesc.text = displayDesc ?? string.Empty;
            varOptionDesc.gameObject.SetActive(!string.IsNullOrWhiteSpace(displayDesc));
        }

        if (varOptionKey != null)
        {
            varOptionKey.text = key ?? string.Empty;
            varOptionKey.gameObject.SetActive(!string.IsNullOrWhiteSpace(key));
        }

        // “置灰”：不引入新颜色，仅通过透明度降低实现。
        float alpha = enabled ? 1f : 0.35f;
        if (varOptionName != null)
        {
            var c = varOptionName.color;
            c.a = alpha;
            varOptionName.color = c;
        }

        if (varOptionDesc != null)
        {
            var c = varOptionDesc.color;
            c.a = alpha;
            varOptionDesc.color = c;
        }

        if (varOptionKey != null)
        {
            var c = varOptionKey.color;
            c.a = alpha;
            varOptionKey.color = c;
        }

        if (varFillProgress != null)
        {
            varFillProgress.onFull = logicTimedHold ? null : onHoldFull;
            varFillProgress.AllowHold = enabled && (onHoldFull != null || logicTimedHold);
            varFillProgress.SetExternalHolding(false);
            varFillProgress.SetExternalLogicProgress(logicTimedHold, 0f);
            varFillProgress.ResetProgress();
            varFillProgress.gameObject.SetActive(onHoldFull != null || logicTimedHold);
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

    public void SetLogicHoldProgress(bool allowHold, Fix64 progress)
    {
        if (varFillProgress == null)
            return;

        varFillProgress.AllowHold = allowHold;
        varFillProgress.SetExternalLogicProgress(true, allowHold ? (float)progress : 0f);
    }
}
