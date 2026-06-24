# Flow Field Crowd Movement System 当前方案

## 0. 原则

权威来源是 `GameAIPro_Chapter23_Crowd_Pathfinding_and_Steerin.pdf`。本文档只保留后续实现、排查、性能优化真正需要的事实，不记录历史流水账。

目标是让大量单位在建筑、窄路、拐角、移动目标、路线交叉和位移效果下稳定移动。运行时不能用隐藏 fallback 掩盖链路问题：主链路失败要明确报错或明确诊断，不能静默改走另一套导航。

## 1. 文献核心结构

第 23 章的主线是：

1. 世界划分为固定网格 sector，相邻 sector 的可通行边界生成 portal window。
2. portal center 是 N-way graph 节点，同 sector 内可互达 portal 之间连边。
3. 每个 flow tile 包含三类 field：`CostField`、`IntegrationField`、`FlowField`。
4. path request 先在 portal graph 上跑 A*，多个 source 用 merging A* 复用已有路径。
5. integrator 从最终 goal 或 portal window 做 seed，执行 LOS pass、成本积分、flow pass。
6. flow field cache 用唯一 tile id 共享结果，动态环境只 dirty 相关 sector/portal/tile。
7. 不同 movement type 拥有各自 cost field 和 portal graph。
8. steering 顺序是：没有有效 flow field 时朝下一个 portal，LOS 内朝精确目标，否则走 flow direction。
9. island field 是 flow grid 的连通分支，用于快速判断可达性。
10. CPU footprint 依赖共享缓存、分帧预算、预构建、压缩和后续多线程。

关键判断：文献并不要求 Unity NavMesh 成为运行时数据源。NavMesh 最多只能是离线生成 cost/source data 的方式之一；当前项目已经改为 authored FlowGrid，因此运行时不得再把 NavMesh 当裁判或兜底。

## 2. 当前真实运行链路

### 2.1 导航底图

`LdtkToTileWorldCreatorImporterWindow` 调用 `FlowNavigationGridPrefabBaker.BakeFromTerrainPrefab()`，根据 terrain prefab 的 `Ground` 层 collider 和 `LevelObstacle` 层 collider 生成 `FlowNavigationGridAsset`。

`FlowNavigationGridPrefabBaker.AttachSourceToLevelPrefab()` 会把 `FlowNavigationGridSource` 挂到 level prefab。运行时 `FlowNavigationGridSource.ApplyToFlowField()` 调用 `FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource()`。

`GroupMoveConfig.RequireAuthoredNavigationSource` 强制为 true。若有 active navigation agents 但没有 `FlowNavigationGridSource` 应用 asset，`GroupMoveManager.Update()` 会抛错。

结论：当前运行时导航底图唯一来源是 `FlowNavigationGridAsset`，不是 NavMesh、WorldCell、逻辑区块或临时采样。

### 2.2 世界构建与动态刷新

`GroupMoveManager.Update()` 每帧驱动：

1. `FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue()`
2. `FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue()`
3. `FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue()`

`FlowFieldCrowdMovementSystem` 基于 authored grid 构建 per movement type world：walkable mask、neighbor traversal mask、cost field、island field、sector、portal graph、shared goal field、flow tile cache。

建筑/障碍/CostStamp 通过 `GroupMoveManager.RegisterBoxObstacle()`、`RegisterCircleObstacle()`、`RegisterBoxCostStamp()`、`RegisterGridCostStamp()` 进入 runtime dirty 队列，只重建受影响 sector/portal/tile。

### 2.3 单位与英雄移动

自动单位主链路：

`CharacterMoveComp.Move()` -> `FlowFieldCrowdMovementSystem.TryGetSteeringVelocity()` -> `MoveExecutor.SetInput()` -> `MoveExecutor.Execute()`

英雄手动链路：

`PlayerMoveComp.Move()` -> `MoveExecutor.SetInput()` -> `MoveExecutor.Execute()`

英雄不走 flow pathfinding，但 `MoveExecutor` 会用 `FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement()` 做 authored FlowGrid 位移约束。这里已经不使用 NavMesh。

