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
    private readonly SortedDictionary<InputKey, IInteractionOption> _options = new();

    public object Owner { get; private set; }

    public Transform Transform => transform;

    public void Init(object owner)
    {
        Owner = owner;
    }

    public bool IsInteractable() => enabled && gameObject.activeInHierarchy && _options.Count > 0;

    public void GetOptions(List<IInteractionOption> results)
    {
        if (results == null || _options.Count == 0)
            return;

        foreach (var kv in _options)
        {
            var option = kv.Value;
            if (option == null || !option.IsAvailable())
                continue;
            // always add option; UI/manager should query host for the key mapping
            results.Add(option);
        }
    }

    /// <summary>
    /// 同时获取按宿主 key 顺序排列的 options 与对应的 keys（keys 与 results 索引一一对应）。
    /// 优先供 UI 使用，避免在外部进行二次排序或查找。
    /// </summary>
    public void GetOptionsWithKeys(SortedDictionary<InputKey, IInteractionOption> results)
    {
        if (results == null || _options.Count == 0)
            return;

        foreach (var kv in _options)
        {
            var key = kv.Key;
            var option = kv.Value;
            if (option == null || !option.IsAvailable())
                continue;

            results.Add(key, option);
        }
    }

    public Vector3 GetPromptPosition() => transform.position;

    public TInteractionOption AddOption<TInteractionOption>(InputKey key, string displayName, InteractionParams @params)
        where TInteractionOption : class, IInteractionOption, new()
    {
        if (_options.ContainsKey(key))
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

        _options.Add(key, option);
        return option;
    }

    public void ResetOptions()
    {
        foreach (var kv in _options)
        {
            var option = kv.Value;
            if (option != null)
            {
                ReferencePool.Release(option);
            }
        }
        _options.Clear();
        Owner = null;
    }

    public bool TryExecute(InputKey key)
    {
        if (!_options.TryGetValue(key, out var option) || option == null)
            return false;

        if (!option.IsExecutable())
            return false;

        option.Execute();
        GF.Event.Fire(this, InteractionOptionTriggeredEventArgs.Create(this, option));
        return true;
    }
}
