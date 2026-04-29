using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Effect
{
    public class UnitOutline : MonoBehaviour
    {
        public enum OutlineType
        {
            Friendly,
            Enemy
        }

        [SerializeField]
        private OutlineType _outlineType = OutlineType.Friendly;

        public OutlineType outlineType
        {
            get => _outlineType;
            set
            {
                if (_outlineType != value)
                {
                    _outlineType = value;
                    if (isActiveAndEnabled)
                    {
                        RemoveOutlineMaterials();
                        ApplyOutlineMaterials();
                    }
                }
            }
        }

        private Renderer[] renderers;

        private static EffectRuntimeConfigComponent _configCache;
        private static Material friendlyBaseMat;
        private static Material enemyBaseMat;

        private void OnEnable()
        {
            InitializeMaterials();
            renderers = GetComponentsInChildren<Renderer>();
            ApplyOutlineMaterials();
        }

        private void OnDisable()
        {
            RemoveOutlineMaterials();
        }

        public static void ForceRefreshMaterials()
        {
            friendlyBaseMat = null;
            enemyBaseMat = null;
            _configCache = null;
        }

        private static void InitializeMaterials()
        {
            if (friendlyBaseMat != null)
                return;

            Shader outlineShader = Shader.Find("Hidden/AAAGame/UnitOutline");
            if (outlineShader == null)
                return;

            _configCache ??= GameEntry.GetComponent<EffectRuntimeConfigComponent>();
            if (_configCache == null)
                return;

            friendlyBaseMat = new Material(outlineShader);
            friendlyBaseMat.SetColor("_OutlineColor", _configCache.FriendlyOutlineColor);
            friendlyBaseMat.SetFloat("_OutlineWidth", _configCache.FriendlyOutlineWidth);

            enemyBaseMat = new Material(outlineShader);
        }

        private void ApplyOutlineMaterials()
        {
            if (renderers == null || friendlyBaseMat == null || _configCache == null)
                return;

            foreach (var r in renderers)
            {
                var mats = new List<Material>(r.sharedMaterials);

                if (_outlineType == OutlineType.Friendly)
                {
                    mats.Add(friendlyBaseMat);
                }
                else
                {
                    var m1 = new Material(enemyBaseMat);
                    var m2 = new Material(enemyBaseMat);

                    m1.SetFloat("_OutlineWidth", _configCache.EnemyOutlineWidth1);
                    m1.SetColor("_OutlineColor", _configCache.EnemyOutlineColor1);

                    m2.SetFloat("_OutlineWidth", _configCache.EnemyOutlineWidth2);
                    m2.SetColor("_OutlineColor", _configCache.EnemyOutlineColor2);

                    mats.Add(m1);
                    mats.Add(m2);
                }

                r.sharedMaterials = mats.ToArray();
            }
        }

        private void RemoveOutlineMaterials()
        {
            if (renderers == null)
                return;

            foreach (var r in renderers)
            {
                if (r == null)
                    continue;

                var mats = new List<Material>(r.sharedMaterials);
                mats.RemoveAll(m => m != null && m.shader != null && m.shader.name == "Hidden/AAAGame/UnitOutline");
                r.sharedMaterials = mats.ToArray();
            }
        }
    }
}