### 2.4 位移系统边界

`MovementMode.Normal` 才允许主动寻路输入。`MovementMode.Displaced` 和 `MovementMode.HardLocked` 期间只执行 external/override 位移，不叠加主动寻路。

后续推力和拉力必须沿用这个边界：

1. 推力是瞬时动量，写入 external 或 override，不提交 flow steering。
2. 拉力是持续变化力，位移期间仍不能主动寻路。
3. 位移结束后再回到 flow 查询和 FlowGrid 约束。

## 3. 当前已收敛的关键决策

### 3.1 严格 authored FlowGrid

运行寻路、移动约束、spawn 合法点、defend 距离估算、AI dead zone 点位都应查询 `FlowFieldCrowdMovementSystem` 的 FlowGrid API。

已经清掉的运行时依赖：

1. `UnityEngine.AI`
2. `NavMesh.SamplePosition`
3. `NavMesh.Raycast`
4. `NavMesh.CalculatePath`
5. `NavMeshQueryFilter`
6. `NavMeshPath`
7. `UseNavMeshSlopeCost`
8. `RequiresNavMeshAnchors`

诊断也不能再拿 NavMesh path 当“权威对照”。Flow 诊断只看 grid cell、walkable/base mask、neighbor traversal、cost、island、portal、LOS trace、grid path estimate。

### 3.2 fallback 定义

不允许的 fallback：

1. FlowGrid 缺失时改走 NavMesh 或直线。
2. path handle/tile/goal resolve 失败时悄悄朝最终目标直走。
3. start/goal 不合法时静默扩大半径、随机抖动、原地低速蠕动。
4. 移动约束失败时吞掉错误，只表现为速度变慢。

允许但必须明确的机制：

1. 文献 23.12 的 pending portal：tile 未就绪时朝下一个 portal 走。这不是 fallback，前提是 portal 来自 portal graph/path handle。
2. 最近同 island 可达点：目标落在不可走格或目标贴边时解析到同 island 最近可达点。这是可达性解析，不是隐藏替代路径；若频繁触发，必须继续查底图或目标点采样。
3. UI 预览或排序可以使用近似，但不能影响实兵移动链路，并且日志/命名不能伪装成真实路径。

## 4. 与文献一致的部分

1. 已使用 sector、portal window、portal graph，而不是全局直线追目标。
2. 已有 `CostField`、integration、flow direction、LOS/pathable/reachable 等 flag。
3. flow tile cache、shared goal field、moving target anchor、stable goal 复用已经承担移动目标共享。
4. runtime obstacle 和 cost stamp 走 dirty sector 重建，而不是全图同步重建。
5. island 是 flow grid 连通分支，不是 WorldCell。
6. steering 已区分 LOS、flow direction、portal pending。
7. 邻居避让使用局部空间桶和 TTC/分离主导，不再全量扫描。
8. 位移期间主动寻路输入被切断，兼容后续推/拉系统。

## 5. 仍与文献不同或未完成的点

### P0 当前功能测试前必须重点关注

1. `FlowNavigationGridPrefabBaker` 目前按格子中心竖直 raycast 生成 walkable。它满足零手工工作流，但比 NavMesh 烘焙更离散，窄边、斜边、角落可能受 cell size 和 collider 边界影响。若出现 island 被切碎或建筑对面错误直冲，先查 asset walkable、neighbor traversal、portal window，不要改 steering 补偿。
2. authored grid 目前只有 walkable mask 和中心 anchor，没有独立道路/地形成本层。墙边 blur 和 runtime CostStamp 能提供基础成本，但“道路偏好”“特殊地形成本”还不是完整编辑器数据源。
3. 多单位攻击同一目标的站位分散依赖 combat slot/cache 和动态避让，还不是文献 multiple goals 或完善 formation。若再次扎点，优先查 slot 分配、目标可达点、攻击距离判定和 collision radius。

### P1 应继续实现

