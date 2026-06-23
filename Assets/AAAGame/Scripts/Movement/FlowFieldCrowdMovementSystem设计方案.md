# Flow Field Crowd Movement System 现状审查与后续方案

## 0. 文档原则

本文档只记录对后续实现有帮助的事实和决策，不再保留历史流水账。

权威来源是 `GameAIPro_Chapter23_Crowd_Pathfinding_and_Steerin.pdf`，当前 md 只能作为参考。若实现、旧文档和论文冲突，先按论文重新判断，再结合 Avenge 的工程约束决定是否保留差异。

目标不是“看起来像流场”，而是让真实战斗场景里大量单位在建筑、窄路、拐角、移动目标、交叉路线和位移效果下稳定移动，并且不能用隐藏 fallback 掩盖链路问题。

## 1. 论文第 23 章核心要求

### 23.3 地图分区与 Portal Graph

论文将世界划分成固定网格 sector，每个 sector 内有成本场、积分场、流场。相邻 sector 之间按可通行边界生成 portal window。portal window 的中心作为 N-way graph 节点，portal 与同 sector 内其他 portal 通过边相连。

对效果的影响：

1. 全局路径不能是直线追目标，必须先走 portal graph。
2. 单位在建筑对面追目标时，应先选择绕建筑侧角对应的 portal/窗口，而不是朝建筑正面撞。
3. portal window 的宽度和连续性会直接影响窄口、拐角、贴边路径质量。

### 23.4 三类 field

论文使用三类 field：

1. `CostField`：8-bit，255 是墙，1-254 是通行成本；坡度、沼泽、墙边 blur、设计师成本都应写到这里。
2. `IntegrationField`：累计到目标的成本，论文示例是 16-bit cost + flags，也允许 float 换更好质量。
3. `FlowField`：8-bit，低 4 bit 是方向索引，高 4 bit 是 pathable/LOS 等 flags。

对效果的影响：

1. 道路偏好、墙边收缩、坡度偏好必须进入 `CostField`，不能放到 steering 层补偿。
2. flow 方向必须来自 integration，而不是直接朝目标点。
3. clear sector 应尽量共享存储，避免全图成本和积分常驻。

### 23.5 Path Requests

论文的 path request 是一个或多个 source 到一个 goal。第一个 source 跑 portal graph A*，后续 source 使用 merging A*，优先并入已有 portal 路径，以共享后续 flow tile，并让同目标队伍自然汇流。

对效果和性能的影响：

1. 多个单位追同一移动目标时，不应每个单位独立做完整路径。
2. 单位越多，越应该复用同一个 goal field、portal path 和 flow tile。
3. 当前项目如果只保留 per-agent query，即使内部有缓存，几百单位时仍可能有过多重复入口开销。

### 23.6 Integrator

论文的 integrator 对单个 flow tile 分多步构建：

1. 用最终目标或 portal window 作为初始 wave front。
2. 最终目标 tile 做 LOS pass；LOS 内单位直接朝精确目标移动。
3. 从 WaveFrontBlocked 边界做成本积分，使用 Eikonal 思路传播成本。
4. flow pass 比较 8 个邻居，写入最便宜方向。
5. portal tile 可携带下游 tile 的成本，让跨 sector 的方向连续。

对效果的影响：

1. 拐角不卡、不过度贴墙，主要依赖正确的 LOS、WaveFrontBlocked、成本积分和 portal seed。
2. 如果 portal tile 没有下游成本，只用 portal 单点，会出现朝墙角/建筑正面走的问题。
3. flow tile 构建应可跨 tick，不能在查询热路径里同步 drain。

### 23.7 Flow Field Cache

论文强调 flow tile 通过唯一 ID 缓存，可引用计数，不再使用时丢弃，也可以预构建/压缩到磁盘。

对性能的影响：

1. 流场比 A* 快的前提是大量单位共享 tile。
2. 对稳定地图，预构建或磁盘缓存能消除首次构建尖刺。

### 23.8 动态环境与移动查询

论文处理动态变化的方式：

