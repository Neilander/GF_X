using System.Collections.Generic;
using GameFramework;
using UnityEngine;

namespace AAAGame.Scripts.BuffSystem
{
    /// <summary>
    /// Buff组件实现类（纯C#类，不依赖MonoBehaviour）
    /// </summary>
    public class CharacterBuffComp : IBuffComp
    {
        /// <summary>
        /// Buff字典
        /// </summary>
        private Dictionary<string, BuffData> _buffDict = new Dictionary<string, BuffData>();

        /// <summary>
        /// 宿主实体
        /// </summary>
        private MAEntity _hostEntity;

        /// <summary>
        /// 属性管理器
        /// </summary>
        private CreaturePropertyManager _propertyManager;

        /// <summary>
        /// 初始化Buff组件
        /// </summary>
        public void Init(MAEntity entity)
        {
            _hostEntity = entity;
            // CreaturePropertyManager不是MonoBehaviour，直接从GeneralCreature获取
            global::GeneralCreature generalCreature = entity as global::GeneralCreature;
            if (generalCreature != null)
            {
                _propertyManager = generalCreature.CreaturePropertyManager;
            }
        }

        /// <summary>
        /// 添加Buff
        /// </summary>
        public bool AddBuff(BuffData buffData, MAEntity hostEntity)
        {
            if (buffData == null || string.IsNullOrEmpty(buffData.id))
            {
                return false;
            }

            if (buffData.modules == null)
            {
                buffData.modules = new List<BuffCallback>();
            }

            ApplyStatusResistanceToNegativeBuff(buffData);

            // 检查是否已存在该Buff
            if (_buffDict.TryGetValue(buffData.id, out BuffData existingBuff))
            {
                if (existingBuff.currentStack >= existingBuff.maxStack && existingBuff.maxStack <= 1)
                {
                    if (!existingBuff.isForever && !buffData.isForever)
                        existingBuff.remainingTime = Mathf.Max(existingBuff.remainingTime, buffData.remainingTime);

                    ReferencePool.Release(buffData);
                    return true;
                }

                // 如果达到最大层数，不叠加
                if (existingBuff.currentStack >= existingBuff.maxStack)
                {
                    ReferencePool.Release(buffData);
                    return false;
                }

                // 叠加层数
                int oldStack = existingBuff.currentStack;
                existingBuff.currentStack++;

                // 调用叠加回调
                if (existingBuff.modules != null)
                {
                    foreach (BuffCallback module in existingBuff.modules)
                    {
                        if (module == null)
                        {
                            continue;
                        }

                        module.OnAddStack(oldStack, existingBuff.currentStack);
                    }
                }

                ReferencePool.Release(buffData);
                return true;
            }

            // 添加新Buff
            _buffDict.Add(buffData.id, buffData);

            // 初始化Buff模块
            foreach (BuffCallback module in buffData.modules)
            {
                if (module == null)
                {
                    continue;
                }

                module.Initialize(buffData, hostEntity);
                module.OnAdd();
            }

            return true;
        }

        private void ApplyStatusResistanceToNegativeBuff(BuffData buffData)
        {
            if (buffData == null || buffData.isForever || buffData.duration <= 0f || !HasNegativeStatusModule(buffData))
                return;

            Fix64 resistance = _propertyManager != null
                ? _propertyManager.GetProperty(CreatureMainProperty.StatusResistance)
                : Fix64.Zero;
            Fix64 multiplier = Fix64.One - resistance / (Fix64)100;
            if (multiplier < Fix64.Zero)
                multiplier = Fix64.Zero;

            buffData.duration = Mathf.Max(0f, (float)((Fix64)buffData.duration * multiplier));
            buffData.remainingTime = buffData.duration;
        }

