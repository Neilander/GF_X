﻿using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

public enum CreatureMainProperty
{
    Def,
    Health,
    Speed,
    Mana,
    CollisionRadius,
    TurnRate,
    Sight
}

public enum CreatureMinorProperty
{
    HealthRecover,
    ManaRecover
}

public enum CreatureCurrentProperty
{
    HealthCurrent,
    ManaCurrent
}

public class CreaturePropertyManager
{
    private static readonly CreatureMainProperty[] CanonicalMainProperties =
    {
        CreatureMainProperty.Def,
        CreatureMainProperty.Health,
        CreatureMainProperty.Speed,
        CreatureMainProperty.Mana,
        CreatureMainProperty.CollisionRadius,
        CreatureMainProperty.TurnRate,
        CreatureMainProperty.Sight
    };

    private static readonly Func<Func<Fix64>[], Func<Fix64>> ConfigOnlyRefFunc =
        funcArray => () =>
        {
            if (funcArray == null || funcArray.Length <= (int)RawComponent.Config)
            {
                return Fix64.Zero;
            }

            return funcArray[(int)RawComponent.Config]();
        };

    private CharacterDataDetail _characterData;

    public PropertyManager propertyManager { get; private set; }

    public const string LevelPropertyName = nameof(RawComponent.Level);

    public CreaturePropertyManager(string creatureType)
    {
        propertyManager = new PropertyManager();

        LoadCharacterData(creatureType);

        //创建所有属性的基准属性，等级
        CreateLevelProperty();

        //创建主属性
        CreateMainProperty();

        //创建次要属性，如回血回蓝
        CreateMinorProperty();

        //创建临时属性，如血量蓝量
        CreateIrreversibleProperty();
    }

    public Fix64 GetProperty(CreatureMainProperty property)
    {
        return propertyManager.GetValueProperty(property.ToString()).GetValue();
    }

    public Fix64 GetProperty(CreatureMinorProperty property)
    {
        return propertyManager.GetValueProperty(property.ToString()).GetValue();
    }

    public Fix64 GetProperty(CreatureCurrentProperty property)
    {
        return propertyManager.GetValueProperty(property.ToString()).GetValue();
    }

    /// <summary>
    /// 这个方法修改所有主要属性的白值Buff
    /// </summary>
    public void ModifyMainPropertyValueBuff(CreatureMainProperty name,
        IPropertyModifier modifier, bool ifAdd = true)
    {
        string refName = PropertyHelper.ModName(name.ToString(), nameof(NormalComputeTp.Value), nameof(NormalBaseValueTp.Buff));
        ModifyProperty(refName, modifier, ifAdd);
    }

    /// <summary>
    /// 这个方法修改所有主要属性的百分比乘区的基础值或Buff
    /// </summary>
    public void ModifyMainPropertyMul(CreatureMainProperty name, NormalBaseValueTp baseValueTp,
        IPropertyModifier modifier, bool ifAdd = true)
    {
        string refName = PropertyHelper.ModName(name.ToString(), nameof(NormalComputeTp.Mul), baseValueTp.ToString());
        ModifyProperty(refName, modifier, ifAdd);
    }

    /// <summary>
    /// 这个方法修改所有过程属性的数值
    /// </summary>
    public void ModifyCurrentProperty(CreatureCurrentProperty name,
        IPropertyModifier modifier, bool ifAdd = true)
    {
        ModifyProperty(name.ToString(), modifier, ifAdd);
    }

    /// <summary>
    ///  这个方法可以修改任意属性的任意乘区，但是注意，计算属性大部分修改器不生效
    /// </summary>
    /// <param name="fullName">
    /// 需要手动合成完整名字
    /// </param>
    public void UnsafeModifyAnyProperty(string fullName,
        IPropertyModifier modifier, bool ifAdd = true)
    {
        ModifyProperty(fullName, modifier, ifAdd);
    }

    protected void ModifyProperty(string name, IPropertyModifier modifier, bool ifAdd)
    {
        ValueProperty vp = propertyManager.GetValueProperty(name);
        if (ifAdd)
            vp.AddModifier(modifier);
        else
            vp.RemoveModifier(modifier);
    }


