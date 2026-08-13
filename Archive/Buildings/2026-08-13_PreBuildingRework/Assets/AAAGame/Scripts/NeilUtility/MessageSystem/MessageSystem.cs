 using System;
using System.Collections.Generic;
using UnityEngine;

namespace BaseUtility
{
    public class MessageSystem : Singleton<MessageSystem>
    {
        // —— 委托定义 ——（仅收 MessageBox + Sender，本身不关心类型）
        public delegate void MessageHandler(MessageBox box, MonoBehaviour sender);

        // Tag → 多个订阅者
        private readonly Dictionary<object, List<MessageHandler>> _handlers = new();

        // ① 注册
        public void Register<T>(T tag, MessageHandler handler) where T : Enum
        {
            if (!_handlers.TryGetValue(tag, out var list))
            {
                list = new List<MessageHandler>();
                _handlers.Add(tag, list);
            }

            if (handler != null && !list.Contains(handler))
                list.Add(handler);
        }

        // ② 反注册（可选，但实用）
    public void Unregister<T>(T tag, MessageHandler handler) where T : Enum
        {
            if (_handlers.TryGetValue(tag, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0) _handlers.Remove(tag);
            }
        }

        // ③ 触发（在这里做“触发者类型校验”）
        public void Send<T>(T tag, MessageBox box, MonoBehaviour sender) where T : Enum
        {
            if (!_handlers.TryGetValue(tag, out var list) || list.Count == 0) return;

            var snapshot = list.ToArray();
            foreach (var h in snapshot) h?.Invoke(box, sender);
        }

        public void Send<T>(T tag, MonoBehaviour sender) where T : Enum
        {
            MessageBox bx = new MessageBox();
            Send<T>(tag, bx, sender);
        }

        public void Empty() { }
    }


    /// <summary>
    /// 通用消息容器，可扩展
    /// </summary>
    public class MessageBox
    {
        public float[] floatData = new float[3];
        public int[] intData = new int[3];
        public bool[] boolData = new bool[3];

        public string str;        // 额外基础类型
        public Vector3 vec3;      // 常见空间类型
        public object extra;      // 万能扩展字段
        public Transform trans;
        public GameObject gmo;

        public MessageBox Copy()
        {
            MessageBox c = new MessageBox();

            // 基础数组复制
            c.floatData = (float[])floatData.Clone();
            c.intData   = (int[])intData.Clone();
            c.boolData  = (bool[])boolData.Clone();

            // 基础类型复制
            c.str = str;
            c.vec3 = vec3;
            c.extra = extra;
            c.trans = trans;
            c.gmo = gmo;

            
            return c;
        }
    }
}

