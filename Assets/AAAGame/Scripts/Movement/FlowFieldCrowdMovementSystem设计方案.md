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
   - 已有 `narrow portal + 长窄走廊中段 + 拐角低可视出口` 识别、通行方向、等待超时、切换冷却、队列 bias。
   - 当前实现已能对窄口和同 sector 内的局部单文件通道进行限速/等待/偏置，而不是只靠斥力。
   - 运行时状态已补入 `current owner`、等待队列顺序、短暂出清保持、同向后继流检测。

7. `Movement Arbitration`
   - `MoveExecutor` 已有 `MovementMode`。
   - `CharacterMoveComp` 在非 `Normal` 模式下会阻断主动寻路输入。
   - `DurationMoveEffectComp` 会在持续位移期间把执行层切到 `Displaced`。
   - 这与文档“位移期间不能主动寻路移动”的约束一致。

8. `Runtime obstacle reroute`
   - runtime obstacle 会推动 sector dirty、portal 重建和 tile cache 失效。
   - 这条链路已补入编辑器回归测试，用于约束“动态建筑生成后继续沿旧走廊硬顶”的回退。

9. `Debug visualization`
   - 当前 gizmo 已覆盖 sector 边界、portal window、flow tile integration heat、flow arrows、agent current cell、desired velocity、resolved velocity、bottleneck axis。
   - 并已补入 bottleneck owner / waiting queue 连线、movement mode 的运行态标记。
   - 这层观测面属于实现主干的一部分，不应在后续收尾时删弱。

### 16.6.2 与文档相比，当前实现仍是工程化近似的部分

以下部分已经可用，但还不能视为文档中的“最终形态”：

1. `Predictive Agent Avoidance`
   - 当前实现已经从早期的 `proximity + overlap + crossingWeight` 工程混合项，进一步收束到“`TTC(time-to-collision) + 最小分离距离` 主导”的预测式语义。
   - overlap 仍保留为强制近距/穿插修正项，alignment / crossing 只作次级 tie-break。
   - 当前实现已经补入 `same-lane follow / same-lane opposing / crossing / overtaking / static overlap` 的 encounter 分类。
   - 剩余差异是：还没有把不同单位类别、优先级、任务类型进一步拉成策略表。

3. `Portal LOS` 的目标语义
   - 文档只要求“LOS 内可直接朝精确目标 steering”。
   - 当前实现已经进一步细化为“portal LOS 目标必须服从 downstream integration 最优槽位语义”。
   - 这是必要的工程化补充，不是偏离文档。

4. `Bottleneck lane commitment`
   - 当前实现已经不再只依赖 portal，而是抽象成 corridor bottleneck 运行时状态。
   - `narrow portal`、同 sector 长窄走廊中段、拐角出口低可视抢出，现在都共享同一套 queue / direction / lane commitment 语义。
   - 当前运行时状态已经具备 `current owner`、等待队列顺序、短暂 convoy hold、`convoy token`、`priority override` 与显式 `owner state`。
   - 剩余差异是：这套状态机仍然偏轻量，还没有扩展到更细的 `occupant set / dual-side queue / token aging / task-specific override table`。

5. `Predictive avoidance` 的风险合成
   - 当前已经把 `TTC + 最小分离距离` 提升为主导变量，并修掉了“同帧读邻居已更新速度”的顺序依赖问题。
   - 现在 `same-lane / crossing / alignment` 只参与 tangent 与 brake 的次级分配，不再主导整体风险强度。
   - 剩余差异在于：还没有把 encounter 类型、优先级、不同单位类别的避让策略拆成更显式的状态机或策略表。

6. `Bottleneck lane context` 的激活条件
   - 当前 lane bias 仍通过“附近是否存在真实 traffic context”来决定是否激活。
   - 这是必要的稳定性约束，能防止单人直走被无意义侧偏污染。
   - 但它仍属于工程化 gate，而不是文档理想态里更明确的 corridor occupancy / encounter state 机。

7. `Queue slot` 的生成方式
   - 当前等待槽位仍按 `anchor + corridor axis + lane axis` 做局部几何生成。
   - 运行时调度已经补入“刚占用过瓶颈时的短暂出清保持 + 同向后继来流检测”，可减少窄门/拐角里一放一抢的频繁换向。
   - 当前运行时已经维护显式等待顺序，但等待槽位本身仍是局部几何生成。
   - 因此它还不是文档理想态里可扩展到“显式队列成员、成组流持续优先、高优先级插队”的完整调度队列模型。

8. `Dynamic obstacle reroute` 的覆盖面
   - 当前编辑器测试已经覆盖“动态障碍生成后会触发重路由”。
   - 但真实运行中的“防御阶段建筑变化 + 多单位正在跨 sector 行进”还需要继续观察实兵链路。

9. `Visualization` 的残缺项
   - 当前 gizmo 已覆盖主要导航与 steering 观测面，也能直接看到 `bottleneck owner / queue / movement mode`。
   - 但这些仍是 gizmo 级观测，不是独立 UI 面板；想做长期调参仍缺更系统的调试面板。

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

#### D. 交叉流与同向窄道侧偏的根因不是“参数偏了”，而是方向语义和时序语义错了

这一轮实现里，真正被测试和临时代码钉死的根因主要有四个：

1. `Predictive avoidance` 读取了同帧已更新的邻居速度，导致局部避让对遍历顺序敏感。
2. `ResolvePortalCorridorAxis` 曾把 vertical boundary portal 的 corridor 主轴写反，导致 lane bias 作用在错误轴上。
3. `narrow portal` 的 lane bias 一度对单人直走也生效，结果把本来正确的 portal / corner 直行速度平白压出侧偏。
4. `portal` 穿越后若直接切到 final goal tile，bottleneck 语义会过早退出；若简单把上一个 portal 继续套用，又会因为方向继承错误把已通过流误判成反向来流。
5. 瓶颈刚有单位出清时，如果立刻只按“当前占用数是否为 0”判断是否可切向，对向来流会过早抢口，破坏同向连续流。

对应修正如下：

1. 邻居速度读取改成“本帧只读上一帧稳定值”，消除交叉流的同帧顺序依赖。
2. `corridor axis` 语义修正为：
   - vertical boundary -> 走廊主轴是 `x`
   - horizontal boundary -> 走廊主轴是 `z`
3. lane bias 只在 narrow portal 且附近存在真实 traffic context 时启用，不再干扰单人直走。
4. bottleneck lane commitment 增加运行时记忆，并允许延续到“刚穿过 portal 的下游短走廊清空段”；但延续时必须显式继承原 traversal direction，不能再按“当前在哪一侧 sector”重新推导。
5. 瓶颈调度补入 `current owner`、等待队列顺序、短暂出清保持与同向后继来流检测，减少窄门/拐角里一放一抢的方向抖动。

这组修正的意义：

1. 十字交叉流会更早开始切向绕行，而不是到贴脸时才硬分开。
2. 同向单位通过窄道时，侧偏会保持稳定，不再前半段在一侧、后半段又翻到另一侧。
3. narrow portal 的调度层不会再污染普通直走 / portal 边界穿越 / L 型拐角入口这些单体主路径语义。
4. “portal 内排队”和“portal 后出清”终于属于同一条连续 corridor 语义，而不是两段互相打架的独立逻辑。
5. 连续同向流经过窄门时，不再那么容易被对向单位在中途抢断。

#### E. 位移测试失败的根因是 effect 时序，而不是 movement mode 设计错误

`DurationMoveEffectComp` 这轮还修过一个很隐蔽、但非常关键的时序 bug：

1. 旧逻辑先把本帧 effect 速度写入执行层，
2. 再根据“更新后的剩余 effect 列表是否为空”决定 `MovementMode`，
3. 于是会出现“本帧位移已经生效，但因为持续时间刚好在本帧扣到 0，就立刻切回 `Normal`”的错误情况。

修正后的规则：

1. `MovementMode.Displaced` 的判定要看“本帧是否有位移 effect 生效过”，而不只是看更新后的 effect 列表是否还非空。
2. 因此位移生效帧一定阻断主动寻路输入；位移完全结束后的下一帧，才恢复 `Normal`。

这条修正不能回退，因为它直接关系到文档第 11 节“位移期间不能主动寻路移动”的核心契约。

### 16.6.4 与位移兼容相关、已确定不能回退的规则

后续实现推力/拉力时，以下行为已经确定，不能再退回旧链路：

1. 位移期间不能继续主动寻路写入执行层。
2. 主动寻路与位移必须只通过 `MovementMode` / 执行层仲裁交汇。
3. 不能把位移实现成“外部速度存在，但寻路仍继续写 input velocity”。
4. 任何寻路修复都不能破坏 `CharacterMoveComp -> MoveExecutor` 的位移阻断语义。

这条约束非常重要，因为寻路系统后续还会继续调整，但位移仲裁边界已经属于核心契约。

### 16.6.5 当前整体完成度与剩余差距总表

这一节专门回答“现在到底还差哪些”，作为后续继续实现时的唯一对照面。

#### A. 已完成的主干

以下部分已经属于“主系统已落地”，后续主要是继续打磨，不再是从 0 到 1：

1. `Portal graph + sector/portal` 主路径框架已经落地。
2. `Flow tile cache`、`portal tile`、`final goal tile` 两类 flow 语义已经落地。
3. `portal downstream integration` 与 `portal LOS 最低可见 cost 槽位选点` 已落地。
4. `CharacterMoveComp -> FlowFieldCrowdMovementSystem -> MoveExecutor` 的主运动链已切通。
5. `MovementMode / Displaced` 位移阻断边界已经生效。
6. `runtime obstacle dirty -> sector dirty -> portal 重建 -> tile 失效` 链路已经落地。
7. `bottleneck scheduler` 的基础识别、让行、等待、lane commitment、queue bias 已落地。
8. `predictive avoidance` 已经不是旧 LJ/safeVelocity 逻辑，而是 flow 驱动下的预测式避让。

#### B. 已完成但仍属于“工程化近似”的部分

这些不是没做，而是已经可用，但还没达到文档理想态的最终版本：

1. `Predictive avoidance`
   - 已进入 `TTC + 最小分离距离` 主导。
   - `head-on / crossing / same-lane / overtaking / static overlap` 的 encounter 分类已落地。
   - 已补入轻量的 `leader vs non-leader` 避让权重差异，减少关键领头单位被非关键来流过度刹停。
   - 剩余差异是：还没有把不同单位类别、优先级、任务类型进一步拉成显式策略表或状态机。

2. `Bottleneck scheduler`
   - 已支持 `窄 portal`、`长窄走廊`、`拐角出口`。
   - 已补入“短暂出清保持 + 同向后继流检测”，减少一放一抢。
   - 已补入 `current owner`、等待队列顺序、`Leader/状态优先级` 排序、`convoy token`、`priority override` 与显式 `owner state machine`。
   - 剩余差异是：还没有更细的 `occupant set / dual-side structured queue / token aging`。

3. `Queue slot`
   - 已能稳定生成局部等待槽位。
   - 但仍是几何生成，不是可扩展到高优先级插队/多组流排队的结构化队列。

4. `Visualization`
   - gizmo 已经够排主链 bug。
   - 但还缺 `movement mode`、`queue occupant`、`current bottleneck owner` 这种 UI 级观测面。

#### C. 现在真正还没完成、后续必须继续补的部分

这是目前最应该继续推进的剩余项：

1. `旧 GroupMoveCoordinator 遗留的工程清理`
   - 当前真实主 steering 已不再走 `safeVelocity`，且运行时旧 `Coordinator.Resolve()` 活链已经移除。
   - `GroupMoveManager` 现在只保留向新系统同步 `agent/obstacle/group/state` 的壳层。
   - 剩余差异主要是：工程层仍沿用 `GroupMoveManager / GroupMoveCoordinator.AgentState` 这套命名与桥接接口，后续可再做命名与 ownership 收敛。

2. `Predictive avoidance` 的最终策略化
   - encounter 分类已落地。
   - 还缺不同单位体型、优先级、任务类型的更完整策略表。

