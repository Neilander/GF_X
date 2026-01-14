using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;


public abstract class BasicAction : ScriptableObject
{
    /*
     * 一个抽象的Action
     * Action包含开始函数、开始事件、Update函数、持续时间、结束事件、后续Action、打断函数、打断事件
     * Action会由一个外部执行器调用开始，然后调用Update，同时订阅其结束事件
     * 正常运行的情况下，Action内部会计算运行时间，到点了触发结束事件
     * 外部执行器看是否有后续Action，没有就结束，有就下一个
     *
     * 如果Action被打断，会由外部执行器告诉Action，触发打断函数和打断事件
     */
    
    [Header("Config")]
    [Tooltip("<= 0 表示不自动结束，由外部或内部手动 Finish")]
    [SerializeField] protected float duration = 0f;

    // Runtime state (not serialized)
    protected float elapsed;
    protected bool isRunning;
    protected bool isInterrupted;
    protected bool isFinished;
    protected GeneralCreature selfBody;
    
    // Events (executor 订阅)
    public event Action Started;
    public event Action Finished;
    public event Action Interrupted;

    public bool IsRunning => isRunning;
    public bool IsFinished => isFinished;
    public bool IsInterrupted => isInterrupted;
    public float Elapsed => elapsed;
    public float Duration => duration;

    // ===== executor API =====

    public virtual void StartAction(GeneralCreature body)
    {
        elapsed = 0f;
        isRunning = true;
        isInterrupted = false;
        isFinished = false;
        selfBody = body;

        Started?.Invoke();
        OnStart();
    }

    public virtual void Tick(float deltaTime)
    {
        if (!isRunning || isFinished || isInterrupted) return;

        elapsed += deltaTime;

        OnUpdate(deltaTime);

        if (duration > 0f && elapsed >= duration)
            FinishAction();
    }

    public virtual void Interrupt()
    {
        if (!isRunning || isFinished || isInterrupted) return;

        isRunning = false;
        isInterrupted = true;

        OnInterrupt();
        Interrupted?.Invoke();
    }

    // 允许子类在条件满足时主动结束（也允许 executor 直接调用）
    public void FinishAction()
    {
        if (!isRunning || isFinished) return;

        isRunning = false;
        isFinished = true;

        OnFinish();
        Finished?.Invoke();
    }

    // ===== hooks =====
    protected virtual void OnStart()
    {
    }

    protected virtual void OnUpdate(float deltaTime)
    {
    }

    protected virtual void OnFinish()
    {
    }

    protected virtual void OnInterrupt()
    {
    }
}
