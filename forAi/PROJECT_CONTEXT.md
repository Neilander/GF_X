# AI 协作须知

## 项目基本信息

- 这是一个 Unity 游戏项目，基于 **GF_X（UnityGameFramework）** 框架构建。
- **master 分支**是框架原版（来自网上），不要修改。
- **BranchForClaude 分支**是实际开发分支，AI 的所有操作都应在此分支上进行。

## 框架 vs 项目代码

- `Assets/Plugins/UnityGameFramework/` — GF_X 框架代码，**不要修改**。
- `Assets/AAAGame/` — 项目自定义代码，**所有开发工作都在这里**。

## 项目架构概览

### 程序集划分
- `Builtin.Runtime.asmdef`（ScriptsBuiltin/）— 启动层代码，不可热更新
- `Hotfix.asmdef`（Scripts/）— 游戏逻辑层，支持 HybridCLR 热更新，绝大部分代码在此

### 核心自研系统（均在 Assets/AAAGame/Scripts/ 下）

| 系统 | 目录 | 说明 |
|------|------|------|
| 行为树 | BehaviorTree/ | AI 决策系统，节点式架构 |
| 动作系统 | ActionSystem/ | 移动、攻击、位置选择等动作指令 |
| 玩家控制 | PlayerControl/ | 输入处理、技能释放、目标选择 |
| 通用生物 | GeneralCreature/ | 组件式生物基类 |
| 实体 | Entity/ | 角色、敌人、设备、技能实体定义 |
| 属性系统 | Property/ | 数值与修改器 |
| 技能系统 | SkillSystem/ | 技能执行框架 |
| 伤害系统 | DamageSystem/ | Hitbox/Hurtbox 与伤害计算 |
| 投射物 | Projectile/ | 投射物生成与运动 |
| 制作系统 | Craft/ | 物品制作 |
| 科技树 | Tech/ | 科技升级 |
| 交互 | Interaction/ | 世界物体交互 |
| UI | UI/ | 界面系统（商店、制作、设置等） |
| 数据表 | DataTable/ | 配置驱动的数据表 |
| 网络 | Network/ | 多人联网模块 |
| 框架扩展 | Extension/ | 对 GF_X 框架的扩展封装 |
| 流程 | Procedures/ | 游戏流程状态机（菜单、加载、游戏、结算） |

### 关键第三方依赖
- A* Pathfinding（寻路）
- DOTween（动画缓动）
- UniTask（异步）
- Protobuf（网络协议）
- HybridCLR（热更新）

### 场景
- Launch.unity — 启动场景
- Game.unity — 主游戏场景
- LevelTestScene.unity / CharacterAndSkillTestScene.unity — 测试场景

## Testcase 系统（纯逻辑测试层）

项目已建立基于接口的测试体系，AI 可以先写测试再写实现。

### 核心接口
- `IEntityContext` — 实体上下文接口，组件和 Brain 通过它访问实体（不直接依赖 MAEntity）
- `IMoveExecutor` — 移动执行器接口，真实版用 CharacterController，测试版直接改坐标

### Sim 组件（在 Tests/Editor/Sim/ 下）
| Sim 组件 | 替代 | 作用 |
|----------|------|------|
| `SimEntityContext` | MAEntity | 纯数据实体，不依赖 MonoBehaviour |
| `SimMoveExecutor` | MoveExecutor | 直接改 Position，不用 CharacterController |
| `SimMoveComp` | CharacterMoveComp | 直线移动，不用 NavMesh |
| `SimAtkComp` | CharacterAttackComp | 纯状态机，不用 Animator |
| `SimTargetingComp` | CharacterTargetingComp | 列表距离查找，不用 Physics |
| `ScriptedBrain` | EnemyAIBrain | 逐帧脚本化输入 |

### 写测试的方式
```csharp
[Test]
public void 我的测试()
{
    var ctx = new SimEntityContext { Position = Vector3.zero, Side = SideType.PlayerSide };
    var executor = new SimMoveExecutor { Position = Vector3.zero };
    ctx.MoveExecutor = executor;
    var moveComp = new SimMoveComp();
    moveComp.Init(ctx);
    ctx.MoveComp = moveComp;

    // 模拟若干帧...
    moveComp.MoveTo(new Vector3(10, 0, 0));
    for (int i = 0; i < 100; i++)
    {
        ctx.SyncPositionToExecutor();
        moveComp.Move(0.016f);
        executor.Execute(0.016f);
        ctx.SyncPositionFromExecutor();
    }

    Assert.Less(Vector3.Distance(ctx.Position, new Vector3(10, 0, 0)), 1f);
}
```

### 测试文件位置
- 测试程序集：`Assets/AAAGame/Tests/Editor/AAAGame.Tests.Editor.asmdef`
- 测试文件：`Assets/AAAGame/Tests/Editor/*.cs`

## 设计原则

### 拆解 + 调用（最重要）
- **不要在 prefab 上预挂不相关的组件**。应在运行时由外部代码（Procedure / Manager）创建并组合。
- 例：血条不挂在单位 prefab 上，而是由 Procedure 在 Show 单位后独立创建 `HealthBarComp.Create(entityId, transform, ...)`。
- 单位 prefab 只包含自身核心（模型、碰撞体、HurtBox），附加功能（血条、选择框、特效）都是独立的，通过事件通信。
- **事件解耦**：系统间通过 `GF.Event.Fire` / `GF.Event.Subscribe` 通信，用 entityId 区分不同单位的消息，避免直接引用。

### 其他
1. 修改代码前先理解 GF_X 框架的模式（Procedure、Entity、DataTable、UI 等模块的使用方式）。
2. 项目当前开发重心在**带兵打仗**（小兵战斗 + AI）。
3. 中文注释为主，代码命名用英文。
4. 配置数据在 `Assets/AAAGame/DataTable/` 下，是数据驱动设计。
5. 组件接口统一使用 `IEntityContext` 而非 `MAEntity`，方法接受 `float deltaTime` 参数。
6. 写新功能前建议先在 `Tests/Editor/` 下写测试。
