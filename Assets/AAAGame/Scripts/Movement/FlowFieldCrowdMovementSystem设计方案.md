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
8. steering 顺序是：没有有效 flow field 时朝 path handle 的下一个 path target；跨 sector 是下一个 portal，同 sector final tile 未就绪时是稳定 final goal。LOS 内朝精确目标或选中 portal 对侧槽位，否则走 flow direction。
9. island field 是 flow grid 的连通分支，用于快速判断可达性。
10. CPU footprint 依赖共享缓存、分帧预算、预构建、压缩和后续多线程。

关键判断：文献并不要求 Unity NavMesh 成为运行时数据源。NavMesh 最多只能是离线生成 cost/source data 的方式之一；当前项目已经改为 authored FlowGrid，因此运行时不得再把 NavMesh 当裁判或兜底。

## 2. 当前真实运行链路

### 2.1 导航底图

`LdtkToTileWorldCreatorImporterWindow` 调用 `FlowNavigationGridPrefabBaker.BakeMovementTypesFromTerrainPrefab()`，根据 terrain prefab 的 `Ground` 层 collider 和 `LevelObstacle` 层 collider 生成 `FlowNavigationGridAsset`。

当前导出会生成 Medium/Small/Large 三份 movement type grid。每份 asset 都保存 walkable mask、cell anchor 和 authored source cost field。baker 在编辑器阶段直接用真实 `Ground`/`LevelObstacle` collider 检查单位圆盘 footprint：圆盘采样点必须都落在 Ground 上，且圆盘范围不能触碰障碍 collider；不是只看 cell center。Large/Small/Medium 通过各自 hard clearance 半径生成可走区和墙边成本。这对应文献 23.10 source cost data 和 23.11 different movement types / wall cushioning。

`FlowNavigationGridPrefabBaker.AttachSourceToLevelPrefab()` 会把 `FlowNavigationGridSource` 挂到 level prefab。运行时 `FlowNavigationGridSource.ApplyToFlowField()` 会收集主 grid 和 movement type grids：单 grid 走 `SetAuthoredNavigationSource()`，多 grid 走 `SetAuthoredNavigationSources()`，并把 authored cost field 一并传入 `FlowFieldCrowdMovementSystem`。

`GroupMoveConfig.RequireAuthoredNavigationSource` 强制为 true。若有 active navigation agents 但没有 `FlowNavigationGridSource` 应用 asset，`GroupMoveManager.Update()` 会抛错。

结论：当前运行时导航底图唯一来源是 `FlowNavigationGridAsset`，不是 NavMesh、WorldCell、逻辑区块或临时采样。

### 2.2 世界构建与动态刷新

`GroupMoveManager.Update()` 每帧驱动：

1. `FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue()`
2. `FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue()`
3. `FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue()`

`FlowFieldCrowdMovementSystem` 基于 authored grid 构建 per movement type world：walkable mask、neighbor traversal mask、source cost field、runtime cost field、island field、sector、portal graph、shared goal field、flow tile cache。多 authored source 时优先按显式 `agentTypeId` 选择对应 source；兼容用的 `_testTerrainOverride` 不能抢先覆盖多 source。

建筑/障碍/CostStamp 通过 `GroupMoveManager.RegisterBoxObstacle()`、`RegisterCircleObstacle()`、`RegisterBoxCostStamp()`、`RegisterGridCostStamp()` 进入 runtime dirty 队列，只重建受影响 sector/portal/tile。动态重建必须从 authored source cost 出发，再叠加动态障碍 blur 和 CostStamp；不能退回“全 1 成本 + 运行时整图算墙距”的旧模型。

### 2.3 单位与英雄移动

自动单位主链路：

`CharacterMoveComp.Move()` -> `FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed()` -> `MoveExecutor.SetInputFixed()` -> `MoveExecutor.Execute()`

生产链路从目标、速度、steering 到移动输入均使用 `Fix64/FixVector2`。`TryGetSteeringVelocity(Vector3, float)` 只保留为 Editor 测试和显式兼容边界，内部立即量化并转调定点入口，不得作为 gameplay authority 调用。

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
2. 同 sector `PendingFinalGoal`：final tile 未就绪时朝稳定 final goal 走，同时 final tile 已进入预算队列。这是 23.12 “next path target”的同 sector 形式，不允许跨 sector 用最终目标直线替代 portal。
3. 最近同 island 可达点：目标落在不可走格或目标贴边时解析到同 island 最近可达点。这是可达性解析，不是隐藏替代路径；若频繁触发，必须继续查底图或目标点采样。
4. UI 预览或排序可以使用近似，但不能影响实兵移动链路，并且日志/命名不能伪装成真实路径。

