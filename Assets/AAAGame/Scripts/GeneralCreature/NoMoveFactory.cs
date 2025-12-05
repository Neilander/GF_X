using UnityEngine;

[CreateAssetMenu(fileName = "NoMoveFactory", menuName = "Move Factory/NoMove")]
public class NoMoveFactory : MoveCompFactory
{
    public override IMoveComp CreateMoveComp(GameObject gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        
        return gmo.AddComponent<NoMoveComp>();
    }
}

public abstract class MoveCompFactory : ScriptableObject
{
    public abstract IMoveComp CreateMoveComp(GameObject gmo);
}
