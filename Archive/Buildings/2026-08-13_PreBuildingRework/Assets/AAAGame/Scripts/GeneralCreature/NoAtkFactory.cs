using GameFramework.Resource;
using UnityEngine;

[CreateAssetMenu(fileName = "NoAtkFactory", menuName = "Atk Factory/NoAtk")]
public class NoAtkFactory : AtkCompFactory
{
    public override IAtkComp CreateAtkComp(IEntityContext gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        NoAtkComp comp = new NoAtkComp();
        gmo.SetAtkComp(comp);
        comp.Init(gmo);
        return comp;
    }
}

public abstract class AtkCompFactory : ScriptableObject
{
    public abstract IAtkComp CreateAtkComp(IEntityContext gmo);
    public static LoadAssetCallbacks AtkFactoryCallBack = new LoadAssetCallbacks(
        (assetName,  asset, duration,  userData)=> (asset as AtkCompFactory)?.CreateAtkComp(userData as IEntityContext));
}






