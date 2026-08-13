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

    public string relatedTriggerString;

    internal BasicAction CreateRuntimeSnapshot()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Skill actions cannot be snapshotted during a logic frame.");

        SkillRuntimeSnapshotValidation.ValidateReferenceFields(this);
        BasicAction snapshot = Instantiate(this);
        snapshot.hideFlags = HideFlags.HideAndDontSave;
        return snapshot;
    }

    // ===== runtime creation =====
    protected virtual ActionInfo CreateInfo(IEntityContext body)
    {
        return new ActionInfo
        {
            selfBody = body,
            elapsed = Fix64.Zero,
            isRunning = false,
            isInterrupted = false,
            isFinished = false,
        };
    }

    // ===== executor API =====

    public virtual void StartAction(IEntityContext body, out ActionInfo info)
    {
        StartAction(body, null, out info);
    }

    public virtual void StartAction(IEntityContext body, SkillInfo fatherInfo, out ActionInfo info)
    {
        StartAction(body, fatherInfo, null, out info);
    }

    public virtual void StartAction(
        IEntityContext body,
        SkillInfo fatherInfo,
        Action<ActionInfo> configure,
        out ActionInfo info)
    {
        info = CreateInfo(body);

        info.elapsed = Fix64.Zero;
        info.isRunning = true;
        info.isInterrupted = false;
        info.isFinished = false;
        info.fatherInfo = fatherInfo;
        configure?.Invoke(info);

        info.RaiseStarted();
        OnStart(info);
    }

    public virtual void Tick(ActionInfo info, Fix64 deltaTime)
    {
        if (!info.isRunning || info.isFinished || info.isInterrupted)
            return;

        info.elapsed += deltaTime;

        OnUpdate(info, deltaTime);

    }

    public virtual void Interrupt(ActionInfo info)
    {
        if (!info.isRunning || info.isFinished || info.isInterrupted)
            return;

        info.isRunning = false;
        info.isInterrupted = true;

        OnInterrupt(info);
        info.RaiseInterrupted();
    }

    public void FinishAction(ActionInfo info)
    {
        if (!info.isRunning || info.isFinished)
            return;

        info.isRunning = false;
        info.isFinished = true;

        OnFinish(info);
        info.RaiseFinished();
    }

    // ===== hooks =====
    protected virtual void OnStart(ActionInfo info) { }
    protected virtual void OnUpdate(ActionInfo info, Fix64 deltaTime) { }
    protected virtual void OnFinish(ActionInfo info) { }
    protected virtual void OnInterrupt(ActionInfo info) { }
}


public class ActionInfo
{
    public int executeIndex;
    public SkillInfo fatherInfo;
    public bool injectInfoAlready = false;

    // ===== 伤害相关 ====
    public IEntityContext selfBody;
    public Damage damageInfo;

    // ===== 继承数值 ====
    public List<ISelectable> selectTargets;
    public FixVector2 selectPos;


    public Fix64 elapsed;
    public bool isRunning;
    public bool isInterrupted;
    public bool isFinished;

    // ===== runtime events =====
    public event Action Started;
    public event Action Finished;
    public event Action Interrupted;

    // 通用数据池
    public readonly Dictionary<string, bool> bools = new();
    public readonly Dictionary<string, int> ints = new();

    internal void RaiseStarted() => Started?.Invoke();
    internal void RaiseFinished() => Finished?.Invoke();
    internal void RaiseInterrupted() => Interrupted?.Invoke();
}
