using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkillEntity : MAEntity
{
    public ISkillComp skillComp { get; protected set; }
    private Transform _rangeTrans;
    
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        //初始化移动和攻击组件

        SetUpSkillComp();
        _rangeTrans = transform.Find("CastRange");
    }
    
    protected override void Update()
    {
        base.Update();
        
        if(CanRun(skillComp))
            skillComp.Skill();
    }

    protected  virtual void SetUpSkillComp()
    {
        GF.LogError("目前还没有Implement通用的skill装载");
    }

    public virtual void ShowCastRange(float ratio)
    {
        _rangeTrans.localScale = new Vector3(ratio,ratio,1);
        _rangeTrans.gameObject.SetActive(ratio > 0.01f);
    }

    public virtual void HideCastRange()
    {
        ShowCastRange(0f);
    }

    public void SetSkillComp(ISkillComp newSkillComp)=> skillComp = newSkillComp;

}
