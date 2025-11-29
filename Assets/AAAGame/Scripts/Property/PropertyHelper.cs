using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class PropertyHelper
{
    public static string ModName(string fatherName,string suffix)
    {
        if (fatherName == "")
            return suffix;
        
        return fatherName+"_"+suffix;
    }

    public static BaseValueProperty CreateBaseProperty(string name, PropertyManager manager)
    {
        BaseValueProperty property = BaseValueProperty.Create(Fix64.Zero, name);
        property.Register(manager);
        return property;
    }

    public static ComputePropertyTree<T,BaseValueProperty> FormComputeBasePropertyTree<T>(string fatherPName,
        string selfSuffix, 
        PropertyManager manager, 
        Func<Func<Fix64>[], Func<Fix64>>refFunc, 
        List<T> referenceBaseProperty = null) where T:Enum
    {
        Array enumValues = Enum.GetValues(typeof(T));
        List<BaseValueProperty>  baseList = new List<BaseValueProperty>();
        for (int i = 0; i < enumValues.Length; i++)
        {
            T enumValue = (T)enumValues.GetValue(i);
            string enumString = enumValues.GetValue(i).ToString();
            string computeId = ModName(ModName(fatherPName, selfSuffix), enumString);
            BaseValueProperty property = null;
            if (referenceBaseProperty != null && referenceBaseProperty.Contains(enumValue))
            {
                property = manager.GetBaseValueProperty(computeId);
            }
            else
            {
                property =
                    BaseValueProperty.Create(Fix64.Zero, computeId);
                property.Register(manager);
            }
            property.NotifyParentDirty(ModName(fatherPName,selfSuffix));
            baseList.Add(property);
        }
        Func<Fix64>[] funcArray = new Func<Fix64>[baseList.Count];
        for (int i = 0; i < funcArray.Length; i++)
        {
            int index = i;
            funcArray[index] = () => manager.GetBaseValueProperty(baseList[index].PropertyId).GetValue();
        }

        ComputeValueProperty compVP = ComputeValueProperty.Create(refFunc(funcArray),
            ModName(fatherPName, selfSuffix));

        compVP.Register(manager);
        if(fatherPName != "")
            compVP.NotifyParentDirty(fatherPName);
        
        ComputePropertyTree<T,BaseValueProperty> tree = new ComputePropertyTree<T,BaseValueProperty>(baseList, compVP);
        return tree;

    }

    public static ComputePropertyTree<T, ComputeValueProperty> BindComputePropertyToOne<T>(string fatherPName, string selfSuffix,
        PropertyManager manager, Func<Func<Fix64>[], Func<Fix64>> refFunc) where T:Enum
    {
        
        Array enumValues = Enum.GetValues(typeof(T));
        Func<Fix64>[] funcArray = new Func<Fix64>[enumValues.Length];
        List<ComputeValueProperty> baseList = new List<ComputeValueProperty>(enumValues.Length);
        
        for (var i = 0; i < funcArray.Length; i++)
        {
            var index = i;
            var id = ModName(ModName(fatherPName, selfSuffix), enumValues.GetValue(index).ToString());

// 新增：把取到的 ComputeValueProperty 放进 baseList
            ComputeValueProperty cvp = manager.GetComputeValueProperty(id);
            baseList.Add(cvp);

// 原来的 func 捕获也改成基于 cvp
            funcArray[index] = () => cvp.GetValue();
        }
        
        ComputeValueProperty compVP = ComputeValueProperty.Create(refFunc(funcArray),
            ModName(fatherPName, selfSuffix));
        compVP.Register(manager);
        if (fatherPName != "")
            compVP.NotifyParentDirty(fatherPName);
        
        ComputePropertyTree<T,ComputeValueProperty> tree = new ComputePropertyTree<T,ComputeValueProperty>(baseList, compVP);
        return tree;
        
    }
}

public class ComputePropertyTree<T,T2> where T : Enum where T2: ValueProperty
{
    public Dictionary<T,T2> baseDictionary;
    public ComputeValueProperty fatherProperty;
    
    public ComputePropertyTree(
        List<T2> baseList,
        ComputeValueProperty father)
    {
        baseDictionary = new Dictionary<T, T2>();
        fatherProperty = father;

        Array enumValues = Enum.GetValues(typeof(T));

        for (int i = 0; i < enumValues.Length && i < baseList.Count; i++)
        {
            T key = (T)enumValues.GetValue(i);
            baseDictionary[key] = baseList[i];
        }
    }
}