using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMoveComp : IMoveComp
{
    private static readonly Fix64 s_MovingThresholdSquared = Fix64.FromRaw(1);

    private InputModel _inputModel;
    private IEntityContext _ctx;
    private bool _isMoving = false;
    private FixVector2 _moveDirection = FixVector2.Zero;

    public void Move(Fix64 deltaTime)
    {
        if (_inputModel == null)
            throw new System.InvalidOperationException("PlayerMoveComp input model was not bound during initialization.");


        FixVector2 translated = _inputModel.CurrentLogicFrame.WorldMove;
        Fix64 magnitude = FixVector2.Magnitude(translated);
        FixVector2 direction = magnitude > Fix64.Zero
            ? new FixVector2(translated.x / magnitude, translated.y / magnitude)
            : FixVector2.Zero;

        // 更新移动状态
        _isMoving = magnitude > Fix64.FromRaw(5);
        _moveDirection = _isMoving ? direction : FixVector2.Zero;

        Fix64 speed = DistanceUnitConverter.ConvertToWorld(_ctx.GetProperty(CreatureMainProperty.Speed));
        _ctx.MoveExecutor.SetInputFixed(direction * speed, preserveSpeedOnStaticSlide: true);

        // 动画控制由MAEntity统一处理
    }

    public void MoveToFixed(FixVector2 destination)
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
        _inputModel = InputModel.RequireActive();
    }

    public void SetNavTargetFixed(FixVector2 destination) { }

    public FixVector2 NavDirectionFixed => _moveDirection;

    public void CommitResolvedDisplacement(FixVector2 displacement)
    {
        _isMoving = FixVector2.SqrMagnitude(displacement) > s_MovingThresholdSquared;
    }

    /// <summary>
    /// 是否正在移动
    /// </summary>
    public bool IsMoving => _isMoving;

    public void ShutDown() { }
    public void Resume() { }
}