    /*
    public Fix64 SetCurrentProperty(CreatureCurrentProperty property, Fix64 value)
    {
        propertyManager.GetValueProperty(property.ToString()).
    }*/

    void CreateLevelProperty()
    {
        PropertyHelper.CreateBaseProperty(LevelPropertyName, propertyManager).SetBaseValue((Fix64)1);
    }

    private void LoadCharacterData(string creatureType)
    {
        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        var rows = table.GetDataRows(r => r.CharacterKey == creatureType);
        if (rows == null || rows.Length == 0)
        {
            GF.LogError($"缺少 CharacterDataDetail，CharacterKey={creatureType}");
            _characterData = null;
            return;
        }

        _characterData = rows[0];
    }

    #region CreateMainProperty




    void CreateMainProperty()
    {
        for (int i = 0; i < CanonicalMainProperties.Length; i++)
        {
            CreatureMainProperty prop = CanonicalMainProperties[i];
            InitNomalValue(prop, RefFuncFactory(prop));
        }
    }

    private Func<Func<Fix64>[], Func<Fix64>> RefFuncFactory(CreatureMainProperty mainProperty)
    {
        return mainProperty switch
        {
            CreatureMainProperty.Def => PropertyFuncRef.GetAbilityWithConfigAndLevel,
            CreatureMainProperty.Health => PropertyFuncRef.GetHealthWithConfigAndLevel,
            CreatureMainProperty.Speed => PropertyFuncRef.GetSpeedWithConfigAndLevel,
            CreatureMainProperty.Mana => PropertyFuncRef.GetManaWithConfigAndLevel,
            _ => ConfigOnlyRefFunc
        };
    }

    private Fix64 GetConfigValue(CreatureMainProperty prop)
    {
        if (_characterData == null)
        {
            return Fix64.Zero;
        }

        return CharacterDataDetailAccessor.GetMainValue(_characterData, prop);
    }

    /// <summary>
    /// 所有基础属性的初始化方法
    /// 计算分三层。第一层，根据Name-Value-Base-config和Level计算Name-Value-Base；
    /// 然后第二层根据Value-Base和Value-buff计算Value，Mul-base和Mul-buff计算Mul；
    /// 最后第三层Value*mul
    /// </summary>
    /// <param name="name"></param>
    void InitNomalValue(CreatureMainProperty eName, Func<Func<Fix64>[], Func<Fix64>> baseFunc)
    {
        string name = eName.ToString();
        if (propertyManager.GetValueProperty(LevelPropertyName) == null)
            GF.LogError("在创建基础属性时，缺失等级");

        /*
         * 计算 Name-Value-Base
         * Father: Name-Value
         * Self suffix : Base
         * child(Full name):
         *      [Ref]Level
         *      Name-Value-Base-Config
         */
        var NameValueBaseReferenceList = new Dictionary<RawComponent, string>
        {
            [RawComponent.Level] = nameof(RawComponent.Level)
        };
        string NameValueBaseFather = PropertyHelper.ModName(name, nameof(NormalComputeTp.Value));
        string NameValueBaseSelf = nameof(NormalBaseValueTp.Base);
        ComputePropertyTree<RawComponent> tree = PropertyHelper.FormComputeBasePropertyTree<RawComponent>(
            NameValueBaseFather,
            NameValueBaseSelf,
            propertyManager,
            baseFunc,
            NameValueBaseReferenceList
            );
        //初始化Config值
        BaseValueProperty configProperty = tree.baseDictionary[RawComponent.Config] as BaseValueProperty;
        if (configProperty != null)
        {
            configProperty.SetBaseValue(GetConfigValue(eName));
        }

        /*
         * 计算 Name-Value
         * Father: Name
         * Self suffix : Value
         * child(Full name):
         *      [Ref]Name-Value-Base
         *      Name-Value-Buff
         */
        var NameValueReferenceList = new Dictionary<NormalBaseValueTp, string>
        {
            [NormalBaseValueTp.Base] = PropertyHelper.ModName(name, nameof(NormalComputeTp.Value), nameof(NormalBaseValueTp.Base))
        };
        PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(
            name,
            nameof(NormalComputeTp.Value),
            propertyManager,
            PropertyFuncRef.SumAll,
            NameValueReferenceList);

        /*
         * 计算 Name-Mul
         * Father: Name
         * Self suffix : Mul
         * child(Full name):
         *      Name-Mul-Base
         *      Name-Mul-Buff
         */

        ComputePropertyTree<NormalBaseValueTp> mulTree = PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(name, nameof(NormalComputeTp.Mul), propertyManager,
            PropertyFuncRef.SumAll);

        //设置Name-Mul-Base的基础值
        BaseValueProperty baseMul = mulTree.baseDictionary[NormalBaseValueTp.Base] as BaseValueProperty;
        if (baseMul != null)
        {
            baseMul.SetBaseValue(Fix64.One);
        }

        /*
         * 合成 Name
         * Father: “”
         * Self suffix : Name
         * child(Full name):
         *      Name-Value
         *      Name-Mul
         */
        PropertyHelper.BindComputePropertyToOne<NormalComputeTp>("", name, propertyManager, PropertyFuncRef.MultAll);
    }


