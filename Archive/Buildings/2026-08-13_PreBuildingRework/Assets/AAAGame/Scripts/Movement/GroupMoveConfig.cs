using UnityEngine;

/// <summary>
/// 士兵 AI 行为参数。Flow field 寻路参数由 FlowFieldNavigationConfig 提供。
/// </summary>
[CreateAssetMenu(fileName = "GroupMoveConfig", menuName = "Movement/GroupMoveConfig")]
public class GroupMoveConfig : ScriptableObject
{
    [Header("敌人脱战")]
    [Tooltip("软返航比例：敌人离家距离 ≥ ChaseRange × 此值 且周围无敌时，温和走回家（不挂 buff，可被打断索敌）。0.6 = ChaseRange 的 60%。")]
    [Range(0f, 1f)]
    [SerializeField] private float EnemySoftReturnRatio = 0.6f;

    [Header("跟随死区")]
    [Tooltip("领袖周围的基础停靠半径。")]
    [Min(0f)]
    [SerializeField] private float FollowBaseStopRadius = 1.5f;
    [Tooltip("远死区宽度。死区外圈半径 = FollowBaseStopRadius + 此值。")]
    [SerializeField] private float FollowDeadZoneRange = 12f;
    [Tooltip("近死区宽度。近死区半径 = FollowBaseStopRadius + 此值。在近死区内 desiredVel = 0。")]
    [SerializeField] private float FollowInnerDeadZoneRange = 2f;

    public Fix64 EnemySoftReturnRatioFixed => (Fix64)EnemySoftReturnRatio;
    public Fix64 FollowBaseStopRadiusFixed => (Fix64)FollowBaseStopRadius;
    public Fix64 FollowDeadZoneRangeFixed => (Fix64)FollowDeadZoneRange;
    public Fix64 FollowInnerDeadZoneRangeFixed => (Fix64)FollowInnerDeadZoneRange;
}
