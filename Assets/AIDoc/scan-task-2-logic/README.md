# Scan Task 2: Logic 层扫描

## 扫描范围
扫描项目中所有**核心逻辑**相关的代码：Procedure 流程、Entity 实体、Fsm 状态机、战斗/背包/技能等核心玩法系统。

## 操作步骤

1. 在 Claude Code 中打开 GF_X 项目根目录
2. 粘贴下面的 prompt 执行
3. 结果保存到 `Assets/AIDoc/scan-results/scan-result-2-logic.md`

## Prompt

```
请扫描本项目的 Logic 层代码，生成继承链文档。

扫描目标：
- 所有继承 ProcedureBase 的流程类
- 所有继承 EntityLogic / EntityData 的实体类
- 所有 Fsm 状态类（FsmState）
- 所有自定义事件类（继承 GameEventArgs）
- 核心玩法系统（战斗、背包、技能、任务等）

扫描方法：

grep -rn ": ProcedureBase" Assets/ --include="*.cs" -l
grep -rn ": EntityLogic\|: EntityData" Assets/ --include="*.cs" -l
grep -rn ": FsmState" Assets/ --include="*.cs" -l
grep -rn ": GameEventArgs" Assets/ --include="*.cs" -l
grep -rn ": MonoBehaviour" Assets/ --include="*.cs" -l | grep -v "UI\|Form\|Dialog\|Widget\|Editor"

逐个读取文件，提取类的继承关系、职责、关键方法、与其他类的调用关系。

输出格式要求（Markdown）：

# Logic 层继承链

## Procedure 流程
（完整流程树 + 流程切换关系图）

## Entity 继承树
（角色、怪物、NPC、道具等实体的继承关系）

## 状态机
（所有 Fsm 状态定义和转换关系）

## 事件定义
（所有自定义事件类、触发时机）

## 核心系统
（每个系统：文件路径、职责、关键方法、依赖关系）

注意：
- 重点梳理类之间的调用关系，不只是继承
- 标注每个核心系统的职责边界
- 如果发现 God Class（职责过多的大类），醒目标注
- 结果保存到 Assets/AIDoc/scan-results/scan-result-2-logic.md
```
