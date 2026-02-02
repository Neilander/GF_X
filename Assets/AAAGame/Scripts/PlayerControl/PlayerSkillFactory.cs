using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.Resource;

[CreateAssetMenu(fileName = "PlayerSkillFactory", menuName = "Skill Factory/PlayerSkill")]
public class PlayerSkillFactory : SkillCompFactory
{
    [Header("玩家技能，目前只能有3个")]
    public List<BasicSkill> skills;
    public override ISkillComp CreateSkillComp(SkillEntity gmo)
    {
        PlayerSkillComp comp = new PlayerSkillComp();
        
        gmo.SetSkillComp(comp);
        comp.Init(gmo,skills);
        //BasicAction[] actionSet = {atkAction1, atkAction2, atkAction3};
        //comp.actions = actionSet;
        return comp;
    }
}

public abstract class SkillCompFactory : ScriptableObject
{
    public abstract ISkillComp CreateSkillComp(SkillEntity gmo);
    public static LoadAssetCallbacks SkillFactoryCallBack = new LoadAssetCallbacks(
        (assetName,  asset, duration,  userData)=> (asset as SkillCompFactory)?.CreateSkillComp(userData as SkillEntity));
}
