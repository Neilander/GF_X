using System;
using System.Collections.Generic;
using GameFramework;

public interface IProperty : IReference
{
    //隶属的属性管理器
    PropertyManager PropertyManager { get; }
    //所有生效中的Modifier
    IReadOnlyList<IPropertyModifier> Modifiers { get; }
    void AddModifier(IPropertyModifier modifier);
    void RemoveModifier(IPropertyModifier modifier);
    //属性名称
    string PropertyId { get; }
    //脏标记模式，属性的计算链可能非常复杂，如果每次都要实时计算会很消耗性能
    //尤其是涉及到MoveSpeed这种可能每帧都要用到的属性，所以通过脏标记模式来节省性能
    void MakeDirty();
    void OnDirty(Action action);
}

public interface IProperty<out T> : IProperty
{

    //获取取值器，接口的默认实现
    Func<T> GetValueGetter() => GetValue;
    //获取计算值
    T GetValue();
}

