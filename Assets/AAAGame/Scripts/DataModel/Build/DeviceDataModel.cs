using GameFramework;
using GameFramework.Event;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 设备数据模型类, 预读各设备数据
/// </summary>
public class DeviceDataModel : DataModelBase
{
    private Dictionary<string, Device> deviceDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        deviceDataDic = new();
        var deviceTb = GF.DataTable.GetDataTable<DeviceTable>();
        foreach (var row in deviceTb.GetAllDataRows())
        {
            Device device = ImportDeviceDataRow(row);
            deviceDataDic[device.Identifier] = device;
        }
    }

    protected override void OnRelease() { }

    public static Device GetDeviceData(string deviceIdentifier)
    {
        var deviceDataModel = GF.DataModel.GetDataModel<DeviceDataModel>();
        if (deviceDataModel.deviceDataDic.TryGetValue(deviceIdentifier, out var device)) return device;
        return null;
    }

    private Device ImportDeviceDataRow(DeviceTable row)
    {
        Device device = new(row.Identifier,
                            row.CostMaterial,
                            row.BuildCapability,
                            row.Workload,
                            row.PrefabName,
                            GF.Localization.GetString(row.Name),
                            GF.Localization.GetString(row.Description));
        return device;
    }
}
