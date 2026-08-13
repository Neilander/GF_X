# 小地图系统 - Procedure 集成指南

## 概述

小地图系统现已完全集成到 GF_X 框架的 Procedure 流程中，使用与卡牌系统和战争迷雾系统相同的加载模式。

## 系统架构

### 核心组件

1. **MinimapManager** (GameFrameworkComponent)
   - 位置：GameEntry 对象上
   - 功能：管理小地图的核心逻辑
   - 继承：`GameFrameworkComponent`

2. **MinimapSetup** (GameFrameworkComponent)
   - 位置：`GF_X/Assets/AAAGame/Scripts/UTManagers/MinimapSetup.cs`
   - 功能：初始化、更新和关闭小地图系统
   - 类似于：`CardSetup`、`FogOfWarSetup`

3. **MinimapGameProcedure** (ProcedureBase)
   - 位置：`GF_X/Assets/AAAGame/Scripts/Procedures/MinimapGameProcedure.cs`
   - 功能：游戏流程控制
   - 类似于：`CardGameProcedure`、`FogOfWarGameProcedure`

4. **MinimapUI** (UIFormBase)
   - 位置：`GF_X/Assets/AAAGame/Scripts/MiniMap/MinimapUI.cs`
   - 功能：小地图 UI 界面
   - 继承：`UIFormBase`

5. **CompleteGameProcedure** (ProcedureBase)
   - 位置：`GF_X/Assets/AAAGame/Scripts/Procedures/CompleteGameProcedure.cs`
   - 功能：同时加载卡牌、小地图和战争迷雾系统
   - 推荐使用

## 快速开始

### 步骤 1：添加 MinimapSetup 组件

在 `GameEntry` 对象上添加 `MinimapSetup` 组件（如果还没有）。

### 步骤 2：确保 MinimapManager 存在

确保 `GameEntry` 对象上已经挂载了 `MinimapManager` 组件。

### 步骤 3：配置 Procedure

**方式 A：使用 MinimapGameProcedure（仅小地图）**

```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "MinimapGameProcedure"
  ]
}
```

**方式 B：使用 CompleteGameProcedure（推荐）**

```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "CompleteGameProcedure"
  ]
}
```

**方式 C：在现有 Procedure 中手动调用**

```csharp
// 在任何 Procedure 的 OnEnter 中
GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();
```

### 步骤 4：确认 UI 预制体

确保小地图 UI 预制体已存在：
- 路径：`Assets/AAAGame/Scripts/MiniMap/MinimapUI.prefab`
- 或者：`Assets/AAAGame/Prefabs/UI/MinimapUI.prefab`

### 步骤 5：测试运行

运行游戏，检查控制台日志：
```
[Minimap] 小地图系统初始化完成
[Minimap] 小地图 UI 已打开
[MinimapManager] MinimapManager initialized with event system
[MinimapUI] MinimapUI initialized
```

## 使用方式

### 方式 1：使用 MinimapGameProcedure

系统会在进入 `MinimapGameProcedure` 时自动加载：

```csharp
// 在 OnEnter 中自动调用：
GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();
```

### 方式 2：使用 CompleteGameProcedure（推荐）

同时加载卡牌、小地图和战争迷雾系统：

```csharp
protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
{
    base.OnEnter(procedureOwner);
    
    // 初始化通用系统
    GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup();

    // 初始化卡牌系统
    GameEntry.GetComponent<CardSetup>().CardSystemSetup();
    GameEntry.GetComponent<CardSetup>().OpenCardUI();

    // 初始化小地图系统
    GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
    GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();

    // 初始化战争迷雾系统
    GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();
    GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();
}
```

### 方式 3：手动加载

在任何 Procedure 或脚本中手动加载：

```csharp
// 初始化系统
GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();

// 打开 UI
GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();
```

## 单位注册

### 使用 MinimapReportComponent

在单位对象上挂载 `MinimapReportComponent`：

