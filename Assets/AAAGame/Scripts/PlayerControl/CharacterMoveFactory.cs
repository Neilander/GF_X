namespace AAAGame.Scripts.PlayerControl
{
    using UnityEngine;

    [CreateAssetMenu(fileName = "CharacterMoveFactory", menuName = "Move Factory/CharacterMove")]
    public class CharacterMoveFactory : MoveCompFactory
    {
        public override IMoveComp CreateMoveComp(MAEntity gmo)
        {
            var comp = new CharacterMoveComp();
            gmo.SetMoveComp(comp);
            comp.Init(gmo);
            return comp;
        }
    }
}