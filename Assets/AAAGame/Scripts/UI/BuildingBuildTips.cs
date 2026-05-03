using UnityGameFramework.Runtime;
using System.Collections.Generic;
using System;
using GameFramework.Event;
using UnityEngine;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class BuildingBuildTips : UIFormBase
{
    public const string P_TargetHost = "TargetHost";

    [SerializeField] private Vector2 uiOffset = new(0f, 80f);

    private const string CoinIconPath = "UI/Icon/Coin.png";
    private const string ForceIconPath = "UI/Icon/Force.png";
    private const string SupplyIconPath = "UI/Icon/Supply.png";
    private const string BaseMilestoneTechPattern = "Tech_BaseBuilt_{0}_Lv1";

    private static readonly Dictionary<BuilType, Archetype> s_LastSelectedIndustryByType = new();
    private static readonly Dictionary<string, int> s_ArmySupplyPerUnitCache = new(StringComparer.Ordinal);

    private readonly List<IndustryOptionBinding> m_IndustryBindings = new();
    private readonly List<BuildOptionBinding> m_BuildOptionBindings = new();
    private readonly Dictionary<Archetype, List<BuildingData>> m_BuildingCandidatesByArchetype = new();

    private BuildingInfoItem m_BuildInfoTemplate;
    private GameObject m_IconNumTemplate;
    private GameObject m_StarTemplate;
    private InputManager m_InputManager;

    private InteractionHost m_TargetHost;
    private BuildingEntity m_TargetBuilding;
    private Archetype m_SelectedArchetype = Archetype.None;

    private BuildOptionBinding m_HoldBinding;
    private float m_HoldProgressStars;
    private bool m_HoldTriggered;

    private sealed class IndustryOptionBinding
    {
        public Archetype Archetype;
        public IndustryOptionItem Item;
        public string ActionName;
    }

    private sealed class BuildOptionBinding
    {
        public BuildingData BuildingData;
        public BuildingInfoItem Item;
        public string ActionName;
        public int StarCount;
        public bool Executable;
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

        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnResourceChanged);
        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnResourceChanged);
        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnResourceChanged);
        GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnResourceChanged);
        GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        ClearRuntimeState();

        base.OnClose(isShutdown, userData);
    }

    private void Update()
    {
        UpdatePanelPosition();
        UpdateIndustryInput();
        UpdateBuildHoldProgress();
    }

    private void RefreshView()
    {
        CacheTemplates();
        ClearAllSpawnedItems();
        ClearRuntimeState();

        if (!BuildIndustryCandidates())
            return;

        SpawnIndustryOptions();
        SelectDefaultIndustry();
    }

    private bool BuildIndustryCandidates()
    {
        m_BuildingCandidatesByArchetype.Clear();

        if (m_TargetBuilding == null || m_TargetBuilding.buildingData == null)
            return false;

        var buildManager = GameEntry.GetComponent<BuildManager>();
        if (buildManager == null)
            return false;

        List<BuildingData> candidates = buildManager.GetLv0ConstructCandidates(m_TargetBuilding, requireUnlockedArche: true);
        if (candidates == null || candidates.Count <= 0)
            return false;

        for (int i = 0; i < candidates.Count; i++)
        {
            BuildingData data = candidates[i];
            if (data == null || data.Arche == Archetype.None)
                continue;

            if (!m_BuildingCandidatesByArchetype.TryGetValue(data.Arche, out List<BuildingData> list))
            {
                list = new List<BuildingData>();
                m_BuildingCandidatesByArchetype[data.Arche] = list;
            }

            list.Add(data);
        }

        foreach (List<BuildingData> list in m_BuildingCandidatesByArchetype.Values)
        {
            list.Sort((left, right) => string.Compare(left.Identifier, right.Identifier, StringComparison.Ordinal));
        }

        return m_BuildingCandidatesByArchetype.Count > 0;
    }

    private void SpawnIndustryOptions()
    {
        if (varIndustryList == null)
            return;

        int optionIndex = 0;
        foreach (Archetype archetype in Enum.GetValues(typeof(Archetype)))
        {
            if (archetype == Archetype.None)
                continue;

            if (!m_BuildingCandidatesByArchetype.TryGetValue(archetype, out List<BuildingData> list) || list.Count <= 0)
                continue;

            if (optionIndex >= 5)
                break;

            IndustryOptionItem optionItem = SpawnItem<UIItemObject>(varIndustryOptionItem, varIndustryList.transform).itemLogic as IndustryOptionItem;
            if (optionItem == null)
                continue;

            string actionName = $"Player/Industry{optionIndex + 1}";
            string keyText = InputGetKeyText.GetKeyText(actionName);
            // Archetype_* 属于 LocalizationTextTable 标识符，必须走 LocalizationTextDataModel。
            string industryName = LocalizationTextDataModel.GetText($"Archetype_{archetype}", applyRichText: false);

            Archetype captured = archetype;
            optionItem.SetData(keyText, industryName, false, () => SelectIndustry(captured, true));

            m_IndustryBindings.Add(new IndustryOptionBinding
            {
                Archetype = archetype,
                Item = optionItem,
                ActionName = actionName
            });

            optionIndex++;
        }
    }

    private void SelectDefaultIndustry()
    {
        Archetype fallback = m_IndustryBindings.Count > 0 ? m_IndustryBindings[0].Archetype : Archetype.None;
        Archetype selected = fallback;

        if (m_TargetBuilding != null
            && m_TargetBuilding.buildingData != null
            && s_LastSelectedIndustryByType.TryGetValue(m_TargetBuilding.buildingData.Type, out Archetype lastSelected))
        {
            for (int i = 0; i < m_IndustryBindings.Count; i++)
            {
                if (m_IndustryBindings[i].Archetype == lastSelected)
                {
                    selected = lastSelected;
                    break;
                }
            }
        }

        SelectIndustry(selected, false);
    }

    private void SelectIndustry(Archetype archetype, bool rememberSelection)
    {
        if (archetype == Archetype.None)
            return;

        bool exists = false;
        for (int i = 0; i < m_IndustryBindings.Count; i++)
        {
            IndustryOptionBinding binding = m_IndustryBindings[i];
            if (binding == null || binding.Item == null)
                continue;

            bool selected = binding.Archetype == archetype;
            binding.Item.SetSelected(selected);
            if (selected)
                exists = true;
        }

        if (!exists)
            return;

        if (m_SelectedArchetype == archetype)
            return;

        m_SelectedArchetype = archetype;
        if (rememberSelection && m_TargetBuilding != null && m_TargetBuilding.buildingData != null)
            s_LastSelectedIndustryByType[m_TargetBuilding.buildingData.Type] = archetype;

        SpawnBuildOptionsForSelectedIndustry();
    }

    private void SpawnBuildOptionsForSelectedIndustry()
    {
        UnspawnItemTemplate(m_IconNumTemplate);
        UnspawnItemTemplate(m_StarTemplate);
        UnspawnItemTemplate(varBuildingInfoSeparationLineItem);
        UnspawnItemTemplate(varBuildingInfoItem);
        m_BuildOptionBindings.Clear();
        ResetHoldState();

        if (m_SelectedArchetype == Archetype.None)
            return;

        if (!m_BuildingCandidatesByArchetype.TryGetValue(m_SelectedArchetype, out List<BuildingData> candidates))
            return;

        int optionCount = Mathf.Min(4, candidates.Count);
        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        Transform buildOptionRoot = varBg != null ? varBg.transform : transform;

        for (int i = 0; i < optionCount; i++)
        {
            if (i > 0)
                SpawnItem<UIItemObject>(varBuildingInfoSeparationLineItem, buildOptionRoot);

            BuildingData data = candidates[i];
            BuildingInfoItem infoItem = SpawnItem<UIItemObject>(varBuildingInfoItem, buildOptionRoot).itemLogic as BuildingInfoItem;
            if (infoItem == null || data == null)
                continue;

            string actionName = $"Player/Build{i + 1}";
            string keyText = InputGetKeyText.GetKeyText(actionName);
            // name 作为标题展示，按需求不走富文本规则（避免关键词高亮污染标题视觉）。
            string name = LocalizationTextManager.GetLocalizedText(data.NameKey, false);
            // desc 默认走富文本（关键词高亮、正负数字着色）。
            string desc = data.GetFormattedDesc();
            infoItem.SetData(keyText, name, desc);

            bool executable = buildManager != null && m_TargetBuilding != null && buildManager.IsConstructOptionExecutable(m_TargetBuilding, data.Identifier);
            infoItem.SetExecutable(executable);

            PopulatePrice(infoItem, data);
            PopulateProperties(infoItem, data);

            BuildOptionBinding binding = new()
            {
                BuildingData = data,
                Item = infoItem,
                ActionName = actionName,
                StarCount = Mathf.Max(1, data.Cost),
                Executable = executable,
            };

            SpawnProgressStars(binding);
            m_BuildOptionBindings.Add(binding);
        }
    }

    private void PopulatePrice(BuildingInfoItem infoItem, BuildingData data)
    {
        if (infoItem == null || data == null || m_IconNumTemplate == null)
            return;

        GameObject root = infoItem.PriceRoot;
        if (root == null)
            return;

        IconNumItem iconNum = SpawnItem<UIItemObject>(m_IconNumTemplate, root.transform).itemLogic as IconNumItem;
        if (iconNum == null)
            return;

        iconNum.SetData(CoinIconPath, data.Cost.ToString());
        if (!HasEnoughCoinForBuild(data.Cost))
            iconNum.SetNumberColor(Color.red);
    }

    private void PopulateProperties(BuildingInfoItem infoItem, BuildingData data)
    {
        if (infoItem == null || data == null)
            return;

        switch (data.Type)
        {
            case BuilType.Base:
                SpawnProperty(infoItem.PropertyListRoot.transform, ForceIconPath, "+10");
                break;
            case BuilType.Tech:
                break;
            case BuilType.Prod:
                SpawnProperty(infoItem.PropertyListRoot.transform, CoinIconPath, FormatSigned(data.Production));
                break;
            case BuilType.Army:
                SpawnProperty(infoItem.PropertyListRoot.transform, SupplyIconPath, ResolveArmySupplyPerUnit(data.UnitID).ToString());
                SpawnProperty(infoItem.PropertyListRoot.transform, ForceIconPath, data.Production.ToString());
                break;
        }
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

    private void SpawnProgressStars(BuildOptionBinding binding)
    {
        if (binding == null || binding.Item == null || m_StarTemplate == null)
            return;

        GameObject root = binding.Item.ProgressRoot;
        if (root == null)
            return;

        int starCount = Mathf.Max(1, binding.StarCount);
        for (int i = 0; i < starCount; i++)
        {
            StarItem starItem = SpawnItem<UIItemObject>(m_StarTemplate, root.transform).itemLogic as StarItem;
            if (starItem == null)
                continue;

            starItem.SetHighlight(false);
            binding.Stars.Add(starItem);
        }

        ApplyStarHighlight(binding, 0);
    }

    private void UpdateIndustryInput()
    {
        if (m_IndustryBindings.Count == 0)
            return;

        for (int i = 0; i < m_IndustryBindings.Count; i++)
        {
            IndustryOptionBinding binding = m_IndustryBindings[i];
            if (binding == null || string.IsNullOrWhiteSpace(binding.ActionName))
                continue;

            if (!WasActionPressedThisFrame(binding.ActionName))
                continue;

            SelectIndustry(binding.Archetype, true);
            break;
        }
    }

    private void UpdateBuildHoldProgress()
    {
        if (m_BuildOptionBindings.Count == 0)
        {
            ResetHoldState();
            return;
        }

        BuildOptionBinding pressedBinding = ResolvePressedBuildBinding();
        if (m_HoldBinding == null)
        {
            if (pressedBinding == null)
                return;

            m_HoldBinding = pressedBinding;
            m_HoldProgressStars = 0f;
            m_HoldTriggered = false;
        }
        else if (pressedBinding != null && pressedBinding != m_HoldBinding)
        {
            ApplyStarHighlight(m_HoldBinding, 0);
            m_HoldBinding = pressedBinding;
            m_HoldProgressStars = 0f;
            m_HoldTriggered = false;
        }

        if (m_HoldBinding == null)
            return;

        int starCount = Mathf.Max(1, m_HoldBinding.Stars.Count);
        float duration = m_HoldBinding.Item != null ? m_HoldBinding.Item.ProgressDuration : 1f;
        float starsPerSecond = starCount / Mathf.Max(0.01f, duration);
        float delta = starsPerSecond * Time.deltaTime;

        bool pressing = IsBuildBindingPressed(m_HoldBinding);
        m_HoldProgressStars = pressing
            ? Mathf.Min(starCount, m_HoldProgressStars + delta)
            : Mathf.Max(0f, m_HoldProgressStars - delta);

        int highlightCount;
        if (pressing)
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
                // 规则：
                // 1) 第一颗星按下即亮；
                // 2) 最后一颗星在进度满时亮，并同帧触发建造；
                // 3) 中间星在两者之间等间隔分配。
                float interval = starCount / (starCount - 1f);
                int extraHighlights = Mathf.FloorToInt((m_HoldProgressStars + 1e-4f) / interval);
                highlightCount = Mathf.Clamp(1 + extraHighlights, 1, starCount);
            }
        }
        else
        {
            // 松手回退沿用线性反向熄灭。
            highlightCount = Mathf.Clamp(Mathf.FloorToInt(m_HoldProgressStars + 1e-4f), 0, starCount);
        }
        ApplyStarHighlight(m_HoldBinding, highlightCount);

        // 视觉与行为对齐：最后一颗星点亮的同一帧就触发建造。
        if (!m_HoldTriggered && pressing && highlightCount >= starCount)
        {
            m_HoldTriggered = true;
            TryConstructCurrentHoldingBuilding();
        }

        if (!pressing && m_HoldProgressStars <= 1e-4f)
        {
            m_HoldBinding = null;
            m_HoldTriggered = false;
        }
    }

    private BuildOptionBinding ResolvePressedBuildBinding()
    {
        for (int i = 0; i < m_BuildOptionBindings.Count; i++)
        {
            BuildOptionBinding binding = m_BuildOptionBindings[i];
            if (binding == null || !binding.Executable || binding.Item == null)
                continue;

            if (IsBuildBindingPressed(binding))
                return binding;
        }

        return null;
    }

    private bool IsBuildBindingPressed(BuildOptionBinding binding)
    {
        if (binding == null || binding.Item == null)
            return false;

        return IsActionPressed(binding.ActionName) || IsPointerHoldingOnItem(binding.Item.HoldRoot);
    }

    private void TryConstructCurrentHoldingBuilding()
    {
        if (m_HoldBinding == null || m_HoldBinding.BuildingData == null || m_TargetBuilding == null)
            return;

        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        if (buildManager == null)
            return;

        bool success = buildManager.ConstructBuilding(m_TargetBuilding, m_HoldBinding.BuildingData.Identifier);
        if (!success)
            RefreshView();
    }

    private void ApplyStarHighlight(BuildOptionBinding binding, int highlightCount)
    {
        if (binding == null || binding.Stars == null)
            return;

        for (int i = 0; i < binding.Stars.Count; i++)
        {
            StarItem star = binding.Stars[i];
            if (star == null)
                continue;

            star.SetHighlight(i < highlightCount);
        }
    }

    private void UpdatePanelPosition()
    {
        if (m_TargetHost == null || varBuildPanel == null)
            return;

        RectTransform panelRect = varBuildPanel.transform as RectTransform;
        RectTransform parentRect = panelRect != null ? panelRect.parent as RectTransform : null;
        if (panelRect == null || parentRect == null)
            return;

        Vector3 uiPos = GF.UI.PositionWorldToUI(m_TargetHost.GetPromptPosition(), parentRect);
        panelRect.anchoredPosition = (Vector2)uiPos + uiOffset;
    }

    private void OnResourceChanged(object sender, GameEventArgs e)
    {
        RefreshView();
    }

    private void OnEntityFactionChanged(object sender, GameEventArgs e)
    {
        EntityFactionChangedEventArgs args = e as EntityFactionChangedEventArgs;
        if (args == null || m_TargetBuilding == null || m_TargetBuilding.Id != args.EntityId)
            return;

        RefreshView();
    }

    private static BuildingEntity ResolveTargetBuilding(InteractionHost targetHost)
    {
        if (targetHost == null)
            return null;

        if (targetHost.Owner is BuildingEntity owner)
            return owner;

        return targetHost.GetComponent<BuildingEntity>();
    }

    private static int ResolveArmySupplyPerUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || GF.DataTable == null)
            return 0;

        if (s_ArmySupplyPerUnitCache.TryGetValue(unitId, out int cachedSupply))
            return cachedSupply;

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        if (table == null)
            return 0;

        foreach (CharacterDataDetail row in table.GetAllDataRows())
        {
            if (row == null || string.IsNullOrWhiteSpace(row.CharacterKey))
                continue;

            s_ArmySupplyPerUnitCache[row.CharacterKey] = Mathf.Max(0, row.Supply);
        }

        return s_ArmySupplyPerUnitCache.TryGetValue(unitId, out cachedSupply) ? cachedSupply : 0;
    }

    private static string FormatSigned(int value)
    {
        return value > 0 ? $"+{value}" : value.ToString();
    }

    private static bool HasEnoughCoinForBuild(int cost)
    {
        return InGameDataModel.GetValue(IngameValueType.Coin) >= cost;
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

    private void ClearAllSpawnedItems()
    {
        UnspawnItemTemplate(m_IconNumTemplate);
        UnspawnItemTemplate(m_StarTemplate);
        UnspawnItemTemplate(varBuildingInfoSeparationLineItem);
        UnspawnItemTemplate(varBuildingInfoItem);
        UnspawnItemTemplate(varIndustryOptionItem);
    }

    private void CacheTemplates()
    {
        if (varBuildingInfoItem == null)
            return;

        if (m_BuildInfoTemplate == null || m_BuildInfoTemplate.gameObject != varBuildingInfoItem)
            m_BuildInfoTemplate = varBuildingInfoItem.GetComponent<BuildingInfoItem>();

        if (m_BuildInfoTemplate == null)
            return;

        m_IconNumTemplate = m_BuildInfoTemplate.IconNumTemplate;
        m_StarTemplate = m_BuildInfoTemplate.StarTemplate;
    }

    private void ClearRuntimeState()
    {
        m_IndustryBindings.Clear();
        m_BuildOptionBindings.Clear();
        m_BuildingCandidatesByArchetype.Clear();
        m_SelectedArchetype = Archetype.None;
        ResetHoldState();
    }

    private void ResetHoldState()
    {
        m_HoldBinding = null;
        m_HoldProgressStars = 0f;
        m_HoldTriggered = false;
    }

    private void UnspawnItemTemplate(GameObject template)
    {
        if (template == null)
            return;

        UnspawnAllItem<UIItemObject>(template);
    }
}
