using AAAGame.Scripts.GeneralCreature;
using UnityEngine;



// 具体实现
[CreateAssetMenu(fileName = "CharacterTargetingFactory", menuName = "Targeting Factory/CharacterTargeting")]
public class CharacterTargetingFactory : TargetingCompFactory
{
    [Header("默认索敌配置")]
    public float defaultAggroRange = 6f;
    public float defaultForgetRange = 8f;
    public float defaultFollowRange = 30f;
    public override ITargetingComp CreateTargetingComp(IEntityContext gmo)
    {
        if (gmo == null)
            throw new System.ArgumentNullException(nameof(gmo));

        ITargetingComp comp;
        bool healingWeapon = WeaponTargetRules.IsHealingWeapon(gmo.WeaponComp?.Data?.Type ?? WeaponType.None);
        if (gmo.IsHeroEntity)
            comp = healingWeapon ? new HeroHealTargetingComp() : new HeroTargetingComp();
        else if (healingWeapon)
            comp = new UnitHealTargetingComp();
        else
            comp = new CharacterTargetingComp();

        comp.Init(gmo);

        if (comp is ITargetSearchRangeComp searchRange)
        {
            searchRange.AggroRangeFixed = (Fix64)defaultAggroRange;
            searchRange.ForgetRangeFixed = (Fix64)defaultForgetRange;
        }
        if (comp is IFollowTargetingComp followTargeting)
            followTargeting.FollowSearchRangeFixed = (Fix64)defaultFollowRange;

        gmo.SetTargetingComp(comp);
        return comp;
    }
}
