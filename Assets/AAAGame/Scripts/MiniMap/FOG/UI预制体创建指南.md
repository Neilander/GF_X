# 战争迷雾 UI 预制体创建指南

## 📋 目标

创建一个符合 GF_X 规范的 `FogOfWarUIForm` 预制体，用于运行时自动加载战争迷雾渲染。

---

## 🎯 预制体结构

```
FogOfWarUIForm (Prefab)
│
├── Canvas (Component)
│   ├── Render Mode: Screen Space - Overlay
│   ├── Canvas Scaler
│   └── Graphic Raycaster
│
├── FogOfWarUIForm (Script Component)
│   └── Fog Raw Image: → FogOverlay
│
└── FogOverlay (GameObject)
    └── RawImage (Component)
        ├── Anchor: Stretch
        └── Color: White
```

---

## 📝 详细创建步骤

### 步骤 1：创建根对象（1分钟）

1. 在 Hierarchy 中右键 → **UI** → **Canvas**
2. 重命名为：`FogOfWarUIForm`
3. 选中 `FogOfWarUIForm`

---

### 步骤 2：配置 Canvas（1分钟）

#### Canvas 组件设置

```
Canvas (Component)
├── Render Mode: Screen Space - Overlay
├── Pixel Perfect: false
├── Sort Order: 0
└── Target Display: Display 1
```

#### Canvas Scaler 组件设置

```
Canvas Scaler (Component)
├── UI Scale Mode: Scale With Screen Size
├── Reference Resolution: 1920 x 1080
├── Screen Match Mode: Match Width Or Height
├── Match: 0.5
└── Reference Pixels Per Unit: 100
```

#### Graphic Raycaster 组件设置

```
Graphic Raycaster (Component)
├── Ignore Reversed Graphics: true
└── Blocking Objects: None
```

---

### 步骤 3：添加 FogOfWarUIForm 脚本（1分钟）

1. 选中 `FogOfWarUIForm` 对象
2. 点击 **Add Component**
3. 搜索：`FogOfWarUIForm`
4. 添加脚本

**脚本参数（暂时不设置，等创建 RawImage 后再设置）：**
```
Fog Raw Image: (待设置)
Auto Update: ✓
Filter Mode: Bilinear
```

---

### 步骤 4：创建 RawImage（2分钟）

#### 4.1 创建对象

1. 右键 `FogOfWarUIForm` → **UI** → **Raw Image**
2. 重命名为：`FogOverlay`

#### 4.2 配置 Rect Transform

1. 选中 `FogOverlay`
2. 在 Inspector 中找到 **Rect Transform**
3. 点击 **Anchor Presets**（左上角的小方块图标）
4. 按住 **Alt + Shift**，点击右下角的 **Stretch** 图标

**结果：**
```
Rect Transform
├── Anchor Min: (0, 0)
├── Anchor Max: (1, 1)
├── Pivot: (0.5, 0.5)
├── Left: 0
├── Right: 0
├── Top: 0
└── Bottom: 0
```

#### 4.3 配置 RawImage 组件

```
Raw Image (Component)
├── Texture: (None) ← 运行时自动设置
├── Color: White (1, 1, 1, 1)
├── Material: None
└── Raycast Target: false ← 不接收点击事件
```

---

### 步骤 5：设置脚本引用（1分钟）

1. 选中 `FogOfWarUIForm` 对象
2. 在 Inspector 中找到 `FogOfWarUIForm (Script)` 组件
3. 拖拽 `FogOverlay` 对象到 **Fog Raw Image** 字段

**最终配置：**
```
FogOfWarUIForm (Script)
├── Fog Raw Image: FogOverlay ← 已设置
├── Auto Update: ✓
└── Filter Mode: Bilinear
```

---

### 步骤 6：保存预制体（1分钟）

#### 6.1 创建预制体文件夹（如果不存在）

```
Assets/AAAGame/Prefabs/UI/
```

#### 6.2 保存预制体

1. 将 Hierarchy 中的 `FogOfWarUIForm` 拖拽到 `Assets/AAAGame/Prefabs/UI/` 文件夹
2. 确认预制体已创建：`FogOfWarUIForm.prefab`
3. 删除 Hierarchy 中的 `FogOfWarUIForm` 对象（预制体已保存）

---

## ✅ 验证预制体

### 检查清单

