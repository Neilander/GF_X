using UnityEngine;

namespace AAAGame.Scripts.Entity
{
    public class PlayerBrain : IControlBrain
    {
        private readonly InputModel _input;

        public PlayerBrain()
        {
            _input = GF.DataModel.GetDataModel<InputModel>();
        }

        private LogicInputFrame CurrentInput => _input.CurrentLogicFrame;

        public Vector2 Move => CurrentInput.WorldMove;
        public FixVector2 MoveFixed => CurrentInput.WorldMove;
        public bool Attack => false;
        public bool Skill1 => false;
        public bool Skill2 => false;
        public bool Skill3 => false;
        public bool Skill4 => false;
        public bool Skill5 => false;
    }
}
