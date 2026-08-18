public class BuildingTargetingComp : AttackRangeTargetingCompBase
{
    protected override bool SupportsOwner(IEntityContext owner)
    {
        return owner is IBuildingLogicContext;
    }

    protected override string OwnerKind => "building";
}
