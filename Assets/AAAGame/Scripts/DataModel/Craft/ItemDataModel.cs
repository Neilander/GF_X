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
/// 物品数据模型类, 预读各物品数据
/// </summary>
public class ItemDataModel : DataModelBase
{
    private Dictionary<string, Item> itemDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        itemDataDic = new();
        foreach (ItemType itemType in Enum.GetValues(typeof(ItemType)))
        {
            ImportItemDataTable(itemType);
        }
    }
    protected override void OnRelease() { }

    public static Item GetItemData(string itemIdentifier)
    {
        var itemDataModel = GF.DataModel.GetDataModel<ItemDataModel>();
        if (itemDataModel.itemDataDic.TryGetValue(itemIdentifier, out var item)) return item;
        return null;
    }
    private void ImportItemDataTable(ItemType itemType)
    {
        switch (itemType)
        {
            case ItemType.Material:
                var itemTb = GF.DataTable.GetDataTable<ItemTable>(itemType.ToString());
                foreach (var row in itemTb.GetAllDataRows())
                {
                    Item item = ImportItemDataRow(row, itemType);
                    itemDataDic[item.Identifier] = item;
                }
                break;
            case ItemType.Consumable:
            case ItemType.Equipment:
            case ItemType.Tool:
                var itemWETb = GF.DataTable.GetDataTable<ItemWithEffectTable>(itemType.ToString());
                foreach (var row in itemWETb.GetAllDataRows())
                {
                    Item item = ImportItemDataRow(row, itemType);
                    itemDataDic[item.Identifier] = item;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(itemType), itemType, null);
        }
    }
    private Item ImportItemDataRow(ItemTable row, ItemType itemType)
    {
        Item item = new(row.Identifier,
                         row.Rarity,
                         row.MaxStack,
                         row.SpriteName,
                         row.NameKey,
                         row.DescriptionKey,
                         itemType,
                         row.Tags);
        return item;
    }
    private Item ImportItemDataRow(ItemWithEffectTable row, ItemType itemType)
    {
        ItemWithEffect item = new(row.Identifier,
                         row.Rarity,
                         row.MaxStack,
                         row.SpriteName,
                         row.NameKey,
                         row.DescriptionKey,
                         itemType,
                         row.Tags,
                         row.PropertyNumerals,
                         row.EffectNumerals,
                         row.EffectIntroduction);
        return item;
    }

}
