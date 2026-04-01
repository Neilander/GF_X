# Logic 层代码扫描报告

> 扫描日期: 2026-03-22
> 扫描范围: `Assets/AAAGame/Scripts/` 下所有 Procedure、Entity、Brain、Component、EventArgs、Manager 类

---

## 目录

1. [Procedure 流程树](#1-procedure-流程树)
2. [Entity 完整继承树](#2-entity-完整继承树)
3. [Brain 体系 & 状态机](#3-brain-体系--状态机)
4. [组件体系 (Comp)](#4-组件体系-comp)
5. [自定义事件清单](#5-自定义事件清单)
6. [核心系统类](#6-核心系统类)
7. [DataModel 清单](#7-datamodel-清单)
8. [关键调用关系图](#8-关键调用关系图)
9. [God Class 警告](#9-god-class-警告)

---

## 1. Procedure 流程树

### 1.1 继承关系

所有 Procedure 均继承自 `ProcedureBase`（GF 框架的 `FsmState<IProcedureManager>`）。

```
ProcedureBase (GF框架)
├── LaunchProcedure               (Builtin) 启动入口
├── UpdateResourcesProcedure      (Builtin) 资源热更新
├── LoadHotfixDllProcedure        (Builtin) 加载HybridCLR热更DLL
├── PreloadProcedure              预加载DataTable/Config/Language
├── ChangeSceneProcedure          场景切换中转站
├── MenuProcedure                 主菜单（当前被跳过）
├── GameProcedure                 主游戏流程（空壳）
├── GameOverProcedure             游戏结束（空壳）
├── CharacterTestProcedure        角色战斗测试流程
└── LevelTestProcedure            关卡/建筑测试流程
```

### 1.2 流程切换关系

```
LaunchProcedure
  ├──[EditorResourceMode]──→ LoadHotfixDllProcedure
  └──[非Editor]──→ UpdateResourcesProcedure ──→ LoadHotfixDllProcedure
                                                        │
                                                        ▼
                                                  PreloadProcedure
                                                        │
                                                        ▼
                                                ChangeSceneProcedure
                                                   ├──["Game"]──→ CharacterTestProcedure (当前默认)
                                                   │   (注释掉: MenuProcedure → GameProcedure)
                                                   └──["LevelTestScene"]──→ LevelTestProcedure
```

### 1.3 各流程职责

| 流程 | 文件路径 | 职责 | 关键依赖 |
|------|---------|------|---------|
| `LaunchProcedure` | `ScriptsBuiltin/Runtime/Procedures/` | 初始化 CultureInfo、Debugger，决定走热更还是编辑器模式 | `AppSettings`, `GFBuiltin` |
| `UpdateResourcesProcedure` | `ScriptsBuiltin/Runtime/Procedures/` | 检查版本号→下载资源列表→下载资源→验证资源 | `WebRequest`, `Resource` |
| `LoadHotfixDllProcedure` | `ScriptsBuiltin/Runtime/Procedures/` | 加载 AOT 元数据+热更 DLL，调用 `HotfixEntry.StartHotfixLogic` | `HybridCLR`, `Resource` |
| `PreloadProcedure` | `Scripts/Procedures/` | 加载 Config/DataTable/Language，初始化 EntityGroup/SoundGroup/UIGroup | `DataTable`, `Config`, `Localization`, `AppConfigs` |
| `ChangeSceneProcedure` | `Scripts/Procedures/` | 卸载旧场景→加载新场景→根据场景名切换到目标 Procedure | `Scene`, `Sound`, `Entity` |
| `CharacterTestProcedure` | `Scripts/Procedures/` | 初始化 DataModel→清空 EntityRegistry→生成玩家+友军+敌军→注册血条 | `EntityRegistry`, `EntityParams`, `HealthBarComp`, `InputManager` |
| `LevelTestProcedure` | `Scripts/Procedures/` | 初始化 DataModel→打开 UI→扫描场景 PresetPoint 生成设备 | `EntityPresetPoint`, `InputManager`, `UIViews` |
| `MenuProcedure` | `Scripts/Procedures/` | 主菜单，当前 `fastEnterGame=true` 直接跳转 GameProcedure | `LevelEntity`, `PlayerDataModel` |
| `GameProcedure` | `Scripts/Procedures/` | **空壳**，无任何逻辑 | - |
| `GameOverProcedure` | `Scripts/Procedures/` | **空壳**，无任何逻辑 | - |

---

## 2. Entity 完整继承树

### 2.1 继承链

```
EntityLogic (GF框架, MonoBehaviour)
└── EntityBase                          基础Entity，处理EntityParams、位置/旋转/缩放、附着
    ├── SampleEntity                    空壳，用于飘字等简单实体
    │   └── BillboardEntity             自动面向摄像机
    ├── ParticleEntity                  粒子特效，支持生命周期自动回收
    ├── LevelEntity                     关卡实体（空壳）
    ├── DeviceEntity                    建筑/设备，管理交互选项
    ├── HitBox                          攻击判定盒，检测HurtBox碰撞
    ├── AbstractProjectile (abstract)   弹道基类，管理HitBox生命周期
    │   └── DirectionProjectile         方向弹道
    └── GeneralCreature                 生物基类：Side/Alive/血量/HurtBox/选择
        └── CompCreature                组件锁系统：LockComp/ResumeComp/CanRun
            └── MAEntity ⚠️             **核心战斗实体**：Brain+Move+Atk+Target+Weapon+MoveExecutor
                ├── SkillEntity         技能层：ISkillComp + CastRange显示
                │   └── CharacterEntity 玩家角色：绑定相机、创建Brain、注册GroupMove
                ├── SoldierEntity       小兵：DirectAtk、NavMesh寻路、GroupMove
                └── PunchBagEntity      沙袋（测试用）
```

### 2.2 各 Entity 职责

| Entity | 文件路径 | 职责 | 关键方法 |
|--------|---------|------|---------|
| `EntityBase` | `Entity/Core/EntityBase.cs` | 解析 EntityParams，设置位置/旋转/缩放/层级，处理附着 | `OnInit`, `OnShow`, `OnHide` |
| `GeneralCreature` | `GeneralCreature/GeneralCreature.cs` | Side/Alive/血量管理，HurtBox 初始化，TakeDamage，选择系统 | `TakeDamage`, `SetUpHurtBox`, `InSelection` |
| `CompCreature` | `GeneralCreature/CompCreature.cs` | 组件锁机制（多把锁计数），CanRun 检查 | `LockComp`, `ResumeComp`, `CanRun` |
| `MAEntity` | `Entity/MAEntity.cs` | **核心实体**：组装所有组件，驱动Update循环，注册EntityRegistry/GroupMove | `Update`, `SetUpMAComp`, `SetBrain` |
| `SkillEntity` | `Entity/SkillEntity.cs` | 在 MAEntity 基础上加入技能组件和施法范围显示 | `SetUpSkillComp`, `ShowCastRange` |
| `CharacterEntity` | `Entity/CharacterEntity.cs` | 玩家角色：Side赋值、Brain创建、相机绑定、GroupMove注册 | `OnShow`, `SetUpSkillComp`, `SetUpMAComp` |
| `SoldierEntity` | `Entity/SoldierEntity.cs` | 小兵：Side赋值、Brain创建、GroupMove注册、Debug绘制 | `OnShow`, `Update`, `SetUpMAComp` |
| `DeviceEntity` | `Entity/DeviceEntity.cs` | 建筑设备：管理 InteractionHost 和交互选项 | `OnShow`, `EnsureInteractionHost` |
| `HitBox` | `DamageSystem/HitBox.cs` | 攻击盒：碰撞检测 HurtBox，记录命中历史，调用 DamageHelper | `Activate`, `OnTriggerStay` |
| `AbstractProjectile` | `Projectile/AbstractProjectile.cs` | 弹道基类：管理 HitBox 的创建和生命周期 | `StartMove`, `Move(abstract)`, `DestroyProjectile(abstract)` |

---

## 3. Brain 体系 & 状态机

### 3.1 接口定义

```
IControlBrain                     决策输出接口
├── Move : Vector2                移动方向
├── Attack : bool                 攻击指令
├── Skill1/2/3 : bool            技能指令

ITickBrain                        每帧更新接口
└── Tick(IEntityContext, float)   AI 逻辑主循环
```

### 3.2 Brain 实现

| Brain | 文件路径 | 接口 | 状态机 | 职责 |
|-------|---------|------|--------|------|
| `PlayerBrain` | `Entity/PlayerBrain.cs` | `IControlBrain` | 无 | 从 InputModel 读取输入，转换为移动/攻击/技能指令 |
| `SoldierAIBrain` | `Movement/SoldierAIBrain.cs` | `IControlBrain + ITickBrain` | **Idle→Follow→Combat** | 小兵AI：跟随Leader、NavMesh寻路、ORCA协调、死区逻辑 |
| `EnemyAIBrain` | `Entity/EnemyAIBrain.cs` | `IControlBrain + ITickBrain` | 无（简单距离判定） | 敌方AI：仇恨追击+分离力 |
| `FriendlyAIBrain` | `Entity/FriendlyAIBrain.cs` | `IControlBrain + ITickBrain` | 无（优先级判定） | 友军AI：优先攻击→跟随玩家→待机 |
| `ScriptedBrain` | `Tests/Editor/Sim/` | `IControlBrain + ITickBrain` | 无 | 测试用脚本化Brain |

### 3.3 SoldierAIBrain 状态转换

```
        ┌─── 有敌人 ───→ Combat
        │                    │
  Idle ─┤                    │ 敌人死了
        │                    ▼
        └── leader近 ──→ Follow ←──┘
                           │
                      leader丢失/超距
                           │
                           ▼
                         Idle (清组, 重新找leader)
```

**关键机制：**
- Follow 状态下有**死区**概念：距离 leader 在 `[leaderEqR, leaderEqR + 3f]` 范围内时停止主动移动，仅接收 ORCA 协调力
- Combat 状态下通过 `SubmitDesiredVelocity` 将 NavMesh 方向提交给 `GroupMoveCoordinator`
- 通过 `EntityRegistry.GetClosestLeader` 惰性获取 leader

### 3.4 BrainFactory

```csharp
// Entity/BrainFactory.cs (static)
BrainFactory.Create(BrainType, MAEntity, EntityParams) → IControlBrain
  Player    → new PlayerBrain()
  SoldierAI → new SoldierAIBrain() + Inject()
  EnemyAI   → 标记 "not used"
  FriendlyAI→ 标记 "not used"
```

---

## 4. 组件体系 (Comp)

### 4.1 接口继承

```
ICapability                       组件开关接口
├── ShutDown()                    禁用
└── Resume()                      恢复

IMoveComp : ICapability           移动组件
├── Init(IEntityContext)
├── Move(float dt)                每帧驱动
├── MoveTo(Vector3)               NavMesh寻路
├── StopMove()
└── GetNavDirection() : Vector3

IAtkComp : ICapability            攻击组件
├── Init(IEntityContext)
└── Attack(float dt)

ITargetingComp : ICapability      索敌组件
├── Init(IEntityContext)
├── UpdateTargeting(float dt)
├── CurrentTarget : IEntityContext
├── FollowTarget : IEntityContext
└── AggroRange/ForgetRange/FollowSearchRange

ISkillComp : ICapability          技能组件
├── Init(SkillEntity, List<BasicSkill>)
└── Skill()

IMoveExecutor                     底层移动执行器
├── SetInput(Vector3)             Brain/MoveComp 设置期望移动
├── AddExternal(Vector3)          外部力（协调器）
├── SetOverride(Vector3)          强制移动（击退）
└── Execute()                     实际调用 CharacterController.Move
```

### 4.2 组件实现

| 组件 | 文件路径 | 实现接口 | 职责 |
|------|---------|---------|------|
| `CharacterMoveComp` | `PlayerControl/CharacterMoveComp.cs` | `IMoveComp` | NavMesh 寻路 + 手动输入，动画驱动，防卡检测 |
| `NoMoveComp` | `GeneralCreature/NoMoveComp.cs` | `IMoveComp` | 空实现，不移动 |
| `DirectAtkComp` | `GeneralCreature/DirectAtkComp.cs` | `IAtkComp` | 直接伤害（WindUp→DealDamage→WindDown→Cooldown），锁定移动 |
| `NoAtkComp` | `GeneralCreature/NoAtkComp.cs` | `IAtkComp` | 空实现，不攻击 |
| `CharacterTargetingComp` | `PlayerControl/CharacterTargetingComp.cs` | `ITargetingComp` | 降频扫描 EntityRegistry，按阵营+距离索敌，同时维护跟随目标 |
| `NoTargetingComp` | `GeneralCreature/NoTargetingComp.cs` | `ITargetingComp` | 空实现 |
| `CharacterSkillComp` | `PlayerControl/CharacterSkillComp.cs` | `ISkillComp` | 3技能槽位管理：冷却、输入检测、技能执行、组件互锁 |
| `PlayerSkillComp` | `PlayerControl/PlayerSkillComp.cs` | `ISkillComp` | 玩家技能组件 |
| `WeaponComp` | `GeneralCreature/WeaponComp.cs` | `ICapability` | 纯数据：持有 WeaponData，提供 AttackRange |
| `MoveExecutor` | `GeneralCreature/MoveExecutor.cs` | `IMoveExecutor` | 底层移动执行：累积 input+external+override → CharacterController.Move |
| `DurationMoveEffectComp` | - | - | 持续移动效果（击退等） |

### 4.3 工厂加载

```
FactoryHelper (static) — 异步加载 ScriptableObject 工厂，缓存后创建组件
├── CreateMoveComp(path, MAEntity)      → MoveCompFactory → IMoveComp
├── CreateAtkComp(path, MAEntity)       → AtkCompFactory  → IAtkComp
├── CreateTargetingComp(path, MAEntity) → TargetingCompFactory → ITargetingComp
└── CreateSkillComp(path, SkillEntity)  → SkillCompFactory → ISkillComp

工厂配置来源: CharacterMAFactoryTable (DataTable)
  每行：CharacterKey → MoveFactoryPath + AttackFactoryPath
```

### 4.4 DirectAtkComp 状态机

```
Idle ──[Brain.Attack + 有目标 + 在攻击范围]──→ WindUp (锁定MoveComp)
                                                   │
                                              前摇结束, DealDamage
                                                   │
                                                   ▼
                                              WindDown
                                                   │
                                              后摇结束, 解锁MoveComp
                                                   │
                                                   ▼
                                              Cooldown ──→ Idle
```

---

## 5. 自定义事件清单

### 5.1 游戏业务事件 (Assets/AAAGame/Scripts/EventArgs/)

| 事件类 | EventId | 关键字段 | 触发者 | 监听者 |
|--------|---------|---------|--------|--------|
| `CreatureHealthChangedEventArgs` | `typeof().GetHashCode()` | `EntityId`, `CurrentHealth`, `MaxHealth`, `Delta` | `GeneralCreature.TakeDamage` | `HealthBarComp` |
| `GameplayEventArgs` | 同上 | `EventType`(GameOver), `Params` | 游戏流程 | UI层 |
| `GFEventArgs` | 同上 | `EventType`(ApplicationQuit), `UserData` | 框架 | 各系统 |
| `PlayerDataChangedEventArgs` | 同上 | `DataType`, `OldValue`, `Value` | `PlayerDataModel` | UI层 |
| `ProfileDataChangedEventArgs` | 同上 | `DataType`, `OldValue`, `Value` | `ProfileDataModel` | UI层 |
| `ItemAmountChangedEventArgs` | 同上 | `ItemIdentifier`, `OldValue`, `Value` | 物品系统 | UI层 |
| `InteractionFocusChangedEventArgs` | 同上 | `Target`(InteractionHost) | `InteractionManager` | UI提示 |
| `InteractionOptionTriggeredEventArgs` | 同上 | `Target`, `Option` | `InteractionHost.TryExecute` | UI/逻辑 |
| `TechUnlockedEventArgs` | 同上 | `TechId` | 科技系统 | UI层 |
| `TechResearchFailedEventArgs` | 同上 | `TechId`, `Reason` | 科技系统 | UI层 |

### 5.2 内建事件 (Assets/AAAGame/ScriptsBuiltin/)

| 事件类 | 关键字段 | 用途 |
|--------|---------|------|
| `LoadHotfixDllEventArgs` | `DllName`, `Assembly`, `UserData` | 热更DLL加载结果通知 |

### 5.3 事件模式

所有自定义事件均：
- 继承 `GameEventArgs`
- 使用 `ReferencePool.Acquire<T>()` + `Create()` 静态工厂
- 实现 `Clear()` 归还到引用池
- `EventId` = `typeof(T).GetHashCode()`

---

## 6. 核心系统类

### 6.1 GroupMoveManager ⚠️ (接近 God Class)

| 属性 | 值 |
|------|---|
| **文件** | `Scripts/Movement/GroupMoveManager.cs` |
| **继承** | `MonoBehaviour` (Singleton) |
| **职责** | ORCA 群体移动的 Unity 侧管理器 |
| **关键方法** | `RegisterAgent`, `UnregisterAgent`, `UpdateAgentPosition`, `RegisterCircleObstacle`, `RegisterBoxObstacle`, `SyncParams`, `SaveParams/LoadParams` |
| **关键依赖** | `GroupMoveCoordinator`, `MAEntity`, `CharacterController` |
| **驱动时机** | `LateUpdate` 中调用 `Coordinator.Resolve()` |
| **⚠️ 问题** | 承担了参数调优面板、PlayerPrefs 持久化、Gizmos 绘制、障碍物管理等多个职责 |

### 6.2 GroupMoveCoordinator

| 属性 | 值 |
|------|---|
| **文件** | `Scripts/Movement/GroupMoveCoordinator.cs` |
| **继承** | 纯 C# 类 |
| **职责** | ORCA 风格的 LJ 力计算：同组/跨组/敌对/障碍物 力场规则 |
| **关键方法** | `RegisterAgent`, `SubmitDesiredVelocity`, `Resolve`, `SetAgentLeader/Group/State`, `SyncAllAgentParams` |
| **状态枚举** | `AgentState { Idle, Follow, Combat }` |
| **驱动时机** | 每帧由 `GroupMoveManager.LateUpdate` 调用 `Resolve()` |

### 6.3 EntityRegistry

| 属性 | 值 |
|------|---|
| **文件** | `Scripts/Entity/EntityRegistry.cs` |
| **继承** | 静态类 |
| **职责** | 全局实体注册表：跟踪所有活着的 `IEntityContext`，维护 Player 引用 |
| **关键方法** | `Register`, `RegisterAsPlayer`, `Unregister`, `Clear`, `GetClosestLeader` |
| **被查询者** | `CharacterTargetingComp`（索敌遍历）、`SoldierAIBrain`（找leader） |

### 6.4 InputManager

| 属性 | 值 |
|------|---|
| **文件** | `Scripts/UTManagers/InputManager.cs` + `InputManager.UIControl.cs` (partial) |
| **继承** | `GameFrameworkComponent` |
| **职责** | 读取 Unity InputSystem → 写入 `InputModel` DataModel |
| **状态机** | `InputState { None, StartScreen, Game, UIForm }` |
| **UI 联动** | 自动在 blocking UI 打开时切换到 UIForm 状态 |

### 6.5 InteractionManager

| 属性 | 值 |
|------|---|
| **文件** | `Scripts/Interaction/InteractionManager.cs` |
| **继承** | `MonoBehaviour` |
| **职责** | 每帧评分候选 InteractionHost，选择最优交互目标，处理输入触发 |
| **关键依赖** | `InteractionDetector`（触发器候选列表）、`InteractionHost`（交互宿主）、`InputModel` |

### 6.6 CameraController

| 属性 | 值 |
|------|---|
| **文件** | `Scripts/Common/CameraController.cs` |
| **继承** | `MonoBehaviour` (Singleton) |
| **职责** | Cinemachine 虚拟相机管理：跟随、视角切换、震屏、URP 叠加 |
| **关键依赖** | Cinemachine, DOTween, UniTask, URP |

### 6.7 DamageSystem

| 类 | 文件路径 | 职责 |
|----|---------|------|
| `HitBox` | `DamageSystem/HitBox.cs` | 攻击判定盒，EntityBase 子类，碰撞检测 HurtBox |
| `HurtBox` | `DamageSystem/HurtBox.cs` | 受伤标记组件，持有 Owner (ITargetable) |
| `DamageHelper` | - | 伤害计算辅助 |
| `EntitySideHelper` | - | 阵营判定（友军/敌方） |

### 6.8 HealthBarComp

| 属性 | 值 |
|------|---|
| **文件** | `Scripts/UI/HealthBarComp.cs` |
| **继承** | `MonoBehaviour` |
| **职责** | 运行时程序化创建世界空间血条 Canvas，监听 `CreatureHealthChangedEventArgs` |
| **创建方式** | `HealthBarComp.Create(entityId, followTarget, curHp, maxHp)` |

---

## 7. DataModel 清单

| DataModel | 文件路径 | 基类 | 职责 |
|-----------|---------|------|------|
| `InputModel` | - | `DataModelBase` | 输入状态容器：Move/Attack/Skill/Interaction |
| `PlayerDataModel` | `DataModel/PlayerDataModel.cs` | `DataModelStorageBase` | 玩家数据（关卡ID等），持久化 |
| `ProfileDataModel` | `DataModel/ProfileDataModel.cs` | `DataModelStorageBase` | 档案数据，持久化 |
| `ItemDataModel` | `DataModel/Craft/ItemDataModel.cs` | `DataModelBase` | 物品定义数据 |
| `ItemCollectionDataModel` | `DataModel/Craft/ItemCollectionDataModel.cs` | `DataModelStorageBase` | 物品持有数据，持久化 |
| `DeviceDataModel` | `DataModel/Build/DeviceDataModel.cs` | `DataModelBase` | 设备/建筑定义数据 |
| `CraftingDeviceDataModel` | `DataModel/Craft/CraftingDeviceDataModel.cs` | `DataModelBase` | 制造台数据 |
| `LocalizationTextDataModel` | `DataModel/Text/LocalizationTextDataModel.cs` | `DataModelBase` | 本地化文本 |
| `TechNodeDataModel` | `DataModel/Tech/TechNodeDataModel.cs` | `DataModelBase` | 科技树节点定义 |
| `TechProgressDataModel` | `DataModel/Tech/TechProgressDataModel.cs` | `DataModelStorageBase` | 科技研究进度，持久化 |
| `CapabilityProgressDataModel` | `DataModel/Tech/CapabilityProgressDataModel.cs` | `DataModelStorageBase` | 能力解锁进度，持久化 |

---

## 8. 关键调用关系图

### 8.1 MAEntity Update 循环 (每帧)

```
MAEntity.Update()
  │
  ├── 1. GroupMoveManager.UpdateAgentPosition(this)     // 同步位置到协调器
  │
  ├── 2. Brain.Tick(this, dt)                           // AI 决策
  │       ├── PlayerBrain: 读 InputModel → 输出 Move/Attack/Skill
  │       └── SoldierAIBrain.Tick():
  │             ├── UpdateState() → Idle/Follow/Combat
  │             ├── TickFollow() → 死区判定 → NavMesh → SubmitDesiredVelocity
  │             └── TickCombat() → NavMesh → SubmitDesiredVelocity / Attack=true
  │
  ├── 3. targetComp.UpdateTargeting(dt)                 // 索敌 (遍历 EntityRegistry)
  │
  ├── 4. moveComp.Move(dt)                              // 移动
  │       └── CharacterMoveComp: Brain.Move 手动输入 OR NavMesh 路径跟踪
  │           → MoveExecutor.SetInput(dir * speed)
  │
  ├── 5. atkComp.Attack(dt)                             // 攻击
  │       └── DirectAtkComp: 状态机 Idle→WindUp→DealDamage→WindDown→Cooldown
  │           ├── LockComp(MoveComp) 前摇时锁定移动
  │           └── target.TakeDamage() 造成伤害
  │
  ├── 6. durationMoveEffectComp.ApplyEffect(dt)         // 持续移动效果
  │
  └── 7. moveExecutor.Execute()                         // 最终执行移动
          └── CharacterController.Move(finalVelocity)
```

### 8.2 GroupMove 协调流程 (LateUpdate)

```
GroupMoveManager.LateUpdate()
  └── Coordinator.Resolve()
        ├── 遍历所有 pending VelocityRequest
        ├── ComputeSafeVelocity(agentId, desiredVelocity)
        │     ├── LJ 同组力（leader→unit: 仅斥力）
        │     ├── LJ 跨组力
        │     ├── LJ 敌对力
        │     ├── 障碍物斥力
        │     └── 混合 → clamp → safeVelocity
        └── 回调 Brain.ApplyVelocity(safeVelocity)
              └── MoveComp.MoveTo(target) 或 StopMove()
```

### 8.3 伤害流程

```
Brain.Attack = true
  → AtkComp (DirectAtkComp/HitBox-based)
    → target.TakeDamage(damage, modType)
      → GeneralCreature.TakeDamage()
        ├── Animator.SetTrigger("GetHit")
        ├── CreaturePropertyManager.ModifyCurrentProperty(HealthCurrent, -damage)
        ├── GF.Event.Fire(CreatureHealthChangedEventArgs)
        │     └── HealthBarComp 更新血条
        └── if (health <= 0) → GF.Entity.HideEntity(Id)
```

### 8.4 实体创建流程

```
Procedure.OnEnter()
  └── GF.Entity.ShowEntity<T>(prefabName, group, entityParams)
        └── [GF框架异步加载Prefab]
              └── OnShowEntitySuccess 事件
                    └── EntityLogic.OnInit() → OnShow()
                          ├── EntityBase: 解析 EntityParams
                          ├── GeneralCreature: 初始化 Side/Alive/HurtBox/PropertyManager
                          ├── CompCreature: 初始化锁表
                          ├── MAEntity: FactoryHelper 加载 Move/Atk 组件, 注册 EntityRegistry
                          ├── CharacterEntity: Side赋值, BrainFactory.Create, 相机绑定, RegisterToGroupMove
                          └── SoldierEntity: Side赋值, BrainFactory.Create, RegisterToGroupMove
```

---

## 9. God Class 警告

### ⚠️ MAEntity — 核心 God Class

**文件**: `Scripts/Entity/MAEntity.cs`
**行数**: ~141 行（本身不多，但继承链深且职责广）

**问题**:
- 继承链 5 层：`EntityLogic → EntityBase → GeneralCreature → CompCreature → MAEntity`
- 同时是：组件容器（Move/Atk/Target/Weapon/Skill/MoveExecutor）+ 更新循环驱动器 + Brain 宿主 + EntityRegistry/GroupMove 注册点
- `Update()` 方法承担了 7 个步骤的更新驱动
- `IEntityContext` 接口暴露了大量字段和方法

**建议**: 将 Update 驱动逻辑拆分为独立的 `EntityUpdateDriver`，组件注册/反注册拆分为生命周期管理器。

---

### ⚠️ GroupMoveManager — 职责过载

**文件**: `Scripts/Movement/GroupMoveManager.cs`

**问题**:
- 同时承担：Inspector 调参面板、PlayerPrefs JSON 持久化、障碍物管理、Agent 生命周期桥接、LateUpdate 驱动、Gizmos 绘制
- 大量 `public float` 字段用于 Inspector 调参，与核心逻辑混杂

**建议**: 将调参面板、持久化、Gizmos 绘制拆分为独立的 Editor/Debug 类。

---

### ⚠️ SoldierAIBrain — 逻辑密集

**文件**: `Scripts/Movement/SoldierAIBrain.cs`
**行数**: ~346 行

**问题**:
- 同时包含：状态机 + 死区逻辑 + ORCA 协调器交互 + NavMesh 寻路委托 + 攻击判定
- 紧耦合 `GroupMoveManager` 和 `GroupMoveCoordinator`
- 状态切换逻辑与具体行为逻辑混在一起

**建议**: 将每个状态的 Tick 逻辑拆为独立的策略类。

---

### ⚠️ CharacterTestProcedure — 测试代码生产化

**文件**: `Scripts/Procedures/CharacterTestProcedure.cs`

**问题**:
- 作为当前默认的游戏入口流程，但类名和注释都表明是"测试"
- 硬编码了生成数量、位置、间距等参数
- 直接在 `OnShowEntitySuccess` 中做实体注册和血条创建

**建议**: 应该有正式的 GameProcedure 来承担这些职责，CharacterTestProcedure 仅用于 Editor 测试。
