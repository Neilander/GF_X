using UnityEngine;

public class NoAtkComp : IAtkComp
{
    public void Init(MAEntity entity)
    {
        
    }

    public void Attack()
    {
        //这是空行为，什么都不做
    }

    public void ShutDown() { }
    public void Resume() { }
}