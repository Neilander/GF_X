using UnityEngine;
using GameFramework.Event;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class SupplyUIForm : UIFormBase
{
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnSupplyChanged);
        RefreshText();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnSupplyChanged);
        base.OnClose(isShutdown, userData);
    }

    private void OnSupplyChanged(object sender, GameEventArgs e)
    {
        var args = e as IngameValueChangedEventArgs;
        if (args.DataType == IngameValueType.CurrentSupply || args.DataType == IngameValueType.MaxSupply)
        {
            RefreshText();
        }
    }
    private void RefreshText()
    {
        int currentSupply = InGameDataModel.GetCurrentSupply();
        int maxSupply = InGameDataModel.GetMaxSupply();
        varSupplyText.text = string.Format(LocalizationTextDataModel.GetText("Supply_CurrentMax"), currentSupply, maxSupply);
    }
}
