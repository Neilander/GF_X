using System;
using System.Collections;
using System.Collections.Generic;
using BaseUtility;
using UnityEngine;
using UnityGameFramework.Runtime;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-1000)]
public partial class InputManager : GameFrameworkComponent
{
    public InputState CurState => selfStateMachine.curState;
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
    private InputAction _playerCancelAction;
    private InputAction _uiCancelAction;

    private InputAction _selectPositionAction;
    private InputAction _skillConfirmAction;


    protected override void Awake()
    {
        base.Awake();

    }

    private void Start()
    {
        selfStateMachine = new InputSM(this);

        InitializeUIFormControl();

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
        _playerCancelAction = actions.FindAction("Player/Cancel");
        _uiCancelAction = actions.FindAction("UI/Cancel");
        if (_playerCancelAction != null && !_playerCancelAction.enabled)
        {
            _playerCancelAction.Enable();
        }

        _selectPositionAction = actions.FindAction("Player/SelectPosition");
        _skillConfirmAction = actions.FindAction("Player/SkillConfirm");
    }

    private void OnDestroy()
    {
        CleanupUIFormControl();
    }

    private void Update()
    {
        selfStateMachine.UpdateState();
    }

    public void ChangeState(InputState newState)
    {
        if (newState == InputState.Game && _model == null)
        {
            FindModel();
        }

        if (newState == InputState.UIForm)
        {
            HideUIFormModeCoveredTips();
        }
        else if (newState == InputState.Game)
        {
            RestoreUIFormModeCoveredTips();
        }

        selfStateMachine.StartState(newState);
    }

    public void FindModel()
    {
        if (GF.DataModel != null)
        {
            _model = GF.DataModel.GetDataModel<InputModel>();
        }
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
        {
            return true;
        }

        if (_uiCancelAction != null && _uiCancelAction.WasPressedThisFrame())
        {
            return true;
        }

        return WasActionPressedThisFrame("Player/Cancel") || WasActionPressedThisFrame("UI/Cancel");
    }

    private void EnsureCancelActions()
    {
        if (playerInput == null || playerInput.actions == null)
        {
            return;
        }

        _playerCancelAction ??= playerInput.actions.FindAction("Player/Cancel");
        _uiCancelAction ??= playerInput.actions.FindAction("UI/Cancel");

        if (_playerCancelAction != null && !_playerCancelAction.enabled)
        {
            _playerCancelAction.Enable();
        }
    }

    public bool IsPrimaryPointerPressed()
    {
        return _attackAction != null && _attackAction.IsPressed();
    }

    public Vector2 GetPointerScreenPosition()
    {
        return _selectPositionAction != null ? _selectPositionAction.ReadValue<Vector2>() : Vector2.zero;
    }

    private class InputSM : AbsStatemachine<InputState, InputManager>
    {

        public InputSM(InputManager inputManager) : base(inputManager)
        { }

        public override void SwitchWhenStart(InputState newState)
        {
            base.SwitchWhenStart(newState);
            switch (newState)
            {
                case InputState.Game:
                    father._model?.Reset();
                    break;
            }
        }

        public override void SwitchWhenEnd(InputState lastState)
        {
            base.SwitchWhenEnd(lastState);
            switch (lastState)
            {
                case InputState.Game:
                    father._model?.Reset();
                    break;
            }
        }

        public override void SwitchWhenUpdate(InputState currentState)
        {
            base.SwitchWhenUpdate(currentState);
            switch (currentState)
            {
                case InputState.Game:
                    if (father._model == null)
                    {
                        return;
                    }

                    Vector2 move = father._moveAction.ReadValue<Vector2>();

                    // 写入你的 Model
                    father._model.MoveX = (Fix64)move.x;
                    father._model.MoveY = (Fix64)move.y;

                    // Interact 按下
                    father._model.InteractionPressed = father._interactAction != null && father._interactAction.WasPressedThisFrame();
                    father._model.Interaction2Pressed = father._interact2Action != null && father._interact2Action.WasPressedThisFrame();
                    father._model.Interaction3Pressed = father._interact3Action != null && father._interact3Action.WasPressedThisFrame();
                    father._model.OpenTechTreePressed = father._openTechTreeAction != null && father._openTechTreeAction.WasPressedThisFrame();

                    //攻击和技能触发
                    father._model.PlayerAttack = father._attackAction.WasPressedThisFrame();
                    father._model.Skill1Pressed = father._skill1Action.WasPressedThisFrame();
                    father._model.Skill2Pressed = father._skill2Action.WasPressedThisFrame();
                    father._model.Skill3Pressed = father._skill3Action.WasPressedThisFrame();

                    //技能期间交互
                    father._model.SelectScreenPosition = father._selectPositionAction.ReadValue<Vector2>();
                    father._model.SkillConfirmPressed = father._skillConfirmAction.WasPressedThisFrame();
                    break;
            }
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
