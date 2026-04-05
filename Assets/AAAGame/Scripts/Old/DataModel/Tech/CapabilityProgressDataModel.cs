using Newtonsoft.Json;
using System.Collections.Generic;

/// <summary>
/// 能力/权限（Capability）存档模型。
/// 作为统一 gate：各系统通过 HasCapability 判断功能开放。
/// </summary>
public class CapabilityProgressDataModel : DataModelStorageBase
{
    [JsonProperty]
    private HashSet<string> m_Capabilities;

    protected override void OnInitialDataModel()
    {
        m_Capabilities = new HashSet<string>();
    }

    public static bool HasCapability(string capId)
    {
        var dm = GF.DataModel.GetOrCreate<CapabilityProgressDataModel>();
        return !string.IsNullOrWhiteSpace(capId) && dm.m_Capabilities.Contains(capId);
    }

    public static void Grant(string capId)
    {
        if (string.IsNullOrWhiteSpace(capId)) return;
        var dm = GF.DataModel.GetOrCreate<CapabilityProgressDataModel>();
        if (dm.m_Capabilities.Add(capId))
        {
            dm.Save();
        }
    }
}
