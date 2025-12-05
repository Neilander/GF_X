using UnityEngine;

public class NoAtkComp : MonoBehaviour, IAtkComp
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


