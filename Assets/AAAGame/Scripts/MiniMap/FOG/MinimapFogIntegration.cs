using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap.FOG
{
    /// <summary>
    /// 小地图战争迷雾集成
    /// 将战争迷雾渲染到小地图上
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class MinimapFogIntegration : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private FogOfWarManager fogManager;
        [SerializeField] private MinimapManager minimapManager;

        [Header("渲染设置")]
        [SerializeField] private bool autoUpdate = true;
        [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;

        [Header("小地图特定设置")]
        [SerializeField] private bool syncWithMinimapBounds = true;

        private RawImage rawImage;
        private Texture2D fogTexture;
        private Color[] currentColors;
        private Color[] targetColors;
        private FogOfWarGrid fogGrid;
        private FogOfWarConfig fogConfig;
        private MinimapConfig minimapConfig;

        private void Awake()
        {
            rawImage = GetComponent<RawImage>();

            // 自动查找管理器
            if (fogManager == null)
            {
                fogManager = GameEntry.GetComponent<FogOfWarManager>();
            }

            if (minimapManager == null)
            {
                minimapManager = GameEntry.GetComponent<MinimapManager>();
            }

            if (fogManager == null)
            {
                Log.Error("[MinimapFog] FogOfWarManager not found!");
                enabled = false;
                return;
            }

            if (minimapManager == null)
            {
                Log.Warning("[MinimapFog] MinimapManager not found, using default fog bounds");
            }

            fogGrid = fogManager.Grid;
            fogConfig = fogManager.Config;

            if (minimapManager != null)
            {
                minimapConfig = minimapManager.Config;

                // 同步边界
                if (syncWithMinimapBounds)
                {
                    SyncBounds();
                }
            }

            // 创建纹理
            InitializeTexture();

            // 订阅视野更新事件
            fogManager.OnVisionUpdated += OnVisionUpdated;

            Log.Info("[MinimapFog] Minimap fog integration initialized");
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
        /// 同步小地图和战争迷雾的边界
        /// </summary>
        private void SyncBounds()
        {
            if (minimapConfig == null) return;

            fogConfig.WorldMinX = minimapConfig.WorldMinX;
            fogConfig.WorldMaxX = minimapConfig.WorldMaxX;
            fogConfig.WorldMinZ = minimapConfig.WorldMinZ;
            fogConfig.WorldMaxZ = minimapConfig.WorldMaxZ;

            Log.Info("[MinimapFog] Bounds synced with minimap");
        }

        /// <summary>
        /// 初始化纹理
        /// </summary>
        private void InitializeTexture()
        {
            int width = fogConfig.GridWidth;
            int height = fogConfig.GridHeight;

            fogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            fogTexture.filterMode = filterMode;
            fogTexture.wrapMode = TextureWrapMode.Clamp;

            currentColors = new Color[width * height];
            targetColors = new Color[width * height];

            // 初始化为黑色（完全隐藏）
            for (int i = 0; i < currentColors.Length; i++)
            {
                currentColors[i] = fogConfig.HiddenColor;
                targetColors[i] = fogConfig.HiddenColor;
            }

            fogTexture.SetPixels(currentColors);
            fogTexture.Apply();

            rawImage.texture = fogTexture;

            Log.Info($"[MinimapFog] Texture initialized: {width}x{height}");
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

            for (int z = 0; z < fogConfig.GridHeight; z++)
            {
                for (int x = 0; x < fogConfig.GridWidth; x++)
                {
                    int index = x + z * fogConfig.GridWidth;
                    FogState state = fogGrid.GetFogState(x, z, playerMask);

                    switch (state)
                    {
                        case FogState.Visible:
                            targetColors[index] = fogConfig.VisibleColor;
                            break;
                        case FogState.Explored:
                            targetColors[index] = fogConfig.ExploredColor;
                            break;
                        case FogState.Hidden:
                            targetColors[index] = fogConfig.HiddenColor;
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

            if (fogConfig.EnableEasing)
            {
                // 颜色过渡
                float lerpSpeed = fogConfig.EasingSpeed * Time.deltaTime;

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

            Log.Info("[MinimapFog] Texture force refreshed");
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
