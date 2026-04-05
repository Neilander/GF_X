using GameFramework;
using UnityGameFramework.Runtime;

public sealed class DeviceUpgradeInteractionOption : IInteractionOption
{
    private DeviceEntity _owner;
    private string _upgradeId;
    public string DisplayName { get; private set; }
    public StringIntPair[] CostMaterial => DeviceDataModel.GetDeviceData(_upgradeId)?.CostMaterial;

    public bool IsExecutable()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        if (!_owner.HasUnlockedUpgrade)
            return false;
        if (string.IsNullOrWhiteSpace(_upgradeId))
            return false;
        return BuildManager.HasBuildCost(_upgradeId);
    }
    public bool IsAvailable()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        if (!_owner.HasUnlockedUpgrade)
            return false;
        return true;
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as DeviceEntity;
        _upgradeId = @params.Get<VarString>("UpgradeId");
        DisplayName = displayName;
    }

    public void Execute()
    {
        if (_owner == null)
            return;

        BuildManager.UpgradeDevice(_owner, _upgradeId);
    }

    public void Clear()
    {
        _owner = null;
        _upgradeId = null;
    }
}
