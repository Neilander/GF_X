# Scan Task 3: UI 层扫描

## 扫描范围
扫描项目中所有 **UI** 相关的代码：UIForm、Dialog、Widget、HUD 等界面组件。

## 操作步骤

1. 在 Claude Code 中打开 GF_X 项目根目录
2. 粘贴下面的 prompt 执行
3. 结果保存到 `Assets/AIDoc/scan-results/scan-result-3-ui.md`

## Prompt

```
请扫描本项目的 UI 层代码，生成继承链文档。

扫描目标：
- 所有继承 UIFormLogic / UGuiForm 的界面类
- 所有 Dialog / Panel / Widget / Window / HUD 类
- 自定义的 UI 基类和公共组件

扫描方法：

grep -rn ": UIFormLogic\|: UGuiForm\|: UIForm" Assets/ --include="*.cs" -l
grep -rn "class.*Dialog\|class.*Panel\|class.*Widget\|class.*Window\|class.*HUD" Assets/ --include="*.cs" -l
find Assets/ -path "*/UI/*.cs" -o -path "*/UIForm/*.cs" 2>/dev/null

逐个读取文件，提取类的继承关系、所属 UIGroup、功能描述、引用的数据类。

输出格式要求（Markdown）：

# UI 层继承链

## UIForm 继承树
（完整的 UI 界面继承关系树状图）

## UIGroup 分组表
| UIGroup | 包含的 Form | 说明 |
（列出所有界面的分组归属）

## 自定义 UI 组件
（可复用的 Widget 组件列表及继承关系）

## UI 与逻辑层交互模式
（观察到的交互方式：事件驱动？直接引用？MVC？）

## 界面清单
（每个 Form：文件路径、UIGroup、功能描述、引用的数据类）

注意：
- 关注 UI 的层级关系（谁包含谁、谁打开谁）
- 标注每个 Form 的 UIGroup 归属
- 如果有自定义 UI 基类，详细说明封装了什么
- 结果保存到 Assets/AIDoc/scan-results/scan-result-3-ui.md
```
