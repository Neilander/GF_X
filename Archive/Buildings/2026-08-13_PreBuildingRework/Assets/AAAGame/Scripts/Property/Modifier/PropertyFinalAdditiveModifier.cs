using GameFramework;
using System;

public class PropertyFinalAdditiveModifier : PropertyAdditiveModifier
{
    public new static PropertyFinalAdditiveModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyFinalAdditiveModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }
    public new static PropertyFinalAdditiveModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyFinalAdditiveModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }
}
