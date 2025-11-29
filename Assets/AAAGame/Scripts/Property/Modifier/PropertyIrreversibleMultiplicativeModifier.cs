using GameFramework;
using System;

public class PropertyIrreversibleMultiplicativeModifier : PropertyMultiplicativeModifier
{
    public new static PropertyIrreversibleMultiplicativeModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyIrreversibleMultiplicativeModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }

    public new static PropertyIrreversibleMultiplicativeModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyIrreversibleMultiplicativeModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }
}
