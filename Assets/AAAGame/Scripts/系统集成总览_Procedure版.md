# GF_X 游戏系统集成总览 - Procedure 版

## 📋 概述

本项目已将三大核心系统完全集成到 GF_X 框架的 Procedure 流程中：
- ✅ 卡牌系统
- ✅ 小地图系统
- ✅ 战争迷雾系统

所有系统使用统一的加载模式，可以独立使用或组合使用。

## 🎯 系统架构

### 组件层次结构

```
GameEntry (GameObject)
├── GeneralSetup (Component)
├── CardSetup (Component)
├── MinimapSetup (Component)
├── FogOfWarSetup (Component)
├── MinimapManager (Component)
└── InputManager (Component)

Scene
└── FogOfWarManager (GameObject)
    └── FogOfWarManager (Component)
```

### Procedure 流程

```
启动流程
├── PreloadProcedure (预加载)
├── MenuProcedure (菜单)
└── 游戏流程（选择其一）
    ├── CardGameProcedure (仅卡牌)
    ├── MinimapGameProcedure (仅小地图)
    ├── FogOfWarGameProcedure (仅战争迷雾)
    └── CompleteGameProcedure (全部系统) ← 推荐
```

## 📊 系统对比

| 特性 | 卡牌系统 | 小地图系统 | 战争迷雾系统 |
|------|---------|-----------|-------------|
| **Setup 组件** | CardSetup | MinimapSetup | FogOfWarSetup |
| **Procedure** | CardGameProcedure | MinimapGameProcedure | FogOfWarGameProcedure |
| **UI Form** | CardUIForm | MinimapUI | FogOfWarUIForm |
| **UIViews 枚举** | CardUIForm = 16 | MinimapUI = 17 | FogOfWarUI = 18 |
| **管理器** | CardSystemController | MinimapManager | FogOfWarManager |
| **管理器类型** | 内部创建 | GameFrameworkComponent | GameFrameworkComponent |
| **管理器位置** | CardSetup 内部 | GameEntry 组件 | 场景独立对象 |
| **主要功能** | 卡牌抽取、放置、生成单位 | 显示单位位置、摄像机视野 | 战争迷雾、视野计算 |

## 🚀 快速开始

### 方式 1：使用 CompleteGameProcedure（推荐）

**配置文件**:
```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "CompleteGameProcedure"
  ]
}
```

**效果**:
- ✅ 自动加载卡牌系统
- ✅ 自动加载小地图系统
- ✅ 自动加载战争迷雾系统
- ✅ 系统间自动协同工作

### 方式 2：使用单独的 Procedure

**仅卡牌系统**:
```json
{
  "Procedures": ["PreloadProcedure", "MenuProcedure", "CardGameProcedure"]
}
```

**仅小地图系统**:
```json
{
  "Procedures": ["PreloadProcedure", "MenuProcedure", "MinimapGameProcedure"]
}
```

**仅战争迷雾系统**:
```json
{
  "Procedures": ["PreloadProcedure", "MenuProcedure", "FogOfWarGameProcedure"]
}
```

### 方式 3：手动组合

在自定义 Procedure 中：

```csharp
public class MyGameProcedure : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        
        // 初始化通用系统
        GameEntry.GetComponent<GeneralSetup>().GeneralSystemSetup();
        
        // 选择需要的系统
        GameEntry.GetComponent<CardSetup>().CardSystemSetup();
        GameEntry.GetComponent<CardSetup>().OpenCardUI();
        
        GameEntry.GetComponent<MinimapSetup>().MinimapSystemSetup();
        GameEntry.GetComponent<MinimapSetup>().OpenMinimapUI();
        
        GameEntry.GetComponent<FogOfWarSetup>().FogOfWarSystemSetup();
        GameEntry.GetComponent<FogOfWarSetup>().OpenFogOfWarUI();
    }
}
```

## 📁 文件结构

```
GF_X/Assets/AAAGame/Scripts/
├── UTManagers/                     # 系统管理组件
│   ├── GeneralSetup.cs            # 通用系统
│   ├── CardSetup.cs               # 卡牌系统 Setup
│   ├── MinimapSetup.cs            # 小地图系统 Setup
│   └── FogOfWarSetup.cs           # 战争迷雾系统 Setup
│
├── Procedures/                     # 游戏流程
│   ├── PreloadProcedure.cs        # 预加载
│   ├── MenuProcedure.cs           # 菜单
│   ├── CardGameProcedure.cs       # 卡牌流程
│   ├── MinimapGameProcedure.cs    # 小地图流程
│   ├── FogOfWarGameProcedure.cs   # 战争迷雾流程
│   └── CompleteGameProcedure.cs   # 完整游戏流程（推荐）
│
├── Card/                           # 卡牌系统
│   ├── Controller/
│   ├── UI/
│   └── ...
│
├── MiniMap/                        # 小地图系统
│   ├── MinimapManager.cs
│   ├── MinimapUI.cs
│   ├── MinimapReportComponent.cs
│   └── FOG/                        # 战争迷雾系统
│       ├── FogOfWarManager.cs
│       ├── FogOfWarGrid.cs
│       └── ...
│
└── UI/                             # UI 组件
    ├── CardUIForm.cs
    ├── FogOfWarUIForm.cs
    └── Core/
        └── UIViews.cs
```

## 🎮 使用示例

### 卡牌系统

```csharp
// 在单位 Entity 中
public class SoldierEntity : EntityLogic
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        // 卡牌系统会自动管理单位生成
    }
}
```

