using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;
using GameFramework;

/// <summary>
/// 监听交互焦点变化，打开/关闭交互提示UI（InteractOptionTips）。
/// 挂在任意常驻对象上（例如 UI Root / Player）。
/// </summary>
public class InteractOptionTipsPresenter : MonoBehaviour
{
    private bool _subscribed;
    private int _tipsFormId = -1;
    private int _buildTipsFormId = -1;

    private void Awake()
    {
        if (_subscribed)
            return;
        GF.Event.Subscribe(InteractionFocusChangedEventArgs.EventId, OnFocusChanged);
        _subscribed = true;
    }

    private void OnDestroy()
    {
        if (!_subscribed)
            return;

        try
        {
            GF.Event.Unsubscribe(InteractionFocusChangedEventArgs.EventId, OnFocusChanged);
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
        var args = e as InteractionFocusChangedEventArgs;
        if (args == null)
            return;

        if (args.Target == null)
        {
            CloseInteractTips();
            CloseBuildTips();
            _tipsFormId = -1;
            _buildTipsFormId = -1;
            Debug.Log("[Interact Tips] Close tips (no focus)");
            return;
        }

        if (ShouldShowBuildTips(args.Target))
        {
            CloseInteractTips();
            OpenOrUpdateBuildTips(args.Target);
            return;
        }

        CloseBuildTips();

        // 已有实例则复用（避免 close/open 时序导致的立即关闭、以及避免 Presenter 因 UI 关闭而退订）
        if (_tipsFormId > 0 && GF.UI.HasUIForm(_tipsFormId))
        {
            var uiForm = GF.UI.GetUIForm(_tipsFormId) as UIForm;
            var logic = uiForm != null ? uiForm.Logic as InteractOptionTips : null;
            if (logic != null)
            {
                Debug.Log($"[Interact Tips] Update tips target={args.Target.Transform.name}");
                logic.ApplyTarget(args.Target);
                return;
            }

            // 实例存在但逻辑不对/未就绪，兜底重开
            GF.UI.Close(_tipsFormId);
            _tipsFormId = -1;
        }

        // 打开新实例
        var uiParams = UIParams.Create();

        Debug.Log($"[Interact Tips] Open tips for target={args.Target.Transform.name}");
        uiParams.Set(InteractOptionTips.P_TargetHost, args.Target);
        _tipsFormId = GF.UI.OpenUIForm(UIViews.InteractOptionTips, uiParams);
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
}
