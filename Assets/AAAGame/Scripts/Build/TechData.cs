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
/// 科技数据格式类
/// </summary>
public class TechData
{
    public string Identifier { get; protected set; }
    public int Cost { get; protected set; }
    public Fix64[] UniqueValues { get; protected set; }
    public string SpritePath { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescKey { get; protected set; }

    public TechData(string identifier,
        string nameKey,
        string descKey,
        int cost,
        Fix64[] uniqueValues,
        string spritePath)
    {
        Identifier = identifier;
        Cost = cost;
        NameKey = nameKey;
        DescKey = descKey;
        UniqueValues = uniqueValues;
        SpritePath = spritePath;
    }

}
