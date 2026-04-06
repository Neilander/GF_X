# DataTable 生成错误修复指南

## 错误信息

```
GameFrameworkException: Name row '1' >= raw row count '1' is not allow.
```

## 问题原因

这个错误表示某个 DataTable Excel 文件的格式不正确，具体原因是：

1. **文件只有一行数据**：Excel 文件中只有标题行，没有数据行
2. **文件被 Excel 打开**：Excel 正在编辑该文件，导致临时锁定
3. **文件格式损坏**：Excel 文件内部结构有问题

## 检测到的问题文件

根据系统检查，以下文件可能有问题：

1. `GF_X/AAAGameData/DataTables/Build/DeviceTable.xlsx` - 有临时锁定文件
2. `GF_X/AAAGameData/DataTables/Tech/TechNodeTable.xlsx` - 有临时锁定文件

## 解决方案

### 方案 1: 关闭 Excel 文件（推荐）

1. **关闭所有打开的 Excel 文件**
2. **删除临时锁定文件**：
   ```
   GF_X/AAAGameData/DataTables/Build/~$DeviceTable.xlsx
   GF_X/AAAGameData/DataTables/Tech/~$TechNodeTable.xlsx
   ```
3. **重新生成 DataTable**：
   - Unity 菜单：`AAAGame → Generate DataTables`

### 方案 2: 检查文件格式

如果关闭 Excel 后问题仍然存在，需要检查文件格式：

#### 正确的 DataTable Excel 格式

DataTable Excel 文件应该至少包含以下行：

```
行 1: 字段名称（Name Row）
行 2: 字段类型（Type Row）
行 3: 注释（Comment Row，可选）
行 4+: 数据行（Content Rows）
```

**示例**：

| Id | Name | Value |
|----|------|-------|
| int | string | float |
| ID | 名称 | 数值 |
| 1 | Test | 100.0 |
| 2 | Test2 | 200.0 |

#### 检查步骤

1. **打开问题文件**：
   - `DeviceTable.xlsx`
   - `TechNodeTable.xlsx`

2. **确认至少有 4 行**：
   - 第 1 行：字段名
   - 第 2 行：字段类型
   - 第 3 行：注释（可选）
   - 第 4 行：至少一条数据

3. **如果只有标题行**：
   - 添加一行示例数据
   - 或者删除该文件（如果不需要）

### 方案 3: 临时禁用问题文件

如果某个 DataTable 文件暂时不需要，可以：

1. **重命名文件**：
   ```
   DeviceTable.xlsx → DeviceTable.xlsx.bak
   ```

2. **或移动到其他目录**：
   ```
   移动到 GF_X/AAAGameData/DataTables/_Backup/
   ```

3. **重新生成 DataTable**

### 方案 4: 使用 PowerShell 脚本自动修复

创建一个 PowerShell 脚本来删除所有临时锁定文件：

```powershell
# 删除所有 Excel 临时锁定文件
Get-ChildItem -Path "GF_X/AAAGameData/DataTables" -Filter "~$*.xlsx" -Recurse | Remove-Item -Force

Write-Host "已删除所有 Excel 临时锁定文件"
```

保存为 `CleanExcelLocks.ps1`，然后在 PowerShell 中运行：

```powershell
cd GF_X
.\CleanExcelLocks.ps1
```

## 快速修复步骤

### 步骤 1: 关闭 Excel

1. 关闭所有 Excel 窗口
2. 检查任务管理器，确保没有 Excel 进程

### 步骤 2: 删除临时文件

在 PowerShell 中执行：

```powershell
cd GF_X
Remove-Item "AAAGameData/DataTables/Build/~$DeviceTable.xlsx" -Force -ErrorAction SilentlyContinue
Remove-Item "AAAGameData/DataTables/Tech/~$TechNodeTable.xlsx" -Force -ErrorAction SilentlyContinue
```

### 步骤 3: 重新生成

1. 返回 Unity
2. 菜单：`AAAGame → Generate DataTables`
3. 等待完成

## 验证修复

生成成功后，应该看到：

```
✓ DataTable 生成成功
✓ 生成了 XX 个 DataTable 文件
✓ UIViews.cs 已更新
```

## 如果问题仍然存在

### 检查具体是哪个文件

错误堆栈中应该会显示具体的文件名。查看完整的错误信息：

```
GameFramework.Editor.DataTableTools.DataTableProcessor..ctor
(System.String dataTableFileName, ...)
```

`dataTableFileName` 参数会显示问题文件的路径。

### 手动检查该文件

1. 用 Excel 打开该文件
2. 检查行数：
   - 至少需要 4 行（名称、类型、注释、数据）
   - 如果只有 1-2 行，添加示例数据
3. 保存并关闭
4. 重新生成

### 创建最小示例

如果文件格式复杂，可以创建一个最小示例：

```
| Id | Name |
|----|------|
| int | string |
| ID | 名称 |
| 1 | Test |
```

## 关于 CardUIForm 的配置

**重要提示**：在修复 DataTable 错误之前，**不要**手动编辑 `UITable.xlsx` 添加 CardUIForm。

原因：
1. 当前 DataTable 生成器有错误，无法正常工作
2. 添加 CardUIForm 后可能会导致更多错误
3. 需要先修复现有的 DataTable 问题

**正确的顺序**：

1. ✅ 修复 DataTable 生成错误（本指南）
2. ✅ 确认 DataTable 可以正常生成
3. ✅ 然后再添加 CardUIForm 到 UITable.xlsx
4. ✅ 最后生成 DataTable 以更新 UIViews.cs

## 常见问题

### Q1: 为什么会有临时锁定文件？

**A**: Excel 打开文件时会创建 `~$` 开头的临时文件。正常情况下关闭 Excel 会自动删除，但有时会残留。

### Q2: 可以直接删除 DeviceTable.xlsx 吗？

**A**: 如果你的项目不需要这个表，可以删除。但建议先备份。

### Q3: 生成 DataTable 需要多长时间？

**A**: 通常 10-30 秒，取决于 DataTable 文件的数量和大小。

### Q4: 生成失败会影响现有代码吗？

**A**: 不会。生成失败只是不会更新 DataTable 相关的 C# 代码，现有代码不受影响。

## 技术细节

### DataTable 生成器配置

GF_X 的 DataTable 生成器配置：

```csharp
nameRow = 1        // 第 1 行是字段名
typeRow = 2        // 第 2 行是字段类型
commentRow = 3     // 第 3 行是注释（可选）
contentStartRow = 4 // 第 4 行开始是数据
```

### 错误检查逻辑

```csharp
if (nameRow >= rawRowCount)
{
    throw new GameFrameworkException(
        $"Name row '{nameRow}' >= raw row count '{rawRowCount}' is not allow.");
}
```

这意味着如果文件只有 1 行，而 `nameRow = 1`（第 2 行，0-based），就会触发错误。

## 总结

1. **立即操作**：关闭所有 Excel 文件
2. **删除临时文件**：删除 `~$` 开头的文件
3. **重新生成**：Unity 菜单 → Generate DataTables
4. **验证成功**：检查 Console 没有错误
5. **然后配置 CardUIForm**：按照快速配置指南操作

---

**创建时间**: 2025-03-31
**问题**: DataTable 生成错误
**状态**: 等待用户修复
