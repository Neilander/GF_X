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
    public float duration;
    
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
    public float remainingTime;
    
    public void Clear()
    {
        id = null;
        duration = 0f;
        isForever = false;
        maxStack = 1;
        currentStack = 1;
        modules?.Clear();
        modules = null;
        remainingTime = 0f;
    }
    
    public static BuffData Create()
    {
        return ReferencePool.Acquire<BuffData>();
    }
    
    public static BuffData Create(string id, float duration, bool isForever, int maxStack, List<BuffCallback> modules)
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