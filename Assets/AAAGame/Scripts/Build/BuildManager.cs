using JetBrains.Annotations;

public static class BuildManager
{
    public static void UpgradeDevice(DeviceEntity device)
    {
        if (!HasBuildCapability(device.UpgradeDeviceID)) return;
        if (!HasBuildCost(device.UpgradeDeviceID)) return;
        BuildDevice(device.UpgradeDeviceID);
        GF.Entity.HideEntity(device.Entity);
    }

    public static void BuildDevice(string deviceID)
    {
        if (!HasBuildCapability(deviceID)) return;
        if (!HasBuildCost(deviceID)) return;
        Device device = DeviceDataModel.GetDeviceData(deviceID);
        var deviceParams = EntityParams.Create();
        //建造参数
        GF.Entity.ShowEntity<DeviceEntity>(device.PrefabName, Const.EntityGroup.Building, deviceParams);
    }

    public static bool HasBuildCost(string deviceID)
    {
        Device device = DeviceDataModel.GetDeviceData(deviceID);
        if (device == null) return false;
        return ItemCollectionDataModel.HasItem(device.CostMaterial);
    }

    /// <summary>
    /// 检查建造所需能力（Capability），为空则视为无需校验。
    /// </summary>
    public static bool HasBuildCapability(string deviceID)
    {
        var device = DeviceDataModel.GetDeviceData(deviceID);
        if (device == null) return false;
        if (string.IsNullOrWhiteSpace(device.BuildCapability)) return true;
        return CapabilityDataModel.HasCapability(device.BuildCapability);
    }
}