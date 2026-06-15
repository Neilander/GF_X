# Flow Field Crowd Movement System 设计方案

## 1. 文档目标

本文档用于固化 Avenge 后续要实现的新寻路移动体系，防止实现周期较长、上下文压缩、多人协作或阶段性暂停后遗忘关键设计决策。

目标不是“在现有系统上继续补丁”，而是构建一套面向以下问题的长期可维护体系：

1. 大量单位同时移动时的群体避障效果。
2. 多个单位前往不同目标、路线相交时的自然交互。
3. 多个单位通过窄路、门洞、拐角时的稳定表现。
4. 建筑生成、道路拐角、动态阻挡下不容易卡死。
5. 后续兼容位移系统：
   - 推力：瞬时动量。
   - 拉力：持续变化力。
   - 位移期间不能主动寻路移动。

本方案以文献《Game AI Pro Chapter 23 - Crowd Pathfinding and Steering Using Flow Field Tiles》为主干，并针对 Avenge 项目约束增加两层扩展：

1. 窄口/瓶颈调度。
2. 位移仲裁。

---

## 2. 为什么必须重做，而不是继续修补当前系统

当前系统的根因不是“参数没调好”，而是架构职责混杂。

现状可概括为：

1. `CharacterMoveComp`
   - 负责 NavMesh 路径计算。
   - 负责角点推进。
   - 负责卡住检测。

2. `SoldierAIBrain`
   - 负责状态机。
   - 负责设导航目标。
   - 负责从 `GroupMoveCoordinator` 拿安全速度。
   - 还会把安全速度反写成新的 `MoveTo` 目标。

3. `GroupMoveCoordinator`
   - 名义上是群体协调器。
   - 实际上同时承担群体斥力、障碍修正、速度裁剪。
   - 但它修的是“局部速度”，而不是“稳定流向”。

4. `MoveExecutor`
   - 负责 CharacterController 执行。
   - 负责 NavMesh 边界约束。
   - 同时还叠加 override / external 位移。

这导致几个结构性问题：

1. 全局路径和局部避让互相抢方向。
2. 安全速度被回写成微小目标点，局部抖动会被放大成路径抖动。
3. 窄道死锁时没有专门的通行规则，只能靠斥力和随机破对称。
4. 拐角附近会出现“想切角”和“边界约束拉回”的对冲。
5. 位移效果进入后，会与主动寻路竞争输入。

因此，如果目标是“效果最优”，就不能继续在现有系统上做增量补偿。

---

## 3. 最终目标架构总览

新体系分五层：

1. 全局层：`Portal Graph Path Planning`
2. 中层：`Flow Field Tile Cache`
3. 局部层：`Predictive Crowd Steering`
4. 瓶颈层：`Portal Bottleneck Scheduler`
5. 执行层：`Movement Arbitration + Move Execution`

职责边界如下：

### 3.1 全局层

负责回答：

- 这次移动应该经过哪些 sector / portal。

不负责：

- 单位每帧具体朝哪一格走。
- 单位之间的碰撞避让。

### 3.2 中层

负责回答：

- 当前所在格子的主流向是什么。
- 是否已进入目标 LOS。
- 当前 tile 是否可复用。

不负责：

- 个体碰撞。
- 位移覆盖。

### 3.3 局部层

负责回答：

- 在当前 flow direction 基础上，怎样避开其他单位和局部障碍。

不负责：

- 决定大方向路径。

### 3.4 瓶颈层

负责回答：

- 窄门、窄桥、狭长走廊、拐角口的双向通行谁先过。

不负责：

- 全局寻路。

### 3.5 执行层

负责回答：

- 这一帧最终生效的速度来源是谁。
- 主动寻路是否被位移、攻击、控制等状态压制。

这是后续位移兼容的关键层。

---

## 4. 与文献方案的关系

## 4.1 直接沿用文献的部分

以下内容应尽量忠实采用：

1. 地图按固定 `sector/tile` 切分。
2. 用 `portal window` 连接 sector。
3. 路径请求先走高层 `portal graph A*`。
4. 每个 tile 维护：
   - `Cost Field`
   - `Integration Field`
   - `Flow Field`
5. 目标 tile 做 `LOS pass`。
6. flow tile 可缓存、可共享、可失效重建。
7. moving goal / dynamic dirty tile 走增量更新。

