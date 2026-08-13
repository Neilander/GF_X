namespace AAAGame.Scripts.PlayerControl
{
    using UnityEngine;

    [CreateAssetMenu(fileName = "CharacterAtkFactory", menuName = "Atk Factory/CharacterAtk")]
    public class CharacterAtkFactory : AtkCompFactory
    {
        public override IAtkComp CreateAtkComp(IEntityContext gmo)
        {
            var comp = new DirectAtkComp();
            gmo.SetAtkComp(comp);
            comp.Init(gmo);
            return comp;
        }
    }
}
