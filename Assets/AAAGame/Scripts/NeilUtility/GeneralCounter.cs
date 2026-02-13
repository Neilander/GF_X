using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GeneralCounter
{
    private Fix64 _target;
    private Fix64 _current;
    private Fix64 _start;

    private bool _setted = false;
    private bool _finished = false;

    public void Init(Fix64 target, Fix64 resetValue, Fix64 currentValue)
    {
        _setted = true;
        _target = target;
        _start = resetValue;
        _current = currentValue;
        _finished = _current >= _target;
    }

    public void Init(Fix64 target, bool startWithFinish)
    {
        Init(target, Fix64.Zero,startWithFinish?target: Fix64.Zero);
    }

    public virtual void Tick(Fix64 deltaTime)
    {
        if (!_setted)
            return;

        if (_finished)
            return;
        
        _current += deltaTime;
        _finished = _current >= _target;
    }

    public void Reset()
    {
        _finished = false;
        _current = _start;
    }

    public bool IsFinished()
    {
        if (!_setted)
        {
            GF.Log("当前计时器没有初始化");
            return false;
        }

        

        return _finished;
    }
}
