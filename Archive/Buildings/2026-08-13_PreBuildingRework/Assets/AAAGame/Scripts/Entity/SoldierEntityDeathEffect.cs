public partial class SoldierEntity
{
    protected override void OnLogicUnitDiedPresentation(IEntityContext attacker)
    {
        AAAGame.Effect.UnitDeathDissolveEffect.PlayFor(gameObject);
        base.OnLogicUnitDiedPresentation(attacker);
    }
}
