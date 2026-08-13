using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.DataTable;

public readonly struct TipPresentation
{
    public TipPresentation(string title, string content, string icon, float duration)
    {
        Title = title;
        Content = content;
        Icon = icon;
        Duration = duration;
    }

    public string Title { get; }
    public string Content { get; }
    public string Icon { get; }
    public float Duration { get; }

    public float ResolveDuration(float? durationOverride)
    {
        return durationOverride ?? Duration;
    }
}

public sealed class TipsDataModel : DataModelBase
{
    public const string DefaultIcon = "Narrator";

    private Dictionary<string, TipsTable> m_Rows;

    protected override void OnCreate(RefParams userdata)
    {
        IDataTable<TipsTable> table = GF.DataTable.GetDataTable<TipsTable>();
        if (table == null)
            throw new InvalidOperationException("TipsDataModel requires the TipsTable data table.");

        m_Rows = new Dictionary<string, TipsTable>(StringComparer.Ordinal);
        TipsTable[] rows = table.GetAllDataRows();
        for (int i = 0; i < rows.Length; i++)
        {
            TipsTable row = rows[i] ?? throw new InvalidOperationException($"TipsTable row {i} is null.");
            if (string.IsNullOrWhiteSpace(row.Identifier))
                throw new InvalidOperationException($"TipsTable row {row.Id} has an empty identifier.");
            if (!m_Rows.TryAdd(row.Identifier, row))
                throw new InvalidOperationException($"TipsTable contains duplicate identifier '{row.Identifier}'.");
        }
    }

    protected override void OnRelease()
    {
        m_Rows?.Clear();
        m_Rows = null;
    }

    public static TipPresentation GetPresentation(
        string identifier,
        string titleOverride = null,
        string contentOverride = null,
        params object[] contentFormatArgs)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Tip identifier is empty.", nameof(identifier));

        TipsDataModel model = GF.DataModel.GetDataModel<TipsDataModel>()
                              ?? throw new InvalidOperationException("TipsDataModel has not been initialized.");
        if (model.m_Rows == null || !model.m_Rows.TryGetValue(identifier, out TipsTable row))
            throw new InvalidOperationException($"TipsTable does not contain identifier '{identifier}'.");

        string title = titleOverride ?? Localize(row.TitleKey);
        string content = contentOverride ?? Localize(row.ContentKey);
        if (contentFormatArgs != null && contentFormatArgs.Length > 0)
            content = string.Format(content, contentFormatArgs);

        string icon = string.IsNullOrWhiteSpace(row.Icon) ? DefaultIcon : row.Icon;
        return new TipPresentation(title, content, icon, row.Duration);
    }

    private static string Localize(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        string localized = GF.Localization.GetString(key);
        return LocalizationTextManager.ProcessText(localized);
    }
}
