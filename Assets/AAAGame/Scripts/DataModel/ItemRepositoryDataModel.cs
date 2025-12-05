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
/// 仓库数据类, 储存各物品数量
/// </summary>
public class ItemRepositoryDataModel : DataModelStorageBase
{
    [JsonProperty]
    private Dictionary<string, int> repositoryDataDic;

    protected override void OnInitialDataModel()
    {
        repositoryDataDic = new Dictionary<string, int>();
    }

    public int GetItemAmount(string itemIdentifier)
    {
        if (repositoryDataDic.TryGetValue(itemIdentifier, out int amount))
        {
            return amount;
        }
        return 0;
    }

    public void SetItemAmount(string itemIdentifier, int value, bool triggerEvent = true)
    {
        int oldValue = repositoryDataDic.TryGetValue(itemIdentifier, out int amount) ? amount : 0;

        var itemTb = GF.DataTable.GetDataTable<ItemTable>(itemIdentifier.Substring(0, itemIdentifier.IndexOf('_')));
        int maxStack = itemTb.GetDataRow((row) => row.Identifier == itemIdentifier).MaxStack;

        repositoryDataDic[itemIdentifier] = maxStack != -1 ? Mathf.Clamp(value, 0, maxStack) : Mathf.Max(value, 0);

        if (triggerEvent)
            GF.Event.Fire(this, ItemAmountChangedEventArgs.Create(itemIdentifier, oldValue, value));
    }

    public void ModifyItemAmount(string itemIdentifier, int delta, bool triggerEvent = true)
    {
        SetItemAmount(itemIdentifier, GetItemAmount(itemIdentifier) + delta, triggerEvent);
    }

    public bool HasItem(string itemIdentifier)
    {
        return GetItemAmount(itemIdentifier) > 0;
    }
}
