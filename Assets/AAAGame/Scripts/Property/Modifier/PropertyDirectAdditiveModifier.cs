using GameFramework;
using System;

public class PropertyDirectAdditiveModifier : PropertyAdditiveModifier
{
    public new static PropertyDirectAdditiveModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyDirectAdditiveModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }

    public new static PropertyDirectAdditiveModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyDirectAdditiveModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }
}
