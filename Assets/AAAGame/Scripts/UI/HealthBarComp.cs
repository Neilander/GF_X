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
    [SerializeField] private RectTransform fillRect;

    private int _entityId;
    private Transform _followTarget;
    private Vector3 _offset = new Vector3(0, 2f, 0);
    private bool _subscribed;
    private bool _pendingDestroy;

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

        UpdateBar(currentHealth, maxHealth);

        if (!_subscribed)
        {
            GF.Event.Subscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
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
            _pendingDestroy = true;
        }
    }

    private void UpdateBar(float current, float max)
    {
        if (fillRect == null) return;
        float ratio = max > 0 ? Mathf.Clamp01(current / max) : 0f;
        // 通过 anchorMax.x 控制填充宽度，不依赖 sprite
        fillRect.anchorMax = new Vector2(ratio, 1f);
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
        transform.position = _followTarget.position + _offset;

        // 始终面向主摄像机
        if (Camera.main != null)
            transform.forward = Camera.main.transform.forward;
    }

    private void OnDestroy()
    {
        if (_subscribed)
        {
            try
            {
                GF.Event.Unsubscribe(CreatureHealthChangedEventArgs.EventId, OnHealthChanged);
            }
            catch (System.Exception) { /* GF 可能已销毁（退出 Play Mode 等） */ }
            _subscribed = false;
        }
    }

    /// <summary>
    /// 运行时创建血条的工厂方法。调用方只需一行代码。
    /// </summary>
    public static HealthBarComp Create(int entityId, Transform followTarget, float curHp, float maxHp)
    {
        // Canvas (World Space)
        var go = new GameObject($"HealthBar_{entityId}");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(120f, 20f);
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
        fillImg.color = Color.green;
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        // HealthBarComp
        var comp = go.AddComponent<HealthBarComp>();
        comp.fillRect = fillRt;
        comp.Init(entityId, followTarget, curHp, maxHp);

        return comp;
    }
}
