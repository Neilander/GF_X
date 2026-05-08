using UnityEngine;

/// <summary>
/// 群体移动协调器的全部可调参数。
/// SO 资产由 GroupMoveManager 引用，运行时同步给 GroupMoveCoordinator。
/// </summary>
[CreateAssetMenu(fileName = "GroupMoveConfig", menuName = "Movement/GroupMoveConfig")]
public class GroupMoveConfig : ScriptableObject
{
    [Header("单位 LJ 参数")]
    public float UnitRepulsionStrength = 50f;
    public float UnitAttractionStrength = 2f;
    public float UnitEquilibriumRadius = 60f / GroupMoveCoordinator.PX_SCALE;
    public float UnitMaxInfluenceRange = 370f / GroupMoveCoordinator.PX_SCALE;

    [Header("领袖 LJ 参数")]
    public float LeaderRepulsionStrength = 50f;
    public float LeaderAttractionStrength = 2f;
    public float LeaderEquilibriumRadius = 105f / GroupMoveCoordinator.PX_SCALE;
    public float LeaderMaxInfluenceRange = 370f / GroupMoveCoordinator.PX_SCALE;

    [Header("敌对阵营 LJ 参数")]
    public float EnemyRepulsionStrength = 50f;
    public float EnemyAttractionStrength = 10f;
    public float EnemyEquilibriumRadius = 1.5f;
    public float EnemyMaxInfluenceRange = 15f;

    [Header("障碍物")]
    public float ObstacleWeight = 100f;

    [Header("移动阈值")]
    [Tooltip("没有单位速度时使用的兜底阈值")]
    public float MoveThreshold = 0.5f;
    [Tooltip("按单位世界速度的倍率计算低速忽略阈值。1.5 = 速度的 150%")]
    public float MoveThresholdSpeedRatio = 1.5f;
    [Tooltip("最终安全速度平滑系数。1=不平滑，越低越稳但响应越慢")]
    [Range(0.01f, 1f)]
    public float VelocitySmoothing = 0.35f;

    [Header("跟随触发")]
    [Tooltip("领袖进入此距离时单位才切 Follow 状态。调大 = 单位更早被勾过来跟随。")]
    public float FollowRecruitRadius = 8f;
    [Tooltip("跟随中领袖超过此距离则脱离 Follow 回 Idle。调大 = 领袖跑得再远也不放弃。")]
    public float FollowLeashRange = 30f;

    [Header("跟随死区")]
    [Tooltip("远死区宽度。死区外圈半径 = LeaderEquilibriumRadius + 此值。")]
    public float FollowDeadZoneRange = 12f;
    [Tooltip("近死区宽度。近死区半径 = LeaderEquilibriumRadius + 此值。在近死区内 desiredVel = 0。")]
    public float FollowInnerDeadZoneRange = 2f;
}
