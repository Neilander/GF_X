using System;
using System.Collections.Generic;
using GameFramework.DataTable;

/// <summary>
/// 从单位表读取种族到单位类型的映射。
/// </summary>
public class ArchetypeUnitTypeMapper
{
    private readonly Dictionary<Archetype, HashSet<UnitType>> m_UnitTypesByArchetype = new();

    public ArchetypeUnitTypeMapper()
    {
        BuildFromCurrentDataTables();
    }

    public IReadOnlyCollection<UnitType> GetUnitTypes(Archetype archetype)
    {
        return m_UnitTypesByArchetype.TryGetValue(archetype, out var unitTypes)
            ? unitTypes
            : Array.Empty<UnitType>();
    }

    private void BuildFromCurrentDataTables()
    {
        IDataTable<CharacterDataDetail> table = GF.DataTable?.GetDataTable<CharacterDataDetail>();
        if (table == null)
            return;

        foreach (CharacterDataDetail row in table.GetAllDataRows())
        {
            if (row == null
                || string.IsNullOrWhiteSpace(row.CharacterKey))
            {
                continue;
            }

            if (!UnitTypeHelper.TryParseUnitType(row.CharacterKey, out UnitType unitType))
                continue;

            if (!m_UnitTypesByArchetype.TryGetValue(row.Archetype, out var unitTypes))
            {
                unitTypes = new HashSet<UnitType>();
                m_UnitTypesByArchetype[row.Archetype] = unitTypes;
            }

            unitTypes.Add(unitType);
        }
    }
}
