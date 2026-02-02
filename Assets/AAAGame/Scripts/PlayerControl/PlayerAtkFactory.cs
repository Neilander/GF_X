using UnityEngine;

[CreateAssetMenu(fileName = "PlayerAtkFactory", menuName = "Atk Factory/PlayerAtk")]
public class PlayerAtkFactory : AtkCompFactory
{
    public BasicAction atkAction1;
    public BasicAction atkAction2;
    public BasicAction atkAction3;
    
    public override IAtkComp CreateAtkComp(MAEntity gmo)
    {
        PlayerAttackComp comp = new PlayerAttackComp();
        gmo.SetAtkComp(comp);
        comp.Init(gmo);
        BasicAction[] actionSet = {atkAction1, atkAction2, atkAction3};
        comp.actions = actionSet;
        return comp;
    }
}