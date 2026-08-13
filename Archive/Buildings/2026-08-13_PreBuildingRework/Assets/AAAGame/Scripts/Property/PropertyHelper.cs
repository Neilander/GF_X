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
        T[] enumValues = PropertyEnumCache<T>.Values;
        string[] enumNames = PropertyEnumCache<T>.Names;
        ValueProperty[] baseProperties = new ValueProperty[enumValues.Length];
        for (int i = 0; i < enumValues.Length; i++)
        {
            T enumValue = enumValues[i];
            string enumString = enumNames[i];
            string computeId = ModName(ModName(fatherPName, selfSuffix), enumString);
            ValueProperty property = null;
            if (referenceProperties != null && referenceProperties.TryGetValue(enumValue, out string refId))
            {
                property = manager.GetValueProperty(refId);
                if (property == null)
                {
                    throw new InvalidOperationException(
                        $"Property tree reference '{refId}' is missing while building '{ModName(fatherPName, selfSuffix)}'.");
                }
            }
            else
            {
                property =
                    BaseValueProperty.Create(Fix64.Zero, computeId);
                property.Register(manager);
            }

            property.NotifyParentDirty(ModName(fatherPName,selfSuffix));
            baseProperties[i] = property;
        }
        Func<Fix64>[] funcArray = new Func<Fix64>[baseProperties.Length];
        for (int i = 0; i < funcArray.Length; i++)
        {
            int index = i;
            ValueProperty property = baseProperties[index];
            string propertyId = property.PropertyId;
            funcArray[index] = () =>
            {
                ValueProperty p = manager.GetValueProperty(propertyId);
                if (p == null)
                {
                    throw new InvalidOperationException(
                        $"Property tree node '{propertyId}' is missing from its manager.");
                }

                return p.GetValue();
            };
        }

        ComputeValueProperty compVP = ComputeValueProperty.Create(refFunc(funcArray),
            ModName(fatherPName, selfSuffix));

        compVP.Register(manager);
        if(fatherPName != "")
            compVP.NotifyParentDirty(fatherPName);
        
        ComputePropertyTree<T> tree = new ComputePropertyTree<T>(baseProperties, compVP);
        return tree;

    }

    public static ComputePropertyTree<T> BindComputePropertyToOne<T>(string fatherPName, string selfSuffix,
        PropertyManager manager, Func<Func<Fix64>[], Func<Fix64>> refFunc) where T:Enum
    {
        
        T[] enumValues = PropertyEnumCache<T>.Values;
        string[] enumNames = PropertyEnumCache<T>.Names;
        Func<Fix64>[] funcArray = new Func<Fix64>[enumValues.Length];
        ValueProperty[] baseProperties = new ValueProperty[enumValues.Length];
        
        for (var i = 0; i < funcArray.Length; i++)
        {
            var index = i;
            string id = ModName(ModName(fatherPName, selfSuffix), enumNames[index]);

// 新增：把取到的 ComputeValueProperty 放进 baseList
            ValueProperty cvp = manager.GetValueProperty(id);
            if (cvp == null)
                throw new InvalidOperationException($"Property tree node '{id}' is missing while binding '{ModName(fatherPName, selfSuffix)}'.");
            baseProperties[index] = cvp;

// 原来的 func 捕获也改成基于 cvp
            funcArray[index] = () => cvp.GetValue();
        }
        
        ComputeValueProperty compVP = ComputeValueProperty.Create(refFunc(funcArray),
            ModName(fatherPName, selfSuffix));
        compVP.Register(manager);
        if (fatherPName != "")
            compVP.NotifyParentDirty(fatherPName);
        
        ComputePropertyTree<T> tree = new ComputePropertyTree<T>(baseProperties, compVP);
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



internal static class PropertyEnumCache<T> where T : Enum
{
    public static readonly T[] Values = (T[])Enum.GetValues(typeof(T));
    public static readonly string[] Names = CreateNames();

    public static int IndexOf(T value)
    {
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < Values.Length; i++)
        {
            if (comparer.Equals(Values[i], value))
                return i;
        }

        return -1;
    }

    private static string[] CreateNames()
    {
        var names = new string[Values.Length];
        for (int i = 0; i < Values.Length; i++)
            names[i] = Values[i].ToString();
        return names;
    }
}

public class ComputePropertyTree<T> where T : Enum 
{
    private readonly ValueProperty[] m_BaseProperties;
    public ComputeValueProperty fatherProperty;
    
    public ComputePropertyTree(
        ValueProperty[] baseProperties,
        ComputeValueProperty father)
    {
        m_BaseProperties = baseProperties ?? throw new ArgumentNullException(nameof(baseProperties));
        fatherProperty = father;
    }

    public ValueProperty GetBaseProperty(T key)
    {
        int index = PropertyEnumCache<T>.IndexOf(key);
        if (index < 0 || index >= m_BaseProperties.Length)
            throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown property-tree key.");
        return m_BaseProperties[index];
    }
}

