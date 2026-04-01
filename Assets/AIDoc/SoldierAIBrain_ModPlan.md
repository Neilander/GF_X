# SoldierAIBrain 修改计划

对照 SoldierLogic.md 中 Brain 部分的描述，对当前 SoldierAIBrain.cs 的修改计划。

## 1. 领袖获取方式

**问题**: 当前 `_player` 硬编码为领袖，`Inject` 只接受玩家引用
**改动**: Brain 不自己找 leader，而是问 `EntityRegistry` 要。`EntityRegistry` 的改动后续再做，Brain 这边先改成调用 `EntityRegistry` 获取领袖的接口
**涉及**: `Inject()` 方法、`Tick()` 中的惰性刷新逻辑、`_player` 字段重命名为 `_leader`

## 2. 状态转换规则修正

**问题**:
- Follow 没有回 Idle 的路径
- Combat 有 `LeashRange` 拉回 Follow（不应该有）

**改动**:
- Follow 加判断：离领袖超过阈值 → 回 Idle，忘掉领袖
- Combat 删掉 LeashRange 回 Follow 的逻辑，小兵打到底（敌人死了才回 Follow）

**涉及**: `UpdateState()` 中 Follow 和 Combat 分支

## 3. Brain 不改其他组件的值

**问题**: Brain 直接写 `self.TargetComp.AggroRange` 和 `self.TargetComp.CurrentTarget`
**改动**: 删掉 `SyncTargetComp()` 整个方法，删掉 `Tick()` 中 `AggroRange` 赋值。索敌应该由 TargetComp 自己完成
**涉及**: `SyncTargetComp()` 方法、`Tick()` 中两处调用

## 4. Combat 追敌时提交 NavMesh 方向给 LJ

**问题**: 追敌时 NavMesh 和 LJ 分开处理——`MoveTo(enemy)` 走路径，`SubmitCombatLJ` 只提交零速度取斥力
**改动**: 追敌时从 NavMesh 获取下一帧期望方向，作为 desiredVelocity 提交给协调器，协调器叠加 LJ 力后返回最终速度，用最终速度执行移动
**涉及**: `TickCombat()` 方法、`SubmitCombatLJ()` 可能合并到 `SubmitToCoordinator`

## 5. Group 概念澄清（不删除）

**文档纠正**: Group 概念是正确的，不是残留
**规则**:
- 单位进入组后，受到**组内成员的引力**
- 两个不同组的友方单位之间，**只有斥力**没有引力
- 敌方单位之间只有斥力

**改动**: 不删除 group 相关逻辑，保留 `_joinedGroup` 和 `SetAgentLeader`。后续需确认协调器是否正确区分了组内引力和组间斥力
