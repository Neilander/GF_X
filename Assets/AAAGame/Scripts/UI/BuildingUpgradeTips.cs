using UnityGameFramework.Runtime;
using System.Collections.Generic;
using System;
using System.Text;
using GameFramework.Event;
using UnityEngine;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class BuildingUpgradeTips : UIFormBase
{
    public const string P_TargetHost = "TargetHost";

    [SerializeField] private Vector2 uiOffset = new(0f, 80f);

    private const string CoinIconPath = "UI/Icon/Coin.png";
    private const string ForceIconPath = "UI/Icon/Force.png";
    private const string SupplyIconPath = "UI/Icon/Supply.png";
    private const string CoinReservesPrefix = "剩余";

    private const string ConditionBaseLevelTextId = "Building_Upgrade_Cond_BaseLevel";
    private const string ConditionUniqueTechTextId = "Building_Upgrade_Cond_UniqueTech";
    private const string RecycleTextFormat = "回收  <sprite name=\"Coin\"> {0}";
    private static readonly Color32 DefaultLitColor = new(250, 112, 36, 255);

    private static readonly char[] s_OptionMarks = { '\u03B1', '\u03B2', '\u03B3', '\u03B4' };
    private static readonly Dictionary<string, int> s_LastSelectedOptionByBuildingLevel = new(StringComparer.Ordinal);

    private readonly List<UpgradeOptionBinding> m_UpgradeBindings = new();

    private InputManager m_InputManager;
    private InteractionHost m_TargetHost;
    private BuildingEntity m_TargetBuilding;
    private BuildingInfoItem m_ItemTemplate;
    private GameObject m_IconNumTemplate;
    private GameObject m_StarTemplate;

    private UpgradeOptionBinding m_SelectedBinding;
    private UpgradeOptionBinding m_HoldBinding;
    private float m_HoldProgressStars;
    private int m_LastHighlightStars;
    private float m_RecycleHoldProgress;
    private bool m_RecycleLogicHoldActive;
    private Color m_ConditionIconSatisfiedColor = DefaultLitColor;

    private sealed class UpgradeOptionBinding
    {
        public int OptionIndex;
        public string ActionName;
        public string TechId;
        public TechData TechData;
        public string UpgradeBuildingId;
        public BuildingData UpgradeBuildingData;
        public UpgradeButtonItem ButtonItem;
        public BuildingInfoItem PreviewItem;
        public readonly List<StarItem> Stars = new();
    }

    public void ApplyTarget(InteractionHost targetHost)
    {
        m_TargetHost = targetHost;
        m_TargetBuilding = ResolveTargetBuilding(targetHost);
        CacheTemplates();
        RefreshView();
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        ApplyTarget(Params != null ? Params.Get(P_TargetHost) as InteractionHost : null);

        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnStateChanged);
        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnStateChanged);
        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        LogicInteractionHoldService.RegisterPanelConsumer(
            ResolveLogicHoldButton,
            ResolveLogicHoldThresholdFrames,
            ExecuteLogicHold);
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnStateChanged);
        GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnStateChanged);
        GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        if (LogicInteractionHoldService.IsActive)
        {
            LogicInteractionHoldService.UnregisterPanelConsumer(
                ResolveLogicHoldButton,
                ResolveLogicHoldThresholdFrames,
                ExecuteLogicHold);
        }
        ClearCoinPreviewDeduction();
        ClearRuntimeState();

        base.OnClose(isShutdown, userData);
    }

    private void Update()
    {
        UpdatePanelPosition();
        UpdateButtonInput();
        UpdateUpgradeHoldProgress();
        UpdateRecycleHoldProgress();
        UpdateExecutableStates();
    }

    private void RefreshView()
    {
        CacheTemplates();
        ClearAllSpawnedItems();
        ClearRuntimeState();
        RefreshRecycleArea();

        if (!CanShowUpgradeTips())
            return;

        SpawnCurrentInfoItem();
        BuildUpgradeCandidates();
        SpawnUpgradeButtons();
        SelectDefaultOption();
    }

    private void SpawnCurrentInfoItem()
    {
        if (varInfoRoot == null || varBuildingInfoItem == null || m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return;

        BuildingInfoItem item = SpawnItem<UIItemObject>(varBuildingInfoItem, varInfoRoot).itemLogic as BuildingInfoItem;
        if (item == null)
            return;

        ComposeCurrentInfoText(m_TargetBuilding, out string name, out string desc);
        item.SetData(string.Empty, name, desc);
        item.SetPreviewVisible(false);
        item.SetPriceVisible(false);
        item.SetProgressVisible(false);

        PopulateCurrentProperties(item, m_TargetBuilding);
        PopulateCoinReserves(item, m_TargetBuilding);
    }

    private void BuildUpgradeCandidates()
    {
        if (m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return;

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            return;

        BuildingData current = m_TargetBuilding.buildingData;
        string[] optionIds = current.UpgradeTechIDs;
        if (optionIds == null)
            return;

        string upgradeBuildingId = current.Type == BuilType.Tech ? current.Identifier : BuildingDataModel.GetUpgradeID(current.Identifier);
        BuildingData upgradeData = string.IsNullOrWhiteSpace(upgradeBuildingId) ? null : BuildingDataModel.GetBuildingData(upgradeBuildingId);

        int optionIndex = 0;
        for (int i = 0; i < optionIds.Length && optionIndex < 4; i++)
        {
            string techId = optionIds[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            TechData techData = TechDataModel.GetTechData(techId);
            if (techData == null)
                continue;

            bool visible = current.Type == BuilType.Tech
                ? techManager.IsResearchOptionVisible(m_TargetBuilding, techId)
                : techManager.IsUpgradeOptionVisible(m_TargetBuilding, upgradeBuildingId, techId);
            if (!visible)
                continue;

            m_UpgradeBindings.Add(new UpgradeOptionBinding
            {
                OptionIndex = optionIndex,
                ActionName = $"Player/Build{optionIndex + 1}",
                TechId = techId,
                TechData = techData,
                UpgradeBuildingId = upgradeBuildingId,
                UpgradeBuildingData = upgradeData,
            });

            optionIndex++;
        }
    }

    private void SpawnUpgradeButtons()
    {
        if (varUpgradeButtonArea == null || varUpgradeButtonItem == null)
            return;

        for (int i = 0; i < m_UpgradeBindings.Count; i++)
        {
            UpgradeOptionBinding binding = m_UpgradeBindings[i];
            if (binding == null)
                continue;

            UpgradeButtonItem buttonItem = SpawnItem<UIItemObject>(varUpgradeButtonItem, varUpgradeButtonArea).itemLogic as UpgradeButtonItem;
            if (buttonItem == null)
                continue;

            int capturedIndex = i;
            buttonItem.SetData(
                optionIndex: binding.OptionIndex,
                keyText: InputGetKeyText.GetKeyText(binding.ActionName),
                selected: false,
                onClick: () => SelectOption(capturedIndex, true),
                onHover: () => SelectOption(capturedIndex, true));
            buttonItem.SetExecutable(IsOptionExecutable(binding));

            binding.ButtonItem = buttonItem;
        }
    }

    private void SelectDefaultOption()
    {
        if (m_UpgradeBindings.Count <= 0)
            return;

        int selected = 0;
        string rememberKey = GetSelectionMemoryKey();
        if (!string.IsNullOrWhiteSpace(rememberKey)
            && s_LastSelectedOptionByBuildingLevel.TryGetValue(rememberKey, out int last)
            && last >= 0
            && last < m_UpgradeBindings.Count)
        {
            selected = last;
        }

        SelectOption(selected, false);
    }

    private void SelectOption(int index, bool remember)
    {
        if (index < 0 || index >= m_UpgradeBindings.Count)
            return;

        UpgradeOptionBinding binding = m_UpgradeBindings[index];
        if (binding == null)
            return;

        if (m_SelectedBinding == binding)
            return;

        for (int i = 0; i < m_UpgradeBindings.Count; i++)
        {
            UpgradeOptionBinding option = m_UpgradeBindings[i];
            if (option?.ButtonItem == null)
                continue;

            option.ButtonItem.SetSelected(option == binding);
        }

        m_SelectedBinding = binding;
        ResetHoldState();

        if (remember)
        {
            string rememberKey = GetSelectionMemoryKey();
            if (!string.IsNullOrWhiteSpace(rememberKey))
                s_LastSelectedOptionByBuildingLevel[rememberKey] = index;
        }

        SpawnSelectedUpgradePreview();
        RefreshConditionArea();
    }

    private void SpawnSelectedUpgradePreview()
    {
        UnspawnItemTemplate(m_IconNumTemplate);
        UnspawnItemTemplate(m_StarTemplate);
        UnspawnItemTemplate(varBuildingInfoItem);
        UnspawnItemTemplate(varGoalConditionItem);

        if (m_SelectedBinding == null || varUpgradeRoot == null || varBuildingInfoItem == null)
            return;

        // varBuildingInfoItem 模板同时用于当前信息区与升级预览区，回收后需要先重建当前信息区。
        SpawnCurrentInfoItem();

        BuildingInfoItem preview = SpawnItem<UIItemObject>(varBuildingInfoItem, varUpgradeRoot).itemLogic as BuildingInfoItem;
        if (preview == null)
            return;

        m_SelectedBinding.PreviewItem = preview;

        string keyText = InputGetKeyText.GetKeyText(m_SelectedBinding.ActionName);
        string name = ComposePreviewName(m_TargetBuilding, m_SelectedBinding);
        string desc = m_SelectedBinding.TechData != null ? m_SelectedBinding.TechData.GetFormattedDesc() : string.Empty;
        preview.SetData(keyText, name, desc);
        preview.SetPreviewVisible(false);
        preview.SetExecutable(IsSelectedOptionExecutable());
        preview.SetCoinReservesVisible(false);

        int cost = ResolveOptionCost(m_SelectedBinding);
        PopulatePrice(preview, cost);

        if (m_TargetBuilding != null && m_TargetBuilding.buildingData != null && m_TargetBuilding.buildingData.Type == BuilType.Base)
            SpawnProperty(preview.PropertyListRoot.transform, SupplyIconPath, "+30");

        SpawnProgressStars(m_SelectedBinding, Mathf.Max(1, cost));
    }

    private void RefreshConditionArea()
    {
        UnspawnItemTemplate(varGoalConditionItem);

        if (m_SelectedBinding == null || varConditionArea == null || varGoalConditionItem == null)
            return;

        // 条件1：基地等级条件（非 Base/Tech 建筑升级到 n 级）
        if (m_TargetBuilding != null && m_TargetBuilding.buildingData != null)
        {
            BuildingData current = m_TargetBuilding.buildingData;
            if (current.Type != BuilType.Base && current.Type != BuilType.Tech)
            {
                int requiredLevel = current.Lv + 1;
                string baseName = ResolveArchetypeBaseName(current.Arche);
                string format = LocalizationTextDataModel.GetText(ConditionBaseLevelTextId);
                string text = string.Format(format, baseName, requiredLevel);
                string capturedUpgradeBuildingId = m_SelectedBinding != null
                    ? m_SelectedBinding.UpgradeBuildingId
                    : BuildingDataModel.GetUpgradeID(current.Identifier);
                BuildingData capturedUpgradeBuildingData = !string.IsNullOrWhiteSpace(capturedUpgradeBuildingId)
                    ? BuildingDataModel.GetBuildingData(capturedUpgradeBuildingId)
                    : null;
                int capturedFaction = m_TargetBuilding.OwnerFactionID;
                bool satisfied = SatisfyBaseLevelCondition(capturedUpgradeBuildingData, capturedFaction);

                SpawnCondition(text, satisfied);
            }
        }

        // 条件2：全局唯一科技
        if (m_SelectedBinding.TechData != null && !m_SelectedBinding.TechData.IsStackable)
        {
            string text = LocalizationTextDataModel.GetText(ConditionUniqueTechTextId);
            bool satisfied = !InGameDataModel.HasUnlockedTech(m_SelectedBinding.TechId);
            SpawnCondition(text, satisfied);
        }

    }

    private void SpawnCondition(string text, bool satisfied)
    {
        GoalConditionItem item = SpawnItem<UIItemObject>(varGoalConditionItem, varConditionArea).itemLogic as GoalConditionItem;
        if (item == null)
            return;

        item.SetText(text);
        item.SetState(
            satisfied,
            iconActiveColor: m_ConditionIconSatisfiedColor,
            iconInactiveColor: Color.white,
            textSatisfiedColor: Color.white,
            textUnsatisfiedColor: Color.red);
    }

    private void PopulateCurrentProperties(BuildingInfoItem item, BuildingEntity building)
    {
        if (item == null || building == null || building.buildingData == null)
            return;

        Transform root = item.PropertyListRoot != null ? item.PropertyListRoot.transform : null;
        if (root == null)
            return;

        switch (building.buildingData.Type)
        {
            case BuilType.Base:
                SpawnProperty(root, SupplyIconPath, $"+{building.buildingData.Lv * 10}");
                break;
            case BuilType.Tech:
                break;
            case BuilType.Prod:
                SpawnProperty(root, CoinIconPath, FormatSigned(building.GetProduction()));
                break;
            case BuilType.Army:
                SpawnProperty(root, SupplyIconPath, building.GetArmySupplyPerUnit().ToString());
                SpawnProperty(root, ForceIconPath, building.GetArmyForce().ToString());
                break;
        }
    }

    private void PopulatePrice(BuildingInfoItem item, int cost)
    {
        if (item == null || m_IconNumTemplate == null)
            return;

        Transform root = item.PriceRoot != null ? item.PriceRoot.transform : null;
        if (root == null)
            return;

        IconNumItem iconNum = SpawnItem<UIItemObject>(m_IconNumTemplate, root).itemLogic as IconNumItem;
        if (iconNum == null)
            return;

        iconNum.SetData(CoinIconPath, cost.ToString());
        bool hasEnoughMoney = InGameDataModel.GetValue(IngameValueType.Coin) >= cost;
        UpdatePriceNumberColor(item, hasEnoughMoney);
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

        iconNum.SetData(CoinIconPath, $"{CoinReservesPrefix}{reserves}");
    }

    private void SpawnProgressStars(UpgradeOptionBinding binding, int cost)
    {
        if (binding == null || binding.PreviewItem == null || m_StarTemplate == null)
            return;

        Transform root = binding.PreviewItem.ProgressRoot != null ? binding.PreviewItem.ProgressRoot.transform : null;
        if (root == null)
            return;

        binding.Stars.Clear();
        int starCount = Mathf.Max(1, cost);
        for (int i = 0; i < starCount; i++)
        {
            StarItem star = SpawnItem<UIItemObject>(m_StarTemplate, root).itemLogic as StarItem;
            if (star == null)
                continue;

            star.SetHighlight(false);
            binding.Stars.Add(star);
        }
    }

    private void UpdateButtonInput()
    {
        if (m_UpgradeBindings.Count <= 0)
            return;

        for (int i = 0; i < m_UpgradeBindings.Count; i++)
        {
            UpgradeOptionBinding binding = m_UpgradeBindings[i];
            if (binding == null || string.IsNullOrWhiteSpace(binding.ActionName))
                continue;

            if (!WasActionPressedThisFrame(binding.ActionName))
                continue;

            SelectOption(i, true);
            break;
        }
    }

    private void UpdateUpgradeHoldProgress()
    {
        if (m_HoldBinding == null || m_RecycleLogicHoldActive)
        {
            ApplyStarHighlight(m_HoldBinding, 0);
            return;
        }

        int starCount = Mathf.Max(1, m_HoldBinding.Stars.Count);
        Fix64 logicProgress = LogicInteractionHoldService.IsActive
            ? LogicInteractionHoldService.GetPanelProgress()
            : Fix64.Zero;
        m_HoldProgressStars = (float)(logicProgress * starCount);

        int highlightCount;
        if (logicProgress > Fix64.Zero)
        {
            if (m_HoldProgressStars <= 1e-4f)
            {
                highlightCount = 0;
            }
            else if (starCount <= 1)
            {
                highlightCount = 1;
            }
            else
            {
                float interval = starCount / (starCount - 1f);
                int extra = Mathf.FloorToInt((m_HoldProgressStars + 1e-4f) / interval);
                highlightCount = Mathf.Clamp(1 + extra, 1, starCount);
            }
        }
        else
        {
            highlightCount = Mathf.Clamp(Mathf.FloorToInt(m_HoldProgressStars + 1e-4f), 0, starCount);
        }

        ApplyStarHighlight(m_HoldBinding, highlightCount);
        UpdateCoinPreviewDeduction(highlightCount);

        // 每点亮一颗新星 → 播 goldPay（按住进度条扣钱的"叮"声）
        if (highlightCount > m_LastHighlightStars && AudioManager.Instance != null)
            AudioManager.Instance.Play("goldPay");
        m_LastHighlightStars = highlightCount;
    }

    private LogicInputButton? ResolveLogicHoldButton(LogicInputFrame frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));

        if (m_HoldBinding != null
            && TryResolveHeldButton(frame, m_HoldBinding, out LogicInputButton activeButton))
        {
            return activeButton;
        }
        if (m_RecycleLogicHoldActive
            && TryResolvePointerButton(frame, varRecycleBtn != null ? varRecycleBtn.transform as RectTransform : null))
        {
            return LogicInputButton.PlayerAttack;
        }

        ApplyStarHighlight(m_HoldBinding, 0);
        m_HoldBinding = null;
        m_RecycleLogicHoldActive = false;
        m_LastHighlightStars = 0;

        if (m_SelectedBinding != null
            && m_SelectedBinding.PreviewItem != null
            && IsSelectedOptionExecutable()
            && TryResolveHeldButton(frame, m_SelectedBinding, out LogicInputButton selectedButton))
        {
            m_HoldBinding = m_SelectedBinding;
            return selectedButton;
        }

        if (CanShowRecycle()
            && TryResolvePointerButton(frame, varRecycleBtn != null ? varRecycleBtn.transform as RectTransform : null))
        {
            m_RecycleLogicHoldActive = true;
            return LogicInputButton.PlayerAttack;
        }

        return null;
    }

    private bool TryResolveHeldButton(
        LogicInputFrame frame,
        UpgradeOptionBinding binding,
        out LogicInputButton button)
    {
        if (TryMapBuildAction(binding.ActionName, out button) && frame.IsHeld(button))
            return true;

        button = LogicInputButton.PlayerAttack;
        if (!frame.IsHeld(button))
            return false;
        if (IsScreenPointInside(binding.PreviewItem.HoldRoot, frame.SelectScreenPosition))
            return true;

        return binding.ButtonItem != null
               && IsScreenPointInside(binding.ButtonItem.HoldRoot, frame.SelectScreenPosition);
    }

    private int ResolveLogicHoldThresholdFrames()
    {
        if (m_RecycleLogicHoldActive)
            return 60;
        if (m_HoldBinding == null)
            throw new InvalidOperationException("Upgrade hold threshold requested without an active binding.");

        return ResolveHoldThresholdFrames(Mathf.Max(1, m_HoldBinding.Stars.Count));
    }

    private bool ExecuteLogicHold()
    {
        return m_RecycleLogicHoldActive
            ? TryRecycleBuilding()
            : TryExecuteSelectedUpgrade();
    }

    private bool TryExecuteSelectedUpgrade()
    {
        if (m_SelectedBinding == null || m_TargetBuilding == null)
            return false;

        bool success;
        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            throw new InvalidOperationException("TechManager is unavailable while completing a logic upgrade hold.");

        if (m_TargetBuilding.buildingData != null && m_TargetBuilding.buildingData.Type == BuilType.Tech)
            success = techManager.ResearchTech(m_TargetBuilding, m_SelectedBinding.TechId);
        else
            success = techManager.UpgradeBuilding(m_TargetBuilding, m_SelectedBinding.UpgradeBuildingId, m_SelectedBinding.TechId);

        if (success)
            ClearCoinPreviewDeduction();
        return success;
    }

    private void UpdatePanelPosition()
    {
        if (m_TargetHost == null || varUpgradePanel == null)
            return;

        RectTransform parent = varUpgradePanel.parent as RectTransform;
        if (parent == null)
            return;

        Vector3 uiPos = GF.UI.PositionWorldToUI(m_TargetHost.GetPromptPosition(), parent);
        varUpgradePanel.anchoredPosition = (Vector2)uiPos + uiOffset;
    }

    private void ComposeCurrentInfoText(BuildingEntity building, out string name, out string desc)
    {
        name = string.Empty;
        desc = string.Empty;

        if (building == null || building.buildingData == null)
            return;

        name = LocalizationTextManager.GetLocalizedText(building.buildingData.NameKey, false);
        desc = building.buildingData.GetFormattedDesc();

        List<SelectedUpgradeInfo> selected = CollectSelectedUpgrades(building);
        if (selected.Count <= 0)
            return;

        StringBuilder marks = new();
        for (int i = 0; i < selected.Count; i++)
            marks.Append(GetOptionMark(selected[i].OptionIndex));

        name += "-" + marks;

        for (int i = 0; i < selected.Count; i++)
        {
            string optionDesc = selected[i].TechData != null ? selected[i].TechData.GetFormattedDesc() : string.Empty;
            if (string.IsNullOrWhiteSpace(optionDesc))
                continue;

            desc += "\n·" + optionDesc;
        }
    }

    private string ComposePreviewName(BuildingEntity building, UpgradeOptionBinding selected)
    {
        if (building == null || building.buildingData == null || selected == null)
            return string.Empty;

        string baseName = LocalizationTextManager.GetLocalizedText(building.buildingData.NameKey, false);
        List<SelectedUpgradeInfo> history = CollectSelectedUpgrades(building);
        history.Add(new SelectedUpgradeInfo { OptionIndex = selected.OptionIndex, TechData = selected.TechData });

        if (history.Count <= 0)
            return baseName;

        StringBuilder marks = new();
        for (int i = 0; i < history.Count; i++)
            marks.Append(GetOptionMark(history[i].OptionIndex));

        return baseName + "-" + marks;
    }

    private bool CanShowUpgradeTips()
    {
        if (m_TargetHost == null || m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return false;

        BuildingData data = m_TargetBuilding.buildingData;
        if (data.Lv <= 0)
            return false;

        if (data.Type != BuilType.Tech)
        {
            string upgradeBuildingId = BuildingDataModel.GetUpgradeID(data.Identifier);
            if (string.IsNullOrWhiteSpace(upgradeBuildingId) || BuildingDataModel.GetBuildingData(upgradeBuildingId) == null)
                return false;
        }

        if (data.UpgradeTechIDs == null || data.UpgradeTechIDs.Length <= 0)
            return false;

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            return false;

        for (int i = 0; i < data.UpgradeTechIDs.Length; i++)
        {
            string techId = data.UpgradeTechIDs[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            bool visible = data.Type == BuilType.Tech
                ? techManager.IsResearchOptionVisible(m_TargetBuilding, techId)
                : techManager.IsUpgradeOptionVisible(m_TargetBuilding, BuildingDataModel.GetUpgradeID(data.Identifier), techId);
            if (visible)
                return true;
        }

        return false;
    }

    private int ResolveOptionCost(UpgradeOptionBinding binding)
    {
        if (binding == null)
            return 0;

        if (m_TargetBuilding != null && m_TargetBuilding.buildingData != null && m_TargetBuilding.buildingData.Type == BuilType.Tech)
            return binding.TechData != null ? Mathf.Max(0, binding.TechData.Cost) : 0;

        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        return buildManager != null
            ? buildManager.GetBuildingCost(binding.UpgradeBuildingData, m_TargetBuilding)
            : (binding.UpgradeBuildingData != null ? Mathf.Max(0, binding.UpgradeBuildingData.Cost) : 0);
    }

    private bool IsSelectedOptionExecutable()
    {
        if (m_SelectedBinding == null || m_TargetBuilding == null)
            return false;

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            return false;

        if (m_TargetBuilding.buildingData != null && m_TargetBuilding.buildingData.Type == BuilType.Tech)
            return techManager.IsResearchOptionExecutable(m_TargetBuilding, m_SelectedBinding.TechId);

        return techManager.IsUpgradeOptionExecutable(m_TargetBuilding, m_SelectedBinding.UpgradeBuildingId, m_SelectedBinding.TechId);
    }

    private bool IsOptionExecutable(UpgradeOptionBinding binding)
    {
        if (binding == null || m_TargetBuilding == null)
            return false;

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            return false;

        if (m_TargetBuilding.buildingData != null && m_TargetBuilding.buildingData.Type == BuilType.Tech)
            return techManager.IsResearchOptionExecutable(m_TargetBuilding, binding.TechId);

        return techManager.IsUpgradeOptionExecutable(m_TargetBuilding, binding.UpgradeBuildingId, binding.TechId);
    }

    private bool SatisfyBaseLevelCondition(BuildingData upgradeBuildingData, int ownerFactionId)
    {
        if (upgradeBuildingData == null)
            return false;

        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        return buildManager != null && buildManager.SatisfyBuildCondition(upgradeBuildingData, ownerFactionId);
    }

    private string ResolveArchetypeBaseName(Archetype archetype)
    {
        if (archetype == Archetype.None)
            return string.Empty;

        foreach (BuildingData data in BuildingDataModel.GetAllBuildingData())
        {
            if (data == null || data.Type != BuilType.Base || data.Arche != archetype || data.Lv != 1)
                continue;

            return LocalizationTextManager.GetLocalizedText(data.NameKey, false);
        }

        return archetype.ToString();
    }

    private static List<SelectedUpgradeInfo> CollectSelectedUpgrades(BuildingEntity building)
    {
        List<SelectedUpgradeInfo> results = new();
        if (building == null || building.buildingData == null || string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            return results;

        BuildingData current = building.buildingData;
        if (current.Type == BuilType.Tech)
        {
            AppendSelectedUpgrade(current.UpgradeTechIDs, building.BuildingInstanceId, results);
            return results;
        }

        for (int lv = 1; lv < current.Lv; lv++)
        {
            string levelId = ReplaceLevel(current.Identifier, lv);
            if (string.IsNullOrWhiteSpace(levelId))
                continue;

            BuildingData dataAtLevel = BuildingDataModel.GetBuildingData(levelId);
            if (dataAtLevel == null)
                continue;

            AppendSelectedUpgrade(dataAtLevel.UpgradeTechIDs, building.BuildingInstanceId, results);
        }

        return results;
    }

    private sealed class SelectedUpgradeInfo
    {
        public int OptionIndex;
        public TechData TechData;
    }

    private static void AppendSelectedUpgrade(string[] optionTechIds, string buildingInstanceId, List<SelectedUpgradeInfo> results)
    {
        if (optionTechIds == null || optionTechIds.Length == 0 || string.IsNullOrWhiteSpace(buildingInstanceId))
            return;

        for (int i = 0; i < optionTechIds.Length; i++)
        {
            string techId = optionTechIds[i];
            if (string.IsNullOrWhiteSpace(techId))
                continue;

            if (!InGameDataModel.HasUnlockedTech(techId, buildingInstanceId))
                continue;

            TechData data = TechDataModel.GetTechData(techId);
            if (data == null)
                continue;

            results.Add(new SelectedUpgradeInfo { OptionIndex = i, TechData = data });
            return;
        }
    }

    private static string ReplaceLevel(string identifier, int lv)
    {
        if (string.IsNullOrWhiteSpace(identifier) || lv <= 0)
            return null;

        int idx = identifier.LastIndexOf("_Lv", StringComparison.Ordinal);
        if (idx < 0)
            return null;

        return identifier.Substring(0, idx + 3) + lv;
    }

    private string GetSelectionMemoryKey()
    {
        if (m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return string.Empty;

        return m_TargetBuilding.buildingData.Identifier;
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

    private static int ResolveHoldThresholdFrames(int starCount)
    {
        if (starCount <= 1)
            return 3;

        int gaps = starCount - 1;
        if (gaps < 5)
            return Mathf.Max(30, 12 * gaps);
        if (gaps <= 20)
            return 60;
        return checked(3 * gaps);
    }

    private static bool TryMapBuildAction(string actionName, out LogicInputButton button)
    {
        switch (actionName)
        {
            case "Player/Build1":
                button = LogicInputButton.Build1;
                return true;
            case "Player/Build2":
                button = LogicInputButton.Build2;
                return true;
            case "Player/Build3":
                button = LogicInputButton.Build3;
                return true;
            default:
                button = default;
                return false;
        }
    }

    private bool WasActionPressedThisFrame(string actionName)
    {
        InputManager inputManager = EnsureInputManager();
        return inputManager != null && inputManager.WasActionPressedThisFrame(actionName);
    }

    private InputManager EnsureInputManager()
    {
        if (m_InputManager == null)
            m_InputManager = GameEntry.GetComponent<InputManager>();
        return m_InputManager;
    }

    private static bool TryResolvePointerButton(LogicInputFrame frame, RectTransform itemRect)
    {
        return frame.IsHeld(LogicInputButton.PlayerAttack)
               && IsScreenPointInside(itemRect, frame.SelectScreenPosition);
    }

    private static bool IsScreenPointInside(RectTransform itemRect, FixVector2 screenPosition)
    {
        if (itemRect == null)
            return false;

        Canvas canvas = itemRect.GetComponentInParent<Canvas>();
        Camera uiCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = canvas.worldCamera != null ? canvas.worldCamera : GF.UICamera;

        return RectTransformUtility.RectangleContainsScreenPoint(
            itemRect,
            new Vector2((float)screenPosition.x, (float)screenPosition.y),
            uiCamera);
    }

    private void ApplyStarHighlight(UpgradeOptionBinding binding, int highlightCount)
    {
        if (binding == null || binding.Stars == null)
            return;

        SortStarsByVisualOrder(binding.Stars);
        for (int i = 0; i < binding.Stars.Count; i++)
        {
            StarItem star = binding.Stars[i];
            if (star == null)
                continue;

            star.SetHighlight(i < highlightCount);
        }
    }

    private static void SortStarsByVisualOrder(List<StarItem> stars)
    {
        if (stars == null || stars.Count <= 1)
            return;

        stars.Sort((left, right) =>
        {
            if (left == right)
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            RectTransform leftRect = left.transform as RectTransform;
            RectTransform rightRect = right.transform as RectTransform;
            float leftX = leftRect != null ? leftRect.anchoredPosition.x : left.transform.localPosition.x;
            float rightX = rightRect != null ? rightRect.anchoredPosition.x : right.transform.localPosition.x;
            return leftX.CompareTo(rightX);
        });
    }

    private void ResetHoldState()
    {
        ClearCoinPreviewDeduction();
        m_HoldBinding = null;
        m_HoldProgressStars = 0f;
        m_LastHighlightStars = 0;
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
            varRecycleText.text = string.Format(RecycleTextFormat, ResolveRecycleRefund());

        if (varRecycleFill != null)
            varRecycleFill.fillAmount = Mathf.Clamp01(m_RecycleHoldProgress);
    }

    private void UpdateRecycleHoldProgress()
    {
        if (!CanShowRecycle() || !m_RecycleLogicHoldActive)
        {
            m_RecycleHoldProgress = 0f;
            if (varRecycleFill != null)
                varRecycleFill.fillAmount = 0f;
            return;
        }

        Fix64 progress = LogicInteractionHoldService.IsActive
            ? LogicInteractionHoldService.GetPanelProgress()
            : Fix64.Zero;
        m_RecycleHoldProgress = (float)progress;

        if (varRecycleFill != null)
            varRecycleFill.fillAmount = m_RecycleHoldProgress;
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
        m_RecycleLogicHoldActive = false;
        if (varRecycleFill != null)
            varRecycleFill.fillAmount = 0f;
    }

    private void OnStateChanged(object sender, GameEventArgs e)
    {
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
        if (varBuildingInfoItem == null)
            return;

        if (m_ItemTemplate == null || m_ItemTemplate.gameObject != varBuildingInfoItem)
            m_ItemTemplate = varBuildingInfoItem.GetComponent<BuildingInfoItem>();

        if (m_ItemTemplate == null)
            return;

        m_IconNumTemplate = m_ItemTemplate.IconNumTemplate;
        m_StarTemplate = m_ItemTemplate.StarTemplate;

        if (m_StarTemplate != null)
        {
            StarItem starTemplate = m_StarTemplate.GetComponent<StarItem>();
            if (starTemplate != null)
                m_ConditionIconSatisfiedColor = starTemplate.HighlightColor;
        }
    }

    private void ClearAllSpawnedItems()
    {
        UnspawnItemTemplate(m_IconNumTemplate);
        UnspawnItemTemplate(m_StarTemplate);
        UnspawnItemTemplate(varBuildingInfoItem);
        UnspawnItemTemplate(varUpgradeButtonItem);
        UnspawnItemTemplate(varGoalConditionItem);
    }

    private void ClearRuntimeState()
    {
        m_UpgradeBindings.Clear();
        m_SelectedBinding = null;
        ResetHoldState();
        ResetRecycleHoldState();
    }

    private void UpdateExecutableStates()
    {
        for (int i = 0; i < m_UpgradeBindings.Count; i++)
        {
            UpgradeOptionBinding option = m_UpgradeBindings[i];
            if (option?.ButtonItem == null)
                continue;

            option.ButtonItem.SetExecutable(IsOptionExecutable(option));
        }

        if (m_SelectedBinding == null || m_SelectedBinding.PreviewItem == null)
            return;

        bool executable = IsSelectedOptionExecutable();
        m_SelectedBinding.PreviewItem.SetExecutable(executable);

        int cost = ResolveOptionCost(m_SelectedBinding);
        bool hasEnoughMoney = InGameDataModel.GetValue(IngameValueType.Coin) >= cost;
        UpdatePriceNumberColor(m_SelectedBinding.PreviewItem, hasEnoughMoney);
    }

    private static void UpdatePriceNumberColor(BuildingInfoItem item, bool hasEnoughMoney)
    {
        if (item == null || item.PriceRoot == null)
            return;

        IconNumItem iconNum = item.PriceRoot.GetComponentInChildren<IconNumItem>(true);
        if (iconNum == null)
            return;

        if (hasEnoughMoney)
            iconNum.ResetNumberColor();
        else
            iconNum.SetNumberColor(Color.red);
    }

    private void UnspawnItemTemplate(GameObject template)
    {
        if (template == null)
            return;

        UnspawnAllItem<UIItemObject>(template);
    }

    private static BuildingEntity ResolveTargetBuilding(InteractionHost targetHost)
    {
        if (targetHost == null)
            return null;

        if (targetHost.Owner is BuildingEntity owner)
            return owner;

        return targetHost.GetComponent<BuildingEntity>();
    }

    private void UpdateCoinPreviewDeduction(int highlightCount)
    {
        IngameCoinPreviewState.SetPreviewDeduction(GetInstanceID(), Mathf.Max(0, highlightCount));
    }

    private void ClearCoinPreviewDeduction()
    {
        IngameCoinPreviewState.ClearPreviewDeduction(GetInstanceID());
    }
}
