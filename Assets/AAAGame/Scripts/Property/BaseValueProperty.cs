using GameFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

//有基础值的属性，比如血量、攻击力、防御力等
public class BaseValueProperty : ValueProperty
{
    private Fix64 _baseValue; //基础值

    public static BaseValueProperty Create(Fix64 baseValue, string propertyId)
    {
        var property = ReferencePool.Acquire<BaseValueProperty>();
        property._propertyId = propertyId;
        property._baseValue = baseValue;
        return property;
    }
    public override Fix64 GetValue()
    {
        if (!_isDirty) return _cacheValue;
        var ret = _baseValue;
        ApplyModify(ref ret);
        _cacheValue = ret;
        _isDirty = false;
        return _cacheValue;
    }
    public void SetBaseValue(Fix64 baseValue)
    {
        if (_baseValue == baseValue) return;
        _baseValue = baseValue;
        MakeDirty();
    }

    protected override void ApplyModify(ref Fix64 ret)
    {
        GF.Log("执行了修改器"+_modifiers.Count);
        ProcessModifiers(ref ret, EModifierMergeType.DirectAdditive);
        ProcessModifiers(ref ret, EModifierMergeType.DirectMultiplicative);
        ProcessModifiers(ref ret, EModifierMergeType.FinalAdditive);
        ProcessModifiers(ref ret, EModifierMergeType.FinalMultiplicative);
        ProcessModifiers(ref ret, EModifierMergeType.Override);
        ProcessModifiers(ref ret, EModifierMergeType.Clamp);
    }

    public override void Clear()
    {
        _baseValue = Fix64.Zero;
        base.Clear();
    }

    public override string ToString()
    {
        return base.ToString()+" Type: BaseValueProperty --------- Base Value:"+ _baseValue;
    }
}
