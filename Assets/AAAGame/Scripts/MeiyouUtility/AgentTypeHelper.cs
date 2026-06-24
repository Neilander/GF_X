using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public class AgentTypeHelper : GameFrameworkComponent
{
    public const int UnknownNavAgentTypeId = int.MinValue;
    public const int MediumMovementTypeId = 0;
    public const int SmallMovementTypeId = -1372625422;
    public const int LargeMovementTypeId = -334000983;

    private readonly Dictionary<UnitSize, int> s_navAgentTypeIds = new Dictionary<UnitSize, int>(4)
    {
        { UnitSize.Small, SmallMovementTypeId },
        { UnitSize.Medium, MediumMovementTypeId },
        { UnitSize.Large, LargeMovementTypeId },
        { UnitSize.SuperLarge, LargeMovementTypeId }
    };

    public int GetNavAgentTypeID(UnitSize unitSize)
    {
        return s_navAgentTypeIds.TryGetValue(unitSize, out int agentTypeId)
            ? agentTypeId
            : throw new InvalidOperationException($"未配置 UnitSize={unitSize} 对应的 Flow movement type。");
    }
}
