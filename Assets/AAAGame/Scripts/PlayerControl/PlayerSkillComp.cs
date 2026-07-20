using System.Collections;
using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;
using System.Linq;

public class PlayerSkillComp : ISkillComp
{
    private IEntityContext _entity;
    private InputModel _inputModel;

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
            cooldown.Init((Fix64)skill.ResolveCooldownInterval(), true);
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

        _inputModel = GF.DataModel.GetDataModel<InputModel>();
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

        //检测输入
        int curSkillPressed = CheckInput();

        //if(curSkillPressed != -1)


        //如果冷却好了，就触发技能
        if (curSkillPressed == -1)
            return;
        GF.Log("使用技能："+(curSkillPressed+1));
        SkillSlot curSlot = _skillSlots[curSkillPressed];
        SyncSlotSkill(curSlot, curSkillPressed, true);

        //触发技能逻辑（恢复其他技能还没做好）
        StartASkill(curSlot, curSkillPressed);

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
        RemoveAllPassiveSkills();
    }

    public void Resume()
    {
    }

    public void CancelSkills()
    {
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
                slot.skill.TickSkill(slot.runInfo, (float)deltaTime);
                if (slot.runInfo.isFinished)
                {
                    slot.isTicking = false;
                    slot.runInfo = null;
                    slot.DirectUnlockAll();
                    SkillCastState.EndCast();
                    UnlockCompWhenEnd();

                }
            }
        }
    }

    private void StartASkill(SkillSlot curSlot, int slotIndex)
    {
        //技能进入冷却
        //后续要是想要什么持续技能，切换技能，再加上逻辑就可以，想过是可以实现的
        //切换+冷却本质是技能替换，然后新技能开局有个cd
        curSlot.skill.StartSkill(_entity, out SkillInfo runInfo);
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

    private int CheckInput()
    {
        int curSkillPressed = -1;
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            if (_inputModel == null)
                return curSkillPressed;
        }

        if (!SkillInputRuntime.CanUseActiveSkillsInCurrentPhase())
            return curSkillPressed;

        bool[] pressInfo =
        {
            _inputModel.Skill1Pressed,
            _inputModel.Skill2Pressed,
            _inputModel.Skill3Pressed,
            _inputModel.Skill4Pressed,
            _inputModel.Skill5Pressed
        };
        //GF.Log( _inputModel.Skill1Pressed.ToString());
        for (int i = 0; i < _skillSlots.Count; i++)
        {
            if (!SkillRuntimeDataModel.IsUnlockedActiveSkillSlot(i))
                continue;

            if (!SkillRuntimeDataModel.HasRemainingUsageAt(i))
                continue;

            if (!pressInfo[i])
                continue;

            if (_skillSlots[i].canCast)
                return i;
        }
        return curSkillPressed;
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
            cooldown.SetTarget((Fix64)skill.ResolveCooldownInterval(skillInfo.Level));
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
}
public class SkillSlot: ISkillLocker
{
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
}
