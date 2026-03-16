
using UnityEngine;

public interface IMoveComp : ICapability
{
    void Init(IEntityContext ctx);
    void Move(float deltaTime);

    void MoveTo(Vector3 destination);
    void StopMove();
}
