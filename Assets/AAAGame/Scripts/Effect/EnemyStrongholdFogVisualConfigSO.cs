using UnityEngine;

[CreateAssetMenu(fileName = "EnemyStrongholdFogVisualConfig", menuName = "AAAGame/Effect/EnemyStrongholdFogVisualConfig")]
public class EnemyStrongholdFogVisualConfigSO : ScriptableObject
{
    [Header("Global")]
    [Tooltip("Emission scaling baseline area.")]
    public float AreaBase = 50f;
    [Tooltip("Fog ground alignment offset from stronghold ground Y.")]
    public float GroundYOffset = 0f;

    [Header("Outer Ring")]
    public float OuterThickness = 0.5f;
    public float OuterHeight = 1.2f;
    [Tooltip("Bottom offset from ground. 0 means touching ground.")]
    public float OuterBottomOffset = 0f;
    public float OuterDensityScale = 10f;
    public float OuterStartSizeMultiplier = 0.6f;
    [Range(0f, 1f)] public float OuterAlpha = 1.0f;
    public int OuterSortingOrder = 0;

    [Header("Inner Area")]
    public float InnerHeight = 0.1f;
    [Tooltip("0 for solid fill, >0 for shell.")]
    public float InnerThickness = 0f;
    [Tooltip("Bottom offset from ground. 0 means touching ground.")]
    public float InnerBottomOffset = 0f;
    public float InnerDensityScale = 0.5f;
    public float InnerStartSizeMultiplier = 0.4f;
    [Range(0f, 1f)] public float InnerAlpha = 0.6f;
    public int InnerSortingOrder = 0;

    public static EnemyStrongholdFogVisualConfigSO CreateRuntimeDefault()
    {
        return CreateInstance<EnemyStrongholdFogVisualConfigSO>();
    }
}