3. `Bottleneck scheduler` 的最终化
   - 当前 owner、等待队列顺序、连续流 token、优先级插队、owner state 已落地。
   - 还缺更细的 `occupant set / 双侧结构化队列 / token aging / 更细粒度占用状态图`。

4. `实兵场景验证`
   - 编辑器测试已经覆盖很多关键逻辑。
   - 但真实战斗场景下的“防御阶段建筑变化 + 多兵跨 sector + 英雄/小兵混行”还需要继续看实兵表现。

5. `调试观测面补完`
   - 还需要更清晰地看到当前 agent 属于哪种 movement mode、是否在排队、当前 bottleneck 的 owner/方向/等待方。
   - 这不是效果逻辑本身，但会直接决定后续排查效率。

#### D. 目前优先级建议

按“最终效果最重要”的目标，后续建议优先级如下：

1. 继续补 `bottleneck scheduler`，把窄路/拐角/连续流调度做完整。
2. 继续补 `predictive avoidance` 的 encounter 分类与优先级语义。
3. 用实兵场景验证并收敛 `runtime obstacle reroute + 多流交互`。
4. 再做 `旧 GroupMoveCoordinator` 的进一步清理与调试观测面补完。

#### E. 当前阶段结论

如果按文档 Phase 来看，当前状态大致是：

1. `Phase 1 基础设施`：已完成。
2. `Phase 2 局部 crowd steering`：主体已完成，仍在继续打磨最终效果。
3. `Phase 3 瓶颈调度`：主体已完成，但还没到“最终形态”。
4. `Phase 4 位移仲裁`：核心契约已完成，后续还需要和未来推力/拉力正式实现继续联调。
5. `Phase 5 迁移与清理`：主体已完成。
   - 旧 `safeVelocity` 运行时链已切除。
   - 剩余主要是命名/桥接层收敛与更系统的调试观测面。

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

---

## 18. 基于文献原文的复审结论

这一节不是基于本 md 的自我对照，而是重新对照文献《Game AI Pro Chapter 23 - Crowd Pathfinding and Steering Using Flow Field Tiles》原文，以及当前 `FlowFieldCrowdMovementSystem.cs` 的完整实现后，重新整理出的差异、影响和后续优化建议。

### 18.1 已与文献主干一致的部分

以下部分已经和文献主干语义基本一致，或者属于同一类工程实现：

1. 世界按 `sector + portal window` 组织。
2. 高层路径先走 portal graph，再为路径经过的 sector 构建 tile。
3. 终点 tile 与 portal tile 分开处理。
4. portal tile 的 seed 已经采用“下游 tile 的 integrated cost”而不是全 0，这一点与文献 23.6.1 提到的“至少向前看一个 tile”一致。
5. goal LOS 语义已经存在，单位在 LOS 内会优先使用更直接的 steering，而不是死跟 flow 方向。
6. 支持动态环境 dirty -> rebuild -> reroute。
7. 支持不同 movement type 的独立导航世界与独立 tile cache key。

这些部分说明当前系统已经不是“挂着 flow field 名字的局部修补”，而是确实进入了文献那套分层寻路框架。

### 18.2 与文献存在明显差异、且会影响效果的部分

#### A. 当前并不是真正的“cost field + Eikonal integration”

文献原文：

1. `cost field` 是 8-bit，有真实地表代价。
2. `integration field` 用 Eikonal 方式扩散。
3. cost integration 主体只看 4 邻居成本传播，LOS 解决终点附近方向质量。

当前实现：

1. 运行时主成本几乎只有“可走/不可走”二值。
2. `BuildSectorIntegrationField` 本质是 8 邻域 Dijkstra，步长是 `1 / 1.414`，没有真实 cost field 权重输入。
3. 墙边、狭道、锯齿边的“更优中线代价”并没有提前编码进 field，而是主要依赖后面的 `boundary avoidance / lane bias / bottleneck` 去补。

影响：

1. 这是当前实现与文献最大的本体差异。
2. 它会直接影响“路径天然质量”，尤其是：
   - 贴墙倾向；
   - 走廊/拐角中的中线偏好不足；
   - 多条近似等价路线上的稳定选路；
   - jagged 边缘附近的方向噪声。
3. 目前系统之所以还能跑得不错，很大程度上是靠后层 steering/bottleneck 在补，而不是 field 本身已经足够好。

结论：

这是后续最值得做的大优化之一。如果要进一步逼近文献级效果，最优先的不是再堆局部避让规则，而是把真正的 `cost field` 建起来，至少加入：

1. 墙边/不可走边缘梯度成本；
2. 拐角内缩成本；
3. 狭窄通道中心偏好；
4. movement type 对地表/宽度的真实代价差异。

#### B. LOS 语义存在，但不是文献里的“LOS pass + wavefront blocked”

文献原文：

1. 对 final goal tile 先做 LOS pass。
2. 用 corner + Bresenham 线标出 `Wave Front Blocked`。
3. cost integration 从被 blocked 的边界继续扩散。
4. LOS 可跨 portal 无缝延续。

当前实现：

1. `HasLineOfSightToAnyGoal` 是对每个 cell 直接做格线 LOS 检测。
2. 没有显式 `Wave Front Blocked` 标志，也没有 LOS pass 的二阶段积分。
3. LOS 没有实现文献那种跨 sector/portal 的 blocked edge 继承。

影响：

1. 效果上，当前实现在很多场景下已经够用，特别是 portal tile 又叠加了“最低 downstream cost 可见槽位”选点，所以不会退化成最原始的菱形终点流。
2. 但从文献视角看，当前 LOS 仍然偏“逐格查询型”，不是“积分器的一部分”。
3. 这会带来两个代价：
   - CPU 上：tile build 时 LOS 检测更贵；
   - 质量上：跨 portal 的 LOS 过渡不如文献原版自然。

结论：

这是第二个值得做的大优化，尤其当地图更大、tile 更多时，文献式 LOS pass 会比“每格做 LOS”更稳、更便宜。

#### C. 文献里的 merging A* 没有实现

文献原文：

1. 多 source 对同一 goal 的 path request 会走 `merging A*`。
2. 后来的 source 倾向于 merge 到已有 portal node 结果上。
3. 这样能更强地共享 flow 结果，也让群体更自然地汇流。

当前实现：

1. `BuildPathHandle` 是标准 portal graph A*。
2. 每个 agent 自己建 handle。
3. tile cache 虽然能共享部分 tile，但 path handle 构建阶段没有显式 merge 机制。

影响：

1. 这不会让单位“走不动”，但会影响群体路线收敛质量和缓存复用率。
2. 当大量单位同时去同一目标、但起点较分散时，文献方案更容易形成一致的大流向，而当前实现更像“各自独立找一条差不多的高层路，再在 tile 层复用”。
3. 这类差异在大规模编队移动时会更明显。

结论：

这是第三个值得做的大优化，偏向“群体整体观感”和“高层缓存复用效率”。

#### D. 动态环境支持了 dirty rebuild，但没有文献里的 time-sliced rebuild queue

文献原文：

1. 动态变化通过 dirty 标记。
2. 重建通过优先级队列分帧 time-slice 执行。
3. 这样能稳定 CPU 占用。

当前实现：

1. dirty 链已经有了。
2. 但 rebuild 基本是在 `TryGetSteeringVelocity` 链路上同步触发。
3. 没有“每帧多少毫秒预算”的异步 rebuild queue。

影响：

1. 对效果本身影响不大。
2. 对性能和卡顿风险影响很大，特别是在：
   - 防御阶段建筑连续变化；
   - 多兵同时跨 sector；
   - 多 movement type 世界同时 dirty。

结论：

如果后续实兵场景里出现“偶发掉帧/尖峰”，这会是最优先的性能大优化项。

#### E. 文献里的 cost stamp / source cost data 目前只实现了“硬阻挡”，没实现“真实代价”

文献原文：

1. 支持 cost stamp。
2. 支持地形、墙边梯度、设计师手工 path cost。
3. 不同 movement type 读取不同 cost 数据。

当前实现：

1. 有 circle/box runtime hard obstacle。
2. 会 dirty sector、重建 portal/tile。
3. 但没有“把一组成本 stamp 进 cost field”。
4. 也没有墙边 blur gradient 这样的预处理成本层。

影响：

1. 这再次说明当前 field 更接近“可走域 flow”，而不是“带真实偏好的 cost flow”。
2. 对 RTS 实战效果的直接影响是：
   - 单位更容易把“局部 steering 修正”当成主路径修正；
   - 地图作者无法显式塑造行军偏好；
   - 走廊、开阔地、障碍边缘之间的路径风格不够可控。

结论：

如果目标是“最终效果最优”，那么建立真正的 source cost / cost stamp 体系，价值非常高。

#### F. 文献里的 island field 缺失

文献原文：

1. 支持 island ID。
2. 可快速判断 source/goal 是否连通。
3. 可直接给 UI 或命令层反馈“不可达”。

当前实现：

1. 没有 island field。
2. 依靠 portal graph / integration 失败后判不可达。

影响：

1. 对已发起移动后的实际 steering 效果影响不大。
2. 对前置可达性判断、UI 反馈、频繁无效请求过滤会有影响。

结论：

这不是当前效果瓶颈，但它是一个很好的“低风险、高实用性”补强项。

### 18.3 与文献不同，但未必是缺点的部分

#### A. 文献没有专门解决窄口双向调度，当前实现补了 bottleneck scheduler

这部分是当前实现相对文献的增强，不是偏离缺陷。

文献更偏“field + steering + physics”，并没有直接给 RTS 常见的：

1. 双向窄门让行；
2. 拐角口抢出；
3. 连续同向流出清；
4. priority override / convoy token。

而当前实现在这部分已经明显超出文献原文，对项目是正收益。

#### B. 当前实现用 NavMesh 作为底层空间采样源

文献的世界是作者自有的 editor-built grid cost data。

当前实现是：

1. 从 NavMesh 栅格化 walkable；
2. 用 NavMesh anchor / raycast 修正 cell 间连通；
3. 再在其上做 sector/portal/tile。

只要最终能稳定表达可走域，这不是原则性问题。真正的问题不在“底层是不是 NavMesh”，而在“上层是否只有 walkable，而没有 rich cost field”。

### 18.4 对当前寻路效果影响最大的缺失排序

如果只按“会不会明显限制最终寻路效果”排序，而不按实现难度排序，我会这样排：

1. **缺少真正的 cost field / source cost / wall gradient**
   - 这是目前最大的效果上限瓶颈。

2. **没有文献式 LOS pass / WaveFrontBlocked**
   - 会影响 tile 内方向质量和 CPU。

3. **没有 merging A***
   - 会影响大群体共同目标时的路线收敛和 tile 共享质量。

4. **没有 time-sliced rebuild queue**
   - 主要影响性能尖峰与大场景稳定性。

5. **没有 island field**
   - 主要影响请求前置判定和交互反馈。

### 18.5 还能想到的几个大优化

这些不一定都来自文献原文，但都是在当前架构上有明显收益的大优化：

#### A. 真正建立三层成本

把当前二值 walkable 升级成三层 cost：

1. `Base terrain cost`
   - 地表/坡度/地形类型。
2. `Static clearance gradient`
   - 靠墙、拐角、锯齿边的递增成本。
3. `Runtime soft cost stamp`
   - 大型动态障碍、拥堵区、危险区、临时禁行区。

这会让大量当前靠 steering 修补的问题，直接前移到 field 层解决。

#### B. 把 LOS pass 正式并入积分器

1. 做 `HasLineOfSight` + `WaveFrontBlocked` flag。
2. 用 Bresenham 标 blocked edge。
3. 让 final goal tile 与 portal tile 的积分阶段更接近文献原版。

这会同时提升质量和 tile build 性能。

#### C. 做 request-level group planning

不是只在 agent 级共享 tile，而是：

