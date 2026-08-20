using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using TMPro;

public partial class BuildingInfoItem : UIItemBase
{
    public const float DetailPanelCenterOffset = 102f;
    public const float DetailPanelWidth = DetailPanelCenterOffset * 2f;
    public const float UnitPanelCenterOffset = DetailPanelCenterOffset;

    [SerializeField]
    private float progressDuration = 2f;
    private int m_PreviewSpriteRequestVersion;
    private RectTransform m_UnitPanel;
    private TextMeshProUGUI m_UnitName;
    private TextMeshProUGUI m_UnitDesc;
    private GameObject m_UnitPropertyList;

    public float ProgressDuration => Mathf.Max(0.01f, progressDuration);
    public GameObject PriceRoot => varPrice;
    public GameObject PropertyListRoot => varPropertyList;
    public GameObject ProgressRoot => varProgress;
    public GameObject PreviewRoot => varPreview;
    public GameObject CoinReservesRoot => varCoinReserves;
    public RectTransform HoldRoot => (varBuildingInfoItem != null ? varBuildingInfoItem.transform : transform) as RectTransform;
    public GameObject IconNumTemplate => varIconNumItem;
    public GameObject StarTemplate => varStarItem;
    public GameObject UnitPropertyListRoot
        => DetailPropertyListRoot;
    public GameObject DetailPropertyListRoot
    {
        get
        {
            CacheUnitPanel();
            return m_UnitPropertyList;
        }
    }

    public bool IsDetailPanelVisible
    {
        get
        {
            CacheUnitPanel();
            return m_UnitPanel.gameObject.activeSelf;
        }
    }

    public void SetDetailData(string displayName, string desc)
    {
        CacheUnitPanel();
        m_UnitPanel.gameObject.SetActive(true);
        m_UnitName.text = displayName ?? string.Empty;
        m_UnitDesc.text = desc ?? string.Empty;
    }

    public void SetDetailInfoVisible(bool visible)
    {
        CacheUnitPanel();
        m_UnitPanel.gameObject.SetActive(visible);
    }

    public void SetUnitData(string displayName, string desc)
    {
        SetDetailData(displayName, desc);
    }

    public void SetUnitInfoVisible(bool visible)
    {
        SetDetailInfoVisible(visible);
    }

