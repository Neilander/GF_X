using UnityEngine;
using System;

public interface IDurationMoveEffectComp : ICapability
{
    void Init(MAEntity entity);
    int StartDurationAdditionalMove(float duration,Vector3 speed, Func<Vector3,Vector3>speedModifier = null);
    int StartDurationOverrideMove(float duration,Vector3 speed, Func<Vector3,Vector3>speedModifier = null);
    
    void StopAddtionalMove(int index);
    void StopOverrideMove(int index);
    
    void StopAllMove();
    void ApplyEffect(float deltaTime);
}