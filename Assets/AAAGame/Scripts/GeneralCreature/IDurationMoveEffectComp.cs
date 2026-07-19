using UnityEngine;

public interface IDurationMoveEffectComp : ICapability
{
    void Init(IEntityContext ctx);
    int StartDurationAdditionalMove(float duration, Vector3 speed);
    int StartDurationAdditionalMove(Fix64 duration, FixVector2 speed);
    int StartDurationOverrideMove(float duration, Vector3 speed);
    int StartDurationOverrideMove(Fix64 duration, FixVector2 speed);
    
    void StopAddtionalMove(int index);
    void StopOverrideMove(int index);
    
    void StopAllMove();
    void ApplyEffect(Fix64 deltaTime);
}
