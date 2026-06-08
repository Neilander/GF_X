using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackComp : IAtkComp
{
    private InputModel _inputModel;
    private IEntityContext _ctx;

    public BasicAction[] actions;

    private ActionInfo _actionInfo;

    private bool ifContinueAction;

    private int currentIndex = 0;

    public void Attack(float deltaTime)
    {
        if (_inputModel == null)
        {
            _inputModel = GF.DataModel.GetDataModel<InputModel>();
            return;
        }

        //根据_actionInfo来判断
        if (_actionInfo == null)
        {
            //当前没有执行行为，玩家点击即可执行
            if (_inputModel.PlayerAttack)
            {
                if (_ctx is GeneralCreature gc && gc.animator != null)
                    gc.animator.SetTrigger(actions[currentIndex].relatedTriggerString);

                if (_ctx is GeneralCreature body)
                {
                    actions[currentIndex].StartAction(body, out _actionInfo);
                    _actionInfo.damageInfo = new Damage(body, Fix64.One);
                    WeaponAttackTrailEffect.Play(_ctx);
                }

                _ctx.LockComp(_ctx.MoveComp, this);
                ifContinueAction = false;
            }
        }
        else
        {
            if (_inputModel.PlayerAttack && actions[currentIndex].AcceptInput(_actionInfo))
            {
                _actionInfo.bools[BasicAction.ACCEPTED_INPUT] = true;
                ifContinueAction = true;

                if (_ctx is GeneralCreature gc && gc.animator != null)
                    gc.animator.SetTrigger(actions[(currentIndex + 1) % actions.Length].relatedTriggerString);
            }

            actions[currentIndex].Tick(_actionInfo, deltaTime);
            if (_actionInfo.isFinished)
            {
                if (ifContinueAction)
                {
                    currentIndex += 1;
                    if (currentIndex >= actions.Length)
                    {
                        currentIndex = 0;
                    }

                    if (_ctx is GeneralCreature body)
                    {
                        actions[currentIndex].StartAction(body, out _actionInfo);
                        _actionInfo.damageInfo = new Damage(body, Fix64.One);
                        WeaponAttackTrailEffect.Play(_ctx);
                    }

                    ifContinueAction = false;
                }
                else
                {
                    _actionInfo = null;
                    currentIndex = 0;
                    _ctx.ResumeComp(_ctx.MoveComp, this);
                }
            }
        }
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _actionInfo = null;
        ifContinueAction = false;
        currentIndex = 0;
    }

    public bool IsAttacking => _actionInfo != null;

    public void InterruptAttack(AttackInterruptReason reason = AttackInterruptReason.Forced)
    {
        if (_actionInfo != null && actions != null && actions.Length > 0 && currentIndex >= 0 && currentIndex < actions.Length)
        {
            actions[currentIndex].Interrupt(_actionInfo);
        }

        _actionInfo = null;
        ifContinueAction = false;
        currentIndex = 0;
        WeaponAttackTrailEffect.Stop(_ctx, true);
        _ctx?.ResumeComp(_ctx.MoveComp, this);
    }

    public void ShutDown()
    {
        InterruptAttack(AttackInterruptReason.CapabilityLocked);
    }

    public void Resume() { }
}
