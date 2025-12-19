using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class DeviceEntity : EntityBase
{
    public bool HasInteractivePanel { get; protected set; } = false;
    public bool HasUnlockedUpgrade { get; protected set; } = false;
    public string UpgradeDeviceID { get; protected set; } = string.Empty;
    protected UIViews interactivePanelID;

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
    }

    public void OnInteract()
    {
        if (HasInteractivePanel)
        {
            UIParams uiparams = UIParams.Create();
            GF.UI.OpenUIForm(interactivePanelID, uiparams);
        }
    }

}
