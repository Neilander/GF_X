using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class LvEnterDialog
{
    private const float CommCharactersPerSecond = 38f;

    private readonly List<CommCardView> m_CommCards = new();
    private RectTransform m_BriefingRoot;
    private RectTransform m_ObjectiveRoot;
    private TextMeshProUGUI m_LevelTitleText;
    private TextMeshProUGUI m_HistoryOffsetText;
    private TextMeshProUGUI m_CurrentOffsetText;
    private TextMeshProUGUI m_GradeText;
    private TextMeshProUGUI m_ObjectiveText;
    private Image m_BadgeStamp;
    private TextMeshProUGUI m_BadgeStampText;
    private int m_CommVisualVersion;

    private sealed class CommCardView
    {
        public Image Portrait;
        public TextMeshProUGUI PortraitFallback;
        public TextMeshProUGUI Speaker;
        public TextMeshProUGUI Body;
        public TextMeshProUGUI Signal;
        public ScrollRect Scroll;
        public string FullText;
        public float VisibleCharacters;
        public int TotalCharacters;
        public int VisualVersion;
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        for (int i = 0; i < m_CommCards.Count; i++)
        {
            CommCardView card = m_CommCards[i];
            if (card.Body == null || card.Body.maxVisibleCharacters >= card.TotalCharacters)
                continue;
            card.VisibleCharacters += CommCharactersPerSecond * realElapseSeconds;
            card.Body.maxVisibleCharacters = Mathf.Min(card.TotalCharacters, Mathf.FloorToInt(card.VisibleCharacters));
        }
    }

    private void BuildMissionBriefingUI()
    {
        ConfigureCompactTagPanel();
        BuildCommunicationPanel();
        BuildObjectivePanel();
    }

    private void ShutdownMissionBriefingUI()
    {
        m_CommVisualVersion++;
        m_CommCards.Clear();
        m_BriefingRoot = null;
        m_ObjectiveRoot = null;
        m_LevelTitleText = null;
        m_HistoryOffsetText = null;
        m_CurrentOffsetText = null;
        m_GradeText = null;
        m_ObjectiveText = null;
        m_BadgeStamp = null;
        m_BadgeStampText = null;
    }

    private void ConfigureCompactTagPanel()
    {
        RectTransform positivePanel = varPositiveTagGrids.parent.parent.parent as RectTransform;
        RectTransform negativePanel = varNegativeTagGrids.parent.parent.parent as RectTransform;
        if (positivePanel == null || negativePanel == null || positivePanel.parent != negativePanel.parent)
            throw new InvalidOperationException("LvEnterDialog tag panel hierarchy is invalid.");

        RectTransform tagPanel = positivePanel.parent as RectTransform;
        SetRect(tagPanel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(58f, 92f), new Vector2(680f, 520f));
        Image tagBackground = tagPanel.GetComponent<Image>()
                              ?? throw new InvalidOperationException("LvEnterDialog tag panel requires an Image.");
        tagBackground.color = new Color(0.105f, 0.115f, 0.13f, 0.98f);

        ConfigureTagSection(positivePanel, varPositiveSelectNumText, varPositiveTagGrids, new Vector2(14f, 88f));
        ConfigureTagSection(negativePanel, varNegativeSelectNumText, varNegativeTagGrids, new Vector2(340f, 88f));

        RectTransform infoPanel = varInfoDesc.transform.parent as RectTransform;
        if (infoPanel == null)
            throw new InvalidOperationException("LvEnterDialog tag description panel is missing.");
        infoPanel.SetParent(tagPanel, false);
        SetRect(infoPanel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(14f, 12f), new Vector2(652f, 66f));
        Image infoBackground = infoPanel.GetComponent<Image>()
                               ?? throw new InvalidOperationException("LvEnterDialog tag description panel requires an Image.");
        infoBackground.color = new Color(0.055f, 0.065f, 0.08f, 0.94f);
        SetRect(varInfoDesc.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        varInfoDesc.rectTransform.offsetMin = new Vector2(14f, 8f);
        varInfoDesc.rectTransform.offsetMax = new Vector2(-14f, -8f);
        varInfoDesc.fontSize = 18f;
        varInfoDesc.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private static void ConfigureTagSection(
        RectTransform section,
        TextMeshProUGUI header,
        RectTransform grid,
        Vector2 position)
    {
        SetRect(section, Vector2.zero, Vector2.zero, Vector2.zero, position, new Vector2(312f, 420f));
        Image background = section.GetComponent<Image>();
        if (background != null)
            background.color = Color.clear;

        SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(-20f, 46f));
        header.fontSize = 24f;
        header.alignment = TextAlignmentOptions.MidlineLeft;

        RectTransform scrollRect = grid.parent.parent as RectTransform;
        if (scrollRect == null)
            throw new InvalidOperationException("LvEnterDialog tag scroll view is missing.");
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(0f, 0f);
        scrollRect.offsetMax = new Vector2(0f, -52f);

        GridLayoutGroup layout = grid.GetComponent<GridLayoutGroup>()
                                 ?? throw new InvalidOperationException("LvEnterDialog tag grid requires GridLayoutGroup.");
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.cellSize = new Vector2(98f, 90f);
        layout.spacing = new Vector2(6f, 6f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 3;
        ScrollRect scroll = scrollRect.GetComponent<ScrollRect>();
        if (scroll != null)
        {
            scroll.horizontal = false;
            scroll.vertical = true;
        }
    }

    private void BuildCommunicationPanel()
    {
        m_BriefingRoot = CreateRect("MissionCommunication", transform);
        m_CareerTransientObjects.Add(m_BriefingRoot.gameObject);
        SetRect(m_BriefingRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(58f, -22f), new Vector2(1350f, 326f));

        TextMeshProUGUI title = CreateText(m_BriefingRoot, LocalizeBriefingRequired("LvEnter.Comm.Title"), 27, FontStyles.Bold, 42f);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 42f));
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.margin = new Vector4(140f, 0f, 0f, 0f);

        m_CommCards.Add(CreateCommunicationCard(m_BriefingRoot, new Vector2(0f, -48f)));
        m_CommCards.Add(CreateCommunicationCard(m_BriefingRoot, new Vector2(678f, -48f)));
    }

    private CommCardView CreateCommunicationCard(RectTransform parent, Vector2 position)
    {
        RectTransform root = CreateRect("CommunicationCard", parent);
        SetRect(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(660f, 268f));
        Image background = root.gameObject.AddComponent<Image>();
        background.color = new Color(0.09f, 0.105f, 0.125f, 0.98f);

        var card = new CommCardView();
        RectTransform content = CreateRect("Content", root);
        SetRect(content, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        content.offsetMin = new Vector2(12f, 44f);
        content.offsetMax = new Vector2(-12f, -12f);
        RectTransform portraitFrame = CreateRect("PortraitFrame", content);
        SetRect(portraitFrame, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(126f, 196f));
        Image portraitBackground = portraitFrame.gameObject.AddComponent<Image>();
        portraitBackground.color = new Color(0.15f, 0.17f, 0.2f, 1f);
        RectTransform portraitRect = CreateRect("Portrait", portraitFrame);
        SetRect(portraitRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        card.Portrait = portraitRect.gameObject.AddComponent<Image>();
        card.Portrait.preserveAspect = true;
        card.Portrait.color = Color.clear;
        card.PortraitFallback = CreateText(portraitFrame, string.Empty, 48, FontStyles.Bold, 196f);
        SetRect(card.PortraitFallback.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        card.Speaker = CreateText(content, string.Empty, 23, FontStyles.Bold, 38f);
        SetRect(card.Speaker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(146f, 0f), new Vector2(462f, 38f));
        card.Speaker.alignment = TextAlignmentOptions.MidlineLeft;

        RectTransform viewport = CreateRect("MessageViewport", content);
        SetRect(viewport, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(146f, 0f), new Vector2(462f, -46f));
        viewport.gameObject.AddComponent<RectMask2D>();
        card.Scroll = viewport.gameObject.AddComponent<ScrollRect>();
        card.Scroll.horizontal = false;
        card.Scroll.vertical = true;
        card.Scroll.viewport = viewport;

        RectTransform bodyRect = CreateRect("Message", viewport);
        bodyRect.anchorMin = new Vector2(0f, 1f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = Vector2.zero;
        bodyRect.sizeDelta = Vector2.zero;
        card.Body = bodyRect.gameObject.AddComponent<TextMeshProUGUI>();
        card.Body.fontSize = 21f;
        card.Body.color = new Color(0.9f, 0.92f, 0.94f, 1f);
        card.Body.alignment = TextAlignmentOptions.TopLeft;
        card.Body.enableWordWrapping = true;
        card.Body.overflowMode = TextOverflowModes.Overflow;
        ContentSizeFitter bodyFitter = bodyRect.gameObject.AddComponent<ContentSizeFitter>();
        bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        card.Scroll.content = bodyRect;

        card.Signal = CreateText(root, string.Empty, 16, FontStyles.Normal, 34f);
        SetRect(card.Signal.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(14f, 6f), new Vector2(240f, 34f));
        card.Signal.alignment = TextAlignmentOptions.MidlineLeft;

        return card;
    }

    private void BuildObjectivePanel()
    {
        m_ObjectiveRoot = CreateRect("MissionObjectives", transform);
        m_CareerTransientObjects.Add(m_ObjectiveRoot.gameObject);
        SetRect(m_ObjectiveRoot, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(760f, 150f), new Vector2(648f, 470f));
        Image background = m_ObjectiveRoot.gameObject.AddComponent<Image>();
        background.color = new Color(0.09f, 0.105f, 0.125f, 0.98f);

        TextMeshProUGUI title = CreateText(m_ObjectiveRoot, LocalizeBriefingRequired("LvEnter.Objective.Title"), 27, FontStyles.Bold, 44f);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(190f, 44f));
        title.alignment = TextAlignmentOptions.MidlineLeft;

        m_LevelTitleText = CreateText(m_ObjectiveRoot, string.Empty, 22, FontStyles.Bold, 40f);
        SetRect(m_LevelTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(214f, -12f), new Vector2(360f, 40f));
        m_LevelTitleText.alignment = TextAlignmentOptions.MidlineLeft;

        m_HistoryOffsetText = CreateText(m_ObjectiveRoot, string.Empty, 19, FontStyles.Normal, 32f);
        SetRect(m_HistoryOffsetText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -62f), new Vector2(240f, 32f));
        m_HistoryOffsetText.alignment = TextAlignmentOptions.MidlineLeft;
        m_CurrentOffsetText = CreateText(m_ObjectiveRoot, string.Empty, 19, FontStyles.Normal, 32f);
        SetRect(m_CurrentOffsetText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -96f), new Vector2(240f, 32f));
        m_CurrentOffsetText.alignment = TextAlignmentOptions.MidlineLeft;
        m_GradeText = CreateText(m_ObjectiveRoot, string.Empty, 19, FontStyles.Normal, 32f);
        SetRect(m_GradeText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(270f, -62f), new Vector2(250f, 32f));
        m_GradeText.alignment = TextAlignmentOptions.MidlineLeft;

        RectTransform stampRect = CreateRect("OffsetBadgeStamp", m_ObjectiveRoot);
        SetRect(stampRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(82f, 82f));
        stampRect.localEulerAngles = new Vector3(0f, 0f, 7f);
        m_BadgeStamp = stampRect.gameObject.AddComponent<Image>();
        m_BadgeStamp.color = new Color(0.55f, 0.55f, 0.55f, 0.12f);
        Outline stampOutline = stampRect.gameObject.AddComponent<Outline>();
        stampOutline.effectDistance = new Vector2(3f, -3f);
        stampOutline.effectColor = new Color(0.75f, 0.75f, 0.75f, 0.9f);
        m_BadgeStampText = CreateText(stampRect, string.Empty, 36, FontStyles.Bold, 82f);
        SetRect(m_BadgeStampText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        m_ObjectiveText = CreateText(m_ObjectiveRoot, string.Empty, 20, FontStyles.Normal, 318f);
        SetRect(m_ObjectiveText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-38f, 316f));
        m_ObjectiveText.alignment = TextAlignmentOptions.TopLeft;
        m_ObjectiveText.lineSpacing = 7f;
        m_ObjectiveText.enableWordWrapping = true;
    }

    private void RefreshMissionBriefingContent()
    {
        if (m_BriefingRoot == null || m_ObjectiveRoot == null)
            return;

        StoryCommTable[] commRows = StoryTableQuery.GetCommunicationRows(
            GF.DataTable.GetDataTable<StoryCommTable>()?.GetAllDataRows()
            ?? throw new InvalidOperationException("LvEnterDialog requires StoryCommTable."),
            s_LevelIdentifier);
        for (int i = 0; i < commRows.Length; i++)
            BindCommunicationCard(m_CommCards[i], commRows[i]);

        LevelTable source = CareerConfigRuntime.GetLevelRequired(s_LevelIdentifier);
        LevelTable displayed = m_IsVariableExperiment
            ? CareerConfigRuntime.GetVariableLevelRequired(source)
            : source;
        LevelData level = LevelData.FromRow(displayed);
        m_LevelTitleText.text = LocalizeBriefingRequired(level.NameKey);
        m_ObjectiveText.text = BuildObjectiveText(level);
        RefreshMissionStatistics();
    }

    private void BindCommunicationCard(CommCardView card, StoryCommTable row)
    {
        string speaker = LocalizeBriefingRequired(row.SpeakerKey);
        string message = ApplySignalText(LocalizeBriefingRequired(row.TextKey), row.SignalState);
        card.Speaker.text = speaker;
        card.PortraitFallback.text = GetPortraitFallback(speaker);
        card.FullText = message;
        card.Signal.text = LocalizeBriefingRequired($"LvEnter.Signal.{row.SignalState}");
        ApplySignalStyle(card, row.SignalState);
        card.VisualVersion = ++m_CommVisualVersion;
        ApplyCommunicationPortrait(card, row.PortraitPath, card.VisualVersion);
        StartCommunicationTypewriter(card);
    }

    private void ApplyCommunicationPortrait(CommCardView card, string portraitPath, int version)
    {
        card.Portrait.sprite = null;
        card.Portrait.color = Color.clear;
        card.PortraitFallback.gameObject.SetActive(true);
        if (string.IsNullOrWhiteSpace(portraitPath))
            return;

        string assetPath = UtilityBuiltin.AssetsPath.GetSpritesPath(portraitPath);
        GF.UI.LoadSprite(assetPath, sprite =>
        {
            if (version != card.VisualVersion || card.Portrait == null)
                return;
            if (sprite == null)
                throw new InvalidOperationException($"Story communication portrait failed to load: {portraitPath}");
            card.Portrait.sprite = sprite;
            card.Portrait.color = Color.white;
            card.PortraitFallback.gameObject.SetActive(false);
        });
    }

    private void StartCommunicationTypewriter(CommCardView card)
    {
        card.Body.text = card.FullText ?? string.Empty;
        card.Body.ForceMeshUpdate();
        card.TotalCharacters = card.Body.textInfo.characterCount;
        card.VisibleCharacters = 0f;
        card.Body.maxVisibleCharacters = 0;
        card.Scroll.verticalNormalizedPosition = 1f;
    }

    private void RefreshMissionStatistics()
    {
        if (m_HistoryOffsetText == null)
            return;

        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        bool hasRecord = progress.TryGetMaxOffsetRate(s_LevelIdentifier, out int maxOffsetRate);
        m_HistoryOffsetText.text = string.Format(
            LocalizeBriefingRequired("LvEnter.HistoryOffset"),
            hasRecord ? maxOffsetRate.ToString() : "--");
        int currentOffsetRate = m_NegativeItems
            .Where(item => m_SelectedIds.Contains(item.TagId))
            .Sum(item => item.TagLevel);
        m_CurrentOffsetText.text = string.Format(LocalizeBriefingRequired("LvEnter.CurrentOffset"), currentOffsetRate);
        CareerConfigRuntime.GetGradeProgressForExperience(
            progress.Experience,
            out int currentSegmentExperience,
            out int requiredSegmentExperience);
        m_GradeText.text = string.Format(
            LocalizeBriefingRequired("LvEnter.Grade"),
            progress.CurrentGrade,
            currentSegmentExperience,
            requiredSegmentExperience);

        m_BadgeStamp.gameObject.SetActive(hasRecord);
        if (!hasRecord)
            return;
        ApplyBadgeStamp(CareerConfigRuntime.GetOffsetBadgeTier(maxOffsetRate));
    }

    private void ApplyBadgeStamp(CareerOffsetBadgeTier tier)
    {
        // TODO: Replace the temporary text with the final badge icon.
        Color color;
        string stampText;
        switch (tier)
        {
            case CareerOffsetBadgeTier.Plain:
                color = new Color(0.68f, 0.7f, 0.72f, 1f);
                stampText = "铁";
                break;
            case CareerOffsetBadgeTier.Bronze:
                color = new Color(0.72f, 0.39f, 0.2f, 1f);
                stampText = "铜";
                break;
            case CareerOffsetBadgeTier.Silver:
                color = new Color(0.72f, 0.78f, 0.84f, 1f);
                stampText = "银";
                break;
            case CareerOffsetBadgeTier.Gold:
                color = new Color(0.95f, 0.72f, 0.2f, 1f);
                stampText = "金";
                break;
            case CareerOffsetBadgeTier.Diamond:
                color = new Color(0.3f, 0.88f, 0.92f, 1f);
                stampText = "钻";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unsupported offset badge tier.");
        }

        m_BadgeStamp.color = new Color(color.r, color.g, color.b, 0.13f);
        m_BadgeStamp.GetComponent<Outline>().effectColor = color;
        m_BadgeStampText.color = color;
        m_BadgeStampText.text = stampText;
    }

    private static string BuildObjectiveText(LevelData level)
    {
        var builder = new StringBuilder();
        AppendObjectives(builder, level.PrimaryObjectives, "GoalUI_PrimaryTitle", false);
        AppendObjectives(builder, level.OptionalObjectives, "GoalUI_OptionalTitle", true);
        return builder.ToString();
    }

    private static void AppendObjectives(
        StringBuilder builder,
        IReadOnlyList<LevelObjectiveDefinition> objectives,
        string titleIdentifier,
        bool includeExperience)
    {
        if (objectives == null || objectives.Count == 0)
            return;
        if (builder.Length > 0)
            builder.AppendLine();
        builder.Append("<b><color=#AFC4D3>")
            .Append(LocalizationTextDataModel.GetText(titleIdentifier))
            .AppendLine("</color></b>");
        for (int i = 0; i < objectives.Count; i++)
        {
            LevelObjectiveDefinition objective = objectives[i]
                ?? throw new InvalidOperationException($"Level objective is null at index {i}.");
            string text = ObjectiveDataModel.GetText(objective.ObjectiveIdentifier, objective.UniqueValues);
            builder.Append("<color=#7F91A0>\u25A1</color> ").Append(text);
            if (includeExperience && objective.Experience > 0)
            {
                builder.Append(' ').Append(string.Format(
                    LocalizationTextDataModel.GetText("GoalUI_OptionalExperience"),
                    objective.Experience));
            }
            builder.AppendLine();
        }
    }

    private static void ApplySignalStyle(CommCardView card, StorySignalState state)
    {
        Color color;
        float alpha;
        switch (state)
        {
            case StorySignalState.Normal:
                color = new Color(0.35f, 0.88f, 0.82f, 1f);
                alpha = 1f;
                break;
            case StorySignalState.Corrupted:
                color = new Color(1f, 0.38f, 0.32f, 1f);
                alpha = 0.88f;
                break;
            case StorySignalState.Weak:
                color = new Color(0.58f, 0.66f, 0.72f, 1f);
                alpha = 0.58f;
                break;
            case StorySignalState.Fragment:
                color = new Color(0.66f, 0.76f, 0.8f, 1f);
                alpha = 0.72f;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported story signal state.");
        }
        card.Signal.color = color;
        card.Speaker.color = color;
        card.Body.color = new Color(0.9f, 0.92f, 0.94f, alpha);
        card.PortraitFallback.color = new Color(color.r, color.g, color.b, alpha);
    }

    private static string ApplySignalText(string text, StorySignalState state)
    {
        if (state == StorySignalState.Normal || state == StorySignalState.Weak)
            return text;
        var builder = new StringBuilder(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (state == StorySignalState.Corrupted && !char.IsWhiteSpace(character) && i % 7 == 4)
                builder.Append('\u2592');
            else
                builder.Append(character);
            if (state == StorySignalState.Fragment && !char.IsWhiteSpace(character) && i < text.Length - 1)
                builder.Append(' ');
        }
        return builder.ToString();
    }

    private static string GetPortraitFallback(string speaker)
    {
        for (int i = 0; i < speaker.Length; i++)
        {
            char character = speaker[i];
            if (!char.IsWhiteSpace(character) && character != '\u3010' && character != '\u3011')
                return character.ToString();
        }
        throw new InvalidOperationException("Story communication speaker contains no displayable character.");
    }

    private static string LocalizeBriefingRequired(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("LvEnter localization key is empty.", nameof(key));
        if (GF.Localization == null || !GF.Localization.HasRawString(key))
            throw new InvalidOperationException($"LvEnter localization key is missing: {key}");
        return LocalizationTextManager.GetLocalizedText(key);
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }
}
