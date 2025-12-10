using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class CraftingDialog : UIFormBase
{
    List<CraftingFormula> craftingFormulas;
    Dictionary<CraftingUnit, CraftingFormula> craftingUnits;
    List<ItemUnit> itemUnits;
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        GF.Event.Subscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);
        craftingFormulas = Params.Get("craftingFormulas") as List<CraftingFormula>;
        RefreshList();
        RefreshCraftableUnits();
    }
    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);
        base.OnClose(isShutdown, userData);
    }
    private void RefreshList()
    {
        craftingUnits = new();
        itemUnits = new();
        foreach (var formula in craftingFormulas)
        {
            var craftingUnit = SpawnItem<UIItemObject>(varCraftingUnit, varContent).itemLogic as CraftingUnit;
            craftingUnits.Add(craftingUnit, formula);
            //初始化制造进度条
            craftingUnit.varFillProgress.onFull = () =>
            {
                foreach (var quantityItem in formula.RequiredItems)
                {
                    ItemCollectionDataModel.ModifyItemAmount(quantityItem.str, -quantityItem.num);
                }

                foreach (var quantityItem in formula.ProducedItems)
                {
                    ItemCollectionDataModel.ModifyItemAmount(quantityItem.str, quantityItem.num);
                }
            };
            //初始化物品列表
            foreach (var quantityItem in formula.RequiredItems)
            {
                var itemUnit = SpawnItem<UIItemObject>(varItemUnit, craftingUnit.varRequiredItemPanel).itemLogic as ItemUnit;
                itemUnits.Add(itemUnit);
                itemUnit.SetData(quantityItem.str, quantityItem.num);
            }
            foreach (var quantityItem in formula.ProducedItems)
            {
                var itemUnit = SpawnItem<UIItemObject>(varItemUnit, craftingUnit.varProducedItemPanel).itemLogic as ItemUnit;
                itemUnits.Add(itemUnit);
                itemUnit.SetData(quantityItem.str, quantityItem.num);
            }
        }
    }
    private void OnItemAmountChanged(object sender, GameFramework.Event.GameEventArgs e = null)
    {
        RefreshCraftableUnits();
    }
    private void RefreshCraftableUnits()
    {
        foreach (var craftingUnit in craftingUnits)
        {
            craftingUnit.Key.varFillProgress.gameObject.SetActive(
                craftingUnit.Value.RequiredItems.All(quantityItem => ItemCollectionDataModel.HasItem(quantityItem.str, quantityItem.num))
            );
        }
        foreach (var itemUnit in itemUnits)
        {
            itemUnit.RefreshAmount();
        }
    }
}
