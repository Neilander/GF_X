using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BasicSkill", menuName = "Skills/BasicSkill")]
public class BasicSkill : ScriptableObject
{
    [Header("基础信息")]
    public List<BasicAction> actions;
    
    public CoolDownType coolDownType;

    public float coolDownInterval;

    [Header("属性")] 
    [Tooltip("不可在其他技能释放时释放，默认开启")]
    public bool banWhenOtherSkill = true;
    [Tooltip("其他技能不可插入，默认关闭")]
    public bool banOtherSkillWhenCast = false;

    public void StartSkill(SkillEntity body, out SkillInfo info)
    {
        info = new SkillInfo();
        info.entity = body;
        info.currentIndex = 0;
        info.isFinished = false;
        actions[0].StartAction(body, out info.currentInfo);
    }

    public void TickSkill(SkillInfo info, float deltaTime)
    {
        actions[info.currentIndex].Tick(info.currentInfo, deltaTime);
        if (info.currentInfo.isFinished)
        {
            if (info.currentIndex < actions.Count-1)
            {
                //说明技能还没执行完，继续执行
                info.currentIndex += 1;
                actions[info.currentIndex].StartAction(info.entity, out info.currentInfo);
            }
            else
            {
                //技能执行完毕了
                info.isFinished = true;
            }
        }
    }
    
    public void InterruptSkill()
    {
        GF.LogError("尝试打断了技能，但是并没有实现。感觉是没问题的，就是因为没具体case，所以想有需求了再实现");
    }

}

public class SkillInfo
{
    public int currentIndex = 0;
    public bool isFinished = false;
    public SkillEntity entity;
    public ActionInfo currentInfo;
}



public enum CoolDownType
{
    Count
}


