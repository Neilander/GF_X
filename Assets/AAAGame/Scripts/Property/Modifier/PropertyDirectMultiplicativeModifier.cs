using GameFramework;
using System;

public class PropertyDirectMultiplicativeModifier : PropertyMultiplicativeModifier
{
    public new static PropertyDirectMultiplicativeModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyDirectMultiplicativeModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }

    public new static PropertyDirectMultiplicativeModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyDirectMultiplicativeModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }
}
