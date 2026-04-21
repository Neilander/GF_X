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
/// 技能数据格式类
/// </summary>
public class SkillData
{
    public string Identifier { get; protected set; }
    public Fix64[] Lv1UniqueValues { get; protected set; }
    public int Lv1UsageCount { get; protected set; }
    public Fix64[] Lv2UniqueValues { get; protected set; }
    public int Lv2UsageCount { get; protected set; }
    public SkillType Type { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescKey { get; protected set; }
    public string SpritePath { get; protected set; }

    public SkillData(string identifier,
        Fix64[] lv1UniqueValues,
        int lv1UsageCount,
        Fix64[] lv2UniqueValues,
        int lv2UsageCount,
        SkillType type,
        string nameKey,
        string descKey,
        string spritePath)
    {
        Identifier = identifier;
        Lv1UniqueValues = lv1UniqueValues;
        Lv1UsageCount = lv1UsageCount;
        Lv2UniqueValues = lv2UniqueValues;
        Lv2UsageCount = lv2UsageCount;
        Type = type;
        NameKey = nameKey;
        DescKey = descKey;
        SpritePath = spritePath;
    }
}