using JetBrains.Annotations;
using UnityEngine;

public static class DeviceBuildManager
{
    public static void UpgradeDevice(DeviceEntity device, string upgradeId)
    {
        if (!SatisfyBuildCondition(upgradeId) || !HasBuildCost(upgradeId)) return;

        bool built = BuildDevice(upgradeId, device.CachedTransform.position);
        if (built)
            GF.Entity.HideEntity(device.Entity);
    }


    public static bool BuildDevice(string deviceID, Vector3 position)
    {
        if (!SatisfyBuildCondition(deviceID) || !HasBuildCost(deviceID)) return false;

        Device deviceData = DeviceDataModel.GetDeviceData(deviceID);
        if (deviceData == null) return false;

        if (!ItemCollectionDataModel.ConsumeItems(deviceData.CostMaterial)) return false;

        var deviceParams = EntityParams.Create(position);
        deviceParams.Set(DeviceEntity.P_DeviceData, deviceData);
        //建造参数
        GF.Entity.ShowEntity<DeviceEntity>(deviceData.PrefabName, Const.EntityGroup.Building, deviceParams);

        return true;
    }

    public static bool HasBuildCost(string deviceID)
    {
        Device device = DeviceDataModel.GetDeviceData(deviceID);
        if (device == null) return false;
        return ItemCollectionDataModel.HasItem(device.CostMaterial);
    }

    /// <summary>
    /// 检查建造所需解锁条件（UnlockCondition），为空/None 则视为无需校验。
    /// </summary>
    public static bool SatisfyBuildCondition(string deviceID)
    {
        var device = DeviceDataModel.GetDeviceData(deviceID);
        return UnlockCondition.IsSatisfied(device.BuildCondition);
    }
}