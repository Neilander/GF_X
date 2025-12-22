using GameFramework;
using System;

public class PropertyClampModifier : IPropertyClampModifier<Fix64>
{
    private Func<Fix64> _minGetter;
    private Func<Fix64> _maxGetter;
    public Fix64 Min => _minGetter?.Invoke() ?? Fix64.Zero;
    public Fix64 Max => _maxGetter?.Invoke() ?? Fix64.Zero;
    public int Priority => _priority;
    private int _priority;

    public static PropertyClampModifier Create(Fix64 min, Fix64 max, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyClampModifier>();
        modifier._minGetter = () => min;
        modifier._maxGetter = () => max;
        modifier._priority = priority;
        return modifier;
    }

    public static PropertyClampModifier Create(Func<Fix64> minGetter, Func<Fix64> maxGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyClampModifier>();
        modifier._minGetter = minGetter;
        modifier._maxGetter = maxGetter;
        modifier._priority = priority;
        return modifier;
    }

    public static PropertyClampModifier Create(Func<FixVector2> rangeGetter, int priority = 0)
    {
        var modifier = ReferencePool.Acquire<PropertyClampModifier>();
        modifier._minGetter = () => rangeGetter().x;
        modifier._maxGetter = () => rangeGetter().y;
        modifier._priority = priority;
        return modifier;
    }

    public void Clear()
    {
        _minGetter = null;
        _maxGetter = null;
        _priority = 0;
    }
}
