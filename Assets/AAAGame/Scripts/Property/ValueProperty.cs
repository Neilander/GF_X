using GameFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityGameFramework.Runtime;

public abstract class ValueProperty : IProperty<Fix64>
{
    protected Fix64 _cacheValue;//缓存值
    protected bool _isDirty = true;
    protected Action _onDirty;
    private PropertyManager _propertyManager;
    protected List<IPropertyModifier> _modifiers = new List<IPropertyModifier>();

    public string PropertyId => _propertyId;
    protected string _propertyId;
    protected List<string> _parentIds;
    public PropertyManager PropertyManager => _propertyManager;
    public IReadOnlyList<IPropertyModifier> Modifiers => _modifiers.AsReadOnly();

    public abstract Fix64 GetValue();

    protected abstract void ApplyModify(ref Fix64 ret);

    //分类实现Modifier的作用
    protected void ProcessModifiers(ref Fix64 value, EModifierMergeType type)
    {
        switch (type)
        {
            case EModifierMergeType.DirectAdditive:
                value += _modifiers.OfType<PropertyDirectAdditiveModifier>()
                    .OrderBy(m => m.Priority)
                    .Aggregate(Fix64.Zero, (sum, mod) => sum + mod.Value);
                break;
            case EModifierMergeType.DirectMultiplicative:
                value = _modifiers.OfType<PropertyDirectMultiplicativeModifier>()
                    .OrderBy(m => m.Priority)
                    .Aggregate(value, (current, mod) => current * (1 + mod.Value));
                break;
            case EModifierMergeType.FinalAdditive:
                value += _modifiers.OfType<PropertyFinalAdditiveModifier>()
                    .OrderBy(m => m.Priority)
                    .Aggregate(Fix64.Zero, (sum, mod) => sum + mod.Value);
                break;
            case EModifierMergeType.FinalMultiplicative:
                value = _modifiers.OfType<PropertyFinalMultiplicativeModifier>()
                    .OrderBy(m => m.Priority)
                    .Aggregate(value, (current, mod) => current * (1 + mod.Value));
                break;
            case EModifierMergeType.IrreversibleAdditive:
                value += _modifiers.OfType<PropertyIrreversibleAdditiveModifier>()
                    .OrderBy(m => m.Priority)
                    .Aggregate(Fix64.Zero, (sum, mod) => sum + mod.Value);
                break;
            case EModifierMergeType.IrreversibleMultiplicative:
                value = _modifiers.OfType<PropertyIrreversibleMultiplicativeModifier>()
                    .OrderBy(m => m.Priority)
                    .Aggregate(value, (current, mod) => current * (1 + mod.Value));
                break;
            case EModifierMergeType.IrreversibleOverride:
                var irreversibleOverrideMods = _modifiers.OfType<PropertyIrreversibleOverrideModifier>()
                    .OrderBy(m => m.Priority).ToList();
                if (irreversibleOverrideMods.Any()) value = irreversibleOverrideMods.Last().Value; // IrreversibleOverride类型以最后一个为准
                break;
            case EModifierMergeType.Override:
                var overrideMods = _modifiers.OfType<PropertyOverrideModifier>()
                    .OrderBy(m => m.Priority).ToList();
                if (overrideMods.Any()) value = overrideMods.Last().Value; // Override类型以最后一个为准
                break;
            case EModifierMergeType.Clamp:
                value = _modifiers.OfType<PropertyClampModifier>()
                    .OrderBy(m => m.Priority)
                    .Aggregate(value, (current, mod) => Fix64.Clamp(current, mod.Min, mod.Max));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }


    //当发生Modifier的变化时，标记为脏，并触发脏标记委托
    public virtual void AddModifier(IPropertyModifier modifier)
    {
        //不允许添加重复的Modifier
        if (_modifiers.Contains(modifier))
        {
            throw new Exception($"Modifier {modifier.GetType().Name} already exists");
        }
        _modifiers.Add(modifier);
        MakeDirty();
    }

    public void RemoveModifier(IPropertyModifier modifier)
    {
        _modifiers.Remove(modifier);
        ReferencePool.Release(modifier);
        MakeDirty();
    }

    public void MakeDirty()
    {
        _isDirty = true;
        _onDirty?.Invoke();
    }

    //委托，一般用于通知引用自身的父属性，将其标记为脏，最后使一整条计算链都标记为脏
    public void OnDirty(Action action)
    {
        _onDirty += action;
    }

    public ValueProperty Register(PropertyManager propertyManager)
    {
        _propertyManager = propertyManager;
        _propertyManager.RegisterProperty(this);
        return this;
    }

    //可在父属性注册前注册，用于通知父属性，使其标记为脏
    public ValueProperty NotifyParentDirty(string parentPropertyId)
    {
        if (_parentIds == null)
            _parentIds = new List<string>();
        _parentIds.Add(parentPropertyId);
        OnDirty(() => _propertyManager.GetProperty(parentPropertyId)?.MakeDirty());
        return this;
    }

    public virtual void Clear()
    {
        _cacheValue = Fix64.Zero;
        _onDirty = null;
        _isDirty = true;
        foreach (var modifier in _modifiers)
        {
            ReferencePool.Release(modifier);
        }
        _modifiers.Clear();
        _propertyManager = null;
        _propertyId = null;
    }
    
    public override string ToString()
    {
        string parents = (_parentIds == null || _parentIds.Count == 0)
            ? "None"
            : string.Join(", ", _parentIds);
        return $"PropertyId: {PropertyId}, ParentId: [{parents}], Modifiers: {_modifiers.Count}";
    }
}
