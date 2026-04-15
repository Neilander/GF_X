using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时科技范围索引。
/// 启动时扫描 CharacterDataDetail 一次，后续解析 TechScope 时只查缓存。
/// </summary>
public class TechScopeIndex
{
    private readonly Dictionary<string, CharacterDataDetail> m_CharacterDataByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<UnitType, string> m_CharacterKeyByUnitType = new();
    private readonly Dictionary<string, UnitType> m_UnitTypeByCharacterKey = new(StringComparer.Ordinal);
    private readonly Dictionary<UnitTag, HashSet<string>> m_CharacterKeysByTag = new();

    public TechScopeIndex(IEnumerable<CharacterDataDetail> rows)
    {
        if (rows == null)
            throw new ArgumentNullException(nameof(rows));

        Build(rows);
    }

    public static TechScopeIndex CreateFromCurrentDataTables()
    {
        var table = GF.DataTable?.GetDataTable<CharacterDataDetail>();
        if (table == null)
            throw new InvalidOperationException("CharacterDataDetail data table is required for TechScopeIndex.");

        return new TechScopeIndex(table.GetAllDataRows());
    }

    public IReadOnlyCollection<string> GetAllCharacterKeys()
    {
        return m_CharacterDataByKey.Keys;
    }

    public IReadOnlyCollection<string> GetCharacterKeysByTag(UnitTag unitTag)
    {
        return m_CharacterKeysByTag.TryGetValue(unitTag, out var keys)
            ? keys
            : Array.Empty<string>();
    }

    public bool TryGetCharacterKey(UnitType unitType, out string characterKey)
    {
        return m_CharacterKeyByUnitType.TryGetValue(unitType, out characterKey);
    }

    public bool TryGetUnitType(string characterKey, out UnitType unitType)
    {
        unitType = default;
        return !string.IsNullOrWhiteSpace(characterKey)
               && m_UnitTypeByCharacterKey.TryGetValue(characterKey, out unitType);
    }

    private void Build(IEnumerable<CharacterDataDetail> rows)
    {
        foreach (var row in rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.CharacterKey))
                continue;

            m_CharacterDataByKey[row.CharacterKey] = row;
            CacheUnitTypeMapping(row.CharacterKey);
            CacheTagMapping(row);
        }
    }

    private void CacheUnitTypeMapping(string characterKey)
    {
        if (!Enum.TryParse(characterKey, out UnitType unitType))
            return;

        m_CharacterKeyByUnitType[unitType] = characterKey;
        m_UnitTypeByCharacterKey[characterKey] = unitType;
    }

    private void CacheTagMapping(CharacterDataDetail row)
    {
        if (row.UnitTags == null)
            return;

        for (int i = 0; i < row.UnitTags.Length; i++)
        {
            var unitTag = row.UnitTags[i];
            if (!m_CharacterKeysByTag.TryGetValue(unitTag, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                m_CharacterKeysByTag[unitTag] = keys;
            }

            keys.Add(row.CharacterKey);
        }
    }
}
