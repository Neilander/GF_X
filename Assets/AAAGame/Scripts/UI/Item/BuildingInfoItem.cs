using UnityEngine;

public partial class BuildingInfoItem : UIItemBase
{
    [SerializeField]
    private float progressDuration = 2f;

    public float ProgressDuration => Mathf.Max(0.01f, progressDuration);
    public GameObject PriceRoot => varPrice;
    public GameObject PropertyListRoot => varPropertyList;
    public GameObject ProgressRoot => varProgress;
    public GameObject PreviewRoot => varPreview;
    public RectTransform HoldRoot => (varBuildingInfoItem != null ? varBuildingInfoItem.transform : transform) as RectTransform;
    public GameObject IconNumTemplate => varIconNumItem;
    public GameObject StarTemplate => varStarItem;

    public void SetData(string keyText, string displayName, string desc)
    {
        if (varKey != null)
        {
            varKey.text = keyText ?? string.Empty;
            varKey.gameObject.SetActive(!string.IsNullOrWhiteSpace(keyText));
        }

        if (varName != null)
            varName.text = displayName ?? string.Empty;

        if (varDesc != null)
        {
            varDesc.text = desc ?? string.Empty;
            varDesc.gameObject.SetActive(!string.IsNullOrWhiteSpace(desc));
        }
    }

    public void SetExecutable(bool executable)
    {
        var root = varBuildingInfoItem != null ? varBuildingInfoItem.GetComponent<CanvasGroup>() : null;
        if (root == null && varBuildingInfoItem != null)
            root = varBuildingInfoItem.AddComponent<CanvasGroup>();

        if (root != null)
            root.alpha = executable ? 1f : 0.65f;
    }

    public void SetPreviewVisible(bool visible)
    {
        if (varPreview != null)
            varPreview.SetActive(visible);
    }

    public void SetPriceVisible(bool visible)
    {
        if (varPrice != null)
            varPrice.SetActive(visible);
    }

    public void SetProgressVisible(bool visible)
    {
        if (varProgress != null)
            varProgress.SetActive(visible);
    }
}
