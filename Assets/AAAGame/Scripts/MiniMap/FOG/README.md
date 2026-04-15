# 战争迷雾系统 (Fog of War System)

## 📖 简介

基于 **GF_X 框架** 的高性能战争迷雾系统，适用于 RTS、MOBA 等需要视野机制的游戏。

### ✨ 核心特性

- ✅ **三种迷雾状态**：Hidden（未探索）、Explored（已探索）、Visible（可见）
- ✅ **多玩家支持**：使用位掩码技术，支持联盟和观战模式
- ✅ **地形遮挡**：高地可以遮挡低地视野
- ✅ **平滑过渡**：颜色渐变效果，视觉体验流畅
- ✅ **高性能**：优化算法，支持移动设备 60FPS
- ✅ **GF_X 集成**：完全符合 GF_X 框架规范
- ✅ **小地图集成**：可直接渲染到小地图上

---

## 📁 文件结构

```
FOG/
├── FogOfWarData.cs                 # 数据结构定义
├── FogOfWarGrid.cs                 # 网格数据管理
├── FogOfWarManager.cs              # 核心管理器（GameFrameworkComponent）
├── FogOfWarRenderer.cs             # 全屏渲染器
├── FogOfWarVisionComponent.cs      # 单位视野组件
├── FogOfWarUnitVisibility.cs       # 单位可见性控制
├── MinimapFogIntegration.cs        # 小地图集成
├── README.md                       # 本文件
├── 战争迷雾系统使用指南.md          # 详细使用指南
├── 快速配置指南.md                  # 5分钟快速配置
└── 技术实现说明.md                  # 技术细节和算法
```

---

## 🚀 快速开始

### 1. 添加管理器（1分钟）

在场景中添加 `FogOfWarManager` 组件：

```
Game Framework (GameObject)
└── FogOfWarManager (Component)
    ├── Grid Width: 100
    ├── Grid Height: 100
    ├── World Min X/Z: -50
    ├── World Max X/Z: 50
    └── Update Interval: 0.1
```

### 2. 创建迷雾 UI（1分钟）

在 Canvas 下创建 `RawImage` + `FogOfWarRenderer`：

```
Canvas
└── FogOfWarOverlay (RawImage)
    └── FogOfWarRenderer (Component)
        └── Fog Manager: [拖拽 FogOfWarManager]
```

### 3. 为单位添加视野（1分钟）

在单位预制体上添加 `FogOfWarVisionComponent`：

```
Soldier (Prefab)
└── FogOfWarVisionComponent
    ├── Player Mask: 1
    ├── Vision Range: 10
    └── Auto Register: ✓
```

### 4. 隐藏敌方单位（1分钟）

在敌方单位上添加 `FogOfWarUnitVisibility`：

```
Enemy (Prefab)
└── FogOfWarUnitVisibility
    ├── Owner Player Mask: 2
    ├── Check Player Mask: 1
    ├── Hide When Not Visible: ✓
    └── Hide Method: Layer
```

**完成！** 🎉

---

## 📚 文档导航

### 新手入门
👉 [快速配置指南.md](./快速配置指南.md) - 5分钟快速配置

### 详细使用
👉 [战争迷雾系统使用指南.md](./战争迷雾系统使用指南.md) - 完整功能说明

### 技术深入
👉 [技术实现说明.md](./技术实现说明.md) - 算法和性能优化

---

## 🎮 使用示例

### 代码集成

```csharp
using AAAGame.MiniMap.FOG;

public class SoldierEntity : EntityLogic
{
    private FogOfWarVisionComponent visionComponent;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        // 添加视野
        visionComponent = gameObject.AddComponent<FogOfWarVisionComponent>();
        visionComponent.SetPlayerMask(1); // 玩家0
        visionComponent.SetVisionRange(10f);
        visionComponent.RegisterVision();
    }
    
    protected override void OnHide(bool isShutdown, object userData)
    {
        base.OnHide(isShutdown, userData);
        
        // 注销视野
        if (visionComponent != null)
        {
            visionComponent.UnregisterVision();
        }
    }
}
```

### 手动控制

```csharp
// 获取管理器
var fogManager = GameEntry.GetComponent<FogOfWarManager>();

// 注册视野
int visionId = fogManager.RegisterVision(
    playerMask: 1,
    visionRange: 10f,
    worldPosition: transform.position
);

// 更新视野
fogManager.UpdateVision(visionId, newPosition);

// 注销视野
fogManager.UnregisterVision(visionId);

// 查询可见性
bool isVisible = fogManager.IsPositionVisible(position, playerMask: 1);
```

---

## ⚙️ 核心组件

### FogOfWarManager
全局管理器，继承 `GameFrameworkComponent`

**功能**：
- 管理所有单位视野
- 计算迷雾状态
- 提供查询接口

### FogOfWarVisionComponent
单位视野组件，挂在提供视野的单位上

