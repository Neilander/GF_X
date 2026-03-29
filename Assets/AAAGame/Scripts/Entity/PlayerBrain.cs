using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace AAAGame.Scripts.Entity
{
    public class PlayerBrain : IControlBrain
    {
        private InputModel _input;
        public PlayerBrain() => _input = GF.DataModel.GetDataModel<InputModel>();

        public Vector2 Move =>  InputDirTranslator.Translate(
        new FixVector2(_input.MoveX, _input.MoveY)
            );
        public bool Attack => _input.PlayerAttack;
        public bool Skill1 => _input.Skill1Pressed;
        public bool Skill2 => _input.Skill2Pressed;
        public bool Skill3 => _input.Skill3Pressed;
    }
}