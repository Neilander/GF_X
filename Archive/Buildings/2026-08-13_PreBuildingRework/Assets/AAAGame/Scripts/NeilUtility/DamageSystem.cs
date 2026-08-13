using UnityEngine;

namespace BaseUtility
{
    public enum TempActions
    {
        KillPlayer
    }

    public static class DamageSystem
    {
        public static void ProcessDamage( EffectPackage package, ITarget target){

            foreach (IEPDecoration deco in package.decorations)
            {
                deco.Decorate(target);
            }
        }
    }

    public interface ITarget
    {
    }

}