using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkillEntity : MAEntity
{
    public ISkillComp skillComp { get; protected set; }
    
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        //初始化移动和攻击组件

        SetUpSkillComp();
       
    }
    
    protected override void Update()
    {
        base.Update();
        
        if(CanRun(skillComp))
            skillComp.Skill();
    }

    protected virtual void SetUpSkillComp()
    {
        GF.LogError("目前还没有Implement通用的skill装载");
    }

    public void SetSkillComp(ISkillComp newSkillComp)=> skillComp = newSkillComp;

}
