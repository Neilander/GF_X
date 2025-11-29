using GameFramework;
using System;
using System.Collections.Generic;
using System.Linq;

public class ComputeValueProperty : ValueProperty
{
    private Func<Fix64> _computeFunc;

    public static ComputeValueProperty Create(Func<Fix64> computeFunc, string propertyId)
    {
        var property = ReferencePool.Acquire<ComputeValueProperty>();
        property._computeFunc = computeFunc;
        property._propertyId = propertyId;
        return property;
    }

    public override Fix64 GetValue()
    {
        if (!_isDirty) return _cacheValue;
        var ret = _computeFunc();
        ApplyModify(ref ret);
        _cacheValue = ret;
        _isDirty = false;
        return _cacheValue;
    }

    protected override void ApplyModify(ref Fix64 ret)
    {
        ProcessModifiers(ref ret, EModifierMergeType.Override);
        ProcessModifiers(ref ret, EModifierMergeType.Clamp);
    }

    public override void Clear()
    {
        _computeFunc = null;
        base.Clear();
    }
    
    public override string ToString()
    {
        return base.ToString()+" Type: ComputeValueProperty";
    }
}
