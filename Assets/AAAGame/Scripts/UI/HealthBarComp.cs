using GameFramework.Event;
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
    private const float UnitBarWidth = 120f;
    private const float UnitBarHeight = 20f;
    private const float BuildingMinBarWidth = 170f;
    private const float BuildingMaxBarWidth = 320f;
    private const float CameraBiasDistance = 0.35f;

    [SerializeField] private RectTransform fillRect;
    [SerializeField] private Image fillImage;

    private int _entityId;
    private Transform _followTarget;
    private Vector3 _offset = new Vector3(0, 2f, 0);
    private bool _subscribed;
    private bool _pendingDestroy;
    private bool _isFriendly;

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
        if (offset.HasValue) _offset = offset.Value;
        _isFriendly = ResolveIsFriendlyFromTarget(followTarget, _isFriendly);
        UpdateFillColor();

        UpdateBar(currentHealth, maxHealth);

        if (!_subscribed)
        {
            GF.Event.Subscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
            GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnFactionChanged);
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
            // 先取消订阅再销毁，避免 OnDestroy 时 GF.Event 已清理
            if (_subscribed)
            {
                GF.Event.Unsubscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
                _subscribed = false;
            }
            Destroy(gameObject);
            return;
        }

        if (_followTarget == null) return;
        Vector3 worldPos = _followTarget.position + _offset;

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
        if (_subscribed)
        {
            try
            {
                GF.Event.Unsubscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
                GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnFactionChanged);
            }
            catch (System.Exception) { /* GF 可能已销毁（退出 Play Mode 等） */ }
            _subscribed = false;
        }
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
        comp._isFriendly = isFriendly;
        comp.Init(entityId, followTarget, curHp, maxHp, offset);

        return comp;
    }

    private void UpdateFillColor()
    {
        if (fillImage == null)
            return;

        fillImage.color = _isFriendly ? Color.green : Color.red;
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

    private static bool ShouldSuppressHealthBar(Transform followTarget)
    {
        if (followTarget == null)
            return true;

        var building = followTarget.GetComponent<BuildingEntity>();
        if (building != null && building.IsAlwaysInvincible)
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
