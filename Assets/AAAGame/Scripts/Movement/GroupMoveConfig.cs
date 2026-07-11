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
    public float EnemySoftReturnRatio = 0.6f;

    [Header("跟随触发")]
    [Tooltip("领袖进入此距离时单位才切 Follow 状态。调大 = 单位更早被勾过来跟随。")]
    public float FollowRecruitRadius = 8f;
    [Tooltip("跟随中领袖超过此距离则脱离 Follow 回 Idle。调大 = 领袖跑得再远也不放弃。")]
    public float FollowLeashRange = 30f;

    [Header("跟随死区")]
    [Tooltip("领袖周围的基础停靠半径。")]
    [Min(0f)]
    public float FollowBaseStopRadius = 1.5f;
    [Tooltip("远死区宽度。死区外圈半径 = FollowBaseStopRadius + 此值。")]
    public float FollowDeadZoneRange = 12f;
    [Tooltip("近死区宽度。近死区半径 = FollowBaseStopRadius + 此值。在近死区内 desiredVel = 0。")]
    public float FollowInnerDeadZoneRange = 2f;
}
