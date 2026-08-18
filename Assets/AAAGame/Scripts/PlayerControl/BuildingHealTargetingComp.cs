public sealed class BuildingHealTargetingComp : HealTargetingCompBase
{
    protected override bool SupportsOwner(IEntityContext owner) => owner.IsBuildingEntity;
    protected override string OwnerKind => "building";
    protected override Fix64 GetScanRange(Fix64 attackRange) => attackRange;
    protected override Fix64 GetRetentionRange(Fix64 attackRange) => attackRange;
}
