using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 玩家手牌管理器
    /// 纯转发层，数据全在 Controller 的 PlayerHandModel 里
    /// </summary>
    public class PlayerHandManager : GameFrameworkComponent
    {
        protected override void Awake()
        {
            base.Awake();
        }
    }
}
