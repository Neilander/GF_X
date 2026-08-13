using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.DataTable;

public sealed class ObjectiveDataModel : DataModelBase
{
    private Dictionary<string, ObjectiveTable> m_Rows;

    protected override void OnCreate(RefParams userdata)
    {
        IDataTable<ObjectiveTable> table = GF.DataTable.GetDataTable<ObjectiveTable>();
        if (table == null)
            throw new InvalidOperationException("ObjectiveDataModel requires the ObjectiveTable data table.");

        m_Rows = new Dictionary<string, ObjectiveTable>(StringComparer.Ordinal);
        ObjectiveTable[] rows = table.GetAllDataRows();
        for (int i = 0; i < rows.Length; i++)
        {
            ObjectiveTable row = rows[i] ?? throw new InvalidOperationException($"ObjectiveTable row {i} is null.");
            if (string.IsNullOrWhiteSpace(row.Identifier))
                throw new InvalidOperationException($"ObjectiveTable row {row.Id} has an empty identifier.");
            if (string.IsNullOrWhiteSpace(row.TextKey))
                throw new InvalidOperationException($"ObjectiveTable row '{row.Identifier}' has an empty text key.");
            if (!m_Rows.TryAdd(row.Identifier, row))
                throw new InvalidOperationException($"ObjectiveTable contains duplicate identifier '{row.Identifier}'.");
        }
    }

    protected override void OnRelease()
    {
        m_Rows?.Clear();
        m_Rows = null;
    }

    public static string GetText(string identifier, params object[] formatArgs)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Objective identifier is empty.", nameof(identifier));

        ObjectiveDataModel model = GF.DataModel.GetDataModel<ObjectiveDataModel>()
                                   ?? throw new InvalidOperationException("ObjectiveDataModel has not been initialized.");
        if (model.m_Rows == null || !model.m_Rows.TryGetValue(identifier, out ObjectiveTable row))
            throw new InvalidOperationException($"ObjectiveTable does not contain identifier '{identifier}'.");

        string text = LocalizationTextManager.ProcessText(GF.Localization.GetString(row.TextKey));
        return formatArgs != null && formatArgs.Length > 0
            ? string.Format(text, formatArgs)
            : text;
    }

    public static string GetText(string objectiveIdentifier, Fix64[] uniqueValues)
    {
        if (string.IsNullOrWhiteSpace(objectiveIdentifier))
            throw new ArgumentException("Objective identifier is empty.", nameof(objectiveIdentifier));

        ObjectiveDataModel model = GF.DataModel.GetDataModel<ObjectiveDataModel>()
                                   ?? throw new InvalidOperationException("ObjectiveDataModel has not been initialized.");
        if (model.m_Rows == null || !model.m_Rows.TryGetValue(objectiveIdentifier, out ObjectiveTable row))
            throw new InvalidOperationException($"ObjectiveTable does not contain identifier '{objectiveIdentifier}'.");
        return DescriptionValueFormatter.LocalizeAndFill(row.TextKey, uniqueValues);
    }
}
