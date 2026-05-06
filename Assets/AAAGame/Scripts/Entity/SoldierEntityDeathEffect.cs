public partial class SoldierEntity
{
    public override void OnDead()
    {
        AAAGame.Effect.UnitDeathDissolveEffect.PlayFor(gameObject);
        base.OnDead();
    }
}
