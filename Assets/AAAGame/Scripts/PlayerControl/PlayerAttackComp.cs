using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackComp : IAtkComp
{
    private InputModel _inputModel;
    private MAEntity _playerEntity;
    
    public BasicAction[] actions;

    private ActionInfo _actionInfo;

    private bool ifContinueAction;

    private int currentIndex = 0;
    
    public void Attack()
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            return;
        }
        
        if (_playerEntity.CreaturePropertyManager == null)
            return;
        
        //GF.Log(_inputModel.MoveX.ToString());

        //根据_actionInfo来判断
        if (_actionInfo == null)
        {
            //当前没有执行行为，玩家点击即可执行
            if (_inputModel.PlayerAttack)
            {
                //GF.Log("攻击");
                _playerEntity.animator.SetTrigger(actions[currentIndex].relatedTriggerString);
                actions[currentIndex].StartAction(_playerEntity, out _actionInfo);
                _actionInfo.damageInfo = new Damage(_playerEntity, 1);
                _playerEntity.LockComp(_playerEntity.moveComp, this);
                ifContinueAction = false;
            }
        }
        else
        {
            if (_inputModel.PlayerAttack&& actions[currentIndex].AcceptInput(_actionInfo))
            {
                _actionInfo.bools[BasicAction.ACCEPTED_INPUT] = true;
                ifContinueAction = true;
                _playerEntity.animator.SetTrigger(actions[(currentIndex+1)%actions.Length].relatedTriggerString);
            }



            actions[currentIndex].Tick(_actionInfo, Time.deltaTime);
            if (_actionInfo.isFinished)
            {
                if (ifContinueAction)
                {
                    //GF.Log("接受了预输入，连续执行");
                    currentIndex += 1;
                    if (currentIndex >= actions.Length)
                    {
                        currentIndex = 0;
                        //GF.Log("重置攻击段数");
                    }

                    
                    actions[currentIndex].StartAction(_playerEntity, out _actionInfo);
                    _actionInfo.damageInfo = new Damage(_playerEntity, 1);
                    ifContinueAction = false;
                }
                else
                {
                    _actionInfo = null;
                    currentIndex = 0;
                    _playerEntity.ResumeComp(_playerEntity.moveComp, this);
                    //GF.Log("攻击清0");
                }
            }
        }


    }

    public void Init(MAEntity entity)
    {
        _playerEntity = entity;
        _actionInfo = null;
        ifContinueAction = false;
        currentIndex = 0;
    }
    
    public void ShutDown() { }
    public void Resume() { }
    
    /*
    public static IAtkComp CreateAtkComp(MAEntity gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        PlayerAttackComp comp = new PlayerAttackComp();
        gmo.SetAtkComp(comp);
        comp.Init(gmo);
        return comp;
    }*/
}