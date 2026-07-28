# 交互系统接入清单

## 玩家侧

1. `RuntimeProcedureBase` 必须成对管理 `LogicInteractionAuthorityService`；目标选择与按键消费不依赖玩家 View。
2. 需要交互提示表现时，玩家或其子对象挂 `InteractionManager`；该组件只把逻辑目标映射为迟到 View 和 UI 事件。
3. 距离、权重、切换阈值和最短保持时间由逻辑服务以 Fix64 raw 和整数 Tick 持有，不从 View 序列化字段读取。
4. 不添加 `SphereCollider`、`Rigidbody` 或 `InteractionDetector`。候选目标由逻辑 Tick 从 `EntityRegistry` 稳定扫描。
5. 纯逻辑玩家必须具有有效 `LogicEntityId`，注册为 `EntityRegistry.Player`，并进入 `LogicEntityFrameSnapshotService`；不要求已绑定 `HeroEntity`。

## 可交互建筑侧

1. 建筑权威对象必须是已注册且存活的 `IBuildingLogicContext`，并具有有效 `LogicEntityId`；目标选择和执行不要求已绑定 `BuildingEntity`。
2. 建筑权威距离使用 authored `LogicCombatShape`，不读取 Collider 最近点。
3. 已绑定且需要显示提示的建筑 View 必须挂有效 `InteractionHost`；绑定 View 缺失 host 时明确报错，不做 fallback。
4. `InteractionHost` 只持有 UI option；目标选择和执行使用逻辑帧状态及稳定 ID。

## 输入与长按

- `Player/Interact`、`Interact2`、`Interact3` 进入通用交互逐 Tick hold。
- `Player/Build1`、`Build2`、`Build3` 和鼠标主键进入专用建造/升级面板逐 Tick hold。
- 按下、松开和鼠标屏幕坐标先写入 `LogicInputTimeline`，再由 sealed `LogicInputFrame` 消费。
- UI Update 只显示 `LogicInteractionHoldService` 的 Fix64 进度，不使用 `Time.deltaTime` 推进权威长按。

## 执行与回放

- 建造、升级、研究和回收只提交 `LogicInteractionCommand`，严格下一 Tick 生效。
- 命令使用 `EffectiveFrame + Sequence + LogicEntityId + BuildingInstanceId + payload`。
- `BuildManager` 是唯一运行时消费者；非 Apply 窗口不能直接修改交互事务。
- authority actor/target/切换帧、目标表、hold 状态和 pending/applied 命令均进入 Gameplay Hash；当前 replay 协议为 v64。
