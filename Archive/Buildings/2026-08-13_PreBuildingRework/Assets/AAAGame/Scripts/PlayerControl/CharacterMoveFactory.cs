namespace AAAGame.Scripts.PlayerControl
{
    using UnityEngine;

    [CreateAssetMenu(fileName = "CharacterMoveFactory", menuName = "Move Factory/CharacterMove")]
    public class CharacterMoveFactory : MoveCompFactory
    {
        public override IMoveComp CreateMoveComp(IEntityContext gmo)
        {
            if (gmo is not ILogicFrameEntity logicEntity)
                throw new System.InvalidOperationException("CharacterMoveFactory requires ILogicFrameEntity.");
            var comp = new CharacterMoveComp();
            gmo.SetMoveComp(comp);
            comp.Init(gmo, logicEntity.NavigationAgentTypeId);
            return comp;
        }
    }
}
