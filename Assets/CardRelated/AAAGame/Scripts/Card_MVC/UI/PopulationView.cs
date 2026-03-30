using UnityEngine;
using TMPro;
using GameFramework;
using GameFramework.Event;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 人口显示组件
    /// 显示当前人口和最大人口
    /// </summary>
    public class PopulationView : MonoBehaviour
    {
        [Header("UI组件")]
        [SerializeField] private TextMeshProUGUI populationText;
        [SerializeField] private TextMeshProUGUI currentPopulationText;
        [SerializeField] private TextMeshProUGUI maxPopulationText;
        
        [Header("显示格式")]
        [SerializeField] private string displayFormat = "{0}/{1}";
        [SerializeField] private bool useWarningColor = true;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warningColor = Color.red;
        [SerializeField] private float warningThreshold = 0.8f; // 80%时显示警告色

        private PopulationModel m_PopulationModel;

        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize(PopulationModel populationModel)
        {
            m_PopulationModel = populationModel;
            
            // 订阅人口变化事件
            GFBuiltin.Event.Subscribe(PopulationChangedEventArgs.EventId, OnPopulationChanged);
            
            // 初始显示
            UpdateDisplay();
        }

        /// <summary>
        /// 更新显示
        /// </summary>
        private void UpdateDisplay()
        {
            if (m_PopulationModel == null) return;

            int current = m_PopulationModel.CurrentPopulation;
            int max = m_PopulationModel.MaxPopulation;

            // 更新文本
            if (populationText != null)
            {
                populationText.text = Utility.Text.Format(displayFormat, current, max);
                
                // 根据人口比例设置颜色
                if (useWarningColor)
                {
                    float ratio = max > 0 ? (float)current / max : 0f;
                    populationText.color = ratio >= warningThreshold ? warningColor : normalColor;
                }
            }

            if (currentPopulationText != null)
            {
                currentPopulationText.text = current.ToString();
            }

            if (maxPopulationText != null)
            {
                maxPopulationText.text = max.ToString();
            }
        }

        /// <summary>
        /// 人口变化事件处理
        /// </summary>
        private void OnPopulationChanged(object sender, GameEventArgs e)
        {
            UpdateDisplay();
        }

        private void OnDestroy()
        {
            // 取消订阅（检查 GFBuiltin.Event 是否存在）
            if (GFBuiltin.Event != null)
            {
                GFBuiltin.Event.Unsubscribe(PopulationChangedEventArgs.EventId, OnPopulationChanged);
            }
        }
    }
}