### 小地图系统

```csharp
// 在单位 Entity 中
using AAAGame.MiniMap;

public class SoldierEntity : EntityLogic
{
    private MinimapReportComponent m_MinimapReport;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        m_MinimapReport = gameObject.AddComponent<MinimapReportComponent>();
        m_MinimapReport.Initialize(SideType.PlayerSide);
    }
    
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        m_MinimapReport?.Tick();
    }
}
```

### 战争迷雾系统

```csharp
// 在单位 Entity 中
using AAAGame.MiniMap.FOG;

public class SoldierEntity : EntityLogic
{
    private FogOfWarVisionComponent m_VisionComponent;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        m_VisionComponent = gameObject.AddComponent<FogOfWarVisionComponent>();
        // 组件会自动注册和更新视野
    }
}
```

### 完整示例（三个系统）

```csharp
using AAAGame.MiniMap;
using AAAGame.MiniMap.FOG;
using UnityGameFramework.Runtime;

public class SoldierEntity : EntityLogic
{
    private MinimapReportComponent m_MinimapReport;
    private FogOfWarVisionComponent m_VisionComponent;
    private FogOfWarUnitVisibility m_UnitVisibility;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        // 1. 小地图报告
        m_MinimapReport = gameObject.AddComponent<MinimapReportComponent>();
        m_MinimapReport.Initialize(SideType.PlayerSide);
        
        // 2. 战争迷雾视野
        m_VisionComponent = gameObject.AddComponent<FogOfWarVisionComponent>();
        
        // 3. 单位可见性控制
        m_UnitVisibility = gameObject.AddComponent<FogOfWarUnitVisibility>();
        
        Log.Info("[Soldier] 所有系统组件已添加");
    }
    
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        
        // 更新小地图位置
        m_MinimapReport?.Tick();
        
        // 战争迷雾组件会自动更新
    }
}
```

## 🔧 配置清单

### GameEntry 组件清单

- [x] GeneralSetup
- [x] CardSetup
- [x] MinimapSetup
- [x] FogOfWarSetup
- [x] MinimapManager
- [x] InputManager

### 场景对象清单

- [x] FogOfWarManager (独立空对象)

### UI 预制体清单

- [x] CardUIForm.prefab
- [x] MinimapUI.prefab
- [x] FogOfWarUI.prefab

### Procedure 配置

- [x] CompleteGameProcedure 已创建
- [ ] 在项目配置中添加 Procedure

## 📚 文档索引

### 卡牌系统
- `GF_X/Assets/AAAGame/Scripts/Card/GF_X框架集成完成报告.md`
- `GF_X/Assets/AAAGame/Scripts/Card/快速配置指南.md`

### 小地图系统
- `GF_X/Assets/AAAGame/Scripts/MiniMap/Procedure集成完成报告.md`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/快速开始_Procedure版.md`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/Procedure集成指南.md`

### 战争迷雾系统
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/Procedure集成完成报告.md`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/快速开始_Procedure版.md`
- `GF_X/Assets/AAAGame/Scripts/MiniMap/FOG/Procedure集成指南.md`

### 总览文档
- `GF_X/Assets/AAAGame/Scripts/系统集成总览_Procedure版.md` (本文档)

## 🎯 推荐配置

### 开发阶段

使用 `CompleteGameProcedure`，方便测试所有功能：

```json
{
  "Procedures": [
    "PreloadProcedure",
    "MenuProcedure",
    "CompleteGameProcedure"
  ]
}
```

### 生产阶段

根据实际需求选择：

**完整游戏**:
```json
{
  "Procedures": ["PreloadProcedure", "MenuProcedure", "CompleteGameProcedure"]
}
```

**简化版本**:
```json
{
  "Procedures": ["PreloadProcedure", "MenuProcedure", "CardGameProcedure"]
}
```

## ⚡ 性能建议

### 卡牌系统
- 使用对象池管理卡牌实例
- 限制同时存在的卡牌数量
- 优化卡牌放置检测

### 小地图系统
- 调整更新频率（默认每帧）
- 限制显示的单位数量
- 使用简单的图标

### 战争迷雾系统
- 调整网格大小（默认 100x100）
- 增加更新间隔（默认 0.1 秒）
- 禁用颜色过渡（提高性能）

## 🐛 常见问题

### Q: 系统不加载？

**检查清单**:
- [ ] GameEntry 上是否有对应的 Setup 组件？
- [ ] Procedure 是否正确配置？
- [ ] UI 预制体是否存在？
- [ ] UIViews.cs 是否包含对应枚举？

### Q: 系统冲突？

**解决方案**:
- 使用 `CompleteGameProcedure` 统一管理
- 确保初始化顺序正确
- 检查控制台日志

### Q: 性能问题？

**优化建议**:
- 调整各系统的更新频率
- 减少同时活跃的对象数量
- 使用对象池
- 禁用不需要的功能

## 🎉 总结

三大核心系统已完全集成到 GF_X 框架：

✅ **统一架构** - 所有系统使用相同的 Setup + Procedure 模式
✅ **运行时加载** - UI 动态加载，无需场景配置
✅ **系统协同** - CompleteGameProcedure 统一管理
✅ **易于扩展** - 可以轻松添加新系统
✅ **文档完善** - 每个系统都有详细文档

现在可以开始使用 `CompleteGameProcedure` 来运行完整的游戏系统！

---

**版本**: v1.0  
**日期**: 2025-04-15  
**作者**: Kiro AI Assistant  
**状态**: ✅ 已完成
