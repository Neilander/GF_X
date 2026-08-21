using GameFramework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityGameFramework.Runtime;

/// <summary>
/// 当前关卡内玩家已获得的技能与等级。
/// </summary>
public class SkillRuntimeDataModel : DataModelBase
{
    private readonly struct PendingSkillPresentation
    {
        public PendingSkillPresentation(string skillId, int level)
        {
            SkillId = skillId;
            Level = level;
        }

        public string SkillId { get; }
        public int Level { get; }
    }

    private static SkillRuntimeDataModel s_ActiveModel;
    private readonly Queue<PendingSkillPresentation> m_PendingPresentation = new Queue<PendingSkillPresentation>();
    private readonly Dictionary<string, int> m_SkillLevels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_SkillRemainingUsageCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_SkillStackCounts = new(StringComparer.Ordinal);
    private readonly List<string> m_UnlockOrder = new();
    private readonly List<SkillRuntimeInfo> m_UnlockedSkills = new();
    private readonly ReadOnlyCollection<SkillRuntimeInfo> m_ReadOnlyUnlockedSkills;
    private int m_CachedHeroSkillLevelBonus;

    public SkillRuntimeDataModel()
    {
        if (s_ActiveModel != null
            && !ReferenceEquals(s_ActiveModel, this)
            && GF.DataModel != null
            && ReferenceEquals(GF.DataModel.GetDataModel<SkillRuntimeDataModel>(), s_ActiveModel))
            throw new InvalidOperationException("SkillRuntimeDataModel active runtime model is already bound.");
        s_ActiveModel = this;
        m_ReadOnlyUnlockedSkills = m_UnlockedSkills.AsReadOnly();
    }

    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        if (s_ActiveModel != null && !ReferenceEquals(s_ActiveModel, this))
            throw new InvalidOperationException("SkillRuntimeDataModel active runtime model is already bound.");
        s_ActiveModel = this;
        m_PendingPresentation.Clear();
        LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied;
        ResetSkills();
    }

    protected override void OnRelease()
    {
        LogicPhaseCommandService.PhaseApplied -= OnLogicPhaseApplied;
        ResetSkills();
        if (!ReferenceEquals(s_ActiveModel, this))
            throw new InvalidOperationException("SkillRuntimeDataModel release does not match the active runtime model.");
        s_ActiveModel = null;
        m_PendingPresentation.Clear();
        base.OnRelease();
    }

    public static void UpdatePresentationEvents()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("SkillRuntimeDataModel presentation events cannot run during a logic frame.");
        if (s_ActiveModel == null)
            return;
        if (s_ActiveModel.m_PendingPresentation.Count == 0)
            return;
        if (GF.Event == null)
            throw new InvalidOperationException("SkillRuntimeDataModel cannot publish presentation events before GF.Event is initialized.");
        while (s_ActiveModel.m_PendingPresentation.Count > 0)
        {
            PendingSkillPresentation pending = s_ActiveModel.m_PendingPresentation.Dequeue();
            GF.Event.Fire(
                s_ActiveModel,
                SkillChangedEventArgs.Create(pending.SkillId, pending.Level));
        }
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
        dm.PublishSkillChanged(skillData.Identifier, newLevel);
        return true;
    }

    public static bool IsUnlocked(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return false;

        return GetRequiredModel().m_SkillLevels.ContainsKey(skillId);
    }

    public static int GetLevel(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return 0;

        SkillRuntimeDataModel dm = GetRequiredModel();
        if (!dm.m_SkillLevels.TryGetValue(skillId, out int level) || level <= 0)
            return 0;

        return GetEffectiveLevel(level);
    }

    public static IReadOnlyList<SkillRuntimeInfo> GetUnlockedSkills()
    {
        SkillRuntimeDataModel dm = GetRequiredModel();

        int heroSkillLevelBonus = LevelTagRuntime.GetHeroSkillLevelBonus();
        if (heroSkillLevelBonus != dm.m_CachedHeroSkillLevelBonus)
            dm.RebuildUnlockedSkillsSnapshot(heroSkillLevelBonus);

        return dm.m_ReadOnlyUnlockedSkills;
    }

    public static void InitializeFromKeepsake(IReadOnlyList<string> skillIdentifiers)
    {
        if (skillIdentifiers == null)
            throw new ArgumentNullException(nameof(skillIdentifiers));

        SkillRuntimeDataModel dm = GetRequiredModel();
        if (dm.m_SkillLevels.Count > 0 || dm.m_UnlockOrder.Count > 0)
            throw new InvalidOperationException("Keepsake skills must be initialized before any runtime skill is unlocked.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < skillIdentifiers.Count; i++)
        {
            string skillIdentifier = skillIdentifiers[i];
            if (string.IsNullOrWhiteSpace(skillIdentifier))
                throw new InvalidOperationException($"Keepsake initial skill at index {i} is empty.");
            if (!seen.Add(skillIdentifier))
                throw new InvalidOperationException($"Keepsake repeats initial skill '{skillIdentifier}'.");
            SkillData skill = SkillDataModel.GetSkillData(skillIdentifier)
                              ?? throw new InvalidOperationException($"Keepsake initial skill is missing. skill={skillIdentifier}.");
            dm.AddLevelInternal(skill);
        }
    }

    private void RebuildUnlockedSkillsSnapshot()
    {
        RebuildUnlockedSkillsSnapshot(LevelTagRuntime.GetHeroSkillLevelBonus());
    }

    private void RebuildUnlockedSkillsSnapshot(int heroSkillLevelBonus)
    {
        m_UnlockedSkills.Clear();
        for (int i = 0; i < m_UnlockOrder.Count; i++)
        {
            string skillId = m_UnlockOrder[i];
            if (string.IsNullOrWhiteSpace(skillId))
                continue;

            if (!m_SkillLevels.TryGetValue(skillId, out int level))
                continue;

            SkillData skillData = SkillDataModel.GetSkillData(skillId);
            if (skillData == null)
                throw new InvalidOperationException($"Unlocked SkillData not found. skillId={skillId}");

            int effectiveLevel = GetEffectiveLevel(level, heroSkillLevelBonus);
            int maxUsageCount = GetMaxUsageCount(skillData, effectiveLevel);
            int remainingUsageCount = m_SkillRemainingUsageCounts.TryGetValue(skillId, out int remaining)
                ? remaining
                : 0;
            int stackCount = m_SkillStackCounts.TryGetValue(skillId, out int stacks) ? stacks : 0;
            m_UnlockedSkills.Add(new SkillRuntimeInfo(skillData, effectiveLevel, remainingUsageCount, maxUsageCount, stackCount));
        }
        m_CachedHeroSkillLevelBonus = heroSkillLevelBonus;
    }

    public static LogicSkillSlotCommand RequestSwapSkillSlots(int fromIndex, int toIndex)
    {
        if (!CanSwapSkillSlots(fromIndex, toIndex))
            throw new InvalidOperationException($"Skill slots cannot be swapped. from={fromIndex}, to={toIndex}");
        return LogicSkillSlotCommandService.ScheduleForNextFrame(fromIndex, toIndex);
    }

    public static bool CanSwapSkillSlots(int fromIndex, int toIndex)
    {
        IReadOnlyList<SkillRuntimeInfo> skills = GetUnlockedSkills();
        if (fromIndex < 0 || fromIndex >= skills.Count || toIndex < 0 || toIndex >= skills.Count || fromIndex == toIndex)
            return false;
        SkillData from = skills[fromIndex].Data
                         ?? throw new InvalidOperationException($"Skill slot has null data. index={fromIndex}");
        SkillData to = skills[toIndex].Data
                       ?? throw new InvalidOperationException($"Skill slot has null data. index={toIndex}");
        return from.Type == to.Type;
    }

    internal static void ApplyScheduledSlotSwap(LogicSkillSlotCommand command)
    {
        if (!LogicSkillSlotCommandService.IsApplyingFrame)
        {
            throw new InvalidOperationException(
                "SkillRuntimeDataModel.ApplyScheduledSlotSwap requires the logic skill-slot command apply window.");
        }
        if (command.EffectiveFrame != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"SkillRuntimeDataModel.ApplyScheduledSlotSwap frame mismatch. command={command.EffectiveFrame}, current={LogicTimeControlService.CurrentFrame}.");
        }

        var dm = GetRequiredModel();
        dm.SwapSkillSlotsInternal(command.FromIndex, command.ToIndex);
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

    public static int GetRuntimeStackCount(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("Skill identifier is required.", nameof(skillId));

        SkillRuntimeDataModel dm = GetRequiredModel();
        if (!dm.m_SkillLevels.ContainsKey(skillId))
            throw new InvalidOperationException($"Cannot read stacks for a locked skill. skillId={skillId}");
        return dm.m_SkillStackCounts.TryGetValue(skillId, out int count) ? count : 0;
    }

    public static void SetRuntimeStackCount(string skillId, int count)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("Skill identifier is required.", nameof(skillId));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Skill stack count cannot be negative.");

        SkillRuntimeDataModel dm = GetRequiredModel();
        if (!dm.m_SkillLevels.TryGetValue(skillId, out int level))
            throw new InvalidOperationException($"Cannot set stacks for a locked skill. skillId={skillId}");
        if (dm.m_SkillStackCounts.TryGetValue(skillId, out int current) && current == count)
            return;

        dm.m_SkillStackCounts[skillId] = count;
        dm.RebuildUnlockedSkillsSnapshot();
        dm.m_PendingPresentation.Enqueue(new PendingSkillPresentation(skillId, level));
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

            InsertSkillByCanonicalOrder(skillData);
            m_SkillLevels[skillId] = 1;
            m_SkillStackCounts[skillId] = 0;
            SetInitialUsageCount(skillData, 1);
            RebuildUnlockedSkillsSnapshot();
            return 1;
        }

        int oldMaxUsageCount = GetMaxUsageCount(skillData, level);
        level++;
        m_SkillLevels[skillId] = level;
        AddUpgradeUsageCount(skillData, level, oldMaxUsageCount);
        RebuildUnlockedSkillsSnapshot();
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
        RebuildUnlockedSkillsSnapshot();
        PublishSkillChanged(skillId, level);
    }

    private void OnLogicPhaseApplied(GamePhase oldPhase, GamePhase newPhase)
    {
        RefreshAllUsageCounts();
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        SkillRuntimeDataModel model = GetRequiredModel();
        hasher.Add(0x534B494C4C535441UL);
        hasher.Add(true);

        if (model.m_SkillLevels.Count != model.m_UnlockOrder.Count)
        {
            throw new InvalidOperationException(
                $"SkillRuntimeDataModel deterministic state mismatch. levels={model.m_SkillLevels.Count}, order={model.m_UnlockOrder.Count}.");
        }

        hasher.Add(model.m_UnlockOrder.Count);
        for (int i = 0; i < model.m_UnlockOrder.Count; i++)
        {
            string skillId = model.m_UnlockOrder[i];
            if (string.IsNullOrWhiteSpace(skillId))
                throw new InvalidOperationException($"SkillRuntimeDataModel has an empty skill id at slot {i}.");
            if (!model.m_SkillLevels.TryGetValue(skillId, out int level) || level <= 0)
                throw new InvalidOperationException($"SkillRuntimeDataModel has invalid level state for '{skillId}'.");

            hasher.Add(skillId);
            hasher.Add(level);
            bool hasRemainingUsage = model.m_SkillRemainingUsageCounts.TryGetValue(skillId, out int remainingUsage);
            if (hasRemainingUsage && remainingUsage < 0)
                throw new InvalidOperationException($"SkillRuntimeDataModel has negative remaining usage for '{skillId}'.");
            hasher.Add(hasRemainingUsage);
            if (hasRemainingUsage)
                hasher.Add(remainingUsage);
            bool hasStacks = model.m_SkillStackCounts.TryGetValue(skillId, out int stackCount);
            if (hasStacks && stackCount < 0)
                throw new InvalidOperationException($"SkillRuntimeDataModel has negative stacks for '{skillId}'.");
            hasher.Add(hasStacks);
            if (hasStacks)
                hasher.Add(stackCount);
        }

        foreach (string skillId in model.m_SkillRemainingUsageCounts.Keys)
        {
            if (!model.m_SkillLevels.ContainsKey(skillId))
            {
                throw new InvalidOperationException(
                    $"SkillRuntimeDataModel has remaining usage for unknown skill '{skillId}'.");
            }
        }

        foreach (string skillId in model.m_SkillStackCounts.Keys)
        {
            if (!model.m_SkillLevels.ContainsKey(skillId))
                throw new InvalidOperationException($"SkillRuntimeDataModel has stacks for unknown skill '{skillId}'.");
        }
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
        {
            RebuildUnlockedSkillsSnapshot();
            PublishSkillChanged(null, 0);
        }
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
        return GetEffectiveLevel(storedLevel, LevelTagRuntime.GetHeroSkillLevelBonus());
    }

    private static int GetEffectiveLevel(int storedLevel, int heroSkillLevelBonus)
    {
        if (storedLevel <= 0)
            return 0;

        return storedLevel + heroSkillLevelBonus;
    }

    private void SwapSkillSlotsInternal(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= m_UnlockOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), fromIndex, "Invalid skill slot index.");

        if (toIndex < 0 || toIndex >= m_UnlockOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex), toIndex, "Invalid skill slot index.");

        if (fromIndex == toIndex)
            return;

        SkillData fromSkill = SkillDataModel.GetSkillData(m_UnlockOrder[fromIndex])
                              ?? throw new InvalidOperationException($"Unlocked SkillData not found. skillId={m_UnlockOrder[fromIndex]}");
        SkillData toSkill = SkillDataModel.GetSkillData(m_UnlockOrder[toIndex])
                            ?? throw new InvalidOperationException($"Unlocked SkillData not found. skillId={m_UnlockOrder[toIndex]}");
        if (fromSkill.Type != toSkill.Type)
        {
            throw new InvalidOperationException(
                $"Active and passive skill slots cannot be swapped. from={fromSkill.Identifier}, to={toSkill.Identifier}");
        }

        (m_UnlockOrder[fromIndex], m_UnlockOrder[toIndex]) = (m_UnlockOrder[toIndex], m_UnlockOrder[fromIndex]);
        RebuildUnlockedSkillsSnapshot();
        PublishSkillChanged(null, 0);
    }

    private void PublishSkillChanged(string skillId, int level)
    {
        LogicSkillStateService.RefreshActiveSkillComponents();
        m_PendingPresentation.Enqueue(new PendingSkillPresentation(skillId, level));
    }

    private void ResetSkills()
    {
        m_SkillLevels.Clear();
        m_SkillRemainingUsageCounts.Clear();
        m_SkillStackCounts.Clear();
        m_UnlockOrder.Clear();
        m_UnlockedSkills.Clear();
        m_CachedHeroSkillLevelBonus = LevelTagRuntime.GetHeroSkillLevelBonus();
    }

    private void InsertSkillByCanonicalOrder(SkillData skillData)
    {
        int insertIndex = m_UnlockOrder.Count;
        for (int i = 0; i < m_UnlockOrder.Count; i++)
        {
            SkillData existing = SkillDataModel.GetSkillData(m_UnlockOrder[i])
                                 ?? throw new InvalidOperationException($"Unlocked SkillData not found. skillId={m_UnlockOrder[i]}");
            if (CompareCanonicalOrder(skillData, existing) < 0)
            {
                insertIndex = i;
                break;
            }
        }

        m_UnlockOrder.Insert(insertIndex, skillData.Identifier);
    }

    internal static int CompareCanonicalOrder(SkillData left, SkillData right)
    {
        return CompareCanonicalOrder(left, right, SkillDataModel.GetIndustryMapRequired());
    }

    internal static int CompareCanonicalOrder(
        SkillData left,
        SkillData right,
        IReadOnlyDictionary<string, Archetype> skillIndustries)
    {
        if (left == null)
            throw new ArgumentNullException(nameof(left));
        if (right == null)
            throw new ArgumentNullException(nameof(right));
        if (skillIndustries == null)
            throw new ArgumentNullException(nameof(skillIndustries));
        if (left.Type != right.Type)
            return left.Type == SkillType.Active ? -1 : 1;

        bool leftHasIndustry = skillIndustries.TryGetValue(left.Identifier, out Archetype leftIndustry);
        bool rightHasIndustry = skillIndustries.TryGetValue(right.Identifier, out Archetype rightIndustry);
        if (leftHasIndustry != rightHasIndustry)
            return leftHasIndustry ? 1 : -1;

        int industryOrder = leftHasIndustry
            ? CareerConfigRuntime.GetArchetypeOrderIndexRequired(leftIndustry)
                .CompareTo(CareerConfigRuntime.GetArchetypeOrderIndexRequired(rightIndustry))
            : 0;
        return industryOrder != 0
            ? industryOrder
            : string.CompareOrdinal(left.Identifier, right.Identifier);
    }

    private static SkillRuntimeDataModel GetModel()
    {
        return s_ActiveModel;
    }

    private static SkillRuntimeDataModel GetRequiredModel()
    {
        return GetModel()
               ?? throw new InvalidOperationException(
                   "SkillRuntimeDataModel access requires an active runtime model.");
    }
}

public readonly struct SkillRuntimeInfo
{
    public SkillData Data { get; }
    public int Level { get; }
    public int RemainingUsageCount { get; }
    public int MaxUsageCount { get; }
    public int StackCount { get; }

    public SkillRuntimeInfo(SkillData data, int level, int remainingUsageCount, int maxUsageCount, int stackCount = 0)
    {
        Data = data;
        Level = level;
        RemainingUsageCount = remainingUsageCount;
        MaxUsageCount = maxUsageCount;
        StackCount = stackCount;
    }
}
