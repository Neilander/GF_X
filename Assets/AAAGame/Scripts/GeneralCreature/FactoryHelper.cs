using System.Collections;
using System.Collections.Generic;
using AAAGame.Scripts.GeneralCreature;
using UnityEngine;
using GameFramework.Resource;

public static class FactoryHelper
{
    static Dictionary<string, AtkCompFactory> _atkFactories = new();
    static Dictionary<string, MoveCompFactory> _moveFactories = new();
    static Dictionary<string, SkillCompFactory> _skillFactories = new();
    
    // 新增：TargetingCompFactory 的缓存字典
    static Dictionary<string, TargetingCompFactory> _targetingFactories = new();

    public static void CreateAtkComp(string factoryPath, IEntityContext entity)
    {
        if (_atkFactories.TryGetValue(factoryPath, out var factory))
        {
            factory.CreateAtkComp(entity);
            return;
        }

        var wrappedCallback = WrapWithCache(
            factoryPath,
            AtkCompFactory.AtkFactoryCallBack,
            _atkFactories
        );

        GF.Resource.LoadAsset(factoryPath, wrappedCallback, entity);
    }

    public static void CreateMoveComp(string factoryPath, IEntityContext entity)
    {
        if (_moveFactories.TryGetValue(factoryPath, out var factory))
        {
            factory.CreateMoveComp(entity);
            return;
        }

        var wrappedCallback = WrapWithCache(
            factoryPath,
            MoveCompFactory.MoveFactoryCallBack,
            _moveFactories
        );

        GF.Resource.LoadAsset(factoryPath, wrappedCallback, entity);
    }
    
    public static void CreateSkillComp(string factoryPath, IEntityContext entity)
    {
        if (_skillFactories.TryGetValue(factoryPath, out var factory))
        {
            factory.CreateSkillComp(entity);
            return;
        }

        var wrappedCallback = WrapWithCache(
            factoryPath,
            SkillCompFactory.SkillFactoryCallBack,
            _skillFactories
        );

        GF.Resource.LoadAsset(factoryPath, wrappedCallback, entity);
    }