- [ ] 预制体文件存在：`Assets/AAAGame/Prefabs/UI/FogOfWarUIForm.prefab`
- [ ] Canvas 组件配置正确
- [ ] Canvas Scaler 配置正确
- [ ] FogOfWarUIForm 脚本已添加
- [ ] RawImage 已创建并配置为全屏
- [ ] Fog Raw Image 引用已设置

### 预制体层级结构

在 Project 窗口中双击 `FogOfWarUIForm.prefab`，检查结构：

```
FogOfWarUIForm
├── Canvas
├── Canvas Scaler
├── Graphic Raycaster
├── FogOfWarUIForm (Script)
│   └── Fog Raw Image: FogOverlay
└── FogOverlay
    └── Raw Image
```

---

## 🎨 可选：自定义配置

### 调整 UI 层级

如果需要调整迷雾在其他 UI 之上或之下：

```
Canvas (Component)
└── Sort Order: 0  ← 修改此值
    - 0: 默认层级
    - -1: 在其他 UI 之下
    - 1: 在其他 UI 之上
```

### 调整分辨率适配

```
Canvas Scaler (Component)
├── Reference Resolution: 1920 x 1080  ← 修改为目标分辨率
└── Match: 0.5  ← 调整宽高匹配
    - 0: 完全匹配宽度
    - 0.5: 宽高平衡
    - 1: 完全匹配高度
```

### 添加背景（可选）

如果需要在迷雾下方添加背景：

1. 在 `FogOverlay` 之前创建 `Background (Image)`
2. 设置为全屏
3. 设置颜色为黑色或其他颜色

---

## 🔧 高级配置

### 使用 Render Texture（高级）

如果需要更高质量的渲染：

1. 创建 Render Texture：
   - 右键 Project → **Create** → **Render Texture**
   - 命名：`FogOfWarRenderTexture`
   - Size: 1024 x 1024（或更高）

2. 修改 `FogOfWarUIForm.cs`：
   ```csharp
   // 使用 Render Texture 替代 Texture2D
   // （需要修改代码实现）
   ```

### 添加模糊效果（高级）

如果需要模糊效果：

1. 创建 Material：
   - 右键 Project → **Create** → **Material**
   - 命名：`FogOfWarBlurMaterial`
   - Shader: UI/Default 或自定义模糊 Shader

2. 在 RawImage 中设置：
   ```
   Raw Image (Component)
   └── Material: FogOfWarBlurMaterial
   ```

---

## 🐛 常见问题

### Q1: 预制体创建后找不到？

**解决方案：**
```
1. 检查路径：Assets/AAAGame/Prefabs/UI/
2. 在 Project 窗口中搜索：FogOfWarUIForm
3. 确认文件扩展名：.prefab
```

---

### Q2: RawImage 不是全屏？

**解决方案：**
```
1. 选中 FogOverlay
2. Rect Transform → Anchor Presets
3. 按住 Alt+Shift，点击右下角 Stretch
4. 确认 Left/Right/Top/Bottom 都是 0
```

---

### Q3: 脚本引用丢失？

**解决方案：**
```
1. 重新拖拽 FogOverlay 到 Fog Raw Image 字段
2. 保存预制体（Ctrl+S）
3. 检查 Console 是否有错误
```

---

### Q4: 运行时 UI 不显示？

**检查清单：**
- [ ] 预制体路径正确
- [ ] UI ID 未被占用
- [ ] FogOfWarAutoLoader 已配置
- [ ] FogOfWarManager 已添加

---

## 📊 预制体文件信息

### 文件位置
```
Assets/AAAGame/Prefabs/UI/FogOfWarUIForm.prefab
```

### 文件大小
```
约 5-10 KB
```

### 依赖项
```
- FogOfWarUIForm.cs
- UnityEngine.UI
- UnityGameFramework.Runtime
```

---

## 🎉 完成！

你已经成功创建了 `FogOfWarUIForm` 预制体！

### 下一步

1. 在场景中添加 `FogOfWarAutoLoader`
2. 配置 UI ID 和预制体名称
3. 运行游戏测试

---

## 📞 需要帮助？

查看其他文档：
- [GF_X框架集成指南.md](./GF_X框架集成指南.md)
- [战争迷雾系统使用指南.md](./战争迷雾系统使用指南.md)

---

**预制体创建完成！** 🎨✨
