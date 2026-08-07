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
/// 科技数据缓存模型。
/// 当前建筑科技主链路仍依赖这层缓存从 BuildingTable 汇总 TechData。
/// </summary>
public class TechDataModel : DataModelBase
{
    private Dictionary<string, TechData> techDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        techDataDic = new();
        foreach (BuildingTable row in LogicRuntimeDataTableCache.BuildingRows)
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
                                row.Tech1SkillID,
                                row.Tech1NameKey,
                                row.Tech1DescKey,
                                row.Tech1Cost,
                                row.Tech1UniqueValues,
                                row.Tech1ScopeType,
                                row.Tech1UnitScope,
                                row.Tech1SizeScope,
                                row.Tech1TagScope,
                                row.Tech1ArchScope,
                                row.Tech1SpritePath,
                                row.Tech1Stackable);
        techDataDic[tech.Identifier] = tech;
        if (string.IsNullOrEmpty(row.Tech2ID)) return;
        tech = new(row.Tech2ID,
                                row.Tech2SkillID,
                                row.Tech2NameKey,
                                row.Tech2DescKey,
                                row.Tech2Cost,
                                row.Tech2UniqueValues,
                                row.Tech2ScopeType,
                                row.Tech2UnitScope,
                                row.Tech2SizeScope,
                                row.Tech2TagScope,
                                row.Tech2ArchScope,
                                row.Tech2SpritePath,
                                row.Tech2Stackable);
        techDataDic[tech.Identifier] = tech;
        if (string.IsNullOrEmpty(row.Tech3ID)) return;
        tech = new(row.Tech3ID,
                                row.Tech3SkillID,
                                row.Tech3NameKey,
                                row.Tech3DescKey,
                                row.Tech3Cost,
                                row.Tech3UniqueValues,
                                row.Tech3ScopeType,
                                row.Tech3UnitScope,
                                row.Tech3SizeScope,
                                row.Tech3TagScope,
                                row.Tech3ArchScope,
                                row.Tech3SpritePath,
                                row.Tech3Stackable);
        techDataDic[tech.Identifier] = tech;
        if (string.IsNullOrEmpty(row.Tech4ID)) return;
        tech = new(row.Tech4ID,
                                row.Tech4SkillID,
                                row.Tech4NameKey,
                                row.Tech4DescKey,
                                row.Tech4Cost,
                                row.Tech4UniqueValues,
                                row.Tech4ScopeType,
                                row.Tech4UnitScope,
                                row.Tech4SizeScope,
                                row.Tech4TagScope,
                                row.Tech4ArchScope,
                                row.Tech4SpritePath,
                                row.Tech4Stackable);
        techDataDic[tech.Identifier] = tech;
    }
}
