using GameFramework;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public sealed class DeviceUpgradeInteractionOption : IInteractionOption
{
    private static readonly KeyValuePair<IngameValueType, int>[] EmptyCost = System.Array.Empty<KeyValuePair<IngameValueType, int>>();
    private DeviceEntity _owner;
    private string _upgradeId;
    public string DisplayName { get; private set; }
    public string DisplayDesc { get; private set; }
    public KeyValuePair<IngameValueType, int>[] CostResource => EmptyCost;

    public bool IsVisible()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        if (!_owner.HasUnlockedUpgrade)
            return false;
        if (string.IsNullOrWhiteSpace(_upgradeId))
            return false;
        return true;
    }

    public bool IsExecutable()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        if (!_owner.HasUnlockedUpgrade)
            return false;
        if (string.IsNullOrWhiteSpace(_upgradeId))
            return false;
        return DeviceBuildManager.HasBuildCost(_upgradeId);
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as DeviceEntity;
        _upgradeId = @params.Get<VarString>("UpgradeId");
        DisplayName = displayName;
        DisplayDesc = string.Empty;
    }

    public void Execute()
    {
        if (_owner == null)
            return;

        DeviceBuildManager.UpgradeDevice(_owner, _upgradeId);
    }

    public void Clear()
    {
        _owner = null;
        _upgradeId = null;
        DisplayName = null;
        DisplayDesc = null;
    }
}
