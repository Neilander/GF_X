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
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("AgentTypeHelper cannot prepare runtime mappings during a logic frame.");

        s_UnitAgentTypeIds.Clear();
        IReadOnlyList<CharacterDataDetail> rows = LogicRuntimeDataTableCache.CharacterRows;
        for (int i = 0; i < rows.Count; i++)
        {
            CharacterDataDetail row = rows[i]
                                      ?? throw new InvalidOperationException($"AgentTypeHelper found a null CharacterDataDetail row at index {i}.");
            if (!Enum.TryParse(row.CharacterKey, false, out UnitType unitType))
            {
                if (HasUnitTag(row.UnitTags, UnitTag.Hero))
                    continue;
                throw new InvalidOperationException($"AgentTypeHelper cannot parse CharacterKey '{row.CharacterKey}' as UnitType.");
            }
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
        if (unitType == UnitType.Unit_Hero)
        {
            string characterKey = KeepsakeConfigRuntime.ResolveCharacterKey(unitType);
            CharacterDataDetail hero = LogicRuntimeDataTableCache.GetCharacterRequired(characterKey);
            return ResolveNavAgentTypeId(hero.Size);
        }
        return s_UnitAgentTypeIds.TryGetValue(unitType, out int agentTypeId)
            ? agentTypeId
            : throw new InvalidOperationException($"UnitType={unitType} has no prepared Flow movement type.");
    }

    private static bool HasUnitTag(UnitTag[] tags, UnitTag required)
    {
        if (tags == null)
            return false;
        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == required)
                return true;
        }
        return false;
    }
}
