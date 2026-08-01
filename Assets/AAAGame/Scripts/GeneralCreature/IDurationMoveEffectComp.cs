using UnityEngine;

public readonly struct DisplacementPullTetherState
{
    public DisplacementPullTetherState(int effectId, LogicEntityId sourceEntityId)
    {
        EffectId = effectId;
        SourceEntityId = sourceEntityId;
    }

    public int EffectId { get; }
    public LogicEntityId SourceEntityId { get; }
}

public interface IDurationMoveEffectComp : ICapability
{
    bool IsInLossOfBalance { get; }
    FixVector2 DisplacementVelocity { get; }
    void Init(IEntityContext ctx);
    int StartDurationAdditionalMove(Fix64 duration, FixVector2 speed);
    int StartDurationOverrideMove(Fix64 duration, FixVector2 speed);
    bool TryApplyKnockback(FixVector2 direction, Fix64 strengthLevel);
    bool TryStartPull(LogicEntityId sourceEntityId, Fix64 strengthLevel);
    void CommitStaticCollision(FixVector2 firstHitNormal);
    void CopyActivePullTethers(System.Collections.Generic.List<DisplacementPullTetherState> results);
    
    void StopAddtionalMove(int index);
    void StopOverrideMove(int index);
    
    void StopAllMove();
    void ApplyEffect(Fix64 deltaTime);
}
