using System;
using BaseUtility;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityGameFramework.Runtime;

[DefaultExecutionOrder(-1000)]
public partial class InputManager : GameFrameworkComponent
{
    private const double InputTimestampFutureToleranceSeconds = 1d;

    public InputState CurState => selfStateMachine.curState;
    public LogicInputFrame CurrentLogicInputFrame => _model?.CurrentLogicFrame ?? LogicInputFrame.Empty;

    private InputModel _model;
    private InputSM selfStateMachine;
    public PlayerInput playerInput;
    private InputAction _moveAction;
    private InputAction _interactAction;
    private InputAction _interact2Action;
    private InputAction _interact3Action;
    private InputAction _attackAction;
    private InputAction _skill1Action;
    private InputAction _skill2Action;
    private InputAction _skill3Action;
    private InputAction _skill4Action;
    private InputAction _skill5Action;
    private InputAction _playerCancelAction;
    private InputAction _uiCancelAction;
    private InputAction _selectPositionAction;
    private InputAction _skillConfirmAction;
    private bool _gameplayCallbacksBound;
    private bool _inputTimestampCalibrated;
    private double _inputTimeToLogicRealtimeOffset;
    private bool _wasLogicPaused;

    protected override void Awake()
    {
        base.Awake();
    }

    private void Start()
    {
        selfStateMachine = new InputSM(this);
        InitializeUIFormControl();

        if (playerInput == null || playerInput.actions == null)
            throw new InvalidOperationException("InputManager.Start failed: PlayerInput and its actions are required.");

        var actions = playerInput.actions;
        _moveAction = actions.FindAction("Player/Move");
        _interactAction = actions.FindAction("Player/Interact");
        _interact2Action = actions.FindAction("Player/Interact2");
        _interact3Action = actions.FindAction("Player/Interact3");
        _attackAction = actions.FindAction("Player/Attack");
        _skill1Action = actions.FindAction("Player/Skill1");
        _skill2Action = actions.FindAction("Player/Skill2");
        _skill3Action = actions.FindAction("Player/Skill3");
        _skill4Action = actions.FindAction("Player/Skill4");
        _skill5Action = actions.FindAction("Player/Skill5");
        _playerCancelAction = actions.FindAction("Player/Cancel");
        _uiCancelAction = actions.FindAction("UI/Cancel");
        _selectPositionAction = actions.FindAction("Player/SelectPosition");
        _skillConfirmAction = actions.FindAction("Player/SkillConfirm");

        if (_moveAction == null)
            throw new InvalidOperationException("InputManager.Start failed: Player/Move action is required.");
        if (_selectPositionAction == null)
            throw new InvalidOperationException("InputManager.Start failed: Player/SelectPosition action is required.");

        if (_playerCancelAction != null && !_playerCancelAction.enabled)
            _playerCancelAction.Enable();

        BindGameplayCallbacks();
        LogicTimeControlService.Changed += OnLogicTimeControlChanged;
        _wasLogicPaused = LogicTimeControlService.IsActive && LogicTimeControlService.IsPaused;
    }

    private void OnDestroy()
    {
        SkillCastPresentationService.Cancel();
        _inputTimestampCalibrated = false;
        LogicTimeControlService.Changed -= OnLogicTimeControlChanged;
        UnbindGameplayCallbacks();
        CleanupUIFormControl();
    }

    private void Update()
    {
        selfStateMachine.UpdateState();
        if (SkillCastPresentationService.IsAiming && WasCancelPressedThisFrame())
        {
            SkillCastPresentationService.Cancel();
            return;
        }
        SkillCastPresentationService.UpdateActiveAim(this);
    }

    public void ChangeState(InputState newState)
    {
        if (newState == InputState.Game && _model == null)
            FindModel();

        if (newState == InputState.UIForm)
            HideUIFormModeCoveredTips();
        else if (newState == InputState.Game)
            RestoreUIFormModeCoveredTips();

        selfStateMachine.StartState(newState);
    }

    public void FindModel()
    {
        if (GF.DataModel != null)
            _model = GF.DataModel.GetDataModel<InputModel>();
    }

