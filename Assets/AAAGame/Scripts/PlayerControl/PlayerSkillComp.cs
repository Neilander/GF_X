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
            cooldown.Init((Fix64)5f, false);

            _skillSlots.Add(new SkillSlot
            {
                skill = skillSet[i],
                runtime = new SkillRuntime(),
                cooldown = cooldown
            });
        }

    }
    
    public void Skill()
    {
        //技能冷却
        CoolDown();
        UpdateSkillRuntime();
        
        //检测输入
        int curSkillPressed = CheckInput();
        
        if(curSkillPressed != -1)
            GF.Log("检测到技能："+(curSkillPressed+1));
        
        
        //如果冷却好了，就触发技能
        if (curSkillPressed == -1)
            return;
        
        //根据技能的需求，关闭其他comp和技能输入
    }

    public void ShutDown()
    {
    }

    public void Resume()
    {
    }

    private void CoolDown()
    {
        foreach (var slot in _skillSlots)
        {
            slot.cooldown.Tick((Fix64)Time.deltaTime);
            slot.runtime.isCoolingDown = !slot.cooldown.IsFinished();
        }
    }
    
    private void UpdateSkillRuntime()
    {
        foreach (var slot in _skillSlots)
        {
            slot.runtime.canCast =
                !slot.runtime.isCoolingDown &&
                !slot.runtime.isBanned;
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

            if (_skillSlots[i].runtime.canCast)
                return i;
        }

        


        return curSkillPressed;
    }
    
    
}
class SkillSlot
{
    public BasicSkill skill;
    public SkillRuntime runtime;
    public GeneralCounter cooldown;
}

public class SkillRuntime
{
    public bool isBanned = false;
    public bool isCoolingDown = true;
    
    //整合最终判断
    public bool canCast = false;
}