## 4. 与文献一致的部分

1. 已使用 sector、portal window、portal graph，而不是全局直线追目标。
2. 已有 `CostField`、integration、flow direction、LOS/pathable/reachable 等 flag。
3. portal graph A* 已实现文献 23.5 的 merging A*：同目标、同 world version 的既有 path suffix 会作为带真实 suffix cost 的终点候选；只有总代价不劣于直接 A* 终点时才合并，不再使用 merge priority bias。
4. portal graph transition/access 成本以 portal center 为节点语义：宽 portal window 不再把所有槽位当作零成本节点，避免 sector 角接触时虚构低成本路线。
5. portal tile 的 LOS target 和 pending portal handoff 都绑定选中的对侧槽位。单位站在 portal window 任意格时，不会无视选中槽位直接穿到同索引对侧格。
6. final goal tile 的 LOS 格可以只保留 LOS flag、不写 flow direction；portal tile 不同。portal 是中间目标，LOS flag 表示能看见当前侧 portal goal cell，但运行时还要检查是否能看见选中的对侧槽位。因此 portal tile 的非 goal LOS 格必须同时保留 integration flow，避免对侧槽位不可见时产生 zero direction。
7. flow tile cache、shared goal field、moving target anchor、stable goal 复用已经承担移动目标共享。
8. runtime obstacle 和 cost stamp 走 dirty sector 重建，而不是全图同步重建。
9. island 是 flow grid 连通分支，不是 WorldCell。
10. `TryPrepareNavigationRequest()` 已在 `CharacterMoveComp.SetNavTarget()` / `MoveTo()` 入口提交 path request、shared goal field 和 flow tile chain；`TryPrepareSharedGoalRequest()` 复用同一入口。这对应文献 23.5 “path request 后提交 flow field requests”。
11. steering 已区分 LOS、flow direction、portal pending、final goal pending。
12. flow tile 构建走预算队列，`TryGetSteeringVelocityFixed()` 热路径不再同步 force-complete portal tile 或 final goal tile。
13. 瓶颈 lane commitment 已在同 sector LOS 后保持已承诺走廊轴，避免单位进入窄走廊后因斜向目标反复改轴。
14. 多单位目标占位只保留定点 goal occupancy、稳定 agent ID 顺序和同 Tick reservation；局部空间桶只服务显式诊断/查询，不再承载第二套 float idle recovery 或动态避让行为。
15. 位移期间主动寻路输入被切断，兼容后续推/拉系统。
16. authored source 层已支持多个 movement type grid；同一场景可以为小/大单位提供不同 walkable mask，运行时不会让大单位复用小单位底图。
17. authored source cost field 已进入主链路。`FlowNavigationGridAsset` 保存 cost；`FlowNavigationGridSource` 传入 cost；world build 直接使用 source cost；runtime dirty sector 从 source cost 局部重建后叠加动态障碍和 CostStamp。
18. `FlowFieldCrowdMovementSystemTests` 标记为 `SingleThreaded`。该系统是静态全局状态，测试并行会互相污染，导致瓶颈/半径/movement type 用例出现假失败；这不是运行时 fallback，也不是业务逻辑补偿。
19. FlowGrid 的 `WalkableMask` 语义是“单位中心可站空间”。authored grid 在 bake 阶段已经按 movement type hard clearance 过滤 footprint；runtime box obstacle 也会按当前 world agent radius 膨胀后写入 walkable mask。因此运行时查询 `IsNavigationPointClear`、`TryResolveLegalNavigationPoint`、`TryConstrainNavigationDisplacement`、steering predicted-step 等只能检查 residual clearance，不能再用完整单位半径对同一障碍二次膨胀。
20. 即使 residual clearance 为 0，点查询也必须先检查所在 FlowGrid cell 是否 walkable。不能因为 clearance 为 0 就只看 runtime overlay；否则会出现 “targetClear=True 但 segment target blocked” 的矛盾，最终表现为英雄/单位贴建筑角停住。
21. 位移约束不是 fallback。它的职责是把输入位移限制在 FlowGrid 中心空间内：direct segment 被 blocked cell 拦住时，应基于 blocked cell 边界求法线并沿切线滑动，而不是吞掉速度或用直线/随机候选替代。该修复覆盖英雄手动移动和自动单位最终 MoveExecutor 约束。
22. 移动目标的 flow tile 队列必须只服务仍被活动 path 引用的 tile。旧目标位置留下的 pending tile job 会被剪掉，当前活动 path 的 tile chain 会提升到队列前部，避免单位长期停在 `PendingPortal` 且 required tile 被数千个旧 job 淹没。
23. 非最终 portal tile 不能等待整条下游 tile 链。只要下游 sector 不是最终 sector，就用预构建的 sector portal access / transition 成本生成 seeds；只有“下一 sector 就是 final sector”时才需要下游 final tile 的 seam cost。这符合文献按 sector/portal 预计算连接成本的结构，也避免移动目标反复变更时 tile 依赖串行化。
24. portal graph A* 选择目标 sector 入口 portal 时，不允许同步构建 final goal tile。目标 sector 内 portal 到 goal 的代价直接读取预构建 portal access cost；精确 shared goal field / final tile 由预算队列后续补齐。这样 `TryGetSteeringVelocityFixed()` 热路径不会因为接敌或移动目标换格而卡主线程。

