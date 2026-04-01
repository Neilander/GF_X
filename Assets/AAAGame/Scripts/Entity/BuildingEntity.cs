using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class BuildingEntity : EntityBase
{
    public const string P_BuildingData = "BuildingData";
    public BuildingData buildingData;
    public bool HasInteractionPanel => true; // 先占位，后续根据所有者等条件判断
    public bool HasUnlockedUpgrade
    {
        get
        {
            if (buildingData == null)
                return false;

            //return BuildManager.SatisfyBuildCondition(UpgradeID);
            return BuildingData.GetUpgradeID(buildingData.Identifier) != null; // 先占位，只要有升级ID就认为有升级
        }
    }


    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        // 从 EntityParams 注入 buildingData（若为占位点，可能为空）
        buildingData = null;
        if (Params != null && Params.TryGet<VarObject>(P_BuildingData, out var varObj) && varObj != null)
            buildingData = varObj.Value as BuildingData;

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

        buildingData = null;
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
            string displayName = buildingData.Lv == 0 ? LocalizationTextDataModel.GetText("InteractOption_Build") : LocalizationTextDataModel.GetText("InteractOption_Upgrade");

            InteractionParams @params = InteractionParams.Create();
            @params.Set<VarString>("UpgradeId", BuildingData.GetUpgradeID(buildingData.Identifier));

            host.AddOption<DeviceUpgradeInteractionOption>(InputKey.InteractionSecondary, displayName, @params);
        }
    }
}
