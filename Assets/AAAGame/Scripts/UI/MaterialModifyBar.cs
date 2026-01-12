using GameFramework.Event;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class MaterialModifyBar : UIFormBase
{
    List<ItemModifyUnit> itemUnits;
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        GF.Event.Subscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);
        RefreshList();
    }
    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);
        base.OnClose(isShutdown, userData);
    }
    private void OnItemAmountChanged(object sender, GameFramework.Event.GameEventArgs e = null)
    {
        RefreshItemAmount();
    }
    private void RefreshList()
    {
        UnspawnAllItem<UIItemObject>(varItemModifyUnit);
        itemUnits = new();
        var itemTb = GF.DataTable.GetDataTable<ItemTable>("Material");
        foreach (var row in itemTb.GetAllDataRows())
        {
            var itemUnit = SpawnItem<UIItemObject>(varItemModifyUnit, varMaterialPanel).itemLogic as ItemModifyUnit;
            itemUnits.Add(itemUnit);
            itemUnit.SetData(row.Identifier);
        }
    }
    private void RefreshItemAmount()
    {
        foreach (var itemUnit in itemUnits)
        {
            itemUnit.RefreshAmount();
        }
    }
}