```csharp
// 在单位的 Entity 逻辑类中
protected override void OnShow(object userData)
{
    base.OnShow(userData);
    
    // 获取或添加组件
    var reportComponent = GetComponent<MinimapReportComponent>();
    if (reportComponent == null)
    {
        reportComponent = gameObject.AddComponent<MinimapReportComponent>();
    }
    
    // 初始化（士兵）
    reportComponent.Initialize(SideType.PlayerSide);
    
    // 或者初始化（建筑）
    // reportComponent.Initialize(SideType.PlayerSide, MinimapUnitType.Building, "BuildingIcon_XXX");
}

protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
{
    base.OnUpdate(elapseSeconds, realElapseSeconds);
    
    // 更新位置
    var reportComponent = GetComponent<MinimapReportComponent>();
    if (reportComponent != null)
    {
        reportComponent.Tick();
    }
}
```

### 手动注册

```csharp
// 获取管理器
var minimapManager = GameEntry.GetComponent<MinimapSetup>().GetMinimapManager();

// 注册单位（士兵）
int unitId = minimapManager.RegisterUnit(
    worldPosition: transform.position,
    side: SideType.PlayerSide,
    unitType: MinimapUnitType.Soldier
);

// 注册单位（建筑）
int buildingId = minimapManager.RegisterUnit(
    worldPosition: transform.position,
    side: SideType.PlayerSide,
    unitType: MinimapUnitType.Building,
    iconPrefabName: "BuildingIcon_XXX"
);

// 更新单位位置
minimapManager.UpdateUnit(unitId, newPosition, SideType.PlayerSide);

// 注销单位
minimapManager.UnregisterUnit(unitId);
```

## API 参考

### MinimapSetup

```csharp
// 初始化系统
public void MinimapSystemSetup()

// 更新系统（通常由 Procedure 调用）
public void MinimapSystemUpdate()

// 关闭系统
public void MinimapSystemShutdown()

// 打开 UI
public void OpenMinimapUI()

// 获取管理器
public MinimapManager GetMinimapManager()
```

### MinimapManager

```csharp
// 注册单位
public int RegisterUnit(Vector3 worldPosition, SideType side, MinimapUnitType unitType, string iconPrefabName = null)

// 更新单位
public void UpdateUnit(int unitId, Vector3 worldPosition, SideType side)

// 注销单位
public void UnregisterUnit(int unitId)

// 获取单位数量
public int GetUnitCount()
```

### MinimapReportComponent

```csharp
// 初始化（士兵）
public void Initialize(SideType unitSide)

// 初始化（完整参数）
public void Initialize(SideType unitSide, MinimapUnitType unitType, string iconPrefabName = null)

// 更新位置（在 Entity 的 OnUpdate 中调用）
public void Tick()
```

## 配置参数

### MinimapConfig

```csharp
// 地图范围
World Min X: -50
World Max X: 50
World Min Z: -50
World Max Z: 50

// 士兵显示
Player Soldier Color: (0.2, 0.5, 1) - 蓝色
Enemy Soldier Color: (1, 0, 0) - 红色
Soldier Dot Size: 5

// 建筑显示
Building Icon Size: 15

// 战争迷雾（可选）
Enable Fog Of War: false
Fog Grid Size: 50
Vision Radius: 5
```

## 系统集成

### 与战争迷雾集成

小地图可以与战争迷雾系统集成，只显示可见区域的单位：

```csharp
// 在 CompleteGameProcedure 中自动集成
// 或者手动集成：

// 1. 初始化两个系统
GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();

// 2. 打开 UI
GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();
GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();

// 3. 系统会自动协同工作
```

### 与卡牌系统集成

小地图可以显示卡牌生成的单位：

```csharp
// 在 CompleteGameProcedure 中自动集成
// 卡牌生成的单位会自动显示在小地图上（如果挂载了 MinimapReportComponent）
```

