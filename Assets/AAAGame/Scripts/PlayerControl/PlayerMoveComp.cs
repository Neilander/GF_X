using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMoveComp : IMoveComp
{
    private InputModel _inputModel;
    private MAEntity playerEntity;
    public void Move()
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            return;
        }

        if (playerEntity.CreaturePropertyManager == null)
            return;
        Vector2 translated = InputDirTranslator.Translate(
            new FixVector2(_inputModel.MoveX, _inputModel.MoveY)
        );

        Vector3 move = new Vector3(translated.x, 0f, translated.y);

        float speed = (float)playerEntity.CreaturePropertyManager
            .GetProperty(CreatureMainProperty.Speed);

        move = move.normalized * speed;

        playerEntity.cController.Move(move * Time.deltaTime);
        //GF.Log("移动按键的值是"+ _inputModel.MoveX +","+_inputModel.MoveY);
        //GF.Log("交互按键的值是"+ _inputModel.InteractionPressed);
        

    }

    public void Init(MAEntity entity)
    {
        playerEntity = entity;
    }
}