1. source 移动出原路径 sector 后，重新跑 merging A*。
2. goal 移动时重建 goal flow field；如果跨 sector，再重建 portal path。
3. 墙、坡度、成本变化时，标记相关 cost sector 和 portal dirty，按优先级队列分帧重建受影响路径。

对性能的影响：

1. 建造建筑、动态障碍、CostStamp 不能在当前帧同步重建完整世界。
2. 查询链路不能发现 dirty 就无限预算补完。
3. 移动目标要按目标格/目标 sector 共享，而不是每个追踪者重算一遍。

### 23.9 Cost Stamps

论文的 cost stamp 是一组自定义成本，替换目标区域的 cost，并标记 overlapping sector dirty。论文实现记录原 cost 便于移除。

对项目的判断：

当前实现从 base source + active stamps 重建 dirty sector，不需要逐 stamp 记录原值；这是实现方式不同，但语义等价，前提是所有成本源都能重新生成。

### 23.10 Source Data

论文成本数据来自编辑器：几何、墙、坡度、墙边 blur、设计师可视化和手工修改。

对项目的影响：

如果没有编辑器成本层，道路偏好、地形偏好、特殊建筑通行偏好只能临时塞到 runtime stamp 或 steering，长期会让效果不稳定。

### 23.11 Multiple Movement Types

论文为每种 movement type 构建独立 cost field 和 portal graph。大型单位可以通过更高墙边成本做 cushioning。多选混合 movement type 时，用最受限的 movement type 产生共享路径。

对项目的影响：

当前每个 NavMesh agent type 可以有独立 world，但还缺“多选/多源入口使用最受限 movement type”的上层命令。现在项目暂不使用多选移动，但应保留接口规划。

### 23.12 Steering with Flow Fields

论文 steering 规则：

1. 没有有效 flow field 时，朝下一个 portal 走。
2. 有 LOS flag 时，朝精确目标走。
3. 否则使用 flow direction。
4. 跨格时缓存 path direction 并平滑混合，减少方向跳变。

对项目的影响：

pending portal 方向不是 fallback，而是论文明确要求的“tile 未就绪时朝下一个 portal 走”。但它必须来自 portal access/integration，不能退化成朝最终目标直线走。

### 23.13 Walls and Physics

论文允许单位被推、滑、爆炸、拉扯，因为 flow movement 很便宜，单位被外力带离原路径后可以继续恢复。

对项目的影响：

位移期间必须禁止主动寻路输入，只让外力/位移生效；位移结束后再回到 flow 查询。推力是瞬时动量，拉力是持续变化力，这两者不应和主动寻路叠加抢控制权。

### 23.14 Island Fields

island 是 flow grid 上的连通分支，不是 WorldCell，也不是玩法区块。sector 可以记录统一 island id；混合 sector 才需要逐 cell island field。

对项目的影响：

1. island 用于快速判定目标是否可达。
2. 多 island 不一定是 bug；但如果主可走区域被采样/邻接规则错误切碎，就是底图 bug。
3. 不可达目标应解析到同 island 最近可达点，但这不能掩盖底图连通性错误。

### 23.15 CPU Footprint

论文要求限制每 tick 提交的 tile 或 grid 工作量；integration 内存独立，因此可以分线程。

对项目的影响：

1. 分帧和预算不是拍脑袋，而是论文明确原则。
2. 但分帧只解决尖刺，不解决错误的每单位重复工作。
3. 未来几百单位时，需要把 CPU 大头迁到共享缓存、局部空间结构、Jobs/Burst 或预构建。

### 23.16 Future Work

论文未来方向：3D、预处理并压缩所有 flow permutations、分层 sector/graph、GPU、multiple goals。

项目当前不需要全部实现，但 multiple goals 对“多个英雄/多个等价目标”会有长期价值。

## 2. 当前真实运行链路

### 2.1 初始化与世界构建

1. `GroupMoveManager.Update()` 每帧调用：
   - `FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue()`
   - `FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue()`
   - `FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue()`
2. `LevelEntity` 在运行时初始化完成前调用 `PrewarmNavigationWorlds()`。
3. `LevelEntity.DoRebakeNavMesh()` 后会 `InvalidateNavigation()`；同步 `PrewarmNavigationWorlds()` 只能发生在没有 active navigation agent 的场景。

