using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;
using GameFramework;
using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// 监听交互焦点变化，打开/关闭交互提示UI（InteractOptionTips）。
/// 挂在任意常驻对象上（例如 UI Root / Player）。
/// </summary>
public class InteractOptionTipsPresenter : MonoBehaviour
{
    private bool _subscribed;
    private int _tipsFormId = -1;
    private int _buildTipsFormId = -1;
    private int _upgradeTipsFormId = -1;
    private int _infoTipsFormId = -1;
    private InteractionHost _currentTarget;
    private string _pendingUpgradeBuildingInstanceId;

    private void Awake()
    {
        if (_subscribed)
            return;
        GF.Event.Subscribe(InteractionFocusChangedEventArgs.EventId, OnFocusChanged);
        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        _subscribed = true;
    }

    private void OnDestroy()
    {
        if (!_subscribed)
            return;

        try
        {
            GF.Event.Unsubscribe(InteractionFocusChangedEventArgs.EventId, OnFocusChanged);
            GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        }
        catch (GameFrameworkException)
        {
            // ignore
        }
        finally
        {
            _subscribed = false;
        }
    }

    private void OnFocusChanged(object sender, GameEventArgs e)
    {
        long startTicks = Stopwatch.GetTimestamp();
        try
        {
            HandleFocusChanged(sender, e);
        }
        finally
        {
            MainThreadFrameProfiler.Record(MainThreadPerfScope.InteractionFocusPresenter, Stopwatch.GetTimestamp() - startTicks);
        }
    }

    private void HandleFocusChanged(object sender, GameEventArgs e)
    {
        var args = e as InteractionFocusChangedEventArgs;
        if (args == null)
            return;

        if (args.Target == null && !string.IsNullOrEmpty(_pendingUpgradeBuildingInstanceId))
            return;

        if (args.Target != null)
            _pendingUpgradeBuildingInstanceId = null;

        _currentTarget = args.Target;
        PresentTarget(_currentTarget);
    }

    private void OnTechUnlocked(object sender, GameEventArgs e)
    {
        TechUnlockedEventArgs args = e as TechUnlockedEventArgs
                                     ?? throw new InvalidOperationException("Tech unlocked event payload is invalid.");
        BuildingEntity building = ResolveTargetBuilding(_currentTarget);
        if (building == null
            || !string.Equals(
                building.BuildingInstanceId,
                args.SourceBuildingInstanceId,
                StringComparison.Ordinal))
        {
            return;
        }

        if (building.buildingData == null)
            throw new InvalidOperationException("Focused building has no building data during tech unlock presentation.");

        _pendingUpgradeBuildingInstanceId = args.SourceBuildingInstanceId;
    }

    private void PresentTarget(InteractionHost target)
    {
        if (target == null)
            _currentTarget = null;

        if (target == null)
        {
            CloseInteractTips();
            CloseBuildTips();
            CloseUpgradeTips();
            CloseInfoTips();
            _tipsFormId = -1;
            _buildTipsFormId = -1;
            _upgradeTipsFormId = -1;
            _infoTipsFormId = -1;
            Debug.Log("[Interact Tips] Close tips (no focus)");
            return;
        }

        if (ShouldShowBuildTips(target))
        {
            CloseInteractTips();
            CloseUpgradeTips();
            CloseInfoTips();
            long buildTipsStartTicks = Stopwatch.GetTimestamp();
            OpenOrUpdateBuildTips(target);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.InteractionBuildTipsRequest, Stopwatch.GetTimestamp() - buildTipsStartTicks);
            return;
        }

        if (ShouldShowUpgradeTips(target))
        {
            CloseInteractTips();
            CloseBuildTips();
            CloseInfoTips();
            OpenOrUpdateUpgradeTips(target);
            return;
        }

        if (ShouldShowInfoTips(target))
        {
            CloseInteractTips();
            CloseBuildTips();
            CloseUpgradeTips();
            OpenOrUpdateInfoTips(target);
            return;
        }

        CloseBuildTips();
        CloseUpgradeTips();
        CloseInfoTips();

        if (_tipsFormId > 0 && GF.UI.HasUIForm(_tipsFormId))
        {
            var uiForm = GF.UI.GetUIForm(_tipsFormId) as UIForm;
            var logic = uiForm != null ? uiForm.Logic as InteractOptionTips : null;
            if (logic != null)
            {
                Debug.Log($"[Interact Tips] Update tips target={target.Transform.name}");
                logic.ApplyTarget(target);
                return;
            }

            GF.UI.Close(_tipsFormId);
            _tipsFormId = -1;
        }

        var uiParams = UIParams.Create();

