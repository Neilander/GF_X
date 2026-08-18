public sealed class HeroHealTargetingComp : HealTargetingCompBase
{
    protected override bool SupportsOwner(IEntityContext owner) => owner.IsHeroEntity;
    protected override string OwnerKind => "hero";
    protected override Fix64 GetScanRange(Fix64 attackRange) => attackRange;
    protected override Fix64 GetRetentionRange(Fix64 attackRange) => attackRange;
}