1. 同一次 move command 的 source 集合先做 request 级聚合；
2. 跑 merge-aware portal graph；
3. 共享 path handle / subpath segment。

这会让“大部队一起移动”的整体观感再上一个台阶。

#### D. 建立异步 rebuild budget

把：

1. world rebuild
2. portal rebuild
3. tile rebuild
4. affected path rebuild

放进统一预算队列，而不是在 steering 查询时同步吃掉。

#### E. 为未来位移系统预留“soft flow suppression”

当前已经有 `MovementMode.Displaced` 的硬阻断边界。

后续如果推力/拉力更复杂，可以进一步做：

1. displacement 期间对 flow steering 的软抑制；
2. displacement 恢复后的短期 rejoin smoothing；
3. 被拉拽穿越 sector 后的 path/tile 快速重接入。

这属于当前契约上的增强，不是补 bug。

### 18.6 当前结论

如果只问“当前实现和文献相比，还差的最关键本体是什么”，答案不是 bottleneck，也不是 predictive avoidance 细节，而是：

1. **field 仍然太像 binary walkable field，而不是真正的 rich cost field；**
2. **integrator 仍然不是文献那种 LOS pass + Eikonal 主导的版本；**
3. **request 级 merge 和 rebuild budget 还没建立。**

如果只问“这些差异会不会影响最终寻路效果”，答案是：**会，而且其中第 1 条影响最大。**

如果接下来还要继续往“效果最优”推进，最值得投入的大改方向是：

1. 先做 `cost field / source cost / wall gradient / runtime soft stamp`；
2. 再做文献式 `LOS pass`；
3. 再做 `merging A* + rebuild budget queue`。
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

---

## 21. 基于论文与现实现的第一手审查

这一节不依赖前文总结，只对照论文原文和当前实现本身，记录“哪里已经到位，哪里还缺，缺口会不会影响效果”。

### 21.1 当前实现已经接近论文的部分

1. `sector / portal` 分层已经存在。
2. `flow tile` 缓存已经存在，并按 `world / sector / goal / agentType / dirtyVersion` 复用。
3. 动态脏区与重建链路已经存在。
4. 严格无 fallback 已经落实。
5. 窄口让行、队列槽位、边界恢复、群体避让这些 steering 层补强已经有了。

### 21.2 和论文最核心的差别

1. 当前还是“二值可走区 + 8 邻接积分”，不是论文那种 richer `cost field`。
2. 论文里的 `source cost / wall gradient / blur` 还没有真正进入主链路。
3. `LOS` 现在是逐格判断，不是论文的 `WaveFrontBlocked + Bresenham` 传播。
4. 多源请求没有 `merging A*`。
5. 没有 `island field`。
6. 没有时间切片式 rebuild queue。
7. 论文是 editor-built cost data；当前是 NavMesh 栅格化 + 运行时硬障碍修正，这是工程替代，不是同一层能力。

### 21.3 对效果影响最大的缺口

1. **缺少真正的 cost field / wall gradient**
   - 这是目前最影响“贴墙、拐角、窄路观感”的根因级缺口。
   - 没有它，流场只能在“可走/不可走”之间选，天然更容易贴边。

2. **缺少论文式 LOS pass**
   - 会让 goal 附近和 portal tile 内的方向质量不够平滑。
   - 也会让 tile 构建更吃力。

3. **缺少 merging A***
   - 大量单位朝多个相邻目标移动时，路径共享和收敛性不够好。

4. **缺少异步 rebuild budget**
   - 动态建筑、脏区扩散、路径重算时更容易出现 CPU 尖峰。

5. **缺少 island field**
   - 主要影响“先判可达再下发命令”的效率和交互反馈，不是主效果瓶颈，但很实用。

### 21.4 我认为还值得做的更大优化

1. **真三层成本**
   - `base terrain cost`
   - `static clearance gradient`
   - `runtime soft stamp`
   这会把很多“靠 steering 修”的问题前移到 field 层解决。

2. **清晰的宽度感知 cost**
   - 大单位和小单位分不同 cost field，不只是不同世界状态。

3. **拥堵软成本**
   - 把局部密度、回流、狭口排队压力写成短生命周期 stamp。

4. **request 级合并**
   - 同一批同目标单位先合并请求，再下发 tile/path。

5. **增量重建队列**
   - world / portal / tile / affected path 统一预算化，而不是查询时同步硬算。

### 21.5 结论

当前系统已经不再是旧式“角点追踪 + 局部补偿”了，但它和论文原版相比，真正还差的不是表层 steering，而是**成本场质量**。

如果只选一个下一步大改方向，我会优先做：

1. `cost field`
2. `wall gradient / clearance`
3. `runtime soft stamp`
4. `paper-style LOS`

---

## 22. 性能审查：新寻路为什么比旧系统更慢

这一节直接基于当前代码链路审查，不依赖前文方案推导。

### 22.1 结论先说

当前性能问题不是“流场天生比 A* 慢”，而是**本该预处理、缓存、批量做的工作，被放进了首次寻路和每帧移动链路里**。

所以现在跑出来的是：

1. 首次取目标时，同步做了整张导航世界的重建；
2. 单位移动时，每帧仍在做大量 NavMesh 查询、LOS 查询、moving-goal 重建。

这和论文里“路径廉价、流场可复用”的使用方式不是一回事。

### 22.2 首次获取目标卡死几秒的根因

首次 `TryGetSteeringVelocity` 会触发 `TryEnsureWorldBuilt`。

而当前 `TryEnsureWorldBuilt` 不是轻量取缓存，而是可能同步做完整世界构建：

1. `NavMesh.CalculateTriangulation`
2. 全图 rasterize：每格 `NavMesh.SamplePosition`
3. `BuildCellNavAnchors`：每个可走格再做一次 `NavMesh.SamplePosition`
4. `RebuildPortalsAndTransitions`
5. `BuildSectorPortalTransitions`

其中第 5 步又会对每个 sector 的每个 portal source 跑一次 `BuildSectorIntegrationField`。

而当前 `BuildSectorIntegrationField` 在 NavMesh world 下，邻接扩展不是纯数组操作，而会通过 `CanTraverseNeighborCells` 落到 `NavMesh.Raycast`。

这就意味着：**首帧世界构建阶段，已经混入了大量同步 NavMesh 查询。**

所以“首次取目标卡住几秒”并不是偶然，而是当前架构下的必然结果。

### 22.3 移动中帧率低的根因

移动中的主要问题不是一个点，而是三个重热点叠加：

#### A. 每帧 steering 都会进入完整导航链路

`CharacterMoveComp.Move()` 里，只要目标存在，就会每帧调用一次 `FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(...)`。

所以所有本该极轻量的 per-frame 部分，只要稍微混入重查询，就会被单位数线性放大。

#### B. portal 目标解析每帧都在做 LOS

`ResolveDesiredDirection()` 会先调用 `ResolveTileTargetPosition()`。

对 portal tile 来说，`ResolveTileTargetPosition()` 会遍历 portal 每个候选格，并对每个候选格调用 `HasGridLineOfSight(...)`。

而 `HasGridLineOfSight(...)` 内部每一步又会调用 `CanTraverseNeighborCells(...)`；
在 NavMesh world 下，后者会继续触发 `NavMesh.Raycast(...)`。

也就是说，**当前单位穿 sector / portal 时，每帧都在重复做 portal 级 LOS + NavMesh.Raycast 链路**。

这和论文的做法差异非常大。论文是把 LOS 预写进 tile，而不是让 agent 每帧重新问。

#### C. 边界避让每帧都在查 NavMesh edge

`ResolveBoundaryAvoidance()` 在 NavMesh anchor world 下会走 `TryResolveNavEdgeData()`。

而 `TryResolveNavEdgeData()` 每次都要：

1. `NavMesh.SamplePosition`
2. `NavMesh.FindClosestEdge`

这也是按“每个移动单位、每一帧”发生的。

#### D. moving goal 会频繁重建 path handle / tile

`EnsurePathHandle()` 只按：

1. `WorldVersion`
2. `goalX / goalY`

来判断是否复用。

而当前 `SoldierAIBrain.TickCombat()` 是持续 `MoveTo(enemy.Position)`。

只要敌人的目标格变化，当前单位的：

1. `PathHandle`
2. 终点 tile key
3. portal tile 的 downstream goal hint

都会变化，从而导致重建。

这意味着：**追逐移动目标时，系统很可能退化成“每帧在重算 path/tile”**，这会严重吃掉移动阶段帧率。

### 22.4 还有一个隐含放大器：NavMesh 邻接校验被放进了格图基础操作

当前很多逻辑理论上应该是纯格图 O(1) 邻接判断，但在运行时 world 里：

`CanTraverseNeighborCells(...) -> NavMesh.Raycast(...)`

这个调用被复用到了：

1. portal 构建
2. portal 过渡图构建
3. sector integration
4. grid LOS
5. 对角可达判断

这会把大量本来便宜的 grid 算法，整体抬成“带 NavMesh 查询的 grid 算法”。

这是当前系统性能风格和论文思路偏差最大的地方之一。

### 22.5 根因优先级排序

如果按对现象影响排序，我会这样排：

0. **移动 / Brain / GroupMove 调试日志被强制常开**
   - 原先 `GameDebugSettings` 里 `ForceMovementDiagnostics = true`。
   - 这会让移动链路上的大量日志与诊断分支常驻运行。

1. **首次世界构建完全同步，并且过重**
2. **per-frame portal LOS 解析混入 NavMesh.Raycast**
3. **per-frame boundary avoidance 混入 NavMesh.FindClosestEdge**
4. **moving target 导致 path/tile 高频失效重建**
5. **NavMesh 邻接校验被过度下沉到基础格图操作**
6. **动态避让参与集合过宽**
   - 原先几乎所有有 MoveComp 的单位都会进入动态避让桶。
   - 这会把真正的移动热点扩成“围绕大量非活动单位做邻居查询和预测”。

### 22.6 后续优化方向

真正有效的优化，不是继续调一点参数，而是把这些“运行时重活”重新分层：

1. 世界构建前置化或异步化，不能卡在首次取目标
2. NavMesh 邻接关系预烘焙成 grid 邻接掩码，运行时不再频繁 `NavMesh.Raycast`
3. portal LOS 改成 tile build 时预计算，不在 agent 每帧重问
4. boundary avoidance 尽量使用 grid / cached edge data，不要每帧 `FindClosestEdge`
5. moving target 采用 goal hysteresis / sector-cell quantization / handle 复用策略，避免每格变化都重建

### 22.7 本轮已落地的优化

1. **邻接可达预计算**
   - 不再让 `CanTraverseNeighborCells` 在运行时频繁落到 `NavMesh.Raycast`。
   - 改为 world build / obstacle dirty rebuild 时一次性预写邻接可达掩码。

2. **portal target 缓存**
   - portal tile 的目标解析不再让 agent 每帧重复做 LOS 候选遍历。
   - 改为 tile build 时按 cell 预缓存 portal target。

3. **边界避让回到 grid**
   - 运行时边界避让不再每帧走 `NavMesh.FindClosestEdge`。
   - 统一使用当前导航格图边界信息。

4. **moving goal 的 sector-path 复用**
   - `PathHandle` 不再因为目标在同一 sector 内换了格子就整条 sector path 重建。
   - 先复用 sector 级路径，精确目标仍由末端 tile 处理。

5. **关闭强制移动调试**
   - 不再默认强开 `Move / Brain / GroupMove` 诊断日志。

6. **动态避让参与条件收紧**
   - 动态避让桶优先只纳入当前确实有导航意图、或最近几帧确实在移动的单位。

7. **导航世界主动预热**
   - 关卡初始化完成后预热。
   - 不在建造后同步预热，因为这会把首次寻路卡顿转移成建造卡顿。

8. **分段性能统计**
   - `TryGetSteeringVelocity` 按帧汇总 `world / cells / path / tile / desired / bottleneck / steering / neighbor / boundary`。
   - 只有超过阈值或发生 build 时输出一条 `[FlowPerf]` 汇总日志。
   - 后续优化必须先看该日志里的真实占比，再决定改哪里。