        Debug.Log($"[Interact Tips] Open tips for target={target.Transform.name}");
        uiParams.Set(InteractOptionTips.P_TargetHost, target);
        _tipsFormId = GF.UI.OpenUIForm(UIViews.InteractOptionTips, uiParams);
    }

    private static BuildingEntity ResolveTargetBuilding(InteractionHost target)
    {
        if (target == null)
            return null;

        return target.Owner as BuildingEntity ?? target.GetComponent<BuildingEntity>();
    }

    private void OpenOrUpdateBuildTips(InteractionHost target)
    {
        if (_buildTipsFormId > 0 && GF.UI.HasUIForm(_buildTipsFormId))
        {
            var uiForm = GF.UI.GetUIForm(_buildTipsFormId) as UIForm;
            var logic = uiForm != null ? uiForm.Logic as BuildingBuildTips : null;
            if (logic != null)
            {
                logic.ApplyTarget(target);
                return;
            }

            GF.UI.Close(_buildTipsFormId);
            _buildTipsFormId = -1;
        }

        var uiParams = UIParams.Create();
        uiParams.Set(BuildingBuildTips.P_TargetHost, target);
        _buildTipsFormId = GF.UI.OpenUIForm(UIViews.BuildingBuildTips, uiParams);
    }

    private void OpenOrUpdateUpgradeTips(InteractionHost target)
    {
        if (_upgradeTipsFormId > 0 && GF.UI.HasUIForm(_upgradeTipsFormId))
        {
            var uiForm = GF.UI.GetUIForm(_upgradeTipsFormId) as UIForm;
            var logic = uiForm != null ? uiForm.Logic as BuildingUpgradeTips : null;
            if (logic != null)
            {
                logic.ApplyTarget(target);
                return;
            }

            GF.UI.Close(_upgradeTipsFormId);
            _upgradeTipsFormId = -1;
        }

        var uiParams = UIParams.Create();
        uiParams.Set(BuildingUpgradeTips.P_TargetHost, target);
        _upgradeTipsFormId = GF.UI.OpenUIForm(UIViews.BuildingUpgradeTips, uiParams);
    }

    private void OpenOrUpdateInfoTips(InteractionHost target)
    {
        if (_infoTipsFormId > 0 && GF.UI.HasUIForm(_infoTipsFormId))
        {
            var uiForm = GF.UI.GetUIForm(_infoTipsFormId) as UIForm;
            var logic = uiForm != null ? uiForm.Logic as BuildingInfoTips : null;
            if (logic != null)
            {
                logic.ApplyTarget(target);
                return;
            }

            GF.UI.Close(_infoTipsFormId);
            _infoTipsFormId = -1;
        }

        var uiParams = UIParams.Create();
        uiParams.Set(BuildingInfoTips.P_TargetHost, target);
        _infoTipsFormId = GF.UI.OpenUIForm(UIViews.BuildingInfoTips, uiParams);
    }

    private static bool ShouldShowBuildTips(InteractionHost target)
    {
        if (target == null)
            return false;

        BuildingEntity building = target.Owner as BuildingEntity;
        if (building == null)
            building = target.GetComponent<BuildingEntity>();

        if (building == null || building.buildingData == null || building.buildingData.Lv != 0)
            return false;

        BuildManager buildManager = GameEntry.GetComponent<BuildManager>();
        return buildManager != null && buildManager.HasConstructOption(building);
    }

    private static bool ShouldShowUpgradeTips(InteractionHost target)
    {
        if (target == null)
            return false;

        List<IInteractionOption> options = new();
        target.GetOptions(options);
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is BuildingUpgradeInteractionOption)
                return true;
        }

        return false;
    }

    private static bool ShouldShowInfoTips(InteractionHost target)
    {
        if (target == null)
            return false;

        List<IInteractionOption> options = new();
        target.GetOptions(options);
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is BuildingInfoInteractionOption)
                return true;
        }

        return false;
    }

    private void CloseInteractTips()
    {
        if (_tipsFormId > 0 && GF.UI.HasUIForm(_tipsFormId))
            GF.UI.Close(_tipsFormId);
        else
            GF.UI.CloseUIForms(UIViews.InteractOptionTips);

        _tipsFormId = -1;
    }

    private void CloseBuildTips()
    {
        if (_buildTipsFormId > 0 && GF.UI.HasUIForm(_buildTipsFormId))
            GF.UI.Close(_buildTipsFormId);
        else
            GF.UI.CloseUIForms(UIViews.BuildingBuildTips);

        _buildTipsFormId = -1;
    }

    private void CloseUpgradeTips()
    {
        if (_upgradeTipsFormId > 0 && GF.UI.HasUIForm(_upgradeTipsFormId))
            GF.UI.Close(_upgradeTipsFormId);
        else
            GF.UI.CloseUIForms(UIViews.BuildingUpgradeTips);

        _upgradeTipsFormId = -1;
    }

    private void CloseInfoTips()
    {
        if (_infoTipsFormId > 0 && GF.UI.HasUIForm(_infoTipsFormId))
            GF.UI.Close(_infoTipsFormId);
        else
            GF.UI.CloseUIForms(UIViews.BuildingInfoTips);

        _infoTipsFormId = -1;
    }
}
