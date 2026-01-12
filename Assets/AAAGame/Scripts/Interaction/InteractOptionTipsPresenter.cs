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
            if (_tipsFormId > 0 && GF.UI.HasUIForm(_tipsFormId))
            {
                GF.UI.Close(_tipsFormId);
            }
            else
            {
                // 兜底：关闭全部已打开的提示（防止重复实例）
                GF.UI.CloseUIForms(UIViews.InteractOptionTips);
            }
            _tipsFormId = -1;
            Debug.Log("[Interact Tips] Close tips (no focus)");
            return;
        }

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
}