这些改动的目的不是“微调参数”，而是把不该留在热路径里的同步 NavMesh 查询和重复 LOS 查询拿掉。

---

## 23. 与 Game AI Pro Ch.23 原方案的差异总账（必须补齐）

本节不以当前实现或前文方案为准，而以 `GameAIPro_Chapter23_Crowd_Pathfinding_and_Steerin.pdf` 为第一手资料重新对照。

### 23.1 最关键差异：移动目标请求模型不对

文献里移动目标的核心不是“每个单位追目标当前位置”，而是：

1. 目标移动时重建目标 flow field；
2. 目标跨 sector 时在后台重建 portal nodes / path；
3. 大部分 flow field tiles 已经在 cache 中，只有少量 tile 需要新建；
4. 新 path 准备好后，agent 从旧 path 无缝切到新 path。

当前实现长期保留了一个过渡结构：`PathHandle / StableGoalX / StableGoalY` 在每个 agent 上。

这会导致十几个单位追同一个英雄时，每个单位都可能独立判断目标格变化、独立触发 path/tile 重建。日志里 `pathBuilds ~= calls` 就是该差异造成的直接证据。

已改成：

1. 以目标实体 id 为 key 维护 `MovingTargetAnchor`；
2. 同一目标的所有追击者共享同一个 active goal cell / stable anchor；
3. 移动目标不再使用刷新间隔和 cell hysteresis。目标所在 walkable cell 一变化就刷新 anchor，避免敌兵追旧位置；
4. 高层路径不再按 agent 独立跑 portal A*，而是按 `(worldVersion, agentTypeId, goalSectorId, goalCellIndex, goalSectorDirtyVersion)` 建 `SharedGoalField`；
5. `SharedGoalField` 是从目标 sector portal 反向扩散到全 portal graph 的目标势能，每个追击者只需要从自己的 start sector 选择最优入口 portal；
6. 旧的 active/pending prepare queue 已删除。性能靠目标级共享势能和 tile/path cache，而不是靠延迟切换目标位置。

### 23.2 缓存 key 过细，破坏流场复用

文献的 flow tile cache 目标是让不同 path request 尽可能共享 hallway / portal tile。

当前实现把 `FinalGoalIndex` 写进所有 `FlowTileCacheKey`，导致目标在同 sector 内移动时，远离目标的 portal tile 也会因为精确目标格不同而失效。

必须改成：

1. 最终目标所在 sector 的 final-goal tile 绑定精确 goal cell；
2. 直接通向最终 sector 的 portal tile 可以按 downstream final sector 或粗目标锚点区分；
3. 更远端 portal tile 不应被精确 final goal cell 打散；
4. 只有确实依赖末端精确 LOS / integration 的 tile 才绑定精确目标。

### 23.3 同步构建不符合文献的 integrator 思路

文献把 integrator 描述为可以在一个或多个 tick 内构建 flow field tile。

当前实现大量 world / path / tile 构建仍在 `TryGetSteeringVelocity` 热路径中同步完成。首次取目标卡顿和 moving goal 帧率低都来自这里。

必须改成：

1. world build / rebuild 在关卡初始化、建造完成后主动排队；
2. flow tile build 使用预算队列；
3. moving target 目标变化只提交 request，不在每个单位移动帧里同步把所有 tile 建完；
4. 请求未 ready 时继续使用 active old field，严格模式只在没有任何可用旧场且目标不可达时明确报错。

### 23.4 Cost field 质量仍不等同文献

文献使用预构建 cost field，墙、坡度、设计师标注成本都会进入 field。

当前实现主要是 NavMesh raster walkable mask + grid 边界/clearance 的工程替代。它能工作，但仍缺：

1. 独立的 base terrain cost；
2. 静态 wall / clearance gradient；
3. runtime soft stamp；
4. 按单位尺寸的多 movement type cost field。

这会影响贴墙、拐角、宽窄路选择质量，也会把部分本应在 field 层解决的问题推给 steering。

### 23.5 Island field

文献建议 island field 用于快速判断 path request 是否有效。

此前实现依赖 portal path / tile 构建失败来暴露不可达。严格模式下这会报错，但性能和交互都不如先做 island 判定。

现已补上基础 island field：

1. 每个 `NavigationWorld` 构建后用 `NeighborTraversalMask` 做 BFS 编号；
2. 动态障碍 dirty 后重建 island field；
3. `TryResolveGoalCell` 后先判断 start/goal island；
4. 不可达时严格报 `island mismatch`，避免无意义 portal search / tile build。

剩余差异：

1. island 仍跟当前 walkable mask 绑定；
2. 后续多 movement type / cost field 落地后，需要按 movement type 拆出不同 island field。

### 23.6 LOS pass 不完整

文献的 final goal tile 具有 LOS pass，使目标附近直接朝精确目标 steering，减少菱形方向和拐角抖动。

当前实现已有 line-of-sight 概念和 portal target cache，但不是完整论文式 LOS pass。

后续要补：

1. final goal tile 构建阶段写入 LOS flags；
2. LOS 内 agent 忽略 flow direction，直接朝 exact goal；
3. portal tile 的 LOS / target 也尽量在 build 阶段完成，而不是 per-frame 查询。

### 23.7 当前执行顺序

短期必须先修会直接导致卡顿的差异：

1. per-target shared moving goal anchor；
2. 非末端 portal tile cache key 不再绑定精确 final goal cell；
3. moving target shared reverse portal goal field；
4. flow tile build budget queue。

然后补效果质量差异：

1. cost field / clearance gradient；
2. 完整 LOS pass；
3. 多 movement type cost field。

其中 island field 基础版已在本轮完成，后续只需要在多 movement type / cost field 落地时扩展为按类型拆分。

### 23.8 本轮根因修复结论

这轮日志已经把问题钉死到两个点：

1. **移动目标跨 sector 时，同帧同步把所有追击者各自跑高层 path/tile**
   - 这会让 `goalSectorMismatch` 在英雄跨 sector 的那一帧集中爆发。
   - 早期曾尝试 `active/pending` 双缓冲，但它牺牲目标响应性，导致敌兵追旧位置，不符合最终效果目标。
   - 现已改成目标级共享反向 portal 势能：
     - 同一目标 cell 只构建一次目标端 integration 和 `SharedGoalField`；
     - 多个追击者共享该高层目标势能；
     - 目标 cell 变化立即刷新 anchor，避免人为延迟；
     - 旧 `MovingGoalPrepareRequestsPerFrame / GoalRepathInterval / GoalRepathCellThreshold` 配置和 pending 队列已删除。

2. **关卡初始化的预热时序早于最终 rebake**
   - 先 prewarm 再 rebake，会把第一次目标获取仍然打回冷构建。
   - 现已改成：若初始化期间已有 rebake 请求，先等 rebake 结束，再做预热。

3. **移动目标测试必须用 sector 变化场景验证**
   - 纯开放场里“是否斜向移动”并不能验证共享目标场是否生效。
   - 现在测试改为：同一目标先在上方 sector，再切到下方 sector，检查目标 cell 变化后立即转向，并依赖 shared goal field / portal access cache 控制重建成本。

4. **仍保留的文献差异**
   - cost field / clearance gradient 还没做成完整论文版；
   - final goal LOS pass 还不是论文完整实现。
   - island field 已有基础版，但还没和多 movement type / cost field 完整合并。

### 23.9 本轮新增：首次锁定卡顿的真实根因

最新日志再次确认：首次锁定英雄时的明显卡顿不是 `pathBuilds=12` 这一层造成的，真正尖峰是：

```text
[FlowPerf] frame=1065 ... worldBuilds=1 ... total=342.921ms world=319.986ms ...
```

对应前置日志是：

```text
[LevelEntity] Runtime init prewarm before complete ...
[FlowWorld] Prewarm end elapsed=515.160ms built=3 ...
[LevelEntity] DoRebakeNavMesh begin ... duringInit=False completed=True
[LevelEntity] DoRebakeNavMesh end ... completed=True
```

也就是说：

1. 初始化阶段确实预热过 3 个 agent type；
2. 但初始化完成后又发生了一次 `completed=True` 的 rebake；
3. 这次 rebake 调用了 `InvalidateNavigation`，把已预热的 world 全部标脏；
4. 因为当时没有后续预热，第一次小兵锁定英雄时才在移动热路径同步重建 world；
5. 于是 `TryGetSteeringVelocity` 首帧承担了整张 NavMesh raster / neighbor traversal / island / sector / portal 构建。

后续实测又证明：`QueueNavigationWorldPrewarm` 虽然把首次锁定的冷构建提前了，但它本质仍是在 `Update` 中同步完整 `TryEnsureWorldBuilt`，所以建造后约 1 秒会出现新的 130ms-300ms 卡顿。这不是时序问题，而是算法层把“运行时建筑变化”错误表达成了“整张 NavMesh/flow world 失效”。

最终修复策略：

1. 初始化阶段仍然允许 `RequestRebakeNavMesh -> DoRebakeNavMesh -> PrewarmNavigationWorlds`，因为此时是在生成基础可走区；
2. 运行时建筑 `OnShow/OnHide` 不再调用 `RequestRebakeNavMesh`；
3. 建筑改为在 `BuildingEntity` 生命周期内注册/注销 `FlowFieldCrowdMovementSystem` 的运行时硬障碍；
4. `BuildingCollisionBlockingBuff` 启用/恢复碰撞时只刷新该建筑的 flow obstacle，不触发 NavMesh rebake；
5. `GroupMoveManager.Update` 不再处理 queued world prewarm，`QueueNavigationWorldPrewarm/ProcessQueuedNavigationWorldPrewarm` 已从运行链路移除；
6. 真正需要改变基础 NavMesh 的情况仍走 `LevelEntity.RequestRebakeNavMesh`，但这不再是普通建造的路径。

运行时障碍更新的算法要求：

1. 只把障碍 bounds 命中的 sector 及邻接 sector 标脏；
2. 只重置这些 sector 的 `WalkableMask`，再叠加当前所有 runtime hard obstacles；
3. 只重建这些 sector 的 neighbor traversal mask；
4. portal 使用稳定 id：同一 sector 边界、方向、run 起止 cell 和宽度相同，则重建后仍获得同一个 portal id；
5. `world.Version` 不因 runtime obstacle dirty 改变，避免全局 path/tile cache 作废；
6. 只清理碰到 dirty sector 或引用已消失 portal 的 path / sector path cache / shared goal field；
7. 运行时障碍更新不再全图 `BuildIslandField`。可达性由 portal graph + sector integration 严格判定；不可达仍返回严格失败，不做 fallback。

验证口径：

1. 建造建筑后不应再出现 `[LevelEntity] RequestRebakeNavMesh` / `[FlowWorld] MarkWorldDirty reason=[LevelEntity] DoRebakeNavMesh completed`；
2. 应出现 `[BuildingFlowObstacle] register ...` 和下一次寻路帧的 `[FlowRuntimeDirtyApply] ... dirtySectors=N`；
3. 建筑后不应再出现 `[FlowWorld] Queued prewarm built ... elapsed=xxxms`；
4. 若普通建造后仍看到 `worldBuilds=1 world=xxxms`，说明还有运行时建筑/障碍绕过了 `BuildingEntity.RegisterFlowFieldObstacles`，需要按实体来源追注册链。

portal/transition 增量化：

1. runtime obstacle dirty 不再调用全局 `RebuildPortalsAndTransitions`；
2. 只移除触碰 dirty sector 的 portal；
3. 只重建 dirty sector 四周的 sector 边界 portal；
4. 只重算受影响 sector 的 `PortalTransitions`；
5. portal id 使用 `PortalSignature -> 小整数 id` 的 world 内稳定映射，避免原有 `(sectorId << 16) | portalId` 编码溢出；
6. 消失 portal 会让引用它的 path/cache/shared goal field 失效，未受影响 portal/path/tile 不因建造被全局打掉。

