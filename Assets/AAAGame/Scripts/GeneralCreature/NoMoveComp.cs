using UnityEngine;

public class NoMoveComp : IMoveComp
{
    public void Init(IEntityContext ctx)
    {
    }

    public void Move(float deltaTime)
    {
        //这是空行为，什么都不做
    }

    public void MoveTo(Vector3 destination)
    {
    }

    public void StopMove()
    {
    }

    public Vector3 GetNavDirection() => Vector3.zero;
    public void ShutDown() { }
    public void Resume() { }
    
    public bool IsMoving => false;
}
