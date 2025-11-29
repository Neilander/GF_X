using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreaturePropertyManager
{
    public PropertyManager propertyManager { get; private set; }
    
    public const string LevelPropertyName = "Level";

    public CreaturePropertyManager()
    {
        propertyManager = new PropertyManager();
        
        //创建所有属性的基准属性，等级
        CreateLevelProperty();
        InitNomalValue("Attack");
    }

    public enum NormalBaseValueTp
    {
        Config,
        Buff
    }

    public enum NormalComputeTp
    {
        Value,
        Mul
    }

    void CreateLevelProperty()
    {
        PropertyHelper.CreateBaseProperty(LevelPropertyName, propertyManager).SetBaseValue((Fix64)1);
    }

    /// <summary>
    /// 所有基础属性的初始化方法
    /// 计算分三层。第一层，根据config和level计算Raw；
    /// 然后第二层根据Raw-base和Raw-buff计算Value，Mul-base和Mul-buff计算Mul；
    /// 最后第三层Value*mul
    /// </summary>
    /// <param name="name"></param>
    void InitNomalValue(string name)
    {
        if(propertyManager.GetBaseValueProperty(LevelPropertyName)==null)
            GF.LogError("在创建基础属性时，缺失等级");
        PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(name, nameof(NormalComputeTp.Value), propertyManager,
            PropertyFuncRef.SumAll);
        PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(name, nameof(NormalComputeTp.Mul), propertyManager,
            PropertyFuncRef.SumAll);
        PropertyHelper.BindComputePropertyToOne<NormalComputeTp>("", name, propertyManager, PropertyFuncRef.MultAll);
    }
}
