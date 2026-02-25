using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveExecutor : MonoBehaviour
{
    private CharacterController _controller;

    private Vector3 _inputVelocity;
    private Vector3 _externalVelocity;
    private Vector3 _overrideVelocity;
    private bool _hasOverride;

    public void Init(CharacterController controller)
    {
        _controller = controller;
    }

    // 每帧输入
    public void SetInput(Vector3 velocity)
    {
        _inputVelocity = velocity;
    }

    // 外力可叠加
    public void AddExternal(Vector3 velocity)
    {
        _externalVelocity += velocity;
    }

    // 强制覆盖（击飞等）
    public void SetOverride(Vector3 velocity)
    {
        _overrideVelocity = velocity;
        _hasOverride = true;
    }

    public void ClearOverride()
    {
        _hasOverride = false;
    }

    
    public void SetExternal(Vector3 velocity)
    {
        _externalVelocity = velocity;
    }

    public void Execute()
    {
        Vector3 finalVelocity = _hasOverride
            ? _overrideVelocity
            : _inputVelocity + _externalVelocity;

        _controller.Move(finalVelocity * Time.deltaTime);

        // 输入每帧重置（非常重要）
        _inputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
    }
}
