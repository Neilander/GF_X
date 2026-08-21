using System.Collections;
using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;
using System.Linq;

public class PlayerSkillComp : ISkillComp, ILogicDeterministicStateContributor,
    ILogicSkillCastCommandConsumer, ILogicPausedSkillCastCommandConsumer,
    ISkillCastPreviewProvider, ISkillActionPresentationProvider, ISkillCooldownPresentationProvider
{
    private IEntityContext _entity;
    private bool m_HasPendingCast;
    private LogicSkillCastCommand m_PendingCast;

    public const int SKILL_NUM = SkillInputRuntime.MaxSkillCount;

    private List<SkillSlot> _skillSlots;
    private Dictionary<string, ActiveSkillSO> _skillsById;
    private Dictionary<string, PassiveSkillSO> _passiveSkillsById;
    private HashSet<string> _appliedPassiveSkillIds;
    private Dictionary<string, GeneralCounter> _cooldownsBySkillId;

    public void Init(IEntityContext entity, List<ActiveSkillSO>skillSet, List<PassiveSkillSO> passiveSkillSet)
    {
        _entity = entity;

        _skillSlots = new List<SkillSlot>();
        _skillsById = new Dictionary<string, ActiveSkillSO>(System.StringComparer.Ordinal);
        _passiveSkillsById = new Dictionary<string, PassiveSkillSO>(System.StringComparer.Ordinal);
        _appliedPassiveSkillIds = new HashSet<string>(System.StringComparer.Ordinal);
        _cooldownsBySkillId = new Dictionary<string, GeneralCounter>(System.StringComparer.Ordinal);

        for (int i = 0; i < skillSet.Count; i++)
        {
            ActiveSkillSO skill = skillSet[i];
            if (skill == null)
                throw new System.InvalidOperationException($"Player skill asset is null. index={i}");

            if (string.IsNullOrWhiteSpace(skill.skillId))
                throw new System.InvalidOperationException($"Player skill asset missing skillId. asset={skill.name}");

            _skillsById[skill.skillId] = skill;
            var cooldown = new GeneralCounter();
            cooldown.Init(skill.ResolveCooldownIntervalFixed(), true);
            _cooldownsBySkillId[skill.skillId] = cooldown;
        }

        if (passiveSkillSet != null)
        {
            for (int i = 0; i < passiveSkillSet.Count; i++)
            {
                PassiveSkillSO skill = passiveSkillSet[i];
                if (skill == null)
                    throw new System.InvalidOperationException($"Player passive skill asset is null. index={i}");

                if (string.IsNullOrWhiteSpace(skill.skillId))
                    throw new System.InvalidOperationException($"Player passive skill asset missing skillId. asset={skill.name}");

                _passiveSkillsById[skill.skillId] = skill;
            }
        }

        for (int i = 0; i < SKILL_NUM; i++)
        {
            var cooldown = new GeneralCounter();
            cooldown.Init(Fix64.Zero, false);

            _skillSlots.Add(new SkillSlot
            {
                cooldown = cooldown
            });
        }

        OnSkillChanged();
    }

    public void OnSkillChanged()
    {
        RefreshPassiveSkills();
    }

    public void Skill(Fix64 deltaTime)
    {
        //检测正在执行的技能，运行
        UpdateTickingSkill(deltaTime);


        //技能冷却
        CoolDown(deltaTime);
        UpdateSkillRuntime();

        TryStartPendingCast(out _);

    }

    void LockCompWhenStart()
    {
        _entity.LockComp(_entity.AtkComp,this);
    }

    void UnlockCompWhenEnd()
    {
        _entity.ResumeComp(_entity.AtkComp,this);
    }

    public void ShutDown()
    {
        m_HasPendingCast = false;
        m_PendingCast = default;
        RemoveAllPassiveSkills();
    }

    public void Resume()
    {
    }

    public void CancelSkills()
    {
        m_HasPendingCast = false;
        m_PendingCast = default;
        if (_skillSlots == null)
            return;

        bool canceled = false;
        for (int i = 0; i < _skillSlots.Count; i++)
        {
            SkillSlot slot = _skillSlots[i];
            if (slot == null || !slot.isTicking)
                continue;

            canceled = true;
            if (slot.runInfo != null)
                slot.skill.InterruptSkill(slot.runInfo);

            slot.isTicking = false;
            slot.runInfo = null;
            slot.DirectUnlockAll();
            SkillCastState.EndCast();
        }

        if (canceled)
            UnlockCompWhenEnd();
    }

    private void UpdateTickingSkill(Fix64 deltaTime)
    {
        foreach (var slot in _skillSlots)
        {
            if (slot.isTicking)
            {
                //触发技能的tick
                slot.skill.TickSkill(slot.runInfo, deltaTime);
                if (slot.runInfo.isFinished)
                {
                    CompleteSkill(slot);
                }
            }
        }
    }

    public void ResolvePausedSkillCastCommand()
    {
        if (!LogicPausedOperationService.IsExecuting)
            throw new System.InvalidOperationException("Paused skill resolution requires a paused-operation settlement.");
        if (!TryStartPendingCast(out SkillSlot slot))
            return;

        slot.skill.TickSkill(slot.runInfo, Fix64.Zero);
        if (slot.runInfo.isFinished)
            CompleteSkill(slot);
    }

    private bool TryStartPendingCast(out SkillSlot startedSlot)
    {
        startedSlot = null;
        if (!m_HasPendingCast)
            return false;

        LogicSkillCastCommand command = m_PendingCast;
        m_HasPendingCast = false;
        m_PendingCast = default;
        if (!CanExecuteSkillCast(command.SlotIndex))
            return false;

        startedSlot = _skillSlots[command.SlotIndex];
        GF.Log("使用技能：" + (command.SlotIndex + 1));
        StartASkill(startedSlot, command.SlotIndex, command.RequestedWorldPosition);
        return true;
    }

    private void CompleteSkill(SkillSlot slot)
    {
        slot.isTicking = false;
        slot.runInfo = null;
        slot.DirectUnlockAll();
        SkillCastState.EndCast();
        UnlockCompWhenEnd();
    }

    private void StartASkill(SkillSlot curSlot, int slotIndex, FixVector2 requestedWorldPosition)
    {
        //技能进入冷却
        //后续要是想要什么持续技能，切换技能，再加上逻辑就可以，想过是可以实现的
        //切换+冷却本质是技能替换，然后新技能开局有个cd
        curSlot.skill.StartSkill(_entity, requestedWorldPosition, out SkillInfo runInfo);
        curSlot.cooldown.Reset();
        //触发其开始函数
        curSlot.runInfo = runInfo;
        curSlot.isTicking = true;

        //根据技能的需求，关闭其他comp和技能输入
        //如果技能Ban所有其他的，其他的都按不了
        if (curSlot.skill.banOtherSkillWhenCast)
        {
            BanOtherSkill(curSlot);
        }
        else
        {
            foreach (var slot in _skillSlots)
            {
                if (slot != curSlot && slot.skill != null && slot.skill.banWhenOtherSkill)
                    slot.Lock(curSlot);
            }
        }

        LockCompWhenStart();
        SkillCastState.BeginCast();
        SkillRuntimeDataModel.ConsumeUsageAt(slotIndex);
    }

    private void CoolDown(Fix64 deltaTime)
    {
        if (_cooldownsBySkillId == null)
            return;

        foreach (var cooldown in _cooldownsBySkillId.Values)
        {
            cooldown.Tick(deltaTime);
        }
    }

    private void UpdateSkillRuntime()
    {
        for (int i = 0; i < _skillSlots.Count; i++)
        {
            SkillSlot slot = _skillSlots[i];
            SyncSlotSkill(slot, i, false);
            slot.canCast =
                slot.skill != null &&
                slot.cooldown != null &&
                !slot.isCoolingDown &&
                !slot.IsBanned;
        }
    }

    public void AcceptSkillCastCommand(LogicSkillCastCommand command)
    {
        if (_entity == null || command.CasterId != _entity.LogicEntityId)
        {
            throw new System.InvalidOperationException(
                $"PlayerSkillComp received a cast for another caster. expected={_entity?.LogicEntityId.Value ?? 0}, actual={command.CasterId.Value}.");
        }
        if (m_HasPendingCast)
            throw new System.InvalidOperationException($"PlayerSkillComp already has a pending cast. caster={command.CasterId.Value}.");
        m_PendingCast = command;
        m_HasPendingCast = true;
    }

    public bool CanRequestSkillCast(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SKILL_NUM)
            return false;
        return !m_HasPendingCast
               && SkillInputRuntime.CanUseActiveSkillsInCurrentPhase()
               && SkillRuntimeDataModel.IsUnlockedActiveSkillSlot(slotIndex)
               && SkillRuntimeDataModel.HasRemainingUsageAt(slotIndex)
               && _skillSlots[slotIndex].canCast;
    }

    public SkillCastPreviewDescriptor GetRequiredSkillCastPreview(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SKILL_NUM)
            throw new System.ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");
        if (!SkillRuntimeDataModel.IsUnlockedActiveSkillSlot(slotIndex))
            throw new System.InvalidOperationException($"Skill slot is not unlocked. slot={slotIndex}.");

        SkillRuntimeInfo runtime = SkillRuntimeDataModel.GetUnlockedSkillAt(slotIndex);
        if (!_skillsById.TryGetValue(runtime.Data.Identifier, out ActiveSkillSO skill) || skill == null)
            throw new System.InvalidOperationException($"ActiveSkillSO asset not configured for skillId={runtime.Data.Identifier}.");
        return skill.TryGetPositionSelectionDescriptor(out SkillCastPreviewDescriptor descriptor)
            ? descriptor
            : SkillCastPreviewDescriptor.Instant;
    }

    public int SkillPresentationSlotCount => SKILL_NUM;

    public bool TryGetSkillCooldownPresentation(int slotIndex, out Fix64 remaining, out Fix64 total)
    {
        if (slotIndex < 0 || slotIndex >= SKILL_NUM)
            throw new System.ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");
        if (!SkillRuntimeDataModel.IsUnlockedActiveSkillSlot(slotIndex))
        {
            remaining = Fix64.Zero;
            total = Fix64.Zero;
            return false;
        }

        SkillRuntimeInfo runtime = SkillRuntimeDataModel.GetUnlockedSkillAt(slotIndex);
        string skillId = runtime.Data.Identifier;
        if (!_cooldownsBySkillId.TryGetValue(skillId, out GeneralCounter cooldown) || cooldown == null)
            throw new System.InvalidOperationException($"Player skill cooldown is missing. skillId={skillId}, slot={slotIndex}.");

        total = cooldown.GetTargetRequired();
        remaining = cooldown.GetRemainingRequired();
        if (total <= Fix64.Zero)
            throw new System.InvalidOperationException($"Player skill cooldown target is invalid. skillId={skillId}, slot={slotIndex}.");
        return true;
    }

    public bool TryGetActiveSkillActionPresentation(
        int slotIndex,
        out SkillInfo skillInfo,
        out string triggerName)
    {
        if (slotIndex < 0 || slotIndex >= SKILL_NUM)
            throw new System.ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");
        if (_skillSlots == null || _skillSlots.Count != SKILL_NUM)
            throw new System.InvalidOperationException("PlayerSkillComp presentation state is unavailable before initialization.");

        SkillSlot slot = _skillSlots[slotIndex]
            ?? throw new System.InvalidOperationException($"Player skill slot is null. slot={slotIndex}.");
        if (!slot.isTicking)
        {
            skillInfo = null;
            triggerName = null;
            return false;
        }
        if (slot.skill == null || slot.runInfo == null)
            throw new System.InvalidOperationException($"Running player skill has incomplete state. slot={slotIndex}.");
        if (slot.skill.actions == null
            || slot.runInfo.currentIndex < 0
            || slot.runInfo.currentIndex >= slot.skill.actions.Count)
        {
            throw new System.InvalidOperationException(
                $"Running player skill action index is invalid. slot={slotIndex}, action={slot.runInfo.currentIndex}.");
        }

        BasicAction action = slot.skill.actions[slot.runInfo.currentIndex]
            ?? throw new System.InvalidOperationException(
                $"Running player skill action is null. slot={slotIndex}, action={slot.runInfo.currentIndex}.");
        skillInfo = slot.runInfo;
        triggerName = action.relatedTriggerString;
        return true;
    }

    private bool CanExecuteSkillCast(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _skillSlots.Count)
            throw new System.ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Invalid skill slot index.");
        if (!SkillInputRuntime.CanUseActiveSkillsInCurrentPhase()
            || !SkillRuntimeDataModel.IsUnlockedActiveSkillSlot(slotIndex)
            || !SkillRuntimeDataModel.HasRemainingUsageAt(slotIndex))
        {
            return false;
        }

        SkillSlot slot = _skillSlots[slotIndex];
        SyncSlotSkill(slot, slotIndex, true);
        return slot.canCast;
    }

    private void SyncSlotSkill(SkillSlot slot, int slotIndex, bool requireAsset)
    {
        if (!SkillRuntimeDataModel.IsUnlockedActiveSkillSlot(slotIndex))
        {
            slot.skill = null;
            slot.cooldown = null;
            slot.isCoolingDown = false;
            slot.canCast = false;
            return;
        }

        SkillRuntimeInfo skillInfo = SkillRuntimeDataModel.GetUnlockedSkillAt(slotIndex);
        string skillId = skillInfo.Data.Identifier;
        ActiveSkillSO skill = null;
        GeneralCounter cooldown = null;
        bool hasSkill = _skillsById != null && _skillsById.TryGetValue(skillId, out skill) && skill != null;
        bool hasCooldown = _cooldownsBySkillId != null && _cooldownsBySkillId.TryGetValue(skillId, out cooldown) && cooldown != null;

        if ((!hasSkill || !hasCooldown) && requireAsset)
            throw new System.InvalidOperationException($"ActiveSkillSO asset not configured for skillId={skillId}.");

        slot.skill = hasSkill ? skill : null;
        slot.cooldown = hasCooldown ? cooldown : null;
        if (hasSkill && hasCooldown)
            cooldown.SetTarget(skill.ResolveCooldownIntervalFixed(skillInfo.Level));
        slot.isCoolingDown = hasCooldown && !cooldown.IsFinished();
    }

    private void BanOtherSkill(SkillSlot curSlot)
    {
        foreach (var slot in _skillSlots)
        {
            if(slot!= curSlot)
                slot.Lock( curSlot);
        }
    }

    private void RefreshPassiveSkills()
    {
        if (_entity == null || _passiveSkillsById == null)
            return;

        foreach (var pair in _passiveSkillsById)
        {
            bool unlocked = SkillRuntimeDataModel.IsUnlocked(pair.Key);
            bool applied = _appliedPassiveSkillIds.Contains(pair.Key);

            if (unlocked)
            {
                if (applied)
                    pair.Value.Remove(_entity);

                pair.Value.Apply(_entity);
                _appliedPassiveSkillIds.Add(pair.Key);
            }
            else if (applied)
            {
                pair.Value.Remove(_entity);
                _appliedPassiveSkillIds.Remove(pair.Key);
            }
        }
    }

    private void RemoveAllPassiveSkills()
    {
        if (_entity == null || _passiveSkillsById == null || _appliedPassiveSkillIds == null)
            return;

        foreach (string skillId in _appliedPassiveSkillIds)
        {
            if (_passiveSkillsById.TryGetValue(skillId, out PassiveSkillSO skill) && skill != null)
                skill.Remove(_entity);
        }

        _appliedPassiveSkillIds.Clear();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        SkillCompDeterministicStateUtility.Write(hasher, _entity, _skillSlots, _cooldownsBySkillId, _appliedPassiveSkillIds);
        hasher.Add(m_HasPendingCast);
        if (m_HasPendingCast)
        {
            hasher.Add(m_PendingCast.EffectiveFrame);
            hasher.Add(m_PendingCast.Sequence);
            hasher.Add(m_PendingCast.CasterId.Value);
            hasher.Add(m_PendingCast.SlotIndex);
            hasher.Add(m_PendingCast.RequestedWorldPosition.x.RawValue);
            hasher.Add(m_PendingCast.RequestedWorldPosition.y.RawValue);
        }
    }
}
public class SkillSlot: ISkillLocker
{
    private static readonly List<int> s_DeterministicSlotIndices = new List<int>();
    public ActiveSkillSO skill;

