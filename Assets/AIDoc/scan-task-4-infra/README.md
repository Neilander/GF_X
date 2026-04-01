# Scan Task 4: 框架 & 基础设施层扫描

## 扫描范围
扫描项目中所有**框架层和基础设施**代码：Manager/Singleton 体系、接口定义、抽象基类、GF 扩展、工具类、第三方依赖。

## 操作步骤

1. 在 Claude Code 中打开 GF_X 项目根目录
2. 粘贴下面的 prompt 执行
3. 结果保存到 `Assets/AIDoc/scan-results/scan-result-4-infra.md`

## Prompt

```
请扫描本项目的框架层和基础设施代码，生成继承链文档。

扫描目标：
- 所有 Manager / Singleton 类
- 所有接口（interface）定义
- 所有抽象类（abstract class）
- GF 框架扩展（继承 GameFrameworkComponent 的类）
- 工具类（Util / Helper / Extension）
- GameEntry 注册的所有组件
- 第三方库依赖

扫描方法：

grep -rn ": GameFrameworkComponent" Assets/ --include="*.cs" -l
grep -rn "Singleton\|Manager :" Assets/ --include="*.cs" -l
grep -rn "public interface\|internal interface" Assets/ --include="*.cs" -l
grep -rn "abstract class" Assets/ --include="*.cs" -l
grep -rn "static class" Assets/ --include="*.cs" -l
find Assets/ -name "*Util*" -o -name "*Helper*" -o -name "*Extension*" -o -name "*Tool*" | grep ".cs$"
find Assets/ -name "GameEntry.cs" 2>/dev/null

逐个读取文件，提取类的继承关系、职责、关键方法签名、依赖关系。
特别注意：找到 GameEntry.cs 后仔细阅读它注册了哪些 GF Component。

输出格式要求（Markdown）：

# 框架 & 基础设施层

## GF 组件扩展
（继承 GameFrameworkComponent 的所有自定义组件）

## GameEntry 注册清单
（GameEntry 中注册的所有 Component，及其获取方式）

## Manager / Singleton 体系
（完整继承树 + 每个 Manager 的职责）

## 接口清单
| 接口名 | 文件路径 | 方法签名 | 说明 |

## 抽象基类树
（所有 abstract class 的完整继承树）

## 工具类 & 扩展方法
| 类名 | 类型 | 说明 |

## 第三方依赖
| 库名 | 用途 |

注意：
- 这一层是项目的地基，要特别仔细
- 所有 abstract class 必须列出完整的继承树
- 接口要列出所有方法签名（这是后续开发的约束）
- 泛型基类要说明类型参数的含义
- 结果保存到 Assets/AIDoc/scan-results/scan-result-4-infra.md
```
