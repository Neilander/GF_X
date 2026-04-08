using GameFramework.Event;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class ResourceModifyBar : UIFormBase
{
    private readonly List<ResourceModifyUnit> resourceUnits = new();
    private static readonly IngameValueType[] SupportedResources =
    {
        IngameValueType.Coin,
    };

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnResourceChanged);
        RefreshList();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnResourceChanged);
        base.OnClose(isShutdown, userData);
    }

    private void OnResourceChanged(object sender, GameFramework.Event.GameEventArgs e = null)
    {
        RefreshResourceAmount();
    }

    private void RefreshList()
    {
        UnspawnAllItem<UIItemObject>(varResourceModifyUnit);
        resourceUnits.Clear();

        for (int i = 0; i < SupportedResources.Length; i++)
        {
            var unit = SpawnItem<UIItemObject>(varResourceModifyUnit, varResourcePanel).itemLogic as ResourceModifyUnit;
            if (unit == null)
                continue;

            resourceUnits.Add(unit);
            unit.SetData(SupportedResources[i]);
        }
    }

    private void RefreshResourceAmount()
    {
        foreach (var unit in resourceUnits)
        {
            unit.RefreshAmount();
        }
    }
}
