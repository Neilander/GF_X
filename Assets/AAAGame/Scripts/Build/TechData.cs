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
    public string SkillID { get; protected set; }
    public int Cost { get; protected set; }
    public Fix64[] UniqueValues { get; protected set; }
    public TechScopeType ScopeType { get; protected set; }
    public string[] UnitScope { get; protected set; }
    public UnitSize[] SizeScope { get; protected set; }
    public UnitTag[] TagScope { get; protected set; }
    public Archetype[] ArchScope { get; protected set; }
    public string SpritePath { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescKey { get; protected set; }
    public bool IsStackable { get; protected set; }

    public TechData(string identifier,
        string skillID,
        string nameKey,
        string descKey,
        int cost,
        Fix64[] uniqueValues,
        TechScopeType scopeType,
        string[] unitScope,
        UnitSize[] sizeScope,
        UnitTag[] tagScope,
        Archetype[] archScope,
        string spritePath,
        bool isStackable)
    {
        Identifier = identifier;
        SkillID = skillID;
        Cost = cost;
        NameKey = nameKey;
        DescKey = descKey;
        UniqueValues = uniqueValues != null ? (Fix64[])uniqueValues.Clone() : null;
        ScopeType = scopeType;
        UnitScope = unitScope != null ? (string[])unitScope.Clone() : null;
        SizeScope = sizeScope != null ? (UnitSize[])sizeScope.Clone() : null;
        TagScope = tagScope != null ? (UnitTag[])tagScope.Clone() : null;
        ArchScope = archScope != null ? (Archetype[])archScope.Clone() : null;
        SpritePath = spritePath;
        IsStackable = isStackable;
    }

}
