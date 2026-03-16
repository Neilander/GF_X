using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMoveComp : IMoveComp
{
    private InputModel _inputModel;
    private IEntityContext _ctx;

    public void Move(float deltaTime)
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            return;
        }

        Vector2 translated = InputDirTranslator.Translate(
            new FixVector2(_inputModel.MoveX, _inputModel.MoveY)
        );

        Vector3 move = new Vector3(translated.x, 0f, translated.y);

        float speed = _ctx.GetProperty(CreatureMainProperty.Speed);

        move = move.normalized * speed;

        _ctx.MoveExecutor.SetInput(move * 0.1f);

        // 动画和显示：仅在真实实体上执行
        if (_ctx is GeneralCreature gc)
        {
            if (gc.animator != null)
                gc.animator.SetFloat("Speed", move.magnitude);

            if (gc.display != null)
            {
                if (translated.x < -0.01f)
                {
                    Vector3 scale = gc.display.localScale;
                    scale.x = -Mathf.Abs(scale.x);
                    gc.display.localScale = scale;
                }
                else if (translated.x > 0.01f)
                {
                    Vector3 scale = gc.display.localScale;
                    scale.x = Mathf.Abs(scale.x);
                    gc.display.localScale = scale;
                }
            }
        }
    }

    public void MoveTo(Vector3 destination)
    {
        throw new System.NotImplementedException();
    }

    public void StopMove()
    {
        throw new System.NotImplementedException();
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
    }

    public void ShutDown() { }
    public void Resume() { }
}
