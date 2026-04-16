using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap
{
    /// <summary>
    /// 小地图报告组件
    /// 挂在需要显示在小地图上的单位身上
    /// 由 Entity 逻辑类调用 Tick 方法
    /// </summary>
    public class MinimapReportComponent : MonoBehaviour
    {
        private MinimapManager minimapManager;
        private int unitId = -1;
        private SideType side = SideType.NoSide;
        private MinimapUnitType unitType = MinimapUnitType.Soldier;
        private string iconPrefabName = null;

        /// <summary>
        /// 初始化（士兵）
        /// </summary>
        /// <param name="unitSide">单位势力</param>
        public void Initialize(SideType unitSide)
        {
            Initialize(unitSide, MinimapUnitType.Soldier, null);
        }

        /// <summary>
        /// 初始化（完整参数）
        /// </summary>
        /// <param name="unitSide">单位势力</param>
        /// <param name="unitType">单位类型（士兵/建筑）</param>
        /// <param name="iconPrefabName">建筑图标预制体名称（仅建筑使用）</param>
        public void Initialize(SideType unitSide, MinimapUnitType unitType, string iconPrefabName = null)
        {
            side = unitSide;
            this.unitType = unitType;
            this.iconPrefabName = iconPrefabName;

            RegisterIfNeeded();
        }

        public void SetSide(SideType unitSide)
        {
            side = unitSide;
        }

        /// <summary>
        /// Tick 更新（由 Entity 调用）
        /// </summary>
        public void Tick()
        {
            if (unitId < 0 || minimapManager == null)
            {
                RegisterIfNeeded();
            }

            if (minimapManager != null && unitId >= 0)
            {
                minimapManager.UpdateUnit(unitId, transform.position, side);
            }
        }

        private void OnDisable()
        {
            UnregisterIfNeeded();
        }

        private void OnDestroy()
        {
            UnregisterIfNeeded();
        }

        private void RegisterIfNeeded()
        {
            if (unitId >= 0)
            {
                return;
            }

            minimapManager = GameEntry.GetComponent<MinimapManager>();
            if (minimapManager == null)
            {
                return;
            }

            unitId = minimapManager.RegisterUnit(transform.position, side, unitType, iconPrefabName);
            UnityGameFramework.Runtime.Log.Info($"[MinimapReport] Unit registered: ID={unitId}, Side={side}, Type={unitType}, Pos={transform.position}");
        }

        private void UnregisterIfNeeded()
        {
            if (minimapManager == null || unitId < 0)
            {
                return;
            }

            minimapManager.UnregisterUnit(unitId);
            unitId = -1;
        }
    }
}
