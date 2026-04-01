namespace AAAGame.Scripts.PlayerControl
{
    using UnityEngine;

    [CreateAssetMenu(fileName = "CharacterAtkFactory", menuName = "Atk Factory/CharacterAtk")]
    public class CharacterAtkFactory : AtkCompFactory
    {
        public BasicAction atkAction1;
        public BasicAction atkAction2;
        public BasicAction atkAction3;

        public override IAtkComp CreateAtkComp(MAEntity gmo)
        {
            var comp = new CharacterAttackComp();
            gmo.SetAtkComp(comp);
            comp.Init(gmo);

            comp.actions = new[] { atkAction1, atkAction2, atkAction3 };
            return comp;
        }
    }
}