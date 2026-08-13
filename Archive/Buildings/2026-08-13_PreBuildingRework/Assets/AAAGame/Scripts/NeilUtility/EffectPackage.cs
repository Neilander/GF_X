using UnityEngine;
using System.Collections.Generic;

namespace BaseUtility
{
    public class EffectPackage
    {
        public int damage;
        public bool directKillPlayer;
        public List<IEPDecoration> decorations{get; private set;} =  new List<IEPDecoration>();
        public EffectPackage(int damage)
        {
            this.damage = damage;
        }
        
        public void AddEffect(IEPDecoration effect)
        {
            decorations.Add(effect);
        }
    }

    public interface IEPDecoration
    {
        void Decorate(ITarget target);
    }

}

