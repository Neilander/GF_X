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
    private Dictionary<string, BuildingData> buildingDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        buildingDataDic = new();
        var buildingTb = GF.DataTable.GetDataTable<BuildingTable>();
        foreach (var row in buildingTb.GetAllDataRows())
        {
            ImportBuildingDataRow(row);
        }
    }

    protected override void OnRelease() { }

    public static BuildingData GetBuildingData(string buildingIdentifier)
    {
        var buildingDataModel = GF.DataModel.GetDataModel<BuildingDataModel>();
        if (buildingDataModel.buildingDataDic.TryGetValue(buildingIdentifier, out var building)) return building;
        return null;
    }

    private void ImportBuildingDataRow(BuildingTable row)
    {
        int maxLv = row.Type == BuilType.Tech ? 1 : 3; // 科技建筑只有1级，其他建筑有3级
        for (int lv = 1; lv <= maxLv; lv++)
        {
            BuildingData building = new(row.Identifier + "_Lv" + lv,
                                row.Type,
                                row.Archetype,
                                lv == 1 ? row.Lv1PrefabPath : lv == 2 ? row.Lv2PrefabPath : row.Lv3PrefabPath,
                                row.NameKey,
                                row.DescKey,
                                lv,
                                lv == 1 ? row.Lv1Cost : lv == 2 ? row.Lv2Cost : row.Lv3Cost,
                                lv == 1 ? row.Lv1HP : lv == 2 ? row.Lv2HP : row.Lv3HP,
                                lv == 1 ? row.Lv1Atk : lv == 2 ? row.Lv2Atk : row.Lv3Atk,
                                lv == 1 ? row.Lv1Def : lv == 2 ? row.Lv2Def : row.Lv3Def,
                                row.UniqueValues,
                                row.UnitID,
                                row.Production,
                                lv == 3 ? null :
                                row.Type == BuilType.Base || row.Type == BuilType.Army || row.Type == BuilType.Def ?
                                lv == 1 ? new string[] { row.Tech1ID, row.Tech2ID } : new string[] { row.Tech3ID, row.Tech4ID } :
                                row.Type == BuilType.Prod ? lv == 1 ? new string[] { row.Tech1ID } : new string[] { row.Tech2ID } :
                                row.Type == BuilType.Tech ? new string[] { row.Tech1ID, row.Tech2ID, row.Tech3ID, row.Tech4ID } : null);
            buildingDataDic[building.Identifier] = building;
        }
        return;
    }
}
