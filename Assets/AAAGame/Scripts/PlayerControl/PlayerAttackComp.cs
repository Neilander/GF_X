using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackComp : IAtkComp
{
    private InputModel _inputModel;
    private MAEntity _playerEntity;
    
    public BasicAction[] actions;

    private ActionInfo _actionInfo;
    
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
                _playerEntity.animator.SetTrigger("Attack");
                actions[0].StartAction(_playerEntity, out _actionInfo);
                _actionInfo.damageInfo = new Damage(_playerEntity, 1);
            }
        }
        else
        {
            actions[0].Tick(_actionInfo, Time.deltaTime);
            if (_actionInfo.isFinished)
            {
                _actionInfo = null;
            }
        }


    }

    public void Init(MAEntity entity)
    {
        _playerEntity = entity;
        _actionInfo = null;
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