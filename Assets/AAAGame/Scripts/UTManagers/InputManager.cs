using System;
using System.Collections;
using System.Collections.Generic;
using BaseUtility;
using UnityEngine;
using UnityGameFramework.Runtime;
using UnityEngine.InputSystem;

public class InputManager : GameFrameworkComponent
{
    public InputState CurState => selfStateMachine.curState;
    private InputModel _model;
    private InputSM selfStateMachine;
    public PlayerInput playerInput;
    
    private InputAction _moveAction;
    private InputAction _interactAction;


    protected override void Awake()
    {
        base.Awake();
        
    }

    private void Start()
    {
        selfStateMachine = new InputSM(this);
        
        
        
        var actions = playerInput.actions;

        _moveAction = actions.FindAction("Player/Move");
        _interactAction = actions.FindAction("Player/Interact");
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
        if(_model == null && GF.DataModel!=null)
            _model = GF.DataModel.GetOrCreate<InputModel>();
    }

    private class InputSM : AbsStatemachine<InputState,InputManager>
    {
        
        public InputSM(InputManager inputManager) : base(inputManager)
        {}

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

        public override void SwitchWhenUpdate(InputState curState)
        {
            base.SwitchWhenUpdate(curState);
            switch (curState)
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

                    // Jump 按下
                    father._model.InteractionPressed = father._interactAction.WasPressedThisFrame();
                   
                    break;
            }
        }
    }
}

public enum InputState
{
    None,
    StartScreen,
    Game
}