风险：

`PrewarmNavigationWorlds()` 是同步入口，只能用于加载期、空场景或明确可接受停顿的阶段。

### 2.2 单位移动入口

实兵自动寻路入口是：

`CharacterMoveComp.Move()` -> `FlowFieldCrowdMovementSystem.TryGetSteeringVelocity()` -> `MoveExecutor.SetInput()` -> `MoveExecutor.ExecuteMove()`

手动英雄移动入口是：

`PlayerMoveComp.Move()` -> `MoveExecutor.SetInput()`

因此英雄不走 flow pathfinding，但会受 `MoveExecutor` 的 NavMesh 约束/滑动影响。

### 2.3 位移兼容

`CharacterMoveComp.Move()` 在 `MoveExecutor.MovementMode != Normal` 时会清零主动输入并返回。`MoveExecutor` 在非 Normal 时只使用 external/override 位移，不叠加主动寻路输入。

这是后续推/拉位移系统必须保留的边界。

### 2.4 旧链路状态

`GroupMoveCoordinator` 和 `SteeringMovement` 仍存在，且 `GroupMoveConfig` 仍保留旧 LJ 参数。当前实兵移动链路不通过 `GroupMoveCoordinator.SubmitDesiredVelocity()`，但 `FlowFieldCrowdMovementSystem` 仍借用 `GroupMoveCoordinator.AgentState` 表达 Idle/Follow/Combat 优先级。

后续清理时应：

1. 保留必要的状态枚举或迁移到 flow 自己的状态类型。
2. 不再让旧 LJ/ORCA 协调器参与实战速度计算。
3. 单独处理旧测试，不要把旧协调器问题混入 flow 核心。

## 3. 未完成差异和缺失

### P1-1 编辑器成本源和可视化未完成

现状：

成本来自 NavMesh 栅格、墙边 blur、坡度、runtime obstacles、CostStamp。没有设计师可视化/手绘成本层，也没有稳定的道路/地形成本资产。

风险：

1. 后续“道路偏好”如果只靠 steering，会和全局路径冲突。
2. 建筑边缘、地形偏好、特殊区域无法稳定复现。

最佳修复方向：

1. 增加 `NavigationCostSource` 或 tile/grid asset。
2. 编辑器可视化 cost、island、portal、flow。
3. 道路、软禁行、地形惩罚统一写入 CostField。

### P1-2 movement type 上层策略未完成

现状：

每个 agent type 可拥有独立 world；大单位 wall cushioning 已存在。缺少多选/组移动入口里的 most-restrictive movement type 选择。

风险：

1. 未来混合单位群移动时，可能各走各的路径，破坏队伍一致性。
2. 如果强行共享某个单位类型路径，会让不可达单位卡住。

最佳修复方向：

先实现未接入玩法的多选移动入口：按单位 movement type 分组，兼容组使用最受限 type 的 shared path，不兼容组拆分请求。

### P1-3 调试日志可能影响性能判断

现状：

Flow/Combat/Constraint 诊断仍然比较多；详细日志应继续受 debug category、采样率或测试显式断言控制。

风险：

性能测试时如果 debug category 开启，会放大卡顿并干扰根因判断。

最佳修复方向：

1. 性能测试默认关闭 Move/Brain 详细日志，只保留 `[FlowPerf]` 汇总。
2. 详细日志必须按 character key、问题类型、采样率过滤。
3. 对高频异常日志加一次性摘要，不在每单位每帧刷屏。

### P2-1 离线预构建/磁盘缓存未实现

论文 23.7/23.16 提到可预构建所有 flow permutations 并压缩存盘。

当前未实现。稳定地图和固定建筑阶段可以考虑预构建 portal access、常用目标 flow tile 或 sector-pair 数据，减少首次移动成本。

### P2-2 多线程/Jobs/Burst/GPU 未实现

当前主线程分帧，符合论文最低要求；但几百单位和大地图时，world build、island、portal transition、tile integration 都适合迁到 Jobs/Burst。

### P2-3 分层 sector graph 未实现

当前单层 portal graph 对中等地图足够。超大地图应增加层级图，避免长距离 portal graph 搜索和缓存膨胀。

