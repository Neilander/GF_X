using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.DataTable;

public sealed class ObjectiveDataModel : DataModelBase
{
    private Dictionary<string, ObjectiveTable> m_Rows;
    private Dictionary<int, ObjectiveTable> m_RowsById;

    protected override void OnCreate(RefParams userdata)
    {
        IDataTable<ObjectiveTable> table = GF.DataTable.GetDataTable<ObjectiveTable>();
        if (table == null)
            throw new InvalidOperationException("ObjectiveDataModel requires the ObjectiveTable data table.");

        m_Rows = new Dictionary<string, ObjectiveTable>(StringComparer.Ordinal);
        m_RowsById = new Dictionary<int, ObjectiveTable>();
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
            if (!m_RowsById.TryAdd(row.Id, row))
                throw new InvalidOperationException($"ObjectiveTable contains duplicate id '{row.Id}'.");
        }
    }

    protected override void OnRelease()
    {
        m_Rows?.Clear();
        m_Rows = null;
        m_RowsById?.Clear();
        m_RowsById = null;
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

    public static string GetText(int definitionId, Fix64[] uniqueValues)
    {
        if (definitionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(definitionId));

        ObjectiveDataModel model = GF.DataModel.GetDataModel<ObjectiveDataModel>()
                                   ?? throw new InvalidOperationException("ObjectiveDataModel has not been initialized.");
        if (model.m_RowsById == null || !model.m_RowsById.TryGetValue(definitionId, out ObjectiveTable row))
            throw new InvalidOperationException($"ObjectiveTable does not contain id '{definitionId}'.");
        return DescriptionValueFormatter.LocalizeAndFill(row.TextKey, uniqueValues);
    }
}
