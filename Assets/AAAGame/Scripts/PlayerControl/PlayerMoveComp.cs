using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMoveComp : IMoveComp
{
    private InputModel _inputModel;
    private IEntityContext _ctx;
    private bool _isMoving = false;
    private Vector3 _moveDirection = Vector3.zero;

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
        
        // 更新移动状态
        _isMoving = move.sqrMagnitude > 0.001f;
        _moveDirection = _isMoving ? move.normalized : Vector3.zero;

        float speed = DistanceUnitConverter.ConvertToWorldFloat(_ctx.GetProperty(CreatureMainProperty.Speed));

        move = move.normalized * speed;

        _ctx.MoveExecutor.SetInput(move);

        // 动画控制由MAEntity统一处理
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

    public void SetNavTarget(Vector3 destination) { }

    public Vector3 GetNavDirection()
    {
        return _moveDirection;
    }
    
    /// <summary>
    /// 是否正在移动
    /// </summary>
    public bool IsMoving => _isMoving;
    
    public void ShutDown() { }
    public void Resume() { }
}
