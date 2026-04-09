using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveExecutor : MonoBehaviour, IMoveExecutor
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
        Execute(Time.deltaTime);
    }

    public void Execute(float deltaTime)
    {
        // 检查CharacterController是否活跃，避免在单位死亡后调用Move方法
        if (_controller == null || !_controller.enabled)
        {
            // 输入每帧重置（非常重要）
            _inputVelocity = Vector3.zero;
            _hasOverride = false;
            _externalVelocity = Vector3.zero;
            return;
        }
        
        Vector3 finalVelocity = _hasOverride
            ? _overrideVelocity
            : _inputVelocity + _externalVelocity;

        // 调试用：整体速度缩放
        finalVelocity *= 0.3f;
        //if(finalVelocity.magnitude > 0.01f)
            //Debug.Log(finalVelocity.magnitude);
        if ((finalVelocity * deltaTime).magnitude > 0.1f)
            _controller.Move(finalVelocity * deltaTime);

        // 输入每帧重置（非常重要）
        _inputVelocity = Vector3.zero;
        _hasOverride = false;
        _externalVelocity = Vector3.zero;
    }
}
