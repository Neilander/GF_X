using GameFramework;
using GameFramework.Event;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using UnityEngine;
using UnityGameFramework.Runtime;


public enum BuilType { Base, Army, Prod, Tech, Def }
/// <summary>
/// 建筑数据格式类
/// </summary>
public class Building
{
    public string Identifier { get; protected set; } //格式为 "Buil_Type_LvX"，如 "Buil_Prod_Lv2"。其中 Type 是 BuilType 枚举，X 是等级数字。
    public int Cost { get; protected set; }
    public Fix64[] UniqueValues { get; protected set; }
    public BuilType Type { get; protected set; }
    public string PrefabName { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescKey { get; protected set; }
    public int Lv { get; protected set; }
    public int HP { get; protected set; }
    public int Atk { get; protected set; }
    public int Def { get; protected set; }
    public string UnitID { get; protected set; }
    public int Production { get; protected set; }
    public string[] TechIDs { get; protected set; }

    public Building(string identifier,
        BuilType type,
        string prefabName,
        string nameKey,
        string descKey,
        int lv,
        int cost,
        int hp,
        int atk,
        int def,
        Fix64[] uniqueValues,
        string unitID,
        int production,
        string[] techIDs)
    {
        Identifier = identifier;
        Cost = cost;
        Lv = lv;
        Type = type;
        PrefabName = prefabName;
        NameKey = nameKey;
        DescKey = descKey;
        HP = hp;
        Atk = atk;
        Def = def;
        UniqueValues = uniqueValues;
        UnitID = unitID;
        Production = production;
        TechIDs = techIDs;
        PrefabName = prefabName;
        NameKey = nameKey;
        DescKey = descKey;
    }

    public static string GetUpgradeID(string identifier)
    {
        // 升级ID格式约定：在原ID基础上替换等级数字，如 "Buil_Prod_Lv2" 的升级ID为 "Buil_Prod_Lv3"
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

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
}
