using UnityEngine;

public class NoMoveComp : IMoveComp
{
    public void Init(IEntityContext ctx)
    {
    }

    public void Move(Fix64 deltaTime)
    {
        //这是空行为，什么都不做
    }

    public void MoveToFixed(FixVector2 destination)
    {
    }

    public void StopMove()
    {
    }

    public void SetNavTargetFixed(FixVector2 destination) { }
    public FixVector2 NavDirectionFixed => FixVector2.Zero;
    public void ShutDown() { }
    public void Resume() { }
    
    public bool IsMoving => false;
}
