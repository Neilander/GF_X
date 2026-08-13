using System.Collections.Generic;

public interface ISkillComp : ICapability
{
    void Init(IEntityContext entity, List<ActiveSkillSO> activeSkills, List<PassiveSkillSO> passiveSkills);
    void Skill(Fix64 deltaTime);
    void CancelSkills();
    void OnSkillChanged();
}

public interface ISkillActionPresentationProvider
{
    int SkillPresentationSlotCount { get; }
    bool TryGetActiveSkillActionPresentation(
        int slotIndex,
        out SkillInfo skillInfo,
        out string triggerName);
}

public static class LogicSkillStateService
{
    public static void RefreshActiveSkillComponents()
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is not ISkillCompHost host || host.skillComp == null)
                continue;

            host.skillComp.OnSkillChanged();
        }
    }
}

public static class SkillCompDeterministicStateUtility
{
    private static readonly List<string> s_DeterministicStringKeys = new List<string>();
    private static readonly System.Comparison<string> s_DeterministicStringComparison =
        System.String.CompareOrdinal;

    public static void Write(
        LogicStateHasher hasher,
        IEntityContext entity,
        List<SkillSlot> slots,
        Dictionary<string, GeneralCounter> cooldownsBySkillId,
        HashSet<string> appliedPassiveSkillIds)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));

        hasher.Add(entity?.LogicEntityId.Value ?? 0);
        FillSortedStringKeys(cooldownsBySkillId);
        hasher.Add(s_DeterministicStringKeys.Count);
        for (int i = 0; i < s_DeterministicStringKeys.Count; i++)
        {
            string skillId = s_DeterministicStringKeys[i];
            hasher.Add(skillId);
            GeneralCounter counter = cooldownsBySkillId[skillId]
                ?? throw new System.InvalidOperationException($"Skill cooldown is null. skillId={skillId}.");
            counter.WriteDeterministicState(hasher);
        }

        FillSortedStringKeys(appliedPassiveSkillIds);
        hasher.Add(s_DeterministicStringKeys.Count);
        for (int i = 0; i < s_DeterministicStringKeys.Count; i++)
            hasher.Add(s_DeterministicStringKeys[i]);

        int slotCount = slots?.Count ?? 0;
        hasher.Add(slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            SkillSlot slot = slots[i]
                ?? throw new System.InvalidOperationException($"Skill slot is null. index={i}.");
            slot.WriteDeterministicState(hasher, slots);
        }
    }

    public static void WriteSkillInfo(LogicStateHasher hasher, SkillInfo info)
    {
        hasher.Add(info != null);
        if (info == null)
            return;

        hasher.Add(info.currentIndex);
        hasher.Add(info.isFinished);
        hasher.Add(info.entity?.LogicEntityId.Value ?? 0);
        hasher.Add(info.hasRequestedWorldPosition);
        AddFixedWorldPosition(hasher, info.requestedWorldPosition);
        WriteSelectableList(hasher, info is TargetPositionSkillInfo targetInfo ? targetInfo.selectTargets : info.currentInfo?.selectTargets);
        FixVector2 selectedPosition = info is TargetPositionSkillInfo targetPositionInfo
            ? targetPositionInfo.selectPos
            : info is SelectPosSkillInfo selectPositionInfo
                ? selectPositionInfo.selectPos
                : info.currentInfo?.selectPos ?? FixVector2.Zero;
        AddFixedWorldPosition(hasher, selectedPosition);

        ActionInfo action = info.currentInfo;
        hasher.Add(action != null);
        if (action == null)
            return;
        hasher.Add(action.GetType().FullName);
        hasher.Add(action.executeIndex);
        hasher.Add(action.elapsed.RawValue);
        hasher.Add(action.injectInfoAlready);
        hasher.Add(action.isRunning);
        hasher.Add(action.isInterrupted);
        hasher.Add(action.isFinished);
        WriteSortedBoolDictionary(hasher, action.bools);
        WriteSortedIntDictionary(hasher, action.ints);
        WriteSelectableList(hasher, action.selectTargets);
        AddFixedWorldPosition(hasher, action.selectPos);

        if (action is PositionSelectActionInfo positionInfo)
        {
            hasher.Add(true);
            AddFixedWorldPosition(hasher, positionInfo.lastSelectPos);
            AddFixedWorldPosition(hasher, positionInfo.confirmedSelectPos);
            hasher.Add(positionInfo.getPosAlready);
            WriteSelectableList(hasher, positionInfo.selectedTargets);
            hasher.Add(positionInfo.radius.RawValue);
            hasher.Add(positionInfo.selectionRadius.RawValue);
        }
        else
        {
            hasher.Add(false);
        }
    }

    private static void WriteSelectableList(LogicStateHasher hasher, IReadOnlyList<ISelectable> values)
    {
        int count = values?.Count ?? 0;
        hasher.Add(count);
        for (int i = 0; i < count; i++)
        {
            if (values[i] is not IEntityContext entity || !entity.LogicEntityId.IsValid)
                throw new System.InvalidOperationException($"Skill selected target has no stable logic identity. index={i} type={values[i]?.GetType().FullName ?? "null"}.");
            hasher.Add(entity.LogicEntityId.Value);
        }
    }

    private static void AddFixedWorldPosition(LogicStateHasher hasher, FixVector2 position)
    {
        hasher.Add(position.x.RawValue);
        hasher.Add(position.y.RawValue);
    }

    private static void WriteSortedBoolDictionary(LogicStateHasher hasher, Dictionary<string, bool> values)
    {
        FillSortedStringKeys(values);
        hasher.Add(s_DeterministicStringKeys.Count);
        for (int i = 0; i < s_DeterministicStringKeys.Count; i++)
        {
            string key = s_DeterministicStringKeys[i];
            hasher.Add(key);
            hasher.Add(values[key]);
        }
    }

    private static void WriteSortedIntDictionary(LogicStateHasher hasher, Dictionary<string, int> values)
    {
        FillSortedStringKeys(values);
        hasher.Add(s_DeterministicStringKeys.Count);
        for (int i = 0; i < s_DeterministicStringKeys.Count; i++)
        {
            string key = s_DeterministicStringKeys[i];
            hasher.Add(key);
            hasher.Add(values[key]);
        }
    }

    private static void FillSortedStringKeys<T>(Dictionary<string, T> values)
    {
        s_DeterministicStringKeys.Clear();
        if (values != null)
        {
            foreach (string key in values.Keys)
                s_DeterministicStringKeys.Add(key);
        }
        s_DeterministicStringKeys.Sort(s_DeterministicStringComparison);
    }

    private static void FillSortedStringKeys(HashSet<string> values)
    {
        s_DeterministicStringKeys.Clear();
        if (values != null)
        {
            foreach (string key in values)
                s_DeterministicStringKeys.Add(key);
        }
        s_DeterministicStringKeys.Sort(s_DeterministicStringComparison);
    }
}
