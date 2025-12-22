using GameFramework;
using System;

public class PropertyIrreversibleOverrideModifier : PropertyOverrideModifier
{
    public new static PropertyIrreversibleOverrideModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyIrreversibleOverrideModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }

    public new static PropertyIrreversibleOverrideModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyIrreversibleOverrideModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }
}
