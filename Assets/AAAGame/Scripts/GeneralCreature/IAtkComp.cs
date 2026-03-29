public interface IAtkComp : ICapability
{
    void Init(IEntityContext ctx);
    void Attack(float deltaTime);
}