## 5. 仍与文献不同或未完成的点

### 当前主链路剩余风险

1. `FlowNavigationGridPrefabBaker` 已按 movement type footprint 生成中心可站空间，但它仍是离散 grid raster。窄边、斜边、角落会受 cell size、footprint 采样密度、collider 边界和 neighbor traversal 影响。若出现 island 被切碎或建筑对面错误直冲，先查 asset walkable、neighbor traversal、portal window、portal access cost，不要改 steering 补偿。
2. authored grid 已有 source cost field，但目前成本来源主要是墙边 blur、movement type hard clearance 和 runtime CostStamp。“道路偏好”“特殊地形成本”“设计师手工成本编辑”还不是完整编辑器数据源。
3. 多单位攻击同一目标的站位分散依赖 combat slot/cache、定点 goal occupancy 和同 Tick reservation，还不是文献 multiple goals 或完善 formation。若再次扎点，优先查 slot 分配、目标可达点、reservation 顺序、攻击距离判定和 collision radius。

### 后续工程化增强

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
8. `[FlowTileQueueTrace]` 是否出现 required tile 长期排在队列深处
9. 是否存在 `PendingPortal` 多帧但 `tileBuilds=0` 的队列饥饿
10. 日志是否开启导致性能样本失真

不再把 `NavMesh.CalculatePath`、`NavMesh.Raycast`、`NavMesh.SamplePosition` 列为运行时性能目标。若 Profiler 里又出现这些 API，说明旧链路回流，应当按 bug 处理。

## 7. 不要做的事

1. 不要把“朝最终目标直线走”作为 tile 未就绪策略。
2. 不要在 `TryGetSteeringVelocityFixed()` 热路径同步补完 world、runtime dirty、shared goal field 或任何 flow tile，包括 final goal tile 和 portal tile。
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

当前主链路目标是：严格 authored FlowGrid、无运行时 NavMesh、无静默 fallback。Portal graph path search 已包含真正的 merging A*，不是旧的合并优先级偏置。Portal transition/access 已按 portal center 节点语义计成本，portal handoff/LOS 已按选中对侧槽位推进，path request 会预提交 flow tile chain，flow tile 在预算队列构建，瓶颈车道会保留已承诺走廊轴。

v42 已删除无生产调用的 float goal occupancy、idle overlap recovery、agent leader/group/state shadow 和旧动态避让宽限链。保留的空间桶仅服务显式诊断/查询，保留的 Vector3 steering API 仅在入口量化后转调 fixed 实现；`SoldierAIBrain` 与 `GroupMoveManager` 不再同步或包装上述无行为消费者元数据，Flow agent 注册入口也不再接受无意义的 `isLeader` 参数。

v43 修复 pending shared-goal authority progress Hash 的无意义重复失效：只有 start sector 集合或 cell->sector demand 映射真实改变时才清除缓存；同一 demand 重复提交保持 O(1) 缓存命中，新增 demand 和 job 推进仍必须失效。回归同时校验 Hash 与 refresh count，因此不是只验证结果值相同。该优化不改变 authority/replay 内容，协议继续为 `Avenge-30Hz-v42` / protocol 42。

