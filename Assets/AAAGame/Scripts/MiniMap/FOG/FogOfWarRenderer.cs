using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG
{
    /// <summary>
    /// 战争迷雾渲染器
    /// 负责将迷雾数据渲染到屏幕上
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class FogOfWarRenderer : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private FogOfWarManager fogManager;

        [Header("渲染设置")]
        [SerializeField] private bool autoUpdate = true;
        [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;

        private RawImage rawImage;
        private Texture2D fogTexture;
        private Color[] currentColors;
        private Color[] targetColors;
        private FogOfWarGrid fogGrid;
        private FogOfWarConfig config;

        private void Awake()
        {
            rawImage = GetComponent<RawImage>();

            // 自动查找 FogOfWarManager
            if (fogManager == null)
            {
                fogManager = GameEntry.GetComponent<FogOfWarManager>();
            }

            if (fogManager == null)
            {
                Log.Error("[FogOfWarRenderer] FogOfWarManager not found!");
                enabled = false;
                return;
            }

            fogGrid = fogManager.Grid;
            config = fogManager.Config;

            // 创建纹理
            InitializeTexture();

            // 订阅视野更新事件
            fogManager.OnVisionUpdated += OnVisionUpdated;

            Log.Info("[FogOfWarRenderer] Renderer initialized");
        }

        private void OnDestroy()
        {
            if (fogManager != null)
            {
                fogManager.OnVisionUpdated -= OnVisionUpdated;
            }

            if (fogTexture != null)
            {
                Destroy(fogTexture);
            }
        }

        private void Update()
        {
            if (!autoUpdate) return;

            UpdateTexture();
        }

        /// <summary>
        /// 初始化纹理
        /// </summary>
        private void InitializeTexture()
        {
            int width = config.GridWidth;
            int height = config.GridHeight;

            fogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            fogTexture.filterMode = filterMode;
            fogTexture.wrapMode = TextureWrapMode.Clamp;

            currentColors = new Color[width * height];
            targetColors = new Color[width * height];

            // 初始化为黑色（完全隐藏）
            for (int i = 0; i < currentColors.Length; i++)
            {
                currentColors[i] = config.HiddenColor;
                targetColors[i] = config.HiddenColor;
            }

            fogTexture.SetPixels(currentColors);
            fogTexture.Apply();

            rawImage.texture = fogTexture;

            Log.Info($"[FogOfWarRenderer] Texture initialized: {width}x{height}");
        }

        /// <summary>
        /// 视野更新回调
        /// </summary>
        private void OnVisionUpdated()
        {
            UpdateTargetColors();
        }

        /// <summary>
        /// 更新目标颜色数组
        /// </summary>
        private void UpdateTargetColors()
        {
            int playerMask = fogManager.ActivePlayerMask;

            for (int z = 0; z < config.GridHeight; z++)
            {
                for (int x = 0; x < config.GridWidth; x++)
                {
                    int index = x + z * config.GridWidth;
                    FogState state = fogGrid.GetFogState(x, z, playerMask);

                    switch (state)
                    {
                        case FogState.Visible:
                            targetColors[index] = config.VisibleColor;
                            break;
                        case FogState.Explored:
                            targetColors[index] = config.ExploredColor;
                            break;
                        case FogState.Hidden:
                            targetColors[index] = config.HiddenColor;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// 更新纹理
        /// </summary>
        private void UpdateTexture()
        {
            bool needsUpdate = false;

            if (config.EnableEasing)
            {
                // 颜色过渡
                float lerpSpeed = config.EasingSpeed * Time.deltaTime;

                for (int i = 0; i < currentColors.Length; i++)
                {
                    if (currentColors[i] != targetColors[i])
                    {
                        currentColors[i] = Color.Lerp(currentColors[i], targetColors[i], lerpSpeed);
                        needsUpdate = true;
                    }
                }
            }
            else
            {
                // 直接设置
                for (int i = 0; i < currentColors.Length; i++)
                {
                    if (currentColors[i] != targetColors[i])
                    {
                        currentColors[i] = targetColors[i];
                        needsUpdate = true;
                    }
                }
            }

            if (needsUpdate)
            {
                fogTexture.SetPixels(currentColors);
                fogTexture.Apply();
            }
        }

        /// <summary>
        /// 强制刷新纹理
        /// </summary>
        public void ForceRefresh()
        {
            UpdateTargetColors();
            
            // 直接设置，不使用过渡
            for (int i = 0; i < currentColors.Length; i++)
            {
                currentColors[i] = targetColors[i];
            }

            fogTexture.SetPixels(currentColors);
            fogTexture.Apply();

            Log.Info("[FogOfWarRenderer] Texture force refreshed");
        }

        /// <summary>
        /// 设置渲染的玩家掩码
        /// </summary>
        public void SetPlayerMask(int playerMask)
        {
            if (fogManager != null)
            {
                fogManager.SetActivePlayerMask(playerMask);
                ForceRefresh();
            }
        }
    }
}