仍需继续观察：

1. 建造后如果仍有短尖峰，重点看 `[FlowRuntimeDirtyApply] dirtySectors=N`、portal 数量和 `tileBuilds/pathBuilds` 是否集中；
2. 不应回到 queued prewarm 或运行时 full NavMesh rebake，那会重新制造 130ms-300ms 级别卡顿。

### 23.10 本轮新增：island mismatch 严格报错的诊断口径

最新报错：

```text
[Unit_CanMaker] Flow strict fail: island mismatch start=(269,51) island=1 goal=(224,22) island=7 ...
```

同一段日志里英雄持续出现 `MoveExecutor` 的 NavMesh 边界命中/投影回原地。这个现象强烈指向“追击目标所在 walkable island 与追击者所在 island 不连通”，但在没有目标实体、目标世界坐标、NavMesh sample 位置和两侧最近 island 信息前，不应直接修改索敌或做跨岛吸附。

因此当前只增强严格报错诊断：

1. 报错包含 self / target 的 key、阵营、位置、MovementMode；
2. 报错包含 rawGoal、goal cell center、start cell center；
3. 报错包含 start/goal 的 NavMesh sample 结果和 sample 后 island；
4. 报错包含 goal island 距 start 最近格、start island 距 goal 最近格；
5. 仍然严格抛错，不做 fallback，不隐藏不可达事实。

后续只有在日志证明是目标选择层把不可达目标交给寻路时，才应该去目标选择/攻击意图层过滤；如果证明是 NavMesh raster 或 island 构建错误，才改流场底图。

### 23.11 本轮新增：移动目标追旧位置的根因和修复

现象：英雄走开后，敌兵仍向英雄旧位置移动，约 1 秒后才转向。

最终判断：

1. 旧方案把性能控制压在 `GoalRepathInterval / GoalRepathCellThreshold / active-pending promote` 上；
2. 这会让移动目标天然产生滞后，英雄离开后敌兵继续追旧 cell；
3. 这不是文献方案的核心收益。文献的收益来自可缓存、可共享、可分摊的 flow/path 数据，而不是人为延迟目标更新。

最终修复：

1. 删除移动目标 pending prepare / promote 队列；
2. 删除 `MovingGoalPrepareRequestsPerFrame / GoalRepathInterval / GoalRepathCellThreshold` 配置；
3. 目标 walkable cell 变化立即刷新 `MovingTargetAnchor.ActiveGoal*`；
4. `BuildPathHandle` 使用 `SharedGoalField` 共享同一目标 cell 的反向 portal 势能；
5. `SharedGoalField` cache 命中时不再为每个追击者重复构建 goal sector integration；
6. 起点 sector 到 portal 的代价不再按单位从当前位置重复建 integration，而是按 `(worldVersion, sectorId, portalId, sectorDirtyVersion)` 缓存 `SectorPortalAccessCache`；
7. 每个单位仍用自己的当前 cell 查询 portal access cost 来选择入口，所以不会牺牲同 sector 内不同位置/不同连通块的严格性；
8. path handle 必须匹配 goal cell，不能拿旧目标 cell 的 handle 服务新目标。

验证口径：

1. 追同一移动英雄时，`stableGoal(cell=...)` 可以随目标 cell 变化增长，但 `sectorPath(searches=...)` 不应再接近移动单位数量；
2. 目标 cell 没变时应主要走 `stableGoal(reuse=...)` 和 shared goal cache hit；
3. 同 sector 多单位启动时，不应再因为每个单位重复构建 start-sector integration 产生线性尖峰；
4. 不应再出现 `FlowMovingGoalPendingDelay`，因为该链路已经删除；
5. 英雄移动到新 walkable cell 后，敌兵不应等待 0.25s/3 cell/队列完成才转向。

当前测试现状：

1. `dotnet build .\AAAGame.Tests.Editor.csproj`：0 error / 0 warning；
2. Unity `FlowFieldCrowdMovementSystemTests`：30 个测试中 29 个通过；
3. 仍失败的 `瓶颈等待排序会优先已标记Leader的单位` 属于 bottleneck leader priority 行为，和本节移动目标共享场无直接关系。排序比较器表面方向正确，后续需要单独加瓶颈状态日志钉死，不应在移动目标性能修复中猜改。

### 23.12 本轮新增：不可达实体目标应解析为最近可达接近点

最新报错：

```text
[Unit_CanMaker] Flow strict fail: island mismatch ...
target=Buil_ResearchCenter_Lv1 targetPos=(81.90, 0.00, 16.10)
goalNav=miss
```

根因不是 shared goal field，也不是流场应该 fallback；根因是 `SoldierAIBrain.TickCombat` 判断距离时使用 `DistanceToTargetSurface(enemy)`，但移动时直接 `MoveTo(enemy.Position)`。对建筑来说，`enemy.Position` 常是建筑中心，可能在 NavMesh 外、建筑硬障碍内，甚至被解析到另一个 walkable island。

修复原则：

1. 攻击目标实体不能直接把中心点交给寻路；
2. Combat 移动先用与攻击距离一致的目标表面点；
3. 再把期望接近点解析为“当前单位所在 island 上离该点最近的 walkable cell”；
4. 解析成功后才 `MoveTo(reachableApproachPoint)`；
5. 若当前 island 附近完全没有可达接近点，继续明确抛错并带上下文，不做静默 fallback。

当前实现：

1. `EntityContextExtensions.TryGetTargetClosestPoint` 从 private 改为 public extension，复用 `DistanceToTargetSurface` 的 Collider/HurtBox 语义；
2. `FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal` 根据 self 当前 agent type/world/island 解析最近可达目标点；
3. `SoldierAIBrain.TickCombat` 使用 `ResolveCombatApproachPoint -> TryResolveNearestReachableGoal -> MoveTo(reachableApproachPoint)`；
4. 新增测试 `不可达目标会解析到同岛最近可达点`，保证目标中心在不可达岛/不可走点时，会选择 self 所在 island 上最近的可达点。

### 23.13 本轮新增：Combat 站位点不能成为攻击触发门槛

现象：多个实习生已经围住英雄，外圈不攻击符合预期，但最内圈贴近英雄的实习生也不攻击。

根因：

1. `DirectAtkComp.TryStartAttack` 的真实攻击门槛是 `DistanceToTargetSurface(target) <= weaponRange`；
2. Combat 走位为了避免多个单位扎到同一点，会为同一目标解析多个 `desiredApproachPoint`；
3. 如果 Brain 额外要求“到达 approach point 后才能 Attack”，就会把“战斗站位优化”错误升级为“攻击许可条件”；
4. 内圈单位可能已经在武器射程内，但因为还没到被旋转出来的站位点，`Brain.Attack` 一直为 false，攻击组件自然不会进入 WindUp。

修复原则：

1. 攻击触发只和真实武器射程一致：`distToEnemy <= effectiveRange`；
2. `ResolveCombatApproachPoint` 只负责射程外单位的移动目标选择、绕障和占位分散；
3. 一旦单位已经进入武器射程，立即 `Attack = true` 并 `StopMove()`；
4. 外圈单位因为 `DistanceToTargetSurface > weaponRange` 仍不会攻击，会继续通过流场/站位点接近。

当前实现：

1. `SoldierAIBrain.TickCombat` 使用 `bool shouldAttackNow = distToEnemy <= effectiveRange`；
2. 已删除“必须接近 desiredApproachPoint / arriveDistance 才攻击”的隐含门槛；
3. 新增回归测试 `Combat状态_内圈单位在武器射程内会直接攻击`；
4. 测试前置显式构造战斗阶段数据，因为 `IsAttackTargetable` 在非 Invade/Defend 阶段会返回 false，这是运行时真实规则，不应在测试里绕过。

### 23.14 本轮新增：重新对齐论文 portal/window/integration 主链

本轮重新审视 PDF 后确认：之前把连续 portal window 拆成单格 portal 只是压住“长 portal 选错端点”的中间态，不是论文方案。论文的核心结构是“sector 边界上的一整段 portal window”，flow request 对这段 window 提供多个 goal locations，并且每个 goal location 可携带不同 integrated cost。

因此当前修正为：

1. portal 构建恢复为整段连续 window，不再按单格切碎；
2. portal center 改为整段 window 两侧所有格子的平均中心，避免首尾格子平均导致中心偏移；
3. portal tile 的实际运动方向只来自 integration/flow，不再通过可见 portal 单点或 opposite center 参与执行；
4. portal target cache 不再预构建，相关目标点仅作为诊断即时计算；
5. 最终目标 tile 中 flow 为零且不在目标格时也严格报错，不再 `TileTargetFallback` 直冲目标；
6. 删除旧的 direct fallback 速度函数，避免后续被误接回链路；
7. LOS 标记只对最终目标 tile 生效，portal tile 不再带 LOS 执行语义；
8. LOS 检查遵守 cost field，遇到 cost > 1 的墙边/几何代价区即不再标记直视，符合论文“LOS wave front hits cost greater than one”的规则；
9. sector integration 传播改为四邻居 wavefront，并用 Eikonal 风格公式从正交邻居求 cost-to-go；
10. flow pass 已按论文 23.6.4 改为比较八邻居最低 integration，不再使用四向连续梯度替代；
11. NavMesh anchor 邻接从“任一方向 ray clear 即可”收紧为“双向 ray 都 clear”，否则 neighbor traversal mask 会把建筑边/角误认为连通，导致 flow 直指建筑边而实际移动器贴边慢滑。

这轮根因总结：

1. 单格 portal 虽然提升了某些 corner case，但破坏了论文的 window 多 seed 共享，会让 portal 数量、path graph、cache key 数量膨胀；
2. 八邻居 Dijkstra integration 和论文“四邻居 Eikonal integration + 八邻居 flow pass”不同，容易在斜向障碍、拐角、portal window 附近写入非论文语义的代价；
3. portal LOS/target 点参与执行会把“去 portal window”错误变成“直奔 portal 某个点”，特别容易在建筑另一侧目标时冲向建筑边；
4. 单向 NavMesh 邻接被对称化会直接污染 island、portal、integration 三层底图，是 island 过多和冲建筑边的高风险根因。

仍需继续审查：

1. cost gradient 仍是运行时从 blocked/untraversable 邻居生成，尚不是编辑器源数据预烘焙；
2. 整段 portal 恢复后，若日志显示 portal graph 仍选错 window，应优先检查 window 多 seed 的 downstream integrated cost 是否被正确带回，而不是再次拆 portal；
3. 单向邻接收紧可能让原先由 NavMesh 采样误差造成的窄连接暴露为 island 分裂；若出现，需要修 raster/sample 半径或 NavMesh anchor，而不是放宽为单向连通。

### 23.14.1 本轮新增：最终目标 LOS wavefront pass

论文的 LOS pass 不是每个格子各自向目标做一次射线判断，而是从最终目标 seed 发出 LOS wavefront：在 cost==1 的清晰区域标记 `Has Line Of Sight`，遇到 cost>1 或墙时检测角点并打出 `Wave Front Blocked` 线，防止 LOS 波前绕到遮挡背后。

当前实现：

1. `FlowTileCacheEntry` 增加 `WaveFrontBlocked`；
2. 仅 `TileGoalKind.FinalGoal` 构建 LOS pass，portal tile 不带 LOS 执行语义；
3. LOS pass 从 final goal seed 扩散，只穿过 walkable、可邻接、cost==1 的格子；
4. 遇到不可走、不可邻接或 cost>1 的边界时，沿“目标 -> 遮挡边界”的外延方向标记 `WaveFrontBlocked`；
5. 后续 LOS wavefront 不穿过 `WaveFrontBlocked`；
6. tile 诊断输出当前格和邻居格的 `los/waveBlocked`，用于继续排查拐角可视区是否正确。

跨 sector carry-over：

