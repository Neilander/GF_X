using System.Collections.Generic;
using GameFramework;
using UnityEngine;

/// <summary>
/// 通用可交互宿主：
/// - 存储交互来源 Source
/// - 以 InputKey 作为唯一键创建/持有 IInteractionOption(IReference)
/// - GetOptions 从各 IInteractionOption 生成选项
/// - ResetOptions 统一清理（HideEntity 时由 Entity 调用）
/// </summary>
public class InteractionHost : MonoBehaviour
{
    // 使用 SortedDictionary 保证按 InputKey(enum 值) 的自然顺序枚举
    private readonly SortedDictionary<InputKey, IInteractionOption> _optionsByKey = new();
    private readonly List<IInteractionOption> _options = new();

    public object Owner { get; private set; }

    public Transform Transform => transform;

    public float GetInteractionRadius()
    {
        var colliders = GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
            return 0f;

        bool initialized = false;
        Bounds bounds = default;

        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return initialized ? bounds.extents.magnitude : 0f;
    }

    public bool TryGetClosestDistanceTo(Vector3 worldPosition, out float distance)
    {
        distance = float.PositiveInfinity;

        var colliders = GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
        {
            distance = 0f;
            return false;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            float current = Vector3.Distance(worldPosition, collider.ClosestPoint(worldPosition));
            if (current < distance)
                distance = current;
        }

        if (float.IsPositiveInfinity(distance))
        {
            distance = 0f;
            return false;
        }

        return true;
    }

    public void Init(object owner)
    {
        Owner = owner;
    }

    public bool IsInteractable() => enabled && gameObject.activeInHierarchy && HasVisibleOptions();

    public bool HasVisibleOptions()
    {
        foreach (var option in _optionsByKey.Values)
        {
            if (option != null && option.IsVisible())
                return true;
        }

        foreach (var option in _options)
        {
            if (option != null && option.IsVisible())
                return true;
        }

        return false;
    }

    public void GetOptions(List<IInteractionOption> results)
    {
        if (results == null)
            return;

        foreach (var kv in _optionsByKey)
        {
            var option = kv.Value;
            if (option == null || !option.IsVisible())
                continue;
            // always add option; UI/manager should query host for the key mapping
            results.Add(option);
        }

        foreach (var option in _options)
        {
            if (option == null || !option.IsVisible())
                continue;
            results.Add(option);
        }
    }

    /// <summary>
    /// 同时获取按宿主 key 顺序排列的 options 与对应的 keys（keys 与 results 索引一一对应）。
    /// 优先供 UI 使用，避免在外部进行二次排序或查找。
    /// </summary>
    public void GetOptionsWithKeys(SortedDictionary<InputKey, IInteractionOption> results)
    {
        if (results == null || _optionsByKey.Count == 0)
            return;

        foreach (var kv in _optionsByKey)
        {
            var key = kv.Key;
            var option = kv.Value;
            if (option == null || !option.IsVisible())
                continue;

            results.Add(key, option);
        }
    }

    public Vector3 GetPromptPosition() => transform.position;

    /// <summary>
    /// 有按键的选项
    /// </summary>
    public TInteractionOption AddOption<TInteractionOption>(InputKey key, string displayName, InteractionParams @params)
            where TInteractionOption : class, IInteractionOption, new()
    {
        if (_optionsByKey.ContainsKey(key))
        {
            Debug.LogError($"[Interaction] Duplicate key {key} on {name}.");
            if (@params != null)
            {
                @params.Clear();
                ReferencePool.Release(@params);
            }
            return null;
        }

        var option = ReferencePool.Acquire<TInteractionOption>();
        option.Init(Owner, displayName, @params);

        // params 只用于 Init 时读取，Init 后即可释放，避免变量池残留
        if (@params != null)
        {
            @params.Clear();
            ReferencePool.Release(@params);
        }

        _optionsByKey.Add(key, option);
        return option;
    }
    /// <summary>
    /// 无按键的选项
    /// </summary>
    public TInteractionOption AddOption<TInteractionOption>(string displayName, InteractionParams @params)
        where TInteractionOption : class, IInteractionOption, new()
    {
        var option = ReferencePool.Acquire<TInteractionOption>();
        option.Init(Owner, displayName, @params);

        if (@params != null)
        {
            @params.Clear();
            ReferencePool.Release(@params);
        }

        _options.Add(option);
        return option;
    }

    public void ResetOptions()
    {
        foreach (var kv in _optionsByKey)
        {
            var option = kv.Value;
            if (option != null)
            {
                ReferencePool.Release(option);
            }
        }

        foreach (var option in _options)
        {
            if (option != null)
            {
                ReferencePool.Release(option);
            }
        }

        _optionsByKey.Clear();
        _options.Clear();
        Owner = null;
    }

    public bool TryExecute(InputKey key)
    {
        if (!CanExecute(key))
            return false;

        IInteractionOption option = _optionsByKey[key];
        option.Execute();
        GF.Event.Fire(this, InteractionOptionTriggeredEventArgs.Create(this, option));
        return true;
    }

    public bool CanExecute(InputKey key)
    {
        return _optionsByKey.TryGetValue(key, out IInteractionOption option)
               && option != null
               && option.IsVisible()
               && option.IsExecutable();
    }

    public bool TryExecute(IInteractionOption option)
    {
        if (option == null)
            return false;

        if (!option.IsVisible() || !option.IsExecutable())
            return false;

        option.Execute();
        GF.Event.Fire(this, InteractionOptionTriggeredEventArgs.Create(this, option));
        return true;
    }
}
