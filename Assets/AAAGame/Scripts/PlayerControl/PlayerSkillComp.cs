using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class PlayerSkillComp : ISkillComp
{
    private SkillEntity _entity;
    private InputModel _inputModel;
    
    public const int SKILL_NUM = 3;
    
    private List<SkillSlot> _skillSlots;
    
    public void Init(SkillEntity entity, List<BasicSkill>skillSet)
    {
        _entity = entity;
        
        _skillSlots = new List<SkillSlot>();

        int count = Mathf.Min(SKILL_NUM, skillSet.Count);

        for (int i = 0; i < count; i++)
        {
            var cooldown = new GeneralCounter();
            cooldown.Init((Fix64)skillSet[i].coolDownInterval, false);

            _skillSlots.Add(new SkillSlot
            {
                skill = skillSet[i],
                cooldown = cooldown
            });
        }

    }
    
    public void Skill()
    {
        //检测正在执行的技能，运行
        UpdateTickingSkill();
        
        
        //技能冷却
        CoolDown();
        UpdateSkillRuntime();
        
        //检测输入
        int curSkillPressed = CheckInput();
        
        //if(curSkillPressed != -1)
            
        
        
        //如果冷却好了，就触发技能
        if (curSkillPressed == -1)
            return;
        GF.Log("使用技能："+(curSkillPressed+1));
        LockCompWhenStart();
        SkillSlot curSlot = _skillSlots[curSkillPressed];
       
        
        //触发技能逻辑（恢复其他技能还没做好）
        StartASkill(curSlot);

    }

    void LockCompWhenStart()
    {
        _entity.LockComp(_entity.atkComp,this);
    }

    void UnlockCompWhenEnd()
    {
        _entity.ResumeComp(_entity.atkComp,this);
    }

    public void ShutDown()
    {
    }

    public void Resume()
    {
    }

    private void UpdateTickingSkill()
    {
        foreach (var slot in _skillSlots)
        {
            if (slot.isTicking)
            {
                //触发技能的tick
                slot.skill.TickSkill(slot.runInfo,Time.deltaTime);
                if (slot.runInfo.isFinished)
                {
                    slot.isTicking = false;
                    slot.runInfo = null;
                    slot.DirectUnlockAll();
                    UnlockCompWhenEnd();
                    
                }
            }
        }
    }

    private void StartASkill(SkillSlot curSlot)
    {
        //技能进入冷却
        //后续要是想要什么持续技能，切换技能，再加上逻辑就可以，想过是可以实现的
        //切换+冷却本质是技能替换，然后新技能开局有个cd
        curSlot.cooldown.Reset();
        //触发其开始函数
        curSlot.isTicking = true;
        curSlot.skill.StartSkill(_entity, out curSlot.runInfo);
        
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
                if(slot!=curSlot&& slot.skill.banWhenOtherSkill)
                    slot.Lock(curSlot);
            }
        }
    }

    private void CoolDown()
    {
        foreach (var slot in _skillSlots)
        {
            slot.cooldown.Tick((Fix64)Time.deltaTime);
            slot.isCoolingDown = !slot.cooldown.IsFinished();
        }
    }
    
    private void UpdateSkillRuntime()
    {
        foreach (var slot in _skillSlots)
        {
            slot.canCast =
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
            return curSkillPressed;
        }
       
        List<bool> pressInfo = new List<bool>{ _inputModel.Skill1Pressed, _inputModel.Skill2Pressed, _inputModel.Skill3Pressed};
        //GF.Log( _inputModel.Skill1Pressed.ToString());
        for (int i = 0; i < _skillSlots.Count; i++)
        {
            if (!pressInfo[i])
                continue;

            if (_skillSlots[i].canCast)
                return i;
        }
        return curSkillPressed;
    }
    
    private void BanOtherSkill(SkillSlot curSlot)
    {
        foreach (var slot in _skillSlots)
        {
            if(slot!= curSlot)
                slot.Lock( curSlot);
        }
    }
}
public class SkillSlot: ISkillLocker
{
    public BasicSkill skill;
    
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

