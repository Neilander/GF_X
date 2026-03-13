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

        playerEntity.moveExecutor.SetInput(move*0.1f);
        playerEntity.animator.SetFloat("Speed",move.magnitude);
        if (translated.x < -0.01f)
        {
            Vector3 scale = playerEntity.display.localScale;
            scale.x = -Mathf.Abs(scale.x);
            playerEntity.display.localScale = scale;
        }
        else if (translated.x > 0.01f)
        {
            Vector3 scale = playerEntity.display.localScale;
            scale.x = Mathf.Abs(scale.x);
            playerEntity.display.localScale = scale;
        }
        //GF.Log("移动按键的值是"+ _inputModel.MoveX +","+_inputModel.MoveY);
        //GF.Log("交互按键的值是"+ _inputModel.InteractionPressed);
        

    }

    public void MoveTo(Vector3 destination)
    {
        throw new System.NotImplementedException();
    }

    public void StopMove()
    {
        throw new System.NotImplementedException();
    }

    public void Init(MAEntity entity)
    {
        playerEntity = entity;
    }
    
    public void ShutDown() { }
    public void Resume() { }
}