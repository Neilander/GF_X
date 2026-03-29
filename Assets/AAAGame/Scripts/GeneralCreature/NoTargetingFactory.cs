using GameFramework.Resource;
using UnityEngine;

namespace AAAGame.Scripts.GeneralCreature
{

    [CreateAssetMenu(fileName = "NoTargetingFactory", menuName = "Targeting Factory/NoTargeting")]
    public class NoTargetingFactory : TargetingCompFactory
    {
        public override ITargetingComp CreateTargetingComp(MAEntity gmo)
        {
            NoTargetingComp comp = new NoTargetingComp();
            gmo.SetTargetingComp(comp);
            comp.Init(gmo);
            return comp;
        }
    }
    
    public abstract class TargetingCompFactory : ScriptableObject
    {
        public abstract ITargetingComp CreateTargetingComp(MAEntity gmo);
    
        // 增加统一的资源加载回调
        public static LoadAssetCallbacks TargetingFactoryCallBack = new LoadAssetCallbacks(
            (assetName, asset, duration, userData) => 
                (asset as TargetingCompFactory)?.CreateTargetingComp(userData as MAEntity));
    }
}