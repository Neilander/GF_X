using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;

namespace AAAGame.MiniMap
{
    /// <summary>
    /// 小地图更新事件
    /// 每帧 LateUpdate 广播，携带所有单位的位置快照
    /// </summary>
    public sealed class MinimapUpdateEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(MinimapUpdateEventArgs).GetHashCode();
        
        public override int Id => EventId;

        /// <summary>
        /// 所有单位数据列表
        /// </summary>
        public List<MinimapUnitData> Units { get; private set; }
        
        /// <summary>
        /// 战争迷雾可见性数组 [x, z]
        /// </summary>
        public bool[,] FogVisibility { get; private set; }

        /// <summary>
        /// 创建事件
        /// </summary>
        public static MinimapUpdateEventArgs Create(List<MinimapUnitData> units, bool[,] fogVisibility = null)
        {
            MinimapUpdateEventArgs e = ReferencePool.Acquire<MinimapUpdateEventArgs>();
            
            // 创建列表的副本，避免引用被清空
            if (units != null)
            {
                e.Units = new List<MinimapUnitData>(units);
                UnityGameFramework.Runtime.Log.Info($"[MinimapEvent] Created event with {e.Units.Count} units (copied from {units.Count})");
            }
            else
            {
                e.Units = new List<MinimapUnitData>();
                UnityGameFramework.Runtime.Log.Warning("[MinimapEvent] Input units list is null!");
            }
            
            e.FogVisibility = fogVisibility;
            
            return e;
        }

        /// <summary>
        /// 清理事件
        /// </summary>
        public override void Clear()
        {
            if (Units != null)
            {
                Units.Clear();
                Units = null;
            }
            FogVisibility = null;
        }
    }
}