1. portal tile 也构建最终目标 LOS pass，但 seed 不来自 portal 自身目标，而来自下游 tile 对侧 portal cell 的 `HasLineOfSight`；
2. 下游对侧 portal cell 若带 `WaveFrontBlocked`，当前侧对应 portal cell 也标记 blocked；
3. 当前 tile 只有被 carry-over 证明能直视最终目标时，才允许 `LineOfSight` steering；
4. `LineOfSight` steering 永远朝最终目标 `goalPosition`，不朝 portal target；
5. portal target 只保留为诊断用途，正常执行路径不再每帧扫描 portal cells 的 LOS，避免移动中持续 CPU 浪费。

portal handoff 修正：

1. 最新严格报错显示 portal tile 内当前格 `cost` 已经低于所有本 sector 可走邻居，`flow=(0,0)`；
2. 这不是避障/移动层问题，而是 portal window goal cell 被当成了本 sector 的终点；
3. 论文中的 portal window 是 sector 间连接，agent 到达 portal goal location 后应继续跨 portal 到下游 sector；
4. 因此在 tile 构建阶段，对 `TileGoalKind.Portal` 的 goal cells 写入“当前侧 cell -> 对侧 portal cell”的 handoff flow direction；
5. 这不是 runtime fallback：方向在 flow tile 生成时固化，严格模式仍保留 zero-flow 报错；
6. zero-flow 诊断增加 `isGoalCell/opposite/oppositeTraversable/portal`，后续能直接区分是否是 portal handoff 缺失或底图邻接错误。

当前仍是论文机制的保守近似：

1. blocked line 的方向用格心方向近似 LOS corner Bresenham 外边线；
2. 如果后续在最终目标附近出现“可视区穿角”，再继续补精确 Bresenham corner 起点和外边线；
3. 如果跨 sector LOS 不连续，优先看 carry-over 的 portal cell 对应关系和 `waveBlocked` 诊断，而不是恢复 portal 直线目标。

### 23.15 本轮新增：移动目标共享 anchor 的性能修正

此前 moving target anchor key 包含 `targetId + rawGoalCellIndex`。这会导致英雄每移动一个 cell，就为同一个目标生成一个全新 anchor，追同一英雄的多个单位无法真正共享目标状态和后续 flow/path cache，移动目标场景仍会出现初次锁定和移动过程尖峰。

当前修正为：

1. moving target anchor key 只按 `targetId` 共享；
2. `rawGoalX/rawGoalY` 仍记录在 anchor 内部，用于判断目标 cell 是否变化；
3. 目标 cell 变化仍立即刷新 active goal，不引入延迟转向；
4. 日志保留 `rawCellIndex`，便于继续观察目标贴建筑时 raw cell 与 active reachable cell 的差异。

预期效果：

1. 多个敌兵追同一英雄时，会复用同一个 moving target anchor；
2. 目标移动到新 cell 时仍然立刻刷新，不再靠 0.25s/1s 之类时序延迟省性能；
3. 后续性能瓶颈若仍存在，应继续看 `[FlowPerf]` 的 `tileBuilds/pathBuilds/sectorPath(searches,cacheHits)`，而不是重新加入目标刷新延迟。

### 23.16 本轮新增：portal tile integration seed 只向前看一层，减少移动目标精确格污染

重新对照论文 23.6.1 与 23.8 后确认：portal window 的 initial wave front 可以携带上一层已经 integrated 的 costs，用来让跨 sector flow 更连续；但这不等于每一个上游 portal tile 都必须同步递归构建到精确 final goal cell。

此前实现的问题：

1. `BuildIntegrationSeedsForTile()` 为 portal tile 构建 seed 时，会递归调用 `TryGetOrBuildTileForPathSegment()` 生成下游 tile；
2. 当目标是移动英雄时，一个单位首次锁定目标可能在同一帧沿整条 sector path 同步构建多层 tile；
3. 中间 hallway / portal tile 因为间接依赖精确 final goal cell，更容易被目标移动打散缓存；
4. 这和论文“多数 flow field 已在 cache 中，移动目标只需要少量重建”的目标不一致。

当前修正：

1. 若下游 sector 已经是 final goal sector，仍构建精确 final goal tile，并把其 portal 槽位 integration cost 带回；
2. 若下游 sector 不是 final goal sector，则当前 portal tile 的 integration seed 不再递归构建更深层 tile；
3. 非末端 portal tile 的 integration seed 只读取下游 sector 到“下一 portal window”的 `SectorPortalAccessCache` integration cost，作为当前 portal window 多 seed 成本；
4. 这等价于论文“至少向前看一个 flow field ahead”的保守版本，同时避免把远端精确 final goal cell 传播进所有中间 tile；
5. 若对应下游 portal 槽位不可达，仍抛 `StrictPortalWindowUnreachableException`，并输出 `FormatSectorPortalAccessCosts` 诊断，不做直线 fallback。

注意：23.24 之后，LOS flag / `WaveFrontBlocked` carry-over 会递归构建下游 tile 链来保证论文式跨 sector 可见性连续；这里的“一层”只指 integration seed cost，不再指 LOS carry-over。

同一 goal sector 内移动目标的 path handle 修正：

1. `EnsurePathHandle()` 不再因为 `GoalX/GoalY` 在同一个 goal sector 内变化就重建整条 `PathHandle`；
2. 只要 world、portal 序列、start sector、goal sector 仍有效，就更新 `handle.GoalX/GoalY`，让 final goal tile 按新精确格重建；
3. 如果新目标格导致末端 portal window 实际不可达，tile 构建阶段仍会严格失败并触发一次强制 repath；
4. 这更接近论文 23.8：moving goal 没跨 sector 时重建 goal flow field，跨 sector 时才在后台重建 portal nodes。

已补回归：

1. `多Sector路径站在PortalGoalCell时不会把Portal当终点停住`：防止 portal handoff 被删回零 flow；
2. 编译验证通过，剩余 warning 均来自既有 editor 字段未赋值。

### 23.17 本轮新增：按论文 23.10 补入墙边 cost blur

重新对照论文 23.10 后确认，论文里的 `cost field` 不是简单的 walkable / blocked 二值，也不是每个格子临时数邻居。它由编辑器源数据生成，并对墙边做 blur pass，让 hallway、锯齿边和拐角附近的 flow 更平滑。

此前实现的问题：

1. `BuildCostField()` 只检查 1、2 格内 blocked / untraversable 邻居数量；
2. 成本变化是局部计数惩罚，不是真正的距离梯度；
3. 对长墙、斜向走廊、拐角边缘的代价不够连续，容易出现贴墙、横竖方向偏强、路径看起来绕远的问题；
4. 建图时每格重复扫邻域，也不利于后续大地图性能。

当前修正：

1. `BuildCostField()` 改为从 blocked cell 和 untraversable boundary cell 出发做距离扩散；
2. `WallCostBlurRadiusCells = 2.25`，墙边近格成本最高，外圈逐渐回到 1；
3. `CostField=255` 仍只表示硬不可走，不把软成本当阻挡；
4. LOS pass 仍遵守论文 23.6.2 的规则：`cost > 1` 会截断 LOS wavefront，因此该 blur 半径必须保持小而明确，避免宽路整片失去目标直视；
5. 增加 `墙边成本梯度会让积分流场避开贴边路径` 回归，验证这是 flow/integration 层面的方向改善，不是执行层碰撞补偿。

仍与论文有差异：

1. 当前 blur 仍是运行时从 nav raster / obstacle 反推，不是编辑器烘焙的源成本；
2. 已有 runtime box cost stamp 进入 `CostField`，但还没有坡度、道路偏好、编辑器可视化源成本、不同 movement type 的独立源成本；
3. 后续如果要继续逼近论文，应把 `BaseCostField` 做成可视化、可编辑、可 stamp 的数据层，而不是把所有成本都写死在运行时。

### 23.18 本轮新增：runtime dirty 的 cost field 局部重建

论文 23.8/23.15 的动态环境核心是 dirty 标记和受控重建，不应因为一个建筑/障碍改变就全图重算所有成本与缓存。

此前实现的问题：

1. runtime obstacle dirty 已经能按 sector 重置 walkable、重建 portal、清理受影响 cache；
2. 但 `ApplyRuntimeObstacleDirty()` 仍调用全图 `BuildCostField(world)`；
3. 补入墙边 blur 后，如果继续全图重建，会偏离论文 sector dirty 的增量链路；
4. 这属于算法层 dirty 粒度不一致，不是靠延迟刷新或隐藏 fallback 能解决的问题。

当前修正：

1. runtime dirty 后先得到原始 `dirtySectors`；
2. 按 `WallCostBlurRadiusCells` 扩展出 `costDirtySectors`，因为墙边软成本会影响相邻 sector；
3. 新增 `RebuildCostFieldForSectors()`，只在扩展后的 sector bounds 内重建成本；
4. source bounds 额外带 padding，确保边界外的墙/阻挡仍能影响写入区域；
5. `InvalidateCachesForDirtySectors()` 使用 `costDirtySectors`，避免相邻 sector 的 tile 继续吃旧成本；
6. portal 拓扑仍只按原始 `dirtySectors` 重建，不把软成本扩展误当作 portal 几何变化；
7. `WallDistanceScratch` 复用全图距离数组，避免每次 dirty 分配大块 `float[]` 造成 GC 尖峰。

仍与论文有差异：

1. dirty rebuild 还不是完整 priority queue / fixed millisecond time slice；
2. 当前仍是在下一次寻路调用中同步应用 dirty，只是重建范围缩小；
3. 后续若要继续补齐论文 23.8/23.15，应实现完整 dirty job queue，并明确 pending dirty 时的寻路一致性契约，不能半实现后偷偷使用 stale world。

### 23.19 本轮复审校正：不要再把同 sector 移动目标当成未解决问题

本轮重新读代码后确认，`EnsurePathHandle()` 当前已经具备论文 23.8 的关键语义：

1. world/version、start sector、goal sector、portal 序列仍有效时；
2. 如果目标只是在同一个 goal sector 内换了 `GoalX/GoalY`；
3. 系统只更新 `handle.GoalX/GoalY`；
4. 不重建整条 `PathHandle`；
5. 后续由 final goal tile / shared goal field 根据新目标格更新精确末端成本。

因此后续排查性能时，不要在没有新日志证据的情况下把“同 sector 移动目标重建整条路径”重新列为当前 bug。正确观察点是：

1. `[FlowPerf] pathBuilds` 是否真的随同 sector 目标换格增加；
2. `pathReasons.goalCellMismatch` 只是 goal cell update 计数，不等于整条 path rebuild；
3. `tileBuilds` / `SectorPathSearches` 才是判断移动目标是否仍在过度同步构建的重点；
4. 若目标跨 sector，重建 portal path 是论文允许的，但应依赖 shared goal field / portal access cache 控制成本。

### 23.20 本轮新增：LOS pass 只在真实 LOS corner 生成 WaveFrontBlocked

重新对照论文 23.6.2 后确认，LOS pass 命中 `cost > 1` 或墙体后，并不是无条件画遮挡线。论文步骤是：

1. 先判断该位置是否是 `LOS corner`；
2. 只有一侧 cost > 1、另一侧 cost == 1 时，才是 corner；
3. 对 corner 从格子外边线沿远离目标方向用 Bresenham 生成 `Wave Front Blocked`；
4. 这些 blocked cells 会阻止 LOS wavefront 绕到遮挡背后，并作为后续 cost integration wavefront 的边界。

此前实现的问题：

1. `MarkWaveFrontBlockedFromLosCorner()` 名字像 corner，但命中任何 blocked/cost>1 邻居都会画线；
2. 这属于论文 LOS pass 的简化替代；
3. 风险是过度截断 LOS，或者在长直墙边生成不该有的 blocked line，影响目标附近和拐角处的 flow 质量。

当前修正：

