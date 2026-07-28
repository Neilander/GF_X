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
    private InputAction _openTechTreeAction;
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
    private InputAction _build1Action;
    private InputAction _build2Action;
    private InputAction _build3Action;
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
        _openTechTreeAction = actions.FindAction("Player/OpenTechTree");
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
        _build1Action = actions.FindAction("Player/Build1");
        _build2Action = actions.FindAction("Player/Build2");
        _build3Action = actions.FindAction("Player/Build3");

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
        _inputTimestampCalibrated = false;
        LogicTimeControlService.Changed -= OnLogicTimeControlChanged;
        UnbindGameplayCallbacks();
        CleanupUIFormControl();
    }

    private void Update()
    {
        selfStateMachine.UpdateState();
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
        Vector2 initialScreenPosition = GetPointerScreenPosition();
        bool hasInitialWorldPosition = TryCaptureSelectWorldPosition(initialScreenPosition, out FixVector2 initialWorldPosition);

        _model.LogicTimeline.Begin(
            realtime,
            initialMove,
            initialHeldBits,
            QuantizeScreenPosition(initialScreenPosition),
            hasInitialWorldPosition,
            initialWorldPosition);
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

    public void RequestSelectPosition(Vector2 screenPosition)
    {
        if (!CanCaptureGameplayInput())
            return;

        EnqueueSelectPositionSample(Time.realtimeSinceStartupAsDouble, screenPosition);
    }

    private void BindGameplayCallbacks()
    {
        if (_gameplayCallbacksBound)
            throw new InvalidOperationException("InputManager.BindGameplayCallbacks failed: callbacks are already bound.");

        BindAction(_moveAction);
        BindAction(_interactAction);
        BindAction(_interact2Action);
        BindAction(_interact3Action);
        BindAction(_openTechTreeAction);
        BindAction(_attackAction);
        BindAction(_skill1Action);
        BindAction(_skill2Action);
        BindAction(_skill3Action);
        BindAction(_skill4Action);
        BindAction(_skill5Action);
        BindAction(_selectPositionAction);
        BindAction(_skillConfirmAction);
        BindAction(_build1Action);
        BindAction(_build2Action);
        BindAction(_build3Action);
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
        UnbindAction(_openTechTreeAction);
        UnbindAction(_attackAction);
        UnbindAction(_skill1Action);
        UnbindAction(_skill2Action);
        UnbindAction(_skill3Action);
        UnbindAction(_skill4Action);
        UnbindAction(_skill5Action);
        UnbindAction(_selectPositionAction);
        UnbindAction(_skillConfirmAction);
        UnbindAction(_build1Action);
        UnbindAction(_build2Action);
        UnbindAction(_build3Action);
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
            EnqueueSelectPositionSample(timestamp, context.ReadValue<Vector2>());
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

    private void EnqueueSelectPositionSample(double timestamp, Vector2 screenPosition)
    {
        _model.LogicTimeline.EnqueueSelectScreenPosition(timestamp, QuantizeScreenPosition(screenPosition));
        if (TryCaptureSelectWorldPosition(screenPosition, out FixVector2 worldPosition))
            _model.LogicTimeline.EnqueueSelectWorldPosition(timestamp, worldPosition);
    }

    private static FixVector2 QuantizeScreenPosition(Vector2 screenPosition)
    {
        return new FixVector2((Fix64)screenPosition.x, (Fix64)screenPosition.y);
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
        AddHeldBit(_openTechTreeAction, LogicInputButton.OpenTechTree, ref heldBits);
        AddHeldBit(_attackAction, LogicInputButton.PlayerAttack, ref heldBits);
        AddHeldBit(_skill1Action, LogicInputButton.Skill1, ref heldBits);
        AddHeldBit(_skill2Action, LogicInputButton.Skill2, ref heldBits);
        AddHeldBit(_skill3Action, LogicInputButton.Skill3, ref heldBits);
        AddHeldBit(_skill4Action, LogicInputButton.Skill4, ref heldBits);
        AddHeldBit(_skill5Action, LogicInputButton.Skill5, ref heldBits);
        AddHeldBit(_skillConfirmAction, LogicInputButton.SkillConfirm, ref heldBits);
        AddHeldBit(_build1Action, LogicInputButton.Build1, ref heldBits);
        AddHeldBit(_build2Action, LogicInputButton.Build2, ref heldBits);
        AddHeldBit(_build3Action, LogicInputButton.Build3, ref heldBits);
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
        else if (action == _openTechTreeAction) button = LogicInputButton.OpenTechTree;
        else if (action == _attackAction) button = LogicInputButton.PlayerAttack;
        else if (action == _skill1Action) button = LogicInputButton.Skill1;
        else if (action == _skill2Action) button = LogicInputButton.Skill2;
        else if (action == _skill3Action) button = LogicInputButton.Skill3;
        else if (action == _skill4Action) button = LogicInputButton.Skill4;
        else if (action == _skill5Action) button = LogicInputButton.Skill5;
        else if (action == _skillConfirmAction) button = LogicInputButton.SkillConfirm;
        else if (action == _build1Action) button = LogicInputButton.Build1;
        else if (action == _build2Action) button = LogicInputButton.Build2;
        else if (action == _build3Action) button = LogicInputButton.Build3;
        else
        {
            button = default;
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
