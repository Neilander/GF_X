using UnityEngine;
using UnityGameFramework.Runtime;

namespace AAAGame.Effect
{
    public sealed class EffectRuntimeConfigComponent : GameFrameworkComponent
    {
        [Header("Unit Outline")]
        [SerializeField] private Shader unitOutlineShader;
        [SerializeField] private Color friendlyOutlineColor = new Color(0.472054f, 0.9811321f, 0.5226642f, 0.47058824f);
        [SerializeField, Range(0f, 0.1f)] private float friendlyOutlineWidth = 0.002f;
        [SerializeField] private Color enemyOutlineColor1 = new Color(0.94747066f, 0.22327036f, 1f, 0.5137255f);
        [SerializeField, Range(0f, 0.1f)] private float enemyOutlineWidth1 = 0.002f;
        [SerializeField] private Color enemyOutlineColor2 = new Color(0.81964487f, 0.2924527f, 1f, 0.21568628f);
        [SerializeField, Range(0f, 0.1f)] private float enemyOutlineWidth2 = 0.004f;

        [Header("Trail")]
        [SerializeField] private Material defaultCanTrailMaterial;

        [Header("Enemy Stronghold Fog")]
        [SerializeField] private float fogAreaBase = 50f;
        [SerializeField] private float fogGroundYOffset = 0f;
        [SerializeField] private float fogOuterThickness = 0.2f;
        [SerializeField] private float fogOuterHeight = 0.3f;
        [SerializeField] private float fogOuterBottomOffset = 0f;
        [SerializeField] private float fogOuterDensityScale = 0.5f;
        [SerializeField] private float fogOuterStartSizeMultiplier = 0.1f;
        [SerializeField, Range(0f, 1f)] private float fogOuterAlpha = 0.8f;
        [SerializeField] private int fogOuterSortingOrder = 0;
        [SerializeField] private float fogInnerHeight = 0.3f;
        [SerializeField] private float fogInnerThickness = 0f;
        [SerializeField] private float fogInnerBottomOffset = 0f;
        [SerializeField] private float fogInnerDensityScale = 1f;
        [SerializeField] private float fogInnerStartSizeMultiplier = 0.4f;
        [SerializeField, Range(0f, 1f)] private float fogInnerAlpha = 0.6f;
        [SerializeField] private int fogInnerSortingOrder = 0;

        [Header("Building Ownership Color")]
        [SerializeField] private Color enemyBuildingColor = new Color(0.70980394f, 0.6509804f, 0.7294118f, 1f); // #B5A6BA

        public Shader UnitOutlineShader => unitOutlineShader;
        public Color FriendlyOutlineColor => friendlyOutlineColor;
        public float FriendlyOutlineWidth => friendlyOutlineWidth;
        public Color EnemyOutlineColor1 => enemyOutlineColor1;
        public float EnemyOutlineWidth1 => enemyOutlineWidth1;
        public Color EnemyOutlineColor2 => enemyOutlineColor2;
        public float EnemyOutlineWidth2 => enemyOutlineWidth2;
        public Material DefaultCanTrailMaterial => defaultCanTrailMaterial;

        public float FogAreaBase => fogAreaBase;
        public float FogGroundYOffset => fogGroundYOffset;
        public float FogOuterThickness => fogOuterThickness;
        public float FogOuterHeight => fogOuterHeight;
        public float FogOuterBottomOffset => fogOuterBottomOffset;
        public float FogOuterDensityScale => fogOuterDensityScale;
        public float FogOuterStartSizeMultiplier => fogOuterStartSizeMultiplier;
        public float FogOuterAlpha => fogOuterAlpha;
        public int FogOuterSortingOrder => fogOuterSortingOrder;
        public float FogInnerHeight => fogInnerHeight;
        public float FogInnerThickness => fogInnerThickness;
        public float FogInnerBottomOffset => fogInnerBottomOffset;
        public float FogInnerDensityScale => fogInnerDensityScale;
        public float FogInnerStartSizeMultiplier => fogInnerStartSizeMultiplier;
        public float FogInnerAlpha => fogInnerAlpha;
        public int FogInnerSortingOrder => fogInnerSortingOrder;

        public Color EnemyBuildingColor => enemyBuildingColor;
    }
}
