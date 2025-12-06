using UnityEngine;

public class NoMoveComp :  IMoveComp
{
    public void Move()
    {
        //这是空行为，什么都不做
        
    }
}

public interface IMoveComp
{
    void Move();
}
