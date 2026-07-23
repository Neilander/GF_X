using System.Collections.Generic;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

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
    private readonly SortedDictionary<InputKey, List<IInteractionOption>> _optionsByKey = new();
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

    public void ConfigureFromLogicState(BuildingEntity owner)
    {
        if (owner == null)
            throw new System.ArgumentNullException(nameof(owner));
        if (owner.LogicState == null || !owner.LogicState.IsBuildingEntity)
            throw new System.InvalidOperationException("InteractionHost requires a configured logic building state.");

        IReadOnlyList<LogicInteractionOptionDescriptor> descriptors = owner.LogicState.InteractionOptions;
        for (int i = 0; i < descriptors.Count; i++)
            AddDescriptorOption(descriptors[i]);
    }

    private void AddDescriptorOption(LogicInteractionOptionDescriptor descriptor)
    {
        InteractionParams @params = InteractionParams.Create();
        switch (descriptor.Kind)
        {
            case LogicInteractionOptionKind.ConstructBuilding:
            {
                BuildingData buildingData = BuildingDataModel.GetBuildingData(descriptor.PrimaryId)
                                            ?? throw new System.InvalidOperationException($"Construct interaction building '{descriptor.PrimaryId}' is missing.");
                @params.Set<VarString>("BuildBuildingId", descriptor.PrimaryId);
                string displayName = !string.IsNullOrWhiteSpace(buildingData.NameKey)
                    ? LocalizationTextManager.GetLocalizedText(buildingData.NameKey, false)
                    : buildingData.Identifier;
                AddOption<BuildingConstructInteractionOption>(descriptor, displayName, @params);
                break;
            }
            case LogicInteractionOptionKind.UpgradeBuilding:
            {
                TechData techData = TechDataModel.GetTechData(descriptor.SecondaryId)
                                    ?? throw new System.InvalidOperationException($"Upgrade interaction tech '{descriptor.SecondaryId}' is missing.");
                @params.Set<VarString>("UpgradeBuildingId", descriptor.PrimaryId);
                @params.Set<VarString>("TechId", descriptor.SecondaryId);
                AddOption<BuildingUpgradeInteractionOption>(
                    descriptor,
                    LocalizationTextManager.GetLocalizedText(techData.NameKey, false),
                    @params);
                break;
            }
            case LogicInteractionOptionKind.ResearchTech:
            {
                TechData techData = TechDataModel.GetTechData(descriptor.PrimaryId)
                                    ?? throw new System.InvalidOperationException($"Research interaction tech '{descriptor.PrimaryId}' is missing.");
                @params.Set<VarString>("TechId", descriptor.PrimaryId);
                AddOption<TechResearchInteractionOption>(
                    descriptor,
                    LocalizationTextManager.GetLocalizedText(techData.NameKey, false),
                    @params);
                break;
            }
            case LogicInteractionOptionKind.BuildingInfo:
                AddOption<BuildingInfoInteractionOption>(
                    descriptor,
                    LocalizationTextDataModel.GetText("InteractOption_Check"),
                    @params);
                break;
            default:
                @params.Clear();
                ReferencePool.Release(@params);
                throw new System.ArgumentOutOfRangeException(nameof(descriptor.Kind), descriptor.Kind, "Unknown interaction option kind.");
        }
    }

    private void AddOption<TInteractionOption>(
        LogicInteractionOptionDescriptor descriptor,
        string displayName,
        InteractionParams @params)
        where TInteractionOption : class, IInteractionOption, new()
    {
        if (descriptor.HasInputKey)
            AddOption<TInteractionOption>(descriptor.InputKey, displayName, @params);
        else
            AddOption<TInteractionOption>(displayName, @params);
    }

    public bool IsInteractable() => enabled && gameObject.activeInHierarchy && HasVisibleOptions();

    public bool HasVisibleOptions()
    {
        foreach (var pair in _optionsByKey)
        {
            List<IInteractionOption> options = pair.Value;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] != null && options[i].IsVisible())
                    return true;
            }
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

        foreach (var pair in _optionsByKey)
        {
            IInteractionOption option = ResolveVisibleKeyOption(pair.Key);
            if (option != null)
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

        foreach (var pair in _optionsByKey)
        {
            IInteractionOption option = ResolveVisibleKeyOption(pair.Key);
            if (option != null)
                results.Add(pair.Key, option);
        }
    }

    public Vector3 GetPromptPosition() => transform.position;

    /// <summary>
    /// 有按键的选项
    /// </summary>
    public TInteractionOption AddOption<TInteractionOption>(InputKey key, string displayName, InteractionParams @params)
            where TInteractionOption : class, IInteractionOption, new()
    {
        var option = ReferencePool.Acquire<TInteractionOption>();
        option.Init(Owner, displayName, @params);

        // params 只用于 Init 时读取，Init 后即可释放，避免变量池残留
        if (@params != null)
        {
            @params.Clear();
            ReferencePool.Release(@params);
        }

        if (!_optionsByKey.TryGetValue(key, out List<IInteractionOption> options))
        {
            options = new List<IInteractionOption>();
            _optionsByKey.Add(key, options);
        }
        options.Add(option);
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
        foreach (var pair in _optionsByKey)
        {
            List<IInteractionOption> options = pair.Value;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] != null)
                    ReferencePool.Release(options[i]);
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

        IInteractionOption option = ResolveVisibleKeyOption(key);
        if (option == null)
            return false;
        option.Execute();
        GF.Event.Fire(this, InteractionOptionTriggeredEventArgs.Create(this, option));
        return true;
    }

    public bool CanExecute(InputKey key)
    {
        IInteractionOption option = ResolveVisibleKeyOption(key);
        return option != null && option.IsExecutable();
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

    private IInteractionOption ResolveVisibleKeyOption(InputKey key)
    {
        if (!_optionsByKey.TryGetValue(key, out List<IInteractionOption> options))
            return null;

        IInteractionOption result = null;
        for (int i = 0; i < options.Count; i++)
        {
            IInteractionOption option = options[i];
            if (option == null || !option.IsVisible())
                continue;
            if (result != null)
                throw new System.InvalidOperationException($"InteractionHost '{name}' has multiple visible options for {key}.");
            result = option;
        }
        return result;
    }
}
