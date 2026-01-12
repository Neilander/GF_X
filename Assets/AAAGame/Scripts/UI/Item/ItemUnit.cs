using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ItemUnit : UIItemBase
{
    private string itemIdentifier;
    private int quantity;
    private bool IsRequiredItem;
    public void RefreshAmount()
    {
        var currentAmount = ItemCollectionDataModel.GetItemAmount(itemIdentifier);
        varNumText.text = $"{quantity}({currentAmount})";
        if (IsRequiredItem)
        {
            varNumText.color = currentAmount >= quantity ? Color.white : Color.red;
        }
        else
        {
            varNumText.color = Color.white;
        }
    }
    public void SetData(string itemIdentifier, int quantity, bool IsRequiredItem = false)
    {
        this.itemIdentifier = itemIdentifier;
        this.quantity = quantity;
        this.IsRequiredItem = IsRequiredItem;
        varItemUnit.SetSprite(ItemDataModel.GetItemData(itemIdentifier).SpriteName);
        RefreshAmount();
    }
}
