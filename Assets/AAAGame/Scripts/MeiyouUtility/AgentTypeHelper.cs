using System;
using System.Collections.Generic;
using UnityEngine.AI;
using UnityGameFramework.Runtime;
public class AgentTypeHelper : GameFrameworkComponent
{
    private bool s_navAgentTypeLoaded;
    private readonly Dictionary<UnitSize, int> s_navAgentTypeIds = new Dictionary<UnitSize, int>(3);

    public int GetNavAgentTypeID(UnitSize unitSize)
    {
        EnsureNavAgentTypeLoaded();

        return s_navAgentTypeIds.TryGetValue(unitSize, out int agentTypeId)
            ? agentTypeId
            : throw new InvalidOperationException($"未配置 UnitSize={unitSize} 对应的 NavMesh AgentType。");
    }

    private bool TryMapUnitSize(string settingName, out UnitSize unitSize)
    {
        if (settingName.Equals("Small", StringComparison.OrdinalIgnoreCase))
        {
            unitSize = UnitSize.Small;
            return true;
        }

        if (settingName.Equals("Medium", StringComparison.OrdinalIgnoreCase))
        {
            unitSize = UnitSize.Medium;
            return true;
        }

        if (settingName.Equals("Large", StringComparison.OrdinalIgnoreCase))
        {
            unitSize = UnitSize.Large;
            return true;
        }

        unitSize = default;
        return false;
    }

    private void EnsureNavAgentTypeLoaded()
    {
        if (s_navAgentTypeLoaded)
        {
            return;
        }

        s_navAgentTypeIds.Clear();

        int settingsCount = NavMesh.GetSettingsCount();
        for (int i = 0; i < settingsCount; i++)
        {
            var settings = NavMesh.GetSettingsByIndex(i);
            int agentTypeId = settings.agentTypeID;
            string settingName = NavMesh.GetSettingsNameFromID(agentTypeId);

            if (TryMapUnitSize(settingName, out UnitSize unitSize))
            {
                s_navAgentTypeIds[unitSize] = agentTypeId;
            }
        }

        s_navAgentTypeLoaded = true;

        if (!s_navAgentTypeIds.ContainsKey(UnitSize.Small)
            || !s_navAgentTypeIds.ContainsKey(UnitSize.Medium)
            || !s_navAgentTypeIds.ContainsKey(UnitSize.Large))
        {
            throw new InvalidOperationException($"NavMesh AgentType 缺失，必须存在 Small/Medium/Large 三个配置。当前配置: {GetAllAgentTypeNames()}。");
        }
    }

    private static string GetAllAgentTypeNames()
    {
        int settingsCount = NavMesh.GetSettingsCount();
        if (settingsCount <= 0)
            return "(无)";

        List<string> names = new List<string>(settingsCount);
        for (int i = 0; i < settingsCount; i++)
        {
            var settings = NavMesh.GetSettingsByIndex(i);
            names.Add(NavMesh.GetSettingsNameFromID(settings.agentTypeID));
        }

        return string.Join(", ", names);
    }
}