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
/// 建筑数据模型类, 预读各建筑数据
/// </summary>
public class BuildingDataModel : DataModelBase
{
    private Dictionary<string, Building> buildingDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        buildingDataDic = new();
        var buildingTb = GF.DataTable.GetDataTable<BuildingTable>();
        foreach (var row in buildingTb.GetAllDataRows())
        {
            Building building = ImportBuildingDataRow(row);
            buildingDataDic[building.Identifier] = building;
        }
    }

    protected override void OnRelease() { }

    public static Building GetBuildingData(string buildingIdentifier)
    {
        var buildingDataModel = GF.DataModel.GetDataModel<BuildingDataModel>();
        if (buildingDataModel.buildingDataDic.TryGetValue(buildingIdentifier, out var building)) return building;
        return null;
    }

    private Building ImportBuildingDataRow(BuildingTable row)
    {
        Building building = new(row.Identifier,
                                row.Type,
                                row.PrefabName,
                                row.NameKey,
                                row.DescriptionKey,
                                row.Lv,
                                row.Cost,
                                row.HP,
                                row.Atk,
                                row.Def,
                                row.UniqueValues,
                                row.UnitID,
                                row.Production,
                                row.TechIDs);
        return building;
    }
}
