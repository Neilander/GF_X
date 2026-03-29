using System.Collections.Generic;
using GameFramework;
using UnityEngine;

/// <summary>
/// Buff管理器
/// </summary>
public class BuffManager : MonoBehaviour
{
    private Dictionary<string, BuffData> _buffDict = new Dictionary<string, BuffData>();
    private MAEntity _hostEntity;
    private CreaturePropertyManager _propertyManager;

    public void Initialize(MAEntity entity)
    {
        _hostEntity = entity;
        GeneralCreature generalCreature = entity as GeneralCreature;
        if (generalCreature != null)
        {
            _propertyManager = generalCreature.CreaturePropertyManager;
        }
    }

    public bool AddBuff(BuffData buffData)
    {
        if (buffData == null || string.IsNullOrEmpty(buffData.id))
        {
            return false;
        }

        // 检查是否已存在该Buff
        if (_buffDict.TryGetValue(buffData.id, out BuffData existingBuff))
        {
            if (existingBuff.currentStack >= existingBuff.maxStack)
            {
                ReferencePool.Release(buffData);
                return false;
            }

            int oldStack = existingBuff.currentStack;
            existingBuff.currentStack++;

            foreach (BuffCallback module in existingBuff.modules)
            {
                module.OnAddStack(oldStack, existingBuff.currentStack);
            }

            ReferencePool.Release(buffData);
            return true;
        }

        // 添加新Buff，直接初始化模块（不再 Instantiate）
        _buffDict.Add(buffData.id, buffData);

        foreach (BuffCallback module in buffData.modules)
        {
            module.Initialize(buffData, _hostEntity);
            module.OnAdd();
        }

        return true;
    }

    public bool RemoveBuff(string buffId)
    {
        if (_buffDict.TryGetValue(buffId, out BuffData buffData))
        {
            foreach (BuffCallback module in buffData.modules)
            {
                module.OnRemove();
                module.Clear();
            }

            _buffDict.Remove(buffId);
            ReferencePool.Release(buffData);
            return true;
        }

        return false;
    }

    public bool RemoveExpiredBuff(string buffId)
    {
        if (_buffDict.TryGetValue(buffId, out BuffData buffData))
        {
            List<BuffCallback> modulesCopy = new List<BuffCallback>(buffData.modules);
            foreach (BuffCallback module in modulesCopy)
            {
                module.OnDurationEnd();
                module.OnRemove();
                module.Clear();
            }

            _buffDict.Remove(buffId);
            ReferencePool.Release(buffData);
            return true;
        }

        return false;
    }

    public bool HasBuff(string buffId)
    {
        return _buffDict.ContainsKey(buffId);
    }

    public BuffData GetBuff(string buffId)
    {
        _buffDict.TryGetValue(buffId, out BuffData buffData);
        return buffData;
    }

    public void UpdateBuffs(float deltaTime)
    {
        List<string> expiredBuffs = new List<string>();

        foreach (KeyValuePair<string, BuffData> kvp in _buffDict)
        {
            BuffData buffData = kvp.Value;

            if (!buffData.isForever)
            {
                buffData.remainingTime -= deltaTime;

                if (buffData.remainingTime <= 0f)
                {
                    expiredBuffs.Add(kvp.Key);
                    continue;
                }
            }

            foreach (BuffCallback module in buffData.modules)
            {
                module.OnUpdate(deltaTime);
            }
        }

        foreach (string buffId in expiredBuffs)
        {
            RemoveExpiredBuff(buffId);
        }
    }

    public void OnHostDead()
    {
        List<string> buffIds = new List<string>(_buffDict.Keys);

        foreach (string buffId in buffIds)
        {
            BuffData buffData = _buffDict[buffId];

            foreach (BuffCallback module in buffData.modules)
            {
                module.OnHostDead();
            }
        }

        ClearAllBuffs();
    }

    public void OnKill(MAEntity target)
    {
        foreach (BuffData buffData in _buffDict.Values)
        {
            foreach (BuffCallback module in buffData.modules)
            {
                module.OnKill(target);
            }
        }
    }

    public void ClearAllBuffs()
    {
        foreach (BuffData buffData in _buffDict.Values)
        {
            foreach (BuffCallback module in buffData.modules)
            {
                module.OnRemove();
                module.Clear();
            }
            ReferencePool.Release(buffData);
        }

        _buffDict.Clear();
    }

    public CreaturePropertyManager PropertyManager => _propertyManager;
    public MAEntity HostEntity => _hostEntity;
}
