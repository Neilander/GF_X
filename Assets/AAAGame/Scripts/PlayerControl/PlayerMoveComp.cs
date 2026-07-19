using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMoveComp : IMoveComp
{
    private InputModel _inputModel;
    private IEntityContext _ctx;
    private bool _isMoving = false;
    private Vector3 _moveDirection = Vector3.zero;

    public void Move(Fix64 deltaTime)
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            return;
        }


        FixVector2 translated = _inputModel.CurrentLogicFrame.WorldMove;
        Fix64 magnitude = FixVector2.Magnitude(translated);
        FixVector2 direction = magnitude > Fix64.Zero
            ? new FixVector2(translated.x / magnitude, translated.y / magnitude)
            : FixVector2.Zero;

        // 更新移动状态
        _isMoving = magnitude > (Fix64)0.001f;
        _moveDirection = _isMoving
            ? new Vector3((float)direction.x, 0f, (float)direction.y)
            : Vector3.zero;

        Fix64 speed = DistanceUnitConverter.ConvertToWorld(_ctx.GetProperty(CreatureMainProperty.Speed));
        _ctx.MoveExecutor.SetInputFixed(direction * speed);

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
