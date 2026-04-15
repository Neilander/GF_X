# 战争迷雾系统 - GF_X 框架集成指南

## 📋 概述

本指南说明如何将战争迷雾系统集成到 GF_X 框架中，实现运行时自动加载和配置。

---

## 🚀 快速集成（3步完成）

### 步骤 1：添加 FogOfWarManager 到 Game Framework（1分钟）

1. 在场景中找到 **Game Framework** 对象
2. 添加 `FogOfWarManager` 组件
3. 配置参数（使用默认值即可）：

```
Grid Width: 100
Grid Height: 100
World Min X: -50
World Max X: 50
World Min Z: -50
World Max Z: 50
Update Interval: 0.1
```

4. 保存场景

---

### 步骤 2：创建 FogOfWarUIForm 预制体（2分钟）

#### 2.1 创建 UI 预制体

1. 在 `Assets/AAAGame/Prefabs/UI/` 文件夹下创建新预制体
2. 命名为：`FogOfWarUIForm`

#### 2.2 配置预制体结构

```
FogOfWarUIForm (GameObject)
├── Canvas (Component)
│   ├── Render Mode: Screen Space - Overlay
│   └── Canvas Scaler (Component)
│       └── UI Scale Mode: Scale With Screen Size
│
├── FogOfWarUIForm (Component) ← 添加脚本
│
└── FogOverlay (RawImage)
    ├── Anchor: Stretch (全屏)
    ├── Left/Right/Top/Bottom: 0
    └── Color: White (1, 1, 1, 1)
```

#### 2.3 配置 FogOfWarUIForm 组件

在 `FogOfWarUIForm` 组件中：
- 拖拽 `FogOverlay (RawImage)` 到 **Fog Raw Image** 字段
- Auto Update: ✓
- Filter Mode: Bilinear

#### 2.4 保存预制体

保存预制体到：`Assets/AAAGame/Prefabs/UI/FogOfWarUIForm.prefab`

---

### 步骤 3：添加自动加载器（1分钟）

#### 3.1 添加 FogOfWarAutoLoader

1. 在 Game 场景中找到 **Game Framework** 对象
2. 添加 `FogOfWarAutoLoader` 组件
3. 配置参数：

```
Fog UI Form Id: 10001          ← UI 的唯一 ID
Fog UI Form Asset Name: FogOfWarUIForm  ← 预制体名称
Fog UI Group Name: Default     ← UI 组名称
Auto Load On Start: ✓          ← 自动加载
Delay Load Time: 0.5           ← 延迟 0.5 秒加载
```

4. 保存场景

---

## ✅ 完成！测试效果

### 测试步骤

1. **运行游戏**
2. **观察效果**：
   - 游戏启动后 0.5 秒，战争迷雾 UI 自动加载
   - 屏幕被黑色迷雾覆盖
   - Console 输出：
     ```
     [FogAutoLoader] FogOfWarUI loaded: ID=10001, Asset=FogOfWarUIForm
     [FogOfWarUI] FogOfWarUIForm opened and ready
     [FogOfWarUI] Texture initialized: 100x100
     ```

3. **生成单位**：
   - 单位周围迷雾消失
   - 移动单位，迷雾跟随

---

## 📁 文件结构

```
Assets/AAAGame/
├── Scripts/MiniMap/FOG/
│   ├── FogOfWarUIForm.cs           ← 新增：UI 界面脚本
│   ├── FogOfWarAutoLoader.cs       ← 新增：自动加载器
│   ├── FogOfWarManager.cs          ← 已有：核心管理器
│   ├── FogOfWarVisionComponent.cs  ← 已有：单位视野组件
│   └── ...
│
└── Prefabs/UI/
    └── FogOfWarUIForm.prefab       ← 新增：UI 预制体
```

---

## 🎨 UI 预制体详细配置

### Canvas 设置

```
Canvas (Component)
├── Render Mode: Screen Space - Overlay
├── Pixel Perfect: false
└── Sort Order: 0
```

### Canvas Scaler 设置

```
Canvas Scaler (Component)
├── UI Scale Mode: Scale With Screen Size
├── Reference Resolution: 1920 x 1080
├── Screen Match Mode: Match Width Or Height
└── Match: 0.5
```

### RawImage 设置

```
FogOverlay (RawImage)
├── Rect Transform:
│   ├── Anchor: Stretch (Alt+Shift+右下角)
│   ├── Left: 0
│   ├── Right: 0
│   ├── Top: 0
│   └── Bottom: 0
│
├── Color: White (1, 1, 1, 1)
├── Material: None
└── Texture: (运行时自动设置)
```

