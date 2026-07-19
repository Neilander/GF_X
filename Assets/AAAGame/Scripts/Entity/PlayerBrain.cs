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
        public bool Attack => CurrentInput.WasPressed(LogicInputButton.PlayerAttack);
        public bool Skill1 => CurrentInput.WasPressed(LogicInputButton.Skill1);
        public bool Skill2 => CurrentInput.WasPressed(LogicInputButton.Skill2);
        public bool Skill3 => CurrentInput.WasPressed(LogicInputButton.Skill3);
        public bool Skill4 => CurrentInput.WasPressed(LogicInputButton.Skill4);
        public bool Skill5 => CurrentInput.WasPressed(LogicInputButton.Skill5);
    }
}
