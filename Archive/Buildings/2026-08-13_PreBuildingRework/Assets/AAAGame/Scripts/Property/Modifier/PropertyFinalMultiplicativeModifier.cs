using GameFramework;
using System;

public class PropertyFinalMultiplicativeModifier : PropertyMultiplicativeModifier
{
    public new static PropertyFinalMultiplicativeModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyFinalMultiplicativeModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }

    public new static PropertyFinalMultiplicativeModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyFinalMultiplicativeModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }
}
