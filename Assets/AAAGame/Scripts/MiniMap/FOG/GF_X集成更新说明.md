# 战争迷雾系统 - GF_X 集成更新说明

## 📢 更新内容

根据你的需求，我已经将战争迷雾系统修改为符合 **GF_X 框架** 的 UI 加载方式，实现运行时自动加载和配置。

---

## 🆕 新增文件

### 1. FogOfWarUIForm.cs
**路径**：`Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarUIForm.cs`

**功能**：
- 继承 `UIFormBase`，符合 GF_X UI 规范
- 自动管理战争迷雾渲染
- 支持 OnOpen/OnClose 生命周期
- 自动创建和更新纹理

**特点**：
- ✅ 符合 GF_X 框架规范
- ✅ 自动资源管理
- ✅ 支持对象池回收
- ✅ 完整的生命周期管理

---

### 2. FogOfWarAutoLoader.cs
**路径**：`Assets/AAAGame/Scripts/MiniMap/FOG/FogOfWarAutoLoader.cs`

**功能**：
- 在 Game 场景加载时自动打开战争迷雾 UI
- 支持延迟加载
- 支持手动加载/关闭
- 提供 ContextMenu 测试功能

**特点**：
- ✅ 自动加载 UI
- ✅ 可配置延迟时间
- ✅ 支持手动控制
- ✅ 易于调试

---

### 3. GF_X框架集成指南.md
**路径**：`Assets/AAAGame/Scripts/MiniMap/FOG/GF_X框架集成指南.md`

**内容**：
- 3步快速集成指南
- 详细配置说明
- 常见问题解答
- 与旧版本的区别

---

### 4. UI预制体创建指南.md
**路径**：`Assets/AAAGame/Scripts/MiniMap/FOG/UI预制体创建指南.md`

**内容**：
- 预制体创建步骤
- 详细配置说明
- 验证清单
- 高级配置选项

---

## 🔄 与旧版本的区别

### 旧版本（手动配置）

```
❌ 需要在场景中手动创建 Canvas
❌ 需要手动添加 RawImage
❌ 需要手动添加 FogOfWarRenderer 组件
❌ 需要手动配置引用
❌ 场景切换时需要重新配置
```

### 新版本（自动加载）

```
✅ 运行时自动加载 UI
✅ 自动创建纹理
✅ 自动配置引用
✅ 符合 GF_X UI 规范
✅ 场景切换自动管理
✅ 支持对象池回收
```

---

## 🚀 快速开始

### 3步完成集成

#### 步骤 1：添加 FogOfWarManager
```
Game Framework (GameObject)
└── FogOfWarManager (Component)
```

#### 步骤 2：创建 UI 预制体
```
Assets/AAAGame/Prefabs/UI/FogOfWarUIForm.prefab
├── Canvas
├── FogOfWarUIForm (Script)
└── FogOverlay (RawImage)
```

#### 步骤 3：添加自动加载器
```
Game Framework (GameObject)
└── FogOfWarAutoLoader (Component)
    ├── Fog UI Form Id: 10001
    ├── Fog UI Form Asset Name: FogOfWarUIForm
    └── Auto Load On Start: ✓
```

**详细步骤请查看**：[GF_X框架集成指南.md](./GF_X框架集成指南.md)

---

## 📊 架构对比

### 旧架构

```
场景中的 Canvas
└── RawImage
    └── FogOfWarRenderer (Component)
        └── 手动引用 FogOfWarManager
```

### 新架构

```
运行时加载
└── FogOfWarUIForm (UIFormBase)
    ├── 自动查找 FogOfWarManager
    ├── 自动创建纹理
    └── 自动管理生命周期
```

---

## 🎯 核心改进

### 1. 符合 GF_X 规范

```csharp
// 继承 UIFormBase
public partial class FogOfWarUIForm : UIFormBase
{
    protected override void OnInit(object userData) { }
    protected override void OnOpen(object userData) { }
    protected override void OnClose(bool isShutdown, object userData) { }
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds) { }
    protected override void OnRecycle() { }
}
```

### 2. 自动资源管理

```csharp
// OnOpen 时创建资源
protected override void OnOpen(object userData)
{
    InitializeTexture();
    fogManager.OnVisionUpdated += OnVisionUpdated;
}

// OnClose 时清理资源
protected override void OnClose(bool isShutdown, object userData)
{
    fogManager.OnVisionUpdated -= OnVisionUpdated;
    Destroy(fogTexture);
}
```

### 3. 运行时加载

```csharp
// 自动加载器
public class FogOfWarAutoLoader : MonoBehaviour
{
    private void Start()
    {
        if (autoLoadOnStart)
        {
            Invoke(nameof(LoadFogOfWarUI), delayLoadTime);
        }
    }
    
    public void LoadFogOfWarUI()
    {
        GFBuiltin.UI.OpenUIForm(fogUIFormId, fogUIFormAssetName, fogUIGroupName, false, null);
    }
}
```

---

## 📁 文件清单

### 新增文件（4个）

