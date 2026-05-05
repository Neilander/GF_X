public partial class SoldierEntity
{
    public override void OnDead()
    {
        AAAGame.Effec.UnitDeathDissolveEffect.PlayFor(gameObject);
        base.OnDead();
    }
}
