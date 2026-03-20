# Soldier Logic

# 整体架构
项目使用 Entity + Component，每个 Component 独立职能，互不耦合。
每个 Component 都有 Tick 函数，由 Entity 在 Update 里调用。

Soldier 继承自 MAEntity，组件使用工厂模式创建（数值依赖 SO 注入）。

# Entity
Soldier 有 5 个主要逻辑部分：
1. Brain — 决策
2. Target — 索敌
3. Move — 移动
4. Attack — 攻击执行
5. Weapon — 武器信息提供

除此之外继承 MAEntity 通用逻辑。

# Target
"眼睛"组件，负责查找目标（最近敌人、最近出口等）。
每个周期更新自己的变量，供其他组件取用。

# Brain
决策组件，决定当前该做什么：

1. 没有目标 → 傻站着（Idle）
2. 周围有领袖 → 进组（GroupId = 领袖 entityId），接受组内 LJ 引力+斥力
3. 领袖太远或丢失 → 离开组（清除 GroupId）
4. 发现敌人 → Combat，只受斥力，期望移动方向由 NavMesh 计算

### 力的规则
由 Coordinator 根据 AgentState 判断：
- 敌对阵营 → 只有斥力
- 友方不同组 → 只有斥力
- 友方同组 + Follow → 吸引力 + 斥力
- 友方同组 + Combat → 只有斥力

Brain 每帧通过 SyncStateToCoordinator 同步状态给 Coordinator。
Brain 不应调整其他物体的数值，各组件自己根据信息获取改动。

### 状态切换
- Idle → Follow：领袖在 RecruitRadius 内且同阵营
- Idle → Combat：直接发现敌人
- Follow → Combat：TargetComp 发现敌人
- Follow → Idle：领袖丢失或超过 LeashRange（同时清除组）
- Combat → Follow：敌人死了（不受领袖距离限制，打到底）

### 攻击距离
有效攻击距离 = EnemyEquilibriumRadius + WeaponComp.AttackRange（回退到硬编码 WeaponRange）

### TickCombat
- 敌人在攻击范围内 → 攻击，提交零期望速度，协调器处理推开
- 敌人在范围外 → NavMesh 算路径方向作为期望速度提交给协调器

# Weapon
武器系统核心目的：让单位自由切换武器而不重写逻辑。

WeaponComp 是信息提供中心（实现 ICapability），Brain 和 AtkComp 都从这里获取攻击距离等属性，
保证数据一致。支持运行时 SwapWeapon 切换武器。

WeaponComp 在 DirectAtkCompFactory 创建 AtkComp 时一并创建并 set 到 MAEntity 上。

# Attack
（待补充）
