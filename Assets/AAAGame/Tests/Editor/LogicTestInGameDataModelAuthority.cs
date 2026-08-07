using System;
using System.Collections.Generic;
using System.Reflection;
using GameFramework;
using UnityEngine;

internal static class LogicTestInGameDataModelAuthority
{
    public static InGameDataModel Ensure(GamePhase phase, string ownerName)
    {
        FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GF.DataModel backing field was not found.");
        var component = dataModelField.GetValue(null) as DataModelComponent;
        if (component == null)
        {
            var gameObject = new GameObject($"{ownerName}_DataModel");
            component = gameObject.AddComponent<DataModelComponent>();
            dataModelField.SetValue(null, component);
        }

        FieldInfo dataModelsField = typeof(DataModelComponent).GetField(
            "m_DataModels",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DataModelComponent.m_DataModels was not found.");
        object dataModels = dataModelsField.GetValue(component);
        if (dataModels == null || dataModels.GetType() != dataModelsField.FieldType)
        {
            dataModels = Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(component, dataModels);
        }

        InGameDataModel model = component.GetDataModel<InGameDataModel>();
        if (model == null)
        {
            model = (InGameDataModel)Activator.CreateInstance(typeof(InGameDataModel), true);
            Type pairType = typeof(DataModelComponent).Assembly.GetType("TypeIdPair")
                            ?? throw new InvalidOperationException("TypeIdPair was not found.");
            object pair = Activator.CreateInstance(
                pairType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { typeof(InGameDataModel), 0 },
                null);
            MethodInfo addMethod = dataModels.GetType().GetMethod("Add")
                                   ?? throw new InvalidOperationException(
                                       "DataModelComponent storage has no Add method.");
            addMethod.Invoke(dataModels, new[] { pair, model });
        }

        FieldInfo activeModelField = typeof(InGameDataModel).GetField(
            "s_ActiveModel",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("InGameDataModel active binding field was not found.");
        object activeModel = activeModelField.GetValue(null);
        if (activeModel != null && !ReferenceEquals(activeModel, model))
            throw new InvalidOperationException("InGameDataModel test binding is inconsistent.");
        activeModelField.SetValue(null, model);

        FieldInfo valuesField = typeof(InGameDataModel).GetField(
            "m_IngameValue",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("InGameDataModel value storage field was not found.");
        valuesField.SetValue(
            model,
            new Dictionary<IngameValueType, int>
            {
                [IngameValueType.Phase] = (int)phase,
                [IngameValueType.Day] = 1,
                [IngameValueType.Coin] = 0,
                [IngameValueType.CurrentSupply] = 0,
                [IngameValueType.MaxSupply] = 0,
            });

        PropertyInfo factionsProperty = typeof(InGameDataModel).GetProperty(nameof(InGameDataModel.Factions))
                                        ?? throw new InvalidOperationException(
                                            "InGameDataModel.Factions property was not found.");
        factionsProperty.SetValue(
            model,
            new Dictionary<int, Faction>
            {
                [EntitySideHelper.PlayerFactionId] = new Faction(0),
                [EntitySideHelper.EnemyFactionId] = new Faction(1),
            });
        return model;
    }
}
