using GameFramework;
using GameFramework.Event;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public class PopTextManager : GameFrameworkComponent
{
    [SerializeField]
    [Tooltip("true: 以屏幕/UI飘字显示，尽量避免被建筑遮挡；false: 使用世界空间飘字。")]
    private bool showCoinPopTextAsUI = true;

    [SerializeField]
    [Tooltip("UI飘字挂载根节点。留空则自动寻找或创建运行时Canvas。")]
    private RectTransform uiPopTextRoot;

    [SerializeField]
    [Tooltip("UI飘字在英雄头顶屏幕坐标基础上的起始偏移。")]
    private Vector2 uiStartOffset = new Vector2(0f, 80f);

    [SerializeField]
    [Tooltip("UI飘字向上移动的像素距离。")]
    private float uiRiseDistance = 80f;

    [SerializeField]
    [Tooltip("UI飘字持续时长。")]
    private float uiDuration = 0.8f;

    [SerializeField]
    [Tooltip("UI飘字字号。")]
    private float uiFontSize = 42f;

    [SerializeField]
    [Tooltip("加钱UI飘字颜色（默认黄色）。")]
    private Color uiCoinGainColor = new Color32(255, 215, 0, 255);

    private readonly Vector3 heroPopOffset = new Vector3(0f, 2.0f, 0f);
    private readonly Vector3 popRiseOffset = new Vector3(0f, 1.5f, 0f);
    private bool eventSubscribed;
    private bool waitingEventReadyLogged;

    private void Start()
    {
        TrySubscribeEvents();
    }

    private void OnEnable()
    {
        TrySubscribeEvents();
    }

    private void Update()
    {
        if (!eventSubscribed)
            TrySubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
    }

    private void TrySubscribeEvents()
    {
        if (eventSubscribed)
            return;

        if (GF.Event == null)
        {
            if (!waitingEventReadyLogged)
            {
                waitingEventReadyLogged = true;
                Debug.Log("[PopTextManager] Waiting for GF.Event to become ready...");
            }

            return;
        }

        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        eventSubscribed = true;
        waitingEventReadyLogged = false;
        Debug.Log("[PopTextManager] Subscribed IngameValueChangedEventArgs.");
    }

    private void UnsubscribeEvents()
    {
        if (!eventSubscribed)
            return;

        if (GF.Event != null)
        {
            try
            {
                GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
            }
            catch (GameFrameworkException)
            {
                // PlayMode 退出时 EventPool 可能已释放，忽略退订异常。
            }
        }

        eventSubscribed = false;
    }

    private void OnIngameValueChanged(object sender, GameEventArgs e)
    {
        if (e is not IngameValueChangedEventArgs args)
            return;

        if (args.DataType != IngameValueType.Coin)
            return;

        int delta = args.Value - args.OldValue;
        if (delta <= 0)
            return;

        ShowCoinGainPopText(delta);
    }

    private void ShowCoinGainPopText(int deltaCoin)
    {
        if (deltaCoin <= 0)
            return;

        if (GF.Entity == null)
            return;

        if (!TryGetHeroHeadPosition(out Vector3 startPos))
        {
            Log.Warning("[PopTextManager] Coin gain pop text skipped: player hero not ready. deltaCoin={0}", deltaCoin);
            return;
        }

        Vector3 endPos = startPos + popRiseOffset;
        string content = $"<sprite name=\"Coin\">+{deltaCoin}";

        // 通过该开关控制 Coin 飘字层级：UI/屏幕飘字通常不会被场景建筑遮挡。
        if (showCoinPopTextAsUI)
        {
            ShowCoinGainUIPopText(startPos, content);
        }
        else
        {
            GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), content, endPos, DamageTextType.Coin);
        }
    }

    private void ShowCoinGainUIPopText(Vector3 worldStartPos, string content)
    {
        if (!TryGetUIPopRoot(out RectTransform root))
        {
            Log.Warning("[PopTextManager] UI pop text skipped: no UI root available.");
            return;
        }

        Camera worldCamera = Camera.main;
        if (worldCamera == null)
        {
            Log.Warning("[PopTextManager] UI pop text skipped: Main Camera missing.");
            return;
        }

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(worldCamera, worldStartPos);
        Camera uiCamera = ResolveUICamera(root);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, uiCamera, out Vector2 localPoint))
        {
            return;
        }

        GameObject textObject = new GameObject("CoinPopTextUI", typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(root, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(360f, 120f);
        textRect.anchoredPosition = localPoint + uiStartOffset;

        TextMeshProUGUI tmp = textObject.GetComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        tmp.richText = true;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = uiFontSize;
        tmp.color = uiCoinGainColor;
        tmp.text = content;

        CanvasGroup canvasGroup = textObject.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;

        Sequence seq = DOTween.Sequence();
        seq.Join(textRect.DOAnchorPosY(textRect.anchoredPosition.y + uiRiseDistance, uiDuration).SetEase(Ease.OutCubic));
        seq.Join(canvasGroup.DOFade(0f, uiDuration));
        seq.OnComplete(() => Destroy(textObject));
        seq.SetAutoKill();
    }

    private bool TryGetUIPopRoot(out RectTransform root)
    {
        if (uiPopTextRoot != null)
        {
            root = uiPopTextRoot;
            return true;
        }

        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (!canvas.isRootCanvas || !canvas.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                uiPopTextRoot = canvas.GetComponent<RectTransform>();
                root = uiPopTextRoot;
                return root != null;
            }
        }

        uiPopTextRoot = CreateRuntimePopCanvas();
        root = uiPopTextRoot;
        return root != null;
    }

    private static Camera ResolveUICamera(RectTransform root)
    {
        Canvas canvas = root != null ? root.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera != null ? canvas.worldCamera : GFBuiltin.UICamera;
    }

    private static RectTransform CreateRuntimePopCanvas()
    {
        GameObject canvasObject = new GameObject("RuntimeCoinPopCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvasObject.GetComponent<RectTransform>();
    }

    private bool TryGetHeroHeadPosition(out Vector3 position)
    {
        position = Vector3.zero;

        if (EntityRegistry.Player is MAEntity playerEntity)
        {
            position = playerEntity.transform.position + heroPopOffset;
            return true;
        }

        if (EntityRegistry.Player != null)
        {
            position = EntityRegistry.Player.Position + heroPopOffset;
            return true;
        }

        return false;
    }
}