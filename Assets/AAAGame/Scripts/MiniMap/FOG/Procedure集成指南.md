# 战争迷雾系统 - Procedure 集成指南

## 概述

战争迷雾系统现已完全集成到 GF_X 框架的 Procedure 流程中，使用与卡牌系统相同的加载模式。

## 系统架构

### 核心组件

1. **FogOfWarManager** (GameFrameworkComponent)
   - 位置：场景中的独立空对象
   - 功能：管理战争迷雾的核心逻辑
   - 继承：`GameFrameworkComponent`

2. **FogOfWarSetup** (GameFrameworkComponent)
   - 位置：`GF_X/Assets/AAAGame/Scripts/UTManagers/FogOfWarSetup.cs`
   - 功能：初始化、更新和关闭战争迷雾系统
   - 类似于：`CardSetup`

3. **FogOfWarGameProcedure** (ProcedureBase)
   - 位置：`GF_X/Assets/AAAGame/Scripts/Procedures/FogOfWarGameProcedure.cs`
   - 功能：游戏流程控制
   - 类似于：`CardGameProcedure`

4. **FogOfWarUIForm** (UIFormBase)
   - 位置：`GF_X/Assets/AAAGame/Scripts/UI/FogOfWarUIForm.cs`
   - 功能：战争迷雾 UI 界面
   - 继承：`UIFormBase`

## 快速开始

### 步骤 1：场景配置

1. 在 Game 场景中创建空对象，命名为 `FogOfWarManager`
2. 挂载 `FogOfWarManager` 组件
3. 配置参数：
   ```
   Grid Width: 100
   Grid Height: 100
   World Bounds: 根据场景大小设置
   Update Interval: 0.1
   ```

### 步骤 2：添加 FogOfWarSetup 组件

在 GameEntry 对象上添加 `FogOfWarSetup` 组件（如果还没有）。

### 步骤 3：配置 Procedure

在项目配置中，将 `FogOfWarGameProcedure` 添加到 Procedure 列表：

```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "FogOfWarGameProcedure"  // 添加这一行
  ]
}
```

### 步骤 4：创建 UI 预制体

1. 在 Unity 中创建 UI 预制体：
   - 名称：`FogOfWarUI`
   - 路径：`Assets/AAAGame/Prefabs/UI/FogOfWarUI.prefab`

2. 预制体结构：
   ```
   FogOfWarUI (Canvas)
   └── FogRawImage (RawImage)
       - Anchor: Stretch (全屏)
       - Color: White
   ```

3. 挂载脚本：
   - 在根对象上挂载 `FogOfWarUIForm` 脚本
   - 将 `FogRawImage` 拖到 `Fog Raw Image` 字段

### 步骤 5：配置 UIViews

`UIViews.cs` 已自动更新，包含 `FogOfWarUI = 18`。

## 使用方式

### 方式 1：使用 FogOfWarGameProcedure（推荐）

系统会在进入 `FogOfWarGameProcedure` 时自动加载：

```csharp
// 在 OnEnter 中自动调用：
GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();
GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();
```

### 方式 2：手动加载

在任何 Procedure 或脚本中手动加载：

```csharp
// 初始化系统
GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();

// 打开 UI
GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();
```

### 方式 3：与卡牌系统结合

`FogOfWarGameProcedure` 已经集成了卡牌系统，可以同时使用：

```csharp
protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
{
    base.OnEnter(procedureOwner);
    
    // 初始化通用系统
    GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup();

    // 初始化卡牌系统
    GameEntry.GetComponent<CardSetup>().CardSystemSetup();
    GameEntry.GetComponent<CardSetup>().OpenCardUI();

    // 初始化战争迷雾系统
    GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();
    GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();
}
```

## 单位视野注册

### 使用 FogOfWarVisionComponent

在单位对象上挂载 `FogOfWarVisionComponent`：

```csharp
// 自动注册和更新
public class FogOfWarVisionComponent : MonoBehaviour
{
    [SerializeField] private int playerMask = 1;
    [SerializeField] private float visionRange = 10f;
    
    // 组件会自动处理注册和注销
}
```

### 手动注册

```csharp
// 获取管理器
var fogManager = GameEntry.GetComponent<FogOfWarSetup>().GetFogOfWarManager();

// 注册视野
int visionId = fogManager.RegisterVision(
    playerMask: 1,           // 玩家0
    visionRange: 10f,        // 视野范围
    worldPosition: transform.position,
    terrainHeight: 0
);

// 更新视野
fogManager.UpdateVision(visionId, newPosition, terrainHeight);

// 注销视野
fogManager.UnregisterVision(visionId);
```

## 单位可见性控制

使用 `FogOfWarUnitVisibility` 组件控制单位是否可见：

