
using UnityEngine;

public interface IMoveComp : ICapability
{
    void Init(IEntityContext ctx);
    void Move(float deltaTime);

    void MoveTo(Vector3 destination);
    void StopMove();

    /// <summary>
    /// 获取当前 NavMesh 路径的下一步归一化方向，无路径时返回 Vector3.zero。
    /// </summary>
    Vector3 GetNavDirection();
}
