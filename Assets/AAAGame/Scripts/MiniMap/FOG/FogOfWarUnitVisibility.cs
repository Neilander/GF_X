using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG
{
    /// <summary>
    /// 战争迷雾单位可见性控制
    /// 根据迷雾状态自动显示/隐藏单位
    /// </summary>
    public class FogOfWarUnitVisibility : MonoBehaviour
    {
        [Header("设置")]
        [SerializeField] private int ownerPlayerMask = 1; // 单位所属玩家
        [SerializeField] private int checkPlayerMask = 1; // 检查哪些玩家的视野
        [SerializeField] private bool hideWhenNotVisible = true;
        [SerializeField] private float checkInterval = 0.1f; // 检查间隔

        [Header("隐藏方式")]
        [SerializeField] private HideMethod hideMethod = HideMethod.SetActive;
        [SerializeField] private int hiddenLayer = 31; // 隐藏层（用于 Layer 方式）

        private FogOfWarManager fogManager;
        private bool isVisible = true;
        private float checkTimer = 0f;
        private int originalLayer;
        private Renderer[] renderers;

        public enum HideMethod
        {
            SetActive,      // 使用 SetActive
            Layer,          // 使用 Layer（推荐，性能更好）
            Renderer        // 禁用 Renderer
        }

        private void Start()
        {
            // 从场景中查找 FogOfWarManager
            fogManager = FindObjectOfType<FogOfWarManager>();
            if (fogManager == null)
            {
                Log.Error("[FogVisibility] FogOfWarManager not found in scene! Please add a GameObject named 'FogOfWarManager' with FogOfWarManager component.");
                enabled = false;
                return;
            }

            originalLayer = gameObject.layer;
            renderers = GetComponentsInChildren<Renderer>();

            // 立即检查一次
            CheckVisibility();
        }

        private void Update()
        {
            if (!hideWhenNotVisible) return;

            checkTimer += Time.deltaTime;
            if (checkTimer >= checkInterval)
            {
                checkTimer = 0f;
                CheckVisibility();
            }
        }

        /// <summary>
        /// 检查可见性
        /// </summary>
        private void CheckVisibility()
        {
            // 如果是己方单位，始终可见
            if ((ownerPlayerMask & checkPlayerMask) != 0)
            {
                SetVisible(true);
                return;
            }

            // 检查是否在视野内
            bool shouldBeVisible = fogManager.IsPositionVisible(transform.position, checkPlayerMask);

            if (shouldBeVisible != isVisible)
            {
                SetVisible(shouldBeVisible);
            }
        }

        /// <summary>
        /// 设置可见性
        /// </summary>
        private void SetVisible(bool visible)
        {
            if (isVisible == visible) return;

            isVisible = visible;

            switch (hideMethod)
            {
                case HideMethod.SetActive:
                    gameObject.SetActive(visible);
                    break;

                case HideMethod.Layer:
                    SetLayer(visible ? originalLayer : hiddenLayer);
                    break;

                case HideMethod.Renderer:
                    SetRenderersEnabled(visible);
                    break;
            }

            Log.Info($"[FogVisibility] Unit {gameObject.name} visibility: {visible}");
        }

        /// <summary>
        /// 设置层级
        /// </summary>
        private void SetLayer(int layer)
        {
            gameObject.layer = layer;

            // 递归设置子对象
            foreach (Transform child in transform)
            {
                child.gameObject.layer = layer;
            }
        }

        /// <summary>
        /// 启用/禁用渲染器
        /// </summary>
        private void SetRenderersEnabled(bool enabled)
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        /// <summary>
        /// 强制检查可见性
        /// </summary>
        public void ForceCheckVisibility()
        {
            CheckVisibility();
        }

        /// <summary>
        /// 设置检查的玩家掩码
        /// </summary>
        public void SetCheckPlayerMask(int mask)
        {
            checkPlayerMask = mask;
            ForceCheckVisibility();
        }

        /// <summary>
        /// 获取当前可见性
        /// </summary>
        public bool IsCurrentlyVisible()
        {
            return isVisible;
        }
    }
}