**功能**：
- 自动注册/注销视野
- 自动更新位置
- 支持地形高度

### FogOfWarRenderer
迷雾渲染器，负责将迷雾绘制到屏幕

**功能**：
- 实时渲染迷雾纹理
- 颜色平滑过渡
- 支持多玩家切换

### FogOfWarUnitVisibility
单位可见性控制，根据迷雾自动显示/隐藏单位

**功能**：
- 自动检测可见性
- 三种隐藏方式（SetActive/Layer/Renderer）
- 性能优化

---

## 🎨 效果展示

### 迷雾状态

| 状态 | 颜色 | 说明 |
|------|------|------|
| **Hidden** | 黑色（不透明） | 完全未探索的区域 |
| **Explored** | 灰色（半透明） | 已探索但当前不可见 |
| **Visible** | 透明 | 当前可见区域 |

### 视野效果

- 🔵 **己方单位**：周围有圆形视野
- 🏰 **建筑**：提供更大的视野范围
- 🗼 **侦察塔**：固定位置的视野点
- 👁️ **联盟**：队友视野共享

---

## 📊 性能参考

| 平台 | 网格大小 | 单位数 | FPS |
|------|---------|--------|-----|
| PC | 100x100 | 50 | 60 |
| PC | 200x200 | 100 | 60 |
| 移动 | 50x50 | 20 | 60 |
| 移动 | 100x100 | 50 | 50-60 |

**优化建议**：
- 移动设备：50x50 网格，0.2秒更新
- PC：100-200 网格，0.1秒更新

---

## 🔧 配置参数

### 网格设置
```
Grid Width/Height: 100      # 网格大小
World Min/Max X/Z: ±50      # 世界范围
Update Interval: 0.1        # 更新间隔（秒）
```

### 渲染设置
```
Enable Blur: true           # 模糊效果
Enable Easing: true         # 颜色过渡
Easing Speed: 5             # 过渡速度
```

### 地形遮挡
```
Enable Terrain Blocking: true    # 启用遮挡
Height Block Threshold: 2        # 高度阈值
```

---

## 🐛 常见问题

### Q: 迷雾不显示？
**A:** 检查 FogOfWarManager 是否在场景中，FogOfWarRenderer 是否正确配置。

### Q: 单位不提供视野？
**A:** 确保添加了 FogOfWarVisionComponent 并勾选 Auto Register。

### Q: 敌方单位不隐藏？
**A:** 添加 FogOfWarUnitVisibility 组件，设置正确的 Player Mask。

### Q: 性能问题？
**A:** 减小网格大小，增加更新间隔，禁用地形遮挡和模糊效果。

详细解决方案请查看 [快速配置指南.md](./快速配置指南.md)

---

## 🌟 高级功能

### 多玩家联盟

```csharp
// 玩家0和玩家1组成联盟
int alliance = 1 | 2;  // 0011 = 3

// 注册联盟视野
fogManager.RegisterVision(alliance, 10f, position);

// 检查联盟视野
bool visible = fogManager.IsPositionVisible(position, alliance);
```

### 观战模式

```csharp
// 切换到玩家1视角
fogRenderer.SetPlayerMask(2);

// 观战模式（看到所有玩家）
fogRenderer.SetPlayerMask(1 | 2 | 4 | 8);  // 所有玩家
```

### 小地图集成

```csharp
// 在小地图上显示迷雾
MinimapContainer
└── MinimapFog (RawImage)
    └── MinimapFogIntegration
        ├── Fog Manager: [FogOfWarManager]
        ├── Minimap Manager: [MinimapManager]
        └── Sync With Minimap Bounds: ✓
```

---

## 📖 技术参考

### 算法
- **Bresenham 直线算法**：视野遮挡检测
- **圆形填充算法**：视野范围计算
- **位掩码技术**：多玩家支持

### 参考资料
- [Implementing Fog of War for RTS games in Unity 2/2](https://blog.gemserk.com/2018/11/20/implementing-fog-of-war-for-rts-games-in-unity-2-2/)
- [Gemserk.Rts.Fog GitHub](https://github.com/gemserk/Gemserk.Rts.Fog)

### 游戏参考
- 星际争霸2
- 英雄联盟
- 帝国时代4

---

## 📝 更新日志

### v1.0.0 (2025-04-13)
- ✅ 初始版本发布
- ✅ 基础战争迷雾系统
- ✅ 多玩家支持
- ✅ 地形遮挡
- ✅ 小地图集成
- ✅ 完整文档

---

## 📧 支持

如有问题或建议，请查看：
- [战争迷雾系统使用指南.md](./战争迷雾系统使用指南.md)
- [快速配置指南.md](./快速配置指南.md)
- [技术实现说明.md](./技术实现说明.md)

---

**享受你的战争迷雾系统！** 🎮✨
