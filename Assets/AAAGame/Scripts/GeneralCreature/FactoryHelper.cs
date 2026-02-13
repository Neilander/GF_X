using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.Resource;

public static class FactoryHelper
{
    static Dictionary<string, AtkCompFactory> _atkFactories = new();
    static Dictionary<string, MoveCompFactory> _moveFactories = new();
    static Dictionary<string, SkillCompFactory> _skillFactories = new();

    public static void CreateAtkComp(string factoryPath, MAEntity entity)
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

    public static void CreateMoveComp(string factoryPath, MAEntity entity)
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
    
    public static void CreateSkillComp(string factoryPath, SkillEntity entity)
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
}
