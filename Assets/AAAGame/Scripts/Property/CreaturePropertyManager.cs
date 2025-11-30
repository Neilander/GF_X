using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

public enum CreatureMainProperty
{
    PhysicalAtk,
    SpecialAtk,
    PhysicalDef,
    SpecialDef,
    Health,
    Speed,
    Mana
}

public class CreaturePropertyManager
{
    public PropertyManager propertyManager { get; private set; }
    
    public const string LevelPropertyName = nameof(RawComponent.Level);

    public CreaturePropertyManager(string creatureType)
    {
        propertyManager = new PropertyManager();
        
        //创建所有属性的基准属性，等级
        CreateLevelProperty();
        
        //创建7个基准属性
        Array enumValues = Enum.GetValues(typeof(CreatureMainProperty));
        foreach (var eValue in enumValues)
        {
            InitNomalValue((CreatureMainProperty)eValue,creatureType, RefFuncFactory((CreatureMainProperty)eValue));
        }
        
        //更新7个数值的初始值
    }

    private Func<Func<Fix64>[], Func<Fix64>> RefFuncFactory(CreatureMainProperty mainProperty)
    {
        return mainProperty switch
        {
            CreatureMainProperty.PhysicalAtk => PropertyFuncRef.GetAbilityWithConfigAndLevel,
            CreatureMainProperty.SpecialAtk => PropertyFuncRef.GetAbilityWithConfigAndLevel,
            CreatureMainProperty.PhysicalDef => PropertyFuncRef.GetAbilityWithConfigAndLevel,
            CreatureMainProperty.SpecialDef => PropertyFuncRef.GetAbilityWithConfigAndLevel,
            CreatureMainProperty.Health => PropertyFuncRef.GetHealthWithConfigAndLevel,
            CreatureMainProperty.Speed => PropertyFuncRef.GetSpeedWithConfigAndLevel,
            CreatureMainProperty.Mana => PropertyFuncRef.GetManaWithConfigAndLevel,
            _ => PropertyFuncRef.GetAbilityWithConfigAndLevel
        };
    }
    
    private static Fix64 GetConfigValue(CreatureMainProperty prop, string creatureType)
    {
        var table = GF.DataTable.GetDataTable<CharacterMainPropertyTable>();
        var row = table.GetDataRows(r => r.CharacterKey == creatureType)[0];

        return prop switch
        {
            CreatureMainProperty.PhysicalAtk => (Fix64)row.PhysicalAtk,
            CreatureMainProperty.SpecialAtk  => (Fix64)row.SpecialAtk,
            CreatureMainProperty.PhysicalDef => (Fix64)row.PhysicalDef,
            CreatureMainProperty.SpecialDef  => (Fix64)row.SpecialDef,
            CreatureMainProperty.Health      => (Fix64)row.Health,
            CreatureMainProperty.Speed       => (Fix64)row.Speed,
            CreatureMainProperty.Mana        => (Fix64)row.Mana,
            _ => Fix64.Zero
        };
    }

    void CreateLevelProperty()
    {
        PropertyHelper.CreateBaseProperty(LevelPropertyName, propertyManager).SetBaseValue((Fix64)1);
    }

    /// <summary>
    /// 所有基础属性的初始化方法
    /// 计算分三层。第一层，根据Value-Base-config和Level计算Value-Base；
    /// 然后第二层根据Value-Base和Value-buff计算Value，Mul-base和Mul-buff计算Mul；
    /// 最后第三层Value*mul
    /// </summary>
    /// <param name="name"></param>
    void InitNomalValue(CreatureMainProperty eName, string creatureType, Func<Func<Fix64>[], Func<Fix64>> baseFunc)
    {
        string name = eName.ToString();
        if(propertyManager.GetBaseValueProperty(LevelPropertyName)==null)
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
            configProperty.SetBaseValue(GetConfigValue(eName, creatureType));
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
        
        PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(name, nameof(NormalComputeTp.Mul), propertyManager,
            PropertyFuncRef.SumAll);
        
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
}
