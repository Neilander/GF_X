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
public enum Archetype { None, Coding, Sightseeing, Butchery, Delivery, Firefighting }
/// <summary>
/// 建筑数据格式类
/// </summary>
public class BuildingData
{
    public string Identifier { get; protected set; } //格式为 "Buil_Type_LvX"，如 "Buil_Prod_Lv2"。其中 Type 是 BuilType 枚举，X 是等级数字。
    public int Cost { get; protected set; }
    public Fix64[] UniqueValues { get; protected set; }
    public BuilType Type { get; protected set; }
    public Archetype Arche { get; protected set; }
    public string PrefabPath { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescKey { get; protected set; }
    public int Lv { get; protected set; }
    public Fix64 HP { get; protected set; }
    public Fix64 Atk { get; protected set; }
    public Fix64 Def { get; protected set; }
    public string UnitID { get; protected set; }
    public int Production { get; protected set; }
    public string[] TechIDs { get; protected set; } // 升级时的科技选项

    public BuildingData(string identifier,
        BuilType type,
        Archetype archetype,
        string prefabPath,
        string nameKey,
        string descKey,
        int lv,
        int cost,
        Fix64 hp,
        Fix64 atk,
        Fix64 def,
        Fix64[] uniqueValues,
        string unitID,
        int production,
        string[] techIDs)
    {
        Identifier = identifier;
        Cost = cost;
        Lv = lv;
        Type = type;
        Arche = archetype;
        PrefabPath = prefabPath;
        NameKey = nameKey;
        DescKey = descKey;
        HP = hp;
        Atk = atk;
        Def = def;
        UniqueValues = uniqueValues;
        UnitID = unitID;
        Production = production;
        TechIDs = techIDs;
    }

}
