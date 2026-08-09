using GameFramework.Event;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 血条组件：完全独立，不挂在单位 prefab 上。
/// 由外部（Procedure）在运行时创建，传入 entityId 和要跟随的 Transform。
/// 通过 GF.Event 监听 CreatureHealthChangedEventArgs，按 entityId 过滤。
/// </summary>
public class HealthBarComp : MonoBehaviour
{
    private const string HealthBarShaderName = "AAAGame/UI/HealthBarAlwaysVisible";
    private const float UnitBarWidth = 120f;
    private const float UnitBarHeight = 20f;
    private const float BuildingMinBarWidth = 170f;
    private const float BuildingMaxBarWidth = 320f;
    private const float CameraBiasDistance = 0.35f;
    private const float AmmoBarHeight = 5f;
    private const float AmmoBarGap = 1.5f;
    private static readonly Color AmmoFilledColor = new Color(1f, 0.28f, 0.08f, 0.95f);
    private static readonly Color AmmoEmptyColor = new Color(0.35f, 0.35f, 0.35f, 0.85f);
    private static readonly Dictionary<int, HealthBarComp> ActiveBars = new Dictionary<int, HealthBarComp>();
    private static Material s_healthBarMaterial;
    private static int s_fogVisibleCalls;
    private static int s_fogVisibleCacheHits;
    private static int s_fogVisibleMissing;
    private static int s_fogVisibleNoOps;
    private static int s_fogVisibleCanvasWrites;
    private static long s_fogVisibleInternalTicks;

    [SerializeField] private RectTransform fillRect;
    [SerializeField] private Image fillImage;
    [SerializeField] private Canvas ownerCanvas;
    [SerializeField] private RectTransform ammoRoot;

    private int _entityId;
    private Transform _followTarget;
    private Transform _positionTarget;
    private Vector3 _offset = new Vector3(0, 2f, 0);
    private bool _subscribed;
    private bool _pendingDestroy;
    private bool _isFriendly;
    private bool _visibleByFog = true;
    private readonly List<Image> _ammoSegments = new List<Image>();

