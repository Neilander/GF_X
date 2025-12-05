using UnityEngine;
using System;
using System.Collections.Generic;

namespace BaseUtility
{
    public class MessageBehavior : MonoBehaviour
    {

        private readonly Dictionary<Enum, List<MessageSystem.MessageHandler>> _registered =
            new Dictionary<Enum, List<MessageSystem.MessageHandler>>();


        /// <summary>
        /// 安全注册：自动记录，销毁时自动反注册
        /// </summary>
        public void SafeRegister<T>(T tag, MessageSystem.MessageHandler handler) where T : Enum
        {
            MessageSystem.Instance.Register(tag, handler);

            // 保存本次注册记录
            if (!_registered.TryGetValue(tag, out var list))
            {
                list = new List<MessageSystem.MessageHandler>();
                _registered.Add(tag, list);
            }

            // 避免重复
            if (!list.Contains(handler))
                list.Add(handler);
        }

        /// <summary>
        /// 安全解绑（可选调用，用于提前移除）
        /// </summary>
        public void SafeUnregister<T>(T tag, MessageSystem.MessageHandler handler) where T : Enum
        {
            if (_registered.TryGetValue(tag, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0) _registered.Remove(tag);
            }

            MessageSystem.Instance.Unregister(tag, handler);
        }
        
        protected virtual void OnDestroy()
        {
            if (MessageSystem.HasInstance)
            {
                // 自动清理：遍历所有记录的注册项
                foreach (var kv in _registered)
                {
                    var tag = kv.Key;
                    var handlers = kv.Value;
                    foreach (MessageSystem.MessageHandler h in handlers)
                    {
                        MessageSystem.Instance.Unregister(tag, h);
                    }
                }

                _registered.Clear();
            }
            
        }
    }
}

