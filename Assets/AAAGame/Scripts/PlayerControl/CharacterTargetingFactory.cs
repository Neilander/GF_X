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
    [Tooltip("自己扫描到敌人时，把敌人广播给此半径内的同阵营友军。0 = 关掉广播。")]
    public float defaultAlertRadius = 5f;

    public override ITargetingComp CreateTargetingComp(IEntityContext gmo)
    {
        ITargetingComp comp = WeaponTargetRules.IsHealingWeapon(gmo?.WeaponComp?.Data?.Type ?? WeaponType.None)
            ? new HealTargetingComp()
            : new CharacterTargetingComp();

        comp.Init(gmo);

        // 赋予初始面板值
        comp.AggroRangeFixed = (Fix64)defaultAggroRange;
        comp.ForgetRangeFixed = (Fix64)defaultForgetRange;
        comp.FollowSearchRangeFixed = (Fix64)defaultFollowRange;
        comp.AlertRadiusFixed = (Fix64)defaultAlertRadius;

        gmo.SetTargetingComp(comp);
        return comp;
    }
}