v44 修复 authored anchor 的 fixed authority 仍受 float shadow 影响的漏洞：`GridToWorldCenterFixed()` 现在只依赖 walkable mask、world build 时冻结的 `CellNavAnchorsFixedXZ` 和 fixed cell bounds，NaN/Infinity 的 float shadow 不再让中心回退到 cell center；`GridToWorldCenter()` 仍可单独读取 Unity Y 供表现使用。该修复不新增 serialized fixed payload，也不改变 authority/replay 内容，协议继续为 `Avenge-30Hz-v42` / protocol 42。

v45 修复静态碰撞 fixed authority 仍二次读取 float world shadow 的漏洞：`ResolveDeterministicFlowVelocityFixed()` 和逐 Tick agent static projection 都会消费 `LogicStaticCollisionShadowService.TrySolveFixed()`，其 source 现在直接携带 committed world 冻结的 Q32 cell/origin raw 与 Fix64 clearance raw，`LogicStaticCollisionWorld` 不再从 float `CellSize/Origin/EncodedCenterClearance` 重建。正常 authored raw 与回放内容不变，协议继续为 `Avenge-30Hz-v42` / protocol 42。

v46 将 fixed corridor descriptor 首次派生从整图同步预构建改为按查询局部构建：orientation/candidate/descriptor/resolved 使用 world-version 稀疏 cache，corner connector 搜索限制在 `narrowWidth` 的有界 Manhattan 队列，candidate component 首次解析后整体复用；runtime-dirty 提交继续失效对应 lookup 与 owner。descriptor 仍是纯派生 cache，只有会影响未来行为的 owner 进入 FullHash，协议继续为 `Avenge-30Hz-v42` / protocol 42。

v47 将 pending shared-goal 真实推进后的 authority progress Hash 从全量规范化改为增量摘要：`DeterministicCostHeap` 在 Push/Pop/Clear 时维护内容摘要，demand/settled 集合和 field map/set 在 Add/Update/Remove 时同步维护分域 token，FullHash 只组合 null/count/contentHash 与固定标量。256x256、sector=8、推进 8 个 operation 的红测从单次访问 50 个历史条目降为 0，并用真实容器反算校验防止绕过 helper。pending-job 内容契约改变，协议升级为 `Avenge-30Hz-v43` / protocol 43。

v48 删除 moving-target fixed pending 的 float setter 委托：整数格、pending key、旧 job 移除与 enqueue 由纯整数公共事务负责，float/fixed 入口分别写自己的位置表示；fixed 入口只把结果派生到 Vector3 诊断 shadow。真实 24 Tick 追击红测的 fixed->float 桥接从 22 次降为 0，不改变 v47 协议。

v49 将超长 fixed corridor component 改为逻辑 Tick 增量派生。8192x1 红测证明 v46 仍会在首次查询同步展开 8192 格；现每个 world lookup 只允许一个活动 component job，pending start 按最小 cell index 稳定选择，每 Tick 总预算 256。cell 在 BFS 发现时写入 component id，descriptor 完成时按 component id 提交，不再排序、复制完整 component 或逐格回填 descriptor。查询使用 Pending/Absent/Ready 三态；Pending 明确停速，短组件可在同 Tick 收集请求后用剩余预算完成并重算 owner。endpoint 数量受 narrowWidth 派生上限约束，异常多出口明确拒绝二向 owner。全部 pending/progress/completed 摘要进入 FullHash，协议升级为 `Avenge-30Hz-v44` / protocol 44。

v50 修复 LogicObstacle fixed authority 经 float 注册桥丢 raw 的漏洞。红测在 `16777216 + 1 raw` 坐标上实测 expected `68719476737`、actual `68719476736`；命令/Hash 保存 fixed，而 Flow runtime 保存了二次量化值。现 circle/box 都有正式 fixed 注册入口，权威 center、half-extents、radius 原样保存，dirty bounds 直接用 fixed；Vector3 只由 fixed 单向派生供诊断。float API 仅负责 Unity/Collider 边界的一次量化，LogicObstacleCommandService 直接调用 fixed API，不再经过 GroupMoveManager。无调用的 float circle/bounds sector rebuild 已删除，协议升级为 `Avenge-30Hz-v45` / protocol 45。

v51 修复动态障碍覆盖更新只标新 bounds 且跨 shape 留下旧字典项的问题。跨 sector 移动红测实测旧格在提交后仍 blocked，同 ID circle->box 红测实测两种 shape 同时存在，job `ac73b2cf671e4ffc96c860d8a790f139` 2/2 失败。注册事务现先移除同 ID 的旧唯一 shape，分别标记旧、新 fixed bounds，既能恢复旧 sector，也不会用两点包围矩形重建中间 sectors；Unregister 复用该逻辑，内部双 shape 异常明确报错。修复后 2/2 通过，job `56c8967416b94db7b6f2039bc86d77a3`，协议升级为 `Avenge-30Hz-v46` / protocol 46。

