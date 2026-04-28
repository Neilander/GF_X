using System.Collections.Generic;
using UnityEngine;

namespace AAAGame.Effect
{
    public class UnitOutline : MonoBehaviour
    {
        public enum OutlineType
        {
            Friendly, // 锐利的白光
            Enemy     // 模糊的紫光
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
                    if (this.isActiveAndEnabled)
                    {
                        RemoveOutlineMaterials();
                        ApplyOutlineMaterials();
                    }
                }
            }
        }

        private Renderer[] renderers;
        
        private static UnitOutlineConfigSO _configCache;
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
        }

        private static void InitializeMaterials()
        {
            if (friendlyBaseMat != null) return;
            
            Shader outlineShader = Shader.Find("Hidden/AAAGame/UnitOutline");
            if (outlineShader == null) return;

            if (_configCache == null)
            {
#if UNITY_EDITOR
                // 编辑器下直接寻找
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:UnitOutlineConfigSO");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    _configCache = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitOutlineConfigSO>(path);
                }
#else
                // 运行时需要根据自己的资源框架加载，通常放在 Resources 或 Addressables
                _configCache = Resources.Load<UnitOutlineConfigSO>("UnitOutlineConfig");
#endif
            }

            if (_configCache == null)
            {
                // 如果没找到 SO，走保底数据
                _configCache = ScriptableObject.CreateInstance<UnitOutlineConfigSO>();
            }

            // 我方：锐利白光
            friendlyBaseMat = new Material(outlineShader);
            friendlyBaseMat.SetColor("_OutlineColor", _configCache.FriendlyOutlineColor);
            friendlyBaseMat.SetFloat("_OutlineWidth", _configCache.FriendlyOutlineWidth);

            // 敌方：模糊紫光基底
            enemyBaseMat = new Material(outlineShader);
        }

        private void ApplyOutlineMaterials()
        {
            if (renderers == null || friendlyBaseMat == null) return;

            foreach (var r in renderers)
            {
                var mats = new List<Material>(r.sharedMaterials);
                
                if (_outlineType == OutlineType.Friendly)
                {
                    mats.Add(friendlyBaseMat);
                }
                else
                {
                    // 敌方光环：添加层级达到模糊虚影效果
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
            if (renderers == null) return;

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = new List<Material>(r.sharedMaterials);
                
                // 移除外挂的描边材质
                mats.RemoveAll(m => 
                    m != null && m.shader != null && m.shader.name == "Hidden/AAAGame/UnitOutline");
                
                r.sharedMaterials = mats.ToArray();
            }
        }
    }
}
