using GameFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityGameFramework.Runtime;

//消耗类属性，比如当前血量、当前技力等，这些属性会跟随引用值（如最大血量）按比例变化
public class IrreversibleValueProperty : ValueProperty
{
    private Func<Fix64> _referValueFunc;
    private Fix64 _cacheReferValue;
    public static IrreversibleValueProperty Create(Func<Fix64> referValueFunc, string propertyId)
    {
        var property = ReferencePool.Acquire<IrreversibleValueProperty>();
        property._propertyId = propertyId;
        property._referValueFunc = referValueFunc;
        property._cacheValue = referValueFunc();
        property._cacheReferValue = property._cacheValue;
        return property;
    }
    public override Fix64 GetValue()
    {
        // 根因修复：当前值属性要始终跟随引用值（如最大生命）变化，
        // 不能依赖 _isDirty，否则会出现 current 长时间大于 max 的异常状态。
        Fix64 referValue = _referValueFunc();
        if (_cacheReferValue != referValue)
        {
            if (_cacheReferValue == Fix64.Zero)
                _cacheValue = referValue;
            else
                _cacheValue *= referValue / _cacheReferValue;

            _cacheReferValue = referValue;
            _isDirty = true;
        }

        if (!_isDirty) return _cacheValue;

        var ret = _cacheValue;
        ApplyModify(ref ret);
        _cacheValue = ret;
        _isDirty = false;
        return _cacheValue;
    }

    protected override void ApplyModify(ref Fix64 ret)
    {
        ProcessModifiers(ref ret, EModifierMergeType.IrreversibleAdditive);
        ProcessModifiers(ref ret, EModifierMergeType.IrreversibleMultiplicative);
        ProcessModifiers(ref ret, EModifierMergeType.IrreversibleOverride);
        ProcessModifiers(ref ret, EModifierMergeType.Override);
        ProcessModifiers(ref ret, EModifierMergeType.Clamp);
    }

    public override void AddModifier(IPropertyModifier modifier)
    {
        if (modifier is PropertyIrreversibleAdditiveModifier || modifier is PropertyIrreversibleMultiplicativeModifier
         || modifier is PropertyIrreversibleOverrideModifier)
        {
            //不允许添加重复的Modifier
            if (_modifiers.Contains(modifier))
            {
                throw new Exception($"Modifier {modifier.GetType().Name} already exists");
            }
            _modifiers.Add(modifier);
            _isDirty = true;
            GetValue();
            RemoveModifier(modifier);
            _onDirty?.Invoke();
        }
        else
        {
            base.AddModifier(modifier);
        }
    }

    public override void Clear()
    {
        _cacheValue = Fix64.Zero;
        base.Clear();
    }
    
    public override string ToString()
    {
        return base.ToString()+" Type: IrreValueProperty ---------- "+ GetValue();
    }
}