1. 编辑器成本源和可视化：显示 walkable、cost、island、portal、flow tile、LOS trace；支持道路/软禁行/地形惩罚写入 CostField。
2. 多选/混合 movement type 入口：当前项目暂不使用，但可实现未接入玩法的 API。混合组应按最受限 movement type 共享 path，不兼容时拆分请求。
3. 日志分级和采样：性能测试默认关闭 Move/Brain 详细日志，只保留 `[FlowPerf]` 汇总；问题诊断再按 key、问题类型、采样率开启。
4. 离线预构建/磁盘缓存：稳定地图可预构建 portal access、常用目标 flow tile 或 sector-pair 数据，减少首次打开 tile 延迟。

### P2 中长期优化

1. Jobs/Burst 化 integration、island、portal transition。
2. 分层 portal graph，减少超大地图长距离 graph 搜索。
3. multiple-goal flow field，用于多个英雄、多个基地或多个等价攻击目标。
4. 更完整的 movement type 数据资产：不同半径、不同墙边成本、不同地形成本，避免只靠 agent type id 分 world。

## 6. 性能审查清单

性能测试时重点看：

1. `[FlowPerf] total/path/tile/steering/neighborAvoid/bottleneck/boundary/agentUpdate`
2. `pathBuilds`
3. `sectorPath(searches/cacheHits)`
4. `tileCache(hits/misses)`
5. `pending(sharedGoal/tile)`
6. `stableGoal(raw/reuse/initial/cell)`
7. `FlowFieldCrowdMovementSystem` 与 `SoldierAIBrain` 各自耗时
8. 日志是否开启导致性能样本失真

不再把 `NavMesh.CalculatePath`、`NavMesh.Raycast`、`NavMesh.SamplePosition` 列为运行时性能目标。若 Profiler 里又出现这些 API，说明旧链路回流，应当按 bug 处理。

## 7. 不要做的事

1. 不要把“朝最终目标直线走”作为 tile 未就绪策略。
2. 不要在 `TryGetSteeringVelocity()` 热路径同步补完 world、runtime dirty、shared goal field 或非末端 portal tile。
3. 不要把道路偏好、墙边绕行、建筑侧角选择放到 steering 层硬推。
4. 不要用扩大 arriveDistance、减速、随机抖动掩盖 flow/path 错误。
5. 不要让位移期间主动寻路输入继续叠加。
6. 不要把 WorldCell 当可走格；可走性只看 authored FlowGrid、CostField、NeighborTraversal。
7. 不要因为最近可达点能避免报错，就停止追查主岛被错误切割的问题。
8. 不要让旧 `GroupMoveCoordinator` 的 LJ/ORCA 重新进入实战移动链路。

## 8. 代码所有权地图

1. `FlowFieldCrowdMovementSystem.cs`：flow grid、cost/integration/flow field、portal graph、island、runtime dirty、cache、steering、bottleneck、FlowGrid 查询 API。
2. `FlowNavigationGridAsset.cs`：authored walkable grid asset。
3. `FlowNavigationGridSource.cs`：level prefab 上的运行时 source，应在关卡加载时应用 asset。
4. `FlowNavigationGridPrefabBaker.cs`：LDtk/terrain prefab 导出时生成 FlowGrid asset。
5. `GroupMoveManager.cs`：配置同步、agent/obstacle 注册、队列驱动、严格 source 检查。
6. `CharacterMoveComp.cs`：自动寻路单位入口，不放路径算法。
7. `PlayerMoveComp.cs`：英雄手动输入，不走 flow pathfinding。
8. `MoveExecutor.cs`：CharacterController 执行、FlowGrid 位移约束、位移/外力仲裁。
9. `SoldierAIBrain.cs`：状态机和目标选择，不放路径算法。
10. `GroupMoveCoordinator.cs` / `SteeringMovement.cs`：遗留逻辑和旧测试，不应成为新寻路效果核心。

## 9. 当前测试前结论

当前主链路目标是：严格 authored FlowGrid、无运行时 NavMesh、无静默 fallback。若新测试仍出现“隔建筑直冲”“贴边慢移”“多单位扎点”“island 异常增多”，优先从 FlowGrid asset、neighbor traversal、portal window、goal resolve、combat slot 和日志采样查根因，不要先做表层速度补偿。
