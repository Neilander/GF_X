using System.Collections.Generic;

public interface ISkillComp : ICapability
{
    void Init(IEntityContext entity, List<ActiveSkillSO> activeSkills, List<PassiveSkillSO> passiveSkills);
    void Skill(Fix64 deltaTime);
    void CancelSkills();
    void OnSkillChanged();
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
        var cooldownIds = cooldownsBySkillId != null
            ? new List<string>(cooldownsBySkillId.Keys)
            : new List<string>();
        cooldownIds.Sort(System.StringComparer.Ordinal);
        hasher.Add(cooldownIds.Count);
        for (int i = 0; i < cooldownIds.Count; i++)
        {
            string skillId = cooldownIds[i];
            hasher.Add(skillId);
            GeneralCounter counter = cooldownsBySkillId[skillId]
                ?? throw new System.InvalidOperationException($"Skill cooldown is null. skillId={skillId}.");
            counter.WriteDeterministicState(hasher);
        }

        var passiveIds = appliedPassiveSkillIds != null
            ? new List<string>(appliedPassiveSkillIds)
            : new List<string>();
        passiveIds.Sort(System.StringComparer.Ordinal);
        hasher.Add(passiveIds.Count);
        for (int i = 0; i < passiveIds.Count; i++)
            hasher.Add(passiveIds[i]);

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
        if (action.floats.Count != 0 || action.objects.Count != 0)
            throw new System.InvalidOperationException($"Legacy action has unsupported float/object deterministic state. action={action.GetType().FullName} floats={action.floats.Count} objects={action.objects.Count}.");

        hasher.Add(action.GetType().FullName);
        hasher.Add(action.executeIndex);
        hasher.Add(action.elapsed.RawValue);
        hasher.Add(action.duration.RawValue);
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
        var keys = new List<string>(values.Keys);
        keys.Sort(System.StringComparer.Ordinal);
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            hasher.Add(keys[i]);
            hasher.Add(values[keys[i]]);
        }
    }

    private static void WriteSortedIntDictionary(LogicStateHasher hasher, Dictionary<string, int> values)
    {
        var keys = new List<string>(values.Keys);
        keys.Sort(System.StringComparer.Ordinal);
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            hasher.Add(keys[i]);
            hasher.Add(values[keys[i]]);
        }
    }
}
