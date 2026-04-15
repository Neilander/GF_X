using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using AAAGame.MiniMap.FOG;

#if ENABLE_OBFUZ
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
#endif
public partial class FogOfWarUIForm : UIFormBase
{
    [Header("渲染设置")]
    [SerializeField] private RawImage fogRawImage;
    [SerializeField] private bool autoUpdate = true;
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;

    private FogOfWarManager fogManager;
    private Texture2D fogTexture;
    private Color[] currentColors;
    private Color[] targetColors;
    private FogOfWarGrid fogGrid;
    private FogOfWarConfig config;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        // 自动查找或创建 RawImage
        if (fogRawImage == null)
        {
            fogRawImage = GetComponentInChildren<RawImage>();
            if (fogRawImage == null)
            {
                Log.Error("[FogOfWarUI] RawImage not found! Please add a RawImage component.");
                return;
            }
        }

        Log.Info("[FogOfWarUI] FogOfWarUIForm initialized");
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        // 获取 FogOfWarManager（从场景中查找）
        fogManager = FindObjectOfType<FogOfWarManager>();
        if (fogManager == null)
        {
            Log.Error("[FogOfWarUI] FogOfWarManager not found in scene! Please add a GameObject named 'FogOfWarManager' with FogOfWarManager component.");
            return;
        }

        fogGrid = fogManager.Grid;
        config = fogManager.Config;

        // 初始化纹理
        InitializeTexture();

        // 订阅视野更新事件
        fogManager.OnVisionUpdated += OnVisionUpdated;

        Log.Info("[FogOfWarUI] FogOfWarUIForm opened and ready");
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        base.OnClose(isShutdown, userData);

        // 取消订阅
        if (fogManager != null)
        {
            fogManager.OnVisionUpdated -= OnVisionUpdated;
        }

        // 清理纹理
        if (fogTexture != null)
        {
            Destroy(fogTexture);
            fogTexture = null;
        }

        Log.Info("[FogOfWarUI] FogOfWarUIForm closed");
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

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

        fogRawImage.texture = fogTexture;

        Log.Info($"[FogOfWarUI] Texture initialized: {width}x{height}");
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
        if (fogTexture == null || currentColors == null || targetColors == null)
            return;

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

        Log.Info("[FogOfWarUI] Texture force refreshed");
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

    protected override void OnRecycle()
    {
        base.OnRecycle();

        // 清理资源
        if (fogTexture != null)
        {
            Destroy(fogTexture);
            fogTexture = null;
        }

        currentColors = null;
        targetColors = null;
    }
}