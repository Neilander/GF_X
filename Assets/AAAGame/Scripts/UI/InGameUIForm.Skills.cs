using GameFramework.Event;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class InGameUIForm
{
    private bool m_LastSkillCasting;
    private RectTransform m_SkillTooltipRoot;
    private TextMeshProUGUI m_SkillTooltipText;
    private int m_HoveredSkillSlot = -1;

    private void InitializeSkills()
    {
        BindSkillInputProxies();
        GF.Event.Subscribe(SkillChangedEventArgs.EventId, OnSkillChanged);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnSkillPhaseChanged);
        SkillCastPresentationService.Changed += OnSkillCastStateChanged;
        m_LastSkillCasting = SkillCastState.IsCasting;
        RefreshSkills();
    }

    private void ShutdownSkills()
    {
        SkillCastPresentationService.Cancel();
        HideSkillPreview(-1);
        GF.Event.Unsubscribe(SkillChangedEventArgs.EventId, OnSkillChanged);
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnSkillPhaseChanged);
        SkillCastPresentationService.Changed -= OnSkillCastStateChanged;
        HideAllSkillSlots();
    }

    private void OnSkillChanged(object sender, GameEventArgs e)
    {
        RefreshSkills();
    }

    private void OnSkillPhaseChanged(object sender, GameEventArgs e)
    {
        SkillCastPresentationService.Cancel();
        RefreshSkills();
    }

    private void OnSkillCastStateChanged()
    {
        RefreshSkills();
    }

    private void TickSkillPresentation()
    {
        bool isCasting = SkillCastState.IsCasting;
        if (isCasting != m_LastSkillCasting)
        {
            m_LastSkillCasting = isCasting;
            RefreshSkills();
        }
        TickSkillCooldownPresentation();
    }

    private void RefreshSkills()
    {
        if (varSkills == null)
            return;

        HideAllSkillSlots();

        IReadOnlyList<SkillRuntimeInfo> skills = SkillRuntimeDataModel.GetUnlockedSkills();
        int count = Mathf.Min(varSkills.Length, skills.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject slot = varSkills[i];
            if (slot == null)
                continue;

            SkillData skillData = skills[i].Data;
            if (skillData == null)
                throw new InvalidOperationException($"Skill slot has null SkillData. index={i}");

            slot.SetActive(true);
            SetSkillSlotName(slot, skills[i], i);
            SetSkillSlotCounter(slot, skills[i]);
            EnsureSkillCooldownVisual(slot);

            Button button = slot.GetComponent<Button>();
            if (button == null)
                button = slot.AddComponent<Button>();

            bool canClick = skillData.Type == SkillType.Active
                            && i < PlayerSkillComp.SKILL_NUM
                            && skills[i].RemainingUsageCount > 0
                            && SkillInputRuntime.CanUseActiveSkillsInCurrentPhase()
                            && !SkillCastState.IsCasting
                            && !SkillCastPresentationService.IsAiming
                            && SkillCastPresentationService.CanRequestSkillCast(i);
            button.interactable = canClick;
        }
    }

    private void TickSkillCooldownPresentation()
    {
        if (varSkills == null || EntityRegistry.Player is not ISkillCompHost host)
            return;
        if (host.skillComp is not ISkillCooldownPresentationProvider provider)
            throw new InvalidOperationException("Player skill component does not provide cooldown presentation state.");

        IReadOnlyList<SkillRuntimeInfo> skills = SkillRuntimeDataModel.GetUnlockedSkills();
        int count = Mathf.Min(varSkills.Length, skills.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject slot = varSkills[i];
            if (slot == null || !slot.activeInHierarchy)
                continue;

            Fix64 remaining = Fix64.Zero;
            Fix64 total = Fix64.Zero;
            bool active = false;
            if (skills[i].Data.Type == SkillType.Active)
            {
                bool hasCooldown = provider.TryGetSkillCooldownPresentation(i, out remaining, out total);
                active = hasCooldown && remaining > Fix64.Zero;
            }
            SetSkillCooldownVisual(slot, active, remaining, total);
        }
    }

    private static void EnsureSkillCooldownVisual(GameObject slot)
    {
        Transform existing = slot.transform.Find("SkillCooldownFill");
        if (existing != null)
            return;

        Image slotImage = slot.GetComponent<Image>()
                          ?? throw new InvalidOperationException($"Skill slot is missing Image. slot={slot.name}");
        if (slotImage.sprite == null)
            throw new InvalidOperationException($"Skill slot Image has no Sprite for cooldown fill. slot={slot.name}");

        GameObject fillObject = new GameObject("SkillCooldownFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.layer = slot.layer;
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.SetParent(slot.transform, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillObject.GetComponent<Image>();
        fill.sprite = slotImage.sprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = false;
        fill.color = new Color(0.02f, 0.03f, 0.04f, 0.78f);
        fill.raycastTarget = false;

        GameObject textObject = new GameObject("SkillCooldownText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = slot.layer;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(slot.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI main = GetSkillMainText(slot);
        text.font = main.font;
        text.fontSize = 28f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;

        fillObject.SetActive(false);
        textObject.SetActive(false);
    }

    internal static void SetSkillCooldownVisual(GameObject slot, bool active, Fix64 remaining, Fix64 total)
    {
        if (slot == null)
            throw new ArgumentNullException(nameof(slot));
        EnsureSkillCooldownVisual(slot);
        Image fill = slot.transform.Find("SkillCooldownFill").GetComponent<Image>();
        TextMeshProUGUI text = slot.transform.Find("SkillCooldownText").GetComponent<TextMeshProUGUI>();
        if (!active)
        {
            fill.gameObject.SetActive(false);
            text.gameObject.SetActive(false);
            return;
        }
        if (remaining <= Fix64.Zero || total <= Fix64.Zero || remaining > total)
            throw new ArgumentOutOfRangeException(nameof(remaining), "Active cooldown requires 0 < remaining <= total.");

        fill.fillAmount = Mathf.Clamp01((float)(remaining / total));
        text.text = ((long)Fix64.Ceiling(remaining)).ToString();
        fill.gameObject.SetActive(true);
        text.gameObject.SetActive(true);
        fill.transform.SetAsLastSibling();
        text.transform.SetAsLastSibling();
    }

    private void HideAllSkillSlots()
    {
        if (varSkills == null)
            return;

        for (int i = 0; i < varSkills.Length; i++)
        {
            if (varSkills[i] != null)
                varSkills[i].SetActive(false);
        }
    }

    private void SetSkillSlotName(GameObject slot, SkillRuntimeInfo skillInfo, int slotIndex)
    {
        SkillData skillData = skillInfo.Data;
        TextMeshProUGUI text = GetSkillMainText(slot);
        if (text == null)
            throw new InvalidOperationException($"Skill slot is missing child TextMeshProUGUI. slot={slot.name}, skillId={skillData.Identifier}");

        string skillName = LocalizationTextManager.GetLocalizedText(skillData.NameKey, false);
        string displayName = LocalizationTextManager.ProcessText(skillName);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 6f);
        textRect.offsetMax = new Vector2(-6f, -6f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10f;
        text.fontSizeMax = 18f;
        text.overflowMode = TextOverflowModes.Truncate;
        text.text = skillData.Type == SkillType.Active
            ? $"{SkillInputRuntime.GetKeyLabel(slotIndex)}\n{displayName}"
            : displayName;
    }

    private void BindSkillInputProxies()
    {
        if (varSkills == null)
            return;

        for (int i = 0; i < varSkills.Length; i++)
        {
            if (varSkills[i] == null)
                continue;

            Button button = varSkills[i].GetComponent<Button>();
            if (button == null)
                button = varSkills[i].AddComponent<Button>();

            int slotIndex = i;
            SkillSlotInputProxy proxy = varSkills[i].GetComponent<SkillSlotInputProxy>();
            if (proxy == null)
                proxy = varSkills[i].AddComponent<SkillSlotInputProxy>();

            proxy.Initialize(
                slotIndex,
                () => ResolveActiveSkillInputIndex(slotIndex),
                () => ResolveSkillSlotIndex(slotIndex),
                ShowSkillPreview,
                HideSkillPreview);
        }
    }

    private int ResolveActiveSkillInputIndex(int slotIndex)
    {
        IReadOnlyList<SkillRuntimeInfo> skills = SkillRuntimeDataModel.GetUnlockedSkills();
        if (slotIndex < 0 || slotIndex >= skills.Count)
            return -1;

        if (!SkillInputRuntime.CanUseActiveSkillsInCurrentPhase()
            || SkillCastState.IsCasting
            || SkillCastPresentationService.IsAiming)
            return -1;

        return skills[slotIndex].Data.Type == SkillType.Active && skills[slotIndex].RemainingUsageCount > 0 ? slotIndex : -1;
    }

    private int ResolveSkillSlotIndex(int slotIndex)
    {
        IReadOnlyList<SkillRuntimeInfo> skills = SkillRuntimeDataModel.GetUnlockedSkills();
        if (slotIndex < 0 || slotIndex >= skills.Count)
            return -1;

        return slotIndex;
    }

    private static TextMeshProUGUI GetSkillMainText(GameObject slot)
    {
        TextMeshProUGUI[] texts = slot.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            string objectName = texts[i].gameObject.name;
            if (objectName != "SkillCounter" && objectName != "SkillCooldownText")
                return texts[i];
        }

        throw new InvalidOperationException($"Skill slot is missing its main TextMeshProUGUI. slot={slot.name}");
    }

    private static void SetSkillSlotCounter(GameObject slot, SkillRuntimeInfo skillInfo)
    {
        Transform existing = slot.transform.Find("SkillCounter");
        TextMeshProUGUI counter;
        if (existing != null)
        {
            counter = existing.GetComponent<TextMeshProUGUI>()
                      ?? throw new InvalidOperationException($"SkillCounter is missing TextMeshProUGUI. slot={slot.name}");
        }
        else
        {
            GameObject counterObject = new GameObject("SkillCounter", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            counterObject.layer = slot.layer;
            RectTransform rect = counterObject.GetComponent<RectTransform>();
            rect.SetParent(slot.transform, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-5f, 4f);
            rect.sizeDelta = new Vector2(56f, 24f);
            counter = counterObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI main = GetSkillMainText(slot);
            counter.font = main.font;
            counter.fontSize = 16f;
            counter.alignment = TextAlignmentOptions.BottomRight;
            counter.color = Color.white;
            counter.raycastTarget = false;
        }

        SkillData skill = skillInfo.Data;
        if (skill.Type == SkillType.Active)
        {
            counter.text = skillInfo.RemainingUsageCount.ToString();
        }
        else if (skill.Identifier == "Skill_ForgedInFire")
        {
            counter.text = skillInfo.StackCount + "/" + skill.GetUniqueValue(1, skillInfo.Level);
        }
        else
        {
            counter.text = string.Empty;
        }
    }

    private void ShowSkillPreview(int slotIndex)
    {
        IReadOnlyList<SkillRuntimeInfo> skills = SkillRuntimeDataModel.GetUnlockedSkills();
        if (slotIndex < 0 || slotIndex >= skills.Count || varSkills == null || slotIndex >= varSkills.Length)
            return;

        EnsureSkillTooltip();
        m_HoveredSkillSlot = slotIndex;
        m_SkillTooltipText.text = BuildingPanelPresentation.GetSkillTooltip(skills[slotIndex]);
        m_SkillTooltipRoot.gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        float height = Mathf.Clamp(m_SkillTooltipText.preferredHeight + 24f, 96f, 240f);
        m_SkillTooltipRoot.sizeDelta = new Vector2(390f, height);

        RectTransform slotRect = varSkills[slotIndex].transform as RectTransform
                                 ?? throw new InvalidOperationException($"Skill slot has no RectTransform. index={slotIndex}");
        RectTransform parentRect = m_SkillTooltipRoot.parent as RectTransform
                                   ?? throw new InvalidOperationException("Skill tooltip parent has no RectTransform.");
        Canvas canvas = GetComponentInParent<Canvas>()
                        ?? throw new InvalidOperationException("InGameUIForm requires a Canvas for skill preview.");
        Vector3[] corners = new Vector3[4];
        slotRect.GetWorldCorners(corners);
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[1]);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, canvas.worldCamera, out Vector2 localPoint))
            throw new InvalidOperationException("Could not position the skill tooltip in its canvas.");

        Vector2 desiredLocal = localPoint + new Vector2(-10f, 8f);
        Rect parentBounds = parentRect.rect;
        Vector2 tooltipSize = m_SkillTooltipRoot.rect.size;
        Vector2 tooltipPivot = m_SkillTooltipRoot.pivot;
        desiredLocal.x = Mathf.Clamp(
            desiredLocal.x,
            parentBounds.xMin + tooltipSize.x * tooltipPivot.x,
            parentBounds.xMax - tooltipSize.x * (1f - tooltipPivot.x));
        desiredLocal.y = Mathf.Clamp(
            desiredLocal.y,
            parentBounds.yMin + tooltipSize.y * tooltipPivot.y,
            parentBounds.yMax - tooltipSize.y * (1f - tooltipPivot.y));
        Vector2 anchorReference = new Vector2(
            Mathf.Lerp(parentBounds.xMin, parentBounds.xMax, m_SkillTooltipRoot.anchorMin.x),
            Mathf.Lerp(parentBounds.yMin, parentBounds.yMax, m_SkillTooltipRoot.anchorMin.y));
        m_SkillTooltipRoot.anchoredPosition = desiredLocal - anchorReference;
    }

    private void HideSkillPreview(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex != m_HoveredSkillSlot)
            return;
        m_HoveredSkillSlot = -1;
        if (m_SkillTooltipRoot != null)
            m_SkillTooltipRoot.gameObject.SetActive(false);
    }

    private void EnsureSkillTooltip()
    {
        if (m_SkillTooltipRoot != null)
            return;

        RectTransform parentRect = transform as RectTransform
                                   ?? throw new InvalidOperationException("InGameUIForm has no RectTransform.");
        GameObject root = new GameObject("SkillTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.layer = gameObject.layer;
        m_SkillTooltipRoot = root.GetComponent<RectTransform>();
        m_SkillTooltipRoot.SetParent(parentRect, false);
        m_SkillTooltipRoot.anchorMin = new Vector2(0.5f, 0.5f);
        m_SkillTooltipRoot.anchorMax = new Vector2(0.5f, 0.5f);
        m_SkillTooltipRoot.pivot = new Vector2(1f, 0f);
        Image background = root.GetComponent<Image>();
        background.color = new Color(0.07f, 0.08f, 0.09f, 0.96f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(m_SkillTooltipRoot, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 10f);
        textRect.offsetMax = new Vector2(-12f, -10f);
        m_SkillTooltipText = textObject.GetComponent<TextMeshProUGUI>();
        m_SkillTooltipText.font = GetSkillMainText(varSkills[0]).font;
        m_SkillTooltipText.fontSize = 19f;
        m_SkillTooltipText.alignment = TextAlignmentOptions.TopLeft;
        m_SkillTooltipText.enableWordWrapping = true;
        m_SkillTooltipText.overflowMode = TextOverflowModes.Overflow;
        m_SkillTooltipText.color = Color.white;
        m_SkillTooltipText.raycastTarget = false;
        root.SetActive(false);
    }
}
