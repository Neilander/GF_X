using GameFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Vector2IntArrayProperty : IProperty<Vector2Int[]>
{
    protected Vector2Int[] _cacheValue;//缓存值
    protected bool _isDirty = true;
    private Action _onDirty;
    private PropertyManager _propertyManager;
    protected List<IPropertyModifier> _modifiers = new List<IPropertyModifier>();

    public string PropertyId => _propertyId;
    protected string _propertyId;
    public PropertyManager PropertyManager => _propertyManager;
    public IReadOnlyList<IPropertyModifier> Modifiers => _modifiers.AsReadOnly();

    //分类实现Modifier的作用
    protected void ProcessModifiers(ref Vector2Int[] value, EModifierMergeType type)
    {
        switch (type)
        {
            // case EModifierMergeType.Vector2IntArrayAdditive:
            //     value += _modifiers.OfType<PropertyDirectAdditiveModifier>()
            //         .OrderBy(m => m.Priority)
            //         .Aggregate(Fix64.Zero, (sum, mod) => sum + mod.Value);
            //     break;
            // case EModifierMergeType.RangeAdditive:
            //     value += _modifiers.OfType<PropertyDirectAdditiveModifier>()
            //         .OrderBy(m => m.Priority)
            //         .Aggregate(Fix64.Zero, (sum, mod) => sum + mod.Value);
            //     break;
            // case EModifierMergeType.Vector2IntArrayPreOverride:
            //     var overrideMods = _modifiers.OfType<PropertyOverrideModifier>()
            //         .OrderBy(m => m.Priority).ToList();
            //     if (overrideMods.Any()) value = overrideMods.Last().Value; // Override类型以最后一个为准
            //     break;
            //     break;
            // case EModifierMergeType.Vector2IntArrayFinalOverride:
            //     var overrideMods = _modifiers.OfType<PropertyOverrideModifier>()
            //         .OrderBy(m => m.Priority).ToList();
            //     if (overrideMods.Any()) value = overrideMods.Last().Value; // Override类型以最后一个为准
            //     break;
            // default:
            //     throw new ArgumentOutOfRangeException(nameof(type), type, null);
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

    public Vector2IntArrayProperty Register(PropertyManager propertyManager)
    {
        _propertyManager = propertyManager;
        _propertyManager.RegisterProperty(this);
        return this;
    }

    //可在父属性注册前注册，用于通知父属性，使其标记为脏
    public Vector2IntArrayProperty NotifyParentDirty(string parentPropertyId)
    {
        OnDirty(() => _propertyManager.GetProperty(parentPropertyId)?.MakeDirty());
        return this;
    }

    public virtual void Clear()
    {
        _baseValue = null;
        _cacheValue = null;
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
    private Vector2Int[] _baseValue; //基础值

    public static Vector2IntArrayProperty Create(Vector2Int[] baseValue, string propertyId)
    {
        var property = ReferencePool.Acquire<Vector2IntArrayProperty>();
        property._propertyId = propertyId;
        property._baseValue = baseValue.ToArray();
        return property;
    }
    public Vector2Int[] GetValue()
    {
        if (!_isDirty) return _cacheValue;
        var ret = _baseValue;
        ApplyModify(ref ret);
        _cacheValue = ret;
        _isDirty = false;
        return _cacheValue;
    }

    public void SetBaseValue(Vector2Int[] baseValue)
    {
        if (_baseValue.SequenceEqual(baseValue)) return;
        _baseValue = baseValue.ToArray();
        MakeDirty();
    }
    protected void ApplyModify(ref Vector2Int[] ret)
    {
        ProcessModifiers(ref ret, EModifierMergeType.Vector2IntArrayPreOverride);
        ProcessModifiers(ref ret, EModifierMergeType.Vector2IntArrayAdditive);
        ProcessModifiers(ref ret, EModifierMergeType.RangeAdditive);
        ProcessModifiers(ref ret, EModifierMergeType.Vector2IntArrayFinalOverride);
    }
}
