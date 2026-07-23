using System;
using System.Collections.Generic;
using GameFramework;

/// <summary>
/// Buff数据结构
/// </summary>
[Serializable]
public class BuffData : IReference
{
    /// <summary>
    /// Buff唯一ID
    /// </summary>
    public string id;

    /// <summary>
    /// Buff持续时间（秒）
    /// </summary>
    public Fix64 duration;

    /// <summary>
    /// 是否永久
    /// </summary>
    public bool isForever;

    /// <summary>
    /// 最大叠加层数
    /// </summary>
    public int maxStack;

    /// <summary>
    /// 当前层数
    /// </summary>
    public int currentStack;

    /// <summary>
    /// Buff模块列表
    /// </summary>
    public List<BuffCallback> modules;

    /// <summary>
    /// 剩余时间
    /// </summary>
    public Fix64 remainingTime;

    public bool AdvanceLogicTime(Fix64 deltaTime)
    {
        if (deltaTime <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Buff logic delta must be positive.");
        if (isForever)
            return false;

        remainingTime -= deltaTime;
        return remainingTime <= Fix64.Zero;
    }

    public void Clear()
    {
        id = null;
        duration = Fix64.Zero;
        isForever = false;
        maxStack = 1;
        currentStack = 1;
        modules?.Clear();
        modules = null;
        remainingTime = Fix64.Zero;
    }

    public static BuffData Create()
    {
        return ReferencePool.Acquire<BuffData>();
    }

    public static BuffData Create(string id, float duration, bool isForever, int maxStack, List<BuffCallback> modules)
    {
        return Create(id, (Fix64)duration, isForever, maxStack, modules);
    }

    public static BuffData Create(string id, Fix64 duration, bool isForever, int maxStack, List<BuffCallback> modules)
    {
        BuffData buffData = Create();
        buffData.id = id;
        buffData.duration = duration;
        buffData.isForever = isForever;
        buffData.maxStack = maxStack;
        buffData.currentStack = 1;
        buffData.modules = modules;
        buffData.remainingTime = duration;
        return buffData;
    }
}
