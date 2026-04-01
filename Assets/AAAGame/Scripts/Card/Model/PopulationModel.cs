using UnityEngine;

namespace AAAGame.Card
{
    /// <summary>
    /// 人口数据模型
    /// 管理人口的消耗和恢复
    /// </summary>
    public class PopulationModel
    {
        private int m_CurrentPopulation;
        private int m_MaxPopulation;

        public PopulationModel(int maxPopulation = CardConst.DefaultMaxPopulation)
        {
            m_MaxPopulation = maxPopulation;
            m_CurrentPopulation = 0;
        }

        /// <summary>
        /// 当前人口
        /// </summary>
        public int CurrentPopulation => m_CurrentPopulation;

        /// <summary>
        /// 最大人口
        /// </summary>
        public int MaxPopulation => m_MaxPopulation;

        /// <summary>
        /// 剩余可用人口
        /// </summary>
        public int AvailablePopulation => m_MaxPopulation - m_CurrentPopulation;

        /// <summary>
        /// 设置最大人口
        /// </summary>
        public void SetMaxPopulation(int maxPopulation)
        {
            if (maxPopulation < 0)
            {
                Debug.LogError("[Card] Max population cannot be negative.");
                return;
            }

            m_MaxPopulation = maxPopulation;

            // 如果当前人口超过最大值，调整为最大值
            if (m_CurrentPopulation > m_MaxPopulation)
            {
                m_CurrentPopulation = m_MaxPopulation;
            }
        }

        /// <summary>
        /// 消耗人口
        /// </summary>
        public bool ConsumePopulation(int amount)
        {
            if (amount < 0)
            {
                Debug.LogError("[Card] Consume amount cannot be negative.");
                return false;
            }

            if (!HasEnoughPopulation(amount))
            {
                Debug.LogWarning($"[Card] Not enough population. Required: {amount}, Available: {AvailablePopulation}");
                return false;
            }

            m_CurrentPopulation += amount;
            return true;
        }

        /// <summary>
        /// 恢复人口
        /// </summary>
        public bool RestorePopulation(int amount)
        {
            if (amount < 0)
            {
                Debug.LogError("[Card] Restore amount cannot be negative.");
                return false;
            }

            m_CurrentPopulation -= amount;

            // 确保不会低于0
            if (m_CurrentPopulation < 0)
            {
                m_CurrentPopulation = 0;
            }

            return true;
        }

        /// <summary>
        /// 检查是否有足够的人口
        /// </summary>
        public bool HasEnoughPopulation(int amount)
        {
            return AvailablePopulation >= amount;
        }

        /// <summary>
        /// 重置人口（清空当前人口）
        /// </summary>
        public void Reset()
        {
            m_CurrentPopulation = 0;
        }

        /// <summary>
        /// 获取人口使用率（0-1）
        /// </summary>
        public float GetPopulationUsageRatio()
        {
            if (m_MaxPopulation <= 0)
            {
                return 0f;
            }

            return (float)m_CurrentPopulation / m_MaxPopulation;
        }
    }
}
