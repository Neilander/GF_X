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
/// 设备数据格式类
/// </summary>
public class Device
{
    public string Identifier { get; protected set; }
    public StringIntPair[] CostMaterial { get; protected set; }
    public string BuildCapability { get; protected set; }
    public Fix64 Workload { get; protected set; }
    public string PrefabName { get; protected set; }
    public string Name { get; protected set; }
    public string Description { get; protected set; }

    public Device() { }

    public Device(string identifier, StringIntPair[] costMaterial, string buildCapability, Fix64 workload, string prefabName, string name, string description)
        => (Identifier, CostMaterial, BuildCapability, Workload, PrefabName, Name, Description)
        = (identifier, costMaterial, buildCapability, workload, prefabName, name, description);
}
