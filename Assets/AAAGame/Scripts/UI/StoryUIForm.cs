using System;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using GameFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public sealed class StoryUIForm : UIFormBase, IPointerClickHandler
{
    public const string P_ScriptId = "ScriptId";

    private const float BaseCharactersPerSecond = 36f;
    private const float AutoAdvanceDelay = 1.35f;

    private readonly List<HistoryEntry> m_History = new List<HistoryEntry>();
    private readonly float[] m_Speeds = { 0.75f, 1f, 1.5f, 2f };

    private IReadOnlyList<StoryScriptTable> m_Rows;
    private string m_ScriptId;
    private int m_RowIndex;
    private int m_VisualVersion;
    private int m_TotalCharacters;
    private float m_VisibleCharacters;
    private float m_AutoTimer;
    private int m_SpeedIndex = 1;
    private bool m_IsLineComplete;
    private bool m_AutoPlay;
    private bool m_FastForward;
    private bool m_IsTransitioning;

    private CanvasGroup m_RootGroup;
    private RectTransform m_ContentRoot;
    private Image m_Background;
    private Image m_PortraitLeft;
    private Image m_PortraitCenter;
    private Image m_PortraitRight;
    private Image m_GlitchFlash;
    private GameObject m_NormalPanel;
    private GameObject m_TerminalPanel;
    private GameObject m_DocumentPanel;
    private GameObject m_HistoryPanel;
    private TMP_Text m_NormalSpeaker;
    private TMP_Text m_NormalText;
    private TMP_Text m_TerminalText;
    private TMP_Text m_DocumentText;
    private TMP_Text m_HistoryText;
    private RectTransform m_HistoryContent;
    private TMP_Text m_ProgressText;
    private TMP_Text m_AutoButtonText;
    private TMP_Text m_FastButtonText;
    private TMP_Text m_SpeedButtonText;
    private Button m_FastButton;
    private TMP_Text m_ActiveText;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        BuildView();
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        m_ScriptId = Params.Get<VarString>(P_ScriptId);
        if (string.IsNullOrWhiteSpace(m_ScriptId))
            throw new InvalidOperationException("StoryUIForm opened without a script id.");

        m_Rows = StoryManager.GetActiveRows(m_ScriptId);
        m_RowIndex = 0;
        m_History.Clear();
        m_AutoPlay = false;
        m_FastForward = false;
        m_IsTransitioning = false;
        m_SpeedIndex = 1;
        m_RootGroup.alpha = 1f;
        m_HistoryPanel.SetActive(false);
        m_FastButton.interactable = StoryManager.HasRead(m_ScriptId);
        RefreshControls();
        ShowCurrentRow();
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        if (m_IsTransitioning || m_HistoryPanel.activeSelf || m_ActiveText == null)
            return;

        if (!m_IsLineComplete)
        {
            float charactersPerSecond = BaseCharactersPerSecond * m_Speeds[m_SpeedIndex];
            if (m_FastForward)
                charactersPerSecond = float.MaxValue;
            m_VisibleCharacters += realElapseSeconds * charactersPerSecond;
            int visible = m_FastForward ? m_TotalCharacters : Mathf.FloorToInt(m_VisibleCharacters);
            m_ActiveText.maxVisibleCharacters = Mathf.Min(visible, m_TotalCharacters);
            if (m_ActiveText.maxVisibleCharacters >= m_TotalCharacters)
                CompleteCurrentLine();
            return;
        }

        if (!m_AutoPlay)
            return;
        m_AutoTimer -= realElapseSeconds;
        if (m_AutoTimer <= 0f)
            AdvanceToNextRow();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        m_ContentRoot.DOKill();
        m_RootGroup.DOKill();
        m_GlitchFlash.DOKill();
        base.OnClose(isShutdown, userData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnAdvanceClicked();
    }

    private void ShowCurrentRow()
    {
        if (m_RowIndex < 0 || m_RowIndex >= m_Rows.Count)
            throw new InvalidOperationException($"Story row index {m_RowIndex} is outside script '{m_ScriptId}'.");

        StoryScriptTable row = m_Rows[m_RowIndex];
        m_VisualVersion++;
        ApplyBackground(row.BgSpritePath, m_VisualVersion);
        ApplyPortrait(m_PortraitLeft, row.PortraitLeft, m_VisualVersion);
        ApplyPortrait(m_PortraitCenter, row.PortraitCenter, m_VisualVersion);
        ApplyPortrait(m_PortraitRight, row.PortraitRight, m_VisualVersion);

        string speaker = LocalizeOptional(row.SpeakerKey);
        string text = ResolveText(row);
        m_History.Add(new HistoryEntry(speaker, text, row.Style, row.Order));
        RefreshHistory();

        m_NormalPanel.SetActive(row.Style == StoryStyle.Normal);
        m_TerminalPanel.SetActive(row.Style == StoryStyle.Terminal);
        m_DocumentPanel.SetActive(row.Style == StoryStyle.Document);

        switch (row.Style)
        {
            case StoryStyle.Normal:
                m_NormalSpeaker.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
                m_NormalSpeaker.text = speaker;
                SetActiveText(m_NormalText, text);
                break;
            case StoryStyle.Terminal:
                SetActiveText(m_TerminalText, $"<mspace=1em>{text}</mspace>");
                break;
            case StoryStyle.Document:
                SetActiveText(m_DocumentText, $"{row.Order:00} | {text}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row.Style), row.Style, "Unsupported story style.");
        }

        m_ProgressText.text = $"{m_RowIndex + 1:00}/{m_Rows.Count:00}";
        ApplyEntryEffect(row.Effect);
    }

    private string ResolveText(StoryScriptTable row)
    {
        if (row.DynamicRef != StoryDynamicRef.None)
            throw new NotSupportedException($"Story dynamic reference '{row.DynamicRef}' is not implemented yet.");
        return LocalizeRequired(row.TextKey);
    }

    private void SetActiveText(TMP_Text target, string text)
    {
        m_ActiveText = target;
        target.text = text;
        target.maxVisibleCharacters = 0;
        target.ForceMeshUpdate();
        m_TotalCharacters = target.textInfo.characterCount;
        m_VisibleCharacters = 0f;
        m_AutoTimer = AutoAdvanceDelay;
        m_IsLineComplete = m_TotalCharacters == 0;
        if (m_IsLineComplete || m_FastForward)
            CompleteCurrentLine();
    }

    private void CompleteCurrentLine()
    {
        m_ActiveText.maxVisibleCharacters = int.MaxValue;
        m_VisibleCharacters = m_TotalCharacters;
        m_IsLineComplete = true;
        m_AutoTimer = m_FastForward ? 0.08f : AutoAdvanceDelay;
    }

    private void OnAdvanceClicked()
    {
        if (m_IsTransitioning)
            return;
        if (m_HistoryPanel.activeSelf)
        {
            m_HistoryPanel.SetActive(false);
            return;
        }
        if (!m_IsLineComplete)
        {
            CompleteCurrentLine();
            return;
        }
        AdvanceToNextRow();
    }

    private void AdvanceToNextRow()
    {
        if (m_IsTransitioning)
            return;

        StoryScriptTable current = m_Rows[m_RowIndex];
        Action advance = () =>
        {
            m_RowIndex++;
            if (m_RowIndex >= m_Rows.Count)
            {
                StoryManager.RequestComplete(m_ScriptId);
                return;
            }
            m_IsTransitioning = false;
            ShowCurrentRow();
        };

        if (string.Equals(current.Effect, "FadeOut", StringComparison.Ordinal))
        {
            m_IsTransitioning = true;
            m_RootGroup.DOFade(0f, 0.3f).SetUpdate(true).OnComplete(() =>
            {
                m_RootGroup.alpha = 1f;
                advance();
            });
        }
        else
        {
            advance();
        }
    }

    private void OnSkipClicked()
    {
        if (!m_IsTransitioning)
            StoryManager.RequestComplete(m_ScriptId);
    }

    private void OnAutoClicked()
    {
        m_AutoPlay = !m_AutoPlay;
        m_AutoTimer = AutoAdvanceDelay;
        RefreshControls();
    }

    private void OnFastClicked()
    {
        if (!m_FastButton.interactable)
            return;
        m_FastForward = !m_FastForward;
        if (m_FastForward && !m_IsLineComplete)
            CompleteCurrentLine();
        RefreshControls();
    }

    private void OnSpeedClicked()
    {
        m_SpeedIndex = (m_SpeedIndex + 1) % m_Speeds.Length;
        RefreshControls();
    }

    private void OnHistoryClicked()
    {
        bool show = !m_HistoryPanel.activeSelf;
        m_HistoryPanel.SetActive(show);
        if (show)
            RefreshHistory();
    }

    private void RefreshControls()
    {
        m_AutoButtonText.text = m_AutoPlay ? "AUTO ON" : "AUTO";
        m_AutoButtonText.color = m_AutoPlay ? new Color(0.35f, 0.9f, 0.75f) : Color.white;
        m_FastButtonText.text = m_FastForward ? ">> ON" : ">>";
        m_FastButtonText.color = m_FastForward ? new Color(1f, 0.75f, 0.25f) : Color.white;
        m_SpeedButtonText.text = $"{m_Speeds[m_SpeedIndex]:0.##}x";
    }

    private void RefreshHistory()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < m_History.Count; i++)
        {
            HistoryEntry entry = m_History[i];
            if (i > 0)
                builder.Append("\n\n");
            builder.Append(entry.Order.ToString("00"));
            if (!string.IsNullOrEmpty(entry.Speaker))
                builder.Append("  ").Append(entry.Speaker);
            builder.Append('\n');
            if (entry.Style == StoryStyle.Terminal)
                builder.Append("<mspace=1em>").Append(entry.Text).Append("</mspace>");
            else
                builder.Append(entry.Text);
        }
        m_HistoryText.text = builder.ToString();
        Canvas.ForceUpdateCanvases();
        float viewportHeight = ((RectTransform)m_HistoryContent.parent).rect.height;
        float contentHeight = Mathf.Max(viewportHeight, m_HistoryText.preferredHeight);
        m_HistoryContent.sizeDelta = new Vector2(0f, contentHeight);
        ((RectTransform)m_HistoryText.transform).sizeDelta = new Vector2(0f, contentHeight);
    }

    private void ApplyEntryEffect(string effect)
    {
        if (string.IsNullOrWhiteSpace(effect) || string.Equals(effect, "FadeOut", StringComparison.Ordinal))
            return;
        if (string.Equals(effect, "ShakeScreen", StringComparison.Ordinal))
        {
            m_ContentRoot.DOShakeAnchorPos(0.35f, 14f, 18, 55f, false, true).SetUpdate(true);
            return;
        }
        if (string.Equals(effect, "Glitch", StringComparison.Ordinal))
        {
            m_ContentRoot.DOShakeAnchorPos(0.18f, 5f, 24, 80f, false, true).SetUpdate(true);
            m_GlitchFlash.color = new Color(0.2f, 0.9f, 0.85f, 0f);
            m_GlitchFlash.DOFade(0.2f, 0.04f).SetLoops(4, LoopType.Yoyo).SetUpdate(true);
            return;
        }
        throw new NotSupportedException($"Story effect '{effect}' is not supported.");
    }

    private void ApplyBackground(string spritePath, int version)
    {
        if (string.IsNullOrWhiteSpace(spritePath))
            return;
        string assetPath = UtilityBuiltin.AssetsPath.GetSpritesPath(spritePath);
        GF.UI.LoadSprite(assetPath, sprite =>
        {
            if (version != m_VisualVersion)
                return;
            if (sprite == null)
                throw new InvalidOperationException($"Story background sprite failed to load: {spritePath}");
            m_Background.sprite = sprite;
            m_Background.color = new Color(1f, 1f, 1f, 0f);
            m_Background.DOFade(1f, 0.25f).SetUpdate(true);
        });
    }

    private void ApplyPortrait(Image image, string spritePath, int version)
    {
        if (string.IsNullOrWhiteSpace(spritePath) || spritePath == "-")
        {
            image.sprite = null;
            image.gameObject.SetActive(false);
            return;
        }
        string assetPath = UtilityBuiltin.AssetsPath.GetSpritesPath(spritePath);
        GF.UI.LoadSprite(assetPath, sprite =>
        {
            if (version != m_VisualVersion)
                return;
            if (sprite == null)
                throw new InvalidOperationException($"Story portrait sprite failed to load: {spritePath}");
            image.sprite = sprite;
            image.preserveAspect = true;
            image.gameObject.SetActive(true);
            image.color = new Color(1f, 1f, 1f, 0f);
            image.DOFade(1f, 0.2f).SetUpdate(true);
        });
    }

    private static string LocalizeRequired(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Story localization key is empty.");
        string text = LocalizationTextManager.GetLocalizedText(key);
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, key, StringComparison.Ordinal))
            throw new InvalidOperationException($"Story localization key is missing: {key}");
        return text;
    }

    private static string LocalizeOptional(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : LocalizeRequired(key);
    }

    private void BuildView()
    {
        m_RootGroup = GetComponent<CanvasGroup>();
        m_ContentRoot = CreateRect("StoryContent", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        m_Background = CreateImage("Background", m_ContentRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.025f, 0.03f, 0.035f, 1f));
        CreateImage("Vignette", m_ContentRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.34f));
        m_PortraitLeft = CreatePortrait("PortraitLeft", new Vector2(0f, 0.08f), new Vector2(0.42f, 0.96f));
        m_PortraitCenter = CreatePortrait("PortraitCenter", new Vector2(0.29f, 0.08f), new Vector2(0.71f, 0.96f));
        m_PortraitRight = CreatePortrait("PortraitRight", new Vector2(0.58f, 0.08f), new Vector2(1f, 0.96f));

        BuildNormalPanel();
        BuildTerminalPanel();
        BuildDocumentPanel();
        BuildHistoryPanel();

        BuildToolbar();
        m_GlitchFlash = CreateImage("GlitchFlash", m_ContentRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.2f, 0.9f, 0.85f, 0f));
        m_GlitchFlash.raycastTarget = false;
    }

    private void BuildNormalPanel()
    {
        Image panel = CreateImage("NormalPanel", m_ContentRoot, new Vector2(0.055f, 0.045f), new Vector2(0.945f, 0.34f), Vector2.zero, Vector2.zero, new Color(0.025f, 0.03f, 0.035f, 0.93f));
        m_NormalPanel = panel.gameObject;
        m_NormalSpeaker = CreateText("Speaker", panel.rectTransform, new Vector2(0.035f, 0.7f), new Vector2(0.35f, 0.94f), Vector2.zero, Vector2.zero, 27f, new Color(0.35f, 0.9f, 0.75f));
        m_NormalSpeaker.fontStyle = FontStyles.Bold;
        m_NormalText = CreateText("Text", panel.rectTransform, new Vector2(0.035f, 0.12f), new Vector2(0.94f, 0.71f), Vector2.zero, Vector2.zero, 31f, new Color(0.94f, 0.95f, 0.93f));
    }

    private void BuildTerminalPanel()
    {
        Image panel = CreateImage("TerminalPanel", m_ContentRoot, new Vector2(0.075f, 0.1f), new Vector2(0.925f, 0.9f), Vector2.zero, Vector2.zero, new Color(0.015f, 0.045f, 0.04f, 0.96f));
        m_TerminalPanel = panel.gameObject;
        Image header = CreateImage("Header", panel.rectTransform, new Vector2(0f, 0.91f), Vector2.one, Vector2.zero, Vector2.zero, new Color(0.12f, 0.7f, 0.58f, 0.75f));
        TMP_Text marker = CreateText("Marker", header.rectTransform, new Vector2(0.025f, 0f), new Vector2(0.8f, 1f), Vector2.zero, Vector2.zero, 20f, new Color(0.02f, 0.08f, 0.065f));
        marker.text = "INSTITUTE FIELD TERMINAL / SECURE CHANNEL";
        marker.fontStyle = FontStyles.Bold;
        m_TerminalText = CreateText("TerminalText", panel.rectTransform, new Vector2(0.045f, 0.12f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero, 29f, new Color(0.45f, 1f, 0.72f));
        m_TerminalText.characterSpacing = 2f;
    }

    private void BuildDocumentPanel()
    {
        Image panel = CreateImage("DocumentPanel", m_ContentRoot, new Vector2(0.16f, 0.075f), new Vector2(0.84f, 0.925f), Vector2.zero, Vector2.zero, new Color(0.87f, 0.86f, 0.79f, 0.98f));
        m_DocumentPanel = panel.gameObject;
        CreateImage("Margin", panel.rectTransform, new Vector2(0.1f, 0.04f), new Vector2(0.104f, 0.96f), Vector2.zero, Vector2.zero, new Color(0.55f, 0.16f, 0.14f, 0.55f));
        m_DocumentText = CreateText("DocumentText", panel.rectTransform, new Vector2(0.14f, 0.1f), new Vector2(0.91f, 0.9f), Vector2.zero, Vector2.zero, 29f, new Color(0.09f, 0.085f, 0.07f));
        m_DocumentText.characterSpacing = 1.5f;
    }

    private void BuildHistoryPanel()
    {
        Image panel = CreateImage("HistoryPanel", m_ContentRoot, new Vector2(0.22f, 0.06f), new Vector2(0.95f, 0.91f), Vector2.zero, Vector2.zero, new Color(0.02f, 0.025f, 0.03f, 1f));
        m_HistoryPanel = panel.gameObject;
        Image viewport = CreateImage("Viewport", panel.rectTransform, new Vector2(0.045f, 0.06f), new Vector2(0.955f, 0.94f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0f));
        viewport.gameObject.AddComponent<RectMask2D>();
        m_HistoryContent = CreateRect("Content", viewport.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        m_HistoryContent.pivot = new Vector2(0.5f, 1f);
        m_HistoryText = CreateText("HistoryText", m_HistoryContent, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, 24f, new Color(0.84f, 0.86f, 0.83f));
        ((RectTransform)m_HistoryText.transform).pivot = new Vector2(0.5f, 1f);
        m_HistoryText.enableWordWrapping = true;
        ScrollRect scroll = panel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport.rectTransform;
        scroll.content = m_HistoryContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
    }

    private void BuildToolbar()
    {
        RectTransform toolbar = CreateRect("Toolbar", m_ContentRoot, new Vector2(0.52f, 0.91f), new Vector2(0.97f, 0.985f), Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup layout = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.padding = new RectOffset(0, 0, 0, 0);

        m_ProgressText = CreateToolbarLabel("Progress", toolbar, "00/00");
        CreateToolbarButton("History", toolbar, "LOG", OnHistoryClicked, out _);
        CreateToolbarButton("Speed", toolbar, "1x", OnSpeedClicked, out m_SpeedButtonText);
        m_FastButton = CreateToolbarButton("Fast", toolbar, ">>", OnFastClicked, out m_FastButtonText);
        CreateToolbarButton("Auto", toolbar, "AUTO", OnAutoClicked, out m_AutoButtonText);
        CreateToolbarButton("Skip", toolbar, ">|", OnSkipClicked, out _);
    }

    private Image CreatePortrait(string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        Image image = CreateImage(name, m_ContentRoot, anchorMin, anchorMax, Vector2.zero, Vector2.zero, Color.white);
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.gameObject.SetActive(false);
        return image;
    }

    private static Button CreateToolbarButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction action, out TMP_Text text)
    {
        Image image = CreateImage(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.08f, 0.1f, 0.11f, 0.94f));
        LayoutElement element = image.gameObject.AddComponent<LayoutElement>();
        element.minWidth = 70f;
        element.preferredWidth = 82f;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        text = CreateText("Label", image.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 18f, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        text.text = label;
        return button;
    }

    private static TMP_Text CreateToolbarLabel(string name, Transform parent, string value)
    {
        TMP_Text text = CreateText(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 18f, new Color(0.7f, 0.74f, 0.72f));
        text.alignment = TextAlignmentOptions.Center;
        text.text = value;
        LayoutElement element = text.gameObject.AddComponent<LayoutElement>();
        element.minWidth = 74f;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float fontSize, Color color)
    {
        RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.richText = true;
        return text;
    }

    private readonly struct HistoryEntry
    {
        public HistoryEntry(string speaker, string text, StoryStyle style, int order)
        {
            Speaker = speaker;
            Text = text;
            Style = style;
            Order = order;
        }

        public string Speaker { get; }
        public string Text { get; }
        public StoryStyle Style { get; }
        public int Order { get; }
    }
}
