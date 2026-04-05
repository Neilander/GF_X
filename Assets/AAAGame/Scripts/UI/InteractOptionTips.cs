using UnityGameFramework.Runtime;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using GameFramework.Event;
using UnityEngine;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class InteractOptionTips : UIFormBase
{
    public const string P_TargetHost = "TargetHost";

    [SerializeField] private Vector2 uiOffset = new Vector2(0, 80);

    private InteractionHost _target;

    public void ApplyTarget(InteractionHost target)
    {
        _target = target;
        if (_target != null)
        {
            AttachFollower(_target.GetPromptPosition());
        }
        RefreshList();
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        _target = Params.Get(P_TargetHost) as InteractionHost;
        if (_target != null)
        {
            AttachFollower(_target.GetPromptPosition());
        }

        GF.Event.Subscribe(InteractionOptionTriggeredEventArgs.EventId, OnOptionTriggered);
        GF.Event.Subscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);

        RefreshList();
    }
    protected override void OnClose(bool isShutdown, object userData)
    {
        GF.Event.Unsubscribe(InteractionOptionTriggeredEventArgs.EventId, OnOptionTriggered);
        GF.Event.Unsubscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);
        base.OnClose(isShutdown, userData);
    }

    private void RefreshList()
    {
        // 先回收旧的 item，避免刷新时越刷越多
        UnspawnAllItem<UIItemObject>(varItemUnit);
        UnspawnAllItem<UIItemObject>(varInteractOptionItem);

        if (_target == null)
            return;

        SortedDictionary<InputKey, IInteractionOption> options = new();
        _target.GetOptionsWithKeys(options);

        foreach (var kv in options)
        {
            var interactOptionUnit = SpawnItem<UIItemObject>(varInteractOptionItem, varInteractOptionTipsPanel).itemLogic as InteractOptionUnit;

            string keyText = InputGetKeyText.GetKeyText(kv.Key);
            bool enabled = kv.Value != null && kv.Value.IsExecutable();
            interactOptionUnit.SetData(kv.Value.DisplayName, keyText, enabled);

            if (kv.Value.CostMaterial != null)
            {
                foreach (var quantityItem in kv.Value.CostMaterial)
                {
                    var itemUnit = SpawnItem<UIItemObject>(varItemUnit, interactOptionUnit.varOptionCost).itemLogic as ItemUnit;
                    itemUnit.SetData(quantityItem.str, quantityItem.num, true);
                }
            }
        }

    }

    private void OnOptionTriggered(object sender, GameEventArgs e)
    {
        var args = e as InteractionOptionTriggeredEventArgs;
        if (args == null || args.Target == null)
            return;

        if (_target == null || args.Target.Transform != _target.Transform)
            return;

        // 触发交互后刷新列表（每次刷新都会从 target 拉取最新 options）
        RefreshList();
    }
    private void OnItemAmountChanged(object sender, GameEventArgs e = null)
    {
        RefreshList();
    }

    private void AttachFollower(Vector3 worldPoint)
    {
        RectTransform rect = varInteractOptionTipsPanel != null ? varInteractOptionTipsPanel : transform as RectTransform;
        if (rect == null)
            return;

        var follower = gameObject.GetComponent<UIFollowWorldPoint>();
        if (follower == null)
            follower = gameObject.AddComponent<UIFollowWorldPoint>();

        follower.Init(rect, worldPoint, uiOffset);
    }
}