public sealed class HeroTargetingComp : AttackRangeTargetingCompBase
{
    protected override bool SupportsOwner(IEntityContext owner)
    {
        return owner != null && owner.IsHeroEntity;
    }

    protected override string OwnerKind => "hero";
}
