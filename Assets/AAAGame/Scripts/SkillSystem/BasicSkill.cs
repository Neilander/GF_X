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

}

public enum CoolDownType
{
    Count
}
