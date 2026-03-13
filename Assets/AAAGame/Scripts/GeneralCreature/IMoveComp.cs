
using UnityEngine;

public interface IMoveComp : ICapability
{
    void Init(MAEntity entity);
    void Move();
    
    void MoveTo(Vector3 destination); // 新增：寻路到目标点
    void StopMove(); // 新增：停止移动
}