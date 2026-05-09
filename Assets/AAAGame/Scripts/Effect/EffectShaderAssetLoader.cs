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

        private static readonly Dictionary<string, Shader> ShaderCache = new Dictionary<string, Shader>();
        private static readonly HashSet<string> LoadingPaths = new HashSet<string>();

        public static void PreloadEssentialShaders()
        {
            RequestLoad(HitFlashShaderAssetPath);
            RequestLoad(UnitDeathShaderAssetPath);
            RequestLoad(WeaponAttackTrailShaderAssetPath);
            RequestLoad(GhostShaderAssetPath);
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

        private static void RequestLoad(string assetPath)
        {
            if (GF.Resource == null || LoadingPaths.Contains(assetPath))
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
                assetPath);
        }

        private static void OnLoadSuccess(string assetName, object asset, float duration, object userData)
        {
            string assetPath = userData as string;
            if (!string.IsNullOrEmpty(assetPath))
            {
                LoadingPaths.Remove(assetPath);
            }

            if (asset is Shader shader && !string.IsNullOrEmpty(assetPath))
            {
                ShaderCache[assetPath] = shader;
                Debug.Log($"[EffectShaderAssetLoader] Loaded Shader asset: {assetPath}");
                return;
            }

            Debug.LogError($"[EffectShaderAssetLoader] Loaded asset is not Shader: {assetName}");
        }

        private static void OnLoadFailure(string assetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            string assetPath = userData as string;
            if (!string.IsNullOrEmpty(assetPath))
            {
                LoadingPaths.Remove(assetPath);
            }

            Debug.LogError($"[EffectShaderAssetLoader] Failed to load Shader asset: {assetName}, status={status}, error={errorMessage}");
        }
    }
}
