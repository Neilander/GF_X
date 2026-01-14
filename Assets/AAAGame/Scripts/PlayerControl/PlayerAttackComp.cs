using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackComp : IAtkComp
{
    private InputModel _inputModel;
    private MAEntity playerEntity;
    public void Attack()
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            return;
        }
        
        if (playerEntity.CreaturePropertyManager == null)
            return;
        
        //GF.Log(_inputModel.MoveX.ToString());

        if (_inputModel.PlayerAttack)
        {
            GF.Log("攻击");
            playerEntity.animator.SetTrigger("Attack");
        }
    }

    public void Init(MAEntity entity)
    {
        playerEntity = entity;
    }
    
    public void ShutDown() { }
    public void Resume() { }
    
    public static IAtkComp CreateAtkComp(MAEntity gmo)
    {
        //gmo.AddComponent<NoAtkComp>();
        PlayerAttackComp comp = new PlayerAttackComp();
        gmo.SetAtkComp(comp);
        comp.Init(gmo);
        return comp;
    }
}