## 4.2 相比文献新增的两块

### A. 窄口调度

文献很强，但没有替 RTS/群体战斗项目直接解决“双向狭路死锁”的游戏规则层问题。

Avenge 必须增加：

1. 瓶颈识别。
2. 双向让行。
3. 通过权分配。
4. 超时重选。

### B. 位移仲裁

文献没有“推力瞬时、拉力持续、位移期间不能主动寻路”的业务约束。

Avenge 必须把移动输入进行统一仲裁，否则以后必然出现：

1. 位移中主动寻路又把角色拉回原路。
2. 拉力持续期间局部避让反向抵消。
3. 位移结束后重新算路导致抖动或跳变。

---

## 5. 地图表示与分层寻路

## 5.1 Sector 切分

运行时地图按固定尺寸切为 sector。

建议：

1. 逻辑层使用固定二维网格。
2. 每个 sector 尺寸统一。
3. sector 尺寸必须兼顾：
   - flow tile 复用率。
   - 动态重建成本。
   - 门洞/走廊跨 sector 的频率。

初版建议：

1. 将世界投影到 XZ 平面。
2. 用固定边长切分。
3. sector 内再切细胞格。

具体数值不要在文档里锁死，实现阶段通过地图尺度验证。

## 5.2 Portal Window

portal 表示 sector 边界上可通行的连续窗口。

每个 portal 至少包含：

1. `PortalId`
2. `SectorA`
3. `SectorB`
4. `WindowCells`
5. `WorldCenter`
6. `Width`
7. `Flags`

其中 `Flags` 至少包括：

1. 是否狭窄。
2. 是否允许双向并行。
3. 是否受动态障碍影响。

## 5.3 高层图

每个 sector 内 portal 之间预建图边，边代价由以下因素构成：

1. sector 内基础通行距离。
2. 地形代价。
3. 道路奖励或泥地惩罚。
4. 大小类型限制。

高层路径请求流程：

1. 起点定位到 sector。
2. 终点定位到 sector。
3. 在 portal graph 上跑 A*。
4. 得到 sector/portal 序列。
5. 为每个经过的 sector 申请 flow field tile。

---

## 6. Flow Field Tile 设计

## 6.1 三种 Field

每个 tile 维护三类数据。

### A. Cost Field

每格存基础代价。

来源包括：

1. 可走/不可走。
2. 静态障碍。
3. 道路偏置。
4. 地表类型偏置。
5. 单位尺寸通行限制。

### B. Integration Field

每格存到目标或到 portal window 的累计代价。

同时维护 flag：

1. `ActiveWaveFront`
2. `HasLineOfSight`
3. `WaveFrontBlocked`
4. `Blocked`

### C. Flow Field

每格存：

1. 主方向索引。
2. 可达标记。
3. LOS 标记。
4. 特殊处理标记。

## 6.2 目标类型

flow request 必须支持两类目标：

1. 最终目标格。
2. portal window。

这样才能实现 sector 级链式流动。

## 6.3 LOS Pass

目标 tile 做 LOS pass。

规则：

1. 目标附近可直达位置，直接标 `HasLineOfSight`。
2. 位于 LOS 内的 agent 不必再严格跟 flow arrow，可直接朝精确目标 steering。
3. LOS 边界要能延续到跨 sector portal。

目的：

1. 终点附近更平滑。
2. 拐角出口不容易产生“最后几步横跳”。

## 6.4 Flow Tile Cache

缓存键建议包含：

1. movement type
2. sector id
3. downstream portal id 或 final goal id
4. dirty version

缓存目标：

1. 多单位共享同一段走廊。
2. 目标轻微移动时尽量复用。
3. 动态障碍仅局部失效，不全图重算。

## 6.5 Dirty 策略

以下事件会触发 dirty：

1. 建筑生成/销毁。
2. 可走区域变化。
3. 临时阻挡体出现或消失。
4. movement type 对应的通行规则变化。

dirty 粒度：

1. 优先 sector 级 dirty。
2. 影响 portal 连通性时，联动 portal graph dirty。

---

## 7. 动态障碍与多 movement type

## 7.1 障碍分类

必须区分：