## 摄像机视野框

小地图支持显示摄像机视野框：

### 使用 MinimapCameraFrame 组件（推荐）

```csharp
// 在 MinimapUI 预制体中：
// 1. 添加一个 Image 对象作为视野框
// 2. 挂载 MinimapCameraFrame 组件
// 3. 配置边框颜色和宽度
```

### 使用 RectTransform（旧方式）

```csharp
// 在 MinimapUI 预制体中：
// 1. 添加一个 RectTransform 对象
// 2. 将其拖到 MinimapUI 的 Camera View Frame 字段
```

## 常见问题

### Q: UI 无法显示？

**检查清单**：
- [ ] GameEntry 上是否有 MinimapManager 组件？
- [ ] GameEntry 上是否有 MinimapSetup 组件？
- [ ] UI 预制体是否存在？
- [ ] UIViews.cs 中是否有 MinimapUI 枚举？
- [ ] 是否调用了 OpenMinimapUI()？

**解决方案**：
```csharp
// 检查管理器
var minimapManager = GameEntry.GetComponent<MinimapManager>();
Debug.Log($"MinimapManager 存在: {minimapManager != null}");

var minimapSetup = GameEntry.GetComponent<MinimapSetup>();
Debug.Log($"MinimapSetup 存在: {minimapSetup != null}");
```

### Q: 单位不显示？

**检查清单**：
- [ ] 单位是否挂载了 MinimapReportComponent？
- [ ] 是否调用了 Initialize()？
- [ ] 是否在 OnUpdate 中调用了 Tick()？
- [ ] MinimapManager 是否正常工作？

**解决方案**：
```csharp
// 检查单位数量
var minimapManager = GameEntry.GetComponent<MinimapSetup>().GetMinimapManager();
int count = minimapManager.GetUnitCount();
Debug.Log($"当前单位数量: {count}");
```

### Q: 摄像机视野框不显示？

**检查清单**：
- [ ] 是否配置了 Camera View Frame 或 MinimapCameraFrame？
- [ ] Main Camera 是否存在？
- [ ] 摄像机是否在场景中激活？

**解决方案**：
```csharp
// 检查摄像机
Camera mainCamera = Camera.main;
Debug.Log($"Main Camera 存在: {mainCamera != null}");
```

## 性能优化

### 调整更新频率

```csharp
// MinimapManager 在 LateUpdate 中广播事件
// 如果需要降低频率，可以修改 MinimapManager.cs：

private float updateTimer = 0f;
private float updateInterval = 0.1f; // 每 0.1 秒更新一次

private void LateUpdate()
{
    updateTimer += Time.deltaTime;
    if (updateTimer >= updateInterval)
    {
        updateTimer = 0f;
        // 广播事件
    }
}
```

### 减少单位数量

```csharp
// 只注册重要单位
// 例如：只注册玩家单位和重要建筑
```

## 文件清单

### 核心文件
- `GF_X/Assets/AAAGame/Scripts/MiniMap/MinimapManager.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/MinimapUI.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/MinimapData.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/MinimapEvents.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/MinimapReportComponent.cs`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/MinimapCameraFrame.cs`

### 集成文件
- `GF_X/Assets/AAAGame/Scripts/UTManagers/MinimapSetup.cs` ← 新增
- `GF_X/Assets/AAAGame/Scripts/Procedures/MinimapGameProcedure.cs` ← 新增
- `GF_X/Assets/AAAGame/Scripts/Procedures/CompleteGameProcedure.cs` ← 新增

## 下一步

1. 确认 MinimapManager 和 MinimapSetup 组件已添加
2. 配置 Procedure 流程
3. 在单位上添加 MinimapReportComponent
4. 测试运行

## 技术支持

如有问题，请参考：
- `小地图系统实现指南.md` - 技术细节
- `使用示例.md` - 使用示例
- `摄像机视野框配置指南.md` - 视野框配置
