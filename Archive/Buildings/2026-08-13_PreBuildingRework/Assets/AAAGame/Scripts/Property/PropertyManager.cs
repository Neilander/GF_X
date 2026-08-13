using System.Collections.Generic;
using GameFramework;


public class PropertyManager : IReference
{
    private const int InitialPropertyCapacity = 80;
    private Dictionary<string, IProperty> _properties =
        new Dictionary<string, IProperty>(InitialPropertyCapacity, System.StringComparer.Ordinal);

    public void RegisterProperty(IProperty property)
    {
        _properties[property.PropertyId] = property;
    }

    public IProperty GetProperty(string propertyId)
    {
        return _properties.TryGetValue(propertyId, out var property) ? property : null;
    }

    public ValueProperty GetValueProperty(string propertyId)
    {
        return _properties.TryGetValue(propertyId, out var property) && property is ValueProperty valueProperty ? valueProperty : null;
    }

    public BaseValueProperty GetBaseValueProperty(string propertyId)
    {
        return _properties.TryGetValue(propertyId, out var property) && property is BaseValueProperty baseValueProperty ? baseValueProperty : null;
    }

    public ComputeValueProperty GetComputeValueProperty(string propertyId)
    {
        return _properties.TryGetValue(propertyId, out var property) && property is ComputeValueProperty computeValueProperty ? computeValueProperty : null;
    }

    public IrreversibleValueProperty GetIrreversibleValueProperty(string propertyId)
    {
        return _properties.TryGetValue(propertyId, out var property) && property is IrreversibleValueProperty irreversibleValueProperty ? irreversibleValueProperty : null;
    }

    public Vector2IntArrayProperty GetVector2IntArrayProperty(string propertyId)
    {
        return _properties.TryGetValue(propertyId, out var property) && property is Vector2IntArrayProperty vector2IntArrayProperty ? vector2IntArrayProperty : null;
    }

    public void Clear()
    {
        foreach (var property in _properties)
        {
            ReferencePool.Release(property.Value);
        }
        _properties.Clear();
    }
    
    public override string ToString()
    {
        if (_properties.Count == 0)
            return "PropertyManager: (empty)";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("PropertyManager:");

        foreach (var kv in _properties)
        {
            sb.AppendLine($"  {kv.Value}"); // 调用每个 Property 的 ToString()
        }

        return sb.ToString();
    }
}
