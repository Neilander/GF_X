using UnityEngine;

public class CharacterAttackComp : IAtkComp
{
    private MAEntity _entity;

    public BasicAction[] actions;

    private ActionInfo _actionInfo;
    private bool _ifContinueAction;
    private int _currentIndex;

    public void Attack()
    {
        if (_entity == null) return;
        if (_entity.Brain == null) return;
        // if (_entity.CreaturePropertyManager == null) return;

        if (actions == null || actions.Length == 0) return;

        bool atkPressed = _entity.Brain.Attack;

        if (_actionInfo == null)
        {
            if (!atkPressed) return;

            if (_entity.animator != null)
                _entity.animator.SetTrigger(actions[_currentIndex].relatedTriggerString);

            actions[_currentIndex].StartAction(_entity, out _actionInfo);
            if (_actionInfo != null)
                _actionInfo.damageInfo = new Damage(_entity, 1);

            // 这里第二个参数要求 ICapability：你原 PlayerAttackComp 能传 this，
            // 说明 IAtkComp 大概率就是 ICapability，或者 CharacterAttackComp 实现了它就行。
            _entity.LockComp(_entity.moveComp, this);
            _ifContinueAction = false;
        }
        else
        {
            if (atkPressed && actions[_currentIndex].AcceptInput(_actionInfo))
            {
                _actionInfo.bools[BasicAction.ACCEPTED_INPUT] = true;
                _ifContinueAction = true;

                int nextIndex = (_currentIndex + 1) % actions.Length;
                if (_entity.animator != null)
                    _entity.animator.SetTrigger(actions[nextIndex].relatedTriggerString);
            }

            actions[_currentIndex].Tick(_actionInfo, Time.deltaTime);

            if (_actionInfo.isFinished)
            {
                if (_ifContinueAction)
                {
                    _currentIndex = (_currentIndex + 1) % actions.Length;

                    actions[_currentIndex].StartAction(_entity, out _actionInfo);
                    if (_actionInfo != null)
                        _actionInfo.damageInfo = new Damage(_entity, 1);

                    _ifContinueAction = false;
                }
                else
                {
                    _actionInfo = null;
                    _currentIndex = 0;
                    _entity.ResumeComp(_entity.moveComp, this);
                }
            }
        }
    }

    public void Init(MAEntity entity)
    {
        _entity = entity;
        _actionInfo = null;
        _ifContinueAction = false;
        _currentIndex = 0;
    }

    public void ShutDown() { }
    public void Resume() { }
}