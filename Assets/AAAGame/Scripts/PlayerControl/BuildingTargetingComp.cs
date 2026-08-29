public class BuildingTargetingComp : AttackRangeTargetingCompBase
{
    protected override bool SupportsOwner(IEntityContext owner)
    {
        return owner != null && owner.IsBuildingEntity;
    }

    protected override string OwnerKind => "building";
    protected override bool PropagatesAggroCandidate => true;
}
