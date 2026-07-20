# 交互系统接入清单

## 玩家侧

1. 玩家或其子对象挂 `InteractionManager`。
2. 距离、权重、切换阈值和最短保持时间只作为配置边界；运行时会量化为 Fix64 和整数 Tick。
3. 不再添加 `SphereCollider`、`Rigidbody` 或 `InteractionDetector`。候选目标由逻辑 Tick 从 `EntityRegistry` 稳定扫描。
4. 玩家必须已绑定有效 `LogicEntityId`，并进入 `LogicEntityFrameSnapshotService`。

## 可交互建筑侧

1. 建筑必须是已注册且存活的 `BuildingEntity`，并具有有效 `LogicEntityId`。
2. 建筑权威距离使用 authored `LogicCombatShape`，不读取 Collider 最近点。
3. 有可见选项的建筑必须挂 `InteractionHost`。缺失时明确报错，不做 fallback。
4. `InteractionHost` 只持有 UI option；目标选择使用逻辑帧状态，交互执行提交稳定 ID 命令。

## 输入与长按

- `Player/Interact`、`Interact2`、`Interact3` 进入通用交互逐 Tick hold。
- `Player/Build1`、`Build2`、`Build3` 和鼠标主键进入专用建造/升级面板逐 Tick hold。
- 按下、松开和鼠标屏幕坐标先写入 `LogicInputTimeline`，再由 sealed `LogicInputFrame` 消费。
- UI Update 只显示 `LogicInteractionHoldService` 的 Fix64 进度，不使用 `Time.deltaTime` 推进权威长按。

## 执行与回放

- 建造、升级、研究和回收只提交 `LogicInteractionCommand`，严格下一 Tick 生效。
- 命令使用 `EffectiveFrame + Sequence + LogicEntityId + BuildingInstanceId + payload`。
- `BuildManager` 是唯一运行时消费者；非 Apply 窗口不能直接修改交互事务。
- 当前目标、hold 状态、pending/applied 命令均进入 Gameplay Hash；命令进入 replay v4 metadata。
