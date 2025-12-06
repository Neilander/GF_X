using UnityEngine;

[CreateAssetMenu(fileName = "PlayerMoveFactory", menuName = "Move Factory/PlayerMove")]
public class PlayerMoveFactory : MoveCompFactory
{
    public override IMoveComp CreateMoveComp(MAEntity gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        PlayerMoveComp comp = new PlayerMoveComp();
        gmo.SetMoveComp(comp);
        return comp;
    }
}