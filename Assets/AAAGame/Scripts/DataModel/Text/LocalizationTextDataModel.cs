using GameFramework;
using GameFramework.DataTable;
using System.Collections.Generic;
using System.Data;

/// <summary>
/// 本地化文本数据模型：预读全部LocalizationTextTable，并在读取时做本地化。
/// </summary>
public class LocalizationTextDataModel : DataModelBase
{
    private Dictionary<string, string> _localizationKeyDic;

    protected override void OnCreate(RefParams userdata)
    {
        ImportAllTextTables();
    }

    private void ImportAllTextTables()
    {
        _localizationKeyDic = new();
        List<IDataTable<LocalizationTextTable>> tables = new()
        {
            GF.DataTable.GetDataTable<LocalizationTextTable>("InteractionOption"),
            GF.DataTable.GetDataTable<LocalizationTextTable>("Archetype"),
            GF.DataTable.GetDataTable<LocalizationTextTable>("Misc"),
            GF.DataTable.GetDataTable<LocalizationTextTable>("Tips"),
            GF.DataTable.GetDataTable<LocalizationTextTable>("Tutorial"),
            GF.DataTable.GetDataTable<LocalizationTextTable>("Old")
        };
        foreach (var table in tables)
        {
            foreach (var row in table.GetAllDataRows())
            {
                _localizationKeyDic[row.Identifier] = row.TextKey;
            }
        }
    }
    protected override void OnRelease() { }

    /// <summary>
    /// LocalizationTextTable 入口（如 Archetype_* / Tips_* / Tutorial_*）统一走这里。
    /// 不要直接用 LocalizationTextManager.GetLocalizedText 读取这些 identifier。
    /// </summary>
    public static string GetText(string identifier, bool applyRichText = true)
    {
        var model = GF.DataModel.GetDataModel<LocalizationTextDataModel>();
        if (model != null && model._localizationKeyDic != null && model._localizationKeyDic.TryGetValue(identifier, out var text))
        {
            string localized = GF.Localization.GetString(text);
            return applyRichText ? LocalizationTextManager.ProcessText(localized) : localized;
        }

        return applyRichText ? LocalizationTextManager.ProcessText(identifier) : identifier;
    }
}
