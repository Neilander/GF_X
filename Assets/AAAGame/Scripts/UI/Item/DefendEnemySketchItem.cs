using UnityEngine;
using UnityGameFramework.Runtime;

public partial class DefendEnemySketchItem : UIItemBase
{
    public void SetData(Vector2 anchoredPosition, float arrowAngle, string label)
    {
        if (transform is RectTransform rectTransform)
            rectTransform.anchoredPosition = anchoredPosition;

        if (varArrow != null)
            varArrow.localRotation = Quaternion.Euler(0f, 0f, arrowAngle);

        if (varText != null)
            varText.text = label;
    }
}
