using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.All)]
public class GFBuiltin : MonoBehaviour
{
    private const float DesignAspectRatio = 16f / 9f;
    private const int UIRendererIndex = 1;

    public static GFBuiltin Instance { get; private set; }
    public static BaseComponent Base { get; private set; }
    public static ConfigComponent Config { get; private set; }
    public static DataNodeComponent DataNode { get; private set; }
    public static DataTableComponent DataTable { get; private set; }
    public static DebuggerComponent Debugger { get; private set; }
    public static DownloadComponent Download { get; private set; }
    public static EntityComponent Entity { get; private set; }
    public static EventComponent Event { get; private set; }
    public static FsmComponent Fsm { get; private set; }
    public static FileSystemComponent FileSystem { get; private set; }
    public static LocalizationComponent Localization { get; private set; }
    public static NetworkComponent Network { get; private set; }
    public static ProcedureComponent Procedure { get; private set; }
    public static ResourceComponent Resource { get; private set; }
    public static SceneComponent Scene { get; private set; }
    public static SettingComponent Setting { get; private set; }
    public static SoundComponent Sound { get; private set; }
    public static UIComponent UI { get; private set; }
    public static ObjectPoolComponent ObjectPool { get; private set; }
    public static WebRequestComponent WebRequest { get; private set; }
    public static BuiltinViewComponent BuiltinView { get; private set; }
    public static Camera UICamera { get; private set; }

    public static Canvas RootCanvas { get; private set; } = null;
    public static Rect DesignViewportRect { get; private set; } = new Rect(0f, 0f, 1f, 1f);

