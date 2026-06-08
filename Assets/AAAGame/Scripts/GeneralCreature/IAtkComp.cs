public interface IAtkComp : ICapability
{
    void Init(IEntityContext ctx);
    void Attack(float deltaTime);
    void InterruptAttack(AttackInterruptReason reason = AttackInterruptReason.Forced);
    
    /// <summary>
    /// 是否正在攻击
    /// </summary>
    bool IsAttacking { get; }
}

public enum AttackInterruptReason
{
    Forced,
    CapabilityLocked,
    Control,
    Disarm,
    Displacement,
    TargetChanged
}
