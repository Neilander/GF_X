# Scan Task 1: Data 层扫描

## 扫描范围
扫描项目中所有**数据定义**相关的代码：ScriptableObject、DataTable/DataRow、配置数据、枚举。

## 操作步骤

1. 在 Claude Code 中打开 GF_X 项目根目录
2. 粘贴下面的 prompt 执行
3. 结果保存到 `Assets/AIDoc/scan-results/scan-result-1-data.md`

## Prompt

```
请扫描本项目的 Data 层代码，生成继承链文档。

扫描目标：
- 所有继承 ScriptableObject 的类
- 所有继承 DataRowBase / 实现 IDataRow 的类
- 所有枚举（enum）定义
- 所有纯数据类（DTO / POCO）

扫描方法（先执行这些命令定位文件）：

grep -rn ": ScriptableObject" Assets/ --include="*.cs" -l
grep -rn ": DataRowBase\|IDataRow" Assets/ --include="*.cs" -l
grep -rn "public enum\|internal enum" Assets/ --include="*.cs" -l

然后逐个读取找到的文件，提取：
- 类名、继承的基类、实现的接口
- 命名空间
- 关键字段（[SerializeField] 或 public 字段）
- 文件路径

输出格式要求（Markdown）：

# Data 层继承链

## ScriptableObject 继承树
（树状图展示完整继承关系）

## DataTable / DataRow 继承树
（列出所有 DR 类及其字段）

## 枚举清单
（枚举名、值列表、文件路径）

## 数据类详细信息
（每个类的完整信息：文件路径、命名空间、继承、接口、字段）

注意：
- 只关注数据定义，不分析逻辑代码
- GF 框架自带的基类只列类名，不展开内部
- 如果发现跨层的类（既是数据又有大量逻辑），标注"跨层"
- 结果保存到 Assets/AIDoc/scan-results/scan-result-1-data.md
```
