using System.Collections.Generic;

namespace AAAGame.Scripts.BuffSystem
{
    /// <summary>
    /// Buff 静态配置数据（纯 C# 类）。
    /// 运行时不可修改，多个 BuffRuntimeInfo 可共享同一个 BuffData。
    /// </summary>
    public class BuffData
    {
        /// <summary>Buff 唯一标识</summary>
        public string Id { get; private set; }

        /// <summary>最大叠加层数</summary>
        public int MaxStack { get; private set; } = 1;

        /// <summary>是否永久 Buff</summary>
        public bool IsForever { get; private set; }

        /// <summary>持续时间（秒）</summary>
        public float Duration { get; private set; }

        /// <summary>tick 间隔时间（秒），0 表示不触发 tick</summary>
        public float TickTime { get; private set; }

        /// <summary>堆叠策略</summary>
        public BuffUpdateEnum UpdateStrategy { get; private set; }

        /// <summary>移除策略</summary>
        public BuffRemoveEnum RemoveStrategy { get; private set; }

        /// <summary>回调模块列表</summary>
        public List<BuffCallback> Modules { get; private set; } = new List<BuffCallback>();

        /// <summary>标签列表</summary>
        public List<string> Tags { get; private set; } = new List<string>();

        /// <summary>
        /// 配置加载时使用的构造方法。构造后字段不可再修改。
        /// </summary>
        public BuffData(
            string id,
            int maxStack = 1,
            bool isForever = false,
            float duration = 0f,
            float tickTime = 0f,
            BuffUpdateEnum updateStrategy = BuffUpdateEnum.AddTime,
            BuffRemoveEnum removeStrategy = BuffRemoveEnum.Clear,
            List<BuffCallback> modules = null,
            List<string> tags = null)
        {
            Id = id;
            MaxStack = maxStack;
            IsForever = isForever;
            Duration = duration;
            TickTime = tickTime;
            UpdateStrategy = updateStrategy;
            RemoveStrategy = removeStrategy;
            Modules = modules ?? new List<BuffCallback>();
            Tags = tags ?? new List<string>();
        }
    }
}