    public float ApplyPanelLayout(
        bool reservePreviewColumn,
        bool reserveProgressRow,
        float minimumPanelHeight = 0f,
        bool compactToContent = false)
    {
        RectTransform root = HoldRoot;
        if (root == null || varName == null || varDesc == null || varPropertyList == null || varProgress == null)
            throw new System.InvalidOperationException("Building info item layout references are incomplete.");

        float contentLeft = reservePreviewColumn ? -90f : -300f;
        const float contentRight = 276f;
        float contentWidth = contentRight - contentLeft;
        float contentCenter = (contentLeft + contentRight) * 0.5f;

        int columnCount = reservePreviewColumn ? 4 : 6;
        GridLayoutGroup grid = ConfigurePropertyGrid(contentWidth, columnCount);
        ConfigureTextLayout();

        CalculateLayoutMetrics(
            varDesc.text,
            varDesc.gameObject.activeSelf,
            contentWidth,
            grid,
            columnCount,
            reserveProgressRow,
            out float descriptionHeight,
            out float propertyHeight,
            out float naturalHeight);

        const float topPadding = 12f;
        const float titleHeight = 28f;
        const float titleGap = 6f;
        const float contentGap = 6f;
        const float bottomRowHeight = 24f;
        float panelHeight = compactToContent
            ? Mathf.Max(naturalHeight, minimumPanelHeight)
            : Mathf.Max(176f, naturalHeight, minimumPanelHeight);
        root.sizeDelta = new Vector2(624f, panelHeight);

        if (varKey != null)
            SetRect(varKey.rectTransform, new Vector2(-82.76f, -46f), new Vector2(20f, 24f));

        float top = panelHeight * 0.5f - topPadding;
        float titleCenterY = top - titleHeight * 0.5f;
        SetRect(
            varName.rectTransform,
            new Vector2(contentLeft + (contentWidth - 86f) * 0.5f, titleCenterY),
            new Vector2(contentWidth - 86f, titleHeight));
        SetRect(
            varPrice.transform as RectTransform,
            new Vector2(262f, titleCenterY),
            new Vector2(76f, 24f));

        float cursor = top - titleHeight - titleGap;
        if (descriptionHeight > 0f)
        {
            SetRect(
                varDesc.rectTransform,
                new Vector2(contentCenter, cursor - descriptionHeight * 0.5f),
                new Vector2(contentWidth, descriptionHeight));
            cursor -= descriptionHeight + contentGap;
        }

        const float bottomPadding = 2f;
        const float contentBottomPadding = 12f;
        bool hasBottomRow = reserveProgressRow || varProgress.activeSelf || varCoinReserves.activeSelf;
        float bottomRowCenterY = -panelHeight * 0.5f + bottomPadding + bottomRowHeight * 0.5f;
        SetRect(varProgress.transform as RectTransform, new Vector2(contentCenter, bottomRowCenterY), new Vector2(contentWidth, 16f));
        SetRect(varCoinReserves.transform as RectTransform, new Vector2(252f, bottomRowCenterY), new Vector2(96f, 24f));

        float propertyBottom = -panelHeight * 0.5f
                               + (hasBottomRow ? bottomPadding : contentBottomPadding)
                               + (hasBottomRow ? bottomRowHeight + contentGap : 0f);
        SetRect(
            varPropertyList.transform as RectTransform,
            new Vector2(contentCenter, propertyBottom + propertyHeight * 0.5f),
            new Vector2(contentWidth, propertyHeight));

        ApplyDetailPanelLayout(panelHeight);

        return panelHeight;
    }

    public float MeasurePanelHeight(string description, bool reservePreviewColumn, bool reserveProgressRow)
    {
        RectTransform root = HoldRoot;
        if (root == null || varName == null || varDesc == null || varPropertyList == null || varProgress == null)
            throw new System.InvalidOperationException("Building info item layout references are incomplete.");

        float contentLeft = reservePreviewColumn ? -90f : -300f;
        const float contentRight = 276f;
        float contentWidth = contentRight - contentLeft;
        int columnCount = reservePreviewColumn ? 4 : 6;
        GridLayoutGroup grid = ConfigurePropertyGrid(contentWidth, columnCount);
        ConfigureTextLayout();

        CalculateLayoutMetrics(
            description,
            !string.IsNullOrWhiteSpace(description),
            contentWidth,
            grid,
            columnCount,
            reserveProgressRow,
            out _,
            out _,
            out float naturalHeight);
        return Mathf.Max(176f, naturalHeight);
    }

    private GridLayoutGroup ConfigurePropertyGrid(float contentWidth, int columnCount)
    {
        GridLayoutGroup grid = varPropertyList.GetComponent<GridLayoutGroup>();
        if (grid == null)
            throw new System.InvalidOperationException("Building info property list requires GridLayoutGroup.");

        const float spacing = 4f;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columnCount;
        grid.spacing = new Vector2(spacing, spacing);
        grid.cellSize = new Vector2((contentWidth - spacing * (columnCount - 1)) / columnCount, 20f);
        grid.childAlignment = TextAnchor.UpperLeft;
        return grid;
    }

    private void ConfigureTextLayout()
    {
        varName.enableAutoSizing = true;
        varName.fontSizeMin = 14f;
        varName.fontSizeMax = 20f;
        varName.enableWordWrapping = false;

        varDesc.enableAutoSizing = false;
        varDesc.fontSize = 12.5f;
        varDesc.enableWordWrapping = true;
    }

