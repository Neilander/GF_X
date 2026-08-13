using System.Collections.Generic;

/*
namespace AAAGame.Scripts.BuffSystem
{
    /// <summary>
    /// Buff 运行时实例，包含对象池以实现零 GC。
    /// 外部只能通过 Acquire/Release 获取和回收实例。
    /// </summary>
    public class BuffRuntimeInfo
    {
        #region 对象池

        private static readonly Stack<BuffRuntimeInfo> _pool = new Stack<BuffRuntimeInfo>();

        /// <summary>
        /// 从池中获取一个实例并初始化。
        /// </summary>
        public static BuffRuntimeInfo Acquire(BuffData data, IEntityContext creator, IEntityContext target)
        {
            var info = _pool.Count > 0 ? _pool.Pop() : new BuffRuntimeInfo();
            info.Init(data, creator, target);
            return info;
        }

        /// <summary>
        /// 回收实例到池中。
        /// </summary>
        public static void Release(BuffRuntimeInfo info)
        {
            info.Reset();
            _pool.Push(info);
        }

        #endregion

        #region 字段

        /// <summary>持有的静态配置数据引用（只读）</summary>
        public BuffData Data { get; private set; }

        /// <summary>施加者实体上下文</summary>
        public IEntityContext Creator { get; private set; }

        /// <summary>承受者实体上下文</summary>
        public IEntityContext Target { get; private set; }

        /// <summary>当前叠加层数</summary>
        public int CurrentStack { get; private set; }

        /// <summary>剩余持续时间</summary>
        public float RemainingTime;

        /// <summary>tick 计时器</summary>
        public float TickTimer;

        #endregion

        #region 属性

        /// <summary>非永久 Buff 且剩余时间耗尽时视为过期</summary>
        public bool IsExpired => !Data.IsForever && RemainingTime <= 0f;

        /// <summary>层数为零时视为空</summary>
        public bool IsStackEmpty => CurrentStack <= 0;

        #endregion

        // 构造函数私有，强制通过对象池使用
        private BuffRuntimeInfo() { }

        #region 方法

        /// <summary>
        /// 初始化所有字段。
        /// </summary>
        private void Init(BuffData data, IEntityContext creator, IEntityContext target)
        {
            Data = data;
            Creator = creator;
            Target = target;
            CurrentStack = 1;
            RemainingTime = data.Duration;
            TickTimer = 0f;
        }

        /// <summary>
        /// 将剩余时间重置为配置的 Duration。
        /// </summary>
        public void ResetDuration()
        {
            RemainingTime = Data.Duration;
        }

        /// <summary>
        /// 增加层数，不超过 Data.MaxStack。
        /// </summary>
        public void AddStack(int count = 1)
        {
            CurrentStack += count;
            if (CurrentStack > Data.MaxStack)
                CurrentStack = Data.MaxStack;
        }

        /// <summary>
        /// 减少层数，最低为 0。
        /// </summary>
        public void ReduceStack(int count = 1)
        {
            CurrentStack -= count;
            if (CurrentStack < 0)
                CurrentStack = 0;
        }

        /// <summary>
        /// 清空所有字段，为对象池回收准备。
        /// </summary>
        private void Reset()
        {
            Data = null;
            Creator = null;
            Target = null;
            CurrentStack = 0;
            RemainingTime = 0f;
            TickTimer = 0f;
        }

        #endregion
    }
}*/
