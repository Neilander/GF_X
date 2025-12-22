using GameFramework;
using System;

public class PropertyMultiplicativeModifier : IPropertyMultiplicativeModifier<Fix64>
{
    protected Func<Fix64> _valueGetter;
    public Fix64 Value => _valueGetter?.Invoke() ?? Fix64.Zero;
    public int Priority => _priority;
    protected int _priority;

    public static PropertyMultiplicativeModifier Create(Fix64 value, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyMultiplicativeModifier>();
        modifier._valueGetter = () => value;
        modifier._priority = priority;
        return modifier;
    }

    public static PropertyMultiplicativeModifier Create(Func<Fix64> valueGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyMultiplicativeModifier>();
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
