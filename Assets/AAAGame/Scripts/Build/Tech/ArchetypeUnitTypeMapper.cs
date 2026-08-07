using System;
using System.Collections.Generic;

/// <summary>
/// 从单位表读取种族到单位类型的映射。
/// </summary>
public class ArchetypeUnitTypeMapper
{
    private readonly Dictionary<Archetype, HashSet<UnitType>> m_UnitTypesByArchetype = new();

    public ArchetypeUnitTypeMapper()
    {
        BuildFromPreparedRuntimeData();
    }

    public IReadOnlyCollection<UnitType> GetUnitTypes(Archetype archetype)
    {
        return m_UnitTypesByArchetype.TryGetValue(archetype, out var unitTypes)
            ? unitTypes
            : Array.Empty<UnitType>();
    }

    private void BuildFromPreparedRuntimeData()
    {
        IReadOnlyList<CharacterDataDetail> rows = LogicRuntimeDataTableCache.CharacterRows;
        for (int i = 0; i < rows.Count; i++)
        {
            CharacterDataDetail row = rows[i];
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