1. 静态硬障碍
   - 建筑
   - 墙
   - 永久地形阻挡

2. 半静态障碍
   - 临时封路
   - 可开闭门

3. 动态软障碍
   - 其他单位
   - 队列占位

静态/半静态进 cost field。  
动态软障碍不直接改全局 field，而进入局部 steering / 瓶颈调度。

## 7.2 movement type

至少预留：

1. 普通地面单位
2. 大体型地面单位
3. 特殊无视部分障碍单位

不同 movement type 应有独立：

1. sector cost interpretation
2. portal 可用性
3. flow cache key

---

## 8. Agent 运行时模型

## 8.1 每个 Agent 必须持有的导航态

建议新增独立导航状态对象，不再把状态散在 Brain / MoveComp / Coordinator 中。

每个 agent 至少持有：

1. `AgentId`
2. `MovementMode`
3. `CurrentSectorId`
4. `PathHandle`
5. `CurrentTileHandle`
6. `CurrentCell`
7. `CurrentFlowDirection`
8. `DesiredVelocity`
9. `ResolvedVelocity`
10. `Radius`
11. `MaxSpeed`
12. `IgnoreCrowd`
13. `DisplacementState`

## 8.2 PathHandle

`PathHandle` 不再是一串 corners，而是：

1. sector 序列
2. portal 序列
3. 当前处于哪一段走廊
4. 相关 flow tile 引用

## 8.3 TileHandle

指向当前 sector 的 flow tile 缓存项。

当 agent 穿过 portal 后切到下一个 tile。

---

## 9. 局部 Steering 设计

## 9.1 原则

局部 steering 只做短时尺度修正，永远不能取代全局/中层路径。

即：

1. 大方向由 flow field 决定。
2. 局部避让只修正速度，不改 global path。
3. 局部避让不得把 agent 长时间推离走廊而无恢复机制。

## 9.2 输入

局部 steering 输入包括：

1. 当前 flow direction
2. LOS 直达目标方向
3. 周围 agent 的位置、速度、半径
4. 动态局部障碍
5. 当前 portal / bottleneck 状态

## 9.3 输出

输出为：

1. `DesiredVelocity`
2. `SteeringAdjustedVelocity`
3. `ResolvedVelocity`

不能输出新的“路径点”。

## 9.4 避让模型

当前项目的纯斥力模型不够。

需要改成预测式模型，核心考虑：

1. 相对速度。
2. 预计碰撞时间。
3. 最小分离距离。
4. 切向绕行优先级。

可保留斥力项，但只能作为补充，不可作为主机制。

建议局部 steering 由以下部分组成：

1. `Flow Seek`
2. `Goal LOS Seek`
3. `Predictive Agent Avoidance`
4. `Static Boundary Avoidance`
5. `Lane Bias`
6. `Portal Scheduler Bias`

## 9.5 Lane Bias

为了让交叉流、窄道会车更自然，需要加入轻量车道偏置。

例如：

1. 在狭长走廊中，根据流向和 corridor 主轴偏向一侧。
2. 对向流时每方保持稳定侧偏，而不是临时左右乱选。

这样可以显著降低对冲卡死。

---

## 10. 瓶颈调度设计

这是本项目关键增强层。

## 10.1 为什么必须有

仅靠 flow field + 局部避让，以下场景仍可能出问题：

1. 单格门洞双向对冲。
2. 狭长走廊中段相遇。
3. 90 度拐角出口同时抢出。

这些问题不是“再加点斥力”能彻底解决的，而是需要明确通行协议。

## 10.2 Bottleneck 定义

识别条件可包括：

1. portal 宽度低于阈值。
2. sector 内 corridor 有长时间单文件通行特征。
3. 拐角出口有效可视面积过小。

## 10.3 运行时状态

每个 bottleneck 维护：

1. 当前主通行方向
2. 占用方
3. 排队列表
4. 等待超时
5. 切换冷却

## 10.4 让行规则

建议优先级：

1. 已进入瓶颈的单位优先出清。
2. 成组流优先连续通过，减少频繁换向。
3. 超时等待方可抢占。
4. 高优先级任务可插队。

## 10.5 与局部 steering 的关系

bottleneck scheduler 不直接控制位移，只提供 bias：

