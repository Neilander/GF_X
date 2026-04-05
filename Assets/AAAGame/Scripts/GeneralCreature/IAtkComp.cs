public interface IAtkComp : ICapability
{
    void Init(IEntityContext ctx);
    void Attack(float deltaTime);
    
    /// <summary>
    /// 是否正在攻击
    /// </summary>
    bool IsAttacking { get; }
}