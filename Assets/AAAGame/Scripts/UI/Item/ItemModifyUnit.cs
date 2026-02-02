using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ItemModifyUnit : UIItemBase
{
    private string itemIdentifier;
    protected override void OnInit()
    {
        base.OnInit();
        varAddBtn.onClick.RemoveAllListeners();
        varReduceBtn.onClick.RemoveAllListeners();
        varAddBtn.onClick.AddListener(OnAddButtonClick);
        varReduceBtn.onClick.AddListener(OnReduceButtonClick);
    }
    public void RefreshAmount()
    {
        var currentAmount = ItemCollectionDataModel.GetItemAmount(itemIdentifier);
        varNumText.text = $"{currentAmount}";
    }
    public void SetData(string itemIdentifier)
    {
        this.itemIdentifier = itemIdentifier;
        varItemIcon.SetSprite(ItemDataModel.GetItemData(itemIdentifier).SpriteName);
        RefreshAmount();
    }
    public void OnAddButtonClick()
    {
        GF.UI.ShowToast($"增加物品 {itemIdentifier}");
        ItemCollectionDataModel.ModifyItemAmount(itemIdentifier, 1);
    }
    public void OnReduceButtonClick()
    {
        GF.UI.ShowToast($"减少物品 {itemIdentifier}");
        ItemCollectionDataModel.ModifyItemAmount(itemIdentifier, -1);
    }
}
