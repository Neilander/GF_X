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
/// 物品集数据模型类, 持久化储存各物品持有数量
/// </summary>
public class ItemCollectionDataModel : DataModelStorageBase
{
    [JsonProperty]
    private Dictionary<string, int> itemCollectionDataDic;

    protected override void OnInitialDataModel()
    {
        itemCollectionDataDic = new Dictionary<string, int>();
    }

    public static int GetItemAmount(string itemIdentifier)
    {
        var dataModel = GF.DataModel.GetOrCreate<ItemCollectionDataModel>();
        if (dataModel.itemCollectionDataDic.TryGetValue(itemIdentifier, out int amount))
        {
            return amount;
        }
        return 0;
    }

    public static void SetItemAmount(string itemIdentifier, int value, bool triggerEvent = true)
    {
        var dataModel = GF.DataModel.GetOrCreate<ItemCollectionDataModel>();
        int oldValue = dataModel.itemCollectionDataDic.TryGetValue(itemIdentifier, out int amount) ? amount : 0;

        var itemTb = GF.DataTable.GetDataTable<ItemTable>(itemIdentifier.Substring(0, itemIdentifier.IndexOf('_')));
        int maxStack = itemTb.GetDataRow((row) => row.Identifier == itemIdentifier).MaxStack;

        dataModel.itemCollectionDataDic[itemIdentifier] = maxStack != -1 ? Mathf.Clamp(value, 0, maxStack) : Mathf.Max(value, 0);

        if (triggerEvent)
            GF.Event.Fire(dataModel, ItemAmountChangedEventArgs.Create(itemIdentifier, oldValue, value));
    }

    public static void ModifyItemAmount(string itemIdentifier, int delta, bool triggerEvent = true)
    {
        SetItemAmount(itemIdentifier, GetItemAmount(itemIdentifier) + delta, triggerEvent);
    }

    public static bool HasItem(string itemIdentifier, int quantity = 1)
    {
        return GetItemAmount(itemIdentifier) >= quantity;
    }
}