    public static ISkillComp CreatePreloadedSkillComp(string factoryPath, IEntityContext entity)
    {
        if (string.IsNullOrWhiteSpace(factoryPath))
            throw new System.ArgumentException("Skill factory path is empty.", nameof(factoryPath));
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));
        if (!_skillFactories.TryGetValue(factoryPath, out SkillCompFactory factory) || factory == null)
        {
            throw new System.InvalidOperationException(
                $"FactoryHelper.CreatePreloadedSkillComp failed: skill factory was not preloaded. path={factoryPath}, entity={entity.LogicEntityId.Value}.");
        }

        return factory.CreateSkillComp(entity)
               ?? throw new System.InvalidOperationException($"Skill factory returned null. path={factoryPath}, entity={entity.LogicEntityId.Value}.");
    }

    public static IMoveComp CreatePreloadedMoveComp(string factoryPath, IEntityContext entity)
    {
        return CreatePreloadedComponent(
            factoryPath,
            entity,
            _moveFactories,
            factory => factory.CreateMoveComp(entity),
            "move");
    }

    public static IAtkComp CreatePreloadedAtkComp(string factoryPath, IEntityContext entity)
    {
        return CreatePreloadedComponent(
            factoryPath,
            entity,
            _atkFactories,
            factory => factory.CreateAtkComp(entity),
            "attack");
    }

    public static ITargetingComp CreatePreloadedTargetingComp(string factoryPath, IEntityContext entity)
    {
        return CreatePreloadedComponent(
            factoryPath,
            entity,
            _targetingFactories,
            factory => factory.CreateTargetingComp(entity),
            "targeting");
    }

    public static void PreloadSkillFactory(
        string factoryPath,
        System.Action<string> onLoaded,
        System.Action<string, LoadResourceStatus, string> onFailed)
    {
        if (string.IsNullOrWhiteSpace(factoryPath))
            throw new System.ArgumentException("Skill factory path is empty.", nameof(factoryPath));
        if (onLoaded == null)
            throw new System.ArgumentNullException(nameof(onLoaded));
        if (onFailed == null)
            throw new System.ArgumentNullException(nameof(onFailed));

        if (_skillFactories.ContainsKey(factoryPath))
        {
            onLoaded(factoryPath);
            return;
        }

        GF.Resource.LoadAsset(
            factoryPath,
            new LoadAssetCallbacks(
                (assetName, asset, duration, userData) =>
                {
                    if (asset is not SkillCompFactory factory)
                        throw new System.InvalidOperationException($"Loaded skill factory has invalid type. path={factoryPath}, asset={assetName}.");
                    _skillFactories.Add(factoryPath, factory);
                    onLoaded(assetName);
                },
                (assetName, status, errorMessage, userData) => onFailed(assetName, status, errorMessage)));
    }

    public static void PreloadMoveFactory(
        string factoryPath,
        System.Action<string> onLoaded,
        System.Action<string, LoadResourceStatus, string> onFailed)
    {
        PreloadFactory(factoryPath, onLoaded, onFailed, _moveFactories, "move");
    }

    public static void PreloadAtkFactory(
        string factoryPath,
        System.Action<string> onLoaded,
        System.Action<string, LoadResourceStatus, string> onFailed)
    {
        PreloadFactory(factoryPath, onLoaded, onFailed, _atkFactories, "attack");
    }

    public static void PreloadTargetingFactory(
        string factoryPath,
        System.Action<string> onLoaded,
        System.Action<string, LoadResourceStatus, string> onFailed)
    {
        PreloadFactory(factoryPath, onLoaded, onFailed, _targetingFactories, "targeting");
    }

    // 新增：创建 TargetingComp 的方法
    public static void CreateTargetingComp(string factoryPath, IEntityContext entity)
    {
        if (_targetingFactories.TryGetValue(factoryPath, out var factory))
        {
            factory.CreateTargetingComp(entity);
            return;
        }

        var wrappedCallback = WrapWithCache(
            factoryPath,
            TargetingCompFactory.TargetingFactoryCallBack,
            _targetingFactories
        );

        GF.Resource.LoadAsset(factoryPath, wrappedCallback, entity);
    }
    
    static LoadAssetCallbacks WrapWithCache<TFactory>(
        string path,
        LoadAssetCallbacks original,
        Dictionary<string, TFactory> cache
    ) where TFactory : class
    {
        return new LoadAssetCallbacks(
            (assetName, asset, duration, userData) =>
            {
                // 1. 先缓存 Factory
                if (asset is TFactory factory && !cache.ContainsKey(path))
                {
                    cache[path] = factory;
                }

                // 2. 转发原 success callback
                original.LoadAssetSuccessCallback?.Invoke(
                    assetName, asset, duration, userData
                );
            },
            original.LoadAssetFailureCallback,
            original.LoadAssetUpdateCallback,
            original.LoadAssetDependencyAssetCallback
        );
    }

    private static TComponent CreatePreloadedComponent<TFactory, TComponent>(
        string factoryPath,
        IEntityContext entity,
        Dictionary<string, TFactory> cache,
        System.Func<TFactory, TComponent> create,
        string componentName)
        where TFactory : class
        where TComponent : class
    {
        if (string.IsNullOrWhiteSpace(factoryPath))
            throw new System.ArgumentException($"{componentName} factory path is empty.", nameof(factoryPath));
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));
        if (!cache.TryGetValue(factoryPath, out TFactory factory) || factory == null)
        {
            throw new System.InvalidOperationException(
                $"FactoryHelper failed: {componentName} factory was not preloaded. path={factoryPath}, entity={entity.LogicEntityId.Value}.");
        }

        return create(factory)
               ?? throw new System.InvalidOperationException(
                   $"{componentName} factory returned null. path={factoryPath}, entity={entity.LogicEntityId.Value}.");
    }

    private static void PreloadFactory<TFactory>(
        string factoryPath,
        System.Action<string> onLoaded,
        System.Action<string, LoadResourceStatus, string> onFailed,
        Dictionary<string, TFactory> cache,
        string factoryName)
        where TFactory : class
    {
        if (string.IsNullOrWhiteSpace(factoryPath))
            throw new System.ArgumentException($"{factoryName} factory path is empty.", nameof(factoryPath));
        if (onLoaded == null)
            throw new System.ArgumentNullException(nameof(onLoaded));
        if (onFailed == null)
            throw new System.ArgumentNullException(nameof(onFailed));

        if (cache.ContainsKey(factoryPath))
        {
            onLoaded(factoryPath);
            return;
        }

        GF.Resource.LoadAsset(
            factoryPath,
            new LoadAssetCallbacks(
                (assetName, asset, duration, userData) =>
                {
                    if (asset is not TFactory factory)
                    {
                        throw new System.InvalidOperationException(
                            $"Loaded {factoryName} factory has invalid type. path={factoryPath}, asset={assetName}.");
                    }
                    cache.Add(factoryPath, factory);
                    onLoaded(assetName);
                },
                (assetName, status, errorMessage, userData) => onFailed(assetName, status, errorMessage)));
    }
}
