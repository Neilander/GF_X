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
Soldier有4个主要逻辑部分：
1. Brain
2. Target
3. Move
4. Attack
除了这些外并没有特殊逻辑，整体继承MAEntity逻辑

# Target
此组件的作用就像是“眼睛”，比如说，当一个人身处危险的环境时，首先会想“我能不能打赢敌人？”然后开始
找最近的敌人；之后发现打不过，就会想“我能不能跑？”，就开始找最近的出口。这里找最近的敌人，
找最近的出口，就是Target该做的事。

这个东西会在每一个周期里更新自己的几个变量，方便其他组件取用。

# Brain