1. 允许通行方获得正向通行 bias。
2. 等待方获得减速/驻停 bias。
3. 进入口附近 agent 会被约束在队列槽位附近。

---

## 11. 位移仲裁设计

这是后续位移系统兼容的核心。

## 11.1 原则

主动寻路、局部避让、推力、拉力、攻击锁定、控制效果，不能各自直接写 `MoveExecutor`。

必须有唯一仲裁层统一决定本帧速度。

## 11.2 移动源分类

定义以下输入通道：

1. `Locomotion`
   - 正常寻路、跟随、追击、返航

2. `Impulse`
   - 瞬时推力
   - 击退
   - 冲击

3. `SustainedForce`
   - 持续拉力
   - 牵引
   - 漩涡

4. `ControlLock`
   - 攻击硬直
   - 技能施法锁
   - 剧情锁

## 11.3 MovementMode

建议至少定义：

1. `Normal`
2. `Displaced`
3. `HardLocked`

规则：

### Normal

- 允许主动寻路。
- 允许 crowd steering。

### Displaced

- 禁止主动寻路输出速度。
- 禁止 flow steering 输出速度。
- 保留路径句柄，不丢目标。
- 仅允许 impulse / sustained force 影响执行层。

### HardLocked

- 不允许任何主动 locomotion。
- 是否允许外力取决于具体控制效果。

## 11.4 恢复规则

位移结束后：

1. 不立即清空路径。
2. 先重新定位当前 sector/cell。
3. 检查旧 path handle 是否仍有效。
4. 有效则继续沿原走廊恢复。
5. 无效才触发重算。

这个规则非常重要，能避免位移结束后的乱跳和抖动。

---

## 12. 执行层设计

## 12.1 MoveExecutor 职责收敛

新的 `MoveExecutor` 只做：

1. 接收最终仲裁后的速度。
2. CharacterController 执行。
3. 地形/重力/最终边界约束。

不再承担：

1. 路径逻辑。
2. crowd 逻辑。
3. 位移仲裁逻辑。

## 12.2 NavMesh 的新定位

如果采用 flow field 主架构，NavMesh 不再是每单位主路径来源。

它的新用途是：

1. 场景可走区域采样来源。
2. sector/build 数据烘焙来源。
3. 最终执行层安全夹取参考。

也就是说：

NavMesh 变成“静态空间数据源”，不再是“每个单位每帧角点导航器”。

---

## 13. 与现有脚本的迁移策略

## 13.1 保留但重写职责

### `MoveExecutor`

保留类名，重写内部职责边界。

### `CharacterMoveComp`

保留作为“Locomotion driver”，但彻底移除：

1. 现有角点追踪。
2. 每帧 `MoveTo(target)` 微目标用法。
3. 卡住后直接清目标的逻辑。

它改为：

1. 维护 agent 导航态。
2. 从 flow tile 采样主方向。
3. 生成 locomotion desired velocity。

### `GroupMoveCoordinator`

保留命名可以，但建议职责重构甚至拆分：

1. `CrowdSteeringSystem`
2. `BottleneckScheduler`

因为现在这个类名下塞太多东西，后续会继续失控。

## 13.2 不应继续保留的旧思路

以下逻辑应废弃：

1. 用 `safeVelocity` 回写 `MoveTo(currentPos + velocity)`。
2. 窄道卡住后简单清空目标。
3. 障碍和人群统一用 LJ 斥力硬推。
4. Brain 自己拼寻路和避让。

## 13.3 Brain 层的新职责

`SoldierAIBrain` 只做：

1. 选目标。
2. 选模式。
3. 发起导航请求。
4. 决定何时暂停/恢复 locomotion。

不再直接决定具体路径点和局部修正。

---

## 14. 运行时流程

每帧推荐执行顺序：

1. 更新实体位置到导航系统。
2. 更新动态障碍、bottleneck 状态。
3. Brain 刷新目标/模式。
4. Path/Tile 系统处理增量请求与 dirty。
5. Locomotion 根据 flow field 生成 `DesiredVelocity`。
6. Crowd steering 生成 `SteeringAdjustedVelocity`。
7. 位移系统生成 `Impulse/SustainedForce`。
8. Movement Arbitration 统一裁决。
9. MoveExecutor 执行最终速度。
10. 回写位置、速度、动画朝向。