    public void BeginLogicInputTimeline(double realtime)
    {
        EnsureInputModel();

        double inputTime = UnityEngine.InputSystem.LowLevel.InputState.currentTime;
        if (double.IsNaN(inputTime) || double.IsInfinity(inputTime) || inputTime < 0d)
            throw new InvalidOperationException($"InputManager cannot calibrate an invalid Input System time. value={inputTime:R}.");
        if (double.IsNaN(realtime) || double.IsInfinity(realtime) || realtime < 0d)
            throw new ArgumentOutOfRangeException(nameof(realtime), realtime, "Logic realtime must be finite and non-negative.");

        _inputTimeToLogicRealtimeOffset = realtime - inputTime;
        _inputTimestampCalibrated = true;
        _wasLogicPaused = LogicTimeControlService.IsActive && LogicTimeControlService.IsPaused;
        Log.Info(
            "[LogicInput] Timestamp calibrated. inputTime={0:R}, logicRealtime={1:R}, offset={2:R}.",
            inputTime,
            realtime,
            _inputTimeToLogicRealtimeOffset);

        FixVector2 initialMove = CurState == InputState.Game
            ? CaptureWorldMove(_moveAction.ReadValue<Vector2>())
            : FixVector2.Zero;
        ulong initialHeldBits = CurState == InputState.Game ? CaptureHeldBits() : 0;
        _model.LogicTimeline.Begin(
            realtime,
            initialMove,
            initialHeldBits);
        _model.ClearCompatibilityState();
    }

    public LogicInputFrame SealLogicInputFrame(ulong frameId, double cutoffRealtime)
    {
        EnsureInputModel();
        LogicInputFrame frame = _model.LogicTimeline.SealReusable(frameId, cutoffRealtime);
        _model.ApplyLogicInputFrame(frame);
        return frame;
    }

    public bool IsActionPressed(string actionName)
    {
        if (playerInput == null || playerInput.actions == null || string.IsNullOrWhiteSpace(actionName))
            return false;

        InputAction action = playerInput.actions.FindAction(actionName);
        return action != null && action.IsPressed();
    }

    public bool WasActionPressedThisFrame(string actionName)
    {
        if (playerInput == null || playerInput.actions == null || string.IsNullOrWhiteSpace(actionName))
            return false;

        InputAction action = playerInput.actions.FindAction(actionName);
        return action != null && action.WasPressedThisFrame();
    }

    public bool WasCancelPressedThisFrame()
    {
        EnsureCancelActions();

        if (_playerCancelAction != null && _playerCancelAction.WasPressedThisFrame())
            return true;
        if (_uiCancelAction != null && _uiCancelAction.WasPressedThisFrame())
            return true;

        return WasActionPressedThisFrame("Player/Cancel") || WasActionPressedThisFrame("UI/Cancel");
    }

    private void EnsureCancelActions()
    {
        if (playerInput == null || playerInput.actions == null)
            return;

        _playerCancelAction ??= playerInput.actions.FindAction("Player/Cancel");
        _uiCancelAction ??= playerInput.actions.FindAction("UI/Cancel");

        if (_playerCancelAction != null && !_playerCancelAction.enabled)
            _playerCancelAction.Enable();
    }

    public bool IsPrimaryPointerPressed()
    {
        return _attackAction != null && _attackAction.IsPressed();
    }

    public Vector2 GetPointerScreenPosition()
    {
        if (_selectPositionAction == null)
            throw new InvalidOperationException("InputManager cannot read pointer position without Player/SelectPosition.");
        return _selectPositionAction.ReadValue<Vector2>();
    }

    public bool TryGetSelectionWorldPosition(Vector2 screenPosition, out FixVector2 worldPosition)
    {
        return TryCaptureSelectWorldPosition(screenPosition, out worldPosition);
    }

    private void BindGameplayCallbacks()
    {
        if (_gameplayCallbacksBound)
            throw new InvalidOperationException("InputManager.BindGameplayCallbacks failed: callbacks are already bound.");

        BindAction(_moveAction);
        BindAction(_interactAction);
        BindAction(_interact2Action);
        BindAction(_interact3Action);
        BindAction(_attackAction);
        BindAction(_skill1Action);
        BindAction(_skill2Action);
        BindAction(_skill3Action);
        BindAction(_skill4Action);
        BindAction(_skill5Action);
        BindAction(_selectPositionAction);
        BindAction(_skillConfirmAction);
        _gameplayCallbacksBound = true;
    }

    private void UnbindGameplayCallbacks()
    {
        if (!_gameplayCallbacksBound)
            return;

        UnbindAction(_moveAction);
        UnbindAction(_interactAction);
        UnbindAction(_interact2Action);
        UnbindAction(_interact3Action);
        UnbindAction(_attackAction);
        UnbindAction(_skill1Action);
        UnbindAction(_skill2Action);
        UnbindAction(_skill3Action);
        UnbindAction(_skill4Action);
        UnbindAction(_skill5Action);
        UnbindAction(_selectPositionAction);
        UnbindAction(_skillConfirmAction);
        _gameplayCallbacksBound = false;
    }

    private void BindAction(InputAction action)
    {
        if (action == null)
            return;

        if (action == _moveAction || action == _selectPositionAction)
            action.performed += OnGameplayActionPerformed;
        else
            action.started += OnGameplayButtonStarted;
        action.canceled += OnGameplayActionCanceled;
    }

