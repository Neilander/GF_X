public sealed class HeroTargetingComp : AttackRangeTargetingCompBase
{
    protected override bool SupportsOwner(IEntityContext owner)
    {
        return owner is IHeroLogicContext hero && hero.IsHeroEntity;
    }

    protected override string OwnerKind => "hero";
}