关键要求：

位移相关输入必须在仲裁前进入，不允许像当前一样晚于 locomotion 才生效。

---

## 15. 数据结构建议

以下为建议性数据结构，不要求完全照搬命名。

## 15.1 SectorData

包含：

1. `SectorId`
2. `Bounds`
3. `CellSize`
4. `BaseCostField`
5. `PortalList`
6. `DirtyVersion`

## 15.2 PortalData

包含：

1. `PortalId`
2. `FromSectorId`
3. `ToSectorId`
4. `WindowCells`
5. `Width`
6. `IsNarrow`
7. `TraversalFlags`

## 15.3 FlowTileCacheEntry

包含：

1. `CacheKey`
2. `CostField`
3. `IntegrationField`
4. `FlowField`
5. `GoalInfo`
6. `DirtyVersion`
7. `RefCount`
8. `LastUsedFrame`

## 15.4 AgentNavState

包含：

1. `AgentId`
2. `CurrentSectorId`
3. `CurrentCell`
4. `PathHandle`
5. `FlowTileHandle`
6. `MovementMode`
7. `DesiredVelocity`
8. `ResolvedVelocity`
9. `Radius`
10. `Priority`

## 15.5 BottleneckRuntimeState

包含：

1. `PortalId`
2. `CurrentDirection`
3. `Occupants`
4. `WaitingQueueA`
5. `WaitingQueueB`
6. `SwitchCooldown`
7. `TimeoutAccumulator`

---

## 16. 风险点与对应策略

## 16.1 实现复杂度高

风险：

1. 模块多。
2. 状态同步复杂。
3. 初版容易出现脏数据或 tile 失效错误。

策略：

1. 先实现单 movement type。
2. 先做固定大小静态地图。
3. 每层都加可视化调试。

## 16.2 动态更新错误

风险：

1. 建筑变化后旧 tile 未失效。
2. portal graph 连通性未刷新。

策略：

1. 明确 sector dirty 事件链。
2. 建立版本号系统。
3. tile cache key 绑定 dirty version。

## 16.3 位移恢复错误

风险：

1. 位移结束后路径句柄悬空。
2. 恢复时突然大幅转向。

策略：

1. 恢复前强制重定位 cell。
2. 校验 path handle 是否仍在有效 corridor。
3. 恢复帧允许短时重新对齐，但不清空目标。

## 16.4 瓶颈规则过强

风险：

1. 过度调度会让单位显得机械排队。

策略：

1. 只在识别为窄口时启用。
2. 宽走廊仍主要依赖 flow + local steering。

## 16.5 性能风险

风险：

1. tile 重建过多。
2. 邻居查询成本高。

策略：

1. tile cache。
2. spatial hash / uniform grid 做邻居查询。
3. 每帧限制重建预算。

---

## 16.6 当前实现对照与已验证修正

本节用于记录“文档方案与当前代码实现”的实际落地状态，尤其记录已经被日志验证过的关键修正，避免后续上下文压缩后重复走弯路。

### 16.6.1 当前已落地且与文档主干一致的部分

已实现并验证的主链路：

1. `Portal Graph Path Planning`
   - 单位先解析当前 sector / goal sector。
   - 使用 sector + portal 序列驱动跨 sector 移动。

2. `Flow Field Tile Cache`
   - tile 按 `sector + goal kind + goal id/downstream hint + dirty version + agent type` 缓存。
   - 支持最终目标 tile 与 portal tile 两类目标。

3. `Portal tile downstream integration`
   - portal tile 的 integration seed 不再是“portal 全窗统一零代价”。
   - 当前实现改为读取下游 tile 对应 portal 槽位的 integration cost，再加跨 portal 常量代价。
   - 这样 portal 内不同槽位会保留下游路径优劣差异。

4. `LOS Pass`
   - tile 级别仍保留 `HasLineOfSight`。
   - LOS 会参与 `DesiredDirection` 决策。

5. `Local Steering`
   - 已有 `agent avoidance + boundary avoidance + lane bias + bottleneck bias` 组合。
   - 输出仍然是速度，不回写新的路径点，符合文档第 9.3 节。

