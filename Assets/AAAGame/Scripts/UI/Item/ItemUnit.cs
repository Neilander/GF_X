using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ItemUnit : UIItemBase
{
    private string itemIdentifier;
    private int quantity;
    public void RefreshAmount()
    {
        var currentAmount = ItemCollectionDataModel.GetItemAmount(itemIdentifier);
        varNumText.text = $"{quantity}/{currentAmount}";
        varNumText.color = currentAmount >= quantity ? Color.white : Color.red;
    }
    public void SetData(string itemIdentifier, int quantity)
    {
        this.itemIdentifier = itemIdentifier;
        this.quantity = quantity;
        varItemUnit.SetSprite(ItemDataModel.GetItemData(itemIdentifier).SpriteName);
        RefreshAmount();
    }
}
