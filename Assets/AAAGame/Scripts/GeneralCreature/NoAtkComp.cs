using UnityEngine;

public class NoAtkComp : IAtkComp
{
    public void Attack()
    {
        //这是空行为，什么都不做
    }
}

public interface IAtkComp
{
    void Attack();
}