1. 新增 `IsLineOfSightCorner()`；
2. 命中阻挡后先检查阻挡边垂直方向两侧的 clear/blocked 状态；
3. 只有两侧状态不同，才认为是真实 LOS corner 并生成 `WaveFrontBlocked`；
4. 对 visible side 也做一次同样判断，覆盖 wavefront 从清晰侧贴边触墙的情况；
5. 不满足 corner 条件时不画 blocked line，只停止该方向 LOS wavefront；
6. 新增 editor-only `TryGetEditorTestCachedTileCellLineOfSightState()` 用于回归观察 tile 内 `HasLineOfSight/WaveFrontBlocked`，不进入运行链路。

已补回归：

1. `LosPass只在真实转角生成WaveFrontBlocked`；
2. L 型墙体背后的格子不应被 LOS wavefront 绕过去标成直视；
3. 真转角外延线上应存在 `WaveFrontBlocked`。

仍与论文有差异：

1. blocked line 起点仍用 blocked cell 格心近似，不是严格的“grid square outer edge position”；
2. 如果后续发现极窄拐角 LOS 边缘仍有误差，应继续补 outer-edge Bresenham 起点，而不是回退到 per-cell raycast 或无条件画线。

### 23.21 本轮新增：LOS integration 顺序与 shared goal merge 复审

重新对照论文 23.5、23.6.2、23.6.3 后，本轮修正两个容易误判的点。

#### A. LOS 不再只是后补标记，而是进入 integrator

论文链路是：

1. reset integration field；
2. initial goal wavefront 先做 LOS pass；
3. LOS pass 标记 `HasLineOfSight`；
4. LOS corner 生成 `WaveFrontBlocked` 第二 active wavefront；
5. cost integration 从该 blocked wavefront 继续扩散；
6. flow field pass 对 LOS 格写 LOS flag，对非 LOS 格按 integration 选方向。

此前实现虽然已经有 `HasLineOfSight/WaveFrontBlocked`，但顺序仍偏简化：

1. 先对整块 tile 做完整 cost integration；
2. 再补 LOS 标记；
3. `WaveFrontBlocked` 更多是 steering/诊断语义，没有真正成为 cost integration 的起始波前。

当前修正：

1. `FlowTileBuildJob.Prepare` 创建 tile 后先 `InitializeIntegrationField()`；
2. `FlowTileBuildJob.LineOfSight` 先写入 LOS wavefront 的 integration cost；
3. `MarkWaveFrontBlockedLine()` 会给 blocked line 写入 integration cost；
4. `FlowTileBuildJob.Integration` 在已有 tile integration 基础上继续 Eikonal；
5. Eikonal open set 只从原始 seed 与 `WaveFrontBlocked` cells 启动，不再把所有 LOS cells 都当作 cost integration 源；
6. LOS 内仍由 agent 直接朝 exact goal steering，非 LOS 区域则读取真正由 blocked wavefront 接续出来的 integration。

这比 23.20 更进一步：23.20 修的是“什么时候生成 blocked line”，23.21 修的是“blocked line 是否真正接入 cost integration”。

仍与论文有差异：

1. blocked line 起点已经从格心推进到远离目标一侧的边界近似，但还不是连续几何 outer-edge Bresenham；
2. 当前 blocked line 上的 integration cost 用格距近似，不是严格从外边线连续长度积分；
3. 如果后续出现极窄角 LOS 边缘误差，继续补精确 outer-edge 采样，而不是退回每格 raycast。

#### B. `SharedGoalField` 已经承担 merging A* 的主要 merge 语义

文献 23.5 说多个 source 到同一 goal 时，后续 portal walker 会倾向 merge 到已走过的 portal node，从而更容易共享 flow field。

重新读当前代码后确认：

1. `GetOrBuildSharedGoalField()` 从 goal sector 反向构建 `SharedGoalField`；
2. `SharedGoalField.NodeCosts` 记录所有 portal node 到目标的代价；
3. `NextNodeTowardGoal` 形成一棵指向目标的共享 next-node 树；
4. 不同 source 在 `BuildPathHandle()` 中只选择自己的 best start portal，然后沿同一棵 shared field 取后续 portal；
5. 因此多个 source 会天然汇入共享 portal node 结果，这已经覆盖了论文 merging A* 的主要目标：共享高层 portal 结果与中间 flow tile。

后续不要再无证据地把“没有 merging A*”列为当前缺口。更准确的状态是：

1. 当前实现不是逐 source 前向 merging A*；
2. 它采用 goal 反向共享场实现等价的 merge/next-node 复用；
3. 真正还可优化的是 request-level 多 source 批处理、flow request 引用计数、以及更强的同命令 path handle 复用，而不是从零再实现一套前向 A*。

### 23.22 本轮新增：flow field pass 改回论文八邻居最低成本

重新对照论文 23.6.4 后确认：flow field pass 的职责不是再做一层连续梯度拟合，而是在 integration field 已经完成后，对每个非 LOS 格比较 `NW/N/NE/E/SE/S/SW/W` 八个邻居，选择 cost-to-go 最低的方向。

此前实现的偏差：

1. `ResolveFlowDirectionFromIntegration()` 先用四向 cost 差做连续梯度；
2. 只有梯度不可用时才 fallback 到八邻居最低成本；
3. 这会让 flow direction 可能偏离 integration field 的实际最便宜邻居，尤其在墙边 blur、拐角、斜向绕障时，把方向拉成“看起来平滑但不是最短 cost-to-go”的向量。

当前修正：

1. 删除四向梯度优先逻辑；
2. `ResolveFlowDirectionFromIntegration()` 直接调用 `ResolveLowestNeighborFlowDirection()`；
3. flow pass 严格以八邻居 integration 最低值输出方向；
4. 保留 LOS steering：有 `HasLineOfSight` 的格子仍直接朝 exact goal，不读 flow direction。

已补回归：

1. `墙边成本梯度会让积分流场避开贴边路径` 追加读取 cached tile flow direction；
2. 断言墙边格 flow 具有朝目标推进和离墙的斜向分量；
3. 新增 editor-only `TryGetEditorTestCachedTileCellFlowDirection()`，仅用于回归观察缓存 tile，不进入运行链路。

### 23.23 本轮新增：runtime cost stamp 进入 CostField

重新对照论文 23.9/23.10 后确认，cost stamp / source cost data 是寻路底层数据，不是 steering 补偿。它应该进入 `CostField`，并通过 dirty sector 触发 integration/tile/path cache 失效。

当前修正：

1. 新增 `CostStamp` 数据结构；
2. 新增 `FlowFieldCrowdMovementSystem.RegisterBoxCostStamp()` / `UnregisterCostStamp()`；
3. `RegisterBoxCostStamp()` 支持默认全 movement type，也支持指定 `agentTypeId`；
4. `GroupMoveManager` 增加对应转发接口；
5. `BuildCostField()` 和 `RebuildCostFieldForSectors()` 在墙边 blur 后应用 cost stamp；
6. stamp cost 合法范围为 `1..254`，`255` 仍只由硬不可走表达；
7. 注册/注销 stamp 复用 runtime dirty sector 链路，重建受影响 sector 的 `CostField` 并清理相关 path/tile/shared goal cache。

已补回归：

1. `CostStamp会进入CostField并影响积分流场`；
2. 验证 stamp cell 的 `CostField` 值真实写入；
3. 验证同 sector 直路中高成本带会让 velocity 产生绕行分量，而不是继续直冲；
4. `CostStamp只影响匹配MovementType的CostField`；
5. 验证指定 agent type 的 stamp 不会污染当前不匹配 agent type 的 `CostField`。

仍与论文有差异：

1. 目前是运行时 box stamp，尚未接入编辑器可视化/可绘制 source cost 数据；
2. 还没有坡度、道路偏好、编辑器可视化 source cost；runtime stamp 已按 movement type 过滤，但还不是完整编辑器源数据层；
3. hard wall stamp 仍由 obstacle / walkable mask 链路承担，尚未做成统一的 255 cost stamp 数据层；
4. 若后续要做道路偏好，应优先扩展 source cost 数据来源，而不是在 steering 层加道路吸附。

### 23.24 本轮新增：跨 sector 续接 WaveFrontBlocked 线

重新对照论文 23.6.2 后确认，`WaveFrontBlocked` 跨 portal carry-over 不是只把 portal window 对应格标记为 blocked。论文要求：当邻接 tile 的 portal window location 带 `Wave Front Blocked` 时，当前 tile 要把这个位置当作一个 LOS corner，并继续沿远离 goal 的方向用 Bresenham 把 blocked line 画完。

此前实现的偏差：

1. `SeedFinalGoalLineOfSightPass()` 看到 downstream portal cell 带 `WaveFrontBlocked` 时，只把 current portal cell 标记 blocked；
2. 当前 tile 内部没有继续生成 blocked line；
3. portal tile 生成新的 LOS blocked line 时，方向还会使用 portal goal cell 近似，而不是 final goal cell；
4. 风险是跨 sector 后 LOS 遮挡边界被截断，LOS wavefront 可能绕到目标不可见区域，或者 cost integration frontier 不完整。

当前修正：

1. `PropagateLineOfSightWaveFront()` 显式接收 final `goalX/goalY`；
2. `MarkWaveFrontBlockedFromLosCorner()` 使用 final goal 决定 blocked line 的远离方向；
3. 新增 `MarkCarriedWaveFrontBlockedLine()`；
4. downstream portal cell 带 `WaveFrontBlocked` 时，当前侧 portal cell 不再只标单格，而是从当前 portal cell 沿远离 final goal 的方向续接 blocked line 到本 sector 边界；
5. carried line 写入 `WaveFrontBlocked` 和 integration cost，继续作为后续 cost integration active frontier；
6. 非末端 portal tile 的 LOS carry-over 不再提前 return，会递归构建下游 tile，直到 final goal tile，把 `HasLineOfSight/WaveFrontBlocked` 沿 sector path 往回带。

已补回归：

1. `PortalTile会续接下游WaveFrontBlocked遮挡线`；
2. 构造 final tile 中墙角 blocked line 到达 portal cell 的场景；
3. 验证上游 portal tile 的 sector 内部格也被标记 `WaveFrontBlocked`，不是只复制 portal 单格；
4. `非末端PortalTile会递归构建下游Tile以携带跨SectorLOS`；
5. 验证长路径首段会构建下游 tile 链来携带跨 sector LOS flags。

仍与论文有差异：

1. 起点仍是离散 portal cell，不是连续 portal window 外边线几何点；
2. blocked line cost 仍按格距近似；
3. 递归构建当前是同步完成，尚未接入论文 23.8/23.15 的 rebuild priority queue / time-slice。

### 23.25 本轮新增：movement type 半径驱动墙边 Cost 缓冲

重新对照论文 23.11 后确认，不同 movement type 不只是不同 portal graph / walkable mask。为了支持大单位，论文还会做 wall cushioning，把墙体向外推，关闭过窄通道，并让大单位不会视觉上贴/穿墙。

当前已有基础：

1. `NavigationWorld` 已按 agent type 独立构建；
2. `FlowTileCacheKey` / `SharedGoalFieldKey` 等 key 已包含 agent type；
3. walkable raster / NavMesh anchor 采样已使用 `ResolveAgentTypeRadius()`；
4. 23.23 已让 runtime cost stamp 可按 agent type 过滤。

此前剩余偏差：

1. 墙边 soft cost blur 半径仍是固定 `WallCostBlurRadiusCells`；
2. 大单位虽然可能通过 NavMesh/raster 关闭窄缝，但墙边 cost 梯度没有随半径扩大；
3. 结果是大单位和小单位可能在同样的墙边软成本下选贴边路线，不符合论文 large-unit cushioning 的目标。

当前修正：

1. 新增 `ResolveWallCostBlurRadiusCells(world)`；
2. `WallCostBlurRadiusCells` 作为基础半径；
3. 若 agent type 半径大于默认 `0.5`，按 `(agentRadius - 0.5) / cellSize` 扩大墙边成本传播半径；
4. full build 和 runtime dirty 局部 rebuild 都使用该动态半径；
5. dirty sector padding 同步使用动态半径，避免大单位 cost field 局部重建漏掉扩展区域；
6. editor-only 增加 `SetEditorTestAgentTypeRadius()` / `ClearEditorTestAgentTypeRadii()`，只用于回归构造不同 movement type 半径。

