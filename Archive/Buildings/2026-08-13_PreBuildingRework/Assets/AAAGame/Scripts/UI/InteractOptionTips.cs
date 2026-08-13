using UnityGameFramework.Runtime;
using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class InteractOptionTips : UIFormBase
{
    public const string P_TargetHost = "TargetHost";

    [SerializeField] private Vector2 uiOffset = new Vector2(0, 80);

    private InteractionHost _target;
    private InputManager _inputManager;
    private readonly List<OptionUnitBinding> _optionBindings = new();

    private sealed class OptionUnitBinding
    {
        public IInteractionOption Option;
        public InteractOptionUnit Unit;
        public InputKey? Key;
    }

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
        _inputManager = GameEntry.GetComponent<InputManager>()
            ?? throw new System.InvalidOperationException("InteractOptionTips requires InputManager.");
        if (_target != null)
        {
            AttachFollower(_target.GetPromptPosition());
        }

        GF.Event.Subscribe(InteractionOptionTriggeredEventArgs.EventId, OnOptionTriggered);
        // GF.Event.Subscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnResourceAmountChanged);
        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnResourceAmountChanged);
        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        RefreshList();
    }
    protected override void OnClose(bool isShutdown, object userData)
    {
        _optionBindings.Clear();
        _inputManager = null;
        GF.Event.Unsubscribe(InteractionOptionTriggeredEventArgs.EventId, OnOptionTriggered);
        // GF.Event.Unsubscribe(ItemAmountChangedEventArgs.EventId, OnItemAmountChanged);
        GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnResourceAmountChanged);
        GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnResourceAmountChanged);
        GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        base.OnClose(isShutdown, userData);
    }

    private void Update()
    {
        UpdateKeyHoldProgress();
    }

    private void RefreshList()
    {
        // 先回收旧的 item，避免刷新时越刷越多
        UnspawnAllItem<UIItemObject>(varResourceUnit);
        UnspawnAllItem<UIItemObject>(varInteractOptionItem);
        _optionBindings.Clear();

        if (_target == null)
            return;

        SortedDictionary<InputKey, IInteractionOption> options = new();
        _target.GetOptionsWithKeys(options);

        foreach (var kv in options)
        {
            string keyText = InputGetKeyText.GetKeyText(kv.Key);
            AddOptionItem(kv.Value, keyText, kv.Key);
        }

        HashSet<IInteractionOption> keyedOptions = new(options.Values);
        List<IInteractionOption> allOptions = new();
        _target.GetOptions(allOptions);
        foreach (var option in allOptions)
        {
            if (keyedOptions.Contains(option))
                continue;

            AddOptionItem(option, string.Empty, null);
        }

    }

    private void AddOptionItem(IInteractionOption option, string keyText, InputKey? key)
    {
        if (option == null)
            return;

        var interactOptionUnit = SpawnItem<UIItemObject>(varInteractOptionItem, varInteractOptionTipsPanel).itemLogic as InteractOptionUnit;
        if (interactOptionUnit == null)
            return;

        bool enabled = option.IsExecutable();
        interactOptionUnit.SetData(
            option.DisplayName,
            option.DisplayDesc,
            keyText,
            enabled,
            () =>
            {
                if (_target != null)
                    _target.TryExecute(option);
            });

        _optionBindings.Add(new OptionUnitBinding
        {
            Option = option,
            Unit = interactOptionUnit,
            Key = key,
        });

        if (option.CostResource == null)
            return;

        for (int i = 0; i < option.CostResource.Length; i++)
        {
            var cost = option.CostResource[i];
            if (cost.Value <= 0)
                continue;

            var resourceUnit = SpawnItem<UIItemObject>(varResourceUnit, interactOptionUnit.OptionCostRoot).itemLogic as ResourceUnit;
            if (resourceUnit == null)
                continue;

            resourceUnit.SetData(cost.Key, cost.Value, true);
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
    private void OnResourceAmountChanged(object sender, GameEventArgs e = null)
    {
        RefreshList();
    }

    private void OnEntityFactionChanged(object sender, GameEventArgs e)
    {
        var args = e as EntityFactionChangedEventArgs;
        if (args == null || _target == null)
            return;

        if (_target.Transform != null && _target.Transform.TryGetComponent<GeneralCreature>(out var creature) && creature.Id == args.EntityId)
        {
            RefreshList();
        }
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

    private void UpdateKeyHoldProgress()
    {
        if (_target == null || _optionBindings.Count == 0)
            return;

        for (int i = 0; i < _optionBindings.Count; i++)
        {
            var binding = _optionBindings[i];
            if (binding == null || binding.Option == null || binding.Unit == null)
                continue;

            bool allowHold = binding.Option.IsVisible() && binding.Option.IsExecutable();
            if (binding.Key.HasValue)
            {
                if (_inputManager == null)
                    throw new System.InvalidOperationException("InteractOptionTips lost its InputManager while open.");
                binding.Unit.SetHoldState(
                    allowHold,
                    _inputManager.IsInteractionPressed(binding.Key.Value));
            }
            else
            {
                binding.Unit.SetHoldState(allowHold, false);
            }
        }
    }

}
