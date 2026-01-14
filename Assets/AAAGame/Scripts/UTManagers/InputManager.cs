using System;
using System.Collections;
using System.Collections.Generic;
using BaseUtility;
using UnityEngine;
using UnityGameFramework.Runtime;
using UnityEngine.InputSystem;

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
        selfStateMachine.StartState(newState);
    }

    public void FindModel()
    {
        if (_model == null && GF.DataModel != null)
            _model = GF.DataModel.GetDataModel<InputModel>();
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
                        father.FindModel();
                        break;
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

                    father._model.PlayerAttack = father._attackAction.WasPressedThisFrame();

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