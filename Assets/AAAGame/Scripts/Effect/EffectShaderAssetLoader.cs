using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Effect
{
    public static class EffectShaderAssetLoader
    {
        public const string HitFlashShaderAssetPath = "Assets/AAAGame/Scripts/Effect/Resources/HitFlashWhite.shader";
        public const string UnitDeathShaderAssetPath = "Assets/AAAGame/Scripts/Effect/Resources/UnitDeathVFXParticle.shader";
        public const string WeaponAttackTrailShaderAssetPath = "Assets/AAAGame/Scripts/Effect/WeaponAttackEffect/WeaponAttackTrailAdditive.shader";
        public const string GhostShaderAssetPath = "Assets/AAAGame/Scripts/Effect/Resources/GhostTransparent.shader";
        public const string CardForbiddenZoneShaderAssetPath = "Assets/AAAGame/Scripts/Card/Material/CardForbiddenZoneOverlay.shader";
        public const string FogOverlayShaderAssetPath = "Assets/AAAGame/Scripts/MiniMap/FOG3/View/Fog3OverlayAlwaysOnTop.shader";

        private static readonly string[] EssentialShaderAssetPaths =
        {
            HitFlashShaderAssetPath,
            UnitDeathShaderAssetPath,
            WeaponAttackTrailShaderAssetPath,
            GhostShaderAssetPath,
            CardForbiddenZoneShaderAssetPath,
            FogOverlayShaderAssetPath,
        };

        private static readonly Dictionary<string, Shader> ShaderCache = new Dictionary<string, Shader>();
        private static readonly HashSet<string> LoadingPaths = new HashSet<string>();

        public static int EssentialShaderCount => EssentialShaderAssetPaths.Length;

        public static void PreloadEssentialShaders(System.Action<string> onLoaded = null, System.Action<string, LoadResourceStatus, string> onFailed = null)
        {
            for (int i = 0; i < EssentialShaderAssetPaths.Length; i++)
            {
                RequestLoad(EssentialShaderAssetPaths[i], onLoaded, onFailed);
            }
        }

        public static Shader TryGet(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            if (ShaderCache.TryGetValue(assetPath, out Shader shader))
            {
                return shader;
            }

            RequestLoad(assetPath);
            return null;
        }

        private sealed class LoadUserData
        {
            public string AssetPath;
            public System.Action<string> OnLoaded;
            public System.Action<string, LoadResourceStatus, string> OnFailed;
        }

        private static void RequestLoad(string assetPath, System.Action<string> onLoaded = null, System.Action<string, LoadResourceStatus, string> onFailed = null)
        {
            if (GF.Resource == null)
            {
                return;
            }

            if (ShaderCache.ContainsKey(assetPath))
            {
                onLoaded?.Invoke(assetPath);
                return;
            }

            if (LoadingPaths.Contains(assetPath))
            {
                return;
            }

            HasAssetResult hasAssetResult = GF.Resource.HasAsset(assetPath);
            if (hasAssetResult == HasAssetResult.NotReady)
            {
                return;
            }

            if (hasAssetResult == HasAssetResult.NotExist)
            {
                if (GF.Resource.InternalResourceVersion <= 0)
                {
                    return;
                }

                Debug.LogError($"[EffectShaderAssetLoader] Shader asset not found in resource system: {assetPath}");
                return;
            }

            LoadingPaths.Add(assetPath);
            GF.Resource.LoadAsset(
                assetPath,
                typeof(Shader),
                new LoadAssetCallbacks(OnLoadSuccess, OnLoadFailure),
                new LoadUserData
                {
                    AssetPath = assetPath,
                    OnLoaded = onLoaded,
                    OnFailed = onFailed,
                });
        }

        private static void OnLoadSuccess(string assetName, object asset, float duration, object userData)
        {
            LoadUserData loadUserData = userData as LoadUserData;
            string assetPath = loadUserData?.AssetPath;
            if (!string.IsNullOrEmpty(assetPath))
            {
                LoadingPaths.Remove(assetPath);
            }

            if (asset is Shader shader && !string.IsNullOrEmpty(assetPath))
            {
                ShaderCache[assetPath] = shader;
                Debug.Log($"[EffectShaderAssetLoader] Loaded Shader asset: {assetPath}");
                loadUserData?.OnLoaded?.Invoke(assetPath);
                return;
            }

            Debug.LogError($"[EffectShaderAssetLoader] Loaded asset is not Shader: {assetName}");
            loadUserData?.OnFailed?.Invoke(assetName, LoadResourceStatus.TypeError, "Loaded asset is not Shader.");
        }

        private static void OnLoadFailure(string assetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            LoadUserData loadUserData = userData as LoadUserData;
            string assetPath = loadUserData?.AssetPath;
            if (!string.IsNullOrEmpty(assetPath))
            {
                LoadingPaths.Remove(assetPath);
            }

            Debug.LogError($"[EffectShaderAssetLoader] Failed to load Shader asset: {assetName}, status={status}, error={errorMessage}");
            loadUserData?.OnFailed?.Invoke(assetName, status, errorMessage);
        }
    }
}
