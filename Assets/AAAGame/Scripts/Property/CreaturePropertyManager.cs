using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreaturePropertyManager
{
    public PropertyManager propertyManager { get; private set; }

    public CreaturePropertyManager()
    {
        propertyManager = new PropertyManager();
        InitNomalValue("Attack");
    }

    public enum NormalBaseValueTp
    {
        Config,
        Buff,
        Other
    }

    public enum NormalComputeTp
    {
        Value,
        Mul
    }

    void InitNomalValue(string name)
    {
        PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(name, NormalComputeTp.Value.ToString(), propertyManager,
            PropertyFuncRef.SumAll);
        PropertyHelper.FormComputeBasePropertyTree<NormalBaseValueTp>(name, NormalComputeTp.Mul.ToString(), propertyManager,
            PropertyFuncRef.SumAll);
        PropertyHelper.BindComputePropertyToOne<NormalComputeTp>("", name, propertyManager, PropertyFuncRef.MultAll);
    }
}
