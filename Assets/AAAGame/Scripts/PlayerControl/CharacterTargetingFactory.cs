using AAAGame.Scripts.GeneralCreature;
using UnityEngine;



// 具体实现
[CreateAssetMenu(fileName = "CharacterTargetingFactory", menuName = "Targeting Factory/CharacterTargeting")]
public class CharacterTargetingFactory : TargetingCompFactory
{
    [Header("默认索敌配置")]
    public float defaultAggroRange = 6f;
    public float defaultForgetRange = 8f;

    public override ITargetingComp CreateTargetingComp(MAEntity gmo)
    {
        var comp = new CharacterTargetingComp();
        comp.Init(gmo);
        
        // 赋予初始面板值
        comp.AggroRange = defaultAggroRange;
        comp.ForgetRange = defaultForgetRange;
        
        gmo.SetTargetingComp(comp);
        return comp;
    }
}