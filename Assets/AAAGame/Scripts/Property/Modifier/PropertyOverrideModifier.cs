using GameFramework;
using System;

public class PropertyOverrideModifier : IPropertyOverrideModifier<Fix64>
{
    protected Func<Fix64> _valueGetter;
    public Fix64 Value => _valueGetter?.Invoke() ?? Fix64.Zero;
    public int Priority => _priority;
    protected int _priority;

    public static PropertyOverrideModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyOverrideModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }

    public static PropertyOverrideModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyOverrideModifier>();
        modifier._valueGetter = valueGetter;
        modifier._priority = priority;
        return modifier;
    }

    public void Clear()
    {
        _valueGetter = null;
        _priority = 0;
    }
}