    public GeneralCounter cooldown;


    //运行时数据
    public bool isCoolingDown = true;

    //整合最终判断
    public bool canCast = false;

    public bool isTicking = false;
    public SkillInfo runInfo = null;


    // ---------- 被谁锁 ----------
    private HashSet<ISkillLocker> _lockers = new HashSet<ISkillLocker>();

    // ---------- 我锁了谁 ----------
    private HashSet<SkillSlot> _lockedSlotsByMe = new HashSet<SkillSlot>();

    public bool IsBanned => _lockers.Count > 0;

    public void Lock(ISkillLocker locker)
    {
        if (locker == null)
            return;

        if (_lockers.Add(locker))
        {
            locker.RecordLockedSkill(this);
        }
    }

    public void Unlock(ISkillLocker locker)
    {
        _lockers.Remove(locker);
    }

    public void RecordLockedSkill(SkillSlot slot)
    {
        if (slot != null)
            _lockedSlotsByMe.Add(slot);
    }

    public void DirectUnlockAll()
    {
        foreach (var slot in _lockedSlotsByMe)
        {
            slot.Unlock(this);
        }

        _lockedSlotsByMe.Clear();
    }

    internal void WriteDeterministicState(LogicStateHasher hasher, List<SkillSlot> ownerSlots)
    {
        hasher.Add(skill?.skillId);
        hasher.Add(cooldown != null);
        if (cooldown != null)
            cooldown.WriteDeterministicState(hasher);
        hasher.Add(isCoolingDown);
        hasher.Add(canCast);
        hasher.Add(isTicking);
        SkillCompDeterministicStateUtility.WriteSkillInfo(hasher, runInfo);
        WriteSlotSet(hasher, ownerSlots, _lockers);
        WriteSlotSet(hasher, ownerSlots, _lockedSlotsByMe);
    }

    private static void WriteSlotSet<T>(LogicStateHasher hasher, List<SkillSlot> ownerSlots, HashSet<T> values)
    {
        s_DeterministicSlotIndices.Clear();
        foreach (T value in values)
        {
            if (value is not SkillSlot slot)
                throw new System.InvalidOperationException($"Skill lock has unsupported owner type {value?.GetType().FullName ?? "null"}.");
            int index = ownerSlots.IndexOf(slot);
            if (index < 0)
                throw new System.InvalidOperationException("Skill lock references a slot outside its owner component.");
            s_DeterministicSlotIndices.Add(index);
        }
        s_DeterministicSlotIndices.Sort();
        hasher.Add(s_DeterministicSlotIndices.Count);
        for (int i = 0; i < s_DeterministicSlotIndices.Count; i++)
            hasher.Add(s_DeterministicSlotIndices[i]);
    }
}