v52 补齐 fixed 导航目标入口。restroom queue 的 fixed slot 和 Friendly AI 的 fixed 帧初目标此前都先派生 Vector3，再由 CharacterMoveComp 量化回 fixed；大坐标相邻 raw 红测的 deterministic hash 从正确值 `17305443358333265389` 分叉为 `4126586236282013463`，job `82101b1309fd49e5a400887e4ba38172` 1/1 失败。现 `IMoveComp.SetNavTargetFixed` 原样提交 raw，queue 使用 fixed slot，Friendly AI 攻击追击/跟随使用 `MoveToFixed(LogicFramePositionFixed())`。修复后 1/1 通过，job `cf5513cefebd43339943c629c304bb39`，协议升级为 `Avenge-30Hz-v47` / protocol 47。

v53 明确并收敛 Flow float shadow 边界。逐处生产/消费者审计确认 fixed 目标占位只读取 `HasNavigationIntent`、`HasGoal`、`LastFixedFlowFrame`、`LastGoalWorldFixed`、`PositionFixed` 和 `RadiusFixed`；fixed steering 与静态撞墙截断读取 committed Q32/Fix64 world、fixed obstacle、fixed portal/corridor owner 和 `LastFixedFlowVelocity`。`ResolvedVelocityFrame/ResolvedVelocity/DesiredVelocity` 只服务 View、Gizmo 与显式诊断，不进入 `WriteDeterministicFrameDigest`。无调用者的 `PreviousResolvedVelocity`、邻居 float 预测、旧 fallback velocity 更新和旧 frame-intent 方法已删除，避免以后误接回 authority 链。

v54 补齐逻辑派生建筑位置的 fixed 出生边界：交互建造、回收和科技升级不再把 `owner.PositionFixed` 转成 Vector3 后重新量化；`BuildBuildingInternalFixed` 原样传递 raw，`ShowBuildingFixed` 只在 View 请求处派生 Vector3，并返回 LogicEntityId。该改动本身不改变 Flow world 算法，但保证动态建筑的 combat shape、LogicObstacleCommand 和后续 runtime-dirty 都从同一 fixed spawn pose 出发。Replay 当前为 `Avenge-30Hz-v49` / protocol 49。

v55 将 authored FlowGrid 的 fixed authority 前移到资产烘焙边界。此前生产资源只序列化 float cell/origin/anchor，进入关卡时由 world build 首次量化；接口红测 job `3cdd9d3ef1e949aa96c334464b977856` 证明 asset 不存在 fixed payload 契约。现 `NavigationGridFixedMath` 统一 IEEE float -> Q32 raw、raw -> Fix64、fixed cell center 和稀疏 anchor 16-byte little-endian 编解码；`FlowNavigationGridAsset` 序列化版本化 Q32 cell/origin 与 fixed anchor blob，`DerivedNavigationData` 升至 v3，缺失、版本错误、float shadow 漂移或 blob 不一致均明确抛错。`FlowNavigationGridSource` 的单 grid 入口调用 `SetAuthoredNavigationSourceFixed`，多 grid 入口逐项提交 `HasFixedAuthorityPayload=true` 的 raw/anchor；world/job 两支都直接复制 baked authority，encoded clearance 只用 fixed cell size 和 agent radius。float API 仅保留 Editor/test 边界。12 个 Lv1/Lv2/Lv3/LvTest Small/Medium/Large 资产已原地迁移且 GUID/meta 保持。serialized authority 内容改变，Replay 升级为 `Avenge-30Hz-v50` / protocol 50。

v56 删除 Building View 对导航权威的重复拥有。此前 `BuildingEntity` 会从 View 目录和 Transform 重建 combat/obstacle shape，`LevelEntity` 在预设生成后扫描全部 BuildingEntity 并立即预热障碍；与此同时 `LogicEntityState.ActivateRuntime` 已从 fixed `LogicObstacleShapes` 为同一稳定 ID 提交 lifecycle Add。只绑定逻辑状态、不初始化 View 目录并扰动 Transform 的红测 job `1d8dbeb67095479d8b31fffed5938f43` 1/1 明确失败。现 View 查询只返回绑定 LogicState 的 fixed shape，View obstacle ID/注册/移除/预热代码和 LevelEntity 全局扫描均已删除；无 View 建筑通过 `Lifecycle.ApplyFrame -> Obstacle.ApplyFrame -> LogicFrameRuntime.Tick` 在出生 Tick 移动前完成障碍应用。首帧命令历史改变，Replay 升级为 `Avenge-30Hz-v51` / protocol 51。