    private void UnbindAction(InputAction action)
    {
        if (action == null)
            return;

        if (action == _moveAction || action == _selectPositionAction)
            action.performed -= OnGameplayActionPerformed;
        else
            action.started -= OnGameplayButtonStarted;
        action.canceled -= OnGameplayActionCanceled;
    }

    private void OnGameplayButtonStarted(InputAction.CallbackContext context)
    {
        if (!CanCaptureGameplayInput())
            return;

        if (TryResolveSkillSlot(context.action, out int slotIndex))
        {
            Vector2 screenPosition = GetPointerScreenPosition();
            if (!SkillCastPresentationService.TryBeginAim(slotIndex, this, screenPosition))
                SkillCastPresentationService.RequestCastAtScreen(slotIndex, this, screenPosition);
            return;
        }
        if (context.action == _skillConfirmAction)
        {
            SkillCastPresentationService.CommitAim();
            return;
        }

        if (TryResolveButton(context.action, out LogicInputButton button))
            _model.LogicTimeline.EnqueueButtonPressed(GetInputEventRealtime(context), button);
    }

    private void OnGameplayActionPerformed(InputAction.CallbackContext context)
    {
        if (!CanCaptureGameplayInput())
            return;

        double timestamp = GetInputEventRealtime(context);
        if (context.action == _moveAction)
        {
            _model.LogicTimeline.EnqueueWorldMove(
                timestamp,
                CaptureWorldMove(context.ReadValue<Vector2>()));
            return;
        }

        if (context.action == _selectPositionAction)
        {
            SkillCastPresentationService.UpdateAim(this, context.ReadValue<Vector2>());
            return;
        }

    }

    private void OnGameplayActionCanceled(InputAction.CallbackContext context)
    {
        if (!CanCaptureGameplayInput())
            return;

        double timestamp = GetInputEventRealtime(context);
        if (context.action == _moveAction)
        {
            _model.LogicTimeline.EnqueueWorldMove(
                timestamp,
                CaptureWorldMove(context.ReadValue<Vector2>()));
            return;
        }

        if (TryResolveSkillSlot(context.action, out _) || context.action == _skillConfirmAction)
            return;

        if (TryResolveButton(context.action, out LogicInputButton button))
            _model.LogicTimeline.EnqueueButtonReleased(timestamp, button);
    }

    private bool CanCaptureGameplayInput()
    {
        return CurState == InputState.Game
               && _model != null
               && _model.LogicTimeline.IsStarted
               && _inputTimestampCalibrated
               && !(LogicTimeControlService.IsActive && LogicTimeControlService.IsPaused);
    }

    private double GetInputEventRealtime(InputAction.CallbackContext context)
    {
        if (!_inputTimestampCalibrated)
            throw new InvalidOperationException("Input timestamp conversion is not calibrated.");

        // Input System documents context.time and InputState.currentTime in its
        // realtime-since-startup domain. We still calibrate their offset against the
        // exact clock start passed by RuntimeProcedureBase instead of assuming zero.
        double inputEventTime = context.time;
        double realtime = Time.realtimeSinceStartupAsDouble;
        if (double.IsNaN(inputEventTime) || double.IsInfinity(inputEventTime) || inputEventTime < 0d)
            throw new InvalidOperationException($"Input action produced an invalid timestamp. value={inputEventTime:R}.");

        double logicRealtime = inputEventTime + _inputTimeToLogicRealtimeOffset;
        if (logicRealtime < 0d || logicRealtime > realtime + InputTimestampFutureToleranceSeconds)
            throw new InvalidOperationException(
                $"Converted input timestamp is outside the logic realtime domain. input={inputEventTime:R}, " +
                $"converted={logicRealtime:R}, realtime={realtime:R}, offset={_inputTimeToLogicRealtimeOffset:R}.");
        return logicRealtime;
    }

    private FixVector2 CaptureWorldMove(Vector2 deviceMove)
    {
        return InputDirTranslator.TranslateAndQuantize(deviceMove, Camera.main);
    }

