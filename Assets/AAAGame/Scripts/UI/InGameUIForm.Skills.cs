using GameFramework.Event;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class InGameUIForm
{
    private void InitializeSkills()
    {
        BindSkillInputProxies();
        GF.Event.Subscribe(SkillChangedEventArgs.EventId, OnSkillChanged);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnSkillPhaseChanged);
        SkillCastState.Changed += OnSkillCastStateChanged;
        RefreshSkills();
    }

    private void ShutdownSkills()
    {
        GF.Event.Unsubscribe(SkillChangedEventArgs.EventId, OnSkillChanged);
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnSkillPhaseChanged);
        SkillCastState.Changed -= OnSkillCastStateChanged;
        HideAllSkillSlots();
    }

    private void OnSkillChanged(object sender, GameEventArgs e)
    {
        RefreshSkills();
    }

    private void OnSkillPhaseChanged(object sender, GameEventArgs e)
    {
        RefreshSkills();
    }

    private void OnSkillCastStateChanged()
    {
        RefreshSkills();
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

            Button button = slot.GetComponent<Button>();
            if (button == null)
                button = slot.AddComponent<Button>();

            bool canClick = skillData.Type == SkillType.Active
                            && i < PlayerSkillComp.SKILL_NUM
                            && skills[i].RemainingUsageCount > 0
                            && SkillInputRuntime.CanUseActiveSkillsInCurrentPhase()
                            && !SkillCastState.IsCasting;
            button.interactable = canClick;
        }
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
        TextMeshProUGUI text = slot.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null)
            throw new InvalidOperationException($"Skill slot is missing child TextMeshProUGUI. slot={slot.name}, skillId={skillData.Identifier}");

        string skillName = LocalizationTextManager.GetLocalizedText(skillData.NameKey, false);
        string levelText = $"Lv{skillInfo.Level}";
        string usageText = skillData.Type == SkillType.Active
            ? $" {skillInfo.RemainingUsageCount}/{skillInfo.MaxUsageCount}"
            : string.Empty;
        text.text = slotIndex >= 0 && slotIndex < SkillInputRuntime.MaxSkillCount
            ? $"{SkillInputRuntime.GetKeyLabel(slotIndex)} {skillName} {levelText}{usageText}"
            : $"{skillName} {levelText}{usageText}";
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

            proxy.Initialize(slotIndex, () => ResolveActiveSkillInputIndex(slotIndex), () => ResolveSkillSlotIndex(slotIndex));
        }
    }

    private int ResolveActiveSkillInputIndex(int slotIndex)
    {
        IReadOnlyList<SkillRuntimeInfo> skills = SkillRuntimeDataModel.GetUnlockedSkills();
        if (slotIndex < 0 || slotIndex >= skills.Count)
            return -1;

        if (!SkillInputRuntime.CanUseActiveSkillsInCurrentPhase() || SkillCastState.IsCasting)
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
}