    private int m_LastScreenWidth = -1;
    private int m_LastScreenHeight = -1;
    private Canvas m_AspectBarsCanvas;
    private RectTransform m_LeftBar;
    private RectTransform m_RightBar;
    private RectTransform m_TopBar;
    private RectTransform m_BottomBar;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            var resCom = GameEntry.GetComponent<ResourceComponent>();
            if (resCom != null)
            {
                var resTp = resCom.GetType();
                var m_ResourceMode = resTp.GetField("m_ResourceMode", BindingFlags.Instance | BindingFlags.NonPublic);
                m_ResourceMode.SetValue(resCom, AppSettings.Instance.ResourceMode);
                GFBuiltin.Log($"------------Set ResourceMode:{AppSettings.Instance.ResourceMode}------------");
            }
        }
    }

    private void Start()
    {
        GFBuiltin.Base = GameEntry.GetComponent<BaseComponent>();
        GFBuiltin.Config = GameEntry.GetComponent<ConfigComponent>();
        GFBuiltin.DataNode = GameEntry.GetComponent<DataNodeComponent>();
        GFBuiltin.DataTable = GameEntry.GetComponent<DataTableComponent>();
        GFBuiltin.Debugger = GameEntry.GetComponent<DebuggerComponent>();
        GFBuiltin.Download = GameEntry.GetComponent<DownloadComponent>();
        GFBuiltin.Entity = GameEntry.GetComponent<EntityComponent>();
        GFBuiltin.Event = GameEntry.GetComponent<EventComponent>();
        GFBuiltin.Fsm = GameEntry.GetComponent<FsmComponent>();
        GFBuiltin.Procedure = GameEntry.GetComponent<ProcedureComponent>();
        GFBuiltin.Localization = GameEntry.GetComponent<LocalizationComponent>();
        GFBuiltin.Network = GameEntry.GetComponent<NetworkComponent>();
        GFBuiltin.Resource = GameEntry.GetComponent<ResourceComponent>();
        GFBuiltin.FileSystem = GameEntry.GetComponent<FileSystemComponent>();
        GFBuiltin.Scene = GameEntry.GetComponent<SceneComponent>();
        GFBuiltin.Setting = GameEntry.GetComponent<SettingComponent>();
        GFBuiltin.Sound = GameEntry.GetComponent<SoundComponent>();
        GFBuiltin.UI = GameEntry.GetComponent<UIComponent>();
        GFBuiltin.ObjectPool = GameEntry.GetComponent<ObjectPoolComponent>();
        GFBuiltin.WebRequest = GameEntry.GetComponent<WebRequestComponent>();
        GFBuiltin.BuiltinView = GameEntry.GetComponent<BuiltinViewComponent>();

        RootCanvas = GFBuiltin.UI.GetComponentInChildren<Canvas>();
        GFBuiltin.UICamera = RootCanvas.worldCamera;

        ConfigureRootCanvas();
        RefreshDesignViewport(true);
        UpdateCanvasScaler();
    }

    private void Update()
    {
        if (Screen.width == m_LastScreenWidth && Screen.height == m_LastScreenHeight)
        {
            return;
        }

        RefreshDesignViewport(false);
        UpdateCanvasScaler();
    }

    public void UpdateCanvasScaler()
    {
        if (RootCanvas == null)
        {
            return;
        }

        CanvasScaler canvasScaler = RootCanvas.GetComponent<CanvasScaler>();
        if (canvasScaler == null)
        {
            return;
        }

        canvasScaler.referenceResolution = AppSettings.Instance.DesignResolution;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0f;
        GFBuiltin.Log($"----------UI适配Match:{canvasScaler.matchWidthOrHeight}----------");
    }

    private void ConfigureRootCanvas()
    {
        if (RootCanvas == null)
        {
            return;
        }

        if (UICamera == null)
        {
            GFBuiltin.LogWarning("UICamera为空，无法启用16:9相机视口UI适配。");
            return;
        }

        RootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        RootCanvas.worldCamera = UICamera;

        ApplyUICameraRenderer(UICamera);
    }

    private static void ApplyUICameraRenderer(Camera camera)
    {
        if (camera == null)
        {
            return;
        }

        var cameraData = camera.GetComponent("UniversalAdditionalCameraData");
        if (cameraData == null)
        {
            return;
        }

        var setRenderer = cameraData.GetType().GetMethod("SetRenderer", BindingFlags.Instance | BindingFlags.Public);
        setRenderer?.Invoke(cameraData, new object[] { UIRendererIndex });
    }

    private void RefreshDesignViewport(bool force)
    {
        int width = Screen.width;
        int height = Screen.height;
        if (!force && width == m_LastScreenWidth && height == m_LastScreenHeight)
        {
            return;
        }

        m_LastScreenWidth = width;
        m_LastScreenHeight = height;
        DesignViewportRect = CalculateDesignViewportRect(width, height);

        ApplyDesignViewport(Camera.main);
        ApplyDesignViewport(UICamera);
        UpdateAspectBars(DesignViewportRect);
    }

    private static Rect CalculateDesignViewportRect(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        float screenAspect = screenWidth / (float)screenHeight;
        if (screenAspect > DesignAspectRatio)
        {
            float width = DesignAspectRatio / screenAspect;
            return new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }

        if (screenAspect < DesignAspectRatio)
        {
            float height = screenAspect / DesignAspectRatio;
            return new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }

        return new Rect(0f, 0f, 1f, 1f);
    }

    public static void ApplyDesignViewport(Camera camera)
    {
        if (camera == null)
        {
            return;
        }

        camera.rect = DesignViewportRect;
    }

    public static Rect GetDesignViewportPixelRect()
    {
        return new Rect(
            DesignViewportRect.x * Screen.width,
            DesignViewportRect.y * Screen.height,
            DesignViewportRect.width * Screen.width,
            DesignViewportRect.height * Screen.height);
    }

    private void UpdateAspectBars(Rect viewport)
    {
        EnsureAspectBars();

        SetBar(m_LeftBar, new Vector2(0f, viewport.yMin), new Vector2(viewport.xMin, viewport.yMax));
        SetBar(m_RightBar, new Vector2(viewport.xMax, viewport.yMin), new Vector2(1f, viewport.yMax));
        SetBar(m_BottomBar, new Vector2(0f, 0f), new Vector2(1f, viewport.yMin));
        SetBar(m_TopBar, new Vector2(0f, viewport.yMax), new Vector2(1f, 1f));
    }

    private void EnsureAspectBars()
    {
        if (m_AspectBarsCanvas != null)
        {
            return;
        }

        var barsRoot = new GameObject("AspectBlackBarsCanvas", typeof(Canvas));
        barsRoot.layer = LayerMask.NameToLayer("UI");
        barsRoot.transform.SetParent(transform, false);

        m_AspectBarsCanvas = barsRoot.GetComponent<Canvas>();
        m_AspectBarsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_AspectBarsCanvas.sortingOrder = short.MaxValue;

        m_LeftBar = CreateAspectBar("LeftBar", barsRoot.transform);
        m_RightBar = CreateAspectBar("RightBar", barsRoot.transform);
        m_TopBar = CreateAspectBar("TopBar", barsRoot.transform);
        m_BottomBar = CreateAspectBar("BottomBar", barsRoot.transform);
    }

    private static RectTransform CreateAspectBar(string name, Transform parent)
    {
        var bar = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bar.layer = LayerMask.NameToLayer("UI");
        bar.transform.SetParent(parent, false);

        var image = bar.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        var rectTransform = bar.GetComponent<RectTransform>();
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        return rectTransform;
    }

    private static void SetBar(RectTransform bar, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (bar == null)
        {
            return;
        }

        bool visible = anchorMax.x - anchorMin.x > 0.0001f && anchorMax.y - anchorMin.y > 0.0001f;
        bar.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        bar.anchorMin = anchorMin;
        bar.anchorMax = anchorMax;
        bar.offsetMin = Vector2.zero;
        bar.offsetMax = Vector2.zero;
    }


    /// <summary>
    /// 退出或重启
    /// </summary>
    /// <param name="type"></param>
    public static void Shutdown(ShutdownType type)
    {
        GameEntry.Shutdown(type);
    }

    public static void Log(string format)
    {
        var colorfulFormat = $"<color=#2BD988>{format}</color>";
        Debug.Log(colorfulFormat);
    }
    public static void LogWarning(string format)
    {
        var colorfulFormat = $"<color=#F2A20C>{format}</color>";
        Debug.LogWarning(colorfulFormat);
    }
    public static void LogError(string format)
    {
        var colorfulFormat = $"<color=#F22E2E>{format}</color>";
        Debug.LogError(colorfulFormat);
    }
}