最新验证：fixed View shape 与无 View lifecycle obstacle 2/2 通过，job `09874f05f5074553b5088991ee8d4d48`；Entity/Obstacle/Replay 75/75 通过，job `7af391ad115a497c87c48f0ed3a3ad92`；Flow/GameplayHash/Replay 183/183 通过，job `73a21237c1d1435c805ff63a3e2e460d`；最终完整 EditMode 471/471 通过，job `ed1cb5b09b944b9f9b32334d4628c894`，失败/跳过 0、耗时 79.21 秒。严格 Launch -> Lv_2 实测 frame/time/entity/collision/damage/snapshot/obstacle 同为 2479，22 个实体生命周期计数闭合；障碍 history=38、unique ID=38、duplicate=0，且全部是 frame 1 fixed box Add。三个 Lv2 source fixed/derived 均为 true，Gameplay Hash=`16391799108515986567`、Navigation Hash=`12976966150889500283`，protocol=51、content=`Avenge-30Hz-v51`，八类异常/旧链/权限关键词为 0；Stop 后全部清零并清除 authored navigation source。

v57 收紧实体位姿和 Flow moving-target 所有权。`IEntityContext.PositionFixed/Position/Rotation` 已改为只读，`LogicEntityState.PositionFixed/Rotation` 不再公开 setter，真实 `Position/Forward` setter 仅允许类内构造与 `MoveCommit` 写入。调用图同时确认旧 `TryResolveStableGoalCell(Vector3)`、float reachable-goal 与 moving-target active/pending setter 是零外部调用的死链；它们及专用计数器已删除。公开 Vector3 steering 兼容入口仍只在边界量化一次并转调 fixed 实现，诊断 Vector3 shadow 不进入 authority digest。接口红测 job `d97c723bd70146959e6e16c208e35ea4`、private setter 红测 job `458e79e23bc646d885a11e520e43e7eb` 和旧链结构红测 job `ea05f43a7c2c4e4fb2e1df9874b531ed` 均先失败后修复；该改动不改变可达 replay 内容，协议保持 v51。

v57 最新验证：精确组合 3/3 通过，job `95b7d1f7e51e459c8bdaf8ce177e11d2`；Flow/Entity/GameplayHash/Obstacle/Replay 251/251 通过，job `001f9b1405274a628f5f3f5cdf7984cb`；完整 EditMode 473/473 通过，job `5227f0f56d394f9eae37a80198ede1ec`，失败/跳过 0、耗时 70.24 秒；编译 0 warning、0 error。严格 Launch -> Lv_2 实测 frame/time/collision/damage/snapshot/obstacle 同为 1695，22 个实体生命周期计数闭合；38 个障碍全部是 frame 1 fixed box 且稳定 ID 无重复。Gameplay Hash=`3732805717685990585`、Navigation Diagnostic Hash=`10258173786119897539`，protocol=51、content=`Avenge-30Hz-v51`，八类异常关键词为 0；Stop 后只剩 Launch，服务、帧、实体、障碍和 authored source 全部清零。

最近一次实测卡墙角的根因不是 steering 方向补偿，而是队列调度和依赖模型：移动目标反复刷新留下大量旧 pending tile jobs，活动单位所需 tile 被压到队列数千位之后，导致长期 `PendingPortal`；同时非最终 portal tile 过度依赖下游 tile，会把本可由 portal access 解决的 tile 链串行化。已修复为活动 path 提升、非活动 job 剪枝、非最终 portal tile 使用预构建 portal access seeds、目标 sector portal access 不再同步构建 final tile。

历史已确认并修复的两个位移/贴边根因仍保留为排查依据：一是 FlowGrid 中心空间被完整半径二次 clearance 压窄；二是 residual clearance 为 0 时点查询跳过 walkable cell，导致清晰度诊断和 segment 约束矛盾。若新测试仍出现“隔建筑直冲”“贴边慢移”“多单位扎点”“island 异常增多”，优先从 FlowGrid asset、source cost、neighbor traversal、portal window、portal access cost、goal resolve、combat slot、flow tile queue、位移约束 segment 诊断和日志采样查根因，不要先做表层速度补偿。