    private static bool TryCaptureSelectWorldPosition(Vector2 screenPosition, out FixVector2 worldPosition)
    {
        Camera camera = Camera.main;
        if (camera == null)
            throw new InvalidOperationException("InputManager cannot project a selection position without Camera.main.");

        int groundMask = LayerMask.GetMask("Ground");
        if (groundMask == 0)
            throw new InvalidOperationException("InputManager cannot project a selection position because the Ground layer is missing.");

        Ray ray = camera.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, camera.farClipPlane, groundMask, QueryTriggerInteraction.Ignore))
        {
            // A ray miss is a valid sample: the timeline retains its last valid world point.
            worldPosition = FixVector2.Zero;
            return false;
        }

        worldPosition = new FixVector2((Fix64)hit.point.x, (Fix64)hit.point.z);
        return true;
    }

    private ulong CaptureHeldBits()
    {
        ulong heldBits = 0;
        AddHeldBit(_interactAction, LogicInputButton.InteractionPrimary, ref heldBits);
        AddHeldBit(_interact2Action, LogicInputButton.InteractionSecondary, ref heldBits);
        AddHeldBit(_interact3Action, LogicInputButton.InteractionTertiary, ref heldBits);
        AddHeldBit(_attackAction, LogicInputButton.PlayerAttack, ref heldBits);
        return heldBits;
    }

    private static void AddHeldBit(InputAction action, LogicInputButton button, ref ulong heldBits)
    {
        if (action != null && action.IsPressed())
            heldBits |= LogicInputTimeline.GetButtonBit(button);
    }

    private bool TryResolveButton(InputAction action, out LogicInputButton button)
    {
        if (action == _interactAction) button = LogicInputButton.InteractionPrimary;
        else if (action == _interact2Action) button = LogicInputButton.InteractionSecondary;
        else if (action == _interact3Action) button = LogicInputButton.InteractionTertiary;
        else if (action == _attackAction) button = LogicInputButton.PlayerAttack;
        else
        {
            button = default;
            return false;
        }

        return true;
    }

    private bool TryResolveSkillSlot(InputAction action, out int slotIndex)
    {
        if (action == _skill1Action) slotIndex = 0;
        else if (action == _skill2Action) slotIndex = 1;
        else if (action == _skill3Action) slotIndex = 2;
        else if (action == _skill4Action) slotIndex = 3;
        else if (action == _skill5Action) slotIndex = 4;
        else
        {
            slotIndex = -1;
            return false;
        }
        return true;
    }

    private void EnsureInputModel()
    {
        if (_model == null)
            FindModel();
        if (_model == null)
            throw new InvalidOperationException("InputManager requires an active InputModel.");
    }

    private void SynchronizeGameplayInput(double timestamp)
    {
        if (_model == null || !_model.LogicTimeline.IsStarted)
            return;

        _model.LogicTimeline.EnqueueResetGameplayState(timestamp);
        _model.LogicTimeline.EnqueueWorldMove(timestamp, CaptureWorldMove(_moveAction.ReadValue<Vector2>()));

        ulong heldBits = CaptureHeldBits();
        for (int i = 0; i < LogicInputTimeline.ButtonCount; i++)
        {
            var button = (LogicInputButton)i;
            if ((heldBits & LogicInputTimeline.GetButtonBit(button)) != 0)
                _model.LogicTimeline.EnqueueButtonHeldState(timestamp, button, true);
        }
    }

    private void OnLogicTimeControlChanged()
    {
        bool isPaused = LogicTimeControlService.IsActive && LogicTimeControlService.IsPaused;
        if (isPaused == _wasLogicPaused)
            return;

        _wasLogicPaused = isPaused;
        if (isPaused)
            SkillCastPresentationService.Cancel();
        if (_model == null || !_model.LogicTimeline.IsStarted)
            return;

        double timestamp = Time.realtimeSinceStartupAsDouble;
        if (isPaused)
            _model.LogicTimeline.EnqueueResetGameplayState(timestamp);
        else if (CurState == InputState.Game)
            SynchronizeGameplayInput(timestamp);
    }

    private class InputSM : AbsStatemachine<InputState, InputManager>
    {
        public InputSM(InputManager inputManager) : base(inputManager)
        {
        }

        public override void SwitchWhenStart(InputState newState)
        {
            base.SwitchWhenStart(newState);
            if (newState == InputState.Game)
            {
                father._model?.ClearCompatibilityState();
                father.SynchronizeGameplayInput(Time.realtimeSinceStartupAsDouble);
            }
        }

        public override void SwitchWhenEnd(InputState lastState)
        {
            base.SwitchWhenEnd(lastState);
            if (lastState == InputState.Game)
            {
                SkillCastPresentationService.Cancel();
                father._model?.ClearCompatibilityState();
                if (father._model != null && father._model.LogicTimeline.IsStarted)
                {
                    father._model.LogicTimeline.EnqueueResetGameplayState(
                        Time.realtimeSinceStartupAsDouble);
                }
            }
        }

        public override void SwitchWhenUpdate(InputState currentState)
        {
            base.SwitchWhenUpdate(currentState);
        }
    }
}

public enum InputState
{
    None,
    StartScreen,
    Game,
    UIForm
}
