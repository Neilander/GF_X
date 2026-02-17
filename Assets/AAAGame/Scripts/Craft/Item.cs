using GameFramework;
using GameFramework.DataTable;
using GameFramework.Event;
using Newtonsoft.Json;
using PlasticGui.Configuration.CloudEdition.Welcome;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 物品稀有度枚举
/// </summary>
public enum ItemRarity
{
    Common,
    Rare,
    Epic,
    Legendary,
    Heroic
}

/// <summary>
/// 物品类型枚举
/// </summary>
public enum ItemType
{
    Consumable,
    Equipment,
    Material,
    Tool
}
/// <summary>
/// 物品标签枚举
/// </summary>
public enum ItemTag
{
    Starting,
    Relic,
    AAA,
    BBB,
    CCC,
    DDD
}

/// <summary>
/// 物品数据格式类
/// </summary>
public class Item
{
    public string Identifier { get; protected set; }
    public ItemRarity Rarity { get; protected set; }
    public int MaxStack { get; protected set; }
    public string SpriteName { get; protected set; }
    public string NameKey { get; protected set; }
    public string DescriptionKey { get; protected set; }
    public ItemType Type { get; protected set; }
    public ItemTag[] Tags { get; protected set; }
    public Item() { }
    public Item(string identifier, ItemRarity rarity, int maxStack, string spriteName, string nameKey, string descriptionKey, ItemType type, ItemTag[] tags)
        => (Identifier, Rarity, MaxStack, SpriteName, NameKey, DescriptionKey, Type, Tags)
           = (identifier, rarity, maxStack, spriteName, nameKey, descriptionKey, type, tags);
}
