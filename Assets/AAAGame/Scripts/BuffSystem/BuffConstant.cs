namespace AAAGame.Scripts.BuffSystem
{
    /// <summary>
    /// Buff 回调时机的字符串常量
    /// </summary>
    public static class BuffConstant
    {
        /// <summary>Buff 首次添加时</summary>
        public const string OnCreate = "OnCreate";

        /// <summary>每次 tick 间隔触发时</summary>
        public const string OnTick = "OnTick";

        /// <summary>Buff 被完全移除时</summary>
        public const string OnRemove = "OnRemove";

        /// <summary>叠加层数时</summary>
        public const string OnAddStack = "OnAddStack";

        /// <summary>减少层数时</summary>
        public const string OnReduceStack = "OnReduceStack";
    }
}
