using UnityEngine;

public class CharacterAttackComp : IAtkComp
{
    private IEntityContext _ctx;

    public BasicAction[] actions;

    private ActionInfo _actionInfo;
    private bool _ifContinueAction;
    private int _currentIndex;

    public void Attack(float deltaTime)
    {
        if (_ctx == null) return;
        if (_ctx.Brain == null) return;

        if (actions == null || actions.Length == 0) return;

        bool atkPressed = _ctx.Brain.Attack;

        if (_actionInfo == null)
        {
            if (!atkPressed) return;

            if (_ctx is GeneralCreature gc && gc.animator != null)
                gc.animator.SetTrigger(actions[_currentIndex].relatedTriggerString);

            if (_ctx is GeneralCreature body)
            {
                actions[_currentIndex].StartAction(body, out _actionInfo);
                if (_actionInfo != null)
                {
                    _actionInfo.damageInfo = new Damage(body, Fix64.One);
                    WeaponAttackTrailEffect.Play(_ctx);
                }
            }

            _ctx.LockComp(_ctx.MoveComp, this);
            _ifContinueAction = false;
        }
        else
        {
            if (atkPressed && actions[_currentIndex].AcceptInput(_actionInfo))
            {
                _actionInfo.bools[BasicAction.ACCEPTED_INPUT] = true;
                _ifContinueAction = true;

                int nextIndex = (_currentIndex + 1) % actions.Length;
                if (_ctx is GeneralCreature gc && gc.animator != null)
                    gc.animator.SetTrigger(actions[nextIndex].relatedTriggerString);
            }

            actions[_currentIndex].Tick(_actionInfo, deltaTime);

            if (_actionInfo.isFinished)
            {
                if (_ifContinueAction)
                {
                    _currentIndex = (_currentIndex + 1) % actions.Length;

                    if (_ctx is GeneralCreature body)
                    {
                        actions[_currentIndex].StartAction(body, out _actionInfo);
                        if (_actionInfo != null)
                        {
                            _actionInfo.damageInfo = new Damage(body, Fix64.One);
                            WeaponAttackTrailEffect.Play(_ctx);
                        }
                    }

                    _ifContinueAction = false;
                }
                else
                {
                    _actionInfo = null;
                    _currentIndex = 0;
                    _ctx.ResumeComp(_ctx.MoveComp, this);
                }
            }
        }
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _actionInfo = null;
        _ifContinueAction = false;
        _currentIndex = 0;
    }

    public bool IsAttacking => _actionInfo != null;

    public void ShutDown() { }
    public void Resume() { }
}
