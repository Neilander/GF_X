using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ResourceModifyUnit : UIItemBase
{
    private IngameValueType resourceType;
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
        var currentAmount = InGameDataModel.GetValue(resourceType);
        varNumText.text = $"{currentAmount}";
    }
    public void SetData(IngameValueType resourceType)
    {
        this.resourceType = resourceType;
        varResourceIcon.SetSprite(InGameDataModel.GetResourceSprite(resourceType));
        RefreshAmount();
    }
    public void OnAddButtonClick()
    {
        GF.UI.ShowToast(string.Format(LocalizationTextDataModel.GetText("ResourceModify_Add"), resourceType));
        InGameDataModel.TryModifyValue(resourceType, 1, true);
    }
    public void OnReduceButtonClick()
    {
        GF.UI.ShowToast(string.Format(LocalizationTextDataModel.GetText("ResourceModify_Reduce"), resourceType));
        InGameDataModel.TryModifyValue(resourceType, -1, true);
    }
}
