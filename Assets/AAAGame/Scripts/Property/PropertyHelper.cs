using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class PropertyHelper
{
    public static string ModName(string fatherName,string suffix)
    {
        if (fatherName == "")
            return suffix;
        
        return fatherName+"_"+suffix;
    }
    public static string ModName(string fatherName,string suffix, string suffix2)
    {
        return ModName( ModName(fatherName, suffix),suffix2);
    }

    public static BaseValueProperty CreateBaseProperty(string name, PropertyManager manager)
    {
        BaseValueProperty property = BaseValueProperty.Create(Fix64.Zero, name);
        property.Register(manager);
        return property;
    }

    public static ComputePropertyTree<T> FormComputeBasePropertyTree<T>(string fatherPName,
        string selfSuffix, 
        PropertyManager manager, 
        Func<Func<Fix64>[], Func<Fix64>>refFunc, 
        Dictionary<T,string> referenceProperties = null) where T:Enum
    {
        Array enumValues = Enum.GetValues(typeof(T));
        List<ValueProperty>  baseList = new List<ValueProperty>();
        for (int i = 0; i < enumValues.Length; i++)
        {
            T enumValue = (T)enumValues.GetValue(i);
            string enumString = enumValues.GetValue(i).ToString();
            string computeId = ModName(ModName(fatherPName, selfSuffix), enumString);
            ValueProperty property = null;
            bool needParent = true;
            if (referenceProperties != null && referenceProperties.Keys.Contains(enumValue))
            {
                string refId = referenceProperties[enumValue];
                property = manager.GetValueProperty(refId);
                if (property == null)
                {
                    //needParent = false;
                    property = manager.GetValueProperty(refId);
                }
                
                if(property == null)
                    GF.LogError("在构建属性树的时候，其中一个Reference值并不存在");
            }
            else
            {
                property =
                    BaseValueProperty.Create(Fix64.Zero, computeId);
                property.Register(manager);
            }

            if (needParent)
            {
                property.NotifyParentDirty(ModName(fatherPName,selfSuffix));
            }
            
            baseList.Add(property);
        }
        Func<Fix64>[] funcArray = new Func<Fix64>[baseList.Count];
        for (int i = 0; i < funcArray.Length; i++)
        {
            int index = i;
            funcArray[index] = () => //manager.GetBaseValueProperty(baseList[index].PropertyId).GetValue();
            {
                if (baseList[index] == null)
                {
                    Debug.LogError($"BaseList[{index}] is NULL at build time!");
                    return Fix64.Zero;
                }

                if (baseList[index].PropertyId == null)
                {
                    Debug.LogError($"BaseList[{index}] PropertyId is NULL!");
                    return Fix64.Zero;
                }

                var p = manager.GetValueProperty(baseList[index].PropertyId);
                if (p == null)
                {
                    Debug.LogError($"BaseValueProperty '{baseList[index].PropertyId}' NOT FOUND in manager!");
                    return Fix64.Zero;
                }

                return p.GetValue();
            };
        }

        ComputeValueProperty compVP = ComputeValueProperty.Create(refFunc(funcArray),
            ModName(fatherPName, selfSuffix));

        compVP.Register(manager);
        if(fatherPName != "")
            compVP.NotifyParentDirty(fatherPName);
        
        ComputePropertyTree<T> tree = new ComputePropertyTree<T>(baseList, compVP);
        return tree;

    }

    public static ComputePropertyTree<T> BindComputePropertyToOne<T>(string fatherPName, string selfSuffix,
        PropertyManager manager, Func<Func<Fix64>[], Func<Fix64>> refFunc) where T:Enum
    {
        
        Array enumValues = Enum.GetValues(typeof(T));
        Func<Fix64>[] funcArray = new Func<Fix64>[enumValues.Length];
        List<ValueProperty> baseList = new List<ValueProperty>(enumValues.Length);
        
        for (var i = 0; i < funcArray.Length; i++)
        {
            var index = i;
            var id = ModName(ModName(fatherPName, selfSuffix), enumValues.GetValue(index).ToString());

// 新增：把取到的 ComputeValueProperty 放进 baseList
            ValueProperty cvp = manager.GetValueProperty(id);
            baseList.Add(cvp);

// 原来的 func 捕获也改成基于 cvp
            funcArray[index] = () => cvp.GetValue();
        }
        
        ComputeValueProperty compVP = ComputeValueProperty.Create(refFunc(funcArray),
            ModName(fatherPName, selfSuffix));
        compVP.Register(manager);
        if (fatherPName != "")
            compVP.NotifyParentDirty(fatherPName);
        
        ComputePropertyTree<T> tree = new ComputePropertyTree<T>(baseList, compVP);
        return tree;
        
    }

    public static ComputeValueProperty FormConnectedComputeProperty(
        string derivePropertyFullId, 
        string propertyId, 
        PropertyManager manager,
        Func<Func<Fix64>[], Func<Fix64>> refFunc)
    {
        ValueProperty deriveProperty = manager.GetValueProperty(derivePropertyFullId);

        Func<Fix64>[] funcArray = new Func<Fix64>[1];
        ComputeValueProperty newProperty = ComputeValueProperty.Create(refFunc(funcArray),
            propertyId);
        newProperty.Register(manager);
        deriveProperty.NotifyParentDirty(newProperty.PropertyId);
        return newProperty;
    }
}



public class ComputePropertyTree<T> where T : Enum 
{
    public Dictionary<T,ValueProperty> baseDictionary;
    public ComputeValueProperty fatherProperty;
    
    public ComputePropertyTree(
        List<ValueProperty> baseList,
        ComputeValueProperty father)
    {
        baseDictionary = new Dictionary<T, ValueProperty>();
        fatherProperty = father;

        Array enumValues = Enum.GetValues(typeof(T));

        for (int i = 0; i < enumValues.Length && i < baseList.Count; i++)
        {
            T key = (T)enumValues.GetValue(i);
            baseDictionary[key] = baseList[i];
        }
    }
}