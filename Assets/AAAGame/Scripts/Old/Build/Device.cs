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
    public UnlockCondition BuildCondition { get; protected set; }
    public Fix64 Workload { get; protected set; }
    public string PrefabName { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescriptionKey { get; protected set; }
    public string UpgradeID { get; protected set; }
    public UIViews? InteractionPanelID { get; protected set; }

    public Device(string identifier,
                  StringIntPair[] costMaterial,
                  UnlockCondition buildCondition,
                  Fix64 workload,
                  string prefabName,
                  string nameKey,
                  string descriptionKey,
                  string upgradeID,
                  UIViews? interactionPanelID)
    {
        Identifier = identifier;
        CostMaterial = costMaterial;
        BuildCondition = buildCondition;
        Workload = workload;
        PrefabName = prefabName;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        UpgradeID = upgradeID;
        InteractionPanelID = interactionPanelID;
    }
}
