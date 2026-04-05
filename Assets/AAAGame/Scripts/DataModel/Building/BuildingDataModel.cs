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

    public static IEnumerable<BuildingData> GetAllBuildingData()
    {
        var buildingDataModel = GF.DataModel.GetDataModel<BuildingDataModel>();
        return buildingDataModel.buildingDataDic.Values;
    }


    public static string GetUpgradeID(string identifier)
    {
        // 升级ID格式约定：在原ID基础上替换等级数字，如 "Buil_Prod_Lv2" 的升级ID为 "Buil_Prod_Lv3"。若该升级ID在字典中不存在，则返回 null。
        int lastLvIndex = identifier.LastIndexOf("Lv", StringComparison.Ordinal);
        if (lastLvIndex < 0 || lastLvIndex + 2 >= identifier.Length)
            return null;

        string prefix = identifier.Substring(0, lastLvIndex + 2); // 包含 "Lv"
        string lvStr = identifier.Substring(lastLvIndex + 2);
        if (!int.TryParse(lvStr, out int lv))
            return null;

        int nextLv = lv + 1;
        string nextIdentifier = prefix + nextLv;
        return BuildingDataModel.GetBuildingData(nextIdentifier) != null ? nextIdentifier : null;
    }

    private void ImportBuildingDataRow(BuildingTable row)
    {
        if (row.Identifier.Substring(row.Identifier.Length - 3) == "Lv0")
        {
            BuildingData building = new(row.Identifier,
                                row.Type,
                                row.Archetype,
                                row.Lv1PrefabPath,
                                row.NameKey,
                                row.DescKey,
                                0,
                                0,
                                Fix64.One,
                                Fix64.Zero,
                                Fix64.Zero,
                                row.UniqueValues,
                                row.UnitID,
                                row.Production,
                                null);
            buildingDataDic[building.Identifier] = building;
        }
        else
        {
            int maxLv = row.Type == BuilType.Tech ? 1 : 3;
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
        }
    }
}
