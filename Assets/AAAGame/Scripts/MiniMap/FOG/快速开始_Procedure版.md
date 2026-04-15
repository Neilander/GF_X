# 战争迷雾系统 - 快速开始（Procedure 版）

## 🎯 5 分钟快速配置

### 步骤 1：场景配置（1 分钟）

1. 在 Game 场景中创建空对象
   - 名称：`FogOfWarManager`
   - 位置：场景根目录

2. 挂载组件
   - 添加 `FogOfWarManager` 组件
   - 配置参数：
     ```
     Grid Width: 100
     Grid Height: 100
     World Bounds:
       - Min: (-50, 0, -50)
       - Max: (50, 0, 50)
     Update Interval: 0.1
     ```

### 步骤 2：添加 Setup 组件（30 秒）

在 `GameEntry` 对象上添加 `FogOfWarSetup` 组件（如果还没有）。

### 步骤 3：配置 Procedure（1 分钟）

在项目配置文件中添加 `FogOfWarGameProcedure`：

```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "FogOfWarGameProcedure"
  ]
}
```

或者在现有的 Procedure 中调用：

```csharp
// 在任何 Procedure 的 OnEnter 中
GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();
GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();
```

### 步骤 4：创建 UI 预制体（2 分钟）

1. 创建 Canvas
   - 右键 → UI → Canvas
   - 名称：`FogOfWarUI`
   - Render Mode: Screen Space - Overlay

2. 添加 RawImage
   - 右键 Canvas → UI → Raw Image
   - 名称：`FogRawImage`
   - Anchor: Stretch (全屏)
   - Color: White (255, 255, 255, 255)

3. 挂载脚本
   - 在 Canvas 上添加 `FogOfWarUIForm` 组件
   - 将 `FogRawImage` 拖到 `Fog Raw Image` 字段

4. 保存预制体
   - 将 Canvas 拖到 `Assets/AAAGame/Prefabs/UI/` 目录
   - 名称：`FogOfWarUI.prefab`

### 步骤 5：测试运行（30 秒）

1. 运行游戏
2. 检查控制台日志：
   ```
   [FogOfWar] Manager initialized with grid size 100x100
   [FogOfWar] 战争迷雾系统初始化完成
   [FogOfWarUI] FogOfWarUIForm initialized
   [FogOfWarUI] FogOfWarUIForm opened and ready
   [FogOfWar] 战争迷雾 UI 已打开
   ```

## 🎮 添加单位视野

### 方式 1：使用组件（推荐）

在单位对象上添加 `FogOfWarVisionComponent`：

```csharp
// 组件会自动注册和更新视野
[SerializeField] private int playerMask = 1;      // 玩家 0
[SerializeField] private float visionRange = 10f; // 视野范围 10 米
```

### 方式 2：手动注册

```csharp
// 获取管理器
var fogManager = GameEntry.GetComponent<FogOfWarSetup>().GetFogOfWarManager();

// 注册视野
int visionId = fogManager.RegisterVision(
    playerMask: 1,
    visionRange: 10f,
    worldPosition: transform.position
);

// 在 Update 中更新位置
fogManager.UpdateVision(visionId, transform.position);

// 在销毁时注销
fogManager.UnregisterVision(visionId);
```

## 🎨 自定义颜色

在 `FogOfWarManager` 组件中配置：

```
Hidden Color: (0, 0, 0, 1)      # 黑色 - 未探索区域
Explored Color: (0.5, 0.5, 0.5, 0.5)  # 灰色半透明 - 已探索区域
Visible Color: (1, 1, 1, 0)     # 完全透明 - 可见区域
```

## 🔧 常见问题

### Q: UI 没有显示？

**检查清单**：
- [ ] 场景中是否有 `FogOfWarManager` 对象？
- [ ] `GameEntry` 上是否有 `FogOfWarSetup` 组件？
- [ ] UI 预制体是否正确创建？
- [ ] `UIViews.cs` 中是否有 `FogOfWarUI = 18`？
- [ ] 是否调用了 `OpenFogOfWarUI()`？

**解决方案**：
```csharp
// 在控制台中检查
var fogManager = FindObjectOfType<FogOfWarManager>();
Debug.Log($"FogOfWarManager 存在: {fogManager != null}");

var fogSetup = GameEntry.GetComponent<FogOfWarSetup>();
Debug.Log($"FogOfWarSetup 存在: {fogSetup != null}");
```

### Q: 视野不更新？

**检查清单**：
- [ ] 单位是否挂载了 `FogOfWarVisionComponent`？
- [ ] `playerMask` 是否正确设置？
- [ ] `visionRange` 是否大于 0？
- [ ] `UpdateInterval` 是否设置正确？

**解决方案**：
```csharp
// 强制更新视野
var fogManager = GameEntry.GetComponent<FogOfWarSetup>().GetFogOfWarManager();
fogManager.ForceUpdateVision();

// 检查视野数量
int count = fogManager.GetVisionCount();
Debug.Log($"当前视野数量: {count}");
```

### Q: 编译错误？

**错误**: `Can't add script component 'FogOfWarUIForm' because the script class cannot be found`

**原因**: 
1. 脚本文件名与类名不匹配
2. 命名空间问题
3. 编译错误

**解决方案**：
1. 检查 `FogOfWarUIForm.cs` 是否在 `Assets/AAAGame/Scripts/UI/` 目录
2. 检查类名是否为 `FogOfWarUIForm`
3. 检查是否有编译错误（查看 Console）
4. 重新导入脚本（右键 → Reimport）

## 📊 性能优化

### 调整更新频率

```csharp
// 在 FogOfWarManager 中
Update Interval: 0.1  // 每 0.1 秒更新一次（推荐）
Update Interval: 0.2  // 每 0.2 秒更新一次（性能优先）
Update Interval: 0    // 每帧更新（质量优先）
```

### 调整网格大小

```csharp
// 小场景（50x50 米）
Grid Width: 50
Grid Height: 50

// 中等场景（100x100 米）
Grid Width: 100
Grid Height: 100

// 大场景（200x200 米）
Grid Width: 200
Grid Height: 200
```

### 禁用颜色过渡

```csharp
// 在 FogOfWarManager 中
Enable Easing: false  // 禁用过渡，提高性能
```

## 🚀 高级功能

### 多玩家支持

```csharp
// 玩家 0
int player0 = 1;  // 0001

// 玩家 1
int player1 = 2;  // 0010

// 玩家 0 和 1 联盟
int alliance = 3; // 0011

// 设置当前渲染的玩家
fogManager.SetActivePlayerMask(player0);
```

### 检查位置可见性

```csharp
// 检查某个位置是否对玩家 0 可见
bool isVisible = fogManager.IsPositionVisible(targetPosition, playerMask: 1);

if (isVisible)
{
    // 显示敌人
}
else
{
    // 隐藏敌人
}
```

### 单位可见性控制

在敌人单位上添加 `FogOfWarUnitVisibility` 组件：

```csharp
[SerializeField] private int ownerPlayerMask = 2;      // 单位属于玩家 1
[SerializeField] private int visibleToPlayerMask = 1;  // 对玩家 0 可见
```

组件会自动根据迷雾状态显示/隐藏单位。

## 📚 更多文档

- `Procedure集成指南.md` - 详细的集成步骤
- `Procedure集成完成报告.md` - 完整的实现报告
- `技术实现说明.md` - 技术细节
- `快速参考.md` - API 参考

## 🎉 完成！

现在你的战争迷雾系统已经配置完成，可以开始使用了！

如果遇到问题，请查看控制台日志或参考上面的常见问题部分。
