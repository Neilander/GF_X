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

    [Header("Config")] [SerializeField] protected bool ifUseDuration = true;
    [SerializeField] protected float duration = 0f;
    [SerializeField] protected float acceptInputFromPercent = 1f;
    public string relatedTriggerString;

    public const string HAS_HITBOX = "HAS_HITBOX";
    public const string HITBOX_PREFAB_NAME = "HitBox";
    public const string ACCEPTED_INPUT = "Accepted_Input";
    
    // ===== runtime creation =====
    protected virtual ActionInfo CreateInfo(GeneralCreature body)
    {
        return new ActionInfo
        {
            selfBody = body,
            elapsed = 0f,
            isRunning = false,
            isInterrupted = false,
            isFinished = false,
            inputs = GF.DataModel.GetDataModel<InputModel>()
        };
    }

    // ===== executor API =====

    public virtual void StartAction(GeneralCreature body, out ActionInfo info)
    {
        info = CreateInfo(body);

        info.elapsed = 0f;
        info.isRunning = true;
        info.isInterrupted = false;
        info.isFinished = false;
        
        info.bools[HAS_HITBOX] = false;
        info.bools[ACCEPTED_INPUT] = false;

        info.RaiseStarted();
        OnStart(info);
    }

    public virtual void Tick(ActionInfo info, float deltaTime)
    {
        if (!info.isRunning || info.isFinished || info.isInterrupted)
            return;

        info.elapsed += deltaTime;

        OnUpdate(info, deltaTime);

        if (duration > 0f && info.elapsed >= duration && ifUseDuration)
            FinishAction(info);
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

    public virtual bool AcceptInput(ActionInfo info)
    {
        if (!ifUseDuration)
        {
            GF.LogWarning("警告，行为没有启用duration，却在用duration检查是否允许输入");
            return false;
        }

        return info.elapsed / duration >= acceptInputFromPercent && !info.bools[ACCEPTED_INPUT];
    }

    // ===== hooks =====
    protected virtual void OnStart(ActionInfo info) { }
    protected virtual void OnUpdate(ActionInfo info, float deltaTime) { }
    protected virtual void OnFinish(ActionInfo info) { }
    protected virtual void OnInterrupt(ActionInfo info) { }
}


public class ActionInfo
{
    public InputModel inputs;
    public int executeIndex;
    public SkillInfo fatherInfo;
    public bool injectInfoAlready = false;
    
    // ===== 伤害相关 ====
    public GeneralCreature selfBody;
    public HitBox hitbox;
    public int hitboxID;
    public Damage damageInfo;
    
    // ===== 继承数值 ====
    public List<ISelectable> selectTargets;
    public Vector3 selectPos;
    

    public float elapsed;
    public bool isRunning;
    public bool isInterrupted;
    public bool isFinished;

    // ===== runtime events =====
    public event Action Started;
    public event Action Finished;
    public event Action Interrupted;

    // 通用数据池
    public readonly Dictionary<string, bool> bools = new();
    public readonly Dictionary<string, float> floats = new();
    public readonly Dictionary<string, int> ints = new();
    public readonly Dictionary<string, UnityEngine.Object> objects = new();

    internal void RaiseStarted()     => Started?.Invoke();
    internal void RaiseFinished()    => Finished?.Invoke();
    internal void RaiseInterrupted() => Interrupted?.Invoke();
}