using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public class AgentTypeHelper : GameFrameworkComponent
{
    public const int UnknownNavAgentTypeId = int.MinValue;
    public const int MediumMovementTypeId = 0;
    public const int SmallMovementTypeId = -1372625422;
    public const int LargeMovementTypeId = -334000983;

    private static readonly Dictionary<UnitSize, int> s_navAgentTypeIds = new Dictionary<UnitSize, int>(4)
    {
        { UnitSize.Small, SmallMovementTypeId },
        { UnitSize.Medium, MediumMovementTypeId },
        { UnitSize.Large, LargeMovementTypeId },
        { UnitSize.SuperLarge, LargeMovementTypeId }
    };
    private static readonly Dictionary<UnitType, int> s_UnitAgentTypeIds = new Dictionary<UnitType, int>();
    private static bool s_RuntimeMappingsPrepared;

    public static void PrepareRuntimeMappings()
    {
        if (GF.DataTable == null)
            throw new InvalidOperationException("AgentTypeHelper cannot prepare runtime mappings before DataTable is initialized.");

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>()
                    ?? throw new InvalidOperationException("AgentTypeHelper requires CharacterDataDetail.");
        s_UnitAgentTypeIds.Clear();
        CharacterDataDetail[] rows = table.GetAllDataRows();
        for (int i = 0; i < rows.Length; i++)
        {
            CharacterDataDetail row = rows[i]
                                      ?? throw new InvalidOperationException($"AgentTypeHelper found a null CharacterDataDetail row at index {i}.");
            if (!Enum.TryParse(row.CharacterKey, false, out UnitType unitType))
                throw new InvalidOperationException($"AgentTypeHelper cannot parse CharacterKey '{row.CharacterKey}' as UnitType.");
            int agentTypeId = ResolveNavAgentTypeId(row.Size);
            if (s_UnitAgentTypeIds.TryGetValue(unitType, out int existing) && existing != agentTypeId)
            {
                throw new InvalidOperationException(
                    $"AgentTypeHelper found inconsistent movement types for UnitType={unitType}. first={existing}, current={agentTypeId}.");
            }
            s_UnitAgentTypeIds[unitType] = agentTypeId;
        }
        s_RuntimeMappingsPrepared = true;
    }

    public int GetNavAgentTypeID(UnitSize unitSize)
    {
        return ResolveNavAgentTypeId(unitSize);
    }

    public static int ResolveNavAgentTypeId(UnitSize unitSize)
    {
        return s_navAgentTypeIds.TryGetValue(unitSize, out int agentTypeId)
            ? agentTypeId
            : throw new InvalidOperationException($"未配置 UnitSize={unitSize} 对应的 Flow movement type。");
    }

    public int GetNavAgentTypeID(UnitType unitType)
    {
        return ResolveNavAgentTypeId(unitType);
    }

    public static int ResolveNavAgentTypeId(UnitType unitType)
    {
        if (!s_RuntimeMappingsPrepared)
            throw new InvalidOperationException("AgentTypeHelper runtime mappings have not been prepared.");
        return s_UnitAgentTypeIds.TryGetValue(unitType, out int agentTypeId)
            ? agentTypeId
            : throw new InvalidOperationException($"UnitType={unitType} has no prepared Flow movement type.");
    }
}
