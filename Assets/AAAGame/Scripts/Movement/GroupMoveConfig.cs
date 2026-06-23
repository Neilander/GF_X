using UnityEngine;

/// <summary>
/// Flow field 群体移动和士兵 AI 的可调参数。
/// SO 资产由 GroupMoveManager 引用，运行时同步给 FlowFieldCrowdMovementSystem。
/// </summary>
[CreateAssetMenu(fileName = "GroupMoveConfig", menuName = "Movement/GroupMoveConfig")]
public class GroupMoveConfig : ScriptableObject
{
    [Header("移动")]
    [Tooltip("flow 方向和直接路径方向的混合系数。1=更贴近直接路径，越低越服从 flow。")]
    [Range(0.01f, 1f)]
    public float VelocitySmoothing = 0.35f;

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

    [Header("Flow Field")]
    [Tooltip("启用后必须由 FlowNavigationGridSource 提供第一手导航格；缺失时明确报错，不回退到 Unity NavMesh。")]
    public bool RequireAuthoredNavigationSource = false;
    [Tooltip("导航底图格子大小。<= 0 时按 NavMesh Agent 半径自动推导。")]
    [Min(0f)]
    public float NavigationCellSize = 0f;
    [Tooltip("从 NavMesh 边界向外额外扩展的导航包围盒边距。")]
    [Min(0f)]
    public float NavigationBoundsPadding = 0.6f;
    [Tooltip("每个 sector 包含的格子边长。")]
    [Min(4)]
    public int SectorSizeInCells = 12;
    [Tooltip("窄口判定：portal 宽度小于等于该格数时启用瓶颈调度。")]
    [Min(1)]
    public int PortalNarrowWidthCells = 2;
    [Tooltip("宽 portal window 超过该格数时拆成多个 graph 节点，避免大门/宽边界只有一个中心点导致 A* 粒度过粗。")]
    [Min(2)]
    public int PortalMaxWindowWidthCells = 6;
    [Tooltip("flow tile 缓存上限。")]
    [Min(16)]
    public int FlowTileCacheLimit = 256;
    [Tooltip("运行时障碍/CostStamp 脏数据每帧重建预算，单位毫秒。")]
    [Min(0.05f)]
    public float RuntimeRebuildBudgetMilliseconds = 1.5f;
    [Tooltip("根据相邻 NavMesh 采样点高度差给 CostField 增加坡度成本。")]
    public bool UseNavMeshSlopeCost = true;
    [Tooltip("每 1 个格子尺寸的高度差转换成多少额外 cost。")]
    [Min(0f)]
    public float SlopeCostPerCellHeight = 8f;

    [Header("Crowd Steering")]
    [Tooltip("邻居预测时间，越大越会提前避让。")]
    [Range(0.05f, 1.5f)]
    public float CrowdPredictionTime = 0.35f;
    [Tooltip("窄道内的稳定侧偏强度。")]
    [Range(0f, 1f)]
    public float LaneBiasStrength = 0.22f;
    [Tooltip("局部边界避让强度。")]
    [Min(0f)]
    public float BoundaryAvoidanceWeight = 1f;

    [Header("Bottleneck")]
    [Tooltip("瓶颈切换方向的冷却时间。")]
    [Min(0f)]
    public float BottleneckSwitchCooldown = 0.35f;
    [Tooltip("等待超过该时长后可抢占通行权。")]
    [Min(0.1f)]
    public float BottleneckWaitTimeout = 1.25f;
    [Tooltip("进入瓶颈口附近多远时开始受调度影响。")]
    [Min(0.1f)]
    public float BottleneckInfluenceDistance = 2.2f;
    [Tooltip("瓶颈刚有单位通过后，继续为同向后继流保留通行权的时间。")]
    [Min(0.05f)]
    public float BottleneckClearanceHoldTime = 0.45f;

    [Header("Debug")]
    public bool DrawNavigationDebug = true;
    public bool DrawFlowFieldDebug = false;
}
