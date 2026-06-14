using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

/// <summary>
/// 当前关卡内玩家已获得的技能与等级。
/// </summary>
public class SkillRuntimeDataModel : DataModelBase
{
    private readonly Dictionary<string, int> m_SkillLevels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_SkillRemainingUsageCounts = new(StringComparer.Ordinal);
    private readonly List<string> m_UnlockOrder = new();

    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        ResetSkills();
    }

    protected override void OnRelease()
    {
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
        ResetSkills();
        base.OnRelease();
    }

    public static bool LearnOrUpgradeFromTech(TechData techData)
    {
        if (techData == null)
            throw new ArgumentNullException(nameof(techData));

        if (techData.ScopeType != TechScopeType.Skill)
            return false;

        if (string.IsNullOrWhiteSpace(techData.SkillID))
            throw new InvalidOperationException($"Skill tech has empty SkillID. techId={techData.Identifier}");

        SkillData skillData = SkillDataModel.GetSkillData(techData.SkillID);
        if (skillData == null)
            throw new InvalidOperationException($"SkillData not found. skillId={techData.SkillID}, techId={techData.Identifier}");

        var dm = GetRequiredModel();
        int newLevel = dm.AddLevelInternal(skillData);
        GF.Event.Fire(dm, SkillChangedEventArgs.Create(skillData.Identifier, newLevel));
        return true;
    }

    public static bool IsUnlocked(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return false;

        var dm = GetModel();
        return dm != null && dm.m_SkillLevels.ContainsKey(skillId);
    }

    public static int GetLevel(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return 0;

        var dm = GetModel();
        if (dm == null || !dm.m_SkillLevels.TryGetValue(skillId, out int level) || level <= 0)
            return 0;

        return GetEffectiveLevel(level);
    }

    public static IReadOnlyList<SkillRuntimeInfo> GetUnlockedSkills()
    {
        var dm = GetModel();
        if (dm == null)
            return Array.Empty<SkillRuntimeInfo>();

        List<SkillRuntimeInfo> results = new(dm.m_UnlockOrder.Count);
        for (int i = 0; i < dm.m_UnlockOrder.Count; i++)
        {
            string skillId = dm.m_UnlockOrder[i];
            if (string.IsNullOrWhiteSpace(skillId))
                continue;

            if (!dm.m_SkillLevels.TryGetValue(skillId, out int level))
                continue;

            SkillData skillData = SkillDataModel.GetSkillData(skillId);
            if (skillData == null)
                throw new InvalidOperationException($"Unlocked SkillData not found. skillId={skillId}");

            int effectiveLevel = GetEffectiveLevel(level);
            int maxUsageCount = GetMaxUsageCount(skillData, effectiveLevel);
            int remainingUsageCount = dm.m_SkillRemainingUsageCounts.TryGetValue(skillId, out int remaining)
                ? remaining
                : 0;
            results.Add(new SkillRuntimeInfo(skillData, effectiveLevel, remainingUsageCount, maxUsageCount));
        }

        return results;
    }

    public static void SwapSkillSlots(int fromIndex, int toIndex)
    {
        var dm = GetRequiredModel();
        dm.SwapSkillSlotsInternal(fromIndex, toIndex);
    }

    public static bool IsUnlockedActiveSkillSlot(int slotIndex)
    {
        IReadOnlyList<SkillRuntimeInfo> skills = GetUnlockedSkills();
        if (slotIndex < 0 || slotIndex >= skills.Count)
            return false;

        return skills[slotIndex].Data != null && skills[slotIndex].Data.Type == SkillType.Active;
    }

    public static bool HasRemainingUsageAt(int slotIndex)
    {
        SkillRuntimeInfo skillInfo = GetUnlockedSkillAt(slotIndex);
        return skillInfo.Data != null
               && skillInfo.Data.Type == SkillType.Active
               && skillInfo.RemainingUsageCount > 0;
    }

    public static SkillRuntimeInfo GetUnlockedSkillAt(int slotIndex)
    {
        IReadOnlyList<SkillRuntimeInfo> skills = GetUnlockedSkills();
        if (slotIndex < 0 || slotIndex >= skills.Count)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");

        return skills[slotIndex];
    }

    public static void ConsumeUsageAt(int slotIndex)
    {
        var dm = GetRequiredModel();
        dm.ConsumeUsageAtInternal(slotIndex);
    }

    private int AddLevelInternal(SkillData skillData)
    {
        if (skillData == null)
            throw new ArgumentNullException(nameof(skillData));

        string skillId = skillData.Identifier;
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("skillId is required.", nameof(skillId));

        if (!m_SkillLevels.TryGetValue(skillId, out int level))
        {
            if (m_UnlockOrder.Count >= SkillInputRuntime.MaxSkillCount)
                throw new InvalidOperationException($"Cannot unlock more than {SkillInputRuntime.MaxSkillCount} skills. skillId={skillId}");

            m_UnlockOrder.Add(skillId);
            m_SkillLevels[skillId] = 1;
            SetInitialUsageCount(skillData, 1);
            return 1;
        }

        int oldMaxUsageCount = GetMaxUsageCount(skillData, level);
        level++;
        m_SkillLevels[skillId] = level;
        AddUpgradeUsageCount(skillData, level, oldMaxUsageCount);
        return level;
    }

    private void SetInitialUsageCount(SkillData skillData, int level)
    {
        if (skillData.Type != SkillType.Active)
            return;

        int maxUsageCount = GetRequiredMaxUsageCount(skillData, level);
        m_SkillRemainingUsageCounts[skillData.Identifier] = maxUsageCount;
    }

    private void AddUpgradeUsageCount(SkillData skillData, int newLevel, int oldMaxUsageCount)
    {
        if (skillData.Type != SkillType.Active)
            return;

        int newMaxUsageCount = GetRequiredMaxUsageCount(skillData, newLevel);
        int usageDelta = newMaxUsageCount - oldMaxUsageCount;
        m_SkillRemainingUsageCounts.TryGetValue(skillData.Identifier, out int remainingUsageCount);
        m_SkillRemainingUsageCounts[skillData.Identifier] = Math.Min(Math.Max(remainingUsageCount + usageDelta, 0), newMaxUsageCount);
    }

    private void ConsumeUsageAtInternal(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= m_UnlockOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");

        string skillId = m_UnlockOrder[slotIndex];
        SkillData skillData = SkillDataModel.GetSkillData(skillId);
        if (skillData == null)
            throw new InvalidOperationException($"Unlocked SkillData not found. skillId={skillId}");

        if (skillData.Type != SkillType.Active)
            throw new InvalidOperationException($"Cannot consume usage for passive skill. skillId={skillId}");

        if (!m_SkillRemainingUsageCounts.TryGetValue(skillId, out int remainingUsageCount))
            throw new InvalidOperationException($"Skill usage count missing. skillId={skillId}");

        if (remainingUsageCount <= 0)
            throw new InvalidOperationException($"Skill has no remaining usage count. skillId={skillId}");

        m_SkillRemainingUsageCounts[skillId] = remainingUsageCount - 1;
        int level = m_SkillLevels.TryGetValue(skillId, out int storedLevel) ? storedLevel : 0;
        GF.Event.Fire(this, SkillChangedEventArgs.Create(skillId, level));
    }

    private void OnIngamePhaseChanged(object sender, GameEventArgs e)
    {
        RefreshAllUsageCounts();
    }

    private void RefreshAllUsageCounts()
    {
        bool changed = false;
        for (int i = 0; i < m_UnlockOrder.Count; i++)
        {
            string skillId = m_UnlockOrder[i];
            SkillData skillData = SkillDataModel.GetSkillData(skillId);
            if (skillData == null)
                throw new InvalidOperationException($"Unlocked SkillData not found. skillId={skillId}");

            if (skillData.Type != SkillType.Active)
                continue;

            if (!m_SkillLevels.TryGetValue(skillId, out int level))
                throw new InvalidOperationException($"Skill level missing. skillId={skillId}");

            int maxUsageCount = GetRequiredMaxUsageCount(skillData, GetEffectiveLevel(level));
            if (!m_SkillRemainingUsageCounts.TryGetValue(skillId, out int current) || current != maxUsageCount)
            {
                m_SkillRemainingUsageCounts[skillId] = maxUsageCount;
                changed = true;
            }
        }

        if (changed)
            GF.Event.Fire(this, SkillChangedEventArgs.Create(null, 0));
    }

    private static int GetRequiredMaxUsageCount(SkillData skillData, int level)
    {
        int maxUsageCount = GetMaxUsageCount(skillData, level);
        if (maxUsageCount <= 0)
            throw new InvalidOperationException($"Active skill has invalid usage count. skillId={skillData.Identifier}, level={level}");

        return maxUsageCount;
    }

    private static int GetMaxUsageCount(SkillData skillData, int level)
    {
        if (skillData == null || skillData.Type != SkillType.Active)
            return 0;

        return Math.Max(0, skillData.Lv1UsageCount + skillData.UpgradeIncrementUsageCount * (level - 1));
    }

    private static int GetEffectiveLevel(int storedLevel)
    {
        if (storedLevel <= 0)
            return 0;

        return storedLevel + LevelTagRuntime.GetHeroSkillLevelBonus();
    }

    private void SwapSkillSlotsInternal(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= m_UnlockOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), fromIndex, "Invalid skill slot index.");

        if (toIndex < 0 || toIndex >= m_UnlockOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex), toIndex, "Invalid skill slot index.");

        if (fromIndex == toIndex)
            return;

        (m_UnlockOrder[fromIndex], m_UnlockOrder[toIndex]) = (m_UnlockOrder[toIndex], m_UnlockOrder[fromIndex]);
        GF.Event.Fire(this, SkillChangedEventArgs.Create(null, 0));
    }

    private void ResetSkills()
    {
        m_SkillLevels.Clear();
        m_SkillRemainingUsageCounts.Clear();
        m_UnlockOrder.Clear();
    }

    private static SkillRuntimeDataModel GetModel()
    {
        return GF.DataModel != null ? GF.DataModel.GetDataModel<SkillRuntimeDataModel>() : null;
    }

    private static SkillRuntimeDataModel GetRequiredModel()
    {
        var model = GetModel();
        if (model == null)
            throw new InvalidOperationException("SkillRuntimeDataModel is required before learning skills.");

        return model;
    }
}

public readonly struct SkillRuntimeInfo
{
    public SkillData Data { get; }
    public int Level { get; }
    public int RemainingUsageCount { get; }
    public int MaxUsageCount { get; }

    public SkillRuntimeInfo(SkillData data, int level, int remainingUsageCount, int maxUsageCount)
    {
        Data = data;
        Level = level;
        RemainingUsageCount = remainingUsageCount;
        MaxUsageCount = maxUsageCount;
    }
}
