using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 人口管理器
    /// 纯转发层，数据全在 Controller 的 PopulationModel 里
    /// </summary>
    public class PopulationManager : GameFrameworkComponent
    {
        protected override void Awake()
        {
            base.Awake();
        }
    }
}