    #endregion

    #region CreateMinorProperty


    void CreateMinorProperty()
    {
        CreateSingleMinorProperty(nameof(CreatureMainProperty.Health), nameof(CreatureMinorProperty.HealthRecover));
        CreateSingleMinorProperty(nameof(CreatureMainProperty.Mana), nameof(CreatureMinorProperty.ManaRecover));

    }

    void CreateSingleMinorProperty(string nameOfDeriver, string minorPropName)
    {
        PropertyHelper.FormConnectedComputeProperty(
            PropertyHelper.ModName(nameOfDeriver, nameof(NormalComputeTp.Value), nameof(NormalBaseValueTp.Base)),
            PropertyHelper.ModName(minorPropName, nameof(NormalComputeTp.Value), nameof(NormalBaseValueTp.Base)),
            propertyManager,
            PropertyFuncRef.GetPercentOfMaxValue);

        var NameValueReferenceList = new Dictionary<NormalBaseValueTp, string>
        {
            [NormalBaseValueTp.Base] = PropertyHelper.ModName(minorPropName, nameof(NormalComputeTp.Value), nameof(NormalBaseValueTp.Base))
        };
        PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(
            minorPropName,
            nameof(NormalComputeTp.Value),
            propertyManager,
            PropertyFuncRef.SumAll,
            NameValueReferenceList);

        ComputePropertyTree<NormalBaseValueTp> mulTree = PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(minorPropName, nameof(NormalComputeTp.Mul), propertyManager,
            PropertyFuncRef.SumAll);

        //设置Name-Mul-Base的基础值
        BaseValueProperty baseMul = mulTree.baseDictionary[NormalBaseValueTp.Base] as BaseValueProperty;
        if (baseMul != null)
        {
            baseMul.SetBaseValue(Fix64.One);
        }

        PropertyHelper.BindComputePropertyToOne<NormalComputeTp>("", minorPropName, propertyManager, PropertyFuncRef.MultAll);
    }

    #endregion

    #region CreateIrreversibleValueProperty

    void CreateIrreversibleProperty()
    {
        Func<Fix64>[] arr =
        {
            () => GetProperty(CreatureMainProperty.Health)
        };
        IrreversibleValueProperty.Create(PropertyFuncRef.GetDirectValue(arr), nameof(CreatureCurrentProperty.HealthCurrent)).Register(propertyManager);

        Func<Fix64>[] arrM =
        {
            () => GetProperty(CreatureMainProperty.Mana)
        };
        IrreversibleValueProperty.Create(PropertyFuncRef.GetDirectValue(arrM), nameof(CreatureCurrentProperty.ManaCurrent)).Register(propertyManager);
    }

    #endregion
}
public enum RawComponent
{
    Level,
    Config

}

public enum NormalBaseValueTp
{
    Base,
    Buff
}

public enum NormalComputeTp
{
    Value,
    Mul
}
