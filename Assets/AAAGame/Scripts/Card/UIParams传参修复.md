# UIParams 传参修复

## 问题描述

### 错误 1: 编译错误
```
error CS1061: 'UIParams' does not contain a definition for 'UserData'
```

### 错误 2: 运行时错误
```
CardSystemController is null.
```

## 问题原因

`UIParams` 不是直接有 `UserData` 属性，而是继承自 `RefParams`，需要使用 `Set()` 和 `Get()` 方法来传递自定义数据。

## 解决方案

### 修改前（错误的方式）

#### CardGameProcedure.cs
```csharp
UIParams uiParams = UIParams.Create();
uiParams.UserData = m_CardSystemController;  // ❌ 错误：UserData 属性不存在
```

#### CardUIForm.cs
```csharp
m_CardSystemController = userData as CardSystemController;  // ❌ 错误：无法直接转换
```

### 修改后（正确的方式）

#### CardGameProcedure.cs
```csharp
UIParams uiParams = UIParams.Create();
uiParams.Set("CardSystemController", m_CardSystemController);  // ✅ 正确：使用 Set 方法
```

#### CardUIForm.cs
```csharp
UIParams uiParams = userData as UIParams;
if (uiParams != null)
{
    m_CardSystemController = uiParams.Get("CardSystemController") as CardSystemController;  // ✅ 正确：使用 Get 方法
}
```

## RefParams 的使用方法

### 基本概念

`RefParams` 是 GF_X 框架中用于传递参数的引用类型基类，避免频繁 new 对象。

### API 说明

#### 1. Set 方法 - 设置参数

```csharp
// 设置任意类型的对象
public void Set(string key, object value)

// 设置 Variable 类型
public void Set<T>(string key, T value) where T : Variable
```

**示例**：
```csharp
UIParams uiParams = UIParams.Create();
uiParams.Set("CardSystemController", m_CardSystemController);
uiParams.Set("Level", 5);
uiParams.Set("PlayerName", "Player1");
```

#### 2. Get 方法 - 获取参数

```csharp
// 获取任意类型的对象
public object Get(string key)

// 获取 Variable 类型
public T Get<T>(string key, T defaultValue = null) where T : Variable
```

**示例**：
```csharp
var controller = uiParams.Get("CardSystemController") as CardSystemController;
var level = (int)uiParams.Get("Level");
var playerName = (string)uiParams.Get("PlayerName");
```

#### 3. TryGet 方法 - 安全获取参数

```csharp
public bool TryGet<T>(string key, out T value) where T : Variable
```

**示例**：
```csharp
if (uiParams.TryGet("CardSystemController", out var controller))
{
    // 使用 controller
}
```

## 完整示例

### 场景 1: 传递单个对象

```csharp
// 发送方
UIParams uiParams = UIParams.Create();
uiParams.Set("Controller", myController);
GF.UI.OpenUIForm(UIViews.MyForm, uiParams);

// 接收方
protected override void OnOpen(object userData)
{
    base.OnOpen(userData);
    
    UIParams uiParams = userData as UIParams;
    if (uiParams != null)
    {
        var controller = uiParams.Get("Controller") as MyController;
        if (controller != null)
        {
            // 使用 controller
        }
    }
}
```

### 场景 2: 传递多个参数

```csharp
// 发送方
UIParams uiParams = UIParams.Create();
uiParams.Set("PlayerId", 123);
uiParams.Set("PlayerName", "Alice");
uiParams.Set("Level", 10);
uiParams.Set("Controller", myController);
GF.UI.OpenUIForm(UIViews.PlayerInfo, uiParams);

// 接收方
protected override void OnOpen(object userData)
{
    base.OnOpen(userData);
    
    UIParams uiParams = userData as UIParams;
    if (uiParams != null)
    {
        int playerId = (int)uiParams.Get("PlayerId");
        string playerName = (string)uiParams.Get("PlayerName");
        int level = (int)uiParams.Get("Level");
        var controller = uiParams.Get("Controller") as MyController;
        
        // 使用这些参数
    }
}
```

### 场景 3: 使用 UIParams 的内置属性

```csharp
UIParams uiParams = UIParams.Create(
    allowEscape: true,  // 允许 ESC 关闭
    sortOrder: 100      // 显示层级
);

// 设置回调
uiParams.OpenCallback = (form) => 
{
    Log.Info("UI 已打开");
};

uiParams.CloseCallback = (form) => 
{
    Log.Info("UI 已关闭");
};

// 设置自定义数据
uiParams.Set("Data", myData);

GF.UI.OpenUIForm(UIViews.MyForm, uiParams);
```

## 注意事项

### 1. 类型转换

使用 `Get()` 方法获取的是 `object` 类型，需要手动转换：

```csharp
// ✅ 正确
var controller = uiParams.Get("Controller") as MyController;

// ❌ 错误
MyController controller = uiParams.Get("Controller");  // 编译错误
```

### 2. 空值检查

始终检查获取的值是否为 null：

```csharp
UIParams uiParams = userData as UIParams;
if (uiParams != null)
{
    var controller = uiParams.Get("Controller") as MyController;
    if (controller != null)
    {
        // 安全使用 controller
    }
    else
    {
        Log.Error("Controller is null");
    }
}
```

### 3. Key 名称

使用有意义的 Key 名称，建议使用常量：

```csharp
// ✅ 推荐
public static class UIParamKeys
{
    public const string CardSystemController = "CardSystemController";
    public const string PlayerId = "PlayerId";
    public const string Level = "Level";
}

uiParams.Set(UIParamKeys.CardSystemController, controller);
```

### 4. 内存管理

`RefParams` 使用对象池管理，会自动回收：

```csharp
// 不需要手动释放
// UIParams 在 UI 关闭时会自动回收到对象池
```

## 修复验证

### 编译检查

✅ 无编译错误
✅ 无警告

### 运行时检查

1. 启动游戏
2. 进入卡牌游戏场景
3. 检查 Console：
   - ✅ 没有 "CardSystemController is null" 错误
   - ✅ CardUIForm 正常打开
   - ✅ 卡牌系统正常工作

## 相关文件

### 修改的文件

1. `GF_X/Assets/AAAGame/Scripts/Procedures/CardGameProcedure.cs`
   - 修改 `OpenCardUI()` 方法
   - 使用 `uiParams.Set()` 传递参数

2. `GF_X/Assets/AAAGame/Scripts/Card/UI/CardUIForm.cs`
   - 修改 `OnOpen()` 方法
   - 使用 `uiParams.Get()` 获取参数

### 参考文件

1. `GF_X/Assets/AAAGame/Scripts/UI/Core/UIParams.cs`
   - UIParams 类定义

2. `GF_X/Assets/AAAGame/Scripts/Common/RefParams.cs`
   - RefParams 基类定义

## 总结

### 关键点

1. **不要使用 `UserData` 属性** - UIParams 没有这个属性
2. **使用 `Set()` 和 `Get()` 方法** - 这是正确的传参方式
3. **类型转换** - Get() 返回 object，需要手动转换
4. **空值检查** - 始终检查 null

### 修复状态

✅ 编译错误已修复
✅ 运行时错误已修复
✅ 参数传递正常工作

---

**修复时间**: 2025-03-31
**问题**: UIParams 传参错误
**状态**: ✅ 已修复