---

## 🔧 高级配置

### 自定义 UI ID

如果 10001 已被占用，修改为其他 ID：

```csharp
// 在 FogOfWarAutoLoader 中
Fog UI Form Id: 10002  // 改为未使用的 ID
```

### 自定义加载时机

```csharp
// 禁用自动加载
Auto Load On Start: false

// 在代码中手动加载
var autoLoader = FindObjectOfType<FogOfWarAutoLoader>();
autoLoader.LoadFogOfWarUI();
```

### 自定义 UI 组

```csharp
// 如果要放在其他 UI 组
Fog UI Group Name: "Game"  // 或 "HUD"、"Overlay" 等
```

---

## 🎮 单位集成

### 在 SoldierEntity 中添加视野

```csharp
using AAAGame.MiniMap.FOG;

public class SoldierEntity : EntityLogic
{
    private FogOfWarVisionComponent visionComponent;
    
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        
        // 添加视野组件
        visionComponent = gameObject.AddComponent<FogOfWarVisionComponent>();
        visionComponent.SetPlayerMask(Side == SideType.PlayerSide ? 1 : 2);
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

---

## 🐛 常见问题

### Q1: UI 不显示？

**检查清单：**
- [ ] FogOfWarManager 是否在场景中？
- [ ] FogOfWarAutoLoader 是否在场景中？
- [ ] UI 预制体是否正确创建？
- [ ] UI ID 是否唯一（未被占用）？
- [ ] Console 是否有错误？

**解决方案：**
```
1. 检查 Console 输出
2. 确认 FogOfWarManager 已添加到 Game Framework
3. 确认 UI 预制体路径正确
4. 尝试手动加载：autoLoader.LoadFogOfWarUI()
```

---

### Q2: 迷雾不更新？

**检查清单：**
- [ ] 单位是否添加了 FogOfWarVisionComponent？
- [ ] FogOfWarManager 的 Update Interval 是否合理？
- [ ] 单位是否在世界范围内？

**解决方案：**
```csharp
// 检查视野数量
var fogManager = GameEntry.GetComponent<FogOfWarManager>();
Debug.Log($"Vision count: {fogManager.GetVisionCount()}");
// 应该 > 0
```

---

### Q3: 性能问题？

**优化建议：**
```
移动设备：
- Grid Width/Height: 50
- Update Interval: 0.2
- Enable Terrain Blocking: false
- Enable Easing: false

PC：
- Grid Width/Height: 100
- Update Interval: 0.1
- Enable Terrain Blocking: true
- Enable Easing: true
```

---

## 📊 与旧版本的区别

### 旧版本（手动配置）

```
需要手动：
1. 在场景中创建 Canvas
2. 添加 RawImage
3. 添加 FogOfWarRenderer 组件
4. 手动配置引用
```

### 新版本（自动加载）

```
自动完成：
1. 运行时自动加载 UI
2. 自动创建纹理
3. 自动配置引用
4. 符合 GF_X UI 规范
```

---

## ✅ 集成检查清单

### 场景配置
- [ ] Game Framework 对象存在
- [ ] FogOfWarManager 已添加
- [ ] FogOfWarAutoLoader 已添加
- [ ] 参数已配置

### UI 预制体
- [ ] FogOfWarUIForm.prefab 已创建
- [ ] Canvas 和 RawImage 已配置
- [ ] FogOfWarUIForm 组件已添加
- [ ] 引用已设置

### 代码集成
- [ ] SoldierEntity 已添加视野组件
- [ ] 敌方单位已添加可见性控制
- [ ] 编译无错误

### 测试
- [ ] 运行游戏，UI 自动加载
- [ ] 迷雾正确显示
- [ ] 单位视野正常工作
- [ ] 性能正常

---

## 🎉 完成！

现在你的战争迷雾系统已经完全集成到 GF_X 框架中，运行时自动加载和配置！

### 下一步

1. 🎨 调整迷雾颜色和效果
2. ⚡ 优化性能参数
3. 🔧 添加特殊视野功能
4. 📱 移动设备测试

---

## 📞 需要帮助？

查看其他文档：
- [战争迷雾系统使用指南.md](./战争迷雾系统使用指南.md)
- [快速配置指南.md](./快速配置指南.md)
- [技术实现说明.md](./技术实现说明.md)

---

**享受你的战争迷雾系统！** 🎮✨
