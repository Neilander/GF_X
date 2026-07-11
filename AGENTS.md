# 必读规则

- 遇到问题先找根因，不要做表层补偿。不要只看逻辑无实证猜原因，要加完整日志作实际验证，确保锁定根因。
- 实现保持极简，避免大段防御性兜底逻辑。
- 运行链路遇到空值等非正常情况时要明确报错，不要静默跳过或加 fallback。
- 功能落到该在的脚本，不做跨域实现。
- 涉及中文、注释、日志、UI 文本或任何非 ASCII 内容的文件，编辑前必须确认编码、BOM、换行风格；编辑后必须读回目标片段验证中文无乱码。
- 新文件编译 csproj 可能会被 Unity 卡住，不要因此逃避创建新文件、改文件名或把新文件合进老文件。
- 多步任务不中途停顿，持续推进到实现、验证和结果说明均完成。

# 破坏性操作与测试沙箱

- 永久禁止使用 junction、symbolic link、hard link 或其他重解析点，把测试目录、临时项目或恢复目录连接到真实工作区、`Assets`、`Packages`、`ProjectSettings` 或其任何子目录。
- 永久禁止对工作区、`Assets`、`Packages`、`ProjectSettings`、它们的祖先目录或来源不明的计算路径执行递归删除、递归移动、镜像同步或清空操作。
- 禁止直接使用 `Remove-Item -Recurse`、`Directory.Delete(path, true)`、`AssetDatabase.DeleteAsset("Assets")`、`robocopy /MIR` 等方式清理测试目录。不得通过其他 shell 间接执行删除。
- 文件系统测试沙箱必须位于工作区之外，使用 `Tools/Safety/New-TestSandbox.ps1` 创建，并使用 `Tools/Safety/SafeRemove-TestSandbox.ps1` 清理。没有路径绑定 sentinel 的目录一律不得删除。
- 清理前必须规范化并打印绝对路径，验证目标严格位于允许根目录的子目录内，且与工作区及受保护目录完全不重叠。
- 清理前必须逐层检查目标及其全部子项。发现 junction、symbolic link 或任何 `ReparsePoint` 时立即报错并停止，不能跟随、跳过或 fallback。
- 安全清理必须逐文件、逐目录自底向上执行，每次删除前重新验证路径和 `ReparsePoint`；不得使用递归删除，以避免检查后路径被替换造成穿透。
- Unity 测试若必须在 `Assets` 下生成临时资源，只能使用固定、明确、最小化的测试专用路径，并逐个删除已知资源；禁止删除其父目录，更禁止把临时项目的 `Assets` 指向真实 `Assets`。
- 任何高风险文件操作前，必须记录 `git status`，并把未提交修改和关键生成资源快照到其他磁盘。操作后必须核对文件数、零字节文件、GUID、meta 配对和内容级 git diff。
- 对删除目标、解析结果或所有权有任何不确定时，停止删除并明确报错。不得为了让测试继续而放宽检查。

# 项目注意事项

- 始终由 launch 场景进入，通过 procedure 加载需要的场景，加载的场景与 launch 场景同时存在。
- 自动生成代码（文件开头有注释说明，尤其 DataTable 生成的 cs 和 UI 生成的 Variables）不手改，优先在非生成层做适配。
- 核心战斗数值使用 Fix64，float 仅用于 Unity API/UI 边界。
- 事件使用 GF.EventArgs。
