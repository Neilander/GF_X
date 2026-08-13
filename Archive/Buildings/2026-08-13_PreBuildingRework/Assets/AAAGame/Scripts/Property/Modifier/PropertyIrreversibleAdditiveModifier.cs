using GameFramework;
using System;

public class PropertyIrreversibleAdditiveModifier : PropertyAdditiveModifier
{
    public new static PropertyIrreversibleAdditiveModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyIrreversibleAdditiveModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }
    public new static PropertyIrreversibleAdditiveModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyIrreversibleAdditiveModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }
}