    private void CalculateLayoutMetrics(
        string description,
        bool descriptionVisible,
        float contentWidth,
        GridLayoutGroup grid,
        int columnCount,
        bool reserveProgressRow,
        out float descriptionHeight,
        out float propertyHeight,
        out float naturalHeight)
    {
        const float spacing = 4f;
        int propertyCount = 0;
        for (int i = 0; i < varPropertyList.transform.childCount; i++)
        {
            if (varPropertyList.transform.GetChild(i).gameObject.activeSelf)
                propertyCount++;
        }

        int propertyRows = Mathf.Max(1, Mathf.CeilToInt(propertyCount / (float)columnCount));
        propertyHeight = propertyRows * grid.cellSize.y + (propertyRows - 1) * spacing;

        descriptionHeight = descriptionVisible
            ? Mathf.Max(22f, Mathf.Ceil(varDesc.GetPreferredValues(description ?? string.Empty, contentWidth, 0f).y))
            : 0f;

        const float topPadding = 12f;
        const float bottomPadding = 2f;
        const float contentBottomPadding = 12f;
        const float titleHeight = 28f;
        const float titleGap = 6f;
        const float contentGap = 6f;
        const float bottomRowHeight = 24f;
        bool hasBottomRow = reserveProgressRow || varProgress.activeSelf || varCoinReserves.activeSelf;
        float bottomRowSpace = hasBottomRow ? contentGap + bottomRowHeight : 0f;
        naturalHeight = topPadding
                        + titleHeight
                        + titleGap
                        + descriptionHeight
                        + (descriptionHeight > 0f ? contentGap : 0f)
                        + propertyHeight
                        + bottomRowSpace
                        + (hasBottomRow ? bottomPadding : contentBottomPadding);
    }

    private static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        if (rect == null)
            throw new System.InvalidOperationException("Building info item RectTransform is missing.");

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private void CacheUnitPanel()
    {
        if (m_UnitPanel != null)
            return;

        Transform panel = transform.Find("UnitPanel");
        Transform name = panel != null ? panel.Find("UnitName") : null;
        Transform desc = panel != null ? panel.Find("UnitDesc") : null;
        Transform propertyList = panel != null ? panel.Find("UnitPropertyList") : null;
        m_UnitPanel = panel as RectTransform;
        m_UnitName = name != null ? name.GetComponent<TextMeshProUGUI>() : null;
        m_UnitDesc = desc != null ? desc.GetComponent<TextMeshProUGUI>() : null;
        m_UnitPropertyList = propertyList != null ? propertyList.gameObject : null;
        if (m_UnitPanel == null || m_UnitName == null || m_UnitDesc == null || m_UnitPropertyList == null)
            throw new System.InvalidOperationException("Building info item unit panel references are incomplete.");
    }