```csharp
public class FogOfWarUnitVisibility : MonoBehaviour
{
    [SerializeField] private int ownerPlayerMask = 2; // 单位所属玩家
    [SerializeField] private int visibleToPlayerMask = 1; // 对哪些玩家可见
    
    // 组件会自动根据迷雾状态显示/隐藏单位
}
```

## API 参考

### FogOfWarSetup

```csharp
// 初始化系统
public void FogOfWarSystemSetup()

// 更新系统（通常由 Procedure 调用）
public void FogOfWarSystemUpdate()

// 关闭系统
public void FogOfWarSystemShutdown()

// 打开 UI
public void OpenFogOfWarUI()

// 获取管理器
public FogOfWarManager GetFogOfWarManager()
```

### FogOfWarManager

```csharp
// 注册单位视野
public int RegisterVision(int playerMask, float visionRange, Vector3 worldPosition, short terrainHeight = 0)

// 更新单位视野
public void UpdateVision(int unitId, Vector3 worldPosition, short terrainHeight = 0)

// 注销单位视野
public void UnregisterVision(int unitId)

// 设置激活的玩家掩码
public void SetActivePlayerMask(int playerMask)

// 检查位置是否可见
public bool IsPositionVisible(Vector3 worldPosition, int playerMask)

// 获取迷雾状态
public FogState GetFogState(Vector3 worldPosition, int playerMask)

// 强制更新视野
public void ForceUpdateVision()

// 重置迷雾
public void ResetFog()
```

### FogOfWarUIForm

```csharp
// 强制刷新纹理
public void ForceRefresh()

// 设置玩家掩码
public void SetPlayerMask(int playerMask)
```

## 玩家掩码说明

使用位掩码支持多玩家和联盟：

```csharp
// 单个玩家
int player0 = 1;      // 0001 (玩家0)
int player1 = 2;      // 0010 (玩家1)
int player2 = 4;      // 0100 (玩家2)
int player3 = 8;      // 1000 (玩家3)

// 联盟
int alliance01 = 3;   // 0011 (玩家0和1联盟)
int alliance23 = 12;  // 1100 (玩家2和3联盟)
```

## 性能优化

1. **更新间隔**：调整 `UpdateInterval` 参数（默认 0.1 秒）
2. **网格大小**：根据场景大小调整 `GridWidth` 和 `GridHeight`
3. **视野范围**：合理设置单位的 `VisionRange`
4. **颜色过渡**：禁用 `EnableEasing` 可以提高性能

## 调试

### 启用日志

```csharp
// 在 FogOfWarManager 中查看日志
Log.Info("[FogOfWar] ...");
```

### 检查视野数量

```csharp
var fogManager = GameEntry.GetComponent<FogOfWarSetup>().GetFogOfWarManager();
int count = fogManager.GetVisionCount();
Debug.Log($"当前视野数量: {count}");
```

### 强制刷新

```csharp
// 强制更新视野
fogManager.ForceUpdateVision();

// 强制刷新 UI
var uiForm = GF.UI.GetUIForm(UIViews.FogOfWarUI) as FogOfWarUIForm;
uiForm?.ForceRefresh();
```

## 常见问题

### Q: UI 无法显示？

A: 检查以下几点：
1. 场景中是否有 `FogOfWarManager` 对象
2. UI 预制体是否正确配置
3. `UIViews.cs` 是否包含 `FogOfWarUI`
4. 是否调用了 `OpenFogOfWarUI()`

### Q: 视野不更新？

A: 检查以下几点：
1. 单位是否挂载了 `FogOfWarVisionComponent`
2. `UpdateInterval` 是否设置正确
3. 玩家掩码是否匹配

### Q: 编译错误？

A: 确保以下文件存在：
- `FogOfWarSetup.cs`
- `FogOfWarGameProcedure.cs`
- `FogOfWarUIForm.cs` (在 UI 目录)
- `UIViews.cs` 包含 `FogOfWarUI`

## 文件清单

### 核心文件
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarData.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarGrid.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarManager.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarVisionComponent.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarUnitVisibility.cs`

### 集成文件
- `GF_X/Assets/AAAGame/Scripts/UTManagers/FogOfWarSetup.cs`
- `GF_X/Assets/AAAGame/Scripts/Procedures/FogOfWarGameProcedure.cs`
- `GF_X/Assets/AAAGame/Scripts/UI/FogOfWarUIForm.cs`
- `GF_X/Assets/AAAGame/Scripts/UI/Core/UIViews.cs` (已更新)

### 可选文件
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarRenderer.cs` (MonoBehaviour 渲染器)
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/MinimapFogIntegration.cs` (小地图集成)

## 下一步

1. 创建 UI 预制体
2. 配置 Procedure 流程
3. 在单位上添加 `FogOfWarVisionComponent`
4. 测试运行

## 技术支持

如有问题，请参考：
- `技术实现说明.md` - 技术细节
- `快速参考.md` - API 快速参考
- `README.md` - 系统概述
