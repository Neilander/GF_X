# 小地图系统 - 快速开始（Procedure 版）

## 🎯 3 分钟快速配置

### 步骤 1：添加组件（30 秒）

在 `GameEntry` 对象上确认以下组件：
- [x] `MinimapManager` 组件（应该已存在）
- [ ] `MinimapSetup` 组件（新增）

### 步骤 2：配置 Procedure（1 分钟）

**方式 A：使用 CompleteGameProcedure（推荐）**

在项目配置文件中：

```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "CompleteGameProcedure"
  ]
}
```

这将同时加载：
- ✅ 卡牌系统
- ✅ 小地图系统
- ✅ 战争迷雾系统

**方式 B：仅使用小地图**

```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "MinimapGameProcedure"
  ]
}
```

**方式 C：在现有 Procedure 中调用**

```csharp
// 在任何 Procedure 的 OnEnter 中
GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();
```

### 步骤 3：测试运行（30 秒）

1. 运行游戏
2. 检查控制台日志：
   ```
   [Minimap] 小地图系统初始化完成
   [Minimap] 小地图 UI 已打开
   [MinimapManager] MinimapManager initialized with event system
   [MinimapUI] MinimapUI initialized
   ```
3. 检查屏幕上是否显示小地图

### 步骤 4：添加单位显示（1 分钟）

在单位的 Entity 逻辑类中：

```csharp
using AAAGame.MiniMap;

public class SoldierEntity : EntityLogic
{
    private MinimapReportComponent m_MinimapReport;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        // 添加小地图报告组件
        m_MinimapReport = gameObject.GetComponent<MinimapReportComponent>();
        if (m_MinimapReport == null)
        {
            m_MinimapReport = gameObject.AddComponent<MinimapReportComponent>();
        }
        
        // 初始化（士兵）
        m_MinimapReport.Initialize(SideType.PlayerSide);
    }
    
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        
        // 更新小地图位置
        if (m_MinimapReport != null)
        {
            m_MinimapReport.Tick();
        }
    }
}
```

## 🎮 完整示例

### 士兵单位

```csharp
using AAAGame.MiniMap;
using UnityGameFramework.Runtime;

public class SoldierEntity : EntityLogic
{
    private MinimapReportComponent m_MinimapReport;
    private SideType m_Side = SideType.PlayerSide;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        // 从 userData 获取势力信息
        // var data = userData as SoldierData;
        // m_Side = data.Side;
        
        // 添加小地图组件
        m_MinimapReport = gameObject.GetComponent<MinimapReportComponent>();
        if (m_MinimapReport == null)
        {
            m_MinimapReport = gameObject.AddComponent<MinimapReportComponent>();
        }
        
        // 初始化为士兵
        m_MinimapReport.Initialize(m_Side);
        
        Log.Info($"[Soldier] 已注册到小地图，势力={m_Side}");
    }
    
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        
        // 每帧更新位置
        if (m_MinimapReport != null)
        {
            m_MinimapReport.Tick();
        }
    }
    
    protected override void OnHide(bool isShutdown, object userData)
    {
        base.OnHide(isShutdown, userData);
        
        // MinimapReportComponent 会在 OnDestroy 时自动注销
        Log.Info("[Soldier] 从小地图注销");
    }
}
```

### 建筑单位

```csharp
using AAAGame.MiniMap;
using UnityGameFramework.Runtime;

public class BuildingEntity : EntityLogic
{
    private MinimapReportComponent m_MinimapReport;
    private SideType m_Side = SideType.PlayerSide;
    private string m_IconName = "BuildingIcon_XXX";
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        // 添加小地图组件
        m_MinimapReport = gameObject.GetComponent<MinimapReportComponent>();
        if (m_MinimapReport == null)
        {
            m_MinimapReport = gameObject.AddComponent<MinimapReportComponent>();
        }
        
        // 初始化为建筑
        m_MinimapReport.Initialize(
            unitSide: m_Side,
            unitType: MinimapUnitType.Building,
            iconPrefabName: m_IconName
        );
        
        Log.Info($"[Building] 已注册到小地图，势力={m_Side}，图标={m_IconName}");
    }
    
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        
        // 建筑通常不移动，但仍需调用 Tick
        if (m_MinimapReport != null)
        {
            m_MinimapReport.Tick();
        }
    }
}
```

