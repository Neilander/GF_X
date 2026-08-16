using UnityGameFramework.Runtime;
using System.Collections.Generic;
using System;
using GameFramework.Event;
using UnityEngine;
using System.Text;
using TMPro;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class BuildingInfoTips : UIFormBase
{
    public const string P_TargetHost = "TargetHost";

    [SerializeField] private Vector2 uiOffset = new(0f, 80f);

    private const string CoinIconPath = "UI/Icon/Coin.png";
    private const string ForceIconPath = "UI/Icon/Force.png";
    private const string SupplyIconPath = "UI/Icon/Supply.png";
    private const float RecycleHoldDurationSeconds = 2f;
    private const float HeaderHeight = 72f;
    private const float BottomPadding = 10f;
    private const float PreviewReservedInfoHeight = 152f;
    private const string DemolishText = "拆除";
    private const string UndoTextFormat = "撤销  <sprite name=\"Coin\"> {0}";

    // 用 Unicode 转义避免文件编码导致的 αβγδ 乱码。
    private static readonly char[] s_OptionMarks = { '\u03B1', '\u03B2', '\u03B3', '\u03B4' };

    private InputManager m_InputManager;
    private InteractionHost m_TargetHost;
    private BuildingEntity m_TargetBuilding;
    private BuildingInfoItem m_ItemTemplate;
    private GameObject m_IconNumTemplate;
    private TextMeshProUGUI m_LevelTitleText;
    private float m_RecycleHoldProgress;
    private bool m_RecycleTriggered;
    private bool m_HasDetailPanel;

    private sealed class SelectedUpgradeInfo
    {
        public int OptionIndex;
        public TechData TechData;
    }

    public void ApplyTarget(object target)
    {
        m_TargetHost = ResolveTargetHost(target);
        m_TargetBuilding = ResolveTargetBuilding(target);
        CacheTemplates();
        RefreshView();
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        ApplyTarget(Params != null ? Params.Get(P_TargetHost) : null);

        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        GF.Event.Subscribe(ArmyBuildingCardPropertyChangedEventArgs.EventId, OnArmyPropertyChanged);
        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        GF.Event.Unsubscribe(ArmyBuildingCardPropertyChangedEventArgs.EventId, OnArmyPropertyChanged);
        GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);

        m_InputManager = null;
        m_TargetHost = null;
        m_TargetBuilding = null;
        ResetRecycleHoldState();

        base.OnClose(isShutdown, userData);
    }

    private void Update()
    {
        UpdatePanelPosition();
        UpdateRecycleHoldProgress();
    }

    private void RefreshView()
    {
        CacheTemplates();
        RefreshLevelTitle();
        ClearSpawnedItems();
        m_HasDetailPanel = false;
        RefreshRecycleArea();

        if (!CanShowInfoTips())
            return;

        Transform root = varInfoRoot != null ? varInfoRoot : transform;
        BuildingInfoItem item = SpawnItem<UIItemObject>(varBuildingInfoItem, root).itemLogic as BuildingInfoItem;
        if (item == null)
            return;

        ComposeDisplayText(m_TargetBuilding, out string displayName, out string displayDesc);
        item.SetData(string.Empty, displayName, displayDesc);
        item.SetPreviewVisible(false);
        item.SetPriceVisible(false);
        item.SetProgressVisible(false);

        PopulateProperties(item, m_TargetBuilding);
        PopulateCoinReserves(item, m_TargetBuilding);
        float infoHeight = item.ApplyPanelLayout(
            reservePreviewColumn: true,
            reserveProgressRow: false,
            minimumPanelHeight: PreviewReservedInfoHeight,
            compactToContent: true);
        ApplyAdaptiveLayout(infoHeight);
    }

    private void ApplyAdaptiveLayout(float infoHeight)
    {
        if (varInfoPanel == null || varInfoRoot == null)
            throw new InvalidOperationException("BuildingInfoTips adaptive layout references are incomplete.");
        if (infoHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(infoHeight));

        RectTransform background = varInfoPanel.Find("Bg") as RectTransform;
        RectTransform levelBadge = varInfoPanel.Find("LevelTitleBadge") as RectTransform;
        RectTransform previewFrame = varInfoPanel.Find("PreviewFrame") as RectTransform;
        if (background == null || levelBadge == null || previewFrame == null)
            throw new InvalidOperationException("BuildingInfoTips adaptive layout objects are missing.");

        float totalHeight = HeaderHeight + infoHeight + BottomPadding;
        varInfoPanel.sizeDelta = new Vector2(varInfoPanel.sizeDelta.x, totalHeight);
        SetCenteredRect(background, Vector2.zero, new Vector2(background.sizeDelta.x, totalHeight));

        float top = totalHeight * 0.5f;
        SetCenteredPosition(levelBadge, new Vector2(levelBadge.anchoredPosition.x, top - 39f));
        if (varRecycleBtn != null)
        {
            RectTransform recycle = varRecycleBtn.transform as RectTransform
                                    ?? throw new InvalidOperationException("BuildingInfoTips recycle button requires RectTransform.");
            SetCenteredPosition(recycle, new Vector2(recycle.anchoredPosition.x, top - 32f));
        }

        float infoTop = top - HeaderHeight;
        float infoCenter = infoTop - infoHeight * 0.5f;
        SetCenteredPosition(varInfoRoot, new Vector2(0f, infoCenter));
        SetCenteredPosition(previewFrame, new Vector2(previewFrame.anchoredPosition.x, infoCenter + 12f));
    }

    private static void SetCenteredPosition(RectTransform rect, Vector2 anchoredPosition)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        SetCenteredPosition(rect, anchoredPosition);
        rect.sizeDelta = sizeDelta;
    }

    private void PopulateProperties(BuildingInfoItem item, BuildingEntity building)
    {
        if (item == null || building == null || building.buildingData == null)
            return;

        Transform root = item.PropertyListRoot != null ? item.PropertyListRoot.transform : null;
        if (root == null)
            return;

        switch (building.buildingData.Type)
        {
            case BuilType.Base:
                SpawnProperty(root, SupplyIconPath, BuildingPanelPresentation.GetBaseSupplyText(building.buildingData));
                break;

            case BuilType.Prod:
                SpawnProperty(
                    root,
                    CoinIconPath,
                    BuildingPanelPresentation.GetProductionText(building.buildingData, building));
                break;

            case BuilType.Army:
                SpawnProperty(
                    root,
                    SupplyIconPath,
                    BuildingPanelPresentation.GetArmySupplyText(building.buildingData, building));
                SpawnProperty(
                    root,
                    ForceIconPath,
                    BuildingPanelPresentation.GetArmyForceText(building.buildingData, building));
                break;
        }

        var stats = new List<BuildingPanelStat>();
        BuildingPanelPresentation.CollectBuildingCombatStats(building.buildingData, building, stats);
        for (int i = 0; i < stats.Count; i++)
            SpawnGlyphProperty(root, stats[i]);

        PopulateDetailProperties(item, building);
    }

    private void PopulateDetailProperties(BuildingInfoItem item, BuildingEntity building)
    {
        BuildingData data = building?.buildingData;
        if (data != null && data.Type == BuilType.Army)
        {
            item.SetDetailData(
                BuildingPanelPresentation.GetUnitName(data),
                BuildingPanelPresentation.GetUnitDescription(data));
            Transform unitRoot = item.DetailPropertyListRoot.transform;
            var unitStats = new List<BuildingPanelStat>();
            BuildingPanelPresentation.CollectUnitStats(data, unitStats);
            for (int i = 0; i < unitStats.Count; i++)
                SpawnGlyphProperty(unitRoot, unitStats[i]);
            m_HasDetailPanel = true;
            return;
        }

        item.SetDetailInfoVisible(false);
    }

    private void SpawnProperty(Transform root, string iconPath, string numberText)
    {
        if (root == null || m_IconNumTemplate == null)
            return;

        IconNumItem iconNum = SpawnItem<UIItemObject>(m_IconNumTemplate, root).itemLogic as IconNumItem;
        if (iconNum == null)
            return;

        iconNum.SetData(iconPath, numberText);
    }

    private void SpawnGlyphProperty(Transform root, BuildingPanelStat stat)
    {
        if (root == null || m_IconNumTemplate == null)
            return;

        IconNumItem iconNum = SpawnItem<UIItemObject>(m_IconNumTemplate, root).itemLogic as IconNumItem;
        if (iconNum == null)
            return;

        iconNum.SetGlyphData(stat.Glyph, stat.Value);
    }

    private void PopulateCoinReserves(BuildingInfoItem item, BuildingEntity building)
    {
        if (item == null || building == null || building.buildingData == null)
            return;

        bool shouldShow = building.buildingData.Type == BuilType.Prod;
        item.SetCoinReservesVisible(shouldShow);
        if (!shouldShow || m_IconNumTemplate == null)
            return;

        Transform root = item.CoinReservesRoot != null ? item.CoinReservesRoot.transform : null;
        if (root == null)
            return;

        int reserves = InGameDataModel.GetProductionBuildingCoinReserves(building.BuildingInstanceId);
        IconNumItem iconNum = SpawnItem<UIItemObject>(m_IconNumTemplate, root).itemLogic as IconNumItem;
        if (iconNum == null)
            return;

        iconNum.SetLeadingLabelData("剩余", "Coin", reserves.ToString());
        iconNum.FitParentRect();
        iconNum.AlignTextRight();
    }

    private void ComposeDisplayText(BuildingEntity building, out string name, out string desc)
    {
        name = string.Empty;
        desc = string.Empty;

        if (building == null || building.buildingData == null)
            return;

        BuildingData data = building.buildingData;
        // Name 作为标题展示，不走富文本；描述默认走富文本规则。
        name = BuildingPanelPresentation.GetBuildingName(data);
        desc = BuildingPanelPresentation.GetDescription(data);

        List<SelectedUpgradeInfo> selected = CollectSelectedUpgrades(building);
        if (selected.Count <= 0)
            return;

        StringBuilder markBuilder = new();
        for (int i = 0; i < selected.Count; i++)
            markBuilder.Append(GetOptionMark(selected[i].OptionIndex));

        name += "-" + markBuilder;

        for (int i = 0; i < selected.Count; i++)
        {
            TechData techData = selected[i].TechData;
            if (techData == null)
                continue;
            if (techData.ScopeType == TechScopeType.Skill)
                continue;

            string optionDesc = techData.GetFormattedDesc();
            if (string.IsNullOrWhiteSpace(optionDesc))
                continue;

            desc += "\n·" + optionDesc;
        }
    }

    private List<SelectedUpgradeInfo> CollectSelectedUpgrades(BuildingEntity building)
    {
        List<SelectedUpgradeInfo> results = new();
        if (building == null || building.buildingData == null || string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            return results;

        BuildingData current = building.buildingData;
        for (int lv = 1; lv < current.Lv; lv++)
        {
            string levelIdentifier = ReplaceLevel(current.Identifier, lv);
            if (string.IsNullOrWhiteSpace(levelIdentifier))
                continue;

            BuildingData dataAtLevel = BuildingDataModel.GetBuildingData(levelIdentifier);
            if (dataAtLevel == null)
                continue;

            AppendSelectedUpgrade(dataAtLevel.UpgradeTechIDs, building.BuildingInstanceId, results);
        }

        return results;
    }

    private static void AppendSelectedUpgrade(string[] optionTechIds, string buildingInstanceId, List<SelectedUpgradeInfo> results)
    {
        if (optionTechIds == null || optionTechIds.Length == 0 || string.IsNullOrWhiteSpace(buildingInstanceId) || results == null)
            return;

        for (int i = 0; i < optionTechIds.Length; i++)
        {
            string techId = optionTechIds[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (!InGameDataModel.HasUnlockedTech(techId, buildingInstanceId))
                continue;

            TechData techData = TechDataModel.GetTechData(techId);
            if (techData == null)
                continue;

            results.Add(new SelectedUpgradeInfo
            {
                OptionIndex = i,
                TechData = techData,
            });
            return;
        }
    }

    private static string ReplaceLevel(string identifier, int lv)
    {
        if (string.IsNullOrWhiteSpace(identifier) || lv <= 0)
            return null;

        int lvIndex = identifier.LastIndexOf("_Lv", StringComparison.Ordinal);
        if (lvIndex < 0)
            return null;

        return identifier.Substring(0, lvIndex + 3) + lv;
    }

    private bool CanShowInfoTips()
    {
        if (m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return false;

        return m_TargetBuilding.buildingData.Lv >= 3;
    }

    private void UpdatePanelPosition()
    {
        if (varInfoPanel == null)
            return;

        RectTransform panelRect = varInfoPanel;
        RectTransform parentRect = panelRect.parent as RectTransform;
        if (parentRect == null)
            return;

        Vector3 worldPos;
        if (m_TargetHost != null)
            worldPos = m_TargetHost.GetPromptPosition();
        else if (m_TargetBuilding != null)
            worldPos = m_TargetBuilding.transform.position;
        else
            return;

        Vector3 uiPos = GF.UI.PositionWorldToUI(worldPos, parentRect);
        Vector2 detailOffset = m_HasDetailPanel
            ? Vector2.left * BuildingInfoItem.DetailPanelCenterOffset
            : Vector2.zero;
        panelRect.anchoredPosition = (Vector2)uiPos + uiOffset + detailOffset;
        BuildingPanelScreenClamp.ClampToParent(panelRect, parentRect);
    }

    private void OnTechUnlocked(object sender, GameEventArgs e)
    {
        TechUnlockedEventArgs args = e as TechUnlockedEventArgs;
        if (args == null || m_TargetBuilding == null)
            return;

        if (!string.IsNullOrWhiteSpace(args.SourceBuildingInstanceId)
            && !string.Equals(args.SourceBuildingInstanceId, m_TargetBuilding.BuildingInstanceId, StringComparison.Ordinal))
        {
            return;
        }

        RefreshView();
    }

    private void OnArmyPropertyChanged(object sender, GameEventArgs e)
    {
        ArmyBuildingCardPropertyChangedEventArgs args = e as ArmyBuildingCardPropertyChangedEventArgs;
        if (args == null || m_TargetBuilding == null)
            return;

        if (args.EntityId != m_TargetBuilding.Id)
            return;

        RefreshView();
    }

    private void OnIngameValueChanged(object sender, GameEventArgs e)
    {
        IngameValueChangedEventArgs args = e as IngameValueChangedEventArgs;
        if (args == null || args.DataType != IngameValueType.Phase)
            return;

        RefreshView();
    }

    private void OnEntityFactionChanged(object sender, GameEventArgs e)
    {
        EntityFactionChangedEventArgs args = e as EntityFactionChangedEventArgs;
        if (args == null || m_TargetBuilding == null || args.EntityId != m_TargetBuilding.Id)
            return;

        RefreshView();
    }

    private void CacheTemplates()
    {
        if (m_LevelTitleText == null)
        {
            Transform title = varInfoPanel != null
                ? varInfoPanel.Find("LevelTitleBadge/Title")
                : null;
            m_LevelTitleText = title != null ? title.GetComponent<TextMeshProUGUI>() : null;
            if (m_LevelTitleText == null)
                throw new InvalidOperationException("BuildingInfoTips requires InfoPanel/LevelTitleBadge/Title.");
        }

        if (varBuildingInfoItem == null)
            return;

        if (m_ItemTemplate == null || m_ItemTemplate.gameObject != varBuildingInfoItem)
            m_ItemTemplate = varBuildingInfoItem.GetComponent<BuildingInfoItem>();

        if (m_ItemTemplate == null)
            return;

        m_IconNumTemplate = m_ItemTemplate.IconNumTemplate;
    }

    private void RefreshLevelTitle()
    {
        if (m_TargetBuilding?.buildingData == null)
            return;

        m_LevelTitleText.text = BuildingPanelPresentation.GetBuildingLevelTitle(m_TargetBuilding.buildingData.Lv);
    }

    private void ClearSpawnedItems()
    {
        UnspawnItemTemplate(m_IconNumTemplate);
        UnspawnItemTemplate(varBuildingInfoItem);
    }

    private void RefreshRecycleArea()
    {
        bool visible = CanShowRecycle();
        if (varRecycleBtn != null)
            varRecycleBtn.SetActive(visible);

        if (!visible)
        {
            ResetRecycleHoldState();
            return;
        }

        if (varRecycleText != null)
        {
            BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
            varRecycleText.text = buildManager != null && buildManager.IsBuildingPhaseUndo(m_TargetBuilding)
                ? string.Format(UndoTextFormat, ResolveRecycleRefund())
                : DemolishText;
        }

        if (varRecycleFill != null)
            varRecycleFill.fillAmount = Mathf.Clamp01(m_RecycleHoldProgress);
    }

    private void UpdateRecycleHoldProgress()
    {
        if (!CanShowRecycle())
        {
            ResetRecycleHoldState();
            return;
        }

        bool holding = IsPointerHoldingOnRecycleButton();
        m_RecycleHoldProgress = AdvanceRecycleHoldProgress(
            m_RecycleHoldProgress,
            holding,
            Time.deltaTime);

        if (varRecycleFill != null)
            varRecycleFill.fillAmount = m_RecycleHoldProgress;

        if (!m_RecycleTriggered && holding && m_RecycleHoldProgress >= 1f)
        {
            m_RecycleTriggered = true;
            TryRecycleBuilding();
        }

        if (!holding && m_RecycleHoldProgress <= 1e-4f)
            m_RecycleTriggered = false;
    }

    private bool CanShowRecycle()
    {
        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        return buildManager != null && buildManager.CanRecycleBuilding(m_TargetBuilding);
    }

    private int ResolveRecycleRefund()
    {
        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        return buildManager != null ? buildManager.CalculateRecycleRefund(m_TargetBuilding) : 0;
    }

    private bool IsPointerHoldingOnRecycleButton()
    {
        RectTransform itemRect = varRecycleBtn != null ? varRecycleBtn.transform as RectTransform : null;
        InputManager inputManager = EnsureInputManager();
        if (itemRect == null || inputManager == null || !inputManager.IsPrimaryPointerPressed())
            return false;

        Vector2 screenPosition = inputManager.GetPointerScreenPosition();
        Canvas canvas = itemRect.GetComponentInParent<Canvas>();
        Camera uiCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = canvas.worldCamera != null ? canvas.worldCamera : GF.UICamera;

        return RectTransformUtility.RectangleContainsScreenPoint(itemRect, screenPosition, uiCamera);
    }

    private InputManager EnsureInputManager()
    {
        if (m_InputManager == null)
            m_InputManager = GameEntry.GetComponent<InputManager>();
        return m_InputManager;
    }

    private static float AdvanceRecycleHoldProgress(float current, bool holding, float deltaTime)
    {
        float delta = Mathf.Max(0f, deltaTime) / RecycleHoldDurationSeconds;
        return holding
            ? Mathf.Min(1f, current + delta)
            : Mathf.Max(0f, current - delta);
    }

    private bool TryRecycleBuilding()
    {
        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        if (buildManager == null || m_TargetBuilding == null)
            return false;

        if (buildManager.RecycleBuilding(m_TargetBuilding))
        {
            GF.UI.Close(this.UIForm);
            return true;
        }

        return false;
    }

    private void ResetRecycleHoldState()
    {
        m_RecycleHoldProgress = 0f;
        m_RecycleTriggered = false;
        if (varRecycleFill != null)
            varRecycleFill.fillAmount = 0f;
    }

    private static InteractionHost ResolveTargetHost(object target)
    {
        if (target == null)
            return null;

        if (target is InteractionHost host)
            return host;

        if (target is BuildingEntity building)
            return building.GetComponent<InteractionHost>();

        return null;
    }

    private static BuildingEntity ResolveTargetBuilding(object target)
    {
        if (target == null)
            return null;

        if (target is BuildingEntity building)
            return building;

        if (target is InteractionHost host)
        {
            if (host.Owner is BuildingEntity owner)
                return owner;

            return host.GetComponent<BuildingEntity>();
        }

        return null;
    }

    private static string GetOptionMark(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex >= s_OptionMarks.Length)
            return "?";

        return s_OptionMarks[optionIndex].ToString();
    }

    private static string FormatSigned(int value)
    {
        return value > 0 ? $"+{value}" : value.ToString();
    }

    private void UnspawnItemTemplate(GameObject template)
    {
        if (template == null)
            return;

        UnspawnAllItem<UIItemObject>(template);
    }
}