    private void ApplyDetailPanelLayout(float mainPanelHeight)
    {
        CacheUnitPanel();
        if (!m_UnitPanel.gameObject.activeSelf)
            return;

        const float contentWidth = 178f;
        const float topPadding = 12f;
        const float bottomPadding = 12f;
        const float titleHeight = 28f;
        const float titleGap = 4f;
        const float contentGap = 6f;
        const float cellHeight = 20f;
        const float spacing = 2f;
        const int columnCount = 2;

        m_UnitName.enableAutoSizing = true;
        m_UnitName.fontSizeMin = 14f;
        m_UnitName.fontSizeMax = 20f;
        m_UnitName.enableWordWrapping = false;
        m_UnitDesc.enableAutoSizing = true;
        m_UnitDesc.fontSizeMin = 7f;
        m_UnitDesc.fontSizeMax = 11.5f;
        m_UnitDesc.enableWordWrapping = true;
        m_UnitDesc.margin = Vector4.zero;

        bool hasDescription = !string.IsNullOrWhiteSpace(m_UnitDesc.text);
        m_UnitDesc.gameObject.SetActive(hasDescription);
        float descriptionHeight = hasDescription
            ? Mathf.Max(22f, Mathf.Ceil(m_UnitDesc.GetPreferredValues(m_UnitDesc.text, contentWidth, 0f).y))
            : 0f;

        GridLayoutGroup grid = m_UnitPropertyList.GetComponent<GridLayoutGroup>();
        if (grid == null)
            throw new System.InvalidOperationException("Building info detail property list requires GridLayoutGroup.");

        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columnCount;
        grid.spacing = new Vector2(spacing, spacing);
        grid.cellSize = new Vector2((contentWidth - spacing * (columnCount - 1)) / columnCount, cellHeight);
        grid.childAlignment = TextAnchor.UpperLeft;

        int propertyCount = 0;
        for (int i = 0; i < m_UnitPropertyList.transform.childCount; i++)
        {
            if (m_UnitPropertyList.transform.GetChild(i).gameObject.activeSelf)
                propertyCount++;
        }

        int propertyRows = Mathf.CeilToInt(propertyCount / (float)columnCount);
        float propertyHeight = propertyRows > 0
            ? propertyRows * cellHeight + (propertyRows - 1) * spacing
            : 0f;
        float detailHeight = topPadding
                             + titleHeight
                             + (hasDescription ? titleGap + descriptionHeight : 0f)
                             + (propertyRows > 0 ? contentGap + propertyHeight : 0f)
                             + bottomPadding;
        detailHeight = Mathf.Max(64f, detailHeight);

        SetRect(
            m_UnitPanel,
            new Vector2(409f, (mainPanelHeight - detailHeight) * 0.5f),
            new Vector2(194f, detailHeight));

        float cursor = detailHeight * 0.5f - topPadding;
        SetRect(m_UnitName.rectTransform, new Vector2(0f, cursor - titleHeight * 0.5f), new Vector2(contentWidth, titleHeight));
        cursor -= titleHeight;

        if (hasDescription)
        {
            cursor -= titleGap;
            SetRect(
                m_UnitDesc.rectTransform,
                new Vector2(0f, cursor - descriptionHeight * 0.5f),
                new Vector2(contentWidth, descriptionHeight));
            cursor -= descriptionHeight;
        }

        if (propertyRows > 0)
        {
            cursor -= contentGap;
            SetRect(
                m_UnitPropertyList.transform as RectTransform,
                new Vector2(0f, cursor - propertyHeight * 0.5f),
                new Vector2(contentWidth, propertyHeight));
        }
        else
        {
            SetRect(m_UnitPropertyList.transform as RectTransform, Vector2.zero, new Vector2(contentWidth, 0f));
        }
    }

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

    public void SetCoinReservesVisible(bool visible)
    {
        if (varCoinReserves != null)
            varCoinReserves.SetActive(visible);
    }

    public void SetPreviewImage(string spriteRelativePath)
    {
        if (varImage == null)
            return;

        if (string.IsNullOrWhiteSpace(spriteRelativePath))
        {
            ClearPreviewImage();
            return;
        }

        m_PreviewSpriteRequestVersion++;
        int requestVersion = m_PreviewSpriteRequestVersion;
        string assetPath = UtilityBuiltin.AssetsPath.GetSpritesPath(spriteRelativePath);

        varImage.sprite = null;
        varImage.gameObject.SetActive(true);

        GF.UI.LoadSprite(assetPath, sp =>
        {
            if (varImage == null || requestVersion != m_PreviewSpriteRequestVersion)
                return;

            if (sp == null)
            {
                varImage.gameObject.SetActive(false);
                return;
            }

            varImage.sprite = sp;
        });
    }

    public void ClearPreviewImage()
    {
        m_PreviewSpriteRequestVersion++;
        if (varImage == null)
            return;

        varImage.sprite = null;
        varImage.gameObject.SetActive(false);
    }
}
