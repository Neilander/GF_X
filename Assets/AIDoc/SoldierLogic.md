# Soldier Logic

<!-- 在这里整理士兵逻辑相关的笔记 -->

# 整体回忆
项目使用Entity+Component，每个Component都有自己的独立职能，互相不应该耦合，而是要解耦
每个Component都有个Tick函数，由Entity在Update里调用

Soldier继承自MAEntity，需要查阅相关文档，没有找到就询问用户相关细节；

如果发现不再继承，记得提醒用户更改

同时组件使用“工厂模式”创建，即变量数值依赖So注入，像CharacterTargetingComp就有
CharcterTargetingFactory

# Entity
Soldier有5个主要逻辑部分：
1. Brain
2. Target
3. Move
4. Attack
5. Weapon - 等我介绍
除了这些外并没有特殊逻辑，整体继承MAEntity逻辑

# Target
此组件的作用就像是“眼睛”，比如说，当一个人身处危险的环境时，首先会想“我能不能打赢敌人？”然后开始
找最近的敌人；之后发现打不过，就会想“我能不能跑？”，就开始找最近的出口。这里找最近的敌人，
找最近的出口，就是Target该做的事。

这个东西会在每一个周期里更新自己的几个变量，方便其他组件取用。

# Brain
此组件是脑子，用于决定现在该执行什么，整体逻辑应该是这样的：
1. 如果没有目标，就傻站着（Idle）
2. 如果周围有领袖，就进入一个组（GroupId = 领袖的 entityId），接受组内 LJ 的引力+斥力
3. 领袖不一定是玩家（目前 EntityRegistry.GetClosestLeader 只返回 Player，之后扩展）。
   记住一个领袖后只有领袖太远或丢失才会离开组（清除 GroupId，重置 _joinedGroup）
4. 如果找到敌人进入 Combat，只接受斥力（同组也不受吸引力），期望移动方向由 NavMesh 计算

### 力的规则（由 Coordinator 根据 AgentState 判断）
- 敌对阵营：只有斥力
- 友方不同组：只有斥力
- 友方同组 + Follow 状态：吸引力 + 斥力
- 友方同组 + Combat 状态：只有斥力

Brain 每帧通过 SyncStateToCoordinator 把状态（Idle/Follow/Combat）同步给 Coordinator，
Coordinator 的 AgentData.State 字段决定是否施加吸引力。

同时 Brain 也不应该调整其他物体的数值，而是由其他组件自己根据信息获取改动。

### 状态切换
- Idle → Follow：领袖在 RecruitRadius 内且同阵营
- Follow → Combat：TargetComp 发现敌人
- Follow → Idle：领袖丢失或超过 LeashRange（同时清除组）
- Combat → Follow：敌人死了（combat 不受领袖距离限制，打到底）
- Idle → Combat：直接发现敌人

### 攻击距离
有效攻击距离 = 敌对斥力半径（EnemyEquilibriumRadius）+ WeaponRange

### TickCombat
敌人在攻击范围内就攻击，提交零期望速度让协调器处理推开；
敌人在范围外，用 NavMesh 算路径方向作为期望速度提交给协调器

