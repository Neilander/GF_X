# Merge Task: 合并 4 份扫描结果

## 前置条件
确认以下 4 个文件都已生成：
- `Assets/AIDoc/scan-results/scan-result-1-data.md`
- `Assets/AIDoc/scan-results/scan-result-2-logic.md`
- `Assets/AIDoc/scan-results/scan-result-3-ui.md`
- `Assets/AIDoc/scan-results/scan-result-4-infra.md`

## Prompt

```
请读取以下 4 份扫描结果，合并为一份完整的项目继承链文档：

- Assets/AIDoc/scan-results/scan-result-1-data.md
- Assets/AIDoc/scan-results/scan-result-2-logic.md
- Assets/AIDoc/scan-results/scan-result-3-ui.md
- Assets/AIDoc/scan-results/scan-result-4-infra.md

合并规则：

1. 去重：同一个类在多份报告中出现时，合并信息，保留最完整的描述
2. 构建完整继承树：把 4 份碎片拼成统一的继承树
3. 标注层间依赖：分析 Data / Logic / UI / Infra 四层之间的引用关系
4. 标注架构风险：如果有违反分层原则的依赖（如 Data 引用了 UI），醒目标注

输出格式：

# GF_X 项目完整继承链文档

> 由 4 个 Scanner 并行扫描后合并生成

## 总览
- C# 文件总数 / 类总数 / 接口总数 / 枚举总数

## 层级架构图
（四层架构总览）

## 完整继承树
### ScriptableObject 继承树
### MonoBehaviour 继承树
### 接口清单
### 枚举清单

## 层间依赖分析
### 依赖矩阵
（Data/Logic/UI/Infra 的依赖表格）

### 架构风险
（不合理的跨层依赖，醒目标注）

## 各层详细信息
（按 Infra → Data → Logic → UI 顺序展开）

保存到 Assets/AIDoc/scan-results/inheritance-chain-final.md
```
