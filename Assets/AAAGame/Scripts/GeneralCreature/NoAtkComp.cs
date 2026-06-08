using UnityEngine;

public class NoAtkComp : IAtkComp
{
    public void Init(IEntityContext ctx)
    {
    }

    public void Attack(float deltaTime)
    {
        //这是空行为，什么都不做
    }

    public bool IsAttacking => false;

    public void InterruptAttack(AttackInterruptReason reason = AttackInterruptReason.Forced) { }
    
    public void ShutDown() { }
    public void Resume() { }
}
