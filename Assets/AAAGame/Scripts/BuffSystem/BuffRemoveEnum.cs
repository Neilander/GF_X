namespace AAAGame.Scripts.BuffSystem
{
    /// <summary>
    /// Buff 移除时的策略
    /// </summary>
    public enum BuffRemoveEnum
    {
        /// <summary>直接清除所有层数</summary>
        Clear,

        /// <summary>逐层减少（每次移除减 1 层，层数归零时才真正移除）</summary>
        Reduce,
    }
}