## 🎨 自定义配置

### 修改颜色

在 `MinimapManager` 组件中：

```
Player Soldier Color: (0.2, 0.5, 1) - 蓝色
Enemy Soldier Color: (1, 0, 0) - 红色
Soldier Dot Size: 5
```

### 修改地图范围

```
World Min X: -50
World Max X: 50
World Min Z: -50
World Max Z: 50
```

### 添加建筑图标

1. 创建建筑图标预制体
2. 在 `MinimapUI` 组件的 `Building Icon Mappings` 中添加映射：
   - Icon Name: "BuildingIcon_XXX"
   - Prefab: 拖入预制体

## 🔧 常见问题

### Q: 小地图不显示？

**检查清单**：
- [ ] GameEntry 上是否有 MinimapManager？
- [ ] GameEntry 上是否有 MinimapSetup？
- [ ] 是否调用了 OpenMinimapUI()？
- [ ] UI 预制体是否存在？

**解决方案**：
```csharp
// 检查组件
var manager = GameEntry.GetComponent<MinimapManager>();
var setup = GameEntry.GetComponent<MinimapSetup>();
Debug.Log($"Manager: {manager != null}, Setup: {setup != null}");
```

### Q: 单位不显示？

**检查清单**：
- [ ] 单位是否挂载了 MinimapReportComponent？
- [ ] 是否调用了 Initialize()？
- [ ] 是否在 OnUpdate 中调用了 Tick()？

**解决方案**：
```csharp
// 检查单位数量
var manager = GameEntry.GetComponent<MinimapSetup>().GetMinimapManager();
int count = manager.GetUnitCount();
Debug.Log($"小地图单位数量: {count}");
```

### Q: 摄像机视野框不显示？

**检查清单**：
- [ ] MinimapUI 预制体中是否配置了 Camera View Frame？
- [ ] 是否有 Main Camera？

**解决方案**：
```csharp
// 检查摄像机
Camera mainCamera = Camera.main;
Debug.Log($"Main Camera: {mainCamera != null}");
```

## 📊 系统对比

| 特性 | 旧方式 | Procedure 版 |
|------|--------|-------------|
| 初始化 | 手动在场景中配置 | Procedure 自动加载 |
| UI 加载 | 预先放在场景中 | 运行时动态加载 |
| 系统管理 | 分散在各处 | 统一在 Setup 中 |
| 与其他系统集成 | 需要手动协调 | CompleteGameProcedure 自动集成 |
| 资源管理 | 手动清理 | Procedure 自动清理 |

## 🚀 高级功能

### 与战争迷雾集成

使用 `CompleteGameProcedure` 自动集成：

```csharp
// 自动集成，无需额外代码
// 小地图会自动根据战争迷雾显示/隐藏单位
```

### 与卡牌系统集成

```csharp
// 卡牌生成的单位自动显示在小地图上
// 只需在单位 Entity 中添加 MinimapReportComponent
```

### 手动控制单位显示

```csharp
// 获取管理器
var manager = GameEntry.GetComponent<MinimapSetup>().GetMinimapManager();

// 手动注册单位
int unitId = manager.RegisterUnit(
    worldPosition: transform.position,
    side: SideType.PlayerSide,
    unitType: MinimapUnitType.Soldier
);

// 更新位置
manager.UpdateUnit(unitId, newPosition, SideType.PlayerSide);

// 注销单位
manager.UnregisterUnit(unitId);
```

## 📚 更多文档

- `Procedure集成指南.md` - 详细的集成步骤
- `小地图系统实现指南.md` - 技术细节
- `使用示例.md` - 更多使用示例
- `摄像机视野框配置指南.md` - 视野框配置

## 🎉 完成！

现在你的小地图系统已经配置完成，可以开始使用了！

如果遇到问题，请查看控制台日志或参考上面的常见问题部分。
