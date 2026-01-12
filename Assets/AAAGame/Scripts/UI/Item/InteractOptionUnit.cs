using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class InteractOptionUnit : UIItemBase
{
    public void SetData(string displayName, string key, bool enabled = true)
    {
        varOptionText.text = displayName;
        varOptionKey.text = key;

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
    }
}
