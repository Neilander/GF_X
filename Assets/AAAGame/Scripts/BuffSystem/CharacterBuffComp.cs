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
            
            // 检查是否已存在该Buff
            if (_buffDict.TryGetValue(buffData.id, out BuffData existingBuff))
            {
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
                foreach (BuffCallback module in existingBuff.modules)
                {
                    module.OnAddStack(oldStack, existingBuff.currentStack);
                }
                
                ReferencePool.Release(buffData);
                return true;
            }
            
            // 添加新Buff
            _buffDict.Add(buffData.id, buffData);
            
            // 初始化Buff模块
            foreach (BuffCallback module in buffData.modules)
            {
                module.Initialize(buffData, hostEntity);
                module.OnAdd();
            }
            
            return true;
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
                
                // 永久Buff不更新时间
                if (!buffData.isForever)
                {
                    buffData.remainingTime -= deltaTime;
                    
                    // 检查是否过期
                    if (buffData.remainingTime<= 0f)
                    {
                        expiredBuffs.Add(kvp.Key);
                        continue;
                    }
                }
                
                // 更新每个Buff模块
                foreach (BuffCallback module in buffData.modules)
                {
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
        /// 移除过期Buff（调用OnDurationEnd）
        /// </summary>
        private bool RemoveExpiredBuff(string buffId)
        {
            if (_buffDict.TryGetValue(buffId, out BuffData buffData))
            {
                // 调用持续时间结束回调（创建副本避免遍历修改错误）
                List<BuffCallback> modulesCopy = new List<BuffCallback>(buffData.modules);
                foreach (BuffCallback module in modulesCopy)
                {
                    module.OnDurationEnd();
                    module.OnRemove();
                    module.Clear();
                }
                
                // 移除Buff数据
                _buffDict.Remove(buffId);
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
            List<string> buffIds = new List<string>(_buffDict.Keys);
            
            foreach (string buffId in buffIds)
            {
                BuffData buffData = _buffDict[buffId];
                
                foreach (BuffCallback module in buffData.modules)
                {
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
            foreach (BuffData buffData in _buffDict.Values)
            {
                foreach (BuffCallback module in buffData.modules)
                {
                    module.OnKill(target);
                }
            }
        }
        
        /// <summary>
        /// 清空所有Buff
        /// </summary>
        private void ClearAllBuffs()
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