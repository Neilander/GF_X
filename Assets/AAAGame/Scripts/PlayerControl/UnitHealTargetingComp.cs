public sealed class UnitHealTargetingComp : HealTargetingCompBase, ITargetSearchRangeComp, IFollowTargetingComp
{
    private Fix64 m_AggroRange = (Fix64)6;
    private Fix64 m_ForgetRange = (Fix64)8;
    private Fix64 m_FollowSearchRange = (Fix64)30;

    public IEntityContext FollowTarget { get; private set; }
    public Fix64 AggroRangeFixed { get => m_AggroRange; set => m_AggroRange = LogicTargetingRange.Require(value, nameof(AggroRangeFixed)); }
    public Fix64 ForgetRangeFixed { get => m_ForgetRange; set => m_ForgetRange = LogicTargetingRange.Require(value, nameof(ForgetRangeFixed)); }
    public Fix64 FollowSearchRangeFixed { get => m_FollowSearchRange; set => m_FollowSearchRange = LogicTargetingRange.Require(value, nameof(FollowSearchRangeFixed)); }

    protected override bool SupportsOwner(IEntityContext owner) => !owner.IsBuildingEntity && !owner.IsHeroEntity;
    protected override string OwnerKind => "non-hero unit";
    protected override Fix64 GetScanRange(Fix64 attackRange) => Fix64.Max(attackRange, m_AggroRange);
    protected override Fix64 GetRetentionRange(Fix64 attackRange) => Fix64.Max(attackRange, m_ForgetRange);

    protected override void OnInitialized() => FollowTarget = null;

    protected override void AfterTargetingUpdate()
    {
        if (FollowTarget != null
            && (!FollowTarget.IsRegisteredInLogicWorld()
                || !FollowTarget.Alive
                || FollowTarget.Side != Context.Side
                || Context.LogicFrameCenterDistanceFixed(FollowTarget) > m_FollowSearchRange))
        {
            FollowTarget = null;
        }
        IEntityContext player = EntityRegistry.Player;
        if (FollowTarget == null
            && player != null
            && player.Alive
            && player.Side == Context.Side
            && Context.LogicFrameCenterDistanceFixed(player) <= m_FollowSearchRange)
        {
            FollowTarget = player;
        }
    }

    protected override void ClearRoleState() => FollowTarget = null;

    protected override void WriteRoleState(LogicStateHasher hasher)
    {
        hasher.Add(FollowTarget != null && FollowTarget.LogicEntityId.IsValid ? FollowTarget.LogicEntityId.Value : 0);
        hasher.Add(m_AggroRange.RawValue);
        hasher.Add(m_ForgetRange.RawValue);
        hasher.Add(m_FollowSearchRange.RawValue);
    }
}