6. `Bottleneck Scheduler`
   - 已有 narrow portal 识别、通行方向、等待超时、切换冷却、队列 bias。
   - 当前实现已能对窄口进行限速/等待/偏置，而不是只靠斥力。

7. `Movement Arbitration`
   - `MoveExecutor` 已有 `MovementMode`。
   - `CharacterMoveComp` 在非 `Normal` 模式下会阻断主动寻路输入。
   - `DurationMoveEffectComp` 会在持续位移期间把执行层切到 `Displaced`。
   - 这与文档“位移期间不能主动寻路移动”的约束一致。

### 16.6.2 与文档相比，当前实现仍是工程化近似的部分

以下部分已经可用，但还不能视为文档中的“最终形态”：

1. `Predictive Agent Avoidance`
   - 当前仍以相对位置/速度驱动的局部避让为主，已经比旧纯斥力更稳定。
   - 但还没有完全演化到文档里理想化的、系统化的 TTC 风险模型。

2. `Bottleneck 识别范围`
   - 当前主要基于 narrow portal 触发。
   - 对“长走廊中段”和“拐角出口可视面积过小”的识别还不完整。

3. `Portal LOS` 的目标语义
   - 文档只要求“LOS 内可直接朝精确目标 steering”。
   - 当前实现已经进一步细化为“portal LOS 目标必须服从 downstream integration 最优槽位语义”。
   - 这是必要的工程化补充，不是偏离文档。

### 16.6.3 已被日志钉死并修复的关键问题

#### A. 单位在建筑、道路拐角、窄口附近偶发卡住

根因不是单一参数，而是多段链路叠加：

1. portal tile 若不使用下游 integration 差异，单位会把整个 portal window 视作同代价目标。
2. portal LOS 若再按“最近对侧 cell 中心”取目标，会把中层 flow 语义覆盖掉。
3. 单位随后容易贴着边界切向前进，最终在拐角/死角里进入低效路径甚至卡住。

已做修正：

1. portal tile integration seed 改为使用下游 tile 对应槽位的 integration cost。
2. portal LOS 选点不再按最近距离，而是按“可见槽位里 downstream integration cost 最低”选点；同成本才比较距离。
3. 动态避让里排除了不该参与的静态建筑阻挡实体，避免局部 steering 把建筑再次当作动态邻居重复推开。

这组修正的意义：

1. flow field 与 LOS 使用同一套“哪一个 portal 槽位更优”的语义。
2. 单位穿 portal 时不再只会沿 portal 边界最近点做横平竖直吸附。
3. 拐角和窄道中的路线会优先延续真实可通行方向，而不是朝边缘死角硬靠。

#### B. 单位在 portal 阶段出现“横平竖直移动”，无视斜向目标

这是本轮最关键、且已经被日志百分百验证的根因。

错误链路：

1. `FlowPortalTarget` 旧实现对 vertical/horizontal portal 都按“当前单位最近的对侧同索引 cell 中心”选目标。
2. 当 portal 边界与世界轴平行时，这个目标常常只在 `x` 或 `z` 上与单位有差值。
3. 于是 `ResolveDesiredDirection` 里的 LOS 覆盖会得到纯水平或纯垂直 `desiredDir`。
4. 即使 flow 已经是正确的斜向方向，也会被 portal LOS 压成横平竖直。

日志证据模式：

1. `flow` 已经是 `(0.71, 0.71)`、`(0.00, 1.00)` 等正确方向。
2. 但 `tileTarget` 被选成与当前点同 `z` 或同 `x` 的 portal 对侧点。
3. `losDir` 因此变成 `(1, 0, 0)` 或 `(0, 0, 1)`。
4. `source=LineOfSight` 覆盖了 `FlowField`。

修正后规则：

1. `ResolveTileTargetPosition` 在 portal 模式下，先枚举可见 portal 槽位。
2. 在所有可见槽位中，优先选 `goal cell integration` 最低的槽位。
3. 只有 integration 成本近似相同时，才用距离作 tie-break。

修正后的期望日志特征：

1. `FlowPortalTarget` 应显示 `selection=LowestVisibleCost`。
2. 候选列表中可看到 `goalCost` 梯度，而不是只看 `distSq`。
3. `FlowPortalResolve` 的 `flowDotLos` 应明显高于旧实现，常见为接近 `1`。
4. portal 段 `desiredDir` 不再系统性退化成纯水平或纯垂直。