    /// <summary>
    /// 由外部调用初始化。
    /// </summary>
    /// <param name="entityId">要监听的单位 Id</param>
    /// <param name="followTarget">要跟随的 Transform（单位的 transform）</param>
    /// <param name="currentHealth">当前血量</param>
    /// <param name="maxHealth">最大血量</param>
    /// <param name="offset">血条相对于单位的偏移</param>
    public void Init(int entityId, Transform followTarget, float currentHealth, float maxHealth, Vector3? offset = null)
    {
        _entityId = entityId;
        _followTarget = followTarget;
        _positionTarget = ResolvePositionTarget(followTarget);
        if (offset.HasValue) _offset = offset.Value;
        if (ownerCanvas == null)
            ownerCanvas = GetComponent<Canvas>();

        _visibleByFog = true;
        if (ownerCanvas != null)
            ownerCanvas.enabled = true;

        _isFriendly = ResolveIsFriendlyFromTarget(followTarget, _isFriendly);
        UpdateFillColor();

        UpdateBar(currentHealth, maxHealth);

        if (!_subscribed)
        {
            GF.Event.Subscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
            GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnFactionChanged);
            GF.Event.Subscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete);
            _subscribed = true;
        }
    }

    private void OnHealthChanged(object sender, GameEventArgs e)
    {
        var args = (CreatureHealthChangedEventArgs)e;
        if (args.EntityId != _entityId) return;

        UpdateBar(args.CurrentHealth, args.MaxHealth);

        if (args.CurrentHealth <= 0)
        {
            _pendingDestroy = !ShouldKeepAliveAtZeroHealth();
        }
        else
        {
            _pendingDestroy = false;
        }
    }

    private bool ShouldKeepAliveAtZeroHealth()
    {
        if (_followTarget == null)
            return false;

        // 建筑是可失能后恢复的，不在 0 血时立即销毁血条。
        return _followTarget.GetComponent<BuildingEntity>() != null;
    }

    private void UpdateBar(float current, float max)
    {
        if (fillRect == null) return;

        float ratio = ComputeRatio(current, max);
        // 通过 anchorMax.x 控制填充宽度，不依赖 sprite
        fillRect.anchorMax = new Vector2(ratio, 1f);
    }

    private static float ComputeRatio(float current, float max)
    {
        float effectiveMax = max > 0f ? max : 1f;
        return Mathf.Clamp01(current / effectiveMax);
    }

    private void LateUpdate()
    {
        if (_pendingDestroy)
        {
            Remove();
            return;
        }

        if (!_visibleByFog)
        {
            if (ownerCanvas != null && ownerCanvas.enabled)
                ownerCanvas.enabled = false;

            return;
        }

        if (ownerCanvas != null && !ownerCanvas.enabled)
            ownerCanvas.enabled = true;

        if (_followTarget == null) return;
        if (_positionTarget == null)
            throw new System.InvalidOperationException($"HealthBar position target is missing. entityId={_entityId}.");
        UpdateAmmoBar();
        Vector3 worldPos = _positionTarget.position + _offset;

        // 始终面向主摄像机
        if (Camera.main != null)
        {
            var cameraTransform = Camera.main.transform;
            transform.forward = cameraTransform.forward;
            worldPos -= cameraTransform.forward * CameraBiasDistance;
        }

        transform.position = worldPos;
    }

    private void OnDestroy()
    {
        if (ActiveBars.TryGetValue(_entityId, out HealthBarComp value) && value == this)
            ActiveBars.Remove(_entityId);

        if (_subscribed)
        {
            try
            {
                GF.Event.Unsubscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
                GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnFactionChanged);
                GF.Event.Unsubscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete);
            }
            catch (System.Exception) { /* GF 可能已销毁（退出 Play Mode 等） */ }
            _subscribed = false;
        }
    }

    private void OnHideEntityComplete(object sender, GameEventArgs e)
    {
        var args = e as HideEntityCompleteEventArgs;
        if (args == null || args.EntityId != _entityId)
            return;

        Remove();
    }

    private void OnFactionChanged(object sender, GameEventArgs e)
    {
        var args = e as EntityFactionChangedEventArgs;
        if (args == null || args.EntityId != _entityId)
            return;

        _isFriendly = args.NewFactionId == EntitySideHelper.PlayerFactionId;
        UpdateFillColor();
    }

    /// <summary>
    /// 运行时创建血条的工厂方法。调用方只需一行代码。
    /// </summary>
    public static HealthBarComp Create(int entityId, Transform followTarget, float curHp, float maxHp, bool isFriendly = true)
    {
        if (ShouldSuppressHealthBar(followTarget))
        {
            return null;
        }

        Material healthBarMaterial = GetHealthBarMaterial();
        if (healthBarMaterial == null)
            return null;

        bool isBuilding = followTarget != null && followTarget.GetComponent<BuildingEntity>() != null;
        ResolveVisualBounds(followTarget, out bool hasBounds, out Bounds bounds);

        float topOffset = 1.6f;
        if (hasBounds && followTarget != null)
            topOffset = Mathf.Max(0.8f, bounds.max.y - followTarget.position.y);

        float horizontalSize = hasBounds ? Mathf.Max(bounds.size.x, bounds.size.z) : 1f;

        float barWidth = UnitBarWidth;
        if (isBuilding)
            barWidth = Mathf.Clamp(UnitBarWidth + horizontalSize * 28f, BuildingMinBarWidth, BuildingMaxBarWidth);

        float barHeight = UnitBarHeight;
        Vector3 offset = new Vector3(0f, topOffset + (isBuilding ? 0.85f : 0.45f), 0f);

        // Canvas (World Space)
        var go = new GameObject($"HealthBar_{entityId}");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(barWidth, barHeight);
        rt.localScale = Vector3.one * 0.005f;

        // 背景
        var bgGo = new GameObject("BG");
        bgGo.transform.SetParent(go.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.material = healthBarMaterial;
        bgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        // 填充（用 anchor 缩放，不依赖 sprite）
        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(go.transform, false);
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.material = healthBarMaterial;
        // 根据阵营设置颜色：己方绿色，敌方红色
        fillImg.color = isFriendly ? Color.green : Color.red;
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        // HealthBarComp
        var comp = go.AddComponent<HealthBarComp>();
        comp.fillRect = fillRt;
        comp.fillImage = fillImg;
        comp.ownerCanvas = canvas;
        comp._isFriendly = isFriendly;
        comp.CreateAmmoBarIfNeeded(followTarget, barWidth);
        comp.Init(entityId, followTarget, curHp, maxHp, offset);
        ActiveBars[entityId] = comp;

        return comp;
    }

    private static Material GetHealthBarMaterial()
    {
        if (s_healthBarMaterial != null)
            return s_healthBarMaterial;

        Shader shader = Shader.Find(HealthBarShaderName);
        if (shader == null)
        {
            Debug.LogError($"[HealthBar] Required shader not found: {HealthBarShaderName}");
            return null;
        }

        s_healthBarMaterial = new Material(shader)
        {
            name = "HealthBarAlwaysVisible (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_healthBarMaterial;
    }

    private void Remove()
    {
        _pendingDestroy = false;

        if (ActiveBars.TryGetValue(_entityId, out HealthBarComp value) && value == this)
            ActiveBars.Remove(_entityId);

        // 先取消订阅再销毁，避免 OnDestroy 时 GF.Event 已清理。
        if (_subscribed)
        {
            try
            {
                GF.Event.Unsubscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
                GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnFactionChanged);
                GF.Event.Unsubscribe(HideEntityCompleteEventArgs.EventId, OnHideEntityComplete);
            }
            catch (System.Exception) { }
            _subscribed = false;
        }

        if (gameObject != null)
        {
            Object.Destroy(gameObject);
        }
    }

    public static void Remove(int entityId)
    {
        if (ActiveBars.TryGetValue(entityId, out HealthBarComp cached) && cached != null)
        {
            cached.Remove();
            return;
        }

        var healthBar = GameObject.Find($"HealthBar_{entityId}");
        if (healthBar != null)
        {
            var comp = healthBar.GetComponent<HealthBarComp>();
            if (comp != null)
            {
                comp.Remove();
            }
            else
            {
                Object.Destroy(healthBar);
            }
        }
    }

    public static void SetFogVisible(int entityId, bool visible)
    {
        s_fogVisibleCalls++;
        if (ActiveBars.TryGetValue(entityId, out HealthBarComp cached) && cached != null)
        {
            s_fogVisibleCacheHits++;
            cached.SetFogVisibleInternal(visible);
            return;
        }

        s_fogVisibleMissing++;
    }

    public static bool IsFogVisibilityApplied(int entityId, bool visible)
    {
        if (!ActiveBars.TryGetValue(entityId, out HealthBarComp cached) || cached == null)
            return true;

        if (cached.ownerCanvas == null)
            cached.ownerCanvas = cached.GetComponent<Canvas>();
        if (cached.ownerCanvas == null)
            throw new System.InvalidOperationException($"HealthBar owner canvas is missing. entityId={entityId}.");

        return cached._visibleByFog == visible && cached.ownerCanvas.enabled == visible;
    }

    public static void ResetFogVisibilityDiagnostics()
    {
        s_fogVisibleCalls = 0;
        s_fogVisibleCacheHits = 0;
        s_fogVisibleMissing = 0;
        s_fogVisibleNoOps = 0;
        s_fogVisibleCanvasWrites = 0;
        s_fogVisibleInternalTicks = 0L;
    }

    public static void ConsumeFogVisibilityDiagnostics(
        out int calls,
        out int cacheHits,
        out int missing,
        out int noOps,
        out int canvasWrites,
        out long internalTicks)
    {
        calls = s_fogVisibleCalls;
        cacheHits = s_fogVisibleCacheHits;
        missing = s_fogVisibleMissing;
        noOps = s_fogVisibleNoOps;
        canvasWrites = s_fogVisibleCanvasWrites;
        internalTicks = s_fogVisibleInternalTicks;
        ResetFogVisibilityDiagnostics();
    }

    public static void ForceUpdateSide(int entityId, bool isFriendly)
    {
        if (ActiveBars.TryGetValue(entityId, out HealthBarComp comp) && comp != null)
        {
            comp._isFriendly = isFriendly;
            comp.UpdateFillColor();
        }
    }

    private void SetFogVisibleInternal(bool visible)
    {
        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (_visibleByFog == visible && ownerCanvas != null && ownerCanvas.enabled == visible)
            {
                s_fogVisibleNoOps++;
                return;
            }

            _visibleByFog = visible;

            if (ownerCanvas == null)
                ownerCanvas = GetComponent<Canvas>();

            if (ownerCanvas != null && ownerCanvas.enabled != visible)
            {
                ownerCanvas.enabled = visible;
                s_fogVisibleCanvasWrites++;
            }
            else
            {
                s_fogVisibleNoOps++;
            }
        }
        finally
        {
            s_fogVisibleInternalTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;
        }
    }

    private void UpdateFillColor()
    {
        if (fillImage == null)
            return;

        fillImage.color = _isFriendly ? Color.green : Color.red;
    }

    private void CreateAmmoBarIfNeeded(Transform followTarget, float barWidth)
    {
        var entity = followTarget != null ? followTarget.GetComponent<MAEntity>() : null;
        var weaponComp = entity != null ? entity.weaponComp : null;
        if (weaponComp == null || !weaponComp.HasAmmunition)
            return;

        var rootGo = new GameObject("AmmoBar");
        rootGo.transform.SetParent(transform, false);
        ammoRoot = rootGo.AddComponent<RectTransform>();
        ammoRoot.anchorMin = new Vector2(0f, 0f);
        ammoRoot.anchorMax = new Vector2(1f, 0f);
        ammoRoot.pivot = new Vector2(0.5f, 1f);
        ammoRoot.offsetMin = new Vector2(0f, -AmmoBarHeight - 2f);
        ammoRoot.offsetMax = new Vector2(0f, -2f);

        int maxAmmo = weaponComp.MaxAmmo;
        float gap = maxAmmo > 1 ? Mathf.Min(AmmoBarGap, (barWidth / maxAmmo) * 0.35f) : 0f;
        float segmentWidth = Mathf.Max(0.5f, (barWidth - gap * (maxAmmo - 1)) / maxAmmo);
        for (int i = 0; i < maxAmmo; i++)
        {
            var segmentGo = new GameObject($"Ammo_{i + 1}");
            segmentGo.transform.SetParent(rootGo.transform, false);
            var image = segmentGo.AddComponent<Image>();
            image.material = GetHealthBarMaterial();
            image.color = AmmoFilledColor;
            var segmentRt = segmentGo.GetComponent<RectTransform>();
            segmentRt.anchorMin = new Vector2(0f, 0f);
            segmentRt.anchorMax = new Vector2(0f, 1f);
            segmentRt.pivot = new Vector2(0f, 0.5f);

            float x = i * (segmentWidth + gap);
            segmentRt.anchoredPosition = new Vector2(x, 0f);
            segmentRt.sizeDelta = new Vector2(segmentWidth, AmmoBarHeight);
            _ammoSegments.Add(image);
        }

        UpdateAmmoBar();
    }

    private void UpdateAmmoBar()
    {
        if (ammoRoot == null || _ammoSegments.Count == 0 || _followTarget == null)
            return;

        var entity = _followTarget.GetComponent<MAEntity>();
        var weaponComp = entity != null ? entity.weaponComp : null;
        if (weaponComp == null || !weaponComp.HasAmmunition)
        {
            ammoRoot.gameObject.SetActive(false);
            return;
        }

        if (!ammoRoot.gameObject.activeSelf)
            ammoRoot.gameObject.SetActive(true);

        int currentAmmo = weaponComp.CurrentAmmo;
        for (int i = 0; i < _ammoSegments.Count; i++)
        {
            _ammoSegments[i].color = i < currentAmmo ? AmmoFilledColor : AmmoEmptyColor;
        }
    }

    private static bool ResolveIsFriendlyFromTarget(Transform followTarget, bool fallback)
    {
        if (followTarget == null)
            return fallback;

        var building = followTarget.GetComponent<BuildingEntity>();
        if (building != null)
            return building.OwnerFactionID == EntitySideHelper.PlayerFactionId;

        var creature = followTarget.GetComponent<GeneralCreature>();
        if (creature != null)
            return creature.Side == SideType.PlayerSide;

        return fallback;
    }

    private static Transform ResolvePositionTarget(Transform followTarget)
    {
        if (followTarget == null)
            throw new System.ArgumentNullException(nameof(followTarget));

        Transform display = followTarget.Find("Display");
        return display != null ? display : followTarget;
    }

    private static bool ShouldSuppressHealthBar(Transform followTarget)
    {
        if (followTarget == null)
            return true;

        var building = followTarget.GetComponent<BuildingEntity>();
        if (building != null
            && (building.IsLv0Invincible
                || building.IsPermanentlyInvincible
                || building.IsHealthBarSuppressedByBuff))
            return true;

        return false;
    }

    private static void ResolveVisualBounds(Transform followTarget, out bool hasBounds, out Bounds bounds)
    {
        bounds = default;
        hasBounds = false;

        if (followTarget == null)
            return;

        var renderers = followTarget.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
    }
}
