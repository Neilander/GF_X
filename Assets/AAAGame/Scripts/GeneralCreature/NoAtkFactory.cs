using GameFramework.Resource;
using UnityEngine;

[CreateAssetMenu(fileName = "NoAtkFactory", menuName = "Atk Factory/NoAtk")]
public class NoAtkFactory : AtkCompFactory
{
    public override IAtkComp CreateAtkComp(MAEntity gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        NoAtkComp comp = new NoAtkComp();
        gmo.SetAtkComp(comp);
        return comp;
    }
}

public abstract class AtkCompFactory : ScriptableObject
{
    public abstract IAtkComp CreateAtkComp(MAEntity gmo);
    public static LoadAssetCallbacks AtkFactoryCallBack = new LoadAssetCallbacks(
        (assetName,  asset, duration,  userData)=> (asset as AtkCompFactory)?.CreateAtkComp(userData as MAEntity));
}


