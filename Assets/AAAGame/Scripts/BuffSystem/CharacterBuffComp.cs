using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// Buff 管理组件核心实现。
/// 管理所有活跃 Buff 的生命周期：添加、Tick、过期移除、手动移除。
/// </summary>
public class CharacterBuffComp : IBuffComp
{
    #region 字段

    private IEntityContext _ctx;
    private readonly List<BuffRuntimeInfo> _activeBuffs = new List<BuffRuntimeInfo>();
    private readonly List<BuffRuntimeInfo> _removeCache = new List<BuffRuntimeInfo>();
    private readonly List<BuffRuntimeInfo> _addQueue = new List<BuffRuntimeInfo>();
    private readonly List<BuffRuntimeInfo> _tagRemoveCache = new List<BuffRuntimeInfo>();
    private bool _isShutDown;

    #endregion

    #region IBuffComp 实现

    /// <summary>初始化，绑定宿主实体</summary>
    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _activeBuffs.Clear();
        _removeCache.Clear();
        _addQueue.Clear();
        _tagRemoveCache.Clear();
        _isShutDown = false;
    }

    /// <summary>每帧驱动 Buff 生命周期</summary>
    public void UpdateBuff(float deltaTime)
    {
        if (_isShutDown) return;

        // 第一步：将 _addQueue 中的 Buff 转入 _activeBuffs，触发 OnCreate
        FlushAddQueue();

        // 第二步：倒序遍历 _activeBuffs，处理时间和 Tick
        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            var info = _activeBuffs[i];

            // 非永久 Buff：扣减剩余时间
            if (!info.Data.IsForever)
            {
                info.RemainingTime -= deltaTime;
            }

            // 有 TickTime 的 Buff：累加 TickTimer，达到间隔时触发 OnTick
            if (info.Data.TickTime > 0f)
            {
                info.TickTimer += deltaTime;
                if (info.TickTimer >= info.Data.TickTime)
                {
                    info.TickTimer -= info.Data.TickTime;
                    InvokeCallbacks(info, BuffConstant.OnTick);
                }
            }

            // 过期检查
            if (info.IsExpired)
            {
                _removeCache.Add(info);
            }
        }

        // 第三步：处理过期 Buff（遵循 RemoveStrategy）
        for (int i = 0; i < _removeCache.Count; i++)
        {
            var info = _removeCache[i];
            if (info.Data.RemoveStrategy == BuffRemoveEnum.Reduce)
            {
                // Reduce 策略：减一层，刷新 duration，层数归零才真正移除
                info.ReduceStack(1);
                InvokeCallbacks(info, BuffConstant.OnReduceStack);
                if (info.IsStackEmpty)
                {
                    InvokeCallbacks(info, BuffConstant.OnRemove);
                    _activeBuffs.Remove(info);
                    BuffRuntimeInfo.Release(info);
                }
                else
                {
                    info.ResetDuration();
                }
            }
            else
            {
                // Clear 策略：直接移除
                InvokeCallbacks(info, BuffConstant.OnRemove);
                _activeBuffs.Remove(info);
                BuffRuntimeInfo.Release(info);
            }
        }

        // 第四步：清空缓存
        _removeCache.Clear();
    }

    /// <summary>添加 Buff，先入 _addQueue，下一次 UpdateBuff 时 flush</summary>
    public void AddBuff(BuffData buffData, IEntityContext creator)
    {
        // 在 _activeBuffs 和 _addQueue 中查找是否已存在同 Id 的 Buff
        BuffRuntimeInfo existing = FindBuff(buffData.Id);
        if (existing == null)
        {
            existing = FindBuffInQueue(buffData.Id);
        }

        if (existing == null)
        {
            // 不存在：创建新实例，加入 _addQueue
            var info = BuffRuntimeInfo.Acquire(buffData, creator, _ctx);
            _addQueue.Add(info);
        }
        else
        {
            // 已存在：根据堆叠策略处理
            switch (buffData.UpdateStrategy)
            {
                case BuffUpdateEnum.AddTime:
                    existing.RemainingTime += buffData.Duration;
                    break;

                case BuffUpdateEnum.ReplaceAndAddStack:
                    existing.ResetDuration();
                    existing.AddStack();
                    InvokeCallbacks(existing, BuffConstant.OnAddStack);
                    break;

                case BuffUpdateEnum.KeepAndAddStack:
                    existing.AddStack();
                    InvokeCallbacks(existing, BuffConstant.OnAddStack);
                    break;
            }
        }
    }

    /// <summary>按 ID 移除 Buff（遵循 RemoveStrategy）</summary>
    public void RemoveBuff(string buffId)
    {
        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            if (_activeBuffs[i].Data.Id != buffId) continue;

            var info = _activeBuffs[i];
            if (RemoveBuffInternal(info))
            {
                _activeBuffs.RemoveAt(i);
                BuffRuntimeInfo.Release(info);
            }
            return;
        }
    }

    /// <summary>按标签移除所有匹配的 Buff</summary>
    public void RemoveBuffByTag(string tag)
    {
        // 使用独立的 _tagRemoveCache，避免与 UpdateBuff 的 _removeCache 冲突
        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            if (_activeBuffs[i].Data.Tags.Contains(tag))
            {
                _tagRemoveCache.Add(_activeBuffs[i]);
            }
        }

        // 收集完毕后统一处理，避免遍历中修改 _activeBuffs
        for (int i = 0; i < _tagRemoveCache.Count; i++)
        {
            var info = _tagRemoveCache[i];
            if (RemoveBuffInternal(info))
            {
                _activeBuffs.Remove(info);
                BuffRuntimeInfo.Release(info);
            }
        }
        _tagRemoveCache.Clear();
    }

    /// <summary>查询是否存在指定 Buff</summary>
    public bool HasBuff(string buffId)
    {
        for (int i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].Data.Id == buffId) return true;
        }
        return false;
    }

    /// <summary>获取指定 Buff 的当前层数，不存在返回 0</summary>
    public int GetBuffStack(string buffId)
    {
        for (int i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].Data.Id == buffId)
                return _activeBuffs[i].CurrentStack;
        }
        return 0;
    }

    #endregion

    #region ICapability 实现

    public void ShutDown()
    {
        _isShutDown = true;

        // 清理所有活跃 Buff：触发 OnRemove 回调（如还原属性修改），然后 Release
        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            var info = _activeBuffs[i];
            InvokeCallbacks(info, BuffConstant.OnRemove);
            BuffRuntimeInfo.Release(info);
        }
        _activeBuffs.Clear();

        // 清理等待队列中尚未激活的 Buff
        for (int i = 0; i < _addQueue.Count; i++)
        {
            BuffRuntimeInfo.Release(_addQueue[i]);
        }
        _addQueue.Clear();
    }

    public void Resume()
    {
        _isShutDown = false;
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 将 _addQueue 中的 Buff 转入 _activeBuffs 并触发 OnCreate。
    /// OnCreate 在 buff 进入 _activeBuffs 后触发，保证回调中 HasBuff/GetBuffStack 能查到。
    /// 注意：快照 count 防止 OnCreate 回调中递归 AddBuff 导致本轮 flush 无限增长。
    /// 递归添加的 Buff 将在下一次 UpdateBuff 的 FlushAddQueue 中处理。
    /// </summary>
    private void FlushAddQueue()
    {
        if (_addQueue.Count == 0) return;

        int count = _addQueue.Count; // 快照，防止 OnCreate 递归 AddBuff 导致无限循环
        for (int i = 0; i < count; i++)
        {
            var info = _addQueue[i];
            _activeBuffs.Add(info);
            InvokeCallbacks(info, BuffConstant.OnCreate);
        }
        _addQueue.RemoveRange(0, count);
    }

    /// <summary>
    /// 处理单个 Buff 的移除逻辑（回调 + 层数判断），不修改 _activeBuffs。
    /// 返回 true 表示应从 _activeBuffs 中移除并 Release。
    /// </summary>
    private bool RemoveBuffInternal(BuffRuntimeInfo info)
    {
        switch (info.Data.RemoveStrategy)
        {
            case BuffRemoveEnum.Clear:
                InvokeCallbacks(info, BuffConstant.OnRemove);
                return true;

            case BuffRemoveEnum.Reduce:
                info.ReduceStack(1);
                InvokeCallbacks(info, BuffConstant.OnReduceStack);
                if (info.IsStackEmpty)
                {
                    InvokeCallbacks(info, BuffConstant.OnRemove);
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>统一触发回调：遍历 Modules，对每个非 null 的 BuffCallback 调用 Apply</summary>
    private void InvokeCallbacks(BuffRuntimeInfo info, string trigger)
    {
        var modules = info.Data.Modules;
        for (int i = 0; i < modules.Count; i++)
        {
            if (modules[i] != null)
            {
                modules[i].Apply(info, trigger);
            }
        }
    }

    /// <summary>在 _activeBuffs 中查找指定 Id 的 Buff</summary>
    private BuffRuntimeInfo FindBuff(string buffId)
    {
        for (int i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].Data.Id == buffId)
                return _activeBuffs[i];
        }
        return null;
    }

    /// <summary>在 _addQueue 中查找指定 Id 的 Buff</summary>
    private BuffRuntimeInfo FindBuffInQueue(string buffId)
    {
        for (int i = 0; i < _addQueue.Count; i++)
        {
            if (_addQueue[i].Data.Id == buffId)
                return _addQueue[i];
        }
        return null;
    }

    #endregion
}
