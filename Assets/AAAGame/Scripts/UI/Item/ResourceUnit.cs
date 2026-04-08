using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ResourceUnit : UIItemBase
{
    private IngameValueType resourceType;
    private int quantity;
    private bool IsRequiredItem;

    public void RefreshAmount()
    {
        var currentAmount = InGameDataModel.GetValue(resourceType);
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
    public void SetData(IngameValueType resourceType, int quantity, bool IsRequiredItem = false)
    {
        this.resourceType = resourceType;
        this.quantity = quantity;
        this.IsRequiredItem = IsRequiredItem;
        varResourceUnit.SetSprite(InGameDataModel.GetResourceSprite(resourceType));
        RefreshAmount();
    }
}