| 文件名 | 类型 | 大小 | 说明 |
|--------|------|------|------|
| FogOfWarUIForm.cs | 代码 | ~8KB | UI 界面脚本 |
| FogOfWarAutoLoader.cs | 代码 | ~3KB | 自动加载器 |
| GF_X框架集成指南.md | 文档 | ~8KB | 集成指南 |
| UI预制体创建指南.md | 文档 | ~6KB | 预制体指南 |

### 保留文件（原有）

- FogOfWarManager.cs
- FogOfWarGrid.cs
- FogOfWarData.cs
- FogOfWarVisionComponent.cs
- FogOfWarUnitVisibility.cs
- 所有文档文件

### 可选删除（旧版本）

- FogOfWarRenderer.cs（已被 FogOfWarUIForm 替代）
- MinimapFogIntegration.cs（如果不需要小地图集成）

---

## ✅ 集成检查清单

### 场景配置
- [ ] Game Framework 对象存在
- [ ] FogOfWarManager 已添加到 Game Framework
- [ ] FogOfWarAutoLoader 已添加到 Game Framework
- [ ] 参数已配置

### UI 预制体
- [ ] FogOfWarUIForm.prefab 已创建
- [ ] 路径：Assets/AAAGame/Prefabs/UI/
- [ ] Canvas 和 RawImage 已配置
- [ ] FogOfWarUIForm 脚本已添加
- [ ] 引用已设置

### 代码集成
- [ ] SoldierEntity 已添加视野组件
- [ ] 敌方单位已添加可见性控制
- [ ] 编译无错误

### 测试
- [ ] 运行游戏，UI 自动加载
- [ ] Console 输出正确日志
- [ ] 迷雾正确显示
- [ ] 单位视野正常工作

---

## 🎮 使用示例

### 自动加载（推荐）

```csharp
// 在 Game 场景中添加 FogOfWarAutoLoader
// 运行游戏时自动加载
```

### 手动加载

```csharp
// 获取自动加载器
var autoLoader = FindObjectOfType<FogOfWarAutoLoader>();

// 手动加载
autoLoader.LoadFogOfWarUI();

// 手动关闭
autoLoader.CloseFogOfWarUI();
```

### 代码加载

```csharp
// 直接使用 GF_X UI 系统
GFBuiltin.UI.OpenUIForm(10001, "FogOfWarUIForm", "Default", false, null);

// 关闭
GFBuiltin.UI.CloseUIForm(10001);
```

---

## 🐛 常见问题

### Q1: UI 不显示？

**检查：**
- FogOfWarManager 是否在场景中？
- UI 预制体是否正确创建？
- UI ID 是否唯一？
- Console 是否有错误？

**解决：**
```
1. 检查 Console 输出
2. 确认 FogOfWarManager 已添加
3. 确认 UI 预制体路径正确
4. 尝试手动加载测试
```

---

### Q2: 迷雾不更新？

**检查：**
- 单位是否添加了 FogOfWarVisionComponent？
- FogOfWarManager 的 Update Interval 是否合理？

**解决：**
```csharp
// 检查视野数量
var fogManager = GameEntry.GetComponent<FogOfWarManager>();
Debug.Log($"Vision count: {fogManager.GetVisionCount()}");
```

---

### Q3: 场景切换后迷雾消失？

**原因：**
- UI 可能被关闭了

**解决：**
```csharp
// 在新场景中重新加载
var autoLoader = FindObjectOfType<FogOfWarAutoLoader>();
if (autoLoader != null)
{
    autoLoader.LoadFogOfWarUI();
}
```

---

## 📚 文档导航

### 新手入门
👉 [GF_X框架集成指南.md](./GF_X框架集成指南.md) - 3步快速集成

### 预制体创建
👉 [UI预制体创建指南.md](./UI预制体创建指南.md) - 详细创建步骤

### 详细使用
👉 [战争迷雾系统使用指南.md](./战争迷雾系统使用指南.md) - 完整功能说明

### 技术深入
👉 [技术实现说明.md](./技术实现说明.md) - 算法和性能优化

---

## 🎉 总结

### 更新内容

✅ **新增 FogOfWarUIForm**：符合 GF_X UI 规范的界面脚本
✅ **新增 FogOfWarAutoLoader**：自动加载器，运行时加载 UI
✅ **新增集成指南**：详细的 GF_X 框架集成文档
✅ **新增预制体指南**：UI 预制体创建步骤

### 核心改进

- 符合 GF_X 框架规范
- 运行时自动加载
- 自动资源管理
- 完整生命周期管理
- 易于集成和使用

### 适用场景

- RTS 游戏
- MOBA 游戏
- 生存游戏
- 回合制策略游戏

---

**GF_X 集成更新完成！现在你可以在运行时自动加载战争迷雾 UI 了！** 🎮✨

**下一步**：
1. 按照 [GF_X框架集成指南.md](./GF_X框架集成指南.md) 进行集成
2. 创建 UI 预制体
3. 运行游戏测试
4. 享受自动加载的便利！
