namespace AAAGame.Scripts.BuffSystem
{
    /// <summary>
    /// Buff 重复添加时的堆叠策略
    /// </summary>
    public enum BuffUpdateEnum
    {
        /// <summary>叠加持续时间</summary>
        AddTime,

        /// <summary>刷新持续时间 + 叠加层数</summary>
        ReplaceAndAddStack,

        /// <summary>保留当前持续时间 + 叠加层数</summary>
        KeepAndAddStack,
    }
}
