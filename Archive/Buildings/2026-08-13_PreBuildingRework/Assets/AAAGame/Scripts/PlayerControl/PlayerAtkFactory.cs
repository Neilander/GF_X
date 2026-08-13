using UnityEngine;

[CreateAssetMenu(fileName = "PlayerAtkFactory", menuName = "Atk Factory/PlayerAtk")]
public class PlayerAtkFactory : AtkCompFactory
{
    public override IAtkComp CreateAtkComp(IEntityContext gmo)
    {
        var comp = new MoveAtkComp();
        gmo.SetAtkComp(comp);
        comp.Init(gmo);
        return comp;
    }
}
