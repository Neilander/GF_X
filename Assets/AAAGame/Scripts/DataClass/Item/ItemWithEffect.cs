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
/// 效果物品数据格式类
/// </summary>
public class ItemWithEffect : Item
{
    public StringFix64Pair[] PropertyNumerals;
    public Fix64[] EffectNumerals;
    public string EffectIntroduction { get; protected set; }
    public ItemWithEffect() { }
    public ItemWithEffect(string identifier, ItemRarity rarity, int maxStack, string spriteName, string name, string description, ItemType type, ItemTag[] tags, StringFix64Pair[] propertyNumerals, Fix64[] effectNumerals, string effectIntroduction)
        : base(identifier, rarity, maxStack, spriteName, name, description, type, tags)
        => (PropertyNumerals, EffectNumerals, EffectIntroduction) = (propertyNumerals, effectNumerals, effectIntroduction);
}
