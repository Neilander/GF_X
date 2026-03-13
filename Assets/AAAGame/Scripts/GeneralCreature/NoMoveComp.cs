using UnityEngine;

public class NoMoveComp :  IMoveComp
{
    public void Init(MAEntity entity)
    {
    }

    public void Move()
    {
        //这是空行为，什么都不做
        
    }

    public void MoveTo(Vector3 destination)
    {
    }

    public void StopMove()
    {
    }

    public void ShutDown() { }
    public void Resume() { }
}