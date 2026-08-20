using UnityGameFramework.Runtime;
using System.Collections.Generic;
using System;
using System.Text;
using GameFramework.Event;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class BuildingUpgradeTips : UIFormBase
{
    public const string P_TargetHost = "TargetHost";

    [SerializeField] private Vector2 uiOffset = new(0f, 80f);

    private const string CoinIconPath = "UI/Icon/Coin.png";
    private const string ForceIconPath = "UI/Icon/Force.png";
    private const string SupplyIconPath = "UI/Icon/Supply.png";

    private const string ConditionBaseLevelTextId = "Building_Upgrade_Cond_BaseLevel";
    private const string ConditionUniqueTechTextId = "Building_Upgrade_Cond_UniqueTech";
    private const string ObtainSkillTextId = "Building_Skill_Obtain_Format";
    private const string UpgradeSkillTextId = "Building_Skill_Upgrade_Format";
    private const float RecycleHoldDurationSeconds = 2f;
    private const float MinimumInfoHeight = 176f;
    private const float HeaderHeight = 72f;
    private const float BottomPadding = 24f;
    private const float ConditionGap = 12f;
    private const float OptionGap = 8f;
    private const float SeparationInset = 4f;
    private const float SeparationBandHeight = 24f;
    private const string DemolishText = "拆除";
    private const string UndoTextFormat = "撤销  <sprite name=\"Coin\"> {0}";
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
    private TextMeshProUGUI m_CurrentLevelTitleText;
    private TextMeshProUGUI m_NextLevelTitleText;
    private RectTransform m_Background;
    private RectTransform m_LevelTitleBadge;
    private RectTransform m_CurrentPreviewFrame;
    private RectTransform m_UpgradePreviewFrame;
    private RectTransform m_CurrentInfoBottomLine;
    private RectTransform m_UpgradeInfoTopLine;
    private RectTransform m_UpgradeInfoBottomLine;
    private float m_CurrentInfoHeight = MinimumInfoHeight;
    private float m_UpgradeInfoHeight = MinimumInfoHeight;
    private float m_StableConditionHeight;
    private bool m_ReserveDetailPanelSpace;
    private bool m_IsWallMerge;
    private BuildingData m_WallCurrentData;
    private BuildingData m_WallMergedData;

    private UpgradeOptionBinding m_SelectedBinding;
    private UpgradeOptionBinding m_HoldBinding;
    private float m_HoldProgressStars;
    private int m_LastHighlightStars;
    private bool m_HoldTriggered;
    private float m_RecycleHoldProgress;
    private bool m_RecycleTriggered;
    private bool m_IsAwaitingTargetTransition;
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
        m_IsAwaitingTargetTransition = false;
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
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnStateChanged);
        GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnStateChanged);
        GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        ClearCoinPreviewDeduction();
        ClearRuntimeState();

        base.OnClose(isShutdown, userData);
    }

    private void Update()
    {
        if (m_IsAwaitingTargetTransition)
            return;

        UpdatePanelPosition();
        UpdateButtonInput();
        UpdateUpgradeHoldProgress();
        UpdateRecycleHoldProgress();
        UpdateExecutableStates();
    }

    private void RefreshView()
    {
        CacheTemplates();
        ResolveWallMergeMode();
        RefreshLevelTitle();
        ClearAllSpawnedItems();
        ClearRuntimeState();
        RefreshRecycleArea();

        if (!CanShowUpgradeTips())
            return;

        SpawnCurrentInfoItem();
        BuildUpgradeCandidates();
        PrepareStableOptionLayout();
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

        if (m_IsWallMerge)
        {
            string wallName = BuildingPanelPresentation.GetBuildingName(m_WallCurrentData);
            string wallDesc = BuildingPanelPresentation.GetDescription(m_WallCurrentData);
            item.SetData(string.Empty, wallName, wallDesc);
            item.SetPreviewVisible(false);
            item.SetPriceVisible(false);
            item.SetProgressVisible(false);
            PopulateWallProperties(item, m_WallCurrentData);
            m_CurrentInfoHeight = item.ApplyPanelLayout(reservePreviewColumn: true, reserveProgressRow: true);
            return;
        }

        ComposeCurrentInfoText(m_TargetBuilding, out string name, out string desc);
        item.SetData(string.Empty, name, desc);
        item.SetPreviewVisible(false);
        item.SetPriceVisible(false);
        item.SetProgressVisible(false);

        PopulateCurrentProperties(item, m_TargetBuilding);
        PopulateCoinReserves(item, m_TargetBuilding);
        m_CurrentInfoHeight = item.ApplyPanelLayout(reservePreviewColumn: true, reserveProgressRow: true);
    }

    private void BuildUpgradeCandidates()
    {
        if (m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return;

        if (m_IsWallMerge)
        {
            m_UpgradeBindings.Add(new UpgradeOptionBinding
            {
                OptionIndex = 0,
                ActionName = "Player/Build1",
                UpgradeBuildingId = LogicWallRuntime.BuiltBuildingId,
                UpgradeBuildingData = m_WallMergedData,
            });
            return;
        }

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            return;

        BuildingData current = m_TargetBuilding.buildingData;
        string[] optionIds = current.UpgradeTechIDs;
        if (optionIds == null)
            return;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(current.Identifier);
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

            bool visible = techManager.IsUpgradeOptionVisible(m_TargetBuilding, upgradeBuildingId, techId);
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

        int remembered = 0;
        string rememberKey = GetSelectionMemoryKey();
        if (!string.IsNullOrWhiteSpace(rememberKey)
            && s_LastSelectedOptionByBuildingLevel.TryGetValue(rememberKey, out int last)
            && last >= 0
            && last < m_UpgradeBindings.Count)
        {
            remembered = last;
        }

        bool[] heldStates = new bool[m_UpgradeBindings.Count];
        for (int i = 0; i < m_UpgradeBindings.Count; i++)
            heldStates[i] = IsActionPressed(m_UpgradeBindings[i].ActionName);

        int selected = ResolveDefaultOptionIndex(remembered, heldStates);
        SelectOption(selected, false);
    }

    private static int ResolveDefaultOptionIndex(int rememberedIndex, IReadOnlyList<bool> heldStates)
    {
        if (heldStates == null || heldStates.Count <= 0)
            throw new ArgumentException("Upgrade option hold states must not be empty.", nameof(heldStates));
        if (rememberedIndex < 0 || rememberedIndex >= heldStates.Count)
            throw new ArgumentOutOfRangeException(nameof(rememberedIndex), rememberedIndex, "Remembered upgrade option is outside the available option range.");

        int heldIndex = -1;
        for (int i = 0; i < heldStates.Count; i++)
        {
            if (!heldStates[i])
                continue;
            if (heldIndex >= 0)
                return rememberedIndex;

            heldIndex = i;
        }

        return heldIndex >= 0 ? heldIndex : rememberedIndex;
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

        if (m_IsWallMerge)
        {
            string wallKeyText = InputGetKeyText.GetKeyText(m_SelectedBinding.ActionName);
            preview.SetData(
                wallKeyText,
                BuildingPanelPresentation.GetBuildingName(m_WallMergedData),
                BuildingPanelPresentation.GetDescription(m_WallMergedData));
            preview.SetPreviewVisible(false);
            preview.SetExecutable(IsSelectedOptionExecutable());
            preview.SetCoinReservesVisible(false);
            int wallCost = ResolveOptionCost(m_SelectedBinding);
            PopulatePrice(preview, wallCost);
            PopulateWallProperties(preview, m_WallMergedData);
            SpawnProgressStars(m_SelectedBinding, Mathf.Max(1, wallCost));
            m_UpgradeInfoHeight = preview.ApplyPanelLayout(
                reservePreviewColumn: true,
                reserveProgressRow: true);
            return;
        }

        string keyText = InputGetKeyText.GetKeyText(m_SelectedBinding.ActionName);
        string name = ComposePreviewName(m_TargetBuilding, m_SelectedBinding);
        BuildingData previewData = m_SelectedBinding.UpgradeBuildingData
                                   ?? throw new InvalidOperationException(
                                       $"Upgrade preview is missing building data. building={m_SelectedBinding.UpgradeBuildingId}.");
        List<SelectedUpgradeInfo> previewUpgrades = CollectSelectedUpgrades(m_TargetBuilding);
        previewUpgrades.Add(new SelectedUpgradeInfo
        {
            OptionIndex = m_SelectedBinding.OptionIndex,
            TechData = m_SelectedBinding.TechData,
        });
        string desc = ComposeDescription(previewData, previewUpgrades, m_SelectedBinding.TechData);
        preview.SetData(keyText, name, desc);
        preview.SetPreviewVisible(false);
        preview.SetExecutable(IsSelectedOptionExecutable());
        preview.SetCoinReservesVisible(false);

        int cost = ResolveOptionCost(m_SelectedBinding);
        PopulatePrice(preview, cost);

        PopulatePreviewProperties(preview, previewData);

        SpawnProgressStars(m_SelectedBinding, Mathf.Max(1, cost));
        float stableInfoHeight = MeasureStableUpgradeInfoHeight(preview, previewData);
        m_UpgradeInfoHeight = preview.ApplyPanelLayout(
            reservePreviewColumn: true,
            reserveProgressRow: true,
            minimumPanelHeight: stableInfoHeight);
    }

    private void RefreshConditionArea()
    {
        UnspawnItemTemplate(varGoalConditionItem);

        if (m_SelectedBinding == null || varConditionArea == null || varGoalConditionItem == null)
            return;

        SpawnConditions(m_SelectedBinding);
        ApplyAdaptiveLayout();
    }

    private void SpawnConditions(UpgradeOptionBinding binding)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (m_IsWallMerge)
            return;

        // 条件1：基地等级条件（非 Base/Tech 建筑升级到 n 级）
        if (m_TargetBuilding != null && m_TargetBuilding.buildingData != null)
        {
            BuildingData current = m_TargetBuilding.buildingData;
            if (current.Type != BuilType.Base)
            {
                int requiredLevel = current.Lv + 1;
                string baseName = ResolveArchetypeBaseName(current.Arche);
                string format = LocalizationTextDataModel.GetText(ConditionBaseLevelTextId);
                string text = string.Format(format, baseName, requiredLevel);
                string capturedUpgradeBuildingId = binding.UpgradeBuildingId;
                BuildingData capturedUpgradeBuildingData = !string.IsNullOrWhiteSpace(capturedUpgradeBuildingId)
                    ? BuildingDataModel.GetBuildingData(capturedUpgradeBuildingId)
                    : null;
                int capturedFaction = m_TargetBuilding.OwnerFactionID;
                bool satisfied = SatisfyBaseLevelCondition(capturedUpgradeBuildingData, capturedFaction);

                SpawnCondition(text, satisfied);
            }
        }

        // 条件2：全局唯一科技
        if (binding.TechData != null && !binding.TechData.IsStackable)
        {
            string text = LocalizationTextDataModel.GetText(ConditionUniqueTechTextId);
            bool satisfied = !InGameDataModel.HasUnlockedTech(binding.TechId);
            SpawnCondition(text, satisfied);
        }
    }

    private void PrepareStableOptionLayout()
    {
        m_ReserveDetailPanelSpace = m_TargetBuilding?.buildingData?.Type == BuilType.Army;
        m_StableConditionHeight = 0f;

        for (int i = 0; i < m_UpgradeBindings.Count; i++)
        {
            UpgradeOptionBinding binding = m_UpgradeBindings[i]
                                           ?? throw new InvalidOperationException($"Upgrade option {i} is missing its binding.");
            if (binding.TechData?.ScopeType == TechScopeType.Skill)
                m_ReserveDetailPanelSpace = true;

            UnspawnItemTemplate(varGoalConditionItem);
            SpawnConditions(binding);
            SetTopCenteredRect(varConditionArea, Vector2.zero, new Vector2(600f, 0f));
            LayoutRebuilder.ForceRebuildLayoutImmediate(varConditionArea);
            float height = Mathf.Max(0f, LayoutUtility.GetPreferredHeight(varConditionArea));
            if (HasActiveConditionItem(varConditionArea) && height <= 0f)
                throw new InvalidOperationException($"Upgrade option {i} conditions have content but no preferred height.");
            m_StableConditionHeight = Mathf.Max(m_StableConditionHeight, height);
        }

        UnspawnItemTemplate(varGoalConditionItem);
    }

    private float MeasureStableUpgradeInfoHeight(BuildingInfoItem preview, BuildingData previewData)
    {
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));
        if (previewData == null)
            throw new ArgumentNullException(nameof(previewData));

        float height = MinimumInfoHeight;
        List<SelectedUpgradeInfo> history = CollectSelectedUpgrades(m_TargetBuilding);
        for (int i = 0; i < m_UpgradeBindings.Count; i++)
        {
            UpgradeOptionBinding binding = m_UpgradeBindings[i]
                                           ?? throw new InvalidOperationException($"Upgrade option {i} is missing its binding.");
            var selected = new List<SelectedUpgradeInfo>(history)
            {
                new SelectedUpgradeInfo
                {
                    OptionIndex = binding.OptionIndex,
                    TechData = binding.TechData,
                },
            };
            string description = ComposeDescription(previewData, selected, binding.TechData);
            height = Mathf.Max(
                height,
                preview.MeasurePanelHeight(
                    description,
                    reservePreviewColumn: true,
                    reserveProgressRow: true));
        }

        return height;
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

        PopulateDetailedStats(item, building.buildingData, building);
    }

    private void PopulatePreviewProperties(BuildingInfoItem item, BuildingData data)
    {
        if (item?.PropertyListRoot == null || data == null)
            throw new InvalidOperationException("Upgrade preview property list is unavailable.");

        Transform root = item.PropertyListRoot.transform;
        switch (data.Type)
        {
            case BuilType.Base:
                SpawnProperty(root, SupplyIconPath, BuildingPanelPresentation.GetBaseSupplyText(data));
                break;
            case BuilType.Prod:
                SpawnProperty(root, CoinIconPath, BuildingPanelPresentation.GetProductionText(data, null));
                break;
            case BuilType.Army:
                SpawnProperty(root, SupplyIconPath, BuildingPanelPresentation.GetArmySupplyText(data, null));
                SpawnProperty(root, ForceIconPath, BuildingPanelPresentation.GetArmyForceText(data, null));
                break;
        }

        PopulateDetailedStats(item, data, null);
    }

    private void PopulateDetailedStats(BuildingInfoItem item, BuildingData data, BuildingEntity runtimeBuilding)
    {
        Transform root = item.PropertyListRoot.transform;
        var stats = new List<BuildingPanelStat>();
        BuildingPanelPresentation.CollectBuildingCombatStats(data, runtimeBuilding, stats);
        for (int i = 0; i < stats.Count; i++)
            SpawnGlyphProperty(root, stats[i]);

        PopulateDetailProperties(item, data, runtimeBuilding);
    }

    private void PopulateDetailProperties(BuildingInfoItem item, BuildingData data, BuildingEntity runtimeBuilding)
    {
        if (data != null && data.Type == BuilType.Army)
        {
            item.SetDetailData(
                BuildingPanelPresentation.GetUnitName(data),
                BuildingPanelPresentation.GetUnitDescription(data));
            PopulateDetailStats(item, data);
            return;
        }

        TechData skillTech = m_SelectedBinding?.TechData?.ScopeType == TechScopeType.Skill
            ? m_SelectedBinding.TechData
            : null;
        if (skillTech == null)
        {
            item.SetDetailInfoVisible(false);
            return;
        }

        int currentLevel = SkillRuntimeDataModel.GetLevel(skillTech.SkillID);
        int level = ResolveSkillDetailLevel(runtimeBuilding != null, currentLevel);
        if (level <= 0)
        {
            item.SetDetailInfoVisible(false);
            return;
        }

        SkillData skill = SkillDataModel.GetSkillData(skillTech.SkillID)
                          ?? throw new InvalidOperationException($"Selected skill tech is missing skill data. skill={skillTech.SkillID}");
        string skillName = LocalizationTextManager.GetLocalizedText(skill.NameKey, false);
        item.SetDetailData(
            BuildingPanelPresentation.FormatSkillName(skillName, level),
            skill.GetFormattedDesc(level));
        Transform root = item.DetailPropertyListRoot.transform;
        var stats = new List<BuildingPanelStat>();
        BuildingPanelPresentation.CollectSkillStats(skill, level, stats);
        for (int i = 0; i < stats.Count; i++)
            SpawnGlyphProperty(root, stats[i]);
    }

    private void PopulateDetailStats(BuildingInfoItem item, BuildingData data)
    {
        Transform root = item.DetailPropertyListRoot.transform;
        var stats = new List<BuildingPanelStat>();
        BuildingPanelPresentation.CollectUnitStats(data, stats);
        for (int i = 0; i < stats.Count; i++)
            SpawnGlyphProperty(root, stats[i]);
    }

    private static int ResolveTargetSkillLevel(TechData tech)
    {
        if (tech == null || tech.ScopeType != TechScopeType.Skill || string.IsNullOrWhiteSpace(tech.SkillID))
            throw new ArgumentException("Target skill level requires a skill tech.", nameof(tech));
        int current = SkillRuntimeDataModel.GetLevel(tech.SkillID);
        return ResolveSkillDetailLevel(false, current);
    }

    internal static int ResolveSkillDetailLevel(bool currentBuilding, int currentLevel)
    {
        if (currentLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(currentLevel), currentLevel, "Skill level cannot be negative.");

        return currentBuilding ? currentLevel : currentLevel + 1;
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
        iconNum.FitParentRect();
        iconNum.AlignContentRight();
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
        if (m_SelectedBinding == null || m_SelectedBinding.PreviewItem == null || !IsSelectedOptionExecutable())
        {
            ResetHoldState();
            return;
        }

        UpgradeOptionBinding pressed = ResolvePressedBinding();
        if (m_HoldBinding == null)
        {
            if (pressed == null)
                return;

            m_HoldBinding = pressed;
            m_HoldProgressStars = 0f;
            m_LastHighlightStars = 0;
            m_HoldTriggered = false;
        }
        else if (pressed != null && pressed != m_HoldBinding)
        {
            ApplyStarHighlight(m_HoldBinding, 0);
            m_HoldBinding = pressed;
            m_HoldProgressStars = 0f;
            m_LastHighlightStars = 0;
            m_HoldTriggered = false;
        }

        if (m_HoldBinding == null)
            return;

        int starCount = Mathf.Max(1, m_HoldBinding.Stars.Count);
        bool pressing = IsBindingPressed(m_HoldBinding);
        m_HoldProgressStars = BuildingInteractionHoldPresentation.AdvanceConfiguredProgressStars(
            m_HoldProgressStars,
            pressing,
            starCount,
            Time.deltaTime);

        int highlightCount;
        if (pressing)
        {
            if (m_HoldProgressStars <= 1e-4f)
            {
                highlightCount = 0;
            }
            else if (starCount <= 1)
            {
                highlightCount = m_HoldProgressStars >= starCount ? 1 : 0;
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

        if (!m_HoldTriggered && pressing && m_HoldProgressStars >= starCount)
        {
            m_HoldTriggered = true;
            TryExecuteSelectedUpgrade();
        }

        if (!pressing && m_HoldProgressStars <= 1e-4f)
            ResetHoldState();
    }

    private UpgradeOptionBinding ResolvePressedBinding()
    {
        UpgradeOptionBinding selected = m_SelectedBinding;
        if (selected == null)
            return null;

        return IsBindingPressed(selected) ? selected : null;
    }

    private bool IsBindingPressed(UpgradeOptionBinding binding)
    {
        if (binding == null || binding.PreviewItem == null)
            return false;

        if (IsActionPressed(binding.ActionName))
            return true;

        if (IsPointerHoldingOnItem(binding.PreviewItem.HoldRoot))
            return true;

        return binding.ButtonItem != null && IsPointerHoldingOnItem(binding.ButtonItem.HoldRoot);
    }

    private void TryExecuteSelectedUpgrade()
    {
        if (m_SelectedBinding == null || m_TargetBuilding == null)
            return;

        if (m_IsWallMerge)
        {
            BuildManager buildManager = GameEntry.GetComponent<BuildManager>()
                                        ?? throw new InvalidOperationException(
                                            "BuildManager is unavailable while completing a wall construction hold.");
            bool wallSuccess = buildManager.ConstructBuilding(
                m_TargetBuilding,
                LogicWallRuntime.BuiltBuildingId);
            if (wallSuccess)
            {
                m_IsAwaitingTargetTransition = true;
                IngameCoinPreviewState.CommitPreviewDeduction(
                    GetInstanceID(),
                    m_TargetBuilding.LogicEntityId,
                    LogicInteractionActionKind.ConstructBuilding);
            }
            else
                RefreshView();
            return;
        }

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            throw new InvalidOperationException("TechManager is unavailable while completing an upgrade hold.");

        bool success = techManager.UpgradeBuilding(
            m_TargetBuilding,
            m_SelectedBinding.UpgradeBuildingId,
            m_SelectedBinding.TechId);

        if (success)
        {
            m_IsAwaitingTargetTransition = true;
            IngameCoinPreviewState.CommitPreviewDeduction(
                GetInstanceID(),
                m_TargetBuilding.LogicEntityId,
                LogicInteractionActionKind.UpgradeBuilding);
        }
        else
            RefreshView();
    }

    private void ApplyAdaptiveLayout()
    {
        CacheLayoutReferences();

        SetTopCenteredRect(varConditionArea, Vector2.zero, new Vector2(600f, 0f));
        LayoutRebuilder.ForceRebuildLayoutImmediate(varConditionArea);
        float activeConditionHeight = Mathf.Max(0f, LayoutUtility.GetPreferredHeight(varConditionArea));
        if (HasActiveConditionItem(varConditionArea) && activeConditionHeight <= 0f)
            throw new InvalidOperationException("Upgrade conditions have content but no preferred height.");
        if (activeConditionHeight > m_StableConditionHeight + 0.01f)
            throw new InvalidOperationException(
                $"Selected upgrade condition height exceeds the stable panel reservation. active={activeConditionHeight}, reserved={m_StableConditionHeight}.");
        float conditionHeight = m_StableConditionHeight;

        float optionHeight = 0f;
        for (int i = 0; i < m_UpgradeBindings.Count; i++)
        {
            RectTransform optionRoot = m_UpgradeBindings[i]?.ButtonItem?.HoldRoot;
            if (optionRoot != null)
                optionHeight = Mathf.Max(optionHeight, optionRoot.rect.height);
        }
        if (optionHeight <= 0f)
            throw new InvalidOperationException("Upgrade option row has no measurable option objects.");

        float totalHeight = HeaderHeight
                            + m_CurrentInfoHeight
                            + SeparationInset
                            + SeparationBandHeight
                            + SeparationInset
                            + m_UpgradeInfoHeight
                            + SeparationInset
                            + ConditionGap
                            + conditionHeight
                            + OptionGap
                            + optionHeight
                            + BottomPadding;
        varUpgradePanel.sizeDelta = new Vector2(varUpgradePanel.sizeDelta.x, totalHeight);
        SetCenteredRect(m_Background, Vector2.zero, new Vector2(m_Background.sizeDelta.x, totalHeight));

        float top = totalHeight * 0.5f;
        SetCenteredPosition(m_LevelTitleBadge, new Vector2(m_LevelTitleBadge.anchoredPosition.x, top - 39f));
        SetCenteredPosition(
            varRecycleBtn.transform as RectTransform,
            new Vector2((varRecycleBtn.transform as RectTransform).anchoredPosition.x, top - 32.24f));

        float currentTop = top - HeaderHeight;
        float currentCenter = currentTop - m_CurrentInfoHeight * 0.5f;
        SetCenteredPosition(varInfoRoot, new Vector2(0f, currentCenter));
        SetCenteredPosition(
            m_CurrentPreviewFrame,
            new Vector2(m_CurrentPreviewFrame.anchoredPosition.x, currentCenter + 12f));

        float currentBottomLine = currentTop - m_CurrentInfoHeight - SeparationInset;
        SetCenteredPosition(m_CurrentInfoBottomLine, new Vector2(0f, currentBottomLine));

        float upgradeTopLine = currentBottomLine - SeparationBandHeight;
        SetCenteredPosition(m_UpgradeInfoTopLine, new Vector2(0f, upgradeTopLine));

        float upgradeTop = upgradeTopLine - SeparationInset;
        float upgradeCenter = upgradeTop - m_UpgradeInfoHeight * 0.5f;
        SetCenteredPosition(varUpgradeRoot, new Vector2(0f, upgradeCenter));
        SetCenteredPosition(
            m_UpgradePreviewFrame,
            new Vector2(m_UpgradePreviewFrame.anchoredPosition.x, upgradeCenter + 12f));

        float upgradeBottomLine = upgradeTop - m_UpgradeInfoHeight - SeparationInset;
        SetCenteredPosition(m_UpgradeInfoBottomLine, new Vector2(0f, upgradeBottomLine));

        float conditionTop = upgradeBottomLine - ConditionGap;
        SetTopCenteredRect(varConditionArea, new Vector2(0f, conditionTop), new Vector2(600f, conditionHeight));

        float optionTop = conditionTop - conditionHeight - OptionGap;
        SetCenteredRect(
            varUpgradeButtonArea,
            new Vector2(0f, optionTop - optionHeight * 0.5f),
            new Vector2(varUpgradeButtonArea.sizeDelta.x, optionHeight));
        LayoutRebuilder.ForceRebuildLayoutImmediate(varUpgradeButtonArea);
    }

    private static void SetCenteredPosition(RectTransform rect, Vector2 anchoredPosition)
    {
        if (rect == null)
            throw new ArgumentNullException(nameof(rect));

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

    private static void SetTopCenteredRect(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        if (rect == null)
            throw new ArgumentNullException(nameof(rect));

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private void UpdatePanelPosition()
    {
        if (m_TargetHost == null || varUpgradePanel == null)
            return;

        RectTransform parent = varUpgradePanel.parent as RectTransform;
        if (parent == null)
            return;

        Vector2 detailOffset = m_ReserveDetailPanelSpace
            ? Vector2.left * BuildingInfoItem.DetailPanelCenterOffset
            : Vector2.zero;
        BuildingPanelScreenClamp.PlaceBesideTarget(
            varUpgradePanel,
            parent,
            m_TargetHost.transform,
            uiOffset + detailOffset,
            m_ReserveDetailPanelSpace ? BuildingInfoItem.DetailPanelWidth : 0f);
    }

    private void ComposeCurrentInfoText(BuildingEntity building, out string name, out string desc)
    {
        name = string.Empty;
        desc = string.Empty;

        if (building == null || building.buildingData == null)
            return;

        name = BuildingPanelPresentation.GetBuildingName(building.buildingData);

        List<SelectedUpgradeInfo> selected = CollectSelectedUpgrades(building);
        desc = ComposeDescription(building.buildingData, selected);
        if (selected.Count <= 0)
            return;

        StringBuilder marks = new();
        for (int i = 0; i < selected.Count; i++)
            marks.Append(GetOptionMark(selected[i].OptionIndex));

        name += "-" + marks;

    }

    private static string ComposeDescription(
        BuildingData data,
        IReadOnlyList<SelectedUpgradeInfo> selectedUpgrades,
        TechData highlightedSkillTech = null)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        StringBuilder builder = new(BuildingPanelPresentation.GetDescription(data));
        if (selectedUpgrades == null)
            return builder.ToString();

        for (int i = 0; i < selectedUpgrades.Count; i++)
        {
            TechData tech = selectedUpgrades[i]?.TechData;
            if (tech == null || tech.ScopeType == TechScopeType.Skill)
                continue;

            string optionDesc = tech.GetFormattedDesc();
            if (string.IsNullOrWhiteSpace(optionDesc))
                continue;

            builder.Append('\n').Append('\u00B7').Append(optionDesc.Trim());
        }

        if (highlightedSkillTech?.ScopeType == TechScopeType.Skill)
        {
            string skillNotice = GetSkillUpgradeNotice(highlightedSkillTech);
            builder.Append('\n').Append('\u00B7').Append(skillNotice);
        }

        return builder.ToString();
    }

    private static string GetSkillUpgradeNotice(TechData tech)
    {
        if (tech == null || tech.ScopeType != TechScopeType.Skill || string.IsNullOrWhiteSpace(tech.SkillID))
            throw new ArgumentException("Skill upgrade notice requires a skill tech.", nameof(tech));

        SkillData skill = SkillDataModel.GetSkillData(tech.SkillID)
                          ?? throw new InvalidOperationException($"Skill tech references missing skill data. tech={tech.Identifier}, skill={tech.SkillID}");
        string skillName = LocalizationTextManager.ProcessText(
            LocalizationTextManager.GetLocalizedText(skill.NameKey, false));
        string textId = SkillRuntimeDataModel.GetLevel(tech.SkillID) > 0
            ? UpgradeSkillTextId
            : ObtainSkillTextId;
        return LocalizationTextDataModel.GetText(textId).Replace("{Skill}", skillName);
    }

    private string ComposePreviewName(BuildingEntity building, UpgradeOptionBinding selected)
    {
        if (building == null || building.buildingData == null || selected == null)
            return string.Empty;

        BuildingData previewData = selected.UpgradeBuildingData
                                   ?? throw new InvalidOperationException(
                                       $"Upgrade name is missing building data. building={building.buildingData.Identifier}.");
        return BuildingPanelPresentation.GetBuildingName(previewData);
    }

    internal static bool HasActiveConditionItem(RectTransform conditionArea)
    {
        if (conditionArea == null)
            throw new ArgumentNullException(nameof(conditionArea));

        for (int i = 0; i < conditionArea.childCount; i++)
        {
            if (conditionArea.GetChild(i).gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private bool CanShowUpgradeTips()
    {
        if (m_TargetHost == null || m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return false;

        if (m_IsWallMerge)
            return true;

        BuildingData data = m_TargetBuilding.buildingData;
        if (data.Lv <= 0)
            return false;

        string upgradeBuildingId = BuildingDataModel.GetUpgradeID(data.Identifier);
        if (string.IsNullOrWhiteSpace(upgradeBuildingId) || BuildingDataModel.GetBuildingData(upgradeBuildingId) == null)
            return false;

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

            bool visible = techManager.IsUpgradeOptionVisible(m_TargetBuilding, upgradeBuildingId, techId);
            if (visible)
                return true;
        }

        return false;
    }

    private int ResolveOptionCost(UpgradeOptionBinding binding)
    {
        if (binding == null)
            return 0;

        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        return buildManager != null
            ? buildManager.GetBuildingCost(binding.UpgradeBuildingData, m_TargetBuilding)
            : (binding.UpgradeBuildingData != null ? Mathf.Max(0, binding.UpgradeBuildingData.Cost) : 0);
    }

    private bool IsSelectedOptionExecutable()
    {
        if (m_SelectedBinding == null || m_TargetBuilding == null)
            return false;

        if (m_IsWallMerge)
        {
            BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
            return buildManager != null && buildManager.IsConstructOptionExecutable(
                m_TargetBuilding,
                LogicWallRuntime.BuiltBuildingId);
        }

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            return false;

        return techManager.IsUpgradeOptionExecutable(m_TargetBuilding, m_SelectedBinding.UpgradeBuildingId, m_SelectedBinding.TechId);
    }

    private bool IsOptionExecutable(UpgradeOptionBinding binding)
    {
        if (binding == null || m_TargetBuilding == null)
            return false;

        if (m_IsWallMerge)
        {
            BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
            return buildManager != null && buildManager.IsConstructOptionExecutable(
                m_TargetBuilding,
                LogicWallRuntime.BuiltBuildingId);
        }

        TechManager techManager = GameEntry.GetComponent<TechManager>();
        if (techManager == null)
            return false;

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

            return LocalizationTextManager.GetLocalizedText(data.NameKey);
        }

        return archetype.ToString();
    }

    private static List<SelectedUpgradeInfo> CollectSelectedUpgrades(BuildingEntity building)
    {
        List<SelectedUpgradeInfo> results = new();
        if (building == null || building.buildingData == null || string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            return results;

        BuildingData current = building.buildingData;
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

    private static float ResolveHoldDurationSeconds(int starCount)
    {
        return BuildingInteractionHoldPresentation.ResolveConfiguredDurationSeconds(starCount);
    }

    private bool IsActionPressed(string actionName)
    {
        InputManager inputManager = EnsureInputManager();
        return inputManager != null && inputManager.IsActionPressed(actionName);
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

    private bool IsPointerHoldingOnItem(RectTransform itemRect)
    {
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
        m_HoldTriggered = false;
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

    private static float AdvanceRecycleHoldProgress(float current, bool holding, float deltaTime)
    {
        float delta = deltaTime / Mathf.Max(0.01f, RecycleHoldDurationSeconds);
        return holding
            ? Mathf.Min(1f, current + delta)
            : Mathf.Max(0f, current - delta);
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
        RectTransform rect = varRecycleBtn != null ? varRecycleBtn.transform as RectTransform : null;
        return IsPointerHoldingOnItem(rect);
    }

    private void TryRecycleBuilding()
    {
        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        if (buildManager == null || m_TargetBuilding == null)
            return;

        if (buildManager.RecycleBuilding(m_TargetBuilding))
            GF.UI.Close(this.UIForm);
        else
            RefreshView();
    }

    private void ResetRecycleHoldState()
    {
        m_RecycleHoldProgress = 0f;
        m_RecycleTriggered = false;
        if (varRecycleFill != null)
            varRecycleFill.fillAmount = 0f;
    }

    private void OnStateChanged(object sender, GameEventArgs e)
    {
        if (m_IsAwaitingTargetTransition)
            return;

        TechUnlockedEventArgs techArgs = e as TechUnlockedEventArgs;
        if (techArgs != null
            && m_TargetBuilding != null
            && m_TargetBuilding.buildingData != null
            && string.Equals(
                m_TargetBuilding.BuildingInstanceId,
                techArgs.SourceBuildingInstanceId,
                StringComparison.Ordinal))
        {
            m_IsAwaitingTargetTransition = true;
            return;
        }

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
        CacheLayoutReferences();

        if (m_CurrentLevelTitleText == null || m_NextLevelTitleText == null)
        {
            Transform badge = varUpgradePanel != null
                ? varUpgradePanel.Find("LevelTitleBadge")
                : null;
            m_CurrentLevelTitleText = badge != null
                ? badge.Find("CurrentLevel")?.GetComponent<TextMeshProUGUI>()
                : null;
            m_NextLevelTitleText = badge != null
                ? badge.Find("NextLevel")?.GetComponent<TextMeshProUGUI>()
                : null;
            if (m_CurrentLevelTitleText == null || m_NextLevelTitleText == null)
                throw new InvalidOperationException("BuildingUpgradeTips requires current and next level title fields.");
        }

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

    private void CacheLayoutReferences()
    {
        if (varUpgradePanel == null)
            throw new InvalidOperationException("BuildingUpgradeTips requires an upgrade panel root.");

        m_Background ??= varUpgradePanel.Find("Bg") as RectTransform;
        m_LevelTitleBadge ??= varUpgradePanel.Find("LevelTitleBadge") as RectTransform;
        m_CurrentPreviewFrame ??= varUpgradePanel.Find("CurrentPreviewFrame") as RectTransform;
        m_UpgradePreviewFrame ??= varUpgradePanel.Find("UpgradePreviewFrame") as RectTransform;
        m_CurrentInfoBottomLine ??= varUpgradePanel.Find("CurrentInfoBottomLine") as RectTransform;
        m_UpgradeInfoTopLine ??= varUpgradePanel.Find("UpgradeInfoTopLine") as RectTransform;
        m_UpgradeInfoBottomLine ??= varUpgradePanel.Find("UpgradeInfoBottomLine") as RectTransform;

        if (m_Background == null
            || m_LevelTitleBadge == null
            || m_CurrentPreviewFrame == null
            || m_UpgradePreviewFrame == null
            || m_CurrentInfoBottomLine == null
            || m_UpgradeInfoTopLine == null
            || m_UpgradeInfoBottomLine == null)
        {
            throw new InvalidOperationException("BuildingUpgradeTips layout references are incomplete.");
        }
    }

    private void RefreshLevelTitle()
    {
        if (m_TargetBuilding?.buildingData == null)
            return;

        if (m_IsWallMerge)
        {
            m_CurrentLevelTitleText.text = LogicWallRuntime.TryGetPreviewCell(
                m_TargetBuilding.LogicEntityId,
                out WallGridCell cell)
                ? ResolveLongestAdjacentWallCellCount(cell, m_TargetBuilding.OwnerFactionID).ToString()
                : throw new InvalidOperationException("Wall merge target lost its preview cell.");
            m_NextLevelTitleText.text = LogicWallRuntime.BuildMergedCells(
                cell,
                m_TargetBuilding.OwnerFactionID).Count.ToString();
            return;
        }

        int currentLevel = m_TargetBuilding.buildingData.Lv;
        m_CurrentLevelTitleText.text = BuildingPanelPresentation.GetBuildingLevelTitle(currentLevel);
        m_NextLevelTitleText.text = BuildingPanelPresentation.GetBuildingLevelTitle(currentLevel + 1);
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
        m_CurrentInfoHeight = MinimumInfoHeight;
        m_UpgradeInfoHeight = MinimumInfoHeight;
        m_StableConditionHeight = 0f;
        m_ReserveDetailPanelSpace = false;
        ResetHoldState();
        ResetRecycleHoldState();
    }

    private void ResolveWallMergeMode()
    {
        m_IsWallMerge = false;
        m_WallCurrentData = null;
        m_WallMergedData = null;
        if (!LogicWallRuntime.IsWallPreviewBuilding(m_TargetBuilding?.buildingData))
            return;
        if (!LogicWallRuntime.TryGetPreviewCell(m_TargetBuilding.LogicEntityId, out WallGridCell cell))
            throw new InvalidOperationException("Wall preview building has no registered wall cell.");
        int longest = ResolveLongestAdjacentWallCellCount(cell, m_TargetBuilding.OwnerFactionID);
        if (longest <= 0)
            return;
        BuildingData perCell = BuildingDataModel.GetBuildingData(LogicWallRuntime.BuiltBuildingId)
                               ?? throw new InvalidOperationException("Lv1 wall building data is missing.");
        m_IsWallMerge = true;
        m_WallCurrentData = LogicWallRuntime.CreateBranchBuildingData(perCell, longest);
        m_WallMergedData = LogicWallRuntime.CreateBranchBuildingData(
            perCell,
            LogicWallRuntime.BuildMergedCells(cell, m_TargetBuilding.OwnerFactionID).Count);
    }

    private static int ResolveLongestAdjacentWallCellCount(WallGridCell cell, int ownerFactionId)
    {
        int longest = 0;
        IReadOnlyList<LogicEntityId> adjacent = LogicWallRuntime.GetAdjacentBranches(cell, ownerFactionId);
        for (int i = 0; i < adjacent.Count; i++)
            longest = Math.Max(longest, LogicWallRuntime.GetRequiredBranch(adjacent[i]).CellCount);
        return longest;
    }

    private void PopulateWallProperties(BuildingInfoItem item, BuildingData data)
    {
        if (item == null || data == null)
            throw new ArgumentNullException(item == null ? nameof(item) : nameof(data));
        Transform root = item.PropertyListRoot != null ? item.PropertyListRoot.transform : null;
        if (root == null)
            throw new InvalidOperationException("Wall upgrade item has no property root.");
        var stats = new List<BuildingPanelStat>();
        BuildingPanelPresentation.CollectBuildingCombatStats(data, null, stats);
        for (int i = 0; i < stats.Count; i++)
            SpawnGlyphProperty(root, stats[i]);
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
