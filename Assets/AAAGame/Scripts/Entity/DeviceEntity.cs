using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class DeviceEntity : EntityBase
{
    public const string P_DeviceData = "DeviceData";
    public Device deviceData;
    public bool HasInteractionPanel => deviceData != null && deviceData.InteractionPanelID != null;
    public bool HasUnlockedUpgrade
    {
        get
        {
            if (deviceData == null)
                return false;

            if (string.IsNullOrWhiteSpace(deviceData.UpgradeID))
                return false;

            return BuildManager.SatisfyBuildCondition(deviceData.UpgradeID);
        }
    }


    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        // 从 EntityParams 注入 deviceData（若为占位点，可能为空）
        deviceData = null;
        if (Params != null && Params.TryGet<VarObject>(P_DeviceData, out var varObj) && varObj != null)
            deviceData = varObj.Value as Device;

        if (HasInteractionPanel || HasUnlockedUpgrade)
        {
            EnsureInteractionHost();
        }
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        // 对象池安全：清理运行时引用，避免下次复用时指向旧数据
        var host = GetComponent<InteractionHost>();
        if (host != null)
            host.ResetOptions();

        deviceData = null;
        base.OnHide(isShutdown, userData);
    }

    private void EnsureInteractionHost()
    {
        var host = GetComponent<InteractionHost>();
        if (host == null)
            host = gameObject.AddComponent<InteractionHost>();

        // 防御：即使 OnHide 没被调用，也不让旧交互泄漏到下一次复用。
        host.ResetOptions();
        host.Init(this);

        // 固定按键：开面板 = Primary，升级/占位建造 = Secondary
        // key 重复时 InteractionHost 会报错。
        if (HasInteractionPanel)
        {
            string displayName = LocalizationTextDataModel.GetText("InteractOption_Craft");

            host.AddOption<DeviceOpenPanelInteractionOption>(InputKey.InteractionPrimary, displayName, null);
        }

        if (HasUnlockedUpgrade)
        {
            string displayName = deviceData.Identifier == null ? LocalizationTextDataModel.GetText("InteractOption_Build") : LocalizationTextDataModel.GetText("InteractOption_Upgrade");

            InteractionParams @params = InteractionParams.Create();
            @params.Set<VarString>("UpgradeId", deviceData.UpgradeID);

            host.AddOption<DeviceUpgradeInteractionOption>(InputKey.InteractionSecondary, displayName, @params);
        }
    }
}
