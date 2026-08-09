using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public partial class LvEnterDialog
{
    private readonly List<GameObject> m_CareerTransientObjects = new();
    private readonly List<Archetype> m_AvailableArchetypes = new();
    private RectTransform m_CareerEntryRoot;
    private RectTransform m_IndustryList;
    private Button m_ModeButton;
    private TextMeshProUGUI m_ModeButtonText;
    private TextMeshProUGUI m_IndustryTitle;
    private RectTransform m_Modal;
    private bool m_IsVariableExperiment;
    private Archetype m_SelectedArchetype;
    private int m_IndustryVisibilityRequestVersion;

    private void InitializeCareerEntryUI()
    {
        CareerRunSettings.CancelRun();
        m_IsVariableExperiment = false;
        BuildCareerEntryRoot();
        RefreshCareerEntryUI();
    }

    private void ShutdownCareerEntryUI()
    {
        for (int i = 0; i < m_CareerTransientObjects.Count; i++)
        {
            if (m_CareerTransientObjects[i] != null)
                Destroy(m_CareerTransientObjects[i]);
        }
        m_CareerTransientObjects.Clear();
        m_CareerEntryRoot = null;
        m_IndustryList = null;
        m_ModeButton = null;
        m_ModeButtonText = null;
        m_IndustryTitle = null;
        m_Modal = null;
        m_AvailableArchetypes.Clear();
        m_SelectedArchetype = Archetype.None;
        m_IndustryVisibilityRequestVersion++;
    }

    private void BuildCareerEntryRoot()
    {
        m_CareerEntryRoot = CreateRect("CareerEntryRoot", transform);
        m_CareerTransientObjects.Add(m_CareerEntryRoot.gameObject);
        m_CareerEntryRoot.anchorMin = new Vector2(1f, 1f);
        m_CareerEntryRoot.anchorMax = new Vector2(1f, 1f);
        m_CareerEntryRoot.pivot = new Vector2(1f, 1f);
        m_CareerEntryRoot.anchoredPosition = new Vector2(-20f, -20f);
        m_CareerEntryRoot.sizeDelta = new Vector2(460f, 430f);
        Image background = m_CareerEntryRoot.gameObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.11f, 0.96f);
        VerticalLayoutGroup layout = m_CareerEntryRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        CreateText(m_CareerEntryRoot, "\u6311\u6218\u8bbe\u7f6e", 27, FontStyles.Bold, 40f);
        m_ModeButton = CreateButton(m_CareerEntryRoot, string.Empty, 48f, out m_ModeButtonText);
        m_ModeButton.onClick.AddListener(ToggleVariableExperiment);
        m_IndustryTitle = CreateText(m_CareerEntryRoot, string.Empty, 21, FontStyles.Normal, 32f);
        m_IndustryList = CreateRect("IndustryList", m_CareerEntryRoot);
        LayoutElement industryHeight = m_IndustryList.gameObject.AddComponent<LayoutElement>();
        industryHeight.preferredHeight = 190f;
        GridLayoutGroup grid = m_IndustryList.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(202f, 42f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        RectTransform footer = CreateRect("Footer", m_CareerEntryRoot);
        LayoutElement footerHeight = footer.gameObject.AddComponent<LayoutElement>();
        footerHeight.preferredHeight = 48f;
        HorizontalLayoutGroup footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 10f;
        footerLayout.childControlHeight = true;
        footerLayout.childControlWidth = true;
        footerLayout.childForceExpandWidth = true;
        Button career = CreateButton(footer, "\u751f\u6daf\u8bb0\u5f55", 48f, out _);
        career.onClick.AddListener(OpenCareerRecordPanel);
        Button growth = CreateButton(footer, "\u5c40\u5916\u6210\u957f", 48f, out _);
        growth.onClick.AddListener(OpenGrowthPanel);
    }

    private void ToggleVariableExperiment()
    {
        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        if (!progress.IsVariableExperimentUnlocked(s_LevelIdentifier))
            return;
        m_IsVariableExperiment = !m_IsVariableExperiment;
        m_SelectedIds.Clear();
        for (int i = 0; i < m_PositiveItems.Count; i++)
            m_PositiveItems[i].SetSelected(false);
        for (int i = 0; i < m_NegativeItems.Count; i++)
            m_NegativeItems[i].SetSelected(false);
        RefreshCareerEntryUI();
        RefreshTexts();
    }

    private void RefreshCareerEntryUI()
    {
        bool hasExperimentConfig = CareerConfigRuntime.TryGetExperiment(
            s_LevelIdentifier,
            out VariableExperimentTable experiment);
        bool isTutorial = CareerConfigRuntime.IsTutorialLevel(s_LevelIdentifier);
        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        bool isExperimentUnlocked = hasExperimentConfig
                                    && progress.IsVariableExperimentUnlocked(s_LevelIdentifier);
        if (m_IsVariableExperiment && !isExperimentUnlocked)
            throw new InvalidOperationException($"Level '{s_LevelIdentifier}' variable experiment is locked.");

        m_ModeButton.interactable = isExperimentUnlocked;
        m_ModeButtonText.text = m_IsVariableExperiment
            ? "\u53d8\u91cf\u8bd5\u9a8c"
            : "\u6807\u51c6\u6311\u6218";

        bool tagsVisible = !m_IsVariableExperiment && !isTutorial;
        varPositiveTagGrids.gameObject.SetActive(tagsVisible);
        varNegativeTagGrids.gameObject.SetActive(tagsVisible);
        varPositiveSelectNumText.gameObject.SetActive(tagsVisible);
        varNegativeSelectNumText.gameObject.SetActive(tagsVisible);

        m_AvailableArchetypes.Clear();
        m_AvailableArchetypes.AddRange(progress.GetUnlockedArchetypes());
        LevelTable sourceLevel = CareerConfigRuntime.GetLevelRequired(s_LevelIdentifier);
        if (!isTutorial)
        {
            CareerRunSettings.EnsureDefaultSelectionAvailable(
                m_AvailableArchetypes,
                sourceLevel.DefaultArchetype);
        }

        VariableExperimentRuleTable rule = null;
        if (m_IsVariableExperiment)
        {
            if (!hasExperimentConfig)
                throw new InvalidOperationException($"Level '{s_LevelIdentifier}' has no variable experiment config.");
            rule = CareerConfigRuntime.GetRuleRequired(experiment.RuleIdentifier);
            if (rule.ForcedArchetype != Archetype.None)
            {
                m_AvailableArchetypes.Clear();
                m_AvailableArchetypes.Add(rule.ForcedArchetype);
            }
            m_ModeButtonText.text = LocalizationTextDataModel.GetText(rule.NameKey);
            varInfoDesc.text = LocalizationTextDataModel.GetText(rule.DescKey);
        }
        else
        {
            varInfoDesc.text = string.Empty;
        }

        if (isTutorial)
        {
            m_AvailableArchetypes.Clear();
            m_AvailableArchetypes.Add(Archetype.Coding);
            m_SelectedArchetype = Archetype.Coding;
        }
        else
        {
            Archetype defaultArchetype = rule != null && rule.ForcedArchetype != Archetype.None
                ? rule.ForcedArchetype
                : sourceLevel.DefaultArchetype;
            m_SelectedArchetype = CareerRunSettings.ResolveRememberedSelection(
                s_LevelIdentifier,
                m_AvailableArchetypes,
                defaultArchetype);
        }
        m_IndustryTitle.gameObject.SetActive(false);
        m_IndustryList.gameObject.SetActive(false);
        m_IndustryTitle.text = $"\u521d\u59cb\u884c\u4e1a: {GetIndustryName(m_SelectedArchetype)}";
        RebuildIndustryButtons();

        int requestVersion = ++m_IndustryVisibilityRequestVersion;
        if (!isTutorial)
            RefreshIndustryVisibilityAsync(requestVersion).Forget();
    }

    private async UniTaskVoid RefreshIndustryVisibilityAsync(int requestVersion)
    {
        try
        {
            bool hasInitialBase = await LevelStartingIndustryService.HasInitialBaseAsync(
                s_LevelIdentifier,
                m_IsVariableExperiment);
            if (requestVersion != m_IndustryVisibilityRequestVersion || m_IndustryTitle == null || m_IndustryList == null)
                return;

            m_IndustryTitle.gameObject.SetActive(hasInitialBase);
            m_IndustryList.gameObject.SetActive(hasInitialBase);
        }
        catch (Exception exception)
        {
            Log.Error(
                "[LvEnterDialog] Failed to inspect initial base placeholder. level={0}, experiment={1}, error={2}",
                s_LevelIdentifier,
                m_IsVariableExperiment,
                exception);
        }
    }

    private void RebuildIndustryButtons()
    {
        for (int i = m_IndustryList.childCount - 1; i >= 0; i--)
            Destroy(m_IndustryList.GetChild(i).gameObject);

        for (int i = 0; i < m_AvailableArchetypes.Count; i++)
        {
            Archetype archetype = m_AvailableArchetypes[i];
            Button button = CreateButton(m_IndustryList, GetIndustryName(archetype), 42f, out TextMeshProUGUI label);
            SetButtonSelected(button, archetype == m_SelectedArchetype);
            button.onClick.AddListener(() =>
            {
                m_SelectedArchetype = archetype;
                CareerRunSettings.RememberSelection(s_LevelIdentifier, archetype);
                m_IndustryTitle.text = $"\u521d\u59cb\u884c\u4e1a: {GetIndustryName(archetype)}";
                RebuildIndustryButtons();
            });
            label.fontSize = 19f;
        }
    }

    private void OpenCareerRecordPanel()
    {
        CloseModal();
        m_Modal = CreateModal("CareerRecordPanel", new Vector2(760f, 680f));
        VerticalLayoutGroup layout = AddVerticalLayout(m_Modal, 24, 14f);
        layout.childAlignment = TextAnchor.UpperCenter;
        CreateText(m_Modal, "\u751f\u6daf\u8bb0\u5f55\uff08\u6d4b\u8bd5\uff09", 30, FontStyles.Bold, 52f);

        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        string cleared = JoinSorted(progress.GetClearedLevelsForDebug());
        string experiments = JoinSorted(progress.GetClearedExperimentsForDebug());
        var lines = new List<string>
        {
            $"\u5df2\u901a\u8fc7\u5173\u5361: {cleared}",
            $"\u5df2\u901a\u8fc7\u53d8\u91cf\u8bd5\u9a8c: {experiments}",
            $"\u6210\u957f\u70b9: \u83b7\u5f97 {progress.GetEarnedPointCount()} / \u5df2\u7528 {progress.GetSpentPointCount()} / \u53ef\u7528 {progress.GetAvailablePointCount()}",
            $"\u504f\u79fb\u7387\u5956\u52b1\u95e8\u69db: {CareerConfigRuntime.OffsetPointThreshold}"
        };
        HashSet<string> allLevels = new(progress.GetClearedLevelsForDebug(), StringComparer.Ordinal);
        allLevels.UnionWith(progress.GetClearedExperimentsForDebug());
        foreach (string level in allLevels.OrderBy(value => value, StringComparer.Ordinal))
            lines.Add($"{level} \u6700\u9ad8\u504f\u79fb\u7387: {progress.GetMaxOffsetRate(level)}");
        CreateText(m_Modal, string.Join("\n", lines), 22, FontStyles.Normal, 470f).alignment = TextAlignmentOptions.TopLeft;
        Button close = CreateButton(m_Modal, "\u5173\u95ed", 52f, out _);
        close.onClick.AddListener(CloseModal);
    }

    private void OpenGrowthPanel()
    {
        CloseModal();
        m_Modal = CreateModal("MetaGrowthPanel", new Vector2(1020f, 900f));
        VerticalLayoutGroup outer = AddVerticalLayout(m_Modal, 20, 10f);
        outer.childAlignment = TextAnchor.UpperCenter;
        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        TextMeshProUGUI title = CreateText(
            m_Modal,
            $"\u5c40\u5916\u6210\u957f    \u53ef\u7528\u70b9\u6570: {progress.GetAvailablePointCount()}",
            28,
            FontStyles.Bold,
            48f);

        ScrollRect scroll = CreateScrollView(m_Modal, 740f, out RectTransform content);
        IReadOnlyList<MetaGrowthTable> rows = CareerConfigRuntime.GrowthRows;
        for (int i = 0; i < rows.Count; i++)
            CreateGrowthRow(content, rows[i], title);

        Button close = CreateButton(m_Modal, "\u5173\u95ed", 48f, out _);
        close.onClick.AddListener(CloseModal);
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1f;
    }

    private void CreateGrowthRow(RectTransform parent, MetaGrowthTable row, TextMeshProUGUI title)
    {
        RectTransform root = CreateRect(row.Identifier, parent);
        LayoutElement height = root.gameObject.AddComponent<LayoutElement>();
        height.preferredHeight = 104f;
        Image background = root.gameObject.AddComponent<Image>();
        background.color = new Color(0.16f, 0.17f, 0.2f, 1f);
        HorizontalLayoutGroup layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;

        RectTransform info = CreateRect("Info", root);
        LayoutElement infoWidth = info.gameObject.AddComponent<LayoutElement>();
        infoWidth.preferredWidth = 530f;
        VerticalLayoutGroup infoLayout = info.gameObject.AddComponent<VerticalLayoutGroup>();
        infoLayout.spacing = 2f;
        infoLayout.childControlHeight = true;
        infoLayout.childControlWidth = true;
        infoLayout.childForceExpandHeight = false;

        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        int level = progress.GetGrowthLevel(row.Identifier);
        TextMeshProUGUI name = CreateText(info, string.Empty, 21, FontStyles.Bold, 36f);
        TextMeshProUGUI preview = CreateText(info, string.Empty, 18, FontStyles.Normal, 32f);
        preview.color = new Color(1f, 0.72f, 0.25f, 1f);
        name.text = $"{GetGrowthName(row)}  Lv.{level}/{row.UniqueValues.Length - 1}    {FormatGrowthEffect(row, level)}";

        CreateGrowthButton(root, "min", row, 0, level > 0, preview, title);
        CreateGrowthButton(root, "-", row, level - 1, level > 0, preview, title);
        CreateGrowthButton(
            root,
            "+",
            row,
            level + 1,
            level < row.UniqueValues.Length - 1 && progress.GetAvailablePointCount() >= row.LevelCosts[level],
            preview,
            title);
        int maxTarget = progress.GetMaxAffordableLevel(row.Identifier);
        CreateGrowthButton(root, "max", row, maxTarget, maxTarget > level, preview, title);
    }

    private void CreateGrowthButton(
        RectTransform parent,
        string label,
        MetaGrowthTable row,
        int targetLevel,
        bool interactable,
        TextMeshProUGUI preview,
        TextMeshProUGUI title)
    {
        Button button = CreateButton(parent, label, 52f, out _);
        LayoutElement width = button.gameObject.GetComponent<LayoutElement>();
        width.preferredWidth = label.Length > 1 ? 66f : 48f;
        button.interactable = interactable;
        if (!interactable)
            return;

        button.onClick.AddListener(() =>
        {
            CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
            if (!progress.TrySetGrowthLevel(row.Identifier, targetLevel, out string error))
            {
                Log.Error("[MetaGrowthUI] Failed to set growth. id={0}, target={1}, error={2}", row.Identifier, targetLevel, error);
                return;
            }
            OpenGrowthPanel();
        });
        AddHover(button.gameObject,
            () => preview.text = $"\u9884\u89c8: {FormatGrowthEffect(row, targetLevel)}",
            () => preview.text = string.Empty);
    }

    private RectTransform CreateModal(string name, Vector2 size)
    {
        RectTransform modal = CreateRect(name, transform);
        m_CareerTransientObjects.Add(modal.gameObject);
        modal.anchorMin = new Vector2(0.5f, 0.5f);
        modal.anchorMax = new Vector2(0.5f, 0.5f);
        modal.pivot = new Vector2(0.5f, 0.5f);
        modal.sizeDelta = size;
        Image image = modal.gameObject.AddComponent<Image>();
        image.color = new Color(0.07f, 0.08f, 0.1f, 0.99f);
        return modal;
    }

    private void CloseModal()
    {
        if (m_Modal == null)
            return;
        m_CareerTransientObjects.Remove(m_Modal.gameObject);
        Destroy(m_Modal.gameObject);
        m_Modal = null;
    }

    private static ScrollRect CreateScrollView(Transform parent, float height, out RectTransform content)
    {
        RectTransform root = CreateRect("Scroll", parent);
        LayoutElement rootHeight = root.gameObject.AddComponent<LayoutElement>();
        rootHeight.preferredHeight = height;
        ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;

        RectTransform viewport = CreateRect("Viewport", root);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.2f);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 7f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }

    private static VerticalLayoutGroup AddVerticalLayout(RectTransform rect, int padding, float spacing)
    {
        VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = spacing;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent,
        string text,
        int fontSize,
        FontStyles style,
        float preferredHeight)
    {
        RectTransform rect = CreateRect("Text", parent);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        return label;
    }

    private static Button CreateButton(Transform parent, string label, float preferredHeight, out TextMeshProUGUI labelText)
    {
        RectTransform rect = CreateRect("Button", parent);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.24f, 0.26f, 0.3f, 1f);
        Button button = rect.gameObject.AddComponent<Button>();
        labelText = CreateText(rect, label, 21, FontStyles.Normal, preferredHeight);
        RectTransform textRect = labelText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private static void SetButtonSelected(Button button, bool selected)
    {
        Image image = button.GetComponent<Image>();
        image.color = selected
            ? new Color(0.9f, 0.45f, 0.08f, 1f)
            : new Color(0.24f, 0.26f, 0.3f, 1f);
    }

    private static void AddHover(GameObject target, Action enter, Action exit)
    {
        EventTrigger trigger = target.AddComponent<EventTrigger>();
        EventTrigger.Entry enterEntry = new() { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(_ => enter());
        trigger.triggers.Add(enterEntry);
        EventTrigger.Entry exitEntry = new() { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => exit());
        trigger.triggers.Add(exitEntry);
    }

    private static string GetIndustryName(Archetype archetype)
    {
        return LocalizationTextDataModel.GetText($"Archetype_{archetype}", applyRichText: false);
    }

    private static string JoinSorted(IReadOnlyCollection<string> values)
    {
        return values.Count == 0
            ? "-"
            : string.Join(", ", values.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string GetGrowthName(MetaGrowthTable row)
    {
        if (row == null)
            throw new ArgumentNullException(nameof(row));
        return LocalizationTextDataModel.GetText(row.NameKey, applyRichText: false);
    }

    private static string FormatGrowthEffect(MetaGrowthTable row, int level)
    {
        if (level < 0 || level >= row.UniqueValues.Length)
            throw new ArgumentOutOfRangeException(nameof(level));
        Fix64 value = row.UniqueValues[level];
        if (level == 0)
            return "\u65e0\u6548\u679c";
        return DescriptionValueFormatter.LocalizeAndFill(row.DescKey, new[] { value });
    }
}
