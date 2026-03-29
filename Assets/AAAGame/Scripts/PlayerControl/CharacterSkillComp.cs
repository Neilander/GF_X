using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterSkillComp : ISkillComp
{
    private SkillEntity _entity;
    
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
        if (_entity?.Brain == null) return -1;

        // Skill1/2/3
        bool[] pressInfo = { _entity.Brain.Skill1, _entity.Brain.Skill2, _entity.Brain.Skill3 };

        for (int i = 0; i < _skillSlots.Count; i++)
        {
            if (!pressInfo[i]) continue;
            if (_skillSlots[i].canCast) return i;
        }
        return -1;
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