已补回归：

1. `大单位MovementType会扩大墙边Cost缓冲`；
2. 默认半径下某远离边界格 cost 为 1；
3. 同图大半径 agent type 下同一格进入墙边缓冲，cost 高于默认半径。

仍与论文有差异：

1. 当前是运行时根据 NavMesh agent radius 推导 cushioning，不是编辑器预烘焙的多 movement type source cost；
2. 没有单独的设计师可视化/手工编辑层；
3. hard wall outward expansion 仍主要由 NavMesh/raster walkable mask 负责，soft CostField 只负责路线偏好与离墙缓冲。

### 23.26 本轮新增：runtime dirty rebuild priority/time-slice 队列

重新对照论文 23.8/23.15 后确认，动态墙体、坡度、CostStamp 等环境变化不应在导航查询链路里同步重建完整 dirty 链路。论文描述的做法是：标记 dirty 后进入 priority queue，每个 rebuild item 分配固定毫秒片，逐步控制何时、如何重建。

此前实现的偏差：

1. `TryEnsureWorldBuilt()` 发现 `DirtyRuntimeObstacleSectors` 后直接同步调用 `ApplyRuntimeObstacleDirty()`；
2. 单次 dirty 会在同一帧完成 reset walkable、应用 obstacle、neighbor traversal、symmetrize、CostField、island、portal graph、cache invalidation；
3. `SymmetrizeNeighborTraversalMask()` 是全图扫描，即使只脏了少量 sector；
4. 这与论文的 time-sliced rebuild queue 不一致，也会让建造建筑/CostStamp 之类运行时变化产生帧尖刺。

当前修正：

1. 新增 `RuntimeDirtyRebuildJob`；
2. `MarkRuntimeObstacleDirty()` 只收集 dirty sectors，并使旧 pending job 失效重排；
3. `GroupMoveManager.Update()` 每帧调用 `FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue()`；
4. 新增 `GroupMoveConfig.RuntimeRebuildBudgetMilliseconds`，默认 `1.5ms`；
5. job 在 cloned working world 上分阶段重建，完成前不改当前导航 world；
6. job 阶段顺序与旧同步链路一致：reset walkable -> apply obstacles -> neighbor mask -> symmetrize -> cost field -> island field -> portal graph -> commit；
7. commit 时原子替换 `WalkableMask/CostField/NeighborTraversalMask/IslandIds/Sectors/Portals`，再清理受影响 cache、moving target anchors、bottleneck runtime、agent path handle/stable goal；
8. 若导航查询发生在 job 完成前，`TryEnsureWorldBuilt()` 会强制 drain 当前 job 后再返回 world，避免在严格链路中使用 dirty world；
9. `SymmetrizeNeighborTraversalMaskForSectors()` 将对称化限制在 dirty sector bounds 外扩 1 格，不再全图扫；
10. pending job 被新的 runtime dirty 打断时，会先把旧 job 的 dirty sectors 合回 `DirtyRuntimeObstacleSectors` 再重排，避免连续建造/连续 stamp 时丢掉前一次尚未提交的区域；
11. `ProcessRuntimeRebuildQueue()` 会收集 pending world states 并按优先级排序：单位当前 sector 命中 dirty 最高，其次是现有 path handle 触碰 dirty，再其次是 dirty sector 数量；
12. `CostField` 阶段改为按 cost dirty sector 分帧重建，每个 sector 仍使用扩展 source bounds，保证墙边 blur / CostStamp 不漏邻域；
13. `IslandField` 阶段改为 job 内可暂停 BFS，逐步扫描 walkable cells 和 flood fill 连通分支，完成后一次性写入 island count / main island；
14. `PortalGraph` 阶段拆成 remove old portals、按 dirty sector 重建边界、按 portal 写入、按 transition sector/from-portal 重建 transitions 四个子阶段；
15. portal transition 不再一口气重建整个 sector 的所有 portal 对，而是每次处理一个 from-portal；
16. from-portal integration 也不再调用同步 `BuildSectorIntegrationField()`，而是在 job 内维护私有 `MinHeap` 和 integration 数组，按 open-set 节点推进，避免跨帧污染全局 `OpenSet`；
17. portal tile 构建中，seed 解析与 LOS 递归会共享同一次 downstream tile，避免同一链路重复构建；
18. full world build 接入 `ProcessWorldBuildQueue()`；不再保留旧 `TryResolveTerrainSource()` / `BuildWorld()` 同步入口；
19. full world build 使用 job-local `WorkingWorld`，完成前不暴露半成品 world；
20. full world build 阶段已拆为：initialize、NavMesh raster sampling、create world shell、apply runtime obstacles、initialize sectors、cell nav anchors、neighbor mask、symmetrize neighbor mask、CostField、IslandField、PortalGraph、commit；
21. full world build 的 NavMesh raster、cell anchors、neighbor mask、symmetrize、CostField、IslandField、portal transition integration 都按 cell / sector / open-set 节点预算推进；
22. world build 与 runtime dirty 都使用 obstacle / CostStamp 输入快照，避免分帧构建中途读取全局可变表导致半旧半新的结果；
23. world dirty 期间如果 runtime obstacle / CostStamp 又变化，会废弃 pending build job，等待下一次按最新输入重建，不把旧输入 job 继续提交；
24. `TryEnsureWorldBuilt()` 若在 world build job 完成前被查询链路调用，会强制 drain 完成后再返回，保持严格无半成品 world 语义；
25. 新增 `FlowTileBuildQueue`，`GroupMoveManager.Update()` 会在 world build / runtime dirty 后推进 flow tile 预构建；
26. flow tile miss 不再直接从查询链路同步构建完整 tile，而是先进入统一队列；若查询恰好需要该 tile，会严格 drain 对应 job 后再返回；
27. portal tile 的下游 tile 链按 goal sector -> current sector 的顺序排队，保证跨 sector LOS carry-over 仍能拿到已完成下游 tile；
28. tile job 绑定 path handle id 和 handle snapshot；移动目标更新 goal cell 时会刷新 handle id，旧 pending tile job 会被判定 stale，不再浪费预算构建旧目标 tile；
29. `GroupMoveManager.Update()` 先推进 world build queue，再推进 runtime dirty rebuild queue，最后推进 flow tile build queue；
30. 单个 flow tile job 已拆成 `Prepare -> LineOfSight -> Integration -> FlowDirections -> PortalHandoff -> Commit`；
31. `LineOfSight` 按 open queue 分帧推进，`Integration` 按私有 `MinHeap` open set 分帧推进，`FlowDirections` 按 cell cursor 分帧推进，`PortalHandoff` 按 portal window cursor 分帧推进；
32. 未完成的 tile job 会放回队首继续推进，避免 goal 侧 tile 尚未完成时先处理 upstream tile，破坏 portal 链依赖顺序；
33. 旧的 `BuildTileForSector()` 同步整 tile 构建入口已删除，防止后续误接回热路径；
34. portal tile 的 `Prepare` 阶段不再通过 `ResolveDownstreamTileForTileBuild -> TryGetOrBuildTileForPathSegment` 嵌套同步 drain 下游 tile；
35. 若 downstream tile 尚未完成，当前 upstream job 标记 `WaitingForDependency` 后放回队尾，同时把下游 tile 链插到队首继续推进；
36. `_perf.TileBuilds` 只在 tile 真正开始构建时计数，等待依赖不会被误算成已构建 tile。

已补回归：

1. `RuntimeDirtyQueue会原子提交运行时障碍重建`；
2. 验证注册 runtime obstacle 后，队列完成前 `CostField` 仍是旧 committed world；
3. 验证 `ProcessRuntimeRebuildQueue()` 完成后，障碍以 `255` cost 原子提交；
4. 覆盖“不能把半更新 working world 暴露给导航 world”的约束；
5. `RuntimeDirtyQueue重排不会丢失已Pending的DirtySector`；
6. 验证 pending job 被新 dirty 重排后，旧障碍 sector 和新障碍 sector 都会最终提交；
7. `RuntimeDirtyQueue会分帧重建IslandField`；
8. 验证 runtime obstacle 封堵单格走廊后，队列完成时左右两端从同一 island 正确变为两个 island；
9. `WorldBuildQueue会构建脏World但不暴露半成品`；
10. `WorldBuildQueue会分帧完成FullWorldBuild后再原子提交`；
11. 验证低预算下 full world build 会保留 pending job、不暴露 world，完成后才具备 island field 与 portal graph；
12. `FlowTileBuildQueue会在查询前预构建路径Tile链`；
13. 验证查询先建立 path handle 后，清空 tile cache，再只推进 `ProcessFlowTileBuildQueue()` 也能预构建下游 tile 链；后续查询命中预构建 tile，不再新增 tile build；
14. `FlowTileBuildQueue低预算会保留未完成TileJob`；
15. 验证低预算下 tile queue 会留下 pending job，并在后续预算帧内完成提交，而不是单帧同步做完整条 tile 链。

仍与论文有差异：

1. 当前 priority queue 是每帧收集后排序，不是独立 binary heap；优先级已考虑单位当前位置和现有路径，但还没有玩家视野、单位距离、dirty 原因权重；
2. `CostField` 已拆到 sector 级，`IslandField` 已拆到 BFS cell 级，`PortalGraph` 已拆成子阶段，portal transitions 已拆到 from-portal/open-set 节点级；这部分 runtime dirty 与 full world build 已共用关键推进逻辑；
3. flow tile 已接入统一预算队列，且 tile job 内部已拆成 LOS / integration / flow direction / portal handoff 可暂停阶段；
4. 查询链路仍保留严格 drain：如果预构建尚未完成，查询会同步 drain 对应 tile job。这不是 fallback，但仍可能形成尖刺；后续若仍有尖峰，应继续看严格 drain 触发频率和 tile 链长度，而不是恢复旧同步构建；
5. 继续沿用旧 committed world 直到新 working world commit，这符合论文“新 path ready 后无缝切换”的动态思想，但如果后续要求动态障碍刚放下就绝对不允许旧路径穿过，需要与物理阻挡/建造阶段规则一起设计，而不是在寻路层静默 fallback。

### 23.27 本轮新增：portal target 选择从运行期 LOS 查询移入 tile 构建期缓存

重新对照论文 23.6/23.7 后确认，portal tile 内“从当前 cell 应该瞄向 portal window 哪个槽位”的信息属于 flow tile 派生数据，不应该在每个单位每帧通过 `HasGridLineOfSight()` 重新扫描 portal 候选。

此前实现的偏差：

1. `ResolveTileTargetPosition()` 在运行期为 portal tile 枚举 portal 槽位；
2. 每个候选槽位会调用 `HasGridLineOfSight()`；
3. 这在开启 portal 诊断时会把论文里的 tile 预计算退化成 per-agent/per-frame 查询；
4. 即使正常移动方向主要来自 `FlowDirections`，这条旧路径也容易在后续调试或重接逻辑时被误用。

当前修正：

1. `FlowTileCacheEntry` 增加 `PortalTargets`；
2. portal tile 构建时，在 `FlowDirections` 阶段按 cell 计算并缓存 `CachedPortalTarget`；
3. 缓存内容包括 target position、可见候选数量、选中的 portal 槽位、是否退到 opposite center；
4. `ResolveTileTargetPosition()` 运行期只读取 `PortalTargets`，如果 portal tile 缺缓存会明确抛错；
5. portal target 诊断只输出缓存选择结果、cost、distance，不再重新做 LOS；
6. 旧的 `HasGridLineOfSight()` 只保留在异常诊断和构建期 target cache 中，不再作为常规运行期 portal target 解析链路。

已补回归：

1. `PortalTile会在构建期缓存PortalTarget`；
2. 验证跨 sector 走廊中的 portal tile cell 已有缓存 target；
3. 验证直走廊场景会缓存可见候选和选中槽位，而不是运行期退化成 opposite center。