#### C. “表层补偿”风险被明确禁止

本次实现过程已经验证：对卡住和贴墙问题，不能先改 steering 权重再观察。

需要长期记住的规则：

1. 先用日志区分是 `path/portal/tile` 的中层错误，还是 `steering/executor` 的下层错误。
2. 如果 `flow` 本身是对的，而 `desiredDir` 错，就优先看 LOS / tile target。
3. 如果 `desiredDir` 对，但 `resolvedVel` 错，再看 avoidance / boundary / executor。
4. 不允许用 fallback 或额外兜底掩盖中层寻路语义错误。

### 16.6.4 与位移兼容相关、已确定不能回退的规则

后续实现推力/拉力时，以下行为已经确定，不能再退回旧链路：

1. 位移期间不能继续主动寻路写入执行层。
2. 主动寻路与位移必须只通过 `MovementMode` / 执行层仲裁交汇。
3. 不能把位移实现成“外部速度存在，但寻路仍继续写 input velocity”。
4. 任何寻路修复都不能破坏 `CharacterMoveComp -> MoveExecutor` 的位移阻断语义。

这条约束非常重要，因为寻路系统后续还会继续调整，但位移仲裁边界已经属于核心契约。

---

## 17. 调试可视化要求

实现时必须提供调试视图，否则后续很难排查卡住根因。

至少可视化：

1. sector 边界
2. portal window
3. portal graph path
4. tile cost field
5. integration field heatmap
6. flow field arrows
7. agent current cell
8. desired velocity
9. resolved velocity
10. bottleneck queue / direction
11. movement mode

---

## 18. 测试策略

必须补齐编辑器测试和场景回归测试。

## 18.1 纯逻辑测试

覆盖：

1. integration field 正确性
2. flow direction 正确性
3. cache 复用/失效
4. portal graph 选路
5. bottleneck 调度
6. movement arbitration

## 18.2 群体行为测试

覆盖：

1. 多单位同目标穿窄门
2. 双向对冲过单门
3. 十字交叉流
4. 拐角会车
5. 大单位与小单位混行
6. 动态建筑生成后重路由

## 18.3 位移兼容测试

覆盖：

1. 推力中不主动寻路
2. 拉力持续期间不主动寻路
3. 位移结束后恢复旧目标
4. 位移后若旧 path 失效则触发重算
5. 攻击锁、位移、寻路三者优先级

---

## 19. 分阶段落地计划

## Phase 1：基础设施

1. sector/portal 数据结构
2. 静态 cost field 烘焙
3. portal graph A*
4. flow tile 构建与缓存

验收标准：

1. 单位可不依赖 corner path，而靠 flow 走到目标。

## Phase 2：局部 crowd steering

1. predictive avoidance
2. static boundary avoidance
3. lane bias

验收标准：

1. 多单位交叉流明显优于现系统。

## Phase 3：瓶颈调度

1. 窄口识别
2. 双向让行
3. 队列槽位

验收标准：

1. 窄门、窄桥、拐角出口不再频繁死锁。

## Phase 4：位移仲裁

1. locomotion / impulse / sustained force / lock 四类输入统一
2. `MovementMode` 生效
3. 恢复规则完整

验收标准：

1. 推、拉、击退与主动寻路不互相打架。

## Phase 5：迁移与清理

1. 移除旧 corner 驱动链路
2. 收敛 Brain 和 MoveComp 职责
3. 清理旧协调器遗留参数

---

## 20. 结论

Avenge 的最佳方案不是继续强化“单体路径 + 斥力补偿”，而是切换到：

1. `Portal Graph`
2. `Flow Field Tile`
3. `Predictive Crowd Steering`
4. `Bottleneck Scheduler`
5. `Movement Arbitration`

这是唯一能同时满足以下目标的架构：

1. 大量单位群体移动自然。
2. 交叉流表现稳定。
3. 窄路/门洞/拐角不易卡死。
4. 动态环境可增量更新。
5. 后续位移系统可以无缝兼容。

后续实现时，如与临时工程便利性冲突，以本方案的分层原则为准，不回退到“每帧角点追踪 + 局部斥力补偿”的旧思路。
