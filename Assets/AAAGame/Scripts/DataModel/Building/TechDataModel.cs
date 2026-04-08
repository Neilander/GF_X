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
/// 科技数据模型类, 预读各建筑的科技数据
/// </summary>
public class TechDataModel : DataModelBase
{
    private Dictionary<string, TechData> techDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        techDataDic = new();
        var buildingTb = GF.DataTable.GetDataTable<BuildingTable>();
        foreach (var row in buildingTb.GetAllDataRows())
        {
            ImportTechFromBuildingDataRow(row);
        }
    }

    protected override void OnRelease() { }

    public static TechData GetTechData(string buildingIdentifier)
    {
        var techDataModel = GF.DataModel.GetDataModel<TechDataModel>();
        if (techDataModel.techDataDic.TryGetValue(buildingIdentifier, out var tech)) return tech;
        return null;
    }

    private void ImportTechFromBuildingDataRow(BuildingTable row)
    {
        if (string.IsNullOrEmpty(row.Tech1ID)) return;
        TechData tech = new(row.Tech1ID,
                                row.Tech1NameKey,
                                row.Tech1DescKey,
                                row.Tech1Cost,
                                row.Tech1UniqueValues,
                                row.Tech1SpritePath,
                                row.Tech1Stackable);
        techDataDic[tech.Identifier] = tech;
        if (string.IsNullOrEmpty(row.Tech2ID)) return;
        tech = new(row.Tech2ID,
                                row.Tech2NameKey,
                                row.Tech2DescKey,
                                row.Tech2Cost,
                                row.Tech2UniqueValues,
                                row.Tech2SpritePath,
                                row.Tech2Stackable);
        techDataDic[tech.Identifier] = tech;
        if (string.IsNullOrEmpty(row.Tech3ID)) return;
        tech = new(row.Tech3ID,
                                row.Tech3NameKey,
                                row.Tech3DescKey,
                                row.Tech3Cost,
                                row.Tech3UniqueValues,
                                row.Tech3SpritePath,
                                row.Tech3Stackable);
        techDataDic[tech.Identifier] = tech;
        if (string.IsNullOrEmpty(row.Tech4ID)) return;
        tech = new(row.Tech4ID,
                                row.Tech4NameKey,
                                row.Tech4DescKey,
                                row.Tech4Cost,
                                row.Tech4UniqueValues,
                                row.Tech4SpritePath,
                                row.Tech4Stackable);
        techDataDic[tech.Identifier] = tech;
    }
}