        private static bool HasNegativeStatusModule(BuffData buffData)
        {
            if (buffData?.modules == null)
                return false;

            for (int i = 0; i < buffData.modules.Count; i++)
            {
                if (buffData.modules[i] != null && buffData.modules[i].IsNegativeStatus)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 更新Buff
        /// </summary>
        public void UpdateBuff(float deltaTime)
        {
            List<string> expiredBuffs = new List<string>();

            foreach (KeyValuePair<string, BuffData> kvp in _buffDict)
            {
                BuffData buffData = kvp.Value;
                if (buffData == null)
                {
                    expiredBuffs.Add(kvp.Key);
                    continue;
                }

                // 永久Buff不更新时间
                if (!buffData.isForever)
                {
                    buffData.remainingTime -= deltaTime;

                    // 检查是否过期
                    if (buffData.remainingTime <= 0f)
                    {
                        expiredBuffs.Add(kvp.Key);
                        continue;
                    }
                }

                // 更新每个Buff模块
                if (buffData.modules == null)
                {
                    continue;
                }

                foreach (BuffCallback module in buffData.modules)
                {
                    if (module == null)
                    {
                        continue;
                    }

                    module.OnUpdate(deltaTime);
                }
            }

            // 移除过期Buff（调用OnDurationEnd）
            foreach (string buffId in expiredBuffs)
            {
                RemoveExpiredBuff(buffId);
            }
        }

        /// <summary>
        /// 移除指定 Buff。
        /// </summary>
        public bool RemoveBuff(string buffId)
        {
            if (_buffDict.TryGetValue(buffId, out BuffData buffData))
            {
                // 先从字典摘除，避免回调中重入导致重复释放。
                _buffDict.Remove(buffId);

                List<BuffCallback> modulesCopy = buffData != null && buffData.modules != null
                    ? new List<BuffCallback>(buffData.modules)
                    : null;

                if (modulesCopy != null)
                {
                    foreach (BuffCallback module in modulesCopy)
                    {
                        if (module == null)
                        {
                            continue;
                        }

                        module.OnRemove();
                        module.Clear();
                    }
                }

                ReferencePool.Release(buffData);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 是否存在指定 Buff。
        /// </summary>
        public bool HasBuff(string buffId)
        {
            return !string.IsNullOrEmpty(buffId) && _buffDict.ContainsKey(buffId);
        }

        /// <summary>
        /// 获取指定 id 的 Buff 上第一个类型为 T 的模块。
        /// 常用于外部对"已经挂上的某个 buff 模块"做参数修改（如寿命加减）。
        /// </summary>
        public T GetBuffModule<T>(string buffId) where T : BuffCallback
        {
            if (string.IsNullOrEmpty(buffId)) return null;
            if (!_buffDict.TryGetValue(buffId, out BuffData data) || data.modules == null) return null;
            for (int i = 0; i < data.modules.Count; i++)
            {
                if (data.modules[i] is T t) return t;
            }
            return null;
        }

        /// <summary>
        /// 便捷访问宿主身上的 TimedDeathBuff 实例（没有则返回 null）。
        /// </summary>
        public TimedDeathBuff GetTimedDeathBuff()
        {
            return GetBuffModule<TimedDeathBuff>("timed_death");
        }

        /// <summary>
        /// 按 buff id 前缀批量移除身上的 buff。
        /// 典型用途：DreamPark 丢卡 buff 阶段结束清理。
        /// </summary>
        public int RemoveBuffsByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return 0;
            List<string> toRemove = null;
            foreach (var kv in _buffDict)
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Key.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    toRemove ??= new List<string>();
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove == null) return 0;
            foreach (var id in toRemove) RemoveBuff(id);
            return toRemove.Count;
        }

        /// <summary>
        /// 枚举当前挂在身上的所有 Buff 模块（用于伤害钩子等全局遍历场景）。
        /// </summary>
        public IEnumerable<BuffCallback> EnumerateAllModules()
        {
            foreach (var kv in _buffDict)
            {
                var data = kv.Value;
                if (data == null || data.modules == null) continue;
                for (int i = 0; i < data.modules.Count; i++)
                {
                    var m = data.modules[i];
                    if (m != null) yield return m;
                }
            }
        }

        /// <summary>
        /// 移除过期Buff（调用OnDurationEnd）
        /// </summary>
        private bool RemoveExpiredBuff(string buffId)
        {
            if (_buffDict.TryGetValue(buffId, out BuffData buffData))
            {
                // 先从字典摘除，避免 OnDurationEnd 触发 Hide/Shutdown 时重复处理同一 Buff。
                _buffDict.Remove(buffId);

                // 调用持续时间结束回调（创建副本避免遍历修改错误）
                List<BuffCallback> modulesCopy = buffData != null && buffData.modules != null
                    ? new List<BuffCallback>(buffData.modules)
                    : null;

                if (modulesCopy != null)
                {
                    foreach (BuffCallback module in modulesCopy)
                    {
                        if (module == null)
                        {
                            continue;
                        }

                        module.OnDurationEnd();
                        module.OnRemove();
                        module.Clear();
                    }
                }

                // 移除Buff数据
                ReferencePool.Release(buffData);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 关闭Buff组件
        /// </summary>
        public void ShutDown()
        {
            ClearAllBuffs();
            _hostEntity = null;
            _propertyManager = null;
        }

        /// <summary>
        /// 恢复Buff组件
        /// </summary>
        public void Resume()
        {
            // Buff组件不需要特殊的恢复逻辑
        }

        /// <summary>
        /// 宿主死亡时处理
        /// </summary>
        public void OnHostDead()
        {
            List<BuffData> buffSnapshot = new List<BuffData>(_buffDict.Values);

            foreach (BuffData buffData in buffSnapshot)
            {
                if (buffData == null || buffData.modules == null)
                {
                    continue;
                }

                foreach (BuffCallback module in buffData.modules)
                {
                    if (module == null)
                    {
                        continue;
                    }

                    module.OnHostDead();
                }
            }

            // 清空所有Buff
            ClearAllBuffs();
        }

        /// <summary>
        /// 宿主击杀目标时处理
        /// </summary>
        public void OnKill(MAEntity target)
        {
            List<BuffData> buffSnapshot = new List<BuffData>(_buffDict.Values);

            foreach (BuffData buffData in buffSnapshot)
            {
                if (buffData == null || buffData.modules == null)
                {
                    continue;
                }

                foreach (BuffCallback module in buffData.modules)
                {
                    if (module == null)
                    {
                        continue;
                    }

                    module.OnKill(target);
                }
            }
        }

        /// <summary>
        /// 清空所有Buff
        /// </summary>
        private void ClearAllBuffs()
        {
            if (_buffDict.Count == 0)
            {
                return;
            }

            // 先快照并清空字典，避免回调内重入导致遍历失效或重复释放。
            List<BuffData> buffSnapshot = new List<BuffData>(_buffDict.Values);
            _buffDict.Clear();

            foreach (BuffData buffData in buffSnapshot)
            {
                if (buffData == null)
                {
                    continue;
                }

                if (buffData.modules != null)
                {
                    List<BuffCallback> modulesCopy = new List<BuffCallback>(buffData.modules);
                    foreach (BuffCallback module in modulesCopy)
                    {
                        if (module == null)
                        {
                            continue;
                        }

                        module.OnRemove();
                        module.Clear();
                    }
                }

                ReferencePool.Release(buffData);
            }
        }

        /// <summary>
        /// 获取属性管理器
        /// </summary>
        public CreaturePropertyManager PropertyManager => _propertyManager;

        /// <summary>
        /// 获取宿主实体
        /// </summary>
        public MAEntity HostEntity => _hostEntity;
    }
}
