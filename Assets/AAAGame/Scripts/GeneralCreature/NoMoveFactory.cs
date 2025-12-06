using GameFramework.Resource;
using UnityEngine;

[CreateAssetMenu(fileName = "NoMoveFactory", menuName = "Move Factory/NoMove")]
public class NoMoveFactory : MoveCompFactory
{
    public override IMoveComp CreateMoveComp(MAEntity gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        NoMoveComp comp = new NoMoveComp();
        gmo.SetMoveComp(comp);
        return comp;
    }
}

public abstract class MoveCompFactory : ScriptableObject
{
    public abstract IMoveComp CreateMoveComp(MAEntity gmo);
    public static LoadAssetCallbacks MoveFactoryCallBack = new LoadAssetCallbacks(
        (assetName,  asset, duration,  userData)=> (asset as MoveCompFactory)?.CreateMoveComp(userData as MAEntity));
}