### P2-4 multiple goals 未实现

论文将 multiple goals 列为 future work。若未来有多个英雄、多个基地、多个等价攻击目标，multiple-goal flow field 可以让敌群自然选择最近目标，而不是每目标拆一套请求。

## 4. 性能审查重点

后续性能审查只关注仍可能成为瓶颈的部分：

1. 编辑器成本源缺失导致道路/地形偏好只能走 runtime stamp 或临时配置。
2. 多选/混合 movement type 入口缺失，未来大规模批量命令可能重复提交路径请求。
3. 详细日志如果未采样，会干扰实测帧率和 Profiler 结论。
4. clear tile descriptor 仍可继续压缩，portal seam summary 也还有内存优化空间。
5. integration、island、portal transition 仍在主线程分帧；大地图和数百单位后应评估 Jobs/Burst。
6. 稳定地图还没有离线预构建/磁盘缓存，首次打开大量 tile 仍可能有预算内延迟。

性能验证时要看：

1. `[FlowPerf] total/path/tile/steering/neighborAvoid/bottleneck/boundary/agentUpdate`
2. `pathBuilds`、`sectorPath(searches/cacheHits)`、`tileCache(hits/misses)`、`pending(sharedGoal/tile)`
3. `stableGoal(raw/reuse/initial/cell)`
4. Unity Profiler 中 `NavMesh.CalculatePath`、`NavMesh.Raycast`、`NavMesh.SamplePosition`
5. `SoldierAIBrain` 与 `FlowFieldCrowdMovementSystem` 各自耗时，避免把 AI 站位成本误判成 flow tile 成本

## 5. 后续实现队列

### 接着做

1. 多选/混合 movement type 的 most-restrictive path API。
2. 编辑器成本源和可视化。
3. 日志分级和采样。
4. clear tile descriptor 的进一步压缩和特殊场景专项优化。

### 中长期做

1. Jobs/Burst 化 integration、island、portal transition。
2. 稳定地图预构建/磁盘缓存。
3. 分层 portal graph。
4. multiple-goal flow field。

## 6. 不要做的事

1. 不要把“朝最终目标直线走”作为 tile 未就绪 fallback。
2. 不要在 `TryGetSteeringVelocity()` 热路径同步构建 world、runtime dirty、shared goal field 或非末端 portal tile。
3. 不要把道路偏好、墙边绕行、建筑侧角选择放到 steering 层硬推。
4. 不要用扩大 arriveDistance、减速、随机抖动掩盖底层 flow/path 错误。
5. 不要让位移期间主动寻路输入继续叠加。
6. 不要把 WorldCell 当可走格；可走性只看 flow grid + NavMesh/Cost/NeighborTraversal。
7. 不要因为最近可达点能避免报错，就停止追查主岛被错误切割的问题。
8. 不要让旧 `GroupMoveCoordinator` 的 LJ/ORCA 重新进入实战移动链路。

## 7. 代码所有权地图

1. `FlowFieldCrowdMovementSystem.cs`
   - flow grid、cost/integration/flow field、portal graph、island、runtime dirty、cache、steering、bottleneck。
2. `GroupMoveManager.cs`
   - Unity MonoBehaviour 入口、配置同步、agent/obstacle 注册、每帧队列驱动。
3. `CharacterMoveComp.cs`
   - 自动寻路单位的移动入口；不做路径算法。
4. `PlayerMoveComp.cs`
   - 英雄手动输入；不走 flow pathfinding。
5. `MoveExecutor.cs`
   - CharacterController 执行、NavMesh 约束、位移/外力仲裁。
6. `SoldierAIBrain.cs`
   - 状态机和目标选择；不放路径算法。
7. `GroupMoveCoordinator.cs` / `SteeringMovement.cs`
   - 遗留逻辑和旧测试；不应成为新寻路效果的核心。

## 8. 当前结论

本文档只保留未完成工作和实现约束。后续优先级是：多选/混合 movement type、编辑器成本源和可视化、日志采样、clear descriptor 进一步压缩、Jobs/Burst、预构建、分层图、multiple-goal flow